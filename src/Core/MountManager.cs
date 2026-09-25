// Btrfs USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

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
        private readonly FsSupport support;

        public MountManager(StateStore state, SuperblockCache cache, FsSupport support)
        {
            this.state = state;
            this.cache = cache;
            this.support = support;
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
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Keep-alive PID {0} is now '{1}', not the keep-alive.", pid, p.ProcessName));
                p.Dispose();
            }
            catch
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Keep-alive PID {0} is no longer running.", pid));
            }
            return null;
        }

        public void StartKeepAlive(string distro)
        {
            using (Process existing = GetKeepAliveProcess())
            {
                if (existing != null)
                {
                    Log.Debug(string.Format(CultureInfo.InvariantCulture, "Keep-alive already running (PID {0}).", existing.Id));
                    return;
                }
            }
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.WslExe,
                Arguments = Wsl.JoinArguments(new[] { "-d", distro, "-u", "root", "--exec", "sleep", "infinity" }),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Log.Debug("Starting keep-alive: wsl.exe " + psi.Arguments);
            using (Process p = Process.Start(psi))
            {
                if (p == null)
                {
                    Log.Warn("Keep-alive process did not start; WSL may shut down when idle and drop mounted disks.");
                    return;
                }
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

            Log.Debug("Sync: saved mounts " + string.Join(", ", mounts.Select(m => string.Format(CultureInfo.InvariantCulture,
                "'{0}' (disk {1} part {2}, {3})", m.Name, m.DiskNumber, m.PartitionNumber, m.Distro))));
            List<string> running = await DistroService.RunningAsync(ct).ConfigureAwait(false);
            Log.Debug("Sync: running distros: " + (running.Count == 0 ? "(none)" : string.Join(", ", running)));
            if (running.Count == 0)
            {
                Log.Warn("WSL is not running, so previously mounted disks were detached. Clearing stale entries.");
                state.ReplaceMounts(new MountEntry[0]);
                StopKeepAlive();
                return;
            }

            WslResult r = await Wsl.RunAsync(new[] { "-d", running[0], "-u", "root", "--exec", "cat", "/proc/mounts" }, 30, ct)
                .ConfigureAwait(false);
            if (r.ExitCode != 0)
            {
                Log.Debug("Sync: could not read /proc/mounts; keeping the saved mount list unchanged.");
                return;
            }

            var points = new HashSet<string>(
                r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Split(' '))
                    .Where(f => f.Length > 1)
                    .Select(f => f[1]),
                StringComparer.Ordinal);
            Log.Debug("Sync: mount points under /mnt/wsl: " +
                string.Join(", ", points.Where(x => x.StartsWith("/mnt/wsl/", StringComparison.Ordinal)).DefaultIfEmpty("(none)")));

            // ZFS datasets mount wherever their mountpoint says, so a pool counts as mounted while it is imported
            var pools = new HashSet<string>(StringComparer.Ordinal);
            if (mounts.Any(m => m.Method == MountMethod.ZfsPool))
            {
                WslResult z = await Wsl.RunAsync(new[] { "-d", running[0], "-u", "root", "--exec", "sh", "-c", "zpool list -H -o name 2>/dev/null; true" }, 30, ct)
                    .ConfigureAwait(false);
                foreach (string p in z.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) pools.Add(p.Trim());
                Log.Debug("Sync: imported ZFS pools: " + (pools.Count == 0 ? "(none)" : string.Join(", ", pools)));
            }

            var keep = new List<MountEntry>();
            foreach (MountEntry m in mounts)
            {
                if (m.Method == MountMethod.ZfsPool ? pools.Contains(m.Name) : points.Contains("/mnt/wsl/" + m.Name)) keep.Add(m);
                else Log.Warn(string.Format("'{0}' is no longer mounted in WSL; removing it from the list.", m.Name));
            }
            state.ReplaceMounts(keep);
            if (keep.Count == 0) StopKeepAlive();
            else StartKeepAlive(keep[0].Distro);
        }

        // ---------------------------------------------------------------------------------------
        //  Mount
        // ---------------------------------------------------------------------------------------
        public string MakeMountName(string label, string uuid, FsKind kind)
        {
            string baseName = Regex.Replace(label ?? string.Empty, @"[^A-Za-z0-9._-]", "_").Trim('_', '.');
            if (baseName.Length == 0)
            {
                string id = Regex.Replace(uuid ?? string.Empty, "[^A-Za-z0-9]", string.Empty);
                baseName = FsTypes.Name(kind) + "-" + (id.Length > 0 ? id.Substring(0, Math.Min(8, id.Length)) : "disk");
            }
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

        /// <summary>
        /// Why this volume cannot be mounted with the given distro, or null when it can (or support is not
        /// known yet: then the mount itself finds out). Refreshes the support check once when it said no,
        /// in case a kernel or tool was installed since.
        /// </summary>
        public async Task<string> CheckMountableAsync(VolumeInfo volume, string distro, CancellationToken ct)
        {
            FsKind kind = volume.Kind;
            if (FsTypes.Method(kind) == MountMethod.None) return FsTypes.NotMountableReason(kind);
            await support.EnsureAsync(distro, ct).ConfigureAwait(false);
            FsAvailability a = support.Get(distro, kind);
            if (a == FsAvailability.NoDriver || a == FsAvailability.NeedsTools)
            {
                support.Forget(distro);
                await support.RefreshAsync(distro, ct).ConfigureAwait(false);
                a = support.Get(distro, kind);
            }
            return FsSupport.Explain(kind, a);
        }

        public async Task<MountEntry> MountAsync(VolumeInfo volume, string distro, string options, CancellationToken ct)
        {
            if (volume.Mounted)
            {
                Log.Warn(string.Format("'{0}' is already mounted.", volume.DisplayLabel));
                return null;
            }
            FsKind kind = volume.Kind;
            string reason = await CheckMountableAsync(volume, distro, ct).ConfigureAwait(false);
            if (reason != null)
            {
                Log.Error(string.Format("Cannot mount '{0}' ({1}): {2}", volume.DisplayLabel, volume.KindName, reason));
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
            if (!string.IsNullOrEmpty(volume.FsNote)) Log.Warn("Note: " + volume.FsNote);

            // the options box holds btrfs options (compress=zstd, subvol=...); other filesystems would reject them
            string mountOptions = kind == FsKind.Btrfs && !string.IsNullOrWhiteSpace(options) ? options.Trim() : string.Empty;
            if (kind != FsKind.Btrfs && !string.IsNullOrWhiteSpace(options))
            {
                Log.Debug("Mount options '" + options.Trim() + "' are for btrfs; not used for " + volume.KindName + ".");
            }

            bool apfsWrite = state.Settings.ApfsWrite;
            MountMethod method = support.MethodFor(distro, kind, apfsWrite);
            bool readOnly = WillBeReadOnly(volume, distro);
            string name;
            if (method == MountMethod.ZfsPool)
            {
                // datasets mount at /mnt/wsl/<their mountpoint>, so the pool name is the folder name
                name = volume.Label;
                if (string.IsNullOrEmpty(name) || state.IsNameInUse(name))
                {
                    Log.Error(string.IsNullOrEmpty(name) ? "The ZFS pool name could not be read from the disk."
                        : "A mount named '" + name + "' already exists; eject it before importing this pool.");
                    return null;
                }
            }
            else
            {
                name = MakeMountName(volume.Label, volume.Uuid, kind);
            }
            string diskPath = DiskPath(volume.DiskNumber);
            string partText = volume.PartitionNumber > 0 ? "partition " + volume.PartitionNumber : "whole disk";
            if (kind == FsKind.Apfs && method == MountMethod.Kernel)
            {
                mountOptions = apfsWrite ? "readwrite" : string.Empty;   // linux-apfs-rw mounts read-only unless asked
                if (apfsWrite) Log.Warn("APFS write support is EXPERIMENTAL (linux-apfs-rw). Keep a backup of this drive.");
            }
            if (kind == FsKind.Ufs)
            {
                // the driver cannot tell the UFS variants apart (ufstype); wsl --mount turns "ro" into the read-only flag
                mountOptions = (readOnly ? "ro," : string.Empty) + volume.FsOptions;
                if (!readOnly)
                {
                    Log.Warn("UFS write support is EXPERIMENTAL (the Linux ufs driver). Keep a backup of this drive, and eject it " +
                             "before unplugging.");
                    if (!string.IsNullOrEmpty(volume.FsWriteNote)) Log.Warn("Note: " + volume.FsWriteNote);
                }
                else if (state.Settings.UfsWrite && !volume.ReadOnly)
                {
                    Log.Warn("The UFS driver in " + distro + " was not built by Tools > Build filesystem drivers, so it lacks the " +
                             "changes that keep the filesystem consistent for FreeBSD; mounting read-only. Build the drivers to write.");
                }
            }
            Log.Info(string.Format("Mounting {0} '{1}' (disk {2}, {3}) as '{4}' via {5}{6}...", volume.KindName, volume.DisplayLabel,
                volume.DiskNumber, partText, name, distro, readOnly ? ", read-only" : string.Empty));
            Log.Debug("Mount method: " + method + (mountOptions.Length > 0 ? ", options " + mountOptions : string.Empty));
            StartKeepAlive(distro);

            bool ok;
            if (method == MountMethod.ApfsFuse) ok = await MountApfsAsync(volume, distro, name, diskPath, ct).ConfigureAwait(false);
            else if (method == MountMethod.ZfsPool) ok = await MountZfsAsync(volume, distro, name, diskPath, ct).ConfigureAwait(false);
            else ok = await MountKernelAsync(volume, distro, name, diskPath, mountOptions, ct).ConfigureAwait(false);
            if (!ok)
            {
                if (state.MountCount == 0) StopKeepAlive();
                return null;
            }
            // not cancellable: the drive is mounted now and must be recorded below
            if (method == MountMethod.Kernel && !readOnly && await MountedReadOnlyAsync(distro, name, CancellationToken.None).ConfigureAwait(false))
            {
                // e.g. UFS or ext4 that needs a check: the kernel falls back to read-only instead of failing
                readOnly = true;
                Log.Warn(string.Format("The {0} driver mounted '{1}' read-only instead of read/write. Last kernel messages:", volume.KindName, name));
                await LogKernelMessagesAsync(distro, CancellationToken.None).ConfigureAwait(false);
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
                Options = mountOptions,
                MountedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                FsType = FsTypes.Name(kind),
                ReadOnly = readOnly,
                MountMethod = MountEntry.MethodName(method)
            };
            state.AddMount(entry);
            Log.Ok(string.Format("Mounted at /mnt/wsl/{0}{1}   Windows: {2}", name, readOnly ? " (read-only)" : string.Empty, WindowsPath(distro, name)));
            return entry;
        }

        /// <summary>Whether mounting this volume with this distro gives read-only access.</summary>
        public bool WillBeReadOnly(VolumeInfo volume, string distro)
        {
            if (volume.Mounted) return volume.Mount.ReadOnly;
            if (volume.Kind == FsKind.Apfs)
            {
                return !(state.Settings.ApfsWrite && support.MethodFor(distro, FsKind.Apfs, true) == MountMethod.Kernel);
            }
            if (volume.Kind == FsKind.Ufs)
            {
                return volume.ReadOnly || !(state.Settings.UfsWrite && support.CanWriteUfs(distro));
            }
            return volume.ReadOnly;
        }

        /// <summary>Whether /mnt/wsl/name is mounted read-only (a failed check counts as no).</summary>
        private static async Task<bool> MountedReadOnlyAsync(string distro, string name, CancellationToken ct)
        {
            try
            {
                WslResult r = await Wsl.RunRootAsync(distro, "awk '$2 == \"/mnt/wsl/" + name + "\" { print $4 }' /proc/mounts", 30, ct)
                    .ConfigureAwait(false);
                string options = r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                return r.ExitCode == 0 && options != null && options.Trim().Split(',')[0] == "ro";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.DebugException("Checking whether the mount is read-only failed", ex);
                return false;
            }
        }

        private static async Task LogKernelMessagesAsync(string distro, CancellationToken ct)
        {
            try
            {
                WslResult dm = await Wsl.RunRootAsync(distro, "dmesg | tail -n 8", 30, ct).ConfigureAwait(false);
                foreach (string l in dm.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) Log.Warn("    " + l);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.DebugException("dmesg failed", ex);   // diagnostics only
            }
        }

        /// <summary>
        /// Loads the driver module; built-in drivers need nothing. A WSL restart unloads locally built modules
        /// and also removes them from /lib/modules, so they are restored from the distro's disk first.
        /// </summary>
        private static async Task LoadModuleAsync(string distro, string module, CancellationToken ct)
        {
            WslResult r = await Wsl.RunRootAsync(distro, FsSupport.RestoreModules +
                "grep -qw " + module + " /proc/filesystems || modprobe " + module + " 2>&1", 60, ct).ConfigureAwait(false);
            if (r.ExitCode != 0) Log.Warn("Loading the " + module + " driver failed: " + r.Combined);
        }

        /// <summary>wsl --mount --type: the WSL kernel mounts the filesystem at /mnt/wsl/name.</summary>
        private async Task<bool> MountKernelAsync(VolumeInfo volume, string distro, string name, string diskPath, string options, CancellationToken ct)
        {
            await LoadModuleAsync(distro, FsTypes.Name(volume.Kind), ct).ConfigureAwait(false);
            var args = new List<string> { "--mount", diskPath };
            if (volume.PartitionNumber > 0)
            {
                args.Add("--partition");
                args.Add(volume.PartitionNumber.ToString(CultureInfo.InvariantCulture));
            }
            args.AddRange(new[] { "--type", FsTypes.Name(volume.Kind), "--name", name });
            if (!string.IsNullOrEmpty(options))
            {
                args.Add("--options");
                args.Add(options);
            }
            WslResult r = await Wsl.RunAsync(args, 180, ct).ConfigureAwait(false);
            if (r.ExitCode == 0) return true;
            string msg = r.Combined;
            Log.Error(string.Format("wsl --mount failed (exit {0}): {1}", r.ExitCode, msg));
            await WriteMountFailureHintsAsync(msg, diskPath, distro, volume.Kind, ct).ConfigureAwait(false);
            return false;
        }

        /// <summary>
        /// APFS: attach the disk without mounting, find the partition inside WSL by its container UUID,
        /// then mount it read-only with fsapfsmount (FUSE; it keeps running in the background).
        /// </summary>
        private async Task<bool> MountApfsAsync(VolumeInfo volume, string distro, string name, string diskPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(volume.Uuid))
            {
                Log.Error("This APFS container has no UUID, so it cannot be found inside WSL.");
                return false;
            }
            WslResult a = await Wsl.RunAsync(new[] { "--mount", diskPath, "--bare" }, 180, ct).ConfigureAwait(false);
            if (a.ExitCode != 0)
            {
                Log.Error(string.Format("wsl --mount --bare failed (exit {0}): {1}", a.ExitCode, a.Combined));
                await WriteMountFailureHintsAsync(a.Combined, diskPath, distro, volume.Kind, ct).ConfigureAwait(false);
                return false;
            }
            bool mounted = false;
            try
            {
                string device = await FindDeviceByUuidAsync(distro, volume.Uuid, ct).ConfigureAwait(false);
                if (device == null)
                {
                    Log.Error("The disk attached, but its APFS partition did not show up inside WSL.");
                    return false;
                }
                string mp = "/mnt/wsl/" + name;
                WslResult m = await Wsl.RunRootAsync(distro,
                    "mkdir -p " + mp + " && fsapfsmount -X allow_other " + device + " " + mp + " 2>&1", 120, ct).ConfigureAwait(false);
                if (m.ExitCode != 0)
                {
                    Log.Error(string.Format("fsapfsmount failed (exit {0}): {1}", m.ExitCode, m.Combined));
                    try { await Wsl.RunRootAsync(distro, "rmdir " + mp + " 2>/dev/null; true", 30, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { Log.DebugException("Removing the empty mount point failed", ex); }
                    return false;
                }
                mounted = true;
                try
                {
                    WslResult info = await Wsl.RunRootAsync(distro, "fsapfsinfo " + device + " 2>&1 | grep -E '^\\s*Name\\s*:'", 60, ct).ConfigureAwait(false);
                    List<string> names = info.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Substring(l.IndexOf(':') + 1).Trim()).Where(l => l.Length > 0).ToList();
                    if (names.Count == 1) Log.Info("APFS volume: " + names[0]);
                    else if (names.Count > 1) Log.Info("APFS volumes (one folder each, apfs1, apfs2, ...): " + string.Join(", ", names));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.DebugException("Reading APFS volume names failed", ex);
                }
                return true;
            }
            finally
            {
                if (!mounted)
                {
                    try { await Wsl.RunAsync(new[] { "--unmount", diskPath }, 90, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { Log.DebugException("Detaching after the failed APFS mount failed", ex); }
                    Log.Info("Disk detached again so Windows can use it.");
                }
            }
        }

        /// <summary>
        /// ZFS: attach the disk without mounting, then import the pool by GUID with /mnt/wsl as the
        /// alternate root, so its datasets appear under /mnt/wsl/&lt;mountpoint&gt;. Never forces an import:
        /// a pool still marked as in use by another system must be exported there first.
        /// </summary>
        private async Task<bool> MountZfsAsync(VolumeInfo volume, string distro, string name, string diskPath, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(volume.Uuid))
            {
                Log.Error("The ZFS pool GUID could not be read from the disk.");
                return false;
            }
            WslResult a = await Wsl.RunAsync(new[] { "--mount", diskPath, "--bare" }, 180, ct).ConfigureAwait(false);
            if (a.ExitCode != 0)
            {
                Log.Error(string.Format("wsl --mount --bare failed (exit {0}): {1}", a.ExitCode, a.Combined));
                await WriteMountFailureHintsAsync(a.Combined, diskPath, distro, volume.Kind, ct).ConfigureAwait(false);
                return false;
            }
            bool imported = false;
            try
            {
                string device = await FindDeviceByUuidAsync(distro, volume.Uuid, ct).ConfigureAwait(false);
                if (device == null)
                {
                    Log.Error("The disk attached, but its ZFS partition did not show up inside WSL.");
                    return false;
                }
                await LoadModuleAsync(distro, "zfs", ct).ConfigureAwait(false);
                WslResult m = await Wsl.RunRootAsync(distro,
                    "zpool import -d " + device + " -R /mnt/wsl -o cachefile=none " + volume.Uuid + " 2>&1", 600, ct).ConfigureAwait(false);
                if (m.ExitCode != 0)
                {
                    Log.Error(string.Format("zpool import failed (exit {0}): {1}", m.ExitCode, m.Combined));
                    if (Regex.IsMatch(m.Combined, "in use from another system|was previously in use|-f", RegexOptions.IgnoreCase))
                    {
                        Log.Warn("Hint: the pool was not exported on the computer that used it last. Export it there " +
                                 "(zpool export " + name + "), or, if that computer is gone, import it once by hand with " +
                                 "\"zpool import -f\" inside WSL.");
                    }
                    else if (Regex.IsMatch(m.Combined, "missing|cannot import.*one or more devices|UNAVAIL", RegexOptions.IgnoreCase))
                    {
                        Log.Warn("Hint: this pool spans several disks; attach all of them first.");
                    }
                    return false;
                }
                imported = true;
                WslResult list = await Wsl.RunRootAsync(distro, "zfs list -H -o name,mountpoint,mounted,keystatus -r " + name + " 2>&1", 60, ct)
                    .ConfigureAwait(false);
                foreach (string l in list.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = l.Split('\t');
                    if (f.Length < 3) continue;
                    if (f.Length > 3 && f[3] == "unavailable") Log.Warn("    " + f[0] + ": encrypted, key not loaded (use zfs load-key inside WSL)");
                    else Log.Info(string.Format("    {0}: {1}", f[0], f[2] == "yes" ? "mounted at " + f[1] : "not mounted (" + f[1] + ")"));
                }
                return true;
            }
            finally
            {
                if (!imported)
                {
                    try { await Wsl.RunAsync(new[] { "--unmount", diskPath }, 90, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { Log.DebugException("Detaching after the failed ZFS import failed", ex); }
                    Log.Info("Disk detached again so Windows can use it.");
                }
            }
        }

        /// <summary>The /dev node inside WSL whose filesystem has this UUID (blkid, no cache); null if it never appears.</summary>
        public static async Task<string> FindDeviceByUuidAsync(string distro, string uuid, CancellationToken ct)
        {
            for (int i = 0; i < 20; i++)
            {
                WslResult b = await Wsl.RunRootAsync(distro, "blkid -c /dev/null -U " + uuid, 30, ct).ConfigureAwait(false);
                string first = b.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(l => Regex.IsMatch(l.Trim(), @"^/dev/\S+$"));
                if (b.ExitCode == 0 && first != null)
                {
                    Log.Debug("UUID " + uuid + " is " + first.Trim() + " inside WSL.");
                    return first.Trim();
                }
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            return null;
        }

        private static async Task WriteMountFailureHintsAsync(string msg, string diskPath, string distro, FsKind kind, CancellationToken ct)
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
            else if (Regex.IsMatch(msg, "unknown filesystem|wrong fs type|no such device", RegexOptions.IgnoreCase) && kind != FsKind.Btrfs)
            {
                Log.Warn("Hint: " + FsTypes.NoDriverHint(kind) + " If the kernel does have the driver, the filesystem may use " +
                         "features newer than the WSL kernel supports; the kernel messages below say which.");
                await LogKernelMessagesAsync(distro, ct).ConfigureAwait(false);
            }
            else if (Regex.IsMatch(msg, "failed to mount|attached but", RegexOptions.IgnoreCase))
            {
                Log.Warn(string.Format("Hint: the disk attached but {0} refused to mount it. Last kernel messages:", FsTypes.DisplayName(kind)));
                await LogKernelMessagesAsync(distro, ct).ConfigureAwait(false);
                try { await Wsl.RunAsync(new[] { "--unmount", diskPath }, 60, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.DebugException("Detaching after the failed mount failed", ex); }
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
                bool present = Storage.GetDisks().Any(d => d.Key == diskKey);
                Log.Debug("Disk " + diskKey + (present ? " is present." : " is NOT present (unplugged?)."));
                return present;
            }
            catch (Exception ex)
            {
                Log.DebugException("Could not enumerate disks to check presence; assuming present", ex);
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

            FsKind kind = entry.Kind;
            if (entry.Method == MountMethod.ZfsPool)
            {
                if (present)
                {
                    // export unmounts every dataset and writes everything out; cancelling leaves the pool imported
                    Log.Info(string.Format("Exporting ZFS pool '{0}' (flushes and unmounts all datasets)...", entry.Name));
                    StreamResult x = await Wsl.RunStreamingToLogAsync(Wsl.RootArgs(entry.Distro, "zpool export " + entry.Name + " 2>&1"),
                        TimeSpan.FromHours(1), ct).ConfigureAwait(false);
                    WslResult still = await Wsl.RunRootAsync(entry.Distro, "zpool list -H -o name " + entry.Name + " 2>/dev/null", 60,
                        CancellationToken.None).ConfigureAwait(false);
                    if (still.ExitCode == 0 && still.Output.Trim() == entry.Name)
                    {
                        Log.Warn(x.Stopped
                            ? string.Format("Eject stopped. Pool '{0}' is STILL IMPORTED - do not unplug it; eject again when ready.", entry.Name)
                            : string.Format("zpool export failed (exit {0}); the pool is still imported. Close anything using files under /mnt/wsl, then retry.", x.ExitCode));
                        return false;
                    }
                }
                else
                {
                    Log.Warn("The drive is no longer connected. Releasing the pool from WSL...");
                    try { await Wsl.RunRootAsync(entry.Distro, "zpool export -f " + entry.Name + " 2>&1; true", 120, ct).ConfigureAwait(false); }
                    catch (Exception ex) { Log.DebugException("Forced export of the missing pool failed", ex); }
                }
            }
            else if (entry.Method == MountMethod.ApfsFuse)
            {
                // FUSE mount inside the distro: unmount it first, or detaching the disk fails with "in use"
                Log.Info(string.Format("Unmounting the APFS volume '{0}' (read-only, nothing to flush)...", entry.Name));
                WslResult u = await Wsl.RunRootAsync(entry.Distro,
                    "umount /mnt/wsl/" + entry.Name + " 2>&1 || umount -l /mnt/wsl/" + entry.Name + " 2>&1; rmdir /mnt/wsl/" + entry.Name + " 2>/dev/null; true",
                    60, ct).ConfigureAwait(false);
                if (u.Output.Length > 0) Log.Debug("APFS unmount said: " + u.Output);
            }
            else if (present && entry.ReadOnly)
            {
                Log.Info(string.Format("'{0}' is mounted read-only, so there is nothing to flush.", entry.Name));
            }
            else if (present)
            {
                if (kind == FsKind.Btrfs)
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
                    catch (Exception ex)
                    {
                        // no scrub running, or btrfs-progs missing
                        Log.DebugException("scrub cancel before eject failed (ignored)", ex);
                    }
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
            List<VolumeInfo> mounted = rows.Where(v => v.Mounted && !v.Disconnected && !string.IsNullOrEmpty(v.MountName)).ToList();
            // a ZFS pool's folder is not one filesystem; ask ZFS for the pool's figures instead of df
            foreach (VolumeInfo z in mounted.Where(v => v.Mount.Method == MountMethod.ZfsPool))
            {
                try
                {
                    WslResult zr = await Wsl.RunRootAsync(z.Distro, "zfs get -Hp -o value used,available " + z.MountName, 20, ct).ConfigureAwait(false);
                    string[] v = zr.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    double used, avail;
                    if (zr.ExitCode == 0 && v.Length >= 2 &&
                        double.TryParse(v[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out used) &&
                        double.TryParse(v[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out avail))
                    {
                        space[z.MountName] = new SpaceFigures { Size = used + avail, Used = used, Avail = avail };
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.DebugException("zfs get for pool " + z.MountName + " failed", ex);
                }
            }
            List<VolumeInfo> targets = mounted.Where(v => v.Mount.Method != MountMethod.ZfsPool).ToList();
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
            catch (Exception ex)
            {
                Log.DebugException("df for mounted drives failed; showing estimates from the superblock", ex);
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
            if (!Directory.Exists(path)) Log.Debug("Explorer path not reachable after 5 s, opening anyway: " + path);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
    }

    /// <summary>Finds supported filesystems on (USB) disks and merges them with the saved mount state.</summary>
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
            var scanWatch = Stopwatch.StartNew();
            List<DiskRecord> allDisks = Storage.GetDisks();
            List<MountEntry> saved = state.Mounts;
            Log.Debug(string.Format(CultureInfo.InvariantCulture, "Scan: {0} disk(s) from the Storage API in {1:N0} ms, include non-USB: {2}, saved mounts: {3}",
                allDisks.Count, scanWatch.ElapsedMilliseconds, includeAllDisks, saved.Count));
            foreach (DiskRecord d in allDisks)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture,
                    "  disk {0}: '{1}' {2}, {3}, {4}{5}{6}{7} -> {8}  id {9}",
                    d.Number, d.FriendlyName, Storage.BusTypeName(d.BusType), Fmt.Bytes(d.Size), Storage.PartitionStyleName(d.PartitionStyle),
                    d.IsBoot ? ", boot" : string.Empty, d.IsSystem ? ", system" : string.Empty, d.IsOffline ? ", offline" : string.Empty,
                    d.InScope(includeAllDisks) ? "scanned" : "skipped", d.Key));
            }
            cache.RetainDisks(new HashSet<string>(allDisks.Select(d => d.Key), StringComparer.Ordinal));

            var rows = new List<VolumeInfo>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (DiskRecord d in allDisks.Where(x => x.InScope(includeAllDisks)))
            {
                ct.ThrowIfCancellationRequested();
                List<PartitionRecord> parts = new List<PartitionRecord>();
                if (d.PartitionStyle != 0)
                {
                    try { parts = Storage.GetPartitions(d.Number); }
                    catch (Exception ex)
                    {
                        Log.DebugException("Listing partitions of disk " + d.Number.ToString(CultureInfo.InvariantCulture) + " failed", ex);
                        parts = new List<PartitionRecord>();
                    }
                }
                foreach (PartitionRecord p in parts)
                {
                    Log.Debug(string.Format(CultureInfo.InvariantCulture, "  disk {0} partition {1}: offset {2:N0}, {3}{4}",
                        d.Number, p.Number, p.Offset, Fmt.Bytes(p.Size), p.DriveLetter != '\0' ? ", drive " + p.DriveLetter + ":" : string.Empty));
                }
                if (parts.Count == 0) Log.Debug(string.Format(CultureInfo.InvariantCulture, "  disk {0}: no partitions, checking the whole disk", d.Number));
                string letters = string.Join(" ", parts.Where(p => p.DriveLetter != '\0').Select(p => p.DriveLetter + ":"));

                // btrfs written straight onto the disk ("mkfs.btrfs /dev/sdX") has no partition table
                List<PartitionRecord> targets = parts.Count > 0
                    ? parts
                    : new List<PartitionRecord> { new PartitionRecord { Number = 0, Offset = 0, Size = d.Size } };

                foreach (PartitionRecord t in targets)
                {
                    string key = d.Key + "|" + t.Number.ToString(CultureInfo.InvariantCulture);
                    MountEntry m = saved.FirstOrDefault(x => x.Key == key);
                    if (m != null) Log.Debug(string.Format(CultureInfo.InvariantCulture,
                        "  disk {0} partition {1}: mounted by this tool as '{2}', superblock not read", d.Number, t.Number, m.Name));
                    FsInfo sb = m == null ? cache.GetOrRead(key, d.Number, t.Offset, t.Size) : null;
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
                        row.Kind = sb.Kind;
                        row.ReadOnly = sb.ReadOnly;
                        row.FsNote = sb.Note;
                        row.FsWriteNote = sb.WriteNote;
                        row.FsOptions = sb.KernelOptions;
                        row.Label = sb.Label;
                        row.Uuid = sb.Uuid;
                        row.NumDevices = sb.NumDevices;
                        row.SpaceTotal = sb.TotalBytes;
                        row.SpaceFree = sb.FreeBytes;
                        row.SpaceUsed = sb.UsedBytes;
                    }
                    else
                    {
                        row.Kind = m.Kind;
                        row.ReadOnly = m.ReadOnly;
                        row.Label = m.Label;
                        row.Uuid = m.Uuid;
                        row.NumDevices = 1;
                        row.SpaceFree = -1;
                    }
                    rows.Add(row);
                    seen.Add(key);
                }
            }

            // mounted entries whose disk no longer enumerates normally (e.g. unplugged while mounted)
            foreach (MountEntry m in saved.Where(x => !seen.Contains(x.Key)))
            {
                DiskRecord d = allDisks.FirstOrDefault(x => x.Key == m.DiskUniqueId);
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "  saved mount '{0}' not found in this scan; disk {1}",
                    m.Name, d != null ? "still enumerates as disk " + d.Number.ToString(CultureInfo.InvariantCulture) : "is gone (unplugged)"));
                rows.Add(new VolumeInfo
                {
                    Kind = m.Kind,
                    ReadOnly = m.ReadOnly,
                    SpaceFree = -1,
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

            Log.Debug(string.Format(CultureInfo.InvariantCulture, "Scan finished in {0:N0} ms: {1} btrfs volume(s){2}",
                scanWatch.ElapsedMilliseconds, rows.Count, string.Concat(rows.Select(v => string.Format(CultureInfo.InvariantCulture,
                    "{0}  {7} '{1}' disk {2} part {3}: {4}, {5}{6}", Environment.NewLine, v.DisplayLabel, v.DiskNumber, v.PartitionNumber,
                    v.Disconnected ? "UNPLUGGED" : v.Mounted ? "mounted as " + v.MountName : "not mounted",
                    Fmt.SpaceText(v), v.SpaceApprox ? " (estimate)" : string.Empty, v.KindName)))));
            return new ScanResult
            {
                Volumes = rows,
                Snapshot = allDisks.Where(x => x.InScope(includeAllDisks)).Select(x => x.Key).ToList()
            };
        }
    }
}
