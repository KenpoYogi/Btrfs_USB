// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace XnixUsbMounter.Core
{
    /// <summary>Copyright and license notices, shared by the window, the command line and the log.</summary>
    public static class AppInfo
    {
        public const string Copyright = "Copyright (c) 2026 Jay Weiner";
        public const string LicenseName = "MIT License with the Commons Clause License Condition v1.0";
        public const string LicenseUrl = "https://commonsclause.com/";
        public const string LicenseSummary = "Free to use, copy, modify and share. Selling it, or a product or service whose value derives substantially from it, is not permitted.";
        public const string NoWarranty = "Provided as is, without warranty or liability of any kind (see the license).";

        /// <summary>Full license text shipped next to the program.</summary>
        public static string LicenseFile
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LICENSE"); }
        }
    }

    /// <summary>Well-known file locations. Shared with the earlier PowerShell version, so settings carry over.</summary>
    public static class AppPaths
    {
        // keeps the pre-rename folder name on purpose: state.json and the log carry over to XnixUsbMounter
        public static readonly string AppDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BtrfsUsbMounter");

        public static readonly string StateFile = Path.Combine(AppDir, "state.json");
        public static readonly string LogFile   = Path.Combine(AppDir, "mounter.log");
        public static readonly string ChecksDir = Path.Combine(AppDir, "checks");

        public static string ExePath
        {
            get
            {
                Assembly entry = Assembly.GetEntryAssembly();
                return entry != null ? entry.Location : string.Empty;
            }
        }

        /// <summary>Test hook: replaces wsl.exe (null in normal use).</summary>
        public static string WslExeOverride { get; set; }

        /// <summary>Full path of wsl.exe (Sysnative only matters for a 32-bit process).</summary>
        public static string WslExe
        {
            get
            {
                if (!string.IsNullOrEmpty(WslExeOverride)) return WslExeOverride;
                string windows   = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string sysnative = Path.Combine(windows, "Sysnative", "wsl.exe");
                if (File.Exists(sysnative)) return sysnative;
                return Path.Combine(windows, "System32", "wsl.exe");
            }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(AppDir);
        }

        public static string EnsureChecksDir()
        {
            Directory.CreateDirectory(ChecksDir);
            return ChecksDir;
        }
    }

    public enum LogLevel
    {
        Info,
        Warn,
        Error,
        Ok,
        Debug     // troubleshooting detail: always in mounter.log, shown on screen only on request
    }

    /// <summary>
    /// Thread-safe logger: appends to mounter.log (rotated at 10 MB, one .old copy kept) and raises
    /// <see cref="Written"/>. File lines carry the date, milliseconds and thread id; screen lines are short.
    /// </summary>
    public static class Log
    {
        private const long MaxLogBytes = 10L * 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Raised on the calling thread for every line, with the short screen form of the line.
        /// Subscribers must marshal to their own thread and should skip <see cref="LogLevel.Debug"/>
        /// unless the user asked for detail.
        /// </summary>
        public static event Action<string, LogLevel> Written;

        public static void Info(string message)  { Write(message, LogLevel.Info); }
        public static void Warn(string message)  { Write(message, LogLevel.Warn); }
        public static void Error(string message) { Write(message, LogLevel.Error); }
        public static void Ok(string message)    { Write(message, LogLevel.Ok); }
        public static void Debug(string message) { Write(message, LogLevel.Debug); }

        /// <summary>Short message as an error, plus the full exception (type, stack, inner exceptions) as debug.</summary>
        public static void Exception(string context, Exception ex)
        {
            Error(context + ": " + Fmt.Root(ex).Message);
            Debug(context + " - exception details:" + Environment.NewLine + ex);
        }

        /// <summary>Full exception as debug only, for failures that are handled and expected now and then.</summary>
        public static void DebugException(string context, Exception ex)
        {
            Debug(context + ": " + ex.GetType().Name + ": " + Fmt.Root(ex).Message + Environment.NewLine + ex);
        }

        public static void Write(string message, LogLevel level)
        {
            message = message ?? string.Empty;
            DateTime now = DateTime.Now;
            string levelText = level.ToString().ToUpperInvariant();
            string screen = string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1,-5}  {2}", now, levelText, message);
            string prefix = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,3}] {2,-5}  ",
                now, System.Threading.Thread.CurrentThread.ManagedThreadId, levelText);
            // continuation lines of multi-line messages are indented under the message text
            string file = prefix + message.Replace("\r\n", "\n").Replace("\n", Environment.NewLine + new string(' ', prefix.Length));
            lock (Gate)
            {
                try
                {
                    var info = new FileInfo(AppPaths.LogFile);
                    if (info.Exists && info.Length > MaxLogBytes)
                    {
                        string old = AppPaths.LogFile + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(AppPaths.LogFile, old);
                    }
                    File.AppendAllText(AppPaths.LogFile, file + Environment.NewLine, Utf8NoBom);
                }
                catch
                {
                    // logging must never take the application down
                }
            }
            Action<string, LogLevel> handler = Written;
            if (handler != null)
            {
                try { handler(screen, level); } catch { }
            }
        }

        /// <summary>First lines / characters of command output, indented, for debug lines. Empty stays empty.</summary>
        public static string Excerpt(string text, int maxLines, int maxChars)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            int shown = 0;
            foreach (string l in lines)
            {
                if (shown >= maxLines || sb.Length + l.Length > maxChars) break;
                sb.Append(Environment.NewLine).Append("    ").Append(l);
                shown++;
            }
            if (shown < lines.Length)
            {
                sb.Append(Environment.NewLine).Append(string.Format(CultureInfo.InvariantCulture,
                    "    ... ({0} more lines, {1} characters in total)", lines.Length - shown, text.Length));
            }
            return sb.ToString();
        }
    }

    public static class Fmt
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>Human-readable size; empty for zero or negative values.</summary>
        public static string Bytes(double bytes)
        {
            if (bytes <= 0) return string.Empty;
            int i = 0;
            while (bytes >= 1024 && i < Units.Length - 1)
            {
                bytes /= 1024;
                i++;
            }
            return bytes.ToString("N1", CultureInfo.CurrentCulture) + " " + Units[i];
        }

        /// <summary>Human-readable size; "0 B" for zero.</summary>
        public static string BytesZero(double bytes)
        {
            return bytes <= 0 ? "0 B" : Bytes(bytes);
        }

        public static string SpaceText(VolumeInfo volume)
        {
            if (volume == null || volume.SpaceTotal <= 0) return string.Empty;
            if (volume.SpaceFree < 0) return "size " + Bytes(volume.SpaceTotal);
            return string.Format(CultureInfo.CurrentCulture, "{0}{1} free of {2}",
                volume.SpaceApprox ? "~" : string.Empty, BytesZero(volume.SpaceFree), Bytes(volume.SpaceTotal));
        }

        public static Exception Root(Exception ex)
        {
            while (ex is AggregateException && ex.InnerException != null) ex = ex.InnerException;
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
