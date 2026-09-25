// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace XnixUsbMounter.Launcher
{
    /// <summary>
    /// XnixUsbMounter.com: console front end for XnixUsbMounter.exe.
    /// The exe is a GUI program, so cmd.exe and PowerShell don't wait for it: its output lands after
    /// the next prompt and %ERRORLEVEL% / $LASTEXITCODE are wrong. Typing "XnixUsbMounter" without an
    /// extension picks this .com first (PATHEXT order). It runs the exe in this console, waits, and
    /// returns the exe's exit code. Without command-line switches it just opens the window.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XnixUsbMounter.exe");
            if (!File.Exists(exe))
            {
                Console.Error.WriteLine("XnixUsbMounter.exe not found next to this launcher: " + exe);
                return 1;
            }

            string arguments = RawArguments();
            bool gui = IsGuiLaunch(args);
            bool elevated = IsElevated();

            var psi = new ProcessStartInfo(exe, arguments)
            {
                // Elevated console: run the exe in this console (it attaches to it) and wait.
                // Otherwise the shell handles the UAC prompt; the elevated exe then opens its own
                // console window and waits for Enter, as when it is started directly.
                UseShellExecute = gui || !elevated,
                WorkingDirectory = Environment.CurrentDirectory,
            };

            // Ctrl+C reaches both processes; let the exe decide, and keep waiting for its exit code.
            Console.CancelKeyPress += (s, e) => e.Cancel = true;

            Process process;
            try
            {
                process = Process.Start(psi);
            }
            catch (Win32Exception ex)
            {
                Console.Error.WriteLine(ex.NativeErrorCode == 1223
                    ? "Cancelled: administrator rights are required."
                    : "Could not start XnixUsbMounter.exe: " + ex.Message);
                return 1;
            }

            if (gui || process == null) return 0;
            using (process)
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        /// <summary>True when the exe would only open the window (no args, or just --tray / --distro).</summary>
        private static bool IsGuiLaunch(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i].TrimStart('-', '/').Replace("-", string.Empty).ToLowerInvariant();
                if (key == "tray" || key == "verbose" || key == "v") continue;
                if (key == "distro" && i + 1 < args.Length) { i++; continue; }
                return false;
            }
            return true;
        }

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>The command line after the program name, passed on exactly as typed.</summary>
        private static string RawArguments()
        {
            string line = Environment.CommandLine;
            int i = 0;
            if (line.Length > 0 && line[0] == '"')
            {
                int close = line.IndexOf('"', 1);
                i = close < 0 ? line.Length : close + 1;
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            }
            return line.Substring(i).TrimStart();
        }
    }
}
