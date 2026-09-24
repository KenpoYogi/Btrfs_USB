using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BtrfsUsbMounter.Core
{
    internal static class Kernel32
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint FileShareReadWrite = 0x00000003;
        internal const uint OpenExisting = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);
    }

    /// <summary>Reads sectors straight from \\.\PhysicalDriveN - no filesystem driver involved.</summary>
    public static class RawDisk
    {
        /// <summary>offset and length must be multiples of the sector size (4096 covers 512e and 4Kn).</summary>
        public static byte[] Read(int diskNumber, long offset, int length)
        {
            using (SafeFileHandle handle = Kernel32.CreateFile(@"\\.\PhysicalDrive" + diskNumber, Kernel32.GenericRead,
                       Kernel32.FileShareReadWrite, IntPtr.Zero, Kernel32.OpenExisting, 0, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (var fs = new FileStream(handle, FileAccess.Read, 1, false))
                {
                    fs.Seek(offset, SeekOrigin.Begin);
                    var buffer = new byte[length];
                    int total = 0;
                    while (total < length)
                    {
                        int n = fs.Read(buffer, total, length - total);
                        if (n <= 0) break;
                        total += n;
                    }
                    return buffer;
                }
            }
        }

        /// <summary>Returns the superblock, or null if the partition is not btrfs. readFailed = I/O error.</summary>
        public static Superblock TryReadSuperblock(int diskNumber, long partitionOffset, out bool readFailed)
        {
            byte[] buffer;
            try
            {
                buffer = Read(diskNumber, partitionOffset + Superblock.SuperblockOffset, 4096);
            }
            catch
            {
                readFailed = true;
                return null;
            }
            readFailed = false;
            return Superblock.Parse(buffer);
        }
    }

    /// <summary>
    /// Caches superblock reads per partition, so rescans don't wake a drive that has spun down.
    /// A cached null means "checked, not btrfs". Read errors are never cached.
    /// </summary>
    public sealed class SuperblockCache
    {
        private sealed class Entry
        {
            public Superblock Value;
        }

        private readonly ConcurrentDictionary<string, Entry> entries = new ConcurrentDictionary<string, Entry>();

        public Superblock GetOrRead(string volumeKey, int diskNumber, long offset)
        {
            string key = volumeKey + "|" + offset;
            Entry entry;
            if (entries.TryGetValue(key, out entry)) return entry.Value;
            bool readFailed;
            Superblock sb = RawDisk.TryReadSuperblock(diskNumber, offset, out readFailed);
            if (!readFailed) entries[key] = new Entry { Value = sb };
            return sb;
        }

        public void Clear()
        {
            entries.Clear();
        }

        public void InvalidateDisk(string diskKey)
        {
            string prefix = diskKey + "|";
            foreach (string k in entries.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            {
                Entry ignored;
                entries.TryRemove(k, out ignored);
            }
        }

        public void RetainDisks(ICollection<string> presentDiskKeys)
        {
            foreach (string k in entries.Keys.ToList())
            {
                int bar = k.IndexOf('|');
                string disk = bar >= 0 ? k.Substring(0, bar) : k;
                if (!presentDiskKeys.Contains(disk))
                {
                    Entry ignored;
                    entries.TryRemove(k, out ignored);
                }
            }
        }
    }

    /// <summary>Disk and partition enumeration through the Windows Storage Management API (what Get-Disk uses).</summary>
    public static class Storage
    {
        private const string Namespace = @"root\Microsoft\Windows\Storage";

        public static List<DiskRecord> GetDisks()
        {
            var disks = new List<DiskRecord>();
            using (var searcher = new ManagementObjectSearcher(Namespace,
                "SELECT Number, UniqueId, FriendlyName, SerialNumber, Size, BusType, IsBoot, IsSystem, PartitionStyle, IsOffline FROM MSFT_Disk"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject mo in results)
                {
                    using (mo)
                    {
                        disks.Add(new DiskRecord
                        {
                            Number = ToInt(mo["Number"]),
                            UniqueId = ToStr(mo["UniqueId"]),
                            FriendlyName = ToStr(mo["FriendlyName"]).Trim(),
                            SerialNumber = ToStr(mo["SerialNumber"]),
                            Size = ToLong(mo["Size"]),
                            BusType = ToInt(mo["BusType"]),
                            IsBoot = ToBool(mo["IsBoot"]),
                            IsSystem = ToBool(mo["IsSystem"]),
                            PartitionStyle = ToInt(mo["PartitionStyle"]),
                            IsOffline = ToBool(mo["IsOffline"])
                        });
                    }
                }
            }
            return disks.OrderBy(d => d.Number).ToList();
        }

        public static List<PartitionRecord> GetPartitions(int diskNumber)
        {
            var parts = new List<PartitionRecord>();
            using (var searcher = new ManagementObjectSearcher(Namespace,
                "SELECT PartitionNumber, Offset, Size, DriveLetter FROM MSFT_Partition WHERE DiskNumber = " + diskNumber))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementBaseObject mo in results)
                {
                    using (mo)
                    {
                        parts.Add(new PartitionRecord
                        {
                            Number = ToInt(mo["PartitionNumber"]),
                            Offset = ToLong(mo["Offset"]),
                            Size = ToLong(mo["Size"]),
                            DriveLetter = ToChar(mo["DriveLetter"])
                        });
                    }
                }
            }
            return parts.OrderBy(p => p.Number).ToList();
        }

        private static string ToStr(object o)  { return o == null ? string.Empty : Convert.ToString(o); }
        private static int ToInt(object o)     { try { return o == null ? 0 : Convert.ToInt32(o); } catch { return 0; } }
        private static long ToLong(object o)   { try { return o == null ? 0 : Convert.ToInt64(o); } catch { return 0; } }
        private static bool ToBool(object o)   { try { return o != null && Convert.ToBoolean(o); } catch { return false; } }

        private static char ToChar(object o)
        {
            if (o == null) return '\0';
            try
            {
                if (o is char) return (char)o;
                int code = Convert.ToInt32(o);
                return code > 0 && code < 0xFFFF ? (char)code : '\0';
            }
            catch
            {
                string s = Convert.ToString(o);
                return string.IsNullOrEmpty(s) ? '\0' : s[0];
            }
        }
    }
}
