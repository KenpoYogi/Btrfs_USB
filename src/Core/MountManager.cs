using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BtrfsUsbMounter.Core
{
    /// <summary>Mounting, ejecting, keep-alive and reconciliation of saved state with WSL.</summary>
    public sealed class MountManager
    {
        private readonly StateStore state;
        private readonly SuperblockCache cache;

        public MountManager(StateStore state, SuperblockCache cache)
        {
            this.state = state;
            this.cache = cache;
        }

        public static string WindowsPath(string distro, string mountName)
        {
            return string.Format(@"\\wsl.localhost\{0}\mnt\wsl\{1}", distro, mountName);
        }

        public static string DiskPath(int diskNumber)
        {
            return @"\\.\PHYSICALDRIVE" + diskNumber.ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------------------------------
        //  Keep-alive: an idle WSL VM shuts down after a timeout, which detaches mounted disks
        // ---------------------------------------------------------------------------------------
        private Process GetKeepAliveProcess()
        {
            int pid = state.KeepAlivePid;
            if (pid <= 0) return null;
            try
            {
                Process p = Process.GetProcessById(pid);
                if (!p.HasExited && string.Equals(p.ProcessName, "wsl", StringComparison.OrdinalIgnoreCase)) return p;
                p.Dispose();
            }
            catch
            {
                // not running
            }
            return null;
        }

        public void StartKeepAlive(string distro)
        {
            using (Process existing = GetKeepAliveProcess())
            {
                if (existing != null) return;
            }
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.WslExe,
                Arguments = Wsl.JoinArguments(new[] { "-d", distro, "-u", "root", "--exec", "sleep", "infinity" }),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process p = Process.Start(psi))
            {
                if (p == null) return;
                state.KeepAlivePid = p.Id;
                Log.Info(string.Format("Keep-alive started (PID {0}) so WSL won't idle-shut-down and drop the disk.", p.Id));
            }
        }

        public void StopKeepAlive()
        {
            using (Process p = GetKeepAliveProcess())
            {
                if (p != null)
                {
                    try { p.Kill(); } catch { }
                    Log.Info("Keep-alive stopped.");
                }
            }
            state.KeepAlivePid = 0;
        }

        // ---------------------------------------------------------------------------------------
        //  Reconcile saved state with what is really mounted inside WSL
        // ---------------------------------------------------------------------------------------
        public async Task SyncMountStateAsync(CancellationToken ct)
        {
            List<MountEntry> mounts = state.Mounts;
            if (mounts.Count == 0)
            {
                using (Process p = GetKeepAliveProcess())
                {
                    if (p != null) StopKeepAlive();
                }
                return;
            }

            List<string> running = await DistroService.RunningAsync(ct).ConfigureAwait(false);
            if (running.Count == 0)
            {
                Log.Warn("WSL is not running, so previously mounted disks were detached. Clearing stale entries.");
                state.ReplaceMounts(new MountEntry[0]);
                StopKeepAlive();
                return;
            }

            WslResult r = await Wsl.RunAsync(new[] { "-d", running[0], "-u", "root", "--exec", "cat", "/proc/mounts" }, 30, ct)
                .ConfigureAwait(false);
            if (r.ExitCode != 0) return;

            var points = new HashSet<string>(
                r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Split(' '))
                    .Where(f => f.Length > 1)
                    .Select(f => f[1]),
                StringComparer.Ordinal);

            var keep = new List<MountEntry>();
            foreach (MountEntry m in mounts)
            {
                if (points.Contains("/mnt/wsl/" + m.Name)) keep.Add(m);
                else Log.Warn(string.Format("'{0}' is no longer mounted in WSL; removing it from the list.", m.Name));
            }
            state.ReplaceMounts(keep);
            if (keep.Count == 0) StopKeepAlive();
            else StartKeepAlive(keep[0].Distro);
        }

        // ---------------------------------------------------------------------------------------
        //  Mount
        // ---------------------------------------------------------------------------------------
        public string MakeMountName(string label, string uuid)
        {
            string baseName = Regex.Replace(label ?? string.Empty, @"[^A-Za-z0-9._-]", "_").Trim('_', '.');
            if (baseName.Length == 0) baseName = "btrfs-" + (uuid ?? "00000000").Substring(0, Math.Min(8, (uuid ?? "").Length));
            if (baseName.Length > 40) baseName = baseName.Substring(0, 40);
            string name = baseName;
            int i = 2;
            while (state.IsNameInUse(name))
            {
                name = baseName + "-" + i.ToString(CultureInfo.InvariantCulture);
                i++;
            }
            return name;
        }

        public async Task<MountEntry> MountAsync(VolumeInfo volume, string distro, string options, CancellationToken ct)
        {
            if (volume.Mounted)
            {
                Log.Warn(string.Format("'{0}' is already mounted.", volume.DisplayLabel));
                return null;
            }
            if (volume.NumDevices > 1)
            {
                Log.Warn(string.Format("This filesystem spans {0} devices; every member disk must be attached for it to mount.", volume.NumDevices));
            }
            if (!string.IsNullOrEmpty(volume.DriveLetters))
            {
                Log.Warn(string.Format("Note: Windows has {0} open on this disk. wsl --mount takes the whole disk, so those will go offline.", volume.DriveLetters));
            }

            string name = MakeMountName(volume.Label, volume.Uuid);
            string diskPath = DiskPath(volume.DiskNumber);
            var args = new List<string> { "--mount", diskPath };
            if (volume.PartitionNumber > 0)
            {
                args.Add("--partition");
                args.Add(volume.PartitionNumber.ToString(CultureInfo.InvariantCulture));
            }
            args.AddRange(new[] { "--type", "btrfs", "--name", name });
            if (!string.IsNullOrWhiteSpace(options))
            {
                args.Add("--options");
                args.Add(options.Trim());
            }

            string partText = volume.PartitionNumber > 0 ? "partition " + volume.PartitionNumber : "whole disk";
            Log.Info(string.Format("Mounting '{0}' (disk {1}, {2}) as '{3}' via {4}...", volume.DisplayLabel, volume.DiskNumber, partText, name, distro));
            StartKeepAlive(distro);

            WslResult r = await Wsl.RunAsync(args, 180, ct).ConfigureAwait(false);
            if (r.ExitCode != 0)
            {
                string msg = r.Combined;
                Log.Error(string.Format("wsl --mount failed (exit {0}): {1}", r.ExitCode, msg));
                await WriteMountFailureHintsAsync(msg, diskPath, distro, ct).ConfigureAwait(false);
                if (state.MountCount == 0) StopKeepAlive();
                return null;
            }

            var entry = new MountEntry
            {
                Key = volume.Key,
                DiskUniqueId = volume.DiskUniqueId,
                DiskNumber = volume.DiskNumber,
                PartitionNumber = volume.PartitionNumber,
                Name = name,
                Label = volume.Label,
                Uuid = volume.Uuid,
                Model = volume.Model,
                Distro = distro,
                Options = options ?? string.Empty,
                MountedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture)
            };
            state.AddMount(entry);
            Log.Ok(string.Format("Mounted at /mnt/wsl/{0}   Windows: {1}", name, WindowsPath(distro, name)));
            return entry;
        }

        private static async Task WriteMountFailureHintsAsync(string msg, string diskPath, string distro, CancellationToken ct)
        {
            if (Regex.IsMatch(msg, "0x80070020|in use|being used by another process", RegexOptions.IgnoreCase))
            {
                Log.Warn("Hint: Windows is still using this disk. Close Explorer windows or apps using other partitions on it, then retry.");
            }
            else if (Regex.IsMatch(msg, "0x8007000f|cannot find the drive|not supported", RegexOptions.IgnoreCase))
            {
                Log.Warn("Hint: wsl --mount does not support USB flash drives or SD card readers, only disks. " +
                         "USB hard drives in SATA enclosures normally work. See the README (usbipd-win) for flash sticks.");
            }
            else if (Regex.IsMatch(msg, "failed to mount|attached but", RegexOptions.IgnoreCase))
            {
                Log.Warn("Hint: the disk attached but btrfs refused to mount it. Last kernel messages:");
                try
                {
                    WslResult dm = await Wsl.RunRootAsync(distro, "dmesg | tail -n 8", 30, ct).ConfigureAwait(false);
                    foreach (string l in dm.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        Log.Warn("    " + l);
                    }
                }
                catch
                {
                    // diagnostics only
                }
                try { await Wsl.RunAsync(new[] { "--unmount", diskPath }, 60, ct).ConfigureAwait(false); } catch { }
                Log.Info("Disk detached again so Windows can use it.");
            }
            else if (Regex.IsMatch(msg, "unknown filesystem", RegexOptions.IgnoreCase))
            {
                Log.Warn("Hint: run \"wsl --update\" to get a current WSL kernel with btrfs support.");
            }
            else if (Regex.IsMatch(msg, "already", RegexOptions.IgnoreCase))
            {
                Log.Warn("Hint: the disk seems to be attached already. Try Refresh, or \"wsl --unmount\" and mount again.");
            }
        }

        // ---------------------------------------------------------------------------------------
        //  Eject
        // ---------------------------------------------------------------------------------------
        private static bool IsDiskPresent(string diskKey)
        {
            try
            {
                return Storage.GetDisks().Any(d => d.Key == diskKey);
            }
            catch
            {
                return true;   // can't tell: assume present and flush
            }
        }

        /// <summary>
        /// Flushes (with visible progress, cancellable) and detaches. Returns false if the drive is still
        /// mounted afterwards (cancelled or failed).
        /// </summary>
        public async Task<bool> DismountAsync(VolumeInfo volume, CancellationToken ct)
        {
            MountEntry entry = state.FindMount(volume.Key);
            if (entry == null)
            {
                Log.Warn(string.Format("'{0}' was not mounted by this tool.", volume.DisplayLabel));
                return false;
            }
            int siblings = state.Mounts.Count(m => m.DiskUniqueId == entry.DiskUniqueId);
            string diskPath = DiskPath(volume.DiskNumber);
            bool present = IsDiskPresent(entry.DiskUniqueId);

            if (present)
            {
                try
                {
                    await Wsl.RunRootAsync(entry.Distro, "btrfs scrub cancel /mnt/wsl/" + entry.Name + " >/dev/null 2>&1; true", 20, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // no scrub running, or btrfs-progs missing
                }

                // sync -f flushes only this filesystem; the loop reports the remaining dirty data
                Log.Info(string.Format("Flushing pending writes for '{0}' (after large copies this can take a while)...", entry.Name));
                string flush = ("sync -f /mnt/wsl/__NAME__ & p=$!; n=0; " +
                                "while kill -0 $p 2>/dev/null; do n=$((n+1)); " +
                                "if [ $((n % 3)) -eq 0 ]; then m=$(awk '/^(Dirty|Writeback):/{s+=$2} END{print int(s/1024)}' /proc/meminfo); " +
                                "echo Still writing about $m MB to the drive...; fi; sleep 1; done; wait $p").Replace("__NAME__", entry.Name);
                StreamResult f = await Wsl.RunStreamingToLogAsync(Wsl.RootArgs(entry.Distro, flush), TimeSpan.FromHours(1), ct)
                    .ConfigureAwait(false);
                if (f.Stopped)
                {
                    Log.Warn(string.Format("Eject stopped while data was still being written. '{0}' is STILL MOUNTED - do not unplug it; eject again when ready.", entry.Name));
                    return false;
                }
                if (f.ExitCode != 0)
                {
                    Log.Warn(string.Format("Flushing reported an error (exit {0}). If the drive was unplugged meanwhile, files written just before may be incomplete.", f.ExitCode));
                }
            }
            else
            {
                Log.Warn("The drive is no longer connected, so there is nothing to flush. Releasing it from WSL...");
            }

            Log.Info(string.Format("Detaching disk {0} from WSL...", volume.DiskNumber));
            WslResult r;
            try
            {
                r = await Wsl.RunAsync(new[] { "--unmount", diskPath }, 90, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                r = new WslResult { ExitCode = -1, Output = string.Empty, Error = ex.Message };
            }
            if (r.ExitCode != 0)
            {
                string msg = r.Combined;
                if (!present || volume.Disconnected || Regex.IsMatch(msg, "not attached|not found|0x80070002|0x8007000f", RegexOptions.IgnoreCase))
                {
                    Log.Warn(string.Format("WSL could not release the removed disk cleanly ({0}). Cleaning up the entry. " +
                                           "If WSL misbehaves afterwards, run 'wsl --shutdown' once.", msg));
                }
                else
                {
                    Log.Error("wsl --unmount failed: " + msg);
                    Log.Warn("Hint: close any Explorer window, terminal or app that has files open on the drive, then retry.");
                    return false;
                }
            }

            state.RemoveMounts(m => m.DiskUniqueId == entry.DiskUniqueId);
            if (state.MountCount == 0) StopKeepAlive();
            cache.InvalidateDisk(entry.DiskUniqueId);
            if (siblings > 1)
            {
                Log.Info(string.Format("WSL detaches whole disks, so {0} mounts on this disk were released.", siblings));
            }
            if (present) Log.Ok(string.Format("Disk {0} detached. It is now safe to eject the drive.", volume.DiskNumber));
            else Log.Warn("Entry cleaned up. The drive was unplugged while mounted: next time it is connected, run a scrub or an offline check.");
            return true;
        }

        public async Task DismountAllAsync(IList<VolumeInfo> volumes, CancellationToken ct)
        {
            var done = new HashSet<string>(StringComparer.Ordinal);
            int count = 0;
            foreach (VolumeInfo v in volumes.Where(x => x.Mounted))
            {
                if (!done.Add(v.DiskUniqueId)) continue;
                ct.ThrowIfCancellationRequested();
                count++;
                await DismountAsync(v, ct).ConfigureAwait(false);
            }
            if (count == 0) Log.Info("Nothing is mounted.");
        }

        // ---------------------------------------------------------------------------------------
        //  Free space of mounted drives (one df call for all of them)
        // ---------------------------------------------------------------------------------------
        public async Task<Dictionary<string, SpaceFigures>> GetMountedSpaceAsync(IList<VolumeInfo> rows, CancellationToken ct)
        {
            var space = new Dictionary<string, SpaceFigures>(StringComparer.Ordinal);
            List<VolumeInfo> targets = rows.Where(v => v.Mounted && !v.Disconnected && !string.IsNullOrEmpty(v.MountName)).ToList();
            if (targets.Count == 0) return space;
            var args = new List<string> { "-d", targets[0].Distro, "--exec", "df", "-B1", "--output=target,size,used,avail" };
            args.AddRange(targets.Select(t => "/mnt/wsl/" + t.MountName));
            WslResult r;
            try
            {
                r = await Wsl.RunAsync(args, 20, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return space;
            }
            var line = new Regex(@"^/mnt/wsl/(\S+)\s+(\d+)\s+(\d+)\s+(\d+)\s*$");
            foreach (string l in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = line.Match(l);
                if (!m.Success) continue;
                space[m.Groups[1].Value] = new SpaceFigures
                {
                    Size = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                    Used = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
                    Avail = double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture)
                };
            }
            return space;
        }

        public static async Task OpenInExplorerAsync(string distro, string mountName)
        {
            string path = WindowsPath(distro, mountName);
            for (int i = 0; i < 10 && !Directory.Exists(path); i++)
            {
                await Task.Delay(500).ConfigureAwait(false);
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
    }

    /// <summary>Finds btrfs filesystems on (USB) disks and merges them with the saved mount state.</summary>
    public sealed class VolumeScanner
    {
        private readonly StateStore state;
        private readonly MountManager mounts;
        private readonly SuperblockCache cache;

        public VolumeScanner(StateStore state, MountManager mounts, SuperblockCache cache)
        {
            this.state = state;
            this.mounts = mounts;
            this.cache = cache;
        }

        public SuperblockCache Cache { get { return cache; } }

        /// <summary>Cheap: disk identities only, never touches the drives.</summary>
        public List<string> GetSnapshot(bool includeAllDisks)
        {
            return Storage.GetDisks().Where(d => d.InScope(includeAllDisks)).Select(d => d.Key).ToList();
        }

        public async Task<ScanResult> ScanAsync(bool includeAllDisks, CancellationToken ct)
        {
            List<DiskRecord> allDisks = Storage.GetDisks();
            List<MountEntry> saved = state.Mounts;
            cache.RetainDisks(new HashSet<string>(allDisks.Select(d => d.Key), StringComparer.Ordinal));

            var rows = new List<VolumeInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (DiskRecord d in allDisks.Where(x => x.InScope(includeAllDisks)))
            {
                ct.ThrowIfCancellationRequested();
                List<PartitionRecord> parts = new List<PartitionRecord>();
                if (d.PartitionStyle != 0)
                {
                    try { parts = Storage.GetPartitions(d.Number); } catch { parts = new List<PartitionRecord>(); }
                }
                string letters = string.Join(" ", parts.Where(p => p.DriveLetter != '\0').Select(p => p.DriveLetter + ":"));

                // btrfs written straight onto the disk ("mkfs.btrfs /dev/sdX") has no partition table
                List<PartitionRecord> targets = parts.Count > 0
                    ? parts
                    : new List<PartitionRecord> { new PartitionRecord { Number = 0, Offset = 0, Size = d.Size } };

                foreach (PartitionRecord t in targets)
                {
                    string key = d.Key + "|" + t.Number.ToString(CultureInfo.InvariantCulture);
                    MountEntry m = saved.FirstOrDefault(x => x.Key == key);
                    Superblock sb = m == null ? cache.GetOrRead(key, d.Number, t.Offset) : null;
                    if (m == null && sb == null) continue;

                    var row = new VolumeInfo
                    {
                        Key = key,
                        DiskNumber = d.Number,
                        DiskUniqueId = d.Key,
                        Model = d.FriendlyName,
                        DiskSize = d.Size,
                        PartitionNumber = t.Number,
                        PartitionSize = t.Size,
                        DriveLetters = letters,
                        Mount = m,
                        SpaceApprox = true
                    };
                    if (sb != null)
                    {
                        row.Label = sb.Label;
                        row.Uuid = sb.Uuid;
                        row.NumDevices = sb.NumDevices;
                        row.SpaceTotal = sb.TotalBytes;
                        row.SpaceUsed = sb.BytesUsed;
                        row.SpaceFree = Math.Max(0, sb.TotalBytes - sb.BytesUsed);
                    }
                    else
                    {
                        row.Label = m.Label;
                        row.Uuid = m.Uuid;
                        row.NumDevices = 1;
                    }
                    rows.Add(row);
                    seen.Add(key);
                }
            }

            // mounted entries whose disk no longer enumerates normally (e.g. unplugged while mounted)
            foreach (MountEntry m in saved.Where(x => !seen.Contains(x.Key)))
            {
                DiskRecord d = allDisks.FirstOrDefault(x => x.Key == m.DiskUniqueId);
                rows.Add(new VolumeInfo
                {
                    Key = m.Key,
                    DiskNumber = d != null ? d.Number : m.DiskNumber,
                    DiskUniqueId = m.DiskUniqueId,
                    Model = m.Model,
                    PartitionNumber = m.PartitionNumber,
                    Label = m.Label,
                    Uuid = m.Uuid,
                    NumDevices = 1,
                    Mount = m,
                    Disconnected = d == null,
                    DriveLetters = string.Empty,
                    SpaceApprox = true
                });
            }

            // exact figures for mounted drives come from df inside WSL
            Dictionary<string, SpaceFigures> space = await mounts.GetMountedSpaceAsync(rows, ct).ConfigureAwait(false);
            foreach (VolumeInfo row in rows.Where(r => r.Mounted))
            {
                SpaceFigures sp;
                if (!space.TryGetValue(row.MountName, out sp)) continue;
                row.SpaceTotal = sp.Size;
                row.SpaceUsed = sp.Used;
                row.SpaceFree = sp.Avail;
                row.SpaceApprox = false;
            }

            return new ScanResult
            {
                Volumes = rows,
                Snapshot = allDisks.Where(x => x.InScope(includeAllDisks)).Select(x => x.Key).ToList()
            };
        }
    }
}
