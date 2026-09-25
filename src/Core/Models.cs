// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace XnixUsbMounter.Core
{
    // ---------------------------------------------------------------------------------------
    //  Persisted state (state.json) - member names match the PowerShell version's file
    // ---------------------------------------------------------------------------------------
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string Distro { get; set; }
        [DataMember] public bool AutoMount { get; set; }
        [DataMember] public bool OpenExplorer { get; set; }
        [DataMember] public bool ShowAllDisks { get; set; }
        [DataMember] public string Options { get; set; }
        /// <summary>Mount APFS read/write with the experimental linux-apfs-rw driver (default: read-only).</summary>
        [DataMember] public bool ApfsWrite { get; set; }
        /// <summary>Mount BSD UFS read/write with the locally built ufs driver (default: read-only).</summary>
        [DataMember] public bool UfsWrite { get; set; }
        /// <summary>Height of the log pane in 96-dpi pixels, set by dragging the splitter (0 = default).</summary>
        [DataMember] public int LogHeight { get; set; }

        public AppSettings()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Distro = string.Empty;
            AutoMount = false;
            OpenExplorer = true;
            ShowAllDisks = false;
            Options = string.Empty;
            ApfsWrite = false;
            UfsWrite = false;
            LogHeight = 0;
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }

    [DataContract]
    public sealed class MountEntry
    {
        [DataMember] public string Key { get; set; }
        [DataMember] public string DiskUniqueId { get; set; }
        [DataMember] public int DiskNumber { get; set; }
        [DataMember] public int PartitionNumber { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Label { get; set; }
        [DataMember] public string Uuid { get; set; }
        [DataMember] public string Model { get; set; }
        [DataMember] public string Distro { get; set; }
        [DataMember] public string Options { get; set; }
        [DataMember] public string MountedAt { get; set; }
        /// <summary>Linux filesystem name (btrfs, ext4, xfs, apfs...). Missing in older files = btrfs.</summary>
        [DataMember] public string FsType { get; set; }
        [DataMember] public bool ReadOnly { get; set; }
        /// <summary>"kernel", "fuse" or "zfs"; missing = kernel (see <see cref="Method"/>).</summary>
        [DataMember] public string MountMethod { get; set; }

        public FsKind Kind { get { return FsTypes.Parse(FsType); } }

        public MountMethod Method
        {
            get
            {
                if (MountMethod == "fuse") return Core.MountMethod.ApfsFuse;
                if (MountMethod == "zfs") return Core.MountMethod.ZfsPool;
                if (MountMethod == "kernel") return Core.MountMethod.Kernel;
                return FsTypes.Method(Kind);   // older entries
            }
        }

        public static string MethodName(MountMethod m)
        {
            return m == Core.MountMethod.ApfsFuse ? "fuse" : m == Core.MountMethod.ZfsPool ? "zfs" : "kernel";
        }

        public MountEntry Clone()
        {
            return (MountEntry)MemberwiseClone();
        }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public AppSettings Settings { get; set; }
        [DataMember] public List<MountEntry> Mounts { get; set; }
        [DataMember] public int KeepAlivePid { get; set; }

        public AppState()
        {
            SetDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetDefaults();
        }

        private void SetDefaults()
        {
            Settings = new AppSettings();
            Mounts = new List<MountEntry>();
            KeepAlivePid = 0;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Disks and volumes
    // ---------------------------------------------------------------------------------------
    public sealed class DiskRecord
    {
        public const int BusTypeUsb = 7;

        public int Number { get; set; }
        public string UniqueId { get; set; }
        public string FriendlyName { get; set; }
        public string SerialNumber { get; set; }
        public long Size { get; set; }
        public int BusType { get; set; }
        public bool IsBoot { get; set; }
        public bool IsSystem { get; set; }
        public int PartitionStyle { get; set; }    // 0 = RAW/unknown, 1 = MBR, 2 = GPT
        public bool IsOffline { get; set; }

        /// <summary>Stable identity (same rule as the PowerShell version).</summary>
        public string Key
        {
            get
            {
                if (!string.IsNullOrEmpty(UniqueId)) return UniqueId;
                return string.Format("disk-{0}-{1}", (SerialNumber ?? string.Empty).Trim(), Size);
            }
        }

        public bool InScope(bool includeAllDisks)
        {
            return !IsBoot && !IsSystem && (includeAllDisks || BusType == BusTypeUsb);
        }
    }

    public sealed class PartitionRecord
    {
        public int Number { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
        public char DriveLetter { get; set; }
    }

    public sealed class Superblock
    {
        public const int SuperblockOffset = 0x10000;
        public const string Magic = "_BHRfS_M";

        public string Uuid { get; set; }
        public string Label { get; set; }
        public double TotalBytes { get; set; }
        public double BytesUsed { get; set; }
        public int NumDevices { get; set; }

        /// <summary>Parses a 4 KiB buffer read at partition offset 64 KiB; null if it is not btrfs.</summary>
        public static Superblock Parse(byte[] buffer)
        {
            if (buffer == null || buffer.Length < 0x12B + 256) return null;
            if (Encoding.ASCII.GetString(buffer, 0x40, 8) != Magic) return null;

            var hex = new StringBuilder(32);
            for (int i = 0x20; i < 0x30; i++) hex.Append(buffer[i].ToString("x2"));
            string h = hex.ToString();
            string uuid = h.Substring(0, 8) + "-" + h.Substring(8, 4) + "-" + h.Substring(12, 4) + "-" +
                          h.Substring(16, 4) + "-" + h.Substring(20, 12);

            int end = Array.IndexOf(buffer, (byte)0, 0x12B, 256);
            int labelLength = end < 0 ? 256 : end - 0x12B;

            return new Superblock
            {
                Uuid = uuid,
                Label = Encoding.UTF8.GetString(buffer, 0x12B, labelLength),
                TotalBytes = BitConverter.ToUInt64(buffer, 0x70),
                BytesUsed = BitConverter.ToUInt64(buffer, 0x78),
                NumDevices = (int)Math.Min(int.MaxValue, BitConverter.ToUInt64(buffer, 0x88))
            };
        }
    }

    /// <summary>One filesystem (partition or whole disk) as shown in the main list.</summary>
    public sealed class VolumeInfo
    {
        public FsKind Kind { get; set; }
        /// <summary>Only read access is possible (APFS, journaled HFS+), or the mount is read-only.</summary>
        public bool ReadOnly { get; set; }
        /// <summary>Caveat from the superblock (see <see cref="FsInfo.Note"/>).</summary>
        public string FsNote { get; set; }
        /// <summary>Caveat for read/write mounts (see <see cref="FsInfo.WriteNote"/>).</summary>
        public string FsWriteNote { get; set; }
        /// <summary>Options the kernel needs to mount it (see <see cref="FsInfo.KernelOptions"/>).</summary>
        public string FsOptions { get; set; }
        public string KindName { get { return FsTypes.DisplayName(Kind); } }

        public string Key { get; set; }
        public int DiskNumber { get; set; }
        public string DiskUniqueId { get; set; }
        public string Model { get; set; }
        public double DiskSize { get; set; }
        public int PartitionNumber { get; set; }      // 0 = unpartitioned (whole disk)
        public double PartitionSize { get; set; }
        public string Label { get; set; }
        public string Uuid { get; set; }
        public int NumDevices { get; set; }
        public MountEntry Mount { get; set; }
        public bool Disconnected { get; set; }
        public string DriveLetters { get; set; }
        public double SpaceTotal { get; set; }
        public double SpaceUsed { get; set; }
        /// <summary>Negative when unknown (the superblock does not record it and the drive is not mounted).</summary>
        public double SpaceFree { get; set; }
        public bool SpaceApprox { get; set; }

        public bool Mounted { get { return Mount != null; } }
        public string MountName { get { return Mount != null ? Mount.Name : string.Empty; } }
        public string Distro { get { return Mount != null ? Mount.Distro : string.Empty; } }

        public string DisplayLabel
        {
            get { return string.IsNullOrEmpty(Label) ? (string.IsNullOrEmpty(Uuid) ? "(no label)" : Uuid) : Label; }
        }

        public string WindowsPath
        {
            get { return Mount != null ? MountManager.WindowsPath(Mount.Distro, Mount.Name) : string.Empty; }
        }
    }

    public sealed class Distro
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public string State { get; set; }
        public int Version { get; set; }
    }

    // ---------------------------------------------------------------------------------------
    //  Process results
    // ---------------------------------------------------------------------------------------
    public sealed class WslResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }

        public string Combined
        {
            get
            {
                string text = string.Join(" ", new[] { Output ?? string.Empty, Error ?? string.Empty });
                return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
            }
        }
    }

    public sealed class StreamResult
    {
        public int ExitCode { get; set; }
        public List<string> Lines { get; set; }
        public bool Cancelled { get; set; }
        public bool TimedOut { get; set; }
        public string Error { get; set; }

        public bool Stopped { get { return Cancelled || TimedOut; } }
    }

    // ---------------------------------------------------------------------------------------
    //  btrfs maintenance results
    // ---------------------------------------------------------------------------------------
    public sealed class BlockGroup
    {
        public string Type { get; set; }       // Data, Metadata, System
        public string Profile { get; set; }    // single, DUP, RAID1, ...
        public double Size { get; set; }
        public double Used { get; set; }
    }

    public sealed class UsageInfo
    {
        public double DeviceSize { get; set; }
        public double Allocated { get; set; }
        public double Unallocated { get; set; }
        public double Used { get; set; }
        public double FreeEstimated { get; set; }
        public double FreeMin { get; set; }
        public double FreeStatfs { get; set; }
        public double DataRatio { get; set; }
        public double MetadataRatio { get; set; }
        public double GlobalReserve { get; set; }
        public double GlobalReserveUsed { get; set; }
        public List<BlockGroup> Blocks { get; set; }

        public UsageInfo()
        {
            DataRatio = 1;
            MetadataRatio = 1;
            Blocks = new List<BlockGroup>();
        }

        public double BestFree { get { return FreeEstimated > 0 ? FreeEstimated : FreeStatfs; } }
    }

    public sealed class DeviceStat
    {
        public string Device { get; set; }
        public string Counter { get; set; }
        public long Value { get; set; }
    }

    public sealed class ScrubStatus
    {
        public string Status { get; set; }     // never, running, finished, aborted, interrupted, unknown
        public string Started { get; set; }
        public string Duration { get; set; }
        public double BytesScrubbed { get; set; }
        public double Percent { get; set; }
        public long Errors { get; set; }
        public long Corrected { get; set; }
        public long Uncorrectable { get; set; }

        public bool IsRunning { get { return Status == "running"; } }
        public bool IsResumable { get { return Status == "aborted" || Status == "interrupted"; } }
        public bool HasErrors { get { return Errors > 0 || Uncorrectable > 0; } }

        public static ScrubStatus StartedNow()
        {
            return new ScrubStatus
            {
                Status = "running", Started = string.Empty, Duration = "0:00:00"
            };
        }
    }

    public sealed class DriveInfo
    {
        public bool ToolsMissing { get; set; }
        public string MountName { get; set; }
        public UsageInfo Usage { get; set; }
        public List<DeviceStat> Stats { get; set; }
        public ScrubStatus Scrub { get; set; }
    }

    public sealed class ScrubProgress
    {
        public string Name { get; set; }
        public bool ToolsMissing { get; set; }
        public ScrubStatus Scrub { get; set; }
    }

    public sealed class ScrubCommandResult
    {
        public string Name { get; set; }
        public bool Ok { get; set; }
        public bool ToolsMissing { get; set; }
    }

    public enum CheckStage
    {
        Attach,
        Find,
        Check
    }

    public sealed class CheckResult
    {
        public bool ToolsMissing { get; set; }
        public bool Ok { get; set; }
        public bool Cancelled { get; set; }
        public CheckStage Stage { get; set; }
        public int ExitCode { get; set; }
        public string Summary { get; set; }
        public string LogFile { get; set; }
        public string Label { get; set; }

        public CheckResult()
        {
            Stage = CheckStage.Attach;
            ExitCode = -1;
            Summary = string.Empty;
            LogFile = string.Empty;
            Label = string.Empty;
        }
    }

    public sealed class SpaceFigures
    {
        public double Size { get; set; }
        public double Used { get; set; }
        public double Avail { get; set; }
    }

    public sealed class ScanResult
    {
        public List<VolumeInfo> Volumes { get; set; }
        public List<string> Snapshot { get; set; }
    }
}
