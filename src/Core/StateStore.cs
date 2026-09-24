using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BtrfsUsbMounter.Core
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
                loaded = null;
            }

            if (loaded == null) loaded = new AppState();
            if (loaded.Settings == null) loaded.Settings = new AppSettings();
            if (loaded.Mounts == null) loaded.Mounts = new List<MountEntry>();
            loaded.Mounts = loaded.Mounts.Where(m => m != null && !string.IsNullOrEmpty(m.Key)).ToList();
            if (loaded.Settings.Distro == null) loaded.Settings.Distro = string.Empty;
            if (loaded.Settings.Options == null) loaded.Settings.Options = string.Empty;
            return new StateStore(loaded);
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
                mutate(state.Settings);
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
                SaveLocked();
            }
        }

        public void RemoveMounts(Predicate<MountEntry> match)
        {
            lock (gate)
            {
                state.Mounts.RemoveAll(match);
                SaveLocked();
            }
        }

        public void ReplaceMounts(IEnumerable<MountEntry> mounts)
        {
            lock (gate)
            {
                state.Mounts = mounts.Select(m => m.Clone()).ToList();
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
            }
        }
    }
}
