// Xnix USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace XnixUsbMounter.Core
{
    /// <summary>
    /// Thread-safe owner of <see cref="AppState"/>. Readers get copies; writers go through the
    /// mutating methods, which persist immediately (atomic write via a temp file).
    /// </summary>
    public sealed class StateStore
    {
        private readonly object gate = new object();
        private readonly AppState state;

        private StateStore(AppState state)
        {
            this.state = state;
        }

        public static StateStore Load()
        {
            AppState loaded = null;
            try
            {
                if (File.Exists(AppPaths.StateFile))
                {
                    // the PowerShell version wrote UTF-8 with a BOM; normalise before parsing
                    string text = File.ReadAllText(AppPaths.StateFile, Encoding.UTF8).Trim().TrimStart('\uFEFF');
                    if (text.Length > 0)
                    {
                        var serializer = new DataContractJsonSerializer(typeof(AppState));
                        using (var ms = new MemoryStream(new UTF8Encoding(false).GetBytes(text)))
                        {
                            loaded = (AppState)serializer.ReadObject(ms);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("State file unreadable, starting fresh: " + ex.Message);
                Log.DebugException("State file parse error", ex);
                loaded = null;
            }

            if (loaded == null) loaded = new AppState();
            if (loaded.Settings == null) loaded.Settings = new AppSettings();
            if (loaded.Mounts == null) loaded.Mounts = new List<MountEntry>();
            loaded.Mounts = loaded.Mounts.Where(m => m != null && !string.IsNullOrEmpty(m.Key)).ToList();
            if (loaded.Settings.Distro == null) loaded.Settings.Distro = string.Empty;
            if (loaded.Settings.Options == null) loaded.Settings.Options = string.Empty;
            Log.Debug(string.Format(CultureInfo.InvariantCulture, "State loaded: settings {0}; mounts {1}; keep-alive PID {2}",
                Describe(loaded.Settings), Describe(loaded.Mounts), loaded.KeepAlivePid));
            return new StateStore(loaded);
        }

        private static string Describe(AppSettings s)
        {
            return string.Format(CultureInfo.InvariantCulture, "[distro '{0}', auto-mount {1}, open Explorer {2}, all disks {3}, options '{4}', APFS write {5}, UFS write {6}]",
                s.Distro, s.AutoMount, s.OpenExplorer, s.ShowAllDisks, s.Options, s.ApfsWrite, s.UfsWrite);
        }

        private static string Describe(IEnumerable<MountEntry> mounts)
        {
            List<string> items = mounts.Select(m => string.Format(CultureInfo.InvariantCulture, "'{0}' disk {1} part {2} via {3}",
                m.Name, m.DiskNumber, m.PartitionNumber, m.Distro)).ToList();
            return items.Count == 0 ? "(none)" : "[" + string.Join("; ", items) + "]";
        }

        private void LogMountsLocked(string why)
        {
            Log.Debug("Mount list " + why + ": " + Describe(state.Mounts));
        }

        // ---- settings -------------------------------------------------------------------------
        public AppSettings Settings
        {
            get { lock (gate) return state.Settings.Clone(); }
        }

        public void UpdateSettings(Action<AppSettings> mutate)
        {
            lock (gate)
            {
                string before = Describe(state.Settings);
                mutate(state.Settings);
                string after = Describe(state.Settings);
                if (after != before) Log.Debug("Settings changed: " + after);
                SaveLocked();
            }
        }

        // ---- mounts ---------------------------------------------------------------------------
        public List<MountEntry> Mounts
        {
            get { lock (gate) return state.Mounts.Select(m => m.Clone()).ToList(); }
        }

        public int MountCount
        {
            get { lock (gate) return state.Mounts.Count; }
        }

        public MountEntry FindMount(string key)
        {
            lock (gate)
            {
                MountEntry m = state.Mounts.FirstOrDefault(x => x.Key == key);
                return m != null ? m.Clone() : null;
            }
        }

        public void AddMount(MountEntry entry)
        {
            lock (gate)
            {
                state.Mounts.RemoveAll(m => m.Key == entry.Key);
                state.Mounts.Add(entry.Clone());
                LogMountsLocked("after adding '" + entry.Name + "'");
                SaveLocked();
            }
        }

        public void RemoveMounts(Predicate<MountEntry> match)
        {
            lock (gate)
            {
                int removed = state.Mounts.RemoveAll(match);
                if (removed > 0) LogMountsLocked("after removing " + removed.ToString(CultureInfo.InvariantCulture));
                SaveLocked();
            }
        }

        public void ReplaceMounts(IEnumerable<MountEntry> mounts)
        {
            lock (gate)
            {
                string before = Describe(state.Mounts);
                state.Mounts = mounts.Select(m => m.Clone()).ToList();
                if (Describe(state.Mounts) != before) LogMountsLocked("replaced");
                SaveLocked();
            }
        }

        public bool IsNameInUse(string name)
        {
            lock (gate) return state.Mounts.Any(m => string.Equals(m.Name, name, StringComparison.Ordinal));
        }

        // ---- keep-alive -----------------------------------------------------------------------
        public int KeepAlivePid
        {
            get { lock (gate) return state.KeepAlivePid; }
            set
            {
                lock (gate)
                {
                    if (state.KeepAlivePid != value)
                    {
                        Log.Debug(string.Format(CultureInfo.InvariantCulture, "Keep-alive PID saved: {0} -> {1}", state.KeepAlivePid, value));
                    }
                    state.KeepAlivePid = value;
                    SaveLocked();
                }
            }
        }

        // ---- persistence ----------------------------------------------------------------------
        private void SaveLocked()
        {
            try
            {
                AppPaths.EnsureDirectories();
                byte[] bytes;
                var serializer = new DataContractJsonSerializer(typeof(AppState));
                using (var ms = new MemoryStream())
                {
                    serializer.WriteObject(ms, state);
                    bytes = ms.ToArray();
                }
                string temp = AppPaths.StateFile + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(AppPaths.StateFile))
                {
                    File.Replace(temp, AppPaths.StateFile, null);
                }
                else
                {
                    File.Move(temp, AppPaths.StateFile);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not save state: " + ex.Message);
                Log.DebugException("State save error (" + AppPaths.StateFile + ")", ex);
            }
        }
    }
}
