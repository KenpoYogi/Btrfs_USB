using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace BtrfsUsbMounter.Core
{
    /// <summary>Well-known file locations. Shared with the earlier PowerShell version, so settings carry over.</summary>
    public static class AppPaths
    {
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
        Ok
    }

    /// <summary>Thread-safe logger: appends to mounter.log (rotated at 5 MB) and raises <see cref="Written"/>.</summary>
    public static class Log
    {
        private const long MaxLogBytes = 5L * 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Raised on the calling thread for every line. Subscribers must marshal to their own thread.</summary>
        public static event Action<string, LogLevel> Written;

        public static void Info(string message)  { Write(message, LogLevel.Info); }
        public static void Warn(string message)  { Write(message, LogLevel.Warn); }
        public static void Error(string message) { Write(message, LogLevel.Error); }
        public static void Ok(string message)    { Write(message, LogLevel.Ok); }

        public static void Write(string message, LogLevel level)
        {
            string line = string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1,-5}  {2}",
                DateTime.Now, level.ToString().ToUpperInvariant(), message);
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
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, Utf8NoBom);
                }
                catch
                {
                    // logging must never take the application down
                }
            }
            Action<string, LogLevel> handler = Written;
            if (handler != null)
            {
                try { handler(line, level); } catch { }
            }
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
