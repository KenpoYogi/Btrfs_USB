// Btrfs USB Mounter
// Copyright (c) 2026 Jay W
// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
//
// Licensed under the PolyForm Noncommercial License 1.0.0. Noncommercial use only:
// no commercial use of any kind is permitted. See the LICENSE file or
// https://polyformproject.org/licenses/noncommercial/1.0.0/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

        /// <summary>
        /// Reads the start of a partition (one read, <see cref="FsProbe.ReadLength"/> bytes, less for tiny
        /// partitions) and identifies the filesystem; null if none is recognised. readFailed = I/O error.
        /// </summary>
        public static FsInfo TryProbe(int diskNumber, long partitionOffset, long partitionSize, out bool readFailed)
        {
            int length = FsProbe.ReadLength;
            if (partitionSize > 0 && partitionSize < length) length = (int)(partitionSize & ~4095L);
            if (length < 4096)
            {
                readFailed = false;
                return null;
            }
            byte[] buffer;
            try
            {
                buffer = Read(diskNumber, partitionOffset, length);
            }
            catch (Exception ex)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture,
                    "Raw read of disk {0} at offset {1:N0} ({2:N0} bytes) failed: {3}: {4}{5}", diskNumber, partitionOffset, length,
                    ex.GetType().Name, ex.Message, ex is Win32Exception ? " (Win32 error " + ((Win32Exception)ex).NativeErrorCode + ")" : string.Empty));
                readFailed = true;
                return null;
            }
            readFailed = false;
            return FsProbe.Probe(buffer);
        }
    }

    /// <summary>
    /// Caches superblock reads per partition, so rescans don't wake a drive that has spun down.
    /// A cached null means "checked, no supported filesystem". Read errors are never cached.
    /// </summary>
    public sealed class SuperblockCache
    {
        private sealed class Entry
        {
            public FsInfo Value;
        }

        private readonly ConcurrentDictionary<string, Entry> entries = new ConcurrentDictionary<string, Entry>();

        public FsInfo GetOrRead(string volumeKey, int diskNumber, long offset, long size)
        {
            string key = volumeKey + "|" + offset;
            Entry entry;
            if (entries.TryGetValue(key, out entry))
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "  superblock disk {0} offset {1:N0}: cached, {2}",
                    diskNumber, offset, Describe(entry.Value)));
                return entry.Value;
            }
            bool readFailed;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            FsInfo sb = RawDisk.TryProbe(diskNumber, offset, size, out readFailed);
            if (!readFailed) entries[key] = new Entry { Value = sb };
            Log.Debug(string.Format(CultureInfo.InvariantCulture, "  superblock disk {0} offset {1:N0}: read in {2:N0} ms, {3}",
                diskNumber, offset, sw.ElapsedMilliseconds, readFailed ? "READ FAILED (not cached, retried next scan)" : Describe(sb)));
            return sb;
        }

        private static string Describe(FsInfo fs)
        {
            return fs == null ? "no supported filesystem signature" : fs.ToString();
        }

        public void Clear()
        {
            Log.Debug("Superblock cache cleared (" + entries.Count.ToString(CultureInfo.InvariantCulture) + " entries).");
            entries.Clear();
        }

        public void InvalidateDisk(string diskKey)
        {
            Log.Debug("Superblock cache: forgetting disk " + diskKey);
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
                    Log.Debug("Superblock cache: disk gone, dropping " + k);
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

        /// <summary>MSFT_Disk BusType values, for the log.</summary>
        public static string BusTypeName(int busType)
        {
            switch (busType)
            {
                case 1: return "SCSI";
                case 2: return "ATAPI";
                case 3: return "ATA";
                case 4: return "1394";
                case 5: return "SSA";
                case 6: return "FibreChannel";
                case 7: return "USB";
                case 8: return "RAID";
                case 9: return "iSCSI";
                case 10: return "SAS";
                case 11: return "SATA";
                case 12: return "SD";
                case 13: return "MMC";
                case 14: return "Virtual";
                case 15: return "FileBackedVirtual";
                case 16: return "StorageSpaces";
                case 17: return "NVMe";
                default: return "Unknown(" + busType.ToString(CultureInfo.InvariantCulture) + ")";
            }
        }

        public static string PartitionStyleName(int style)
        {
            return style == 1 ? "MBR" : style == 2 ? "GPT" : "RAW";
        }

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
