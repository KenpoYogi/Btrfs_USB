using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BtrfsUsbMounter.Core
{
    /// <summary>
    /// Parsers for btrfs-progs output. Labels were checked against the format strings compiled into
    /// btrfs-progs ("Free (estimated):\t\t%*s\t(" + "min: %s)", "%s,%s: Size:%s, " + "Used:%s (%.2f%%)",
    /// "[%s].%-16s %llu", "Status:           %s", "\tdata_bytes_scrubbed: %lld", ...).
    /// </summary>
    public static class BtrfsParsers
    {
        private static readonly string[] LineBreaks = { "\r\n", "\n" };

        private static readonly Regex FreeEstimatedMin = new Regex(@"^\s*Free \(estimated\):\s+([\d.]+)\s+\(min:\s*([\d.]+)\)");
        private static readonly Regex FreeEstimated = new Regex(@"^\s*Free \(estimated\):\s+([\d.]+)\s*$");
        private static readonly Regex GlobalReserve = new Regex(@"^\s*Global reserve:\s+([\d.]+)\s+\(used:\s*([\d.]+)\)");
        private static readonly Regex OverallField = new Regex(
            @"^\s*(Device size|Device allocated|Device unallocated|Used|Free \(statfs, df\)|Data ratio|Metadata ratio):\s+([\d.]+)\s*$");
        private static readonly Regex BlockGroupLine = new Regex(@"^(Data|Metadata|System),([^:]+):\s*Size:([\d.]+),\s*Used:([\d.]+)");
        private static readonly Regex StatLine = new Regex(@"^\[(.+?)\]\.(\w+)\s+(\d+)\s*$");
        private static readonly Regex KeyValue = new Regex(@"^\s*([A-Za-z][A-Za-z_ ]*?):\s+(.+?)\s*$");

        private static double D(string s)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string[] Lines(string text)
        {
            return (text ?? string.Empty).Split(LineBreaks, StringSplitOptions.None);
        }

        /// <summary>Raw bytes written per logical byte for a block-group profile.</summary>
        public static int ProfileRatio(string profile)
        {
            string p = (profile ?? string.Empty).Trim().ToUpperInvariant();
            if (p.StartsWith("RAID1C4", StringComparison.Ordinal)) return 4;
            if (p.StartsWith("RAID1C3", StringComparison.Ordinal)) return 3;
            if (p.StartsWith("DUP", StringComparison.Ordinal) || p.StartsWith("RAID10", StringComparison.Ordinal) ||
                p.StartsWith("RAID1", StringComparison.Ordinal)) return 2;
            return 1;
        }

        /// <summary>Parses "btrfs filesystem usage -b &lt;path&gt;".</summary>
        public static UsageInfo ParseUsage(string text)
        {
            var u = new UsageInfo();
            foreach (string line in Lines(text))
            {
                Match m = FreeEstimatedMin.Match(line);
                if (m.Success)
                {
                    u.FreeEstimated = D(m.Groups[1].Value);
                    u.FreeMin = D(m.Groups[2].Value);
                    continue;
                }
                m = FreeEstimated.Match(line);
                if (m.Success)
                {
                    u.FreeEstimated = D(m.Groups[1].Value);
                    continue;
                }
                m = GlobalReserve.Match(line);
                if (m.Success)
                {
                    u.GlobalReserve = D(m.Groups[1].Value);
                    u.GlobalReserveUsed = D(m.Groups[2].Value);
                    continue;
                }
                m = OverallField.Match(line);
                if (m.Success)
                {
                    double v = D(m.Groups[2].Value);
                    switch (m.Groups[1].Value)
                    {
                        case "Device size": u.DeviceSize = v; break;
                        case "Device allocated": u.Allocated = v; break;
                        case "Device unallocated": u.Unallocated = v; break;
                        case "Used": u.Used = v; break;
                        case "Free (statfs, df)": u.FreeStatfs = v; break;
                        case "Data ratio": u.DataRatio = v; break;
                        case "Metadata ratio": u.MetadataRatio = v; break;
                    }
                    continue;
                }
                m = BlockGroupLine.Match(line);
                if (m.Success)
                {
                    u.Blocks.Add(new BlockGroup
                    {
                        Type = m.Groups[1].Value,
                        Profile = m.Groups[2].Value.Trim(),
                        Size = D(m.Groups[3].Value),
                        Used = D(m.Groups[4].Value)
                    });
                }
            }
            return u;
        }

        /// <summary>Parses "btrfs device stats &lt;path&gt;".</summary>
        public static List<DeviceStat> ParseDeviceStats(string text)
        {
            var list = new List<DeviceStat>();
            foreach (string line in Lines(text))
            {
                Match m = StatLine.Match(line);
                if (!m.Success) continue;
                long value;
                long.TryParse(m.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                list.Add(new DeviceStat { Device = m.Groups[1].Value, Counter = m.Groups[2].Value, Value = value });
            }
            return list;
        }

        /// <summary>
        /// Parses "btrfs scrub status -R &lt;path&gt;". Progress is estimated against the raw used bytes
        /// (DUP metadata is read twice by the scrub and counted twice in "Used").
        /// </summary>
        public static ScrubStatus ParseScrubStatus(string text, double usedBytes)
        {
            var kv = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in Lines(text))
            {
                Match m = KeyValue.Match(line);
                if (!m.Success) continue;
                string key = Regex.Replace(m.Groups[1].Value.Trim().ToLowerInvariant(), @"\s+", "_");
                kv[key] = m.Groups[2].Value;
            }

            string status;
            if (kv.ContainsKey("status")) status = kv["status"].ToLowerInvariant();
            else if ((text ?? string.Empty).IndexOf("no stats available", StringComparison.Ordinal) >= 0) status = "never";
            else status = "unknown";

            Func<string, double> num = k =>
            {
                string v;
                return kv.TryGetValue(k, out v) && Regex.IsMatch(v, @"^\d+$") ? D(v) : 0;
            };

            double scrubbed = num("data_bytes_scrubbed") + num("tree_bytes_scrubbed");
            double errors = num("read_errors") + num("csum_errors") + num("verify_errors") + num("super_errors");
            double pct = usedBytes > 0 ? Math.Min(100.0, 100.0 * scrubbed / usedBytes) : 0;
            if (status == "running") pct = Math.Min(pct, 99.9);
            else if (status == "finished") pct = 100.0;

            string started, duration;
            kv.TryGetValue("scrub_started", out started);
            kv.TryGetValue("duration", out duration);

            return new ScrubStatus
            {
                Status = status,
                Started = started ?? string.Empty,
                Duration = duration ?? string.Empty,
                BytesScrubbed = scrubbed,
                Percent = pct,
                Errors = (long)errors,
                Corrected = (long)num("corrected_errors"),
                Uncorrectable = (long)num("uncorrectable_errors")
            };
        }

        public static string ScrubErrorText(ScrubStatus s)
        {
            if (s.Errors <= 0 && s.Uncorrectable <= 0) return "no errors";
            if (s.Uncorrectable > 0) return string.Format("{0} error(s), {1} uncorrectable", s.Errors, s.Uncorrectable);
            return string.Format("{0} error(s), all corrected", s.Errors);
        }

        public static string ScrubText(ScrubStatus s)
        {
            if (s == null) return string.Empty;
            switch (s.Status)
            {
                case "never":
                    return "Never scrubbed. A scrub reads everything stored on the drive and verifies it against its " +
                           "checksums. The drive stays usable meanwhile; the time needed depends on how much data is stored.";
                case "running":
                    return string.Format(CultureInfo.CurrentCulture, "Scrubbing: about {0:N1}%  |  {1} verified  |  running for {2}  |  {3}",
                        s.Percent, Fmt.BytesZero(s.BytesScrubbed), s.Duration, ScrubErrorText(s));
                case "finished":
                    return string.Format("Last scrub finished  |  started {0}  |  took {1}  |  {2}", s.Started, s.Duration, ScrubErrorText(s));
                case "aborted":
                    return string.Format(CultureInfo.CurrentCulture,
                        "Last scrub was cancelled at about {0:N0}% (started {1}). Resume continues where it stopped.", s.Percent, s.Started);
                case "interrupted":
                    return string.Format(CultureInfo.CurrentCulture,
                        "Last scrub was interrupted at about {0:N0}% (started {1}), probably because the drive was unmounted. " +
                        "Resume continues where it stopped.", s.Percent, s.Started);
                default:
                    return "Scrub status: " + s.Status;
            }
        }
    }
}
