// Linux USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LinuxUsbMounter.Setup
{
    /// <summary>
    /// LinuxUsbMounter-&lt;version&gt;-Setup.exe: installer and uninstaller in one file. The program's files are
    /// embedded as "payload/..." resources (BuildSetup target in the csproj). Installing copies this file into the
    /// install folder as Uninstall.exe, which Settings > Apps runs with /uninstall. The uninstaller re-runs itself
    /// from %TEMP%, so the install folder (and Uninstall.exe in it) can be deleted.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false;
            string from = null;
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].TrimStart('-', '/').ToLowerInvariant();
                if (key == "uninstall" || key == "u") uninstall = true;
                else if (key == "from" && i + 1 < args.Length) from = args[++i];
                else
                {
                    MessageBox.Show("Unknown option: " + args[i] + "\r\n\r\nUsage:\r\n  " + Product.SetupFileName +
                                    "               install or update\r\n  " + Product.SetupFileName + " /uninstall    uninstall",
                                    Product.Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 2;
                }
            }

            string self = Assembly.GetExecutingAssembly().Location;
            if (string.Equals(Path.GetFileName(self), Product.UninstallerName, StringComparison.OrdinalIgnoreCase)) uninstall = true;

            if (!uninstall)
            {
                Application.Run(new InstallForm());
                return 0;
            }

            if (from == null)
            {
                // Uninstall.exe in the install folder, or the setup file run with /uninstall
                string dir = string.Equals(Path.GetFileName(self), Product.UninstallerName, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetDirectoryName(self)
                    : Installation.Find() != null ? Installation.Find().Location : null;
                if (dir == null)
                {
                    MessageBox.Show(Product.Name + " is not installed.", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                return StartUninstaller(dir) ? 0 : 1;
            }

            Application.Run(new UninstallForm(Paths.Normalize(from)));
            DeleteSelfLater(self);
            return 0;
        }

        /// <summary>Runs the uninstaller from a copy in %TEMP% (this process is elevated, so the copy is too).</summary>
        public static bool StartUninstaller(string dir)
        {
            string self = Assembly.GetExecutingAssembly().Location;
            string temp = Path.Combine(Path.GetTempPath(), "LinuxUsbMounter-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
            try
            {
                File.Copy(self, temp, true);
                Process.Start(new ProcessStartInfo(temp, "/uninstall /from \"" + Paths.Normalize(dir) + "\"") { UseShellExecute = false });
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start the uninstaller: " + ex.Message, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>The %TEMP% copy removes itself a moment after it exits.</summary>
        private static void DeleteSelfLater(string self)
        {
            if (!Paths.IsUnder(self, Path.GetTempPath())) return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/d /c ping 127.0.0.1 -n 3 > nul & del /f /q \"" + self + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetTempPath()
                });
            }
            catch
            {
                // a leftover file in %TEMP% is harmless
            }
        }
    }

    internal static class Product
    {
        public const string Name = "Linux USB Mounter";
        public const string ExeName = "LinuxUsbMounter.exe";
        public const string UninstallerName = "Uninstall.exe";
        public const string ShortcutName = "Linux USB Mounter.lnk";
        public const string DefaultFolderName = "Linux_USB";
        public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\LinuxUsbMounter";
        // the app keeps the pre-rename names for these on purpose (see CLAUDE.md "Name")
        public const string LogonTaskName = "BtrfsUsbMounter";
        public const string QuitMessageName = "LinuxUsbMounter.Quit";

        public static string Version { get { return SetupInfo.Version; } }
        public static string SetupFileName { get { return "LinuxUsbMounter-" + Version + "-Setup.exe"; } }

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), DefaultFolderName); }
        }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BtrfsUsbMounter"); }
        }
    }

    internal static class Paths
    {
        public static string Normalize(string path)
        {
            string full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            return full.Length > 3 ? full.TrimEnd('\\') : full;
        }

        public static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static bool IsUnder(string path, string dir)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dir)) return false;
            try { return Normalize(path).StartsWith(Normalize(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        /// <summary>Null when the folder is fine to install into, else the reason it isn't.</summary>
        public static string CheckInstallDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return "Choose a folder to install into.";
            string full;
            try
            {
                if (!Path.IsPathRooted(dir.Trim().Trim('"'))) return "Enter a full path, for example " + Product.DefaultDir;
                full = Normalize(dir);
            }
            catch (Exception ex)
            {
                return "That is not a valid folder: " + ex.Message;
            }
            if (Path.GetPathRoot(full).TrimEnd('\\').Length == full.TrimEnd('\\').Length) return "Choose a folder, not the root of a drive.";
            foreach (Environment.SpecialFolder f in new[] {
                Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.Desktop,
                Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.CommonApplicationData })
            {
                string sys = Environment.GetFolderPath(f);
                if (!string.IsNullOrEmpty(sys) && Same(full, sys)) return "Choose a folder of its own, for example " + Path.Combine(full, Product.DefaultFolderName);
            }
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (Same(full, windows) || IsUnder(full, windows)) return "Don't install into the Windows folder.";
            return null;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  The embedded program files
    // ---------------------------------------------------------------------------------------
    internal static class Payload
    {
        private const string Prefix = "payload/";

        /// <summary>Paths relative to the install folder (backslashes), e.g. tools\build-wsl-modules.sh.</summary>
        public static List<string> Files()
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(n => n.Substring(Prefix.Length).Replace('/', '\\'))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Every file the install folder gets, including Uninstall.exe.</summary>
        public static List<string> InstalledFiles()
        {
            List<string> files = Files();
            files.Add(Product.UninstallerName);
            return files;
        }

        public static Stream Open(string relative)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + relative.Replace('\\', '/'));
        }

        public static string ReadText(string relative)
        {
            using (Stream s = Open(relative))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        public static long TotalBytes()
        {
            long total = new FileInfo(Assembly.GetExecutingAssembly().Location).Length;
            foreach (string f in Files())
            {
                using (Stream s = Open(f)) total += s.Length;
            }
            return total;
        }

        public static void Extract(string dir, Action<string> log)
        {
            foreach (string rel in Files())
            {
                string target = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (Stream s = Open(rel))
                using (var fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    s.CopyTo(fs);
                }
                log("  " + target);
            }
            string self = Assembly.GetExecutingAssembly().Location;
            string uninstaller = Path.Combine(dir, Product.UninstallerName);
            if (!Paths.Same(self, uninstaller))
            {
                File.Copy(self, uninstaller, true);
                log("  " + uninstaller);
            }
        }

        /// <summary>Deletes the program's own files, then the folder if nothing else is left. False if the folder stays.</summary>
        public static bool RemoveInstalled(string dir, Action<string> log)
        {
            if (!Directory.Exists(dir)) return true;
            foreach (string rel in InstalledFiles())
            {
                string path = Path.Combine(dir, rel);
                if (!File.Exists(path)) continue;
                try
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                    log("  deleted " + path);
                }
                catch (Exception ex)
                {
                    log("  could not delete " + path + ": " + ex.Message);
                }
            }
            // empty sub-folders (tools), deepest first, then the folder itself
            foreach (string sub in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                TryRemoveEmpty(sub);
            }
            if (TryRemoveEmpty(dir))
            {
                log("  removed " + dir);
                return true;
            }
            log("  kept " + dir + " (it has other files in it)");
            return false;
        }

        private static bool TryRemoveEmpty(string dir)
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(dir).Any()) return false;
                Directory.Delete(dir);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  The "Installed apps" entry: HKLM\...\Uninstall\LinuxUsbMounter
    // ---------------------------------------------------------------------------------------
    internal sealed class Installation
    {
        public string Location { get; private set; }
        public string Version { get; private set; }
        public bool DesktopShortcut { get; private set; }
        public bool StartMenuShortcut { get; private set; }
        public bool AddedToPath { get; private set; }

        private static RegistryKey Hklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        public static Installation Find()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey k = hklm.OpenSubKey(Product.UninstallKey))
            {
                if (k == null) return null;
                string location = k.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(location)) return null;
                return new Installation
                {
                    Location = Paths.Normalize(location),
                    Version = k.GetValue("DisplayVersion") as string ?? "?",
                    DesktopShortcut = Convert.ToInt32(k.GetValue("LumDesktopShortcut", 0)) != 0,
                    StartMenuShortcut = Convert.ToInt32(k.GetValue("LumStartMenuShortcut", 1)) != 0,
                    AddedToPath = Convert.ToInt32(k.GetValue("LumAddedToPath", 0)) != 0,
                };
            }
        }

        public static void Register(string dir, InstallOptions o)
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey k = hklm.CreateSubKey(Product.UninstallKey))
            {
                string uninstaller = Path.Combine(dir, Product.UninstallerName);
                k.SetValue("DisplayName", Product.Name);
                k.SetValue("DisplayVersion", Product.Version);
                k.DeleteValue("Publisher", false);   // no personal name in Installed apps (user request)
                k.SetValue("DisplayIcon", Path.Combine(dir, Product.ExeName) + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
                k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                k.SetValue("Comments", "Mount Linux and Mac formatted USB drives on Windows through WSL2");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(Payload.TotalBytes() / 1024), RegistryValueKind.DWord);
                // what this setup added, so the uninstaller / next update removes only that
                k.SetValue("LumStartMenuShortcut", o.StartMenu ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("LumDesktopShortcut", o.Desktop ? 1 : 0, RegistryValueKind.DWord);
                k.SetValue("LumAddedToPath", o.AddToPath ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        public static void Unregister()
        {
            using (RegistryKey hklm = Hklm()) hklm.DeleteSubKeyTree(Product.UninstallKey, false);
        }

        // WSL distros setup installed, as "<user SID>\t<name>": distros belong to a Windows user, this key to the machine
        private const string SetupDistrosValue = "LumSetupDistros";

        private static string UserSid()
        {
            using (System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent())
                return id.User != null ? id.User.Value : string.Empty;
        }

        /// <summary>Remembers that setup installed <paramref name="distro"/> for the current user (the uninstaller offers to remove it).</summary>
        public static void AddSetupDistro(string distro)
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey k = hklm.OpenSubKey(Product.UninstallKey, true))
            {
                if (k == null) return;
                var entries = new List<string>(k.GetValue(SetupDistrosValue) as string[] ?? new string[0]);
                string entry = UserSid() + "\t" + distro;
                if (!entries.Contains(entry, StringComparer.OrdinalIgnoreCase)) entries.Add(entry);
                k.SetValue(SetupDistrosValue, entries.ToArray(), RegistryValueKind.MultiString);
            }
        }

        /// <summary>Distros setup installed for the current user (they may have been removed since).</summary>
        public static List<string> SetupDistros()
        {
            using (RegistryKey hklm = Hklm())
            using (RegistryKey k = hklm.OpenSubKey(Product.UninstallKey))
            {
                if (k == null) return new List<string>();
                string prefix = UserSid() + "\t";
                string[] entries = k.GetValue(SetupDistrosValue) as string[] ?? new string[0];
                return entries.Where(e => e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                              .Select(e => e.Substring(prefix.Length))
                              .ToList();
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Copies of the program running from a folder: ask them to quit, then end them
    // ---------------------------------------------------------------------------------------
    internal static class AppProcesses
    {
        private const int ProcessQueryLimitedInformation = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        private static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(h);
            }
        }

        private static List<Process> Find(Func<string, bool> matchImage)
        {
            var found = new List<Process>();
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    keep = p.Id != self && p.Id > 4 && matchImage(ImagePath(p.Id));
                }
                catch
                {
                    // exited meanwhile
                }
                if (keep) found.Add(p);
                else p.Dispose();
            }
            return found;
        }

        /// <summary>Processes whose program file is in <paramref name="dir"/> (LinuxUsbMounter.exe / .com), except this one.</summary>
        public static List<Process> InFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return new List<Process>();
            return Find(image => Paths.IsUnder(image, dir));
        }

        // the program's file names before the rename; they share the single-instance mutex
        private static readonly string[] ProgramNames = { Product.ExeName, "BtrfsUsbMounter.exe", "XnixUsbMounter.exe" };

        /// <summary>
        /// The program running from anywhere except <paramref name="dirs"/>: a hand-copied or older copy. It holds the
        /// single-instance mutex, so a freshly installed copy would only bring that one's window up and exit.
        /// </summary>
        public static List<Process> Elsewhere(params string[] dirs)
        {
            return Find(image => image != null &&
                                 ProgramNames.Contains(Path.GetFileName(image), StringComparer.OrdinalIgnoreCase) &&
                                 !dirs.Any(d => !string.IsNullOrEmpty(d) && Paths.IsUnder(image, d)));
        }

        /// <summary>Folders the processes run from (for messages); disposes the list.</summary>
        public static List<string> FoldersOf(List<Process> procs)
        {
            var folders = new List<string>();
            foreach (Process p in procs)
            {
                try
                {
                    string image = ImagePath(p.Id);
                    if (image != null) folders.Add(Path.GetDirectoryName(image));
                }
                catch
                {
                    // exited meanwhile
                }
                p.Dispose();
            }
            return folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static bool AnyRunning(string dir)
        {
            List<Process> list = InFolder(dir);
            foreach (Process p in list) p.Dispose();
            return list.Count > 0;
        }

        /// <summary>
        /// Asks the copies running from <paramref name="dir"/> to exit (they cancel a running task, drives stay mounted),
        /// waits up to 15 s, then ends whatever is left. True if anything was running.
        /// </summary>
        public static bool Stop(string dir, Action<string> log)
        {
            List<Process> procs = InFolder(dir);
            if (procs.Count == 0) return false;
            Stop(procs, log);
            if (AnyRunning(dir)) throw new InvalidOperationException(Product.Name + " is still running from " + dir + ". Exit it (tray icon > Exit) and try again.");
            return true;
        }

        /// <summary>Same for copies running from other folders (their files are left alone).</summary>
        public static bool StopElsewhere(Action<string> log, params string[] dirs)
        {
            List<Process> procs = Elsewhere(dirs);
            if (procs.Count == 0) return false;
            Stop(procs, log);
            return true;
        }

        /// <summary>Posts the quit message to the processes' windows, waits up to 15 s, ends the rest. Disposes the list.</summary>
        private static void Stop(List<Process> procs, Action<string> log)
        {
            try
            {
                log("Closing " + Product.Name + " (" + string.Join(", ", procs.Select(p => p.ProcessName + " " + p.Id)) + ")...");
                int quit = RegisterWindowMessage(Product.QuitMessageName);
                var pids = new HashSet<int>(procs.Select(p => p.Id));
                if (quit != 0)
                {
                    EnumWindows((hwnd, l) =>
                    {
                        int pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        if (pids.Contains(pid)) PostMessage(hwnd, quit, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                }
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                foreach (Process p in procs)
                {
                    int left = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                    try { p.WaitForExit(left); } catch { }
                }
                foreach (Process p in procs)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        log("  " + p.ProcessName + " " + p.Id + " did not exit; ending it.");
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        log("  could not end " + p.Id + ": " + ex.Message);
                    }
                }
                log("  closed.");
            }
            finally
            {
                foreach (Process p in procs) p.Dispose();
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Mounted drives, from the program's state.json (current user)
    // ---------------------------------------------------------------------------------------
    [DataContract]
    internal sealed class StateDoc
    {
        [DataMember] public List<MountDoc> Mounts { get; set; }
    }

    [DataContract]
    internal sealed class MountDoc
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public string Label { get; set; }
        [DataMember] public string FsType { get; set; }
    }

    internal static class MountedDrives
    {
        public static List<string> Read()
        {
            var result = new List<string>();
            string file = Path.Combine(Product.DataDir, "state.json");
            try
            {
                if (!File.Exists(file)) return result;
                string text = File.ReadAllText(file, Encoding.UTF8).Trim().TrimStart((char)0xFEFF);
                using (var ms = new MemoryStream(new UTF8Encoding(false).GetBytes(text)))
                {
                    var doc = (StateDoc)new DataContractJsonSerializer(typeof(StateDoc)).ReadObject(ms);
                    if (doc == null || doc.Mounts == null) return result;
                    foreach (MountDoc m in doc.Mounts)
                    {
                        string label = !string.IsNullOrEmpty(m.Label) ? m.Label : !string.IsNullOrEmpty(m.Name) ? m.Name : "(no label)";
                        result.Add(label + " (" + (string.IsNullOrEmpty(m.FsType) ? "btrfs" : m.FsType) + ")");
                    }
                }
            }
            catch
            {
                // unreadable: the program itself would start fresh as well
            }
            return result;
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Start at logon (scheduled task created by the program: Tools > Start at logon)
    // ---------------------------------------------------------------------------------------
    internal static class LogonTask
    {
        private static int Schtasks(string arguments, out string output)
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                Task<string> err = p.StandardError.ReadToEndAsync();
                output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(30000))
                {
                    try { p.Kill(); } catch { }
                    return -1;
                }
                output += err.Result;
                return p.ExitCode;
            }
        }

        private static string QueryXml()
        {
            string xml;
            return Schtasks("/Query /TN \"" + Product.LogonTaskName + "\" /XML", out xml) == 0 ? xml : null;
        }

        /// <summary>The program the logon task starts, or null when there is no task.</summary>
        public static string Command()
        {
            string xml = QueryXml();
            if (xml == null) return null;
            Match m = Regex.Match(xml, "<Command>(.*?)</Command>", RegexOptions.Singleline);
            return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim().Trim('"')) : null;
        }

        public static void Delete(Action<string> log)
        {
            string ignored;
            if (Schtasks("/Delete /TN \"" + Product.LogonTaskName + "\" /F", out ignored) == 0) log("Removed start at logon.");
        }

        /// <summary>Points the existing task at <paramref name="exe"/> (the install folder moved).</summary>
        public static void Repoint(string exe, Action<string> log)
        {
            string xml = QueryXml();
            if (xml == null) return;
            string updated = Regex.Replace(xml, "<Command>.*?</Command>", "<Command>" + SecurityElement.Escape(exe) + "</Command>", RegexOptions.Singleline);
            string temp = Path.Combine(Path.GetTempPath(), "LinuxUsbMounter-setup-task.xml");
            try
            {
                File.WriteAllText(temp, updated, Encoding.Unicode);
                string output;
                if (Schtasks("/Create /TN \"" + Product.LogonTaskName + "\" /XML \"" + temp + "\" /F", out output) == 0)
                    log("Start at logon now starts " + exe);
                else
                    log("Could not update start at logon (" + output.Trim() + "). The program fixes it the next time it starts.");
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Start menu / desktop shortcuts (all users) through WScript.Shell, late bound
    // ---------------------------------------------------------------------------------------
    internal static class Shortcuts
    {
        public static string StartMenu
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Product.ShortcutName); }
        }

        public static string Desktop
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), Product.ShortcutName); }
        }

        public static void Create(string lnk, string exe, Action<string> log)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object link = null;
            try
            {
                link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                Type t = link.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Mount Linux and Mac formatted USB drives through WSL2" });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                log("  " + lnk);
            }
            finally
            {
                if (link != null) Marshal.FinalReleaseComObject(link);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static void Delete(string lnk, Action<string> log)
        {
            if (!File.Exists(lnk)) return;
            try
            {
                File.Delete(lnk);
                log("  deleted " + lnk);
            }
            catch (Exception ex)
            {
                log("  could not delete " + lnk + ": " + ex.Message);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  System PATH (so "LinuxUsbMounter --list" works in any terminal)
    // ---------------------------------------------------------------------------------------
    internal static class SystemPath
    {
        private const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

        private static List<string> Read(RegistryKey k)
        {
            string value = k.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
            return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static bool Matches(string entry, string dir)
        {
            try { return Paths.Same(entry, dir); } catch { return false; }
        }

        public static bool Contains(string dir)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Key)) return k != null && Read(k).Any(e => Matches(e, dir));
        }

        public static void Add(string dir, Action<string> log)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Key, true))
            {
                List<string> parts = Read(k);
                if (parts.Any(e => Matches(e, dir))) return;
                parts.Add(dir);
                k.SetValue("Path", string.Join(";", parts), RegistryValueKind.ExpandString);
            }
            Broadcast();
            log("Added " + dir + " to the system PATH (new terminals see it).");
        }

        public static void Remove(string dir, Action<string> log)
        {
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Key, true))
            {
                List<string> parts = Read(k);
                int before = parts.Count;
                parts.RemoveAll(e => Matches(e, dir));
                if (parts.Count == before) return;
                k.SetValue("Path", string.Join(";", parts), RegistryValueKind.ExpandString);
            }
            Broadcast();
            log("Removed " + dir + " from the system PATH.");
        }

        private static void Broadcast()
        {
            IntPtr ignored;
            SendMessageTimeout(new IntPtr(0xffff), 0x001A /* WM_SETTINGCHANGE */, IntPtr.Zero, "Environment", 0x0002 /* SMTO_ABORTIFHUNG */, 3000, out ignored);
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Install / update
    // ---------------------------------------------------------------------------------------
    internal sealed class InstallOptions
    {
        public string Dir;
        public bool StartMenu;
        public bool Desktop;
        public bool AddToPath;
        public bool Launch;
        public bool CloseOtherCopies;
    }

    internal static class Installer
    {
        public static void Run(InstallOptions o, Installation old, Action<string> log)
        {
            string dir = o.Dir;
            string exe = Path.Combine(dir, Product.ExeName);
            string oldDir = old != null ? old.Location : null;
            bool moving = oldDir != null && !Paths.Same(oldDir, dir);

            log(old == null ? "Installing " + Product.Name + " " + Product.Version + " to " + dir
                            : "Updating " + Product.Name + " " + old.Version + " -> " + Product.Version + (moving ? ", moving it from " + oldDir + " to " + dir : " in " + dir));

            // 1. a running copy holds its files open; one running from another folder holds the single-instance mutex
            AppProcesses.Stop(dir, log);
            if (moving) AppProcesses.Stop(oldDir, log);
            if (o.CloseOtherCopies) AppProcesses.StopElsewhere(log, dir, oldDir);

            // 2. files
            log("Copying files:");
            Directory.CreateDirectory(dir);
            Payload.Extract(dir, log);

            // 3. shortcuts
            log("Shortcuts:");
            if (o.StartMenu) Shortcuts.Create(Shortcuts.StartMenu, exe, log); else Shortcuts.Delete(Shortcuts.StartMenu, log);
            if (o.Desktop) Shortcuts.Create(Shortcuts.Desktop, exe, log); else Shortcuts.Delete(Shortcuts.Desktop, log);

            // 4. PATH: remove only what this setup added before
            if (old != null && old.AddedToPath && (moving || !o.AddToPath)) SystemPath.Remove(oldDir, log);
            if (o.AddToPath) SystemPath.Add(dir, log);

            // 5. Installed apps entry
            Installation.Register(dir, o);
            log("Registered in Settings > Apps > Installed apps.");

            // 6. start at logon still pointing at the old folder
            if (moving)
            {
                string cmd = LogonTask.Command();
                if (cmd != null && Paths.IsUnder(cmd, oldDir)) LogonTask.Repoint(exe, log);
            }

            // 7. the old folder
            if (moving)
            {
                log("Removing the old copy:");
                Payload.RemoveInstalled(oldDir, log);
            }
            log("Done.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Uninstall
    // ---------------------------------------------------------------------------------------
    internal static class Uninstaller
    {
        /// <summary>
        /// False when the user stopped it because drives could not be unmounted. <paramref name="removeDistros"/>: WSL
        /// distros setup installed that the user chose to delete as well (after the drives are unmounted).
        /// </summary>
        public static bool Run(string dir, bool removeData, IList<string> removeDistros, Func<string, bool> confirm, Action<string> log)
        {
            string exe = Path.Combine(dir, Product.ExeName);
            log("Uninstalling " + Product.Name + " from " + dir);

            // 1. close the program (a running task is cancelled; drives stay mounted for step 2)
            AppProcesses.Stop(dir, log);

            // 2. unmount: the command line flushes and detaches, like Unmount all in the window. Not while a copy from
            //    another folder runs: that one still manages (and can eject) the drives
            List<string> mounted = MountedDrives.Read();
            List<string> others = AppProcesses.FoldersOf(AppProcesses.Elsewhere(dir));
            if (mounted.Count > 0 && others.Count > 0)
            {
                log("Leaving " + string.Join(", ", mounted) + " mounted: " + Product.Name + " is also running from " +
                    string.Join(", ", others) + " and keeps managing them.");
            }
            else if (mounted.Count > 0)
            {
                if (File.Exists(exe))
                {
                    log("Unmounting " + string.Join(", ", mounted) + " (flushing pending writes first; this can take a while)...");
                    int code = RunCli(exe, "--unmount-all", log);
                    mounted = MountedDrives.Read();
                    if (code == 0 && mounted.Count == 0) log("All drives are unmounted. They are safe to unplug.");
                }
                if (mounted.Count > 0)
                {
                    string question =
                        "These drives are still mounted: " + string.Join(", ", mounted) + ".\r\n\r\n" +
                        "Uninstall anyway? They stay attached to WSL until you run \"wsl --shutdown\" or restart Windows. " +
                        "Do not unplug them before that: data not yet written would be lost.\r\n\r\n" +
                        "No = stop here. Start " + Product.Name + ", eject the drives, then uninstall again.";
                    if (!confirm(question))
                    {
                        log("Stopped. Nothing was removed.");
                        return false;
                    }
                    log("Continuing with drives still mounted: " + string.Join(", ", mounted));
                }
            }

            // 2b. the Linux distros setup installed, if the user ticked them. Never with drives still mounted (they are
            //     mounted through the distro) or while another copy of the program may be using it
            if (removeDistros != null && removeDistros.Count > 0)
            {
                if (mounted.Count > 0 || others.Count > 0)
                {
                    log("Keeping " + string.Join(", ", removeDistros) + ": " +
                        (mounted.Count > 0 ? "drives are still mounted through it." : Product.Name + " is also running from " + string.Join(", ", others) + ".") +
                        " Remove it later with \"wsl --unregister <name>\".");
                }
                else
                {
                    foreach (string distro in removeDistros)
                    {
                        try
                        {
                            LinuxSetup.RemoveDistro(distro, log);
                        }
                        catch (Exception ex)
                        {
                            log("  " + ex.Message);
                        }
                    }
                }
            }

            // 3. start at logon
            string cmd = LogonTask.Command();
            if (cmd != null && Paths.IsUnder(cmd, dir)) LogonTask.Delete(log);

            // 4. shortcuts
            log("Shortcuts:");
            Shortcuts.Delete(Shortcuts.StartMenu, log);
            Shortcuts.Delete(Shortcuts.Desktop, log);

            // 5. PATH
            if (SystemPath.Contains(dir)) SystemPath.Remove(dir, log);

            // 6. Installed apps entry
            Installation inst = Installation.Find();
            if (inst == null || Paths.Same(inst.Location, dir))
            {
                Installation.Unregister();
                log("Removed from Settings > Apps > Installed apps.");
            }

            // 7. files
            log("Program files:");
            Payload.RemoveInstalled(dir, log);

            // 8. settings and logs
            if (removeData && Directory.Exists(Product.DataDir))
            {
                try
                {
                    Directory.Delete(Product.DataDir, true);
                    log("Deleted settings and logs: " + Product.DataDir);
                }
                catch (Exception ex)
                {
                    log("Could not delete " + Product.DataDir + ": " + ex.Message);
                }
            }
            log("Done. WSL itself, and any Linux distribution not removed above, were left in place (with the filesystem drivers " +
                "built inside them, /var/lib/wsl-modules). \"wsl --unregister <name>\" removes a distribution and everything in it.");
            return true;
        }

        /// <summary>Runs the program's command line with its output piped here (no console window).</summary>
        private static int RunCli(string exe, string arguments, Action<string> log)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            };
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler relay = (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) log("  " + e.Data.TrimEnd());
                };
                p.OutputDataReceived += relay;
                p.ErrorDataReceived += relay;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();   // no time limit (the program times out its own WSL calls; a flush must finish). Also drains the output events
                return p.ExitCode;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    //  WSL2 and a Linux distribution with the packages the program needs
    // ---------------------------------------------------------------------------------------
    internal sealed class WslResult
    {
        public int ExitCode;
        public string Output;
    }

    /// <summary>Runs wsl.exe with a time limit; output lines can be streamed to the log.</summary>
    internal static class WslRunner
    {
        // progress bars and counters ("[====  42.0%  ]", "(Reading database ... 35%"): noise in the log
        private static readonly Regex Progress = new Regex(@"^\[?[\s=#\-]*\d+(\.\d+)?\s*%|\d%\s*\]?\s*$");

        public static string Exe
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wsl.exe"); }
        }

        public static WslResult Run(string arguments, int timeoutSeconds, Action<string> onLine)
        {
            return RunProgram(Exe, arguments, timeoutSeconds, onLine);
        }

        public static WslResult RunProgram(string exe, string arguments, int timeoutSeconds, Action<string> onLine)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["WSL_UTF8"] = "1";   // wsl.exe's own messages in UTF-8, not UTF-16
            var output = new StringBuilder();
            object gate = new object();
            using (var p = new Process { StartInfo = psi })
            {
                DataReceivedEventHandler relay = (s, e) =>
                {
                    if (e.Data == null) return;
                    string line = e.Data.Replace("\0", string.Empty).Replace(((char)0xFEFF).ToString(), string.Empty).Trim();
                    lock (gate) output.AppendLine(line);
                    if (onLine != null && line.Length > 0 && !Progress.IsMatch(line)) onLine("  " + line);
                };
                p.OutputDataReceived += relay;
                p.ErrorDataReceived += relay;
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutSeconds * 1000))
                {
                    try { p.Kill(); } catch { }
                    throw new TimeoutException(Path.GetFileName(exe) + " " + arguments + " did not finish within " + (timeoutSeconds / 60) + " minutes.");
                }
                p.WaitForExit();
                lock (gate) return new WslResult { ExitCode = p.ExitCode, Output = output.ToString().Trim() };
            }
        }

        public static string LastLines(string text, int count)
        {
            string[] lines = (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", lines.Skip(Math.Max(0, lines.Length - count)));
        }
    }

    internal sealed class DistroChoice
    {
        public string Name;        // wsl --install -d NAME
        public string Title;
        public string Launcher;    // Store app launcher, only needed when --no-launch leaves it unregistered

        public DistroChoice(string name, string title, string launcher) { Name = name; Title = title; Launcher = launcher; }
    }

    internal sealed class DistroInfo
    {
        public string Name;
        public int Version;
        public bool IsDefault;
        public bool Checked;
        public string Error;
        public string PackageManager;   // zypper, apt-get, dnf, pacman
        public string OsId;
        public string OsVersion;
        public readonly List<string> Missing = new List<string>();

        public bool Ready { get { return Checked && Error == null && Missing.Count == 0; } }
        public bool Fixable { get { return Checked && Error == null && Missing.Count > 0 && PackageManager != null; } }
    }

    internal sealed class LinuxStatus
    {
        public bool WslInstalled;
        public bool WslCurrent;   // the Store WSL (wsl --version works)
        public readonly List<DistroInfo> Distros = new List<DistroInfo>();
        public DistroInfo Ready;
        public readonly List<string> Problems = new List<string>();

        public IEnumerable<DistroInfo> Wsl2 { get { return Distros.Where(d => d.Version == 2); } }
    }

    internal static class LinuxSetup
    {
        public const string GuidesUrl = "https://github.com/KenpoYogi/Btrfs_USB/blob/main/docs/distros/README.md";

        // first = default choice: openSUSE Tumbleweed (as in the guides); the Ubuntu choice is 26.04 (user decision 2026-09-26)
        public static readonly DistroChoice[] Choices =
        {
            new DistroChoice("openSUSE-Tumbleweed", "openSUSE Tumbleweed (recommended)", "openSUSE-Tumbleweed.exe"),
            new DistroChoice("Ubuntu-26.04", "Ubuntu 26.04 LTS", "ubuntu2604.exe"),
            new DistroChoice("kali-linux", "Kali Linux", "kali.exe"),
        };

        private static readonly Regex ListLine = new Regex(@"^\s*(\*)?\s*(\S+)\s+(\S+)\s+([12])\s*$");

        // tools the program runs in the distro, and the package that has each (Debian/Ubuntu names; zypper: btrfsprogs)
        private static readonly string[][] Tools = { new[] { "btrfs", "btrfs-progs" }, new[] { "blkid", "util-linux" }, new[] { "modinfo", "kmod" } };

        private const string CheckScript =
            "for c in btrfs blkid modinfo; do command -v $c >/dev/null 2>&1 || echo missing $c; done; " +
            "for m in zypper apt-get dnf pacman; do if command -v $m >/dev/null 2>&1; then echo pm $m; break; fi; done; " +
            ". /etc/os-release 2>/dev/null; echo os $ID $VERSION_ID";

        private static string RootShell(string distro, string script)
        {
            // the script has no double quotes, so wrapping it in them is enough for wsl.exe's parser
            return "-d " + distro + " -u root --exec sh -c \"" + script + "\"";
        }

        /// <summary>The package that has <paramref name="tool"/>; openSUSE spells btrfs-progs "btrfsprogs".</summary>
        public static string PackageFor(string tool, string packageManager = null)
        {
            string[] t = Tools.FirstOrDefault(x => x[0] == tool);
            string package = t != null ? t[1] : tool;
            return packageManager == "zypper" && package == "btrfs-progs" ? "btrfsprogs" : package;
        }

        private static List<DistroInfo> List()
        {
            var list = new List<DistroInfo>();
            WslResult r = WslRunner.Run("--list --verbose", 60, null);
            if (r.ExitCode != 0) return list;
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = ListLine.Match(line);
                if (!m.Success || m.Groups[2].Value.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new DistroInfo { Name = m.Groups[2].Value, IsDefault = m.Groups[1].Success, Version = int.Parse(m.Groups[4].Value) });
            }
            return list;
        }

        private static void CheckDistro(DistroInfo d)
        {
            WslResult r = WslRunner.Run(RootShell(d.Name, CheckScript), 180, null);
            d.Checked = true;
            if (r.ExitCode != 0)
            {
                d.Error = "could not start it (" + WslRunner.LastLines(r.Output, 2) + ")";
                return;
            }
            d.Missing.Clear();
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] w = line.Trim().Split(' ');
                if (w.Length >= 2 && w[0] == "missing") d.Missing.Add(w[1]);
                else if (w.Length >= 2 && w[0] == "pm") d.PackageManager = w[1];
                else if (w.Length >= 2 && w[0] == "os") { d.OsId = w[1]; d.OsVersion = w.Length >= 3 ? w[2] : string.Empty; }
            }
        }

        /// <summary>
        /// Is WSL installed and current, and does a WSL2 distro have btrfs, blkid and modinfo? Starting a distro boots the
        /// WSL VM, which is fine here: setup runs once.
        /// </summary>
        public static LinuxStatus Check(Action<string> log)
        {
            var s = new LinuxStatus();
            log("Checking WSL2 and Linux...");
            if (!File.Exists(WslRunner.Exe))
            {
                s.Problems.Add("WSL is not installed on this PC.");
                return s;
            }
            WslResult version = WslRunner.Run("--version", 60, null);
            if (version.ExitCode == 0)
            {
                s.WslInstalled = s.WslCurrent = true;
                log("  " + WslRunner.LastLines(version.Output.Split('\n')[0], 1));
            }
            else
            {
                s.WslInstalled = WslRunner.Run("--status", 60, null).ExitCode == 0;
                if (!s.WslInstalled)
                {
                    s.Problems.Add("WSL is not installed on this PC.");
                    return s;
                }
                s.Problems.Add("WSL is too old (it has no \"wsl --version\"). It needs the current WSL from \"wsl --update\".");
                return s;
            }

            s.Distros.AddRange(List());
            foreach (DistroInfo d in s.Distros.Where(x => x.Version == 1))
                s.Problems.Add(d.Name + " runs as WSL1; the program needs WSL2 (\"wsl --set-version " + d.Name + " 2\" converts it).");
            if (!s.Wsl2.Any())
            {
                s.Problems.Add("No WSL2 Linux distribution is installed.");
                return s;
            }
            foreach (DistroInfo d in s.Wsl2.OrderByDescending(x => x.IsDefault))
            {
                log("  checking " + d.Name + "...");
                CheckDistro(d);
                if (d.Ready)
                {
                    s.Ready = d;
                    log("  " + d.Name + ": ready (btrfs, blkid and modinfo found).");
                    return s;
                }
                s.Problems.Add(d.Error != null ? d.Name + ": " + d.Error + "."
                    : d.Name + " is missing " + string.Join(", ", d.Missing.Select(t => t + " (package " + PackageFor(t, d.PackageManager) + ")")) + ".");
            }
            return s;
        }

        /// <summary>wsl --install --no-distribution. True when Windows must restart before WSL works.</summary>
        public static bool InstallWsl(Action<string> log)
        {
            log("Installing WSL (wsl --install --no-distribution)...");
            WslResult r = WslRunner.Run("--install --no-distribution", 1800, log);
            if (r.ExitCode != 0 && r.ExitCode != 3010)
                throw new InvalidOperationException("wsl --install failed (exit " + r.ExitCode + "): " + WslRunner.LastLines(r.Output, 3));
            bool restart = r.ExitCode == 3010 || Regex.IsMatch(r.Output, "restart|reboot", RegexOptions.IgnoreCase);
            if (!restart) WslRunner.Run("--set-default-version 2", 60, log);
            return restart;
        }

        public static void UpdateWsl(Action<string> log)
        {
            log("Updating WSL (wsl --update)...");
            WslResult r = WslRunner.Run("--update", 1800, log);
            if (r.ExitCode != 0) throw new InvalidOperationException("wsl --update failed (exit " + r.ExitCode + "): " + WslRunner.LastLines(r.Output, 3));
            WslRunner.Run("--set-default-version 2", 60, log);
        }

        /// <summary>wsl --install -d NAME --no-launch, as WSL2. The program runs as root, so no Linux user is created.</summary>
        public static void InstallDistro(DistroChoice c, Action<string> log)
        {
            log("Installing " + c.Title.Replace(" (recommended)", string.Empty) + " (downloads a few hundred MB)...");
            WslResult r = WslRunner.Run("--install -d " + c.Name + " --no-launch", 1800, log);
            if (r.ExitCode != 0)
                throw new InvalidOperationException("wsl --install -d " + c.Name + " failed (exit " + r.ExitCode + "): " + WslRunner.LastLines(r.Output, 3));

            DistroInfo d = List().FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase));
            if (d == null)
            {
                // an older Store package registers only when first launched: its launcher can do that with root as the user
                string launcher = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", c.Launcher);
                if (File.Exists(launcher))
                {
                    log("Registering " + c.Name + " (" + c.Launcher + " install --root)...");
                    WslRunner.RunProgram(launcher, "install --root", 1800, log);
                    d = List().FirstOrDefault(x => string.Equals(x.Name, c.Name, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (d == null)
                throw new InvalidOperationException(c.Name + " was downloaded but is not registered yet. Open it once from the Start menu, " +
                                                    "then run this setup again to add the packages.");
            if (d.Version == 1)
            {
                log("Converting " + c.Name + " to WSL2...");
                WslResult v = WslRunner.Run("--set-version " + c.Name + " 2", 1800, log);
                if (v.ExitCode != 0) throw new InvalidOperationException("wsl --set-version " + c.Name + " 2 failed: " + WslRunner.LastLines(v.Output, 3));
            }
            log(c.Name + " is installed.");
        }

        /// <summary>
        /// Fresh distro (<paramref name="fresh"/>: this setup just installed it): updates it, installs the packages for btrfs,
        /// blkid and modinfo and the optional APFS reader. Existing distro: installs only the packages for the missing tools,
        /// nothing else (installing a present package would also upgrade it). As root; then checks again.
        /// </summary>
        public static void InstallPackages(string distro, bool fresh, Action<string> log)
        {
            var d = new DistroInfo { Name = distro };
            CheckDistro(d);
            if (d.Error != null) throw new InvalidOperationException(distro + ": " + d.Error);
            if (!fresh && d.Missing.Count == 0)
            {
                log(distro + " already has btrfs, blkid and modinfo.");
                return;
            }
            if (d.PackageManager == null) throw new InvalidOperationException(distro + " has none of zypper, apt-get, dnf or pacman; install btrfs-progs, util-linux and kmod by hand.");

            string pm = d.PackageManager;
            List<string> packages = (fresh ? Tools.Select(t => t[0]) : d.Missing).Select(t => PackageFor(t, pm)).Distinct().ToList();
            string names = string.Join(" ", packages);
            string script;
            switch (pm)
            {
                case "zypper":
                    script = "zypper --non-interactive --gpg-auto-import-keys refresh" +
                             (fresh ? " && zypper --non-interactive dup" : string.Empty) +
                             " && zypper --non-interactive install " + names +
                             (fresh ? "; zypper --non-interactive install libfsapfs || echo optional package libfsapfs not installed" : string.Empty);
                    break;
                case "apt-get":
                    // Kali is rolling: full-upgrade, as its guide says. libfsapfs-utils works on Ubuntu 24.04 only (not 26.04)
                    string upgrade = d.OsId == "kali" ? "full-upgrade" : "upgrade";
                    bool apfs = fresh && d.OsId == "ubuntu" && d.OsVersion == "24.04";
                    script = "export DEBIAN_FRONTEND=noninteractive; O='-q -y -o Dpkg::Options::=--force-confdef -o Dpkg::Options::=--force-confold'; " +
                             "apt-get -q update" +
                             (fresh ? " && apt-get $O " + upgrade : string.Empty) +
                             " && apt-get $O install " + names +
                             (apfs ? "; apt-get $O install libfsapfs-utils || echo optional package libfsapfs-utils not installed" : string.Empty);
                    break;
                case "dnf":
                    script = (fresh ? "dnf -y upgrade && " : string.Empty) + "dnf -y install " + names;
                    break;
                default:
                    script = "pacman -Sy" + (fresh ? "u" : string.Empty) + " --noconfirm --needed " + names;
                    break;
            }
            log((fresh ? "Updating " + distro + " and installing " : "Installing into " + distro + ": ") + string.Join(", ", packages) +
                " (this can take a while)...");
            WslResult r = WslRunner.Run(RootShell(distro, script), 3600, log);

            CheckDistro(d);
            if (!d.Ready)
                throw new InvalidOperationException("The packages could not be installed (exit " + r.ExitCode + "). " + distro + " is still missing " +
                                                    string.Join(", ", d.Missing) + ". See the guides for doing it by hand.");
            log(distro + " is ready: btrfs, blkid and modinfo found.");
        }

        /// <summary>Names of the installed distros (WSL1 and 2); empty when WSL is missing. Does not boot the VM.</summary>
        public static List<string> InstalledNames()
        {
            if (!File.Exists(WslRunner.Exe)) return new List<string>();
            try { return List().Select(d => d.Name).ToList(); }
            catch { return new List<string>(); }
        }

        /// <summary>wsl --unregister: deletes the distro and every file inside it.</summary>
        public static void RemoveDistro(string distro, Action<string> log)
        {
            log("Removing the Linux distribution " + distro + " (wsl --unregister)...");
            WslResult r = WslRunner.Run("--unregister " + distro, 600, log);
            if (r.ExitCode != 0)
                throw new InvalidOperationException("wsl --unregister " + distro + " failed (exit " + r.ExitCode + "): " + WslRunner.LastLines(r.Output, 3));
            log("  " + distro + " and everything in it was removed.");
        }

        public static void SetDefault(string distro, Action<string> log)
        {
            if (WslRunner.Run("--set-default " + distro, 60, null).ExitCode == 0) log(distro + " is now the default WSL distribution.");
        }
    }

    // ---------------------------------------------------------------------------------------
    //  Windows
    // ---------------------------------------------------------------------------------------
    internal abstract class SetupForm : Form
    {
        protected readonly TableLayoutPanel Rows;
        protected readonly TextBox LogBox;
        protected readonly FlowLayoutPanel Buttons;
        private bool busy;

        protected SetupForm(string title)
        {
            Text = title;
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 520);
            try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

            Rows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16, 14, 16, 10) };
            Rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            LogBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 8.5f),
                BackColor = SystemColors.Window,
                Visible = false,
                Margin = new Padding(0, 8, 0, 0)
            };

            Buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(12, 6, 12, 10)
            };
            Controls.Add(Rows);
            Controls.Add(Buttons);
        }

        protected void AddRow(Control c)
        {
            Rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Rows.Controls.Add(c, 0, Rows.RowCount++);
        }

        protected void AddLogRow()
        {
            Rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            Rows.Controls.Add(LogBox, 0, Rows.RowCount++);
        }

        protected static Label Caption(string text, float size = 9f, FontStyle style = FontStyle.Regular, Color? color = null)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(566, 0),
                Font = new Font("Segoe UI", size, style),
                ForeColor = color ?? SystemColors.ControlText,
                Margin = new Padding(0, 2, 0, 6)
            };
        }

        protected static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(96, 28), Margin = new Padding(6, 0, 0, 0) };
        }

        /// <summary>Thread-safe: appends a line to the log box.</summary>
        protected void Log(string line)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(Log), line); } catch (InvalidOperationException) { }
                return;
            }
            LogBox.Visible = true;
            LogBox.AppendText(line + Environment.NewLine);
        }

        /// <summary>Thread-safe Yes/No question.</summary>
        protected bool Ask(string question)
        {
            if (InvokeRequired) return (bool)Invoke(new Func<string, bool>(Ask), question);
            return MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        /// <summary>Runs <paramref name="work"/> off the UI thread; returns its exception, if any.</summary>
        protected async Task<Exception> RunBusy(Action work)
        {
            busy = true;
            UseWaitCursor = true;
            try
            {
                await Task.Run(work);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
            finally
            {
                busy = false;
                UseWaitCursor = false;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // never leave a half-finished install / unmount behind
                return;
            }
            base.OnFormClosing(e);
        }
    }

    internal sealed class InstallForm : SetupForm
    {
        private readonly Installation existing = Installation.Find();
        private readonly TextBox dirBox;
        private readonly CheckBox startMenu, desktop, addToPath, launch, checkLinux;
        private readonly Button installButton, cancelButton, uninstallButton, browseButton;
        private bool finished;

        public InstallForm() : base(Product.Name + " " + Product.Version + " Setup")
        {
            AddRow(Caption(Product.Name + " " + Product.Version, 14f, FontStyle.Bold));
            AddRow(Caption("Mounts Linux and Mac formatted USB drives (btrfs, ext2/3/4, XFS and more) on Windows through WSL2."));
            if (existing != null)
            {
                AddRow(Caption("Version " + existing.Version + " is installed in " + existing.Location + ". Setup will update it" +
                            " (a running copy is closed and started again; mounted drives stay mounted).", 9f, FontStyle.Regular, Color.FromArgb(0, 90, 160)));
            }

            AddRow(Caption("Install to:", 9f, FontStyle.Bold));
            var dirRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            dirRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            dirBox = new TextBox { Dock = DockStyle.Fill, Text = existing != null ? existing.Location : Product.DefaultDir, Margin = new Padding(0, 3, 6, 0) };
            browseButton = MakeButton("Browse...");
            browseButton.Margin = new Padding(0);
            browseButton.Click += (s, e) => Browse();
            dirRow.Controls.Add(dirBox, 0, 0);
            dirRow.Controls.Add(browseButton, 1, 0);
            AddRow(dirRow);

            startMenu = new CheckBox { Text = "Start menu shortcut", Checked = existing == null || existing.StartMenuShortcut, AutoSize = true };
            desktop = new CheckBox { Text = "Desktop shortcut", Checked = existing != null && existing.DesktopShortcut, AutoSize = true };
            addToPath = new CheckBox
            {
                Text = "Add the folder to the system PATH, so \"LinuxUsbMounter --list\" works in any terminal",
                Checked = existing != null && existing.AddedToPath,
                AutoSize = true
            };
            launch = new CheckBox { Text = "Start " + Product.Name + " when setup finishes", Checked = true, AutoSize = true };
            checkLinux = new CheckBox
            {
                Text = "Check the Linux setup in WSL2, and offer to set up a distribution if needed",
                Checked = true,
                AutoSize = true,
                MaximumSize = new Size(566, 0)
            };
            var options = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            options.Controls.AddRange(new Control[] { startMenu, desktop, addToPath, launch, checkLinux });
            AddRow(options);

            var license = new LinkLabel
            {
                Text = "MIT License with the Commons Clause: free to use, copy, modify and share; selling it is not permitted. Read the license",
                AutoSize = true,
                MaximumSize = new Size(566, 0),
                Margin = new Padding(0, 2, 0, 4)
            };
            license.LinkArea = new LinkArea(license.Text.Length - "Read the license".Length, "Read the license".Length);
            license.LinkClicked += (s, e) => ShowLicense();
            AddRow(license);

            foreach (string warning in Warnings()) AddRow(Caption(warning, 9f, FontStyle.Regular, Color.FromArgb(170, 70, 0)));
            AddLogRow();

            installButton = MakeButton(existing != null ? "Update" : "Install");
            cancelButton = MakeButton("Cancel");
            installButton.Click += async (s, e) => await InstallAsync();
            cancelButton.Click += (s, e) => Close();
            Buttons.Controls.Add(cancelButton);
            Buttons.Controls.Add(installButton);
            if (existing != null)
            {
                uninstallButton = MakeButton("Uninstall...");
                uninstallButton.Click += (s, e) =>
                {
                    if (Program.StartUninstaller(existing.Location)) Close();
                };
                Buttons.Controls.Add(uninstallButton);
            }
            AcceptButton = installButton;
            CancelButton = cancelButton;
        }

        private static IEnumerable<string> Warnings()
        {
            string wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wsl.exe");
            if (!File.Exists(wsl))
                yield return "WSL is not installed. The program needs it: after setup, run \"wsl --install\" in an administrator terminal and restart Windows.";
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                int release = k != null ? Convert.ToInt32(k.GetValue("Release", 0)) : 0;
                if (release < 528040) yield return "The program needs .NET Framework 4.8, which is not installed. Get it from Microsoft (Windows Update installs it on Windows 10 1903 and later).";
            }
        }

        private void Browse()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the folder to install " + Product.Name + " into.", ShowNewFolderButton = true })
            {
                try
                {
                    string current = dirBox.Text.Trim();
                    while (!string.IsNullOrEmpty(current) && !Directory.Exists(current)) current = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(current)) dlg.SelectedPath = current;
                }
                catch
                {
                    // start at the default location
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string chosen = dlg.SelectedPath;
                // a folder that already has other things in it gets its own sub-folder, like Program Files
                bool ownFolder = string.Equals(Path.GetFileName(chosen.TrimEnd('\\')), Product.DefaultFolderName, StringComparison.OrdinalIgnoreCase) ||
                                 File.Exists(Path.Combine(chosen, Product.ExeName)) ||
                                 (Directory.Exists(chosen) && !Directory.EnumerateFileSystemEntries(chosen).Any());
                dirBox.Text = ownFolder ? chosen : Path.Combine(chosen, Product.DefaultFolderName);
            }
        }

        private void ShowLicense()
        {
            string text = Payload.ReadText("LICENSE") ?? "The LICENSE file is missing from this setup.";
            using (var f = new Form
            {
                Text = "License",
                Font = Font,
                AutoScaleDimensions = new SizeF(96f, 96f),
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(640, 520),
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                ShowInTaskbar = false
            })
            {
                f.Controls.Add(new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9f),
                    BackColor = SystemColors.Window,
                    Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n")
                });
                f.ShowDialog(this);
            }
        }

        private async Task InstallAsync()
        {
            if (finished)
            {
                Close();
                return;
            }
            string problem = Paths.CheckInstallDir(dirBox.Text);
            if (problem != null)
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dirBox.Focus();
                return;
            }
            var o = new InstallOptions
            {
                Dir = Paths.Normalize(dirBox.Text),
                StartMenu = startMenu.Checked,
                Desktop = desktop.Checked,
                AddToPath = addToPath.Checked,
                Launch = launch.Checked
            };

            if (Directory.Exists(o.Dir) && !File.Exists(Path.Combine(o.Dir, Product.ExeName)) && Directory.EnumerateFileSystemEntries(o.Dir).Any())
            {
                if (MessageBox.Show(this, o.Dir + " already has other files in it. Install there anyway?\r\n\r\n" +
                                          "They are left alone, also when uninstalling.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            const string busyNote = "Mounted drives stay mounted. If it is busy (ejecting, scrubbing, checking a drive), that task is cancelled.";
            bool running = existing != null && AppProcesses.AnyRunning(existing.Location) || AppProcesses.AnyRunning(o.Dir);
            if (running && MessageBox.Show(this, Product.Name + " is running. Setup will close it" + (o.Launch ? " and start the new version afterwards" : string.Empty) +
                                                 ".\r\n\r\n" + busyNote + "\r\n\r\nContinue?",
                                                 Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            List<string> others = AppProcesses.FoldersOf(AppProcesses.Elsewhere(o.Dir, existing != null ? existing.Location : null));
            if (others.Count > 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    Product.Name + " is also running from " + string.Join(", ", others) + " (a copy this setup did not install). " +
                    "Only one copy runs at a time: while it runs, starting the new version just brings that one's window up.\r\n\r\n" +
                    "Yes = close it (its files are left alone). " + busyNote + "\r\nNo = leave it running\r\nCancel = don't install now",
                    Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return;
                o.CloseOtherCopies = answer == DialogResult.Yes;
            }

            foreach (Control c in new Control[] { dirBox, browseButton, startMenu, desktop, addToPath, launch, checkLinux, installButton, cancelButton })
                c.Enabled = false;
            if (uninstallButton != null) uninstallButton.Visible = false;

            Exception error = await RunBusy(() => Installer.Run(o, existing, Log));
            if (error != null)
            {
                Log("FAILED: " + error.Message);
                MessageBox.Show(this, "Setup failed: " + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                foreach (Control c in new Control[] { dirBox, browseButton, startMenu, desktop, addToPath, launch, checkLinux, installButton, cancelButton })
                    c.Enabled = true;
                return;
            }

            bool canStart = true;
            if (checkLinux.Checked) canStart = await LinuxStepAsync();

            finished = true;
            installButton.Text = "Close";
            installButton.Enabled = true;
            cancelButton.Visible = false;
            if (o.Launch && canStart)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(o.Dir, Product.ExeName)) { UseShellExecute = true, WorkingDirectory = o.Dir });
                    Log("Started " + Product.Name + ".");
                }
                catch (Exception ex)
                {
                    Log("Could not start " + Product.Name + ": " + ex.Message);
                }
            }
            installButton.Focus();
        }

        /// <summary>
        /// Checks WSL2 and the distros; if none is ready, asks how to set one up and does it. False when Windows must
        /// restart first (then starting the program is pointless).
        /// </summary>
        private async Task<bool> LinuxStepAsync()
        {
            for (int round = 0; round < 3; round++)
            {
                LinuxStatus status = null;
                Exception error = await RunBusy(() => status = LinuxSetup.Check(Log));
                if (error != null)
                {
                    Log("Could not check the Linux setup: " + error.Message);
                    return true;
                }
                if (status.Ready != null)
                {
                    if (!status.Ready.IsDefault)
                        Log("  " + status.Ready.Name + " is not the default WSL distribution: pick it in the program's \"WSL2 distro\" box.");
                    return true;
                }

                LinuxChoice choice;
                using (var dlg = new LinuxSetupDialog(status))
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        Log("Linux setup skipped. The program needs a WSL2 distribution with btrfs-progs, util-linux and kmod: see " + LinuxSetup.GuidesUrl);
                        return true;
                    }
                    choice = dlg.Choice;
                }

                if (choice.Action == LinuxAction.Manual)
                {
                    OpenGuides();
                    Log("Opened the step-by-step guides: " + LinuxSetup.GuidesUrl);
                    return true;
                }

                bool restart = false;
                error = await RunBusy(() =>
                {
                    switch (choice.Action)
                    {
                        case LinuxAction.InstallWsl:
                            restart = LinuxSetup.InstallWsl(Log);
                            break;
                        case LinuxAction.UpdateWsl:
                            LinuxSetup.UpdateWsl(Log);
                            break;
                        case LinuxAction.NewDistro:
                            LinuxSetup.InstallDistro(choice.Distro, Log);
                            Installation.AddSetupDistro(choice.Distro.Name);   // the uninstaller offers to remove it
                            LinuxSetup.InstallPackages(choice.Distro.Name, true, Log);
                            if (choice.SetDefault) LinuxSetup.SetDefault(choice.Distro.Name, Log);
                            Log("To use " + choice.Distro.Name + " yourself, open it from the Start menu (it may ask you to create a Linux user).");
                            break;
                        case LinuxAction.FixDistro:
                            LinuxSetup.InstallPackages(choice.Existing, false, Log);
                            break;
                    }
                });
                if (error != null)
                {
                    Log("FAILED: " + error.Message);
                    MessageBox.Show(this, error.Message + "\r\n\r\nThe step-by-step guides show how to do it by hand:\r\n" + LinuxSetup.GuidesUrl,
                                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                if (restart)
                {
                    Log("Restart Windows to finish installing WSL, then run this setup again to add a Linux distribution.");
                    MessageBox.Show(this, "WSL is installed. Restart Windows to finish, then run " + Product.SetupFileName +
                                          " again (choose Update) to add a Linux distribution.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                if (choice.Action != LinuxAction.UpdateWsl) return true;
                // after a WSL update: check again, which offers the distros
            }
            return true;
        }

        public static void OpenGuides()
        {
            try { Process.Start(new ProcessStartInfo(LinuxSetup.GuidesUrl) { UseShellExecute = true }); } catch { }
        }
    }

    internal enum LinuxAction { Manual, InstallWsl, UpdateWsl, NewDistro, FixDistro }

    internal sealed class LinuxChoice
    {
        public LinuxAction Action;
        public DistroChoice Distro;   // NewDistro
        public string Existing;       // FixDistro
        public bool SetDefault;
    }

    /// <summary>What to do when no WSL2 distro is ready: install WSL, add a distro, fix one, or set it up by hand.</summary>
    internal sealed class LinuxSetupDialog : Form
    {
        private readonly List<KeyValuePair<RadioButton, LinuxChoice>> options = new List<KeyValuePair<RadioButton, LinuxChoice>>();
        private readonly CheckBox makeDefault;

        public LinuxChoice Choice { get; private set; }

        public LinuxSetupDialog(LinuxStatus status)
        {
            Text = "Linux for " + Product.Name;
            Font = new Font("Segoe UI", 9f);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var rows = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16, 14, 16, 8) };
            Action<Control> add = c =>
            {
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                rows.Controls.Add(c, 0, rows.RowCount++);
            };
            Func<string, float, FontStyle, Color, Label> label = (text, size, style, color) => new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(540, 0),
                Font = new Font("Segoe UI", size, style),
                ForeColor = color,
                Margin = new Padding(0, 2, 0, 6)
            };

            add(label(Product.Name + " needs a Linux distribution in WSL2", 12f, FontStyle.Bold, SystemColors.ControlText));
            foreach (string p in status.Problems) add(label(char.ConvertFromUtf32(0x2022) + " " + p, 9f, FontStyle.Regular, Color.FromArgb(170, 70, 0)));
            add(label("What should setup do?", 9f, FontStyle.Bold, SystemColors.ControlText));

            makeDefault = new CheckBox
            {
                Text = "Make it the default WSL distribution",
                AutoSize = true,
                Checked = !status.Wsl2.Any(d => d.IsDefault),
                Margin = new Padding(20, 0, 0, 6)
            };

            if (!status.WslInstalled)
            {
                AddOption(add, "Install WSL now", new LinuxChoice { Action = LinuxAction.InstallWsl },
                          "Windows then needs a restart. Run this setup again afterwards (choose Update) to add a Linux distribution.");
            }
            else if (!status.WslCurrent)
            {
                AddOption(add, "Update WSL now (wsl --update)", new LinuxChoice { Action = LinuxAction.UpdateWsl },
                          "Then setup checks again and offers the Linux distributions.");
            }
            else
            {
                foreach (DistroInfo d in status.Wsl2.Where(x => x.Fixable))
                {
                    AddOption(add, "Install the missing packages into " + d.Name + " (" +
                                   string.Join(", ", d.Missing.Select(t => LinuxSetup.PackageFor(t, d.PackageManager)).Distinct()) + ")",
                              new LinuxChoice { Action = LinuxAction.FixDistro, Existing = d.Name });
                }
                foreach (DistroChoice c in LinuxSetup.Choices.Where(c => !status.Distros.Any(d => string.Equals(d.Name, c.Name, StringComparison.OrdinalIgnoreCase))))
                {
                    AddOption(add, "Install " + c.Title + " with the packages it needs", new LinuxChoice { Action = LinuxAction.NewDistro, Distro = c });
                }
                add(makeDefault);
                add(label("A new distribution downloads a few hundred MB, is brought up to date and gets btrfs-progs, util-linux and kmod " +
                          "(openSUSE also libfsapfs, for Mac drives). That takes about 5 to 20 minutes. Setup does it as root, so no Linux " +
                          "user account is needed.", 8.5f, FontStyle.Regular, SystemColors.GrayText));
            }
            AddOption(add, "I'll set it up myself (opens the step-by-step guides)", new LinuxChoice { Action = LinuxAction.Manual },
                      "Guides for openSUSE, Ubuntu, Kali, Debian, Fedora, Arch, CentOS / AlmaLinux and SLES.");

            var guides = new LinkLabel { Text = "Step-by-step guides on GitHub", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            guides.LinkClicked += (s, e) => InstallForm.OpenGuides();
            add(guides);

            var ok = new Button { Text = "Continue", AutoSize = true, MinimumSize = new Size(96, 28), Margin = new Padding(6, 0, 0, 0) };
            var skip = new Button { Text = "Skip", AutoSize = true, MinimumSize = new Size(96, 28), Margin = new Padding(6, 0, 0, 0), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) =>
            {
                KeyValuePair<RadioButton, LinuxChoice> picked = options.FirstOrDefault(x => x.Key.Checked);
                if (picked.Key == null) return;
                Choice = picked.Value;
                Choice.SetDefault = Choice.Action == LinuxAction.NewDistro && makeDefault.Checked;
                DialogResult = DialogResult.OK;
            };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(skip);
            buttons.Controls.Add(ok);
            add(buttons);

            Controls.Add(rows);
            AcceptButton = ok;
            CancelButton = skip;
            if (options.Count > 0) options[0].Key.Checked = true;
            UpdateDefaultBox();
        }

        /// <summary>A radio button (kept to one line: they don't wrap) with an optional wrapped note under it.</summary>
        private void AddOption(Action<Control> add, string text, LinuxChoice choice, string detail = null)
        {
            var rb = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(4, 2, 0, detail == null ? 4 : 0) };
            rb.CheckedChanged += (s, e) => UpdateDefaultBox();
            options.Add(new KeyValuePair<RadioButton, LinuxChoice>(rb, choice));
            add(rb);
            if (detail != null)
            {
                add(new Label
                {
                    Text = detail,
                    AutoSize = true,
                    MaximumSize = new Size(515, 0),
                    Font = new Font("Segoe UI", 8.5f),
                    ForeColor = SystemColors.GrayText,
                    Margin = new Padding(22, 0, 0, 6)
                });
            }
        }

        private void UpdateDefaultBox()
        {
            if (makeDefault == null) return;
            KeyValuePair<RadioButton, LinuxChoice> picked = options.FirstOrDefault(x => x.Key.Checked);
            makeDefault.Enabled = picked.Key != null && picked.Value.Action == LinuxAction.NewDistro;
        }
    }

    internal sealed class UninstallForm : SetupForm
    {
        private static readonly string WarnSign = char.ConvertFromUtf32(0x26A0);

        private readonly string dir;
        private readonly CheckBox removeData;
        private readonly List<CheckBox> distroBoxes = new List<CheckBox>();
        private readonly Button uninstallButton, cancelButton;
        private readonly bool running;
        private readonly List<string> mounted;
        private bool finished;

        public UninstallForm(string dir) : this(dir, Installation.SetupDistros()) { }

        /// <param name="setupDistros">WSL distros setup installed for this user; those still present get a checkbox.</param>
        internal UninstallForm(string dir, List<string> setupDistros) : base("Uninstall " + Product.Name)
        {
            this.dir = dir;
            running = AppProcesses.AnyRunning(dir);
            List<string> drives = MountedDrives.Read();
            List<string> others = AppProcesses.FoldersOf(AppProcesses.Elsewhere(dir));
            // with another copy running, Uninstaller.Run leaves the drives to it
            mounted = others.Count > 0 ? new List<string>() : drives;

            AddRow(Caption("Uninstall " + Product.Name, 14f, FontStyle.Bold));
            AddRow(Caption("Removes the program from " + dir + ", its Start menu and desktop shortcuts, start at logon and its entry in Installed apps."));

            var warn = Color.FromArgb(170, 70, 0);
            if (running)
                AddRow(Caption(WarnSign + " " + Product.Name + " is running. It will be closed. If it is busy (ejecting, scrubbing or checking a drive), that task is cancelled.", 9f, FontStyle.Regular, warn));
            if (mounted.Count > 0)
                AddRow(Caption(WarnSign + " " + mounted.Count + " drive(s) are mounted: " + string.Join(", ", mounted) + ". They will be unmounted first. " +
                            "Pending writes are flushed, which can take a while. Do not unplug them until this window says they are unmounted.", 9f, FontStyle.Regular, warn));
            if (drives.Count > 0 && others.Count > 0)
                AddRow(Caption("Mounted drives (" + string.Join(", ", drives) + ") stay mounted: " + Product.Name + " is also running from " +
                               string.Join(", ", others) + " and keeps managing them."));
            if (!running && drives.Count == 0)
                AddRow(Caption(Product.Name + " is not running and no drives are mounted."));

            // what to remove: the Windows app always; the Linux distros setup installed only if ticked (off by default)
            List<string> present = setupDistros.Count == 0 ? new List<string>() : LinuxSetup.InstalledNames();
            List<string> offered = setupDistros.Where(d => present.Contains(d, StringComparer.OrdinalIgnoreCase)).ToList();
            if (offered.Count > 0)
            {
                AddRow(Caption("What to remove:", 9f, FontStyle.Bold));
                AddRow(new CheckBox { Text = "The Windows app, " + Product.Name, Checked = true, Enabled = false, AutoSize = true, Margin = new Padding(0, 0, 0, 2) });
                foreach (string d in offered)
                {
                    var box = new CheckBox { Text = "The WSL Linux distribution " + d + ", which setup installed", Tag = d, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
                    distroBoxes.Add(box);
                    AddRow(box);
                    AddRow(new Label
                    {
                        Text = "Deletes " + d + " and every file inside it (wsl --unregister), including files you saved there and the " +
                               "filesystem drivers built in it. This can't be undone. Leave it unticked to keep using it.",
                        AutoSize = true,
                        MaximumSize = new Size(545, 0),
                        Font = new Font("Segoe UI", 8.5f),
                        ForeColor = SystemColors.GrayText,
                        Margin = new Padding(18, 0, 0, 4)
                    });
                }
            }

            removeData = new CheckBox
            {
                Text = "Also delete my settings and logs (" + Product.DataDir + ")",
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0)
            };
            AddRow(removeData);
            AddLogRow();

            uninstallButton = MakeButton("Uninstall");
            cancelButton = MakeButton("Cancel");
            uninstallButton.Click += async (s, e) => await UninstallAsync();
            cancelButton.Click += (s, e) => Close();
            Buttons.Controls.Add(cancelButton);
            Buttons.Controls.Add(uninstallButton);
            CancelButton = cancelButton;
        }

        private async Task UninstallAsync()
        {
            if (finished)
            {
                Close();
                return;
            }
            List<string> removeDistros = distroBoxes.Where(b => b.Checked).Select(b => (string)b.Tag).ToList();
            if ((running || mounted.Count > 0 || removeDistros.Count > 0) &&
                MessageBox.Show(this, (running ? Product.Name + " will be closed.\r\n" : string.Empty) +
                                      (mounted.Count > 0 ? mounted.Count + " mounted drive(s) will be flushed and unmounted: " + string.Join(", ", mounted) + ".\r\n" : string.Empty) +
                                      (removeDistros.Count > 0 ? "The WSL Linux distribution " + string.Join(", ", removeDistros) +
                                                                 " will be DELETED with every file inside it. This can't be undone.\r\n" : string.Empty) +
                                      "\r\nContinue?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;

            var inputs = new List<Control>(distroBoxes) { uninstallButton, cancelButton, removeData };
            foreach (Control c in inputs) c.Enabled = false;
            bool completed = false;
            bool remove = removeData.Checked;
            Exception error = await RunBusy(() => completed = Uninstaller.Run(dir, remove, removeDistros, Ask, Log));
            if (error != null)
            {
                Log("FAILED: " + error.Message);
                MessageBox.Show(this, "Uninstall failed: " + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                foreach (Control c in inputs) c.Enabled = true;
                return;
            }
            finished = true;
            uninstallButton.Text = "Close";
            uninstallButton.Enabled = true;
            cancelButton.Visible = false;
            if (completed) Log(Product.Name + " has been uninstalled.");
            uninstallButton.Focus();
        }
    }
}
