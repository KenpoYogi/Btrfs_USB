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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XnixUsbMounter.Core
{
    // ---------------------------------------------------------------------------------------
    //  btrfs maintenance (runs inside WSL as root; needs btrfs-progs in the distro)
    // ---------------------------------------------------------------------------------------
    public sealed class Maintenance
    {
        private const string Split = "@@SPLIT@@";
        private readonly StateStore state;
        private readonly MountManager mounts;
        private readonly ConcurrentDictionary<string, bool> toolsOk = new ConcurrentDictionary<string, bool>();

        public Maintenance(StateStore state, MountManager mounts)
        {
            this.state = state;
            this.mounts = mounts;
        }

        private static string[] SplitParts(string output, int count)
        {
            string[] parts = (output ?? string.Empty).Split(new[] { Split }, StringSplitOptions.None);
            if (parts.Length >= count) return parts;
            var padded = new string[count];
            for (int i = 0; i < count; i++) padded[i] = i < parts.Length ? parts[i] : string.Empty;
            return padded;
        }

        private static void LogOutput(string output)
        {
            foreach (string l in (output ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (l.Trim().Length > 0) Log.Info("    " + l);
            }
        }

        public async Task<bool> TestToolsAsync(string distro, CancellationToken ct)
        {
            if (toolsOk.ContainsKey(distro)) return true;
            WslResult r = await Wsl.RunRootAsync(distro,
                "command -v btrfs >/dev/null 2>&1 && command -v blkid >/dev/null 2>&1", 60, ct).ConfigureAwait(false);
            if (r.ExitCode != 0)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "btrfs tools not found in {0} (btrfs or blkid missing, exit {1}).", distro, r.ExitCode));
                return false;
            }
            toolsOk[distro] = true;
            return true;
        }

        public async Task<bool> InstallToolsAsync(string distro, CancellationToken ct)
        {
            Log.Info(string.Format("Installing btrfs tools in {0} (this can take a minute)...", distro));
            const string cmd =
                "if command -v zypper >/dev/null 2>&1; then zypper --non-interactive install btrfsprogs util-linux; " +
                "elif command -v apt-get >/dev/null 2>&1; then apt-get update -q && DEBIAN_FRONTEND=noninteractive apt-get install -y -q btrfs-progs util-linux; " +
                "elif command -v dnf >/dev/null 2>&1; then dnf install -y btrfs-progs util-linux; " +
                "elif command -v pacman >/dev/null 2>&1; then pacman -Sy --noconfirm --needed btrfs-progs util-linux; " +
                "elif command -v apk >/dev/null 2>&1; then apk add btrfs-progs util-linux; " +
                "else echo No supported package manager found in this distro; exit 3; fi 2>&1";
            StreamResult r = await Wsl.RunStreamingToLogAsync(Wsl.RootArgs(distro, cmd), TimeSpan.FromMinutes(30), ct).ConfigureAwait(false);
            bool ignored;
            toolsOk.TryRemove(distro, out ignored);
            bool ok = !r.Stopped && await TestToolsAsync(distro, CancellationToken.None).ConfigureAwait(false);
            if (ok) Log.Ok(string.Format("btrfs tools are installed in {0}.", distro));
            else Log.Error(string.Format("Installing btrfs tools failed (exit {0}). See the lines above.", r.ExitCode));
            return ok;
        }

        public async Task<DriveInfo> GetDriveInfoAsync(string distro, string mountName, CancellationToken ct)
        {
            if (!await TestToolsAsync(distro, ct).ConfigureAwait(false))
            {
                return new DriveInfo { ToolsMissing = true, MountName = mountName };
            }
            string mp = "/mnt/wsl/" + mountName;
            string cmd = string.Format("btrfs filesystem usage -b {0} 2>&1; echo {1}; btrfs device stats {0} 2>&1; echo {1}; btrfs scrub status -R {0} 2>&1", mp, Split);
            WslResult r = await Wsl.RunRootAsync(distro, cmd, 90, ct).ConfigureAwait(false);
            string[] parts = SplitParts(r.Output, 3);
            UsageInfo usage = BtrfsParsers.ParseUsage(parts[0]);
            if (usage.DeviceSize <= 0) Log.Warn("Could not read usage for " + mp + ": " + parts[0].Trim());
            return new DriveInfo
            {
                ToolsMissing = false,
                MountName = mountName,
                Usage = usage,
                Stats = BtrfsParsers.ParseDeviceStats(parts[1]),
                Scrub = BtrfsParsers.ParseScrubStatus(parts[2], usage.Used)
            };
        }

        public async Task<ScrubProgress> GetScrubProgressAsync(string distro, string mountName, CancellationToken ct)
        {
            if (!await TestToolsAsync(distro, ct).ConfigureAwait(false))
            {
                return new ScrubProgress { Name = mountName, ToolsMissing = true };
            }
            string mp = "/mnt/wsl/" + mountName;
            WslResult r = await Wsl.RunRootAsync(distro,
                string.Format("btrfs scrub status -R {0} 2>&1; echo {1}; btrfs filesystem usage -b {0} 2>&1", mp, Split), 60, ct)
                .ConfigureAwait(false);
            string[] parts = SplitParts(r.Output, 2);
            UsageInfo usage = BtrfsParsers.ParseUsage(parts[1]);
            return new ScrubProgress { Name = mountName, Scrub = BtrfsParsers.ParseScrubStatus(parts[0], usage.Used) };
        }

        public async Task<ScrubCommandResult> StartScrubAsync(string distro, string mountName, bool resume, CancellationToken ct)
        {
            if (!await TestToolsAsync(distro, ct).ConfigureAwait(false))
            {
                return new ScrubCommandResult { Name = mountName, ToolsMissing = true };
            }
            string verb = resume ? "resume" : "start";
            Log.Info(string.Format("Starting scrub ({0}) on /mnt/wsl/{1}...", verb, mountName));
            WslResult r = await Wsl.RunRootAsync(distro, string.Format("btrfs scrub {0} /mnt/wsl/{1} 2>&1", verb, mountName), 60, ct)
                .ConfigureAwait(false);
            LogOutput(r.Output);
            bool ok = r.ExitCode == 0;
            if (ok) Log.Ok("Scrub is running in the background. The drive stays usable; progress is shown in the list.");
            return new ScrubCommandResult { Name = mountName, Ok = ok };
        }

        public async Task<ScrubCommandResult> CancelScrubAsync(string distro, string mountName, CancellationToken ct)
        {
            WslResult r = await Wsl.RunRootAsync(distro, "btrfs scrub cancel /mnt/wsl/" + mountName + " 2>&1", 120, ct).ConfigureAwait(false);
            LogOutput(r.Output);
            return new ScrubCommandResult { Name = mountName, Ok = r.ExitCode == 0 };
        }

        public async Task<CheckResult> OfflineCheckAsync(VolumeInfo volume, string distro, CancellationToken ct)
        {
            var result = new CheckResult { Label = volume.DisplayLabel };
            if (volume.Kind != FsKind.Btrfs)
            {
                result.Summary = "The offline check (btrfs check) is only available for btrfs.";
                return result;
            }
            if (!await TestToolsAsync(distro, ct).ConfigureAwait(false))
            {
                result.ToolsMissing = true;
                return result;
            }
            string diskPath = MountManager.DiskPath(volume.DiskNumber);
            mounts.StartKeepAlive(distro);
            Log.Info(string.Format("Attaching disk {0} to WSL without mounting it...", volume.DiskNumber));
            WslResult a = await Wsl.RunAsync(new[] { "--mount", diskPath, "--bare" }, 180, ct).ConfigureAwait(false);
            if (a.ExitCode != 0)
            {
                Log.Error("Could not attach the disk: " + a.Combined);
                result.Summary = "Could not attach the disk to WSL: " + a.Combined;
                if (state.MountCount == 0) mounts.StopKeepAlive();
                return result;
            }

            try
            {
                result.Stage = CheckStage.Find;
                string device = null;
                for (int i = 0; i < 20 && device == null; i++)
                {
                    WslResult b = await Wsl.RunRootAsync(distro, "blkid -c /dev/null -U " + volume.Uuid, 30, ct).ConfigureAwait(false);
                    string first = b.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault(l => Regex.IsMatch(l.Trim(), @"^/dev/\S+$"));
                    if (b.ExitCode == 0 && first != null) device = first.Trim();
                    else await Task.Delay(500, ct).ConfigureAwait(false);
                }
                if (device == null)
                {
                    Log.Error("The btrfs partition did not show up inside WSL.");
                    result.Summary = "The disk attached, but its btrfs partition could not be found inside WSL.";
                    return result;
                }

                result.Stage = CheckStage.Check;
                Log.Info("Running read-only check: btrfs check --readonly " + device);
                Log.Info("Nothing on the drive is changed. On large drives this can take a long time; use Cancel task to stop.");
                DateTime started = DateTime.Now;
                StreamResult c = await Wsl.RunStreamingToLogAsync(
                    Wsl.RootArgs(distro, "btrfs check --readonly " + device + " 2>&1"), TimeSpan.FromHours(48), ct).ConfigureAwait(false);
                if (c.Stopped)
                {
                    try { await Wsl.RunRootAsync(distro, "pkill -f 'btrfs check' ; sleep 1", 30, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception ex) { Log.DebugException("Stopping btrfs check inside WSL failed", ex); }
                }

                string safeLabel = Regex.Replace(volume.DisplayLabel, @"[^A-Za-z0-9._-]", "_");
                string logFile = Path.Combine(AppPaths.EnsureChecksDir(),
                    string.Format(CultureInfo.InvariantCulture, "check-{0}-{1:yyyyMMdd-HHmmss}.txt", safeLabel, started));
                var report = new List<string>
                {
                    "btrfs check --readonly " + device,
                    string.Format("Drive: {0}  UUID: {1}  Disk: {2} {3}", volume.DisplayLabel, volume.Uuid, volume.DiskNumber, volume.Model),
                    string.Format("Started: {0}   Finished: {1}   Exit code: {2}   Cancelled: {3}", started, DateTime.Now, c.ExitCode, c.Stopped),
                    string.Empty
                };
                report.AddRange(c.Lines);
                File.WriteAllLines(logFile, report, new UTF8Encoding(false));

                List<string> summary = c.Lines.Where(l => Regex.IsMatch(l, "error|found \\d+ bytes used", RegexOptions.IgnoreCase)).ToList();
                result.ExitCode = c.ExitCode;
                result.Cancelled = c.Stopped;
                result.Ok = !c.Stopped && c.ExitCode == 0;
                result.Summary = string.Join(Environment.NewLine, summary.Skip(Math.Max(0, summary.Count - 3)));
                result.LogFile = logFile;
                if (result.Ok) Log.Ok("Check finished: no errors found. Report: " + logFile);
                else if (!c.Stopped) Log.Error(string.Format("Check reported problems (exit {0}). Report: {1}", c.ExitCode, logFile));
            }
            finally
            {
                Log.Info(string.Format("Detaching disk {0}...", volume.DiskNumber));
                try { await Wsl.RunAsync(new[] { "--unmount", diskPath }, 180, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) { Log.Exception("Detaching the disk after the check failed", ex); }
                if (state.MountCount == 0) mounts.StopKeepAlive();
            }
            return result;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Start at logon: scheduled task with highest privileges (no UAC prompt at logon)
    // ---------------------------------------------------------------------------------------
    public static class StartupTask
    {
        // the pre-rename task name on purpose: an existing task that still starts BtrfsUsbMounter.exe is found
        // and re-pointed at this program (PointsElsewhere), instead of both starting at logon
        public const string TaskName = "BtrfsUsbMounter";

        private static int RunSchtasks(string arguments, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            Log.Debug("schtasks.exe " + arguments);
            using (Process p = Process.Start(psi))
            {
                if (p == null)
                {
                    output = string.Empty;
                    Log.Debug("schtasks.exe did not start.");
                    return -1;
                }
                Task<string> err = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                output = (output + " " + err.Result).Trim();
                // /XML output is long and not interesting unless something failed
                bool quietOutput = p.ExitCode == 0 && arguments.IndexOf("/XML", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                   arguments.IndexOf("/Query", StringComparison.OrdinalIgnoreCase) >= 0;
                Log.Debug("schtasks.exe exit " + p.ExitCode.ToString(CultureInfo.InvariantCulture) +
                          (quietOutput ? string.Empty : Log.Excerpt(output, 10, 1500)));
                return p.ExitCode;
            }
        }

        public static bool Exists()
        {
            string ignored;
            return RunSchtasks("/Query /TN \"" + TaskName + "\"", out ignored) == 0;
        }

        /// <summary>True when the task exists but launches something else (e.g. the old PowerShell script).</summary>
        public static bool PointsElsewhere()
        {
            string xml;
            if (RunSchtasks("/Query /TN \"" + TaskName + "\" /XML", out xml) != 0) return false;
            bool elsewhere = xml.IndexOf(AppPaths.ExePath, StringComparison.OrdinalIgnoreCase) < 0;
            Match command = Regex.Match(xml, "<Command>(.*?)</Command>", RegexOptions.Singleline);
            Log.Debug("Logon task runs: " + (command.Success ? command.Groups[1].Value : "(no command found)") +
                      (elsewhere ? "  (not this program: " + AppPaths.ExePath + ")" : "  (this program)"));
            return elsewhere;
        }

        public static void Enable()
        {
            string user = WindowsIdentity.GetCurrent().Name;
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Xnix USB Mounter (tray)</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + "</UserId></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + SecurityElement.Escape(user) + "</UserId>" +
                "<LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings>\r\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
                "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
                "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
                "    <Enabled>true</Enabled>\r\n" +
                "    <Hidden>false</Hidden>\r\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
                "    <WakeToRun>false</WakeToRun>\r\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
                "    <Priority>7</Priority>\r\n" +
                "  </Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(AppPaths.ExePath) + "</Command>" +
                "<Arguments>--tray</Arguments></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string temp = Path.Combine(Path.GetTempPath(), "XnixUsbMounter-task.xml");
            File.WriteAllText(temp, xml, Encoding.Unicode);
            try
            {
                string output;
                int code = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + temp + "\" /F", out output);
                if (code != 0) throw new InvalidOperationException("schtasks failed: " + output);
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
            Log.Ok("Will start in the tray at logon (no UAC prompt).");
        }

        public static void Disable()
        {
            string ignored;
            RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", out ignored);
            Log.Info("Start-at-logon removed.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Job queue: one background task at a time, with cancellation and UI notifications
    // ---------------------------------------------------------------------------------------
    [Flags]
    public enum JobFlags
    {
        None = 0,
        Mutating = 1,       // changes mounts: disables mount/eject buttons while queued or running
        Quiet = 2,          // background check: no status text or progress bar
        Cancellable = 4,    // shows the Cancel task button
        Unique = 8          // skipped if an identical job is already waiting
    }

    public sealed class JobInfo
    {
        internal JobInfo(string name, JobFlags flags)
        {
            Name = name;
            Flags = flags;
            Cts = new CancellationTokenSource();
        }

        public string Name { get; private set; }
        public JobFlags Flags { get; private set; }
        internal CancellationTokenSource Cts { get; private set; }

        public bool Mutating { get { return (Flags & JobFlags.Mutating) != 0; } }
        public bool Quiet { get { return (Flags & JobFlags.Quiet) != 0; } }
        public bool Cancellable { get { return (Flags & JobFlags.Cancellable) != 0; } }
    }

    public sealed class JobSkippedException : Exception
    {
        public JobSkippedException(string name) : base("Skipped duplicate job: " + name) { }
    }

    public sealed class JobQueue
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private readonly List<JobInfo> pending = new List<JobInfo>();
        private readonly SynchronizationContext context;
        private JobInfo current;

        public JobQueue(SynchronizationContext context)
        {
            this.context = context;
        }

        /// <summary>Raised on the UI thread whenever a job is queued, starts or ends.</summary>
        public event EventHandler Changed;

        public JobInfo Current
        {
            get { lock (sync) return current; }
        }

        public bool IsIdle
        {
            get { lock (sync) return current == null && pending.Count == 0; }
        }

        public bool IsMutatingBusy
        {
            get { lock (sync) return (current != null && current.Mutating) || pending.Any(j => j.Mutating); }
        }

        public bool IsPending(string name)
        {
            lock (sync) return (current != null && current.Name == name) || pending.Any(j => j.Name == name);
        }

        /// <summary>Queues work; the returned task completes (on the caller's context when awaited) with its result.</summary>
        public Task<T> Run<T>(string name, Func<CancellationToken, Task<T>> work, JobFlags flags)
        {
            JobInfo job;
            lock (sync)
            {
                if ((flags & JobFlags.Unique) != 0 && pending.Any(j => j.Name == name))
                {
                    if ((flags & JobFlags.Quiet) == 0) Log.Debug("Job '" + name + "' skipped: an identical job is already waiting.");
                    var skipped = new TaskCompletionSource<T>();
                    skipped.SetException(new JobSkippedException(name));
                    return skipped.Task;
                }
                job = new JobInfo(name, flags);
                pending.Add(job);
                if (!job.Quiet && (current != null || pending.Count > 1))
                {
                    Log.Debug(string.Format(CultureInfo.InvariantCulture, "Job '{0}' queued behind '{1}' ({2} waiting).",
                        name, current != null ? current.Name : pending[0].Name, pending.Count - 1));
                }
            }
            RaiseChanged();
            return RunCore(job, work);
        }

        private async Task<T> RunCore<T>(JobInfo job, Func<CancellationToken, Task<T>> work)
        {
            Stopwatch waited = Stopwatch.StartNew();
            await gate.WaitAsync().ConfigureAwait(false);
            lock (sync)
            {
                pending.Remove(job);
                current = job;
            }
            RaiseChanged();
            // quiet jobs are the frequent background checks: only logged when slow or failing
            if (!job.Quiet)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Job '{0}' started ({1}{2}).", job.Name, job.Flags,
                    waited.ElapsedMilliseconds > 100 ? string.Format(CultureInfo.InvariantCulture, ", waited {0:N0} ms", waited.ElapsedMilliseconds) : string.Empty));
            }
            Stopwatch ran = Stopwatch.StartNew();
            try
            {
                job.Cts.Token.ThrowIfCancellationRequested();
                T result = await Task.Run(() => work(job.Cts.Token), job.Cts.Token).ConfigureAwait(false);
                if (!job.Quiet || ran.ElapsedMilliseconds > 5000)
                {
                    Log.Debug(string.Format(CultureInfo.InvariantCulture, "Job '{0}' finished in {1:N0} ms.", job.Name, ran.ElapsedMilliseconds));
                }
                return result;
            }
            catch (OperationCanceledException)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Job '{0}' cancelled after {1:N0} ms.", job.Name, ran.ElapsedMilliseconds));
                throw;
            }
            catch (Exception ex)
            {
                Log.DebugException(string.Format(CultureInfo.InvariantCulture, "Job '{0}' failed after {1:N0} ms", job.Name, ran.ElapsedMilliseconds), ex);
                throw;
            }
            finally
            {
                lock (sync) current = null;
                gate.Release();
                job.Cts.Dispose();
                RaiseChanged();
            }
        }

        public void CancelCurrent()
        {
            lock (sync)
            {
                if (current != null)
                {
                    Log.Debug("Cancel requested for job '" + current.Name + "'.");
                    try { current.Cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
        }

        public void CancelAll()
        {
            lock (sync)
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Cancelling all jobs (running: {0}, waiting: {1}).",
                    current != null ? current.Name : "none", pending.Count));
                foreach (JobInfo j in pending.Concat(current != null ? new[] { current } : new JobInfo[0]))
                {
                    try { j.Cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;
            if (context != null) context.Post(_ => handler(this, EventArgs.Empty), null);
            else handler(this, EventArgs.Empty);
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Engine: the core services, shared by the GUI and the command line
    // ---------------------------------------------------------------------------------------
    public sealed class Engine
    {
        public Engine(StateStore state)
        {
            State = state;
            Cache = new SuperblockCache();
            Support = new FsSupport();
            Mounts = new MountManager(state, Cache, Support);
            Scanner = new VolumeScanner(state, Mounts, Cache);
            Maintenance = new Maintenance(state, Mounts);
        }

        public StateStore State { get; private set; }
        public SuperblockCache Cache { get; private set; }
        /// <summary>Which filesystems the WSL kernel / distro can mount, per distro.</summary>
        public FsSupport Support { get; private set; }
        public MountManager Mounts { get; private set; }
        public VolumeScanner Scanner { get; private set; }
        public Maintenance Maintenance { get; private set; }
    }
}
