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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BtrfsUsbMounter.Core
{
    /// <summary>Runs wsl.exe asynchronously with timeouts and cooperative cancellation.</summary>
    public static class Wsl
    {
        private static readonly char[] QuoteTriggers = { ' ', '\t', '\n', '\v', '"' };
        private static int callSeq;

        /// <summary>Debug log of how a call ended; more output is kept when it failed.</summary>
        private static void LogEnd(int id, string what, Stopwatch sw, string output, string error, bool failed)
        {
            int lines = failed ? 60 : 12, chars = failed ? 6000 : 1500;
            string outText = Log.Excerpt(output, lines, chars);
            string errText = Log.Excerpt(error, lines, chars);
            Log.Debug(string.Format(CultureInfo.InvariantCulture, "wsl#{0} {1} after {2:N0} ms{3}{4}", id, what, sw.ElapsedMilliseconds,
                outText.Length > 0 ? Environment.NewLine + "  stdout:" + outText : string.Empty,
                errText.Length > 0 ? Environment.NewLine + "  stderr:" + errText : string.Empty));
        }

        private static void StartProcess(Process p, int id)
        {
            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                Log.DebugException(string.Format(CultureInfo.InvariantCulture, "wsl#{0} could not start {1}", id, p.StartInfo.FileName), ex);
                throw;
            }
        }

        /// <summary>Quotes one argument per the Windows (CommandLineToArgvW) rules that wsl.exe uses.</summary>
        public static string QuoteArgument(string arg)
        {
            if (arg == null) arg = string.Empty;
            if (arg.Length > 0 && arg.IndexOfAny(QuoteTriggers) < 0) return arg;

            var sb = new StringBuilder("\"");
            for (int i = 0; i < arg.Length; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\')
                {
                    backslashes++;
                    i++;
                }
                if (i == arg.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                if (arg[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string JoinArguments(IEnumerable<string> args)
        {
            return string.Join(" ", args.Select(QuoteArgument));
        }

        private static ProcessStartInfo CreateStartInfo(IEnumerable<string> args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppPaths.WslExe,
                Arguments = JoinArguments(args),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            // makes wsl.exe's own messages UTF-8 instead of UTF-16
            psi.EnvironmentVariables["WSL_UTF8"] = "1";
            return psi;
        }

        /// <summary>Removes NULs (UTF-16 leftovers of older WSL builds) and BOMs.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\0", string.Empty).Replace("\uFEFF", string.Empty).Trim();
        }

        private static void TryKill(Process p)
        {
            try
            {
                if (!p.HasExited) p.Kill();
            }
            catch
            {
                // already gone
            }
        }

        public static async Task<WslResult> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
        {
            var argList = args.ToList();
            int id = Interlocked.Increment(ref callSeq);
            using (var p = new Process { StartInfo = CreateStartInfo(argList), EnableRaisingEvents = true })
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "wsl#{0} run (timeout {1:N0} s): wsl.exe {2}",
                    id, timeout.TotalSeconds, p.StartInfo.Arguments));
                Stopwatch sw = Stopwatch.StartNew();
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                p.Exited += (s, e) => exited.TrySetResult(true);
                StartProcess(p, id);
                Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                Task<string> errTask = p.StandardError.ReadToEndAsync();

                using (var timeoutCts = new CancellationTokenSource(timeout))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token))
                {
                    Task stopper = Task.Delay(Timeout.Infinite, linked.Token);
                    Task first = await Task.WhenAny(exited.Task, stopper).ConfigureAwait(false);
                    if (first != exited.Task && !p.HasExited)
                    {
                        TryKill(p);
                        Log.Debug(string.Format(CultureInfo.InvariantCulture, "wsl#{0} {1} after {2:N0} ms; process killed",
                            id, ct.IsCancellationRequested ? "cancelled" : "TIMED OUT", sw.ElapsedMilliseconds));
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                        throw new TimeoutException(string.Format(
                            "wsl.exe timed out after {0:N0} s: {1}", timeout.TotalSeconds, JoinArguments(argList)));
                    }
                    linked.Cancel();   // release the stopper task
                }

                p.WaitForExit();
                string output = await outTask.ConfigureAwait(false);
                string error = await errTask.ConfigureAwait(false);
                var result = new WslResult { ExitCode = p.ExitCode, Output = Clean(output), Error = Clean(error) };
                LogEnd(id, "exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture), sw, result.Output, result.Error, result.ExitCode != 0);
                return result;
            }
        }

        public static Task<WslResult> RunAsync(IEnumerable<string> args, int timeoutSeconds, CancellationToken ct)
        {
            return RunAsync(args, TimeSpan.FromSeconds(timeoutSeconds), ct);
        }

        /// <summary>Runs a shell command inside the distro as root.</summary>
        public static Task<WslResult> RunRootAsync(string distro, string command, int timeoutSeconds, CancellationToken ct)
        {
            return RunAsync(RootArgs(distro, command), TimeSpan.FromSeconds(timeoutSeconds), ct);
        }

        public static string[] RootArgs(string distro, string command)
        {
            return new[] { "-d", distro, "-u", "root", "--exec", "sh", "-c", command };
        }

        /// <summary>
        /// Runs wsl.exe and hands every output line to <paramref name="onLine"/> as it arrives.
        /// Cancellation and timeout stop the process and return (they do not throw).
        /// </summary>
        public static async Task<StreamResult> RunStreamingAsync(IEnumerable<string> args, Action<string> onLine,
            TimeSpan timeout, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref callSeq);
            using (var p = new Process { StartInfo = CreateStartInfo(args) })
            {
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "wsl#{0} stream (timeout {1:N0} s): wsl.exe {2}",
                    id, timeout.TotalSeconds, p.StartInfo.Arguments));
                Stopwatch sw = Stopwatch.StartNew();
                StartProcess(p, id);
                Task<string> errTask = p.StandardError.ReadToEndAsync();
                var lines = new List<string>();
                DateTime deadline = DateTime.UtcNow + timeout;
                bool cancelled = false, timedOut = false;

                Task<string> pending = p.StandardOutput.ReadLineAsync();
                while (true)
                {
                    Task first = await Task.WhenAny(pending, Task.Delay(250)).ConfigureAwait(false);
                    if (first == pending)
                    {
                        string line = pending.Result;
                        if (line == null) break;
                        int cr = line.LastIndexOf('\r');
                        if (cr >= 0) line = line.Substring(cr + 1);
                        line = line.Replace("\0", string.Empty);
                        if (line.Trim().Length > 0)
                        {
                            lines.Add(line);
                            if (onLine != null) onLine(line);
                        }
                        pending = p.StandardOutput.ReadLineAsync();
                    }
                    else if (ct.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }
                    else if (DateTime.UtcNow > deadline)
                    {
                        timedOut = true;
                        break;
                    }
                }

                if (cancelled || timedOut)
                {
                    TryKill(p);
                    Log.Warn(timedOut ? string.Format("Timed out after {0:N0} s.", timeout.TotalSeconds) : "Cancelled.");
                }
                p.WaitForExit(5000);
                int code = p.HasExited ? p.ExitCode : -1;
                string err = errTask.Wait(2000) ? Clean(errTask.Result) : string.Empty;
                string how = cancelled ? "cancelled" : timedOut ? "TIMED OUT" : "exit " + code.ToString(CultureInfo.InvariantCulture);
                // the lines themselves already went to onLine (usually the log), so only count them here
                LogEnd(id, string.Format(CultureInfo.InvariantCulture, "{0}, {1} output lines", how, lines.Count),
                    sw, string.Empty, err, code != 0 || cancelled || timedOut);
                return new StreamResult
                {
                    ExitCode = code, Lines = lines, Cancelled = cancelled, TimedOut = timedOut, Error = err
                };
            }
        }

        /// <summary>Streams output lines into the log, indented.</summary>
        public static Task<StreamResult> RunStreamingToLogAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
        {
            return RunStreamingAsync(args, line => Log.Info("    " + line), timeout, ct);
        }
    }

    /// <summary>Lists and resolves WSL distributions.</summary>
    public static class DistroService
    {
        private static readonly Regex ListLine = new Regex(@"^\s*(\*)?\s*(\S+)\s+(\S+)\s+([12])\s*$");

        public static async Task<List<Distro>> ListAsync(CancellationToken ct)
        {
            WslResult r = await Wsl.RunAsync(new[] { "--list", "--verbose" }, 60, ct).ConfigureAwait(false);
            var list = new List<Distro>();
            if (r.ExitCode != 0)
            {
                Log.Debug("Could not list WSL distributions (exit " + r.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");
                return list;
            }
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Match m = ListLine.Match(line);
                if (!m.Success) continue;
                var d = new Distro
                {
                    Name = m.Groups[2].Value,
                    IsDefault = m.Groups[1].Success,
                    State = m.Groups[3].Value,
                    Version = int.Parse(m.Groups[4].Value)
                };
                bool usable = d.Version == 2 && !d.Name.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase);
                if (usable) list.Add(d);
                Log.Debug(string.Format(CultureInfo.InvariantCulture, "Distro '{0}': WSL{1}, {2}{3}{4}", d.Name, d.Version, d.State,
                    d.IsDefault ? ", default" : string.Empty, usable ? string.Empty : " (ignored: not usable for mounting)"));
            }
            if (list.Count == 0) Log.Debug("No usable WSL2 distribution in the list output.");
            return list;
        }

        public static string Resolve(string preferred, IList<Distro> available)
        {
            if (available == null || available.Count == 0) return null;
            if (!string.IsNullOrEmpty(preferred))
            {
                Distro match = available.FirstOrDefault(d => d.Name == preferred);
                if (match != null) return match.Name;
            }
            Distro def = available.FirstOrDefault(d => d.IsDefault);
            return def != null ? def.Name : available[0].Name;
        }

        public static async Task<List<string>> RunningAsync(CancellationToken ct)
        {
            WslResult r = await Wsl.RunAsync(new[] { "--list", "--running", "--quiet" }, 30, ct).ConfigureAwait(false);
            var names = new List<string>();
            if (r.ExitCode != 0 || string.IsNullOrEmpty(r.Output)) return names;
            foreach (string line in r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = line.Trim();
                if (Regex.IsMatch(t, @"^\S+$")) names.Add(t);
            }
            return names;
        }
    }
}
