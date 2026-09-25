// Linux USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LinuxUsbMounter.Core;
using LinuxUsbMounter.UI;

namespace LinuxUsbMounter
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            AppPaths.EnsureDirectories();
            CommandLineOptions options;
            try
            {
                options = CommandLineOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Log.Debug("Bad command line (" + Environment.CommandLine + "): " + ex.Message);
                Cli.ShowMessage(ex.Message + Environment.NewLine + Environment.NewLine + CommandLineOptions.Usage, true);
                return 2;
            }
            if (options.ShowHelp)
            {
                Cli.ShowMessage(CommandLineOptions.Usage, false);
                return 0;
            }
            Diagnostics.LogStartup(options.IsCli ? "command line" : options.Tray ? "tray" : "window", args);
            if (!File.Exists(AppPaths.WslExe))
            {
                const string msg = "WSL is not installed. From an elevated prompt run \"wsl --install\", reboot, then start this program again.";
                Log.Error(msg);
                if (options.IsCli) Cli.ShowMessage(msg, true);
                else MessageBox.Show(msg, UiKit.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            int code = options.IsCli ? Cli.Run(options) : RunGui(options);
            Log.Debug("Exiting with code " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            return code;
        }

        private static int RunGui(CommandLineOptions options)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Exception("Unexpected error", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("Fatal error: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.DebugException("Unobserved background task exception", e.Exception);
                e.SetObserved();
            };

            using (var mutex = new Mutex(false, SingleInstance.MutexName))
            {
                if (!SingleInstance.TryAcquire(mutex, options.Tray)) return 0;
                try
                {
                    var engine = new Engine(StateStore.Load());
                    using (var form = new MainForm(engine, options.Tray, options.Distro))
                    {
                        Application.Run(form);
                    }
                }
                finally
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// One running copy at a time. A second launch asks the running copy to show its window; if it
    /// doesn't answer (hidden and hung, or the old PowerShell version), the user may end it.
    /// </summary>
    internal static class SingleInstance
    {
        // same name as the PowerShell version, so the two can never run side by side. These three keep the
        // old BtrfsUsbMounter name on purpose: a running pre-rename copy must still see (and answer) a new one
        public const string MutexName = @"Local\BtrfsUsbMounter.GUI";
        private const string AckEventName = @"Local\BtrfsUsbMounter.Ack";
        private static readonly IntPtr HwndBroadcast = new IntPtr(0xffff);

        public static readonly int ShowMessage = RegisterWindowMessage("BtrfsUsbMounter.ShowWindow");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static bool Wait(Mutex mutex, int milliseconds)
        {
            try
            {
                return mutex.WaitOne(milliseconds);
            }
            catch (AbandonedMutexException)
            {
                Log.Debug("Single instance: the previous copy ended without releasing the mutex (killed or crashed).");
                return true;   // the previous owner was killed; we own it now
            }
        }

        /// <summary>Called by the running copy after it has shown its window.</summary>
        public static void Acknowledge()
        {
            try
            {
                using (EventWaitHandle ack = EventWaitHandle.OpenExisting(AckEventName)) ack.Set();
            }
            catch
            {
                // nobody waiting
            }
        }

        public static bool TryAcquire(Mutex mutex, bool trayStart)
        {
            if (Wait(mutex, 0))
            {
                Log.Debug("Single instance: this is the only copy.");
                return true;
            }
            Log.Debug("Single instance: another copy holds the mutex" + (trayStart ? "; tray start steps aside." : "; asking it to show its window."));
            if (trayStart)
            {
                Log.Warn("Another copy is already running; the logon copy steps aside.");
                return false;
            }

            using (var ack = new EventWaitHandle(false, EventResetMode.AutoReset, AckEventName))
            {
                if (ShowMessage != 0) PostMessage(HwndBroadcast, ShowMessage, IntPtr.Zero, IntPtr.Zero);
                if (ack.WaitOne(2000))
                {
                    Log.Debug("Single instance: the running copy showed its window; this copy exits.");
                    return false;   // the running copy brought its window up
                }
            }

            List<int> others = FindOtherInstances();
            Log.Warn(string.Format("Another copy is running but did not respond (PID {0}).", string.Join(", ", others)));
            DialogResult answer = MessageBox.Show(
                UiKit.AppName + " is already running but did not respond - it may be hidden or hung.\r\n\r\n" +
                "End it and start a fresh copy? Drives stay mounted either way.",
                UiKit.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return false;

            foreach (int pid in others)
            {
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                    {
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                    Log.Info(string.Format("Ended the previous copy (PID {0}).", pid));
                }
                catch (Exception ex)
                {
                    Log.Error(string.Format("Could not end PID {0}: {1}", pid, ex.Message));
                }
            }
            if (Wait(mutex, 5000)) return true;
            MessageBox.Show("The other copy could not be ended. Sign out and back in, then start the program again.",
                UiKit.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        /// <summary>Other copies of this program plus the older PowerShell version.</summary>
        private static List<int> FindOtherInstances()
        {
            var pids = new List<int>();
            int self = Process.GetCurrentProcess().Id;
            string myName = Path.GetFileNameWithoutExtension(AppPaths.ExePath);
            foreach (Process p in Process.GetProcessesByName(myName))
            {
                using (p)
                {
                    if (p.Id != self) pids.Add(p.Id);
                }
            }
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'powershell.exe' OR Name = 'pwsh.exe'"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementBaseObject mo in results)
                    {
                        using (mo)
                        {
                            string cmd = Convert.ToString(mo["CommandLine"]) ?? string.Empty;
                            if (cmd.IndexOf("BtrfsUsbMounter.ps1", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                pids.Add(Convert.ToInt32(mo["ProcessId"]));
                            }
                        }
                    }
                }
            }
            catch
            {
                // WMI unavailable: only same-name processes
            }
            return pids.Distinct().ToList();
        }
    }

    internal sealed class CommandLineOptions
    {
        public const string Usage =
            "Linux USB Mounter - mount Linux and Mac USB drives through WSL2\r\n" +
            AppInfo.Copyright + ". Licensed under the " + AppInfo.LicenseName + ".\r\n" +
            AppInfo.LicenseSummary + "\r\n" +
            AppInfo.NoWarranty + "\r\n" +
            "Full terms: the LICENSE file next to the program, or " + AppInfo.LicenseUrl + "\r\n\r\n" +
            "Usage: LinuxUsbMounter [options]\r\n\r\n" +
            "  (no options)       open the window\r\n" +
            "  --tray             start hidden in the system tray\r\n" +
            "  --list             list detected filesystems (btrfs, ext2/3/4, XFS, JFS, ZFS,\r\n" +
            "                     HFS+, APFS, UFS/UFS2) and whether they can be mounted\r\n" +
            "  --mount-all        mount every detected, unmounted filesystem that can be mounted\r\n" +
            "  --unmount-all      flush and detach everything this program mounted\r\n" +
            "  --distro <name>    WSL2 distribution to use\r\n" +
            "  --options <opts>   btrfs mount options, e.g. compress=zstd (btrfs drives only)\r\n" +
            "  --verbose          also show troubleshooting detail (always written to the log file)\r\n" +
            "  --help             show this help\r\n";

        public bool Tray { get; private set; }
        public bool List { get; private set; }
        public bool MountAll { get; private set; }
        public bool UnmountAll { get; private set; }
        public bool ShowHelp { get; private set; }
        public bool Verbose { get; private set; }
        public string Distro { get; private set; }
        public string Options { get; private set; }

        public bool IsCli { get { return List || MountAll || UnmountAll; } }

        public static CommandLineOptions Parse(string[] args)
        {
            var o = new CommandLineOptions();
            for (int i = 0; i < args.Length; i++)
            {
                // accept --mount-all, -MountAll, /mountall ...
                string key = args[i].TrimStart('-', '/').Replace("-", string.Empty).ToLowerInvariant();
                switch (key)
                {
                    case "tray": o.Tray = true; break;
                    case "list": o.List = true; break;
                    case "mountall": o.MountAll = true; break;
                    case "unmountall": o.UnmountAll = true; break;
                    case "verbose":
                    case "v": o.Verbose = true; break;
                    case "help":
                    case "h":
                    case "?": o.ShowHelp = true; break;
                    case "distro":
                        if (i + 1 >= args.Length) throw new ArgumentException("--distro needs a value.");
                        o.Distro = args[++i];
                        break;
                    case "options":
                        if (i + 1 >= args.Length) throw new ArgumentException("--options needs a value.");
                        o.Options = args[++i];
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            return o;
        }
    }

    /// <summary>Command-line mode. Attaches to the parent console (or opens one) for output.</summary>
    internal static class Cli
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();

        private static bool ownConsole;

        private static void OpenConsole()
        {
            if (AttachConsole(AttachParentProcess)) return;
            AllocConsole();
            ownConsole = true;   // started without a console (e.g. elevated from a non-admin prompt)
        }

        private static void CloseConsole()
        {
            if (ownConsole)
            {
                Console.WriteLine();
                Console.Write("Press Enter to close...");
                try { Console.ReadLine(); } catch { }
            }
            FreeConsole();
        }

        public static void ShowMessage(string text, bool isError)
        {
            if (AttachConsole(AttachParentProcess))
            {
                Console.WriteLine();
                Console.WriteLine(text);
                FreeConsole();
            }
            else
            {
                MessageBox.Show(text, UiKit.AppName, MessageBoxButtons.OK, isError ? MessageBoxIcon.Error : MessageBoxIcon.Information);
            }
        }

        public static int Run(CommandLineOptions options)
        {
            OpenConsole();
            Console.WriteLine();
            Log.Written += (line, level) =>
            {
                if (level == LogLevel.Debug && !options.Verbose) return;
                ConsoleColor color = level == LogLevel.Debug ? ConsoleColor.DarkGray
                                   : level == LogLevel.Warn ? ConsoleColor.Yellow
                                   : level == LogLevel.Error ? ConsoleColor.Red
                                   : level == LogLevel.Ok ? ConsoleColor.Green
                                   : ConsoleColor.Gray;
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.WriteLine(line);
                Console.ForegroundColor = old;
            };
            try
            {
                return RunAsync(options).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log.Exception("Command failed", ex);
                return 1;
            }
            finally
            {
                CloseConsole();
            }
        }

        private static async Task<int> RunAsync(CommandLineOptions options)
        {
            var engine = new Engine(StateStore.Load());
            AppSettings settings = engine.State.Settings;
            CancellationToken ct = CancellationToken.None;
            await Diagnostics.LogWslInfoAsync(ct).ConfigureAwait(false);
            List<Distro> distros = await DistroService.ListAsync(ct).ConfigureAwait(false);
            string distro = DistroService.Resolve(!string.IsNullOrEmpty(options.Distro) ? options.Distro : settings.Distro, distros);
            string mountOptions = options.Options ?? settings.Options;

            await engine.Mounts.SyncMountStateAsync(ct).ConfigureAwait(false);
            ScanResult scan = await engine.Scanner.ScanAsync(settings.ShowAllDisks, ct).ConfigureAwait(false);
            List<VolumeInfo> volumes = scan.Volumes;
            int exitCode = 0;

            if (distro != null)
            {
                try { await engine.Support.EnsureAsync(distro, ct).ConfigureAwait(false); }
                catch (Exception ex) { Log.DebugException("Filesystem support check failed", ex); }
            }

            if (options.List)
            {
                if (volumes.Count == 0)
                {
                    Console.WriteLine("No supported filesystems found on " + (settings.ShowAllDisks ? "the scanned disks." : "USB disks."));
                }
                else
                {
                    const string row = "{0,-13} {1,-9} {2,-5} {3,-6} {4,-18} {5,-28} {6}";
                    Console.WriteLine(row, "Status", "Type", "Disk", "Part", "Label", "Free", "Windows path");
                    var notes = new List<string>();
                    foreach (VolumeInfo v in volumes)
                    {
                        FsAvailability a = engine.Support.Get(distro, v.Kind);
                        string status = v.Disconnected ? "Unplugged"
                                      : v.Mounted ? (v.Mount.ReadOnly ? "Mounted (ro)" : "Mounted")
                                      : a == FsAvailability.NoDriver ? "No driver"
                                      : a == FsAvailability.NeedsTools ? "Needs tools"
                                      : a == FsAvailability.NotSupported ? "Not mountable"
                                      : engine.Mounts.WillBeReadOnly(v, distro) ? "Ready (ro)" : "Ready";
                        Console.WriteLine(row, status, v.KindName, v.DiskNumber,
                            v.PartitionNumber > 0 ? v.PartitionNumber.ToString() : "whole", v.DisplayLabel, Fmt.SpaceText(v), v.WindowsPath);
                        string why = v.Mounted ? null : FsSupport.Explain(v.Kind, a);
                        if (why != null) notes.Add(v.DisplayLabel + ": " + why);
                        else if (!v.Mounted && !string.IsNullOrEmpty(v.FsNote)) notes.Add(v.DisplayLabel + ": " + v.FsNote);
                    }
                    if (notes.Count > 0)
                    {
                        Console.WriteLine();
                        foreach (string n in notes.Distinct()) Console.WriteLine(n);
                    }
                }
            }

            if (options.MountAll)
            {
                if (distro == null)
                {
                    Log.Error("No WSL2 distribution found. Install one with: wsl --install -d Ubuntu");
                    exitCode = 1;
                }
                else
                {
                    List<VolumeInfo> candidates = volumes.Where(v => !v.Mounted && !v.Disconnected).ToList();
                    foreach (VolumeInfo v in candidates)
                    {
                        FsAvailability a = engine.Support.Get(distro, v.Kind);
                        if (a != FsAvailability.Yes && a != FsAvailability.Unknown)
                        {
                            Log.Info(string.Format("Skipping {0} '{1}': {2}", v.KindName, v.DisplayLabel, FsSupport.Explain(v.Kind, a)));
                        }
                    }
                    List<VolumeInfo> targets = candidates.Where(v =>
                    {
                        FsAvailability a = engine.Support.Get(distro, v.Kind);
                        return a == FsAvailability.Yes || a == FsAvailability.Unknown;
                    }).ToList();
                    if (targets.Count == 0) Log.Info("Nothing to mount.");
                    foreach (VolumeInfo v in targets)
                    {
                        if (await engine.Mounts.MountAsync(v, distro, mountOptions, ct).ConfigureAwait(false) == null) exitCode = 1;
                    }
                }
            }

            if (options.UnmountAll)
            {
                await engine.Mounts.DismountAllAsync(volumes, ct).ConfigureAwait(false);
                if (engine.State.MountCount > 0) exitCode = 1;
            }
            return exitCode;
        }
    }
}
