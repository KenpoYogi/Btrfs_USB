// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XnixUsbMounter.Core
{
    // ---------------------------------------------------------------------------------------
    //  Filesystem kinds and how each one gets mounted
    // ---------------------------------------------------------------------------------------
    public enum FsKind
    {
        Btrfs,
        Ext2,
        Ext3,
        Ext4,
        Xfs,
        Jfs,
        ReiserFs,
        Reiser4,
        Zfs,
        HfsPlus,
        Apfs,
        Ufs
    }

    public enum MountMethod
    {
        /// <summary>wsl --mount --type &lt;kernel type&gt;: the WSL kernel mounts it read/write.</summary>
        Kernel,
        /// <summary>wsl --mount --bare, then fsapfsmount (FUSE, read-only) inside the distro.</summary>
        ApfsFuse,
        /// <summary>wsl --mount --bare, then zpool import under /mnt/wsl (OpenZFS module built for the WSL kernel).</summary>
        ZfsPool,
        /// <summary>Detected and shown, but this tool cannot mount it.</summary>
        None
    }

    public static class FsTypes
    {
        /// <summary>Name as used by Linux (mount -t, /proc/filesystems) and stored in state.json.</summary>
        public static string Name(FsKind kind)
        {
            switch (kind)
            {
                case FsKind.Btrfs: return "btrfs";
                case FsKind.Ext2: return "ext2";
                case FsKind.Ext3: return "ext3";
                case FsKind.Ext4: return "ext4";
                case FsKind.Xfs: return "xfs";
                case FsKind.Jfs: return "jfs";
                case FsKind.ReiserFs: return "reiserfs";
                case FsKind.Reiser4: return "reiser4";
                case FsKind.Zfs: return "zfs";
                case FsKind.HfsPlus: return "hfsplus";
                case FsKind.Apfs: return "apfs";
                case FsKind.Ufs: return "ufs";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        public static string DisplayName(FsKind kind)
        {
            switch (kind)
            {
                case FsKind.Xfs: return "XFS";
                case FsKind.Jfs: return "JFS";
                case FsKind.ReiserFs: return "ReiserFS";
                case FsKind.Reiser4: return "Reiser4";
                case FsKind.Zfs: return "ZFS";
                case FsKind.HfsPlus: return "HFS+";
                case FsKind.Apfs: return "APFS";
                case FsKind.Ufs: return "UFS";
                default: return Name(kind);
            }
        }

        /// <summary>state.json value to kind; missing values are btrfs (files written before other types existed).</summary>
        public static FsKind Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return FsKind.Btrfs;
            foreach (FsKind k in Enum.GetValues(typeof(FsKind)))
            {
                if (string.Equals(Name(k), name, StringComparison.OrdinalIgnoreCase)) return k;
            }
            return FsKind.Btrfs;
        }

        /// <summary>Default method; APFS may use the kernel driver instead (see <see cref="FsSupport.MethodFor"/>).</summary>
        public static MountMethod Method(FsKind kind)
        {
            if (kind == FsKind.Apfs) return MountMethod.ApfsFuse;
            if (kind == FsKind.Zfs) return MountMethod.ZfsPool;
            return MountMethod.Kernel;
        }

        /// <summary>Why a kind can never be mounted by this tool (MountMethod.None); null otherwise.</summary>
        public static string NotMountableReason(FsKind kind)
        {
            return null;
        }

        /// <summary>Kinds whose driver tools/build-wsl-modules.sh can compile for the running WSL kernel.</summary>
        public static bool Buildable(FsKind kind)
        {
            return kind == FsKind.Jfs || kind == FsKind.ReiserFs || kind == FsKind.HfsPlus || kind == FsKind.Zfs || kind == FsKind.Apfs ||
                   kind == FsKind.Ufs;
        }

        /// <summary>Advice when the running WSL kernel has no driver for a kernel-mounted kind.</summary>
        public static string NoDriverHint(FsKind kind)
        {
            if (kind == FsKind.Reiser4)
            {
                return "Reiser4 was never part of mainline Linux, and its out-of-tree patches stop at Linux 5.16, so there is " +
                       "no driver for the WSL kernel (6.x).";
            }
            if (kind == FsKind.ReiserFs)
            {
                return "No ReiserFS driver is installed for the running WSL kernel. ReiserFS was removed from Linux 6.13, so " +
                       "Tools > Build filesystem drivers can only build it for WSL kernels older than 6.13 (see uname -r); " +
                       "on newer kernels it skips ReiserFS. Copy the data off on a Linux system with an older kernel.";
            }
            if (Buildable(kind))
            {
                return "No " + DisplayName(kind) + " driver is installed for the running WSL kernel. Use Tools > Build filesystem " +
                       "drivers (or run tools/build-wsl-modules.sh as root in the distro) to compile it; rerun that after every " +
                       "\"wsl --update\", because a new WSL kernel needs its own build.";
            }
            return "The WSL kernel has no " + DisplayName(kind) + " driver. It works with a custom WSL kernel that includes it " +
                   "(set kernel= in %UserProfile%\\.wslconfig, then run wsl --shutdown).";
        }
    }

    /// <summary>What a superblock says about one filesystem.</summary>
    public sealed class FsInfo
    {
        public FsKind Kind { get; set; }
        public string Label { get; set; }
        public string Uuid { get; set; }
        public double TotalBytes { get; set; }
        /// <summary>Negative when the superblock does not record free space.</summary>
        public double FreeBytes { get; set; }
        public int NumDevices { get; set; }
        /// <summary>Version detail for the log, e.g. "ReiserFS 3.6" or "HFSX".</summary>
        public string Version { get; set; }
        /// <summary>Only read access is possible (APFS via FUSE, journaled HFS+).</summary>
        public bool ReadOnly { get; set; }
        /// <summary>User-facing caveat, shown in the log when mounting.</summary>
        public string Note { get; set; }
        /// <summary>Caveat shown only when mounting read/write (UFS: what changes for FreeBSD).</summary>
        public string WriteNote { get; set; }
        /// <summary>Options the kernel driver needs to mount it at all (UFS: ufstype=...).</summary>
        public string KernelOptions { get; set; }

        public FsInfo()
        {
            FreeBytes = -1;
            NumDevices = 1;
        }

        public double UsedBytes { get { return FreeBytes >= 0 && TotalBytes > 0 ? Math.Max(0, TotalBytes - FreeBytes) : -1; } }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}{1} label '{2}' uuid {3}, {4}{5}{6}{7}",
                FsTypes.DisplayName(Kind), string.IsNullOrEmpty(Version) ? string.Empty : " (" + Version + ")", Label, Uuid,
                TotalBytes > 0 ? Fmt.Bytes(TotalBytes) : "size unknown",
                FreeBytes >= 0 ? ", " + Fmt.BytesZero(FreeBytes) + " free" : string.Empty,
                NumDevices > 1 ? ", " + NumDevices.ToString(CultureInfo.InvariantCulture) + " devices" : string.Empty,
                ReadOnly ? ", read-only" : string.Empty);
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Signature probing on the first 272 KiB of a partition (one raw read, cached per partition)
    // ---------------------------------------------------------------------------------------
    public static class FsProbe
    {
        /// <summary>Covers every location probed below (ZFS uberblocks end at 256 KiB). Multiple of 4 KiB.</summary>
        public const int ReadLength = 0x44000;

        /// <summary>Every recognised signature, in priority order. Normally zero or one.</summary>
        public static List<FsInfo> ProbeAll(byte[] b)
        {
            var found = new List<FsInfo>();
            if (b == null) return found;
            Add(found, Btrfs(b));
            Add(found, Xfs(b));
            Add(found, Ext(b));
            Add(found, Jfs(b));
            Add(found, ReiserFs(b));
            Add(found, Reiser4(b));
            Add(found, HfsPlus(b));
            Add(found, Apfs(b));
            Add(found, Zfs(b));
            Add(found, Ufs(b));
            return found;
        }

        /// <summary>
        /// The filesystem on this partition, or null. When old signatures of another filesystem survive
        /// (reformatted without wiping), the highest-priority one wins and a note says so; mounting the
        /// wrong type simply fails, because the kernel validates the superblock itself.
        /// </summary>
        public static FsInfo Probe(byte[] b)
        {
            List<FsInfo> all = ProbeAll(b);
            if (all.Count == 0) return null;
            FsInfo first = all[0];
            if (all.Count > 1)
            {
                string others = string.Join(", ", all.Skip(1).Select(x => FsTypes.DisplayName(x.Kind)));
                Log.Debug("Several filesystem signatures on one partition: " + string.Join(" | ", all.Select(x => x.ToString())) +
                          ". Using " + FsTypes.DisplayName(first.Kind) + ".");
                first.Note = Join(first.Note, "Old " + others + " signatures were also found on this partition; if mounting fails, " +
                                              "it may have been reformatted without wiping them.");
            }
            return first;
        }

        private static void Add(List<FsInfo> list, FsInfo info)
        {
            if (info != null) list.Add(info);
        }

        private static string Join(string a, string b)
        {
            return string.IsNullOrEmpty(a) ? b : a + " " + b;
        }

        // ---- byte helpers ----------------------------------------------------------------------
        private static bool Has(byte[] b, int offset, int length)
        {
            return offset >= 0 && length >= 0 && offset + length <= b.Length;
        }

        private static bool Ascii(byte[] b, int offset, string magic)
        {
            if (!Has(b, offset, magic.Length)) return false;
            for (int i = 0; i < magic.Length; i++)
            {
                if (b[offset + i] != (byte)magic[i]) return false;
            }
            return true;
        }

        private static ushort Le16(byte[] b, int o) { return (ushort)(b[o] | b[o + 1] << 8); }
        private static uint Le32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }
        private static ulong Le64(byte[] b, int o) { return BitConverter.ToUInt64(b, o); }
        private static ushort Be16(byte[] b, int o) { return (ushort)(b[o] << 8 | b[o + 1]); }
        private static uint Be32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        private static ulong Be64(byte[] b, int o) { return (ulong)Be32(b, o) << 32 | Be32(b, o + 4); }

        /// <summary>NUL-terminated (or padded) UTF-8 text.</summary>
        private static string Text(byte[] b, int offset, int max)
        {
            if (!Has(b, offset, max)) return string.Empty;
            int end = Array.IndexOf(b, (byte)0, offset, max);
            int len = end < 0 ? max : end - offset;
            return Encoding.UTF8.GetString(b, offset, len).Trim();
        }

        /// <summary>16 bytes as a UUID string (byte order as stored, like blkid).</summary>
        private static string Uuid(byte[] b, int offset)
        {
            if (!Has(b, offset, 16)) return string.Empty;
            bool allZero = true;
            var hex = new StringBuilder(32);
            for (int i = 0; i < 16; i++)
            {
                if (b[offset + i] != 0) allZero = false;
                hex.Append(b[offset + i].ToString("x2", CultureInfo.InvariantCulture));
            }
            if (allZero) return string.Empty;
            string h = hex.ToString();
            return h.Substring(0, 8) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4) + "-" + h.Substring(16, 4) + "-" + h.Substring(20, 12);
        }

        // ---- btrfs: superblock at 64 KiB -------------------------------------------------------
        private const int BtrfsOffset = 0x10000;

        private static FsInfo Btrfs(byte[] b)
        {
            if (!Has(b, BtrfsOffset, 4096)) return null;
            var sbBytes = new byte[4096];
            Buffer.BlockCopy(b, BtrfsOffset, sbBytes, 0, 4096);
            Superblock sb = Superblock.Parse(sbBytes);
            if (sb == null) return null;
            // csum_type 0 = crc32c over bytes 0x20..0xFFF, stored in the first 4 bytes. Other checksum types
            // (xxhash, sha256, blake2) are not verified here; the kernel checks them when mounting.
            ushort csumType = Le16(sbBytes, 0xC4);
            if (csumType == 0 && Crc32C.Compute(sbBytes, 0x20, 4096 - 0x20) != Le32(sbBytes, 0))
            {
                Log.Debug("btrfs magic found but the superblock checksum does not match (stale or damaged); ignored.");
                return null;
            }
            return new FsInfo
            {
                Kind = FsKind.Btrfs,
                Label = sb.Label,
                Uuid = sb.Uuid,
                TotalBytes = sb.TotalBytes,
                FreeBytes = Math.Max(0, sb.TotalBytes - sb.BytesUsed),
                NumDevices = sb.NumDevices
            };
        }

        // ---- XFS: superblock at 0, big-endian ---------------------------------------------------
        private static FsInfo Xfs(byte[] b)
        {
            if (!Ascii(b, 0, "XFSB") || !Has(b, 0, 512)) return null;
            uint blockSize = Be32(b, 4);
            if (blockSize < 512 || blockSize > 65536) return null;
            ulong dblocks = Be64(b, 8);
            ulong fdblocks = Be64(b, 144);
            int version = Be16(b, 100) & 0x000F;
            return new FsInfo
            {
                Kind = FsKind.Xfs,
                Label = Text(b, 108, 12),
                Uuid = Uuid(b, 32),
                TotalBytes = (double)dblocks * blockSize,
                FreeBytes = fdblocks <= dblocks ? (double)fdblocks * blockSize : -1,
                Version = "v" + version.ToString(CultureInfo.InvariantCulture)
            };
        }

        // ---- ext2/3/4: superblock at 1 KiB, little-endian ---------------------------------------
        private const uint ExtCompatHasJournal = 0x0004;
        private const uint ExtIncompatJournalDev = 0x0008;
        private const uint ExtIncompat64Bit = 0x0080;
        // features that ext2/ext3 drivers understand (the rest means ext4), as in libblkid
        private const uint Ext2IncompatSupported = 0x0002 | 0x0010;            // FILETYPE | META_BG
        private const uint Ext3IncompatSupported = 0x0002 | 0x0004 | 0x0010;   // + RECOVER
        private const uint Ext23RoCompatSupported = 0x0001 | 0x0002 | 0x0004;  // SPARSE_SUPER | LARGE_FILE | BTREE_DIR

        private static FsInfo Ext(byte[] b)
        {
            const int o = 1024;
            if (!Has(b, o, 1024) || Le16(b, o + 56) != 0xEF53) return null;
            uint compat = Le32(b, o + 92), incompat = Le32(b, o + 96), roCompat = Le32(b, o + 100);
            if ((incompat & ExtIncompatJournalDev) != 0)
            {
                Log.Debug("ext external journal device found (not a mountable filesystem); ignored.");
                return null;
            }
            uint logBlock = Le32(b, o + 24);
            if (logBlock > 6) return null;   // block size 1 KiB .. 64 KiB
            double blockSize = 1024 << (int)logBlock;
            double blocks = Le32(b, o + 4), free = Le32(b, o + 12);
            if ((incompat & ExtIncompat64Bit) != 0)
            {
                blocks += (double)Le32(b, o + 0x150) * 4294967296.0;
                free += (double)Le32(b, o + 0x158) * 4294967296.0;
            }

            FsKind kind;
            bool journal = (compat & ExtCompatHasJournal) != 0;
            if (!journal && (incompat & ~Ext2IncompatSupported) == 0 && (roCompat & ~Ext23RoCompatSupported) == 0) kind = FsKind.Ext2;
            else if (journal && (incompat & ~Ext3IncompatSupported) == 0 && (roCompat & ~Ext23RoCompatSupported) == 0) kind = FsKind.Ext3;
            else kind = FsKind.Ext4;

            return new FsInfo
            {
                Kind = kind,
                Label = Text(b, o + 120, 16),
                Uuid = Uuid(b, o + 104),
                TotalBytes = blocks * blockSize,
                FreeBytes = free <= blocks ? free * blockSize : -1,
                Version = string.Format(CultureInfo.InvariantCulture, "features compat 0x{0:x} incompat 0x{1:x} ro 0x{2:x}", compat, incompat, roCompat)
            };
        }

        // ---- JFS: superblock at 32 KiB, little-endian --------------------------------------------
        private static FsInfo Jfs(byte[] b)
        {
            const int o = 0x8000;
            if (!Ascii(b, o, "JFS1") || !Has(b, o, 184)) return null;
            long size = BitConverter.ToInt64(b, o + 8);   // in physical blocks
            int pbsize = BitConverter.ToInt32(b, o + 24);
            if (pbsize < 512 || pbsize > 65536) pbsize = 512;
            string label = Text(b, o + 152, 16);
            if (label.Length == 0) label = Text(b, o + 101, 11);   // s_fpack, older jfsutils
            return new FsInfo
            {
                Kind = FsKind.Jfs,
                Label = label,
                Uuid = Uuid(b, o + 136),
                TotalBytes = size > 0 ? (double)size * pbsize : 0,
                Version = "v" + Le32(b, o + 4).ToString(CultureInfo.InvariantCulture)
            };
        }

        // ---- ReiserFS 3.x: superblock at 64 KiB (3.5 layouts also at 8 KiB) -----------------------
        private static FsInfo ReiserFs(byte[] b)
        {
            foreach (int o in new[] { 0x10000, 0x2000 })
            {
                if (!Has(b, o, 116)) continue;
                string version = Ascii(b, o + 52, "ReIsEr2Fs") ? "3.6" : Ascii(b, o + 52, "ReIsEr3Fs") ? "3.6 with journal relocation"
                               : Ascii(b, o + 52, "ReIsErFs") ? "3.5" : null;
                if (version == null) continue;
                double blocks = Le32(b, o), free = Le32(b, o + 4);
                int blockSize = Le16(b, o + 44);
                // as libblkid: a sane block size, and not a superblock copy inside the journal
                if (blockSize >> 9 == 0 || (o / 1024) / (blockSize >> 9) > Le32(b, o + 12) / 2) continue;
                bool v2 = version != "3.5";   // the 3.5 superblock has no UUID or label
                return new FsInfo
                {
                    Kind = FsKind.ReiserFs,
                    Label = v2 ? Text(b, o + 100, 16) : string.Empty,
                    Uuid = v2 ? Uuid(b, o + 84) : string.Empty,
                    TotalBytes = blocks * blockSize,
                    FreeBytes = free <= blocks ? free * (double)blockSize : -1,
                    Version = "ReiserFS " + version
                };
            }
            return null;
        }

        // ---- Reiser4: master superblock at 64 KiB ------------------------------------------------
        private static FsInfo Reiser4(byte[] b)
        {
            const int o = 0x10000;
            if (!Ascii(b, o, "ReIsEr4") || !Has(b, o, 52)) return null;
            return new FsInfo
            {
                Kind = FsKind.Reiser4,
                Label = Text(b, o + 36, 16),
                Uuid = Uuid(b, o + 20)
            };
        }

        // ---- HFS+ / HFSX: volume header at 1 KiB, big-endian --------------------------------------
        private const uint HfsJournaledBit = 1u << 13;

        private static FsInfo HfsPlus(byte[] b)
        {
            const int o = 1024;
            if (!Has(b, o, 512)) return null;
            ushort sig = Be16(b, o);
            if (sig != 0x482B && sig != 0x4858) return null;   // "H+" or "HX"
            ushort version = Be16(b, o + 2);
            if (version != 4 && version != 5) return null;
            uint attributes = Be32(b, o + 4);
            uint blockSize = Be32(b, o + 40);
            if (blockSize < 512 || (blockSize & (blockSize - 1)) != 0) return null;
            double total = (double)Be32(b, o + 44) * blockSize, free = (double)Be32(b, o + 48) * blockSize;
            bool journaled = (attributes & HfsJournaledBit) != 0;
            // the volume name lives in the catalog B-tree; the 64-bit volume ID (finderInfo[6..7]) identifies it
            ulong volumeId = Be64(b, o + 80 + 24);
            return new FsInfo
            {
                Kind = FsKind.HfsPlus,
                Label = string.Empty,
                Uuid = volumeId != 0 ? volumeId.ToString("x16", CultureInfo.InvariantCulture) : string.Empty,
                TotalBytes = total,
                FreeBytes = free <= total ? free : -1,
                Version = sig == 0x4858 ? "HFSX (case-sensitive)" : "HFS+",
                ReadOnly = journaled,
                Note = journaled
                    ? "This HFS+ volume is journaled, so Linux mounts it read-only. To write to it, turn journaling off on a Mac " +
                      "(Disk Utility: hold Option, File > Disable Journaling)."
                    : null
            };
        }

        // ---- APFS: container superblock (NXSB) in block 0 ----------------------------------------
        private static FsInfo Apfs(byte[] b)
        {
            if (!Ascii(b, 32, "NXSB") || !Has(b, 0, 88)) return null;
            uint blockSize = Le32(b, 36);
            if (blockSize < 4096 || blockSize > 65536) return null;
            ulong blocks = Le64(b, 40);
            return new FsInfo
            {
                Kind = FsKind.Apfs,
                Label = string.Empty,   // volume names are in the volume superblocks; logged when mounted
                Uuid = Uuid(b, 72),
                TotalBytes = (double)blocks * blockSize,
                ReadOnly = true,
                Note = "APFS is mounted read-only (there is no Linux driver that writes APFS safely). " +
                       "Encrypted (FileVault) volumes are not supported."
            };
        }

        // ---- ZFS: vdev label 0 (nvlist at 16 KiB, uberblock array at 128..256 KiB) ----------------
        private const ulong ZfsUberblockMagic = 0x00bab10c;
        private const int ZfsMinUberblocks = 4;

        private static FsInfo Zfs(byte[] b)
        {
            if (!Has(b, 0x40000, 0)) return null;
            // as libblkid: at least 4 uberblocks in the ring of label 0 (1 KiB steps cover every ashift)
            int uberblocks = 0;
            for (int o = 0x20000; o + 8 <= 0x40000; o += 1024)
            {
                if (Le64(b, o) == ZfsUberblockMagic || Be64(b, o) == ZfsUberblockMagic) uberblocks++;
            }
            if (uberblocks < ZfsMinUberblocks)
            {
                if (uberblocks > 0) Log.Debug("Only " + uberblocks + " ZFS uberblock(s) found; not treated as ZFS.");
                return null;
            }

            var info = new FsInfo { Kind = FsKind.Zfs, Label = string.Empty, Uuid = string.Empty };
            try
            {
                Dictionary<string, object> pairs = ZfsNvList(b, 0x4000, 0x1C000);
                object v;
                if (pairs.TryGetValue("name", out v)) info.Label = (string)v;
                if (pairs.TryGetValue("pool_guid", out v)) info.Uuid = ((ulong)v).ToString(CultureInfo.InvariantCulture);
                if (pairs.TryGetValue("version", out v)) info.Version = "pool version " + ((ulong)v).ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Log.DebugException("ZFS label nvlist could not be parsed (magic found, details unknown)", ex);
            }
            info.Note = FsTypes.NotMountableReason(FsKind.Zfs);
            return info;
        }

        /// <summary>Top-level string and uint64 pairs of an XDR-encoded nvlist; nested lists are skipped.</summary>
        private static Dictionary<string, object> ZfsNvList(byte[] b, int start, int length)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            // 4-byte header: encoding (1 = XDR), endianness, 2 reserved; then nvl_version, nvl_nvflag
            if (!Has(b, start, 12) || b[start] != 1) return result;
            int p = start + 12, end = Math.Min(b.Length, start + length);
            while (p + 8 <= end)
            {
                int encodedSize = (int)Be32(b, p);
                if (encodedSize == 0) break;   // end of list
                if (encodedSize < 0 || p + encodedSize > end) break;
                int q = p + 8;                  // skip encoded and decoded sizes
                int nameLength = (int)Be32(b, q);
                q += 4;
                if (nameLength <= 0 || nameLength > 256 || q + nameLength > end) break;
                string name = Encoding.ASCII.GetString(b, q, nameLength);
                q += (nameLength + 3) & ~3;
                uint type = Be32(b, q), count = Be32(b, q + 4);
                q += 8;
                if (type == 9 && count == 1)          // DATA_TYPE_STRING
                {
                    int len = (int)Be32(b, q);
                    if (len >= 0 && q + 4 + len <= end) result[name] = Encoding.UTF8.GetString(b, q + 4, len);
                }
                else if (type == 8 && count == 1)     // DATA_TYPE_UINT64
                {
                    result[name] = Be64(b, q);
                }
                p += encodedSize;
            }
            return result;
        }

        // ---- UFS (FreeBSD, NetBSD, OpenBSD, Solaris): UFS2 superblock at 64 KiB, UFS1 at 8 KiB, either byte order ----
        private const uint UfsMagic1 = 0x00011954;
        private const uint UfsMagic2 = 0x19540119;
        private const uint UfsStateOk = 0x7c269d38;     // Solaris: fs_state = UfsStateOk - fs_time when clean
        private const byte UfsFlagsUpdated = 0x80;      // in the old 8-bit flags: the 32-bit fs_flags are in use
        private const uint UfsSoftDep = 0x02, UfsNeedsFsck = 0x04, UfsSuj = 0x08, UfsGjournal = 0x40, UfsMetaCkHash = 0x200;

        private static FsInfo Ufs(byte[] b)
        {
            foreach (int o in new[] { 0x10000, 0x2000 })
            {
                // UFS2 at 64 KiB; UFS1 (or, from some makefs versions, UFS2) at 8 KiB. A UFS1 magic at 64 KiB is a
                // backup copy (64 KiB blocks), so it does not count
                if (!Has(b, o, 1376)) continue;
                uint le = Le32(b, o + 1372), bem = Be32(b, o + 1372);
                bool be;
                uint magic;
                if (le == UfsMagic2 || (o == 0x2000 && le == UfsMagic1)) { be = false; magic = le; }
                else if (bem == UfsMagic2 || (o == 0x2000 && bem == UfsMagic1)) { be = true; magic = bem; }
                else continue;
                Func<int, uint> u32 = x => be ? Be32(b, o + x) : Le32(b, o + x);
                Func<int, ulong> u64 = x => be ? Be64(b, o + x) : Le64(b, o + x);

                // the Linux driver's checks: power-of-two sizes, blocks of at least 4 KiB, at most 8 fragments per block
                uint bsize = u32(48), fsize = u32(52), frag = u32(56);
                if (!Pow2(bsize) || !Pow2(fsize) || bsize < 4096 || bsize > 65536 || fsize < 512 || fsize > bsize ||
                    bsize / fsize > 8 || frag != bsize / fsize)
                {
                    Log.Debug("UFS magic found but the block sizes are not sane; ignored.");
                    continue;
                }

                bool ufs2 = magic == UfsMagic2;
                byte clean = b[o + 209], oldFlags = b[o + 211];
                bool newLayout = ufs2 || (oldFlags & UfsFlagsUpdated) != 0;   // FreeBSD 5 and later: fs_flags, fs_volname
                uint flags = newLayout ? u32(1312) : oldFlags;
                bool ckHash = newLayout && (flags & UfsMetaCkHash) != 0 && u32(1308) != 0;   // NetBSD uses 0x200 otherwise
                bool softDep = (flags & UfsSoftDep) != 0;
                bool journal = softDep && (flags & UfsSuj) != 0;
                bool gjournal = newLayout && (flags & UfsGjournal) != 0;

                // which ufstype= the kernel needs: it cannot tell the variants apart itself
                uint time = u32(32);
                string flavour = ufs2 ? "ufs2"
                               : be && u32(1336) == unchecked(UfsStateOk - time) ? "sun"
                               : !be && u32(132) == unchecked(UfsStateOk - time) ? "sunx86"
                               : "44bsd";
                bool solaris = flavour == "sun" || flavour == "sunx86";

                double total = (ufs2 ? (double)u64(1080) : u32(36)) * fsize;
                double free = ((ufs2 ? (double)u64(1016) : u32(196)) * frag + (ufs2 ? (double)u64(1032) : u32(204))) * fsize;
                uint id0 = u32(144), id1 = u32(148);

                var features = new List<string>();
                if (be) features.Add("big-endian");
                if (solaris) features.Add("Solaris");
                if (journal) features.Add("soft updates + journal");
                else if (softDep) features.Add("soft updates");
                if (gjournal) features.Add("gjournal");
                if (ckHash) features.Add("check hashes");

                var info = new FsInfo
                {
                    Kind = FsKind.Ufs,
                    Label = newLayout && !solaris ? Text(b, o + 680, 32) : string.Empty,
                    Uuid = id0 != 0 || id1 != 0 ? id0.ToString("x8", CultureInfo.InvariantCulture) + id1.ToString("x8", CultureInfo.InvariantCulture) : string.Empty,
                    TotalBytes = total,
                    FreeBytes = free <= total ? free : -1,
                    Version = (ufs2 ? "UFS2" : "UFS1") + (features.Count > 0 ? ", " + string.Join(", ", features) : string.Empty),
                    KernelOptions = "ufstype=" + flavour
                };
                if (solaris)
                {
                    info.ReadOnly = true;
                    info.Note = "Solaris UFS is mounted read-only.";
                }
                else if (clean != 1)
                {
                    // FreeBSD and OpenBSD write 0 while mounted, NetBSD 2; the Linux driver takes 2 for clean
                    info.ReadOnly = true;
                    info.Note = "This UFS filesystem was not cleanly unmounted, or is still in use on another system, so it is " +
                                "mounted read-only. Check it on a BSD system first (FreeBSD: fsck_ffs), then eject it there.";
                }
                else if ((flags & UfsNeedsFsck) != 0 && newLayout)
                {
                    info.ReadOnly = true;
                    info.Note = "FreeBSD marked this UFS filesystem as needing a check, so it is mounted read-only. Run fsck_ffs on it on FreeBSD.";
                }
                else if (gjournal)
                {
                    info.ReadOnly = true;
                    info.Note = "This UFS filesystem uses FreeBSD's gjournal, which Linux cannot replay, so it is mounted read-only.";
                }
                if (ufs2 && o != 0x10000)
                {
                    info.Note = Join(info.Note, "Its UFS2 superblock is at 8 KiB (older makefs versions put it there); the Linux UFS " +
                                                "driver only looks at 64 KiB, so it cannot mount it.");
                }
                if (fsize > 4096)
                {
                    info.Note = Join(info.Note, string.Format(CultureInfo.InvariantCulture,
                        "Its fragment size is {0} bytes; the Linux UFS driver only mounts fragments of up to 4096 bytes.", fsize));
                }

                var write = new List<string>();
                if (ckHash)
                {
                    write.Add("Writing switches off FreeBSD's metadata check hashes (Linux does not maintain them). To turn them back " +
                              "on, run fsck_ffs on the unmounted filesystem on FreeBSD, without -p or -y, and answer yes to the " +
                              "\"ADD ... CHECK-HASH PROTECTION\" questions.");
                }
                if (journal)
                {
                    write.Add("If the drive is removed without ejecting it, FreeBSD needs a full fsck_ffs before it mounts it " +
                              "read/write (the soft updates journal from before this mount is not used).");
                }
                if (write.Count > 0) info.WriteNote = string.Join(" ", write);
                return info;
            }
            return null;
        }

        private static bool Pow2(uint v)
        {
            return v != 0 && (v & (v - 1)) == 0;
        }
    }

    /// <summary>CRC-32C (Castagnoli), as used by the btrfs superblock checksum.</summary>
    public static class Crc32C
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0x82F63B78u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return ~crc;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  What the running WSL kernel and distro can mount (checked at runtime, cached per distro)
    // ---------------------------------------------------------------------------------------
    public enum FsAvailability
    {
        Unknown,
        Yes,
        NoDriver,       // kernel lacks the filesystem
        NeedsTools,     // FUSE tool missing in the distro
        NotSupported    // this tool does not mount it at all
    }

    public sealed class FsSupport
    {
        /// <summary>Filesystems a kernel driver can provide (built in, a shipped module, or one built locally).</summary>
        private static readonly FsKind[] KernelKinds =
        {
            FsKind.Btrfs, FsKind.Ext2, FsKind.Ext3, FsKind.Ext4, FsKind.Xfs, FsKind.Jfs, FsKind.ReiserFs, FsKind.Reiser4,
            FsKind.HfsPlus, FsKind.Apfs, FsKind.Zfs, FsKind.Ufs
        };

        private sealed class DistroSupport
        {
            public readonly HashSet<FsKind> Kernel = new HashSet<FsKind>();
            public bool ApfsFuse;
            public bool ZfsTools;
            /// <summary>The ufs module was built by tools/build-wsl-modules.sh (write support plus the FreeBSD handoff).</summary>
            public bool UfsWrite;
        }

        /// <summary>
        /// Shell prefix that puts the locally built modules back after a WSL restart. WSL keeps
        /// /lib/modules/&lt;release&gt; in an overlay whose writable layer is in memory, so
        /// tools/build-wsl-modules.sh also keeps them in /var/lib/wsl-modules/&lt;release&gt; on the distro's
        /// disk. When they are missing this copies them back and runs depmod, and prints "r:restored".
        /// </summary>
        public const string RestoreModules =
            "K=$(uname -r); S=/var/lib/wsl-modules/$K; D=/lib/modules/$K/extra; " +
            "if [ ! -e \"$D/.restored\" ] && ls \"$S\"/*.ko >/dev/null 2>&1; then " +
            "mkdir -p \"$D\" && cp -f \"$S\"/*.ko \"$D\"/ && depmod -a \"$K\" && touch \"$D/.restored\" && echo r:restored; fi; ";

        private readonly ConcurrentDictionary<string, DistroSupport> byDistro =
            new ConcurrentDictionary<string, DistroSupport>(StringComparer.Ordinal);

        /// <summary>Cached answer, or Unknown if this distro has not been checked yet. Never blocks.</summary>
        public FsAvailability Get(string distro, FsKind kind)
        {
            DistroSupport s;
            if (string.IsNullOrEmpty(distro) || !byDistro.TryGetValue(distro, out s)) return FsAvailability.Unknown;
            switch (kind)
            {
                case FsKind.Apfs:
                    return s.Kernel.Contains(FsKind.Apfs) || s.ApfsFuse ? FsAvailability.Yes : FsAvailability.NeedsTools;
                case FsKind.Zfs:
                    if (!s.Kernel.Contains(FsKind.Zfs)) return FsAvailability.NoDriver;
                    return s.ZfsTools ? FsAvailability.Yes : FsAvailability.NeedsTools;
                default:
                    return s.Kernel.Contains(kind) ? FsAvailability.Yes : FsAvailability.NoDriver;
            }
        }

        public bool HasKernelDriver(string distro, FsKind kind)
        {
            DistroSupport s;
            return !string.IsNullOrEmpty(distro) && byDistro.TryGetValue(distro, out s) && s.Kernel.Contains(kind);
        }

        /// <summary>
        /// UFS may be written: the ufs module carries the modinfo field wsl_handoff=1, so it has write support and
        /// keeps fs_clean, check hashes and the journal consistent for FreeBSD (see ufs_handoff in the build script).
        /// </summary>
        public bool CanWriteUfs(string distro)
        {
            DistroSupport s;
            return !string.IsNullOrEmpty(distro) && byDistro.TryGetValue(distro, out s) && s.Kernel.Contains(FsKind.Ufs) && s.UfsWrite;
        }

        /// <summary>
        /// How a kind gets mounted with this distro. APFS: the kernel driver (linux-apfs-rw) when writing is
        /// allowed or FUSE is missing; otherwise fsapfsmount, which is read-only but shows every volume.
        /// </summary>
        public MountMethod MethodFor(string distro, FsKind kind, bool apfsWrite)
        {
            if (kind != FsKind.Apfs) return FsTypes.Method(kind);
            DistroSupport s;
            bool known = !string.IsNullOrEmpty(distro) && byDistro.TryGetValue(distro, out s);
            bool kernel = known && byDistro[distro].Kernel.Contains(FsKind.Apfs);
            bool fuse = known && byDistro[distro].ApfsFuse;
            if (kernel && (apfsWrite || !fuse)) return MountMethod.Kernel;
            return MountMethod.ApfsFuse;
        }

        public bool IsKnown(string distro)
        {
            return !string.IsNullOrEmpty(distro) && byDistro.ContainsKey(distro);
        }

        public void Forget(string distro)
        {
            DistroSupport ignored;
            if (!string.IsNullOrEmpty(distro)) byDistro.TryRemove(distro, out ignored);
        }

        /// <summary>
        /// Asks the distro (as root) which filesystems the kernel can provide, without loading anything:
        /// registered in /proc/filesystems, or a module modinfo can find (shipped, or built by
        /// tools/build-wsl-modules.sh). Also checks for fsapfsmount and zpool, and whether the ufs module is the
        /// locally built one that may write. One short wsl.exe call.
        /// </summary>
        public async Task RefreshAsync(string distro, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(distro)) return;
            string kinds = string.Join(" ", KernelKinds.Select(FsTypes.Name));
            string script = RestoreModules +
                "for t in " + kinds + "; do " +
                "if grep -qw \"$t\" /proc/filesystems || modinfo -n \"$t\" >/dev/null 2>&1; then echo \"k:$t=yes\"; else echo \"k:$t=no\"; fi; done; " +
                "if command -v fsapfsmount >/dev/null 2>&1 && grep -qw fuse /proc/filesystems; then echo t:fsapfsmount=yes; else echo t:fsapfsmount=no; fi; " +
                "if command -v zpool >/dev/null 2>&1; then echo t:zpool=yes; else echo t:zpool=no; fi; " +
                "if modinfo -F wsl_handoff ufs 2>/dev/null | grep -qx 1; then echo t:ufswrite=yes; else echo t:ufswrite=no; fi";
            WslResult r = await Wsl.RunRootAsync(distro, script, 60, ct).ConfigureAwait(false);
            if (r.ExitCode != 0)
            {
                Log.Debug("Filesystem support check failed in " + distro + "; will try again later.");
                return;
            }
            var s = new DistroSupport();
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                bool yes = t.EndsWith("=yes", StringComparison.Ordinal);
                if (t.StartsWith("k:", StringComparison.Ordinal))
                {
                    string name = t.Substring(2, t.IndexOf('=') - 2);
                    FsKind kind = FsTypes.Parse(name);
                    if (yes && string.Equals(FsTypes.Name(kind), name, StringComparison.Ordinal)) s.Kernel.Add(kind);
                }
                else if (t.StartsWith("t:fsapfsmount=", StringComparison.Ordinal)) s.ApfsFuse = yes;
                else if (t.StartsWith("t:zpool=", StringComparison.Ordinal)) s.ZfsTools = yes;
                else if (t.StartsWith("t:ufswrite=", StringComparison.Ordinal)) s.UfsWrite = yes;
                else if (t == "r:restored") Log.Debug("Restored the locally built drivers in " + distro + " after a WSL restart.");
            }
            byDistro[distro] = s;
            Log.Debug("Filesystem support in " + distro + ": kernel drivers " + string.Join(", ", s.Kernel.Select(FsTypes.DisplayName)) +
                      "; fsapfsmount " + (s.ApfsFuse ? "yes" : "no") + "; zpool " + (s.ZfsTools ? "yes" : "no") +
                      "; UFS writable " + (s.UfsWrite ? "yes" : "no"));
        }

        public async Task EnsureAsync(string distro, CancellationToken ct)
        {
            if (!IsKnown(distro)) await RefreshAsync(distro, ct).ConfigureAwait(false);
        }

        /// <summary>User-facing reason why this kind cannot be mounted now; null when it can (or is unknown).</summary>
        public static string Explain(FsKind kind, FsAvailability a)
        {
            switch (a)
            {
                case FsAvailability.NotSupported:
                    return FsTypes.NotMountableReason(kind);
                case FsAvailability.NoDriver:
                    return FsTypes.NoDriverHint(kind);
                case FsAvailability.NeedsTools:
                    if (kind == FsKind.Zfs)
                    {
                        return "The ZFS driver is installed but the zpool tool is missing in the WSL distro: install the " +
                               "package zfs (openSUSE filesystems repository) or zfsutils-linux (Debian, Kali, Ubuntu), then click Refresh.";
                    }
                    return "Mounting APFS needs a driver: install the package libfsapfs (openSUSE; SLES from Package Hub) or " +
                           "libfsapfs-utils (Debian 13, Ubuntu 24.04; the Ubuntu 26.04 and Kali builds lack FUSE support) for " +
                           "read-only access, or build the APFS kernel driver with Tools > Build filesystem drivers (openSUSE, SLES, " +
                           "Debian, Kali, Ubuntu), then click Refresh.";
                default:
                    return null;
            }
        }
    }
}
