// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace XnixUsbMounter.Core
{
    /// <summary>Environment details written to mounter.log at startup, for troubleshooting.</summary>
    public static class Diagnostics
    {
        /// <summary>Synchronous part: never starts processes, never touches the drives.</summary>
        public static void LogStartup(string mode, string[] args)
        {
            try
            {
                Assembly entry = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                string version = entry.GetName().Version.ToString();
                string exe = AppPaths.ExePath;
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "==== Xnix USB Mounter {0} starting ({1}) ====", version, mode);
                Line(sb, "Command line", Environment.CommandLine);
                Line(sb, "Arguments", args == null || args.Length == 0 ? "(none)" : string.Join(" | ", args));
                Line(sb, "Executable", exe + FileStamp(exe));
                Line(sb, "Process", string.Format(CultureInfo.InvariantCulture, "PID {0}, {1}-bit, session {2}",
                    Process.GetCurrentProcess().Id, Environment.Is64BitProcess ? 64 : 32, Process.GetCurrentProcess().SessionId));
                Line(sb, "Windows", WindowsVersion() + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit"));
                Line(sb, ".NET", string.Format(CultureInfo.InvariantCulture, "CLR {0}, Framework release {1}",
                    Environment.Version, FrameworkRelease()));
                Line(sb, "User", UserText());
                Line(sb, "Culture", CultureInfo.CurrentCulture.Name + " / UI " + CultureInfo.CurrentUICulture.Name);
                string wsl = AppPaths.WslExe;
                Line(sb, "wsl.exe", wsl + (File.Exists(wsl) ? FileStamp(wsl) : "  (NOT FOUND)"));
                Line(sb, "Log file", AppPaths.LogFile);
                Line(sb, "State file", AppPaths.StateFile + (File.Exists(AppPaths.StateFile) ? FileStamp(AppPaths.StateFile) : "  (none yet)"));
                Log.Debug(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.DebugException("Could not collect startup diagnostics", ex);
            }
        }

        /// <summary>Asynchronous part: WSL's own version report (one short wsl.exe call).</summary>
        public static async Task LogWslInfoAsync(CancellationToken ct)
        {
            try
            {
                WslResult r = await Wsl.RunAsync(new[] { "--version" }, 20, ct).ConfigureAwait(false);
                if (r.ExitCode != 0)
                {
                    Log.Debug("wsl --version is not supported here (inbox WSL?). Consider \"wsl --update\".");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.DebugException("wsl --version failed", ex);
            }
        }

        private static void Line(StringBuilder sb, string name, string value)
        {
            sb.Append(Environment.NewLine).Append("  ").Append(name.PadRight(13)).Append(value);
        }

        private static string FileStamp(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return string.Empty;
                string ver = string.Empty;
                if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    FileVersionInfo fv = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrEmpty(fv.FileVersion)) ver = ", version " + fv.FileVersion;
                }
                return string.Format(CultureInfo.InvariantCulture, "  ({0:N0} bytes, modified {1:yyyy-MM-dd HH:mm:ss}{2})",
                    fi.Length, fi.LastWriteTime, ver);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string WindowsVersion()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string build = Convert.ToString(k.GetValue("CurrentBuildNumber"));
                        object ubr = k.GetValue("UBR");
                        string name = Convert.ToString(k.GetValue("ProductName"));
                        int b;
                        // ProductName still says "Windows 10" on Windows 11
                        if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        return string.Format(CultureInfo.InvariantCulture, "{0} {1}, build {2}.{3}",
                            name, k.GetValue("DisplayVersion"), build, ubr);
                    }
                }
            }
            catch
            {
                // fall back below
            }
            return Environment.OSVersion.VersionString;
        }

        private static string FrameworkRelease()
        {
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                           .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    object release = k != null ? k.GetValue("Release") : null;
                    if (release != null) return Convert.ToString(release, CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                // unknown
            }
            return "unknown";
        }

        private static string UserText()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    bool admin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
                    return id.Name + (admin ? ", elevated" : ", NOT elevated");
                }
            }
            catch
            {
                return Environment.UserName;
            }
        }
    }
}
