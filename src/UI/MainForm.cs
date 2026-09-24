using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using BtrfsUsbMounter.Core;

namespace BtrfsUsbMounter.UI
{
    internal sealed class MainForm : Form, IDriveActions
    {
        private const int WmDeviceChange = 0x0219;
        private const int DbtDevNodesChanged = 0x0007;
        private const int DbtDeviceArrival = 0x8000;
        private const int DbtDeviceRemoveComplete = 0x8004;
        private const int SpaceColumn = 2;

        private readonly Engine engine;
        private readonly JobQueue jobs;
        private readonly string preferredDistro;
        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private readonly Dictionary<string, ScrubStatus> scrubs = new Dictionary<string, ScrubStatus>(StringComparer.Ordinal);

        private List<VolumeInfo> volumes = new List<VolumeInfo>();
        private List<string> lastDiskIds = new List<string>();
        private string lastDiskSig = string.Empty;

        private bool allowShow;
        private bool started;
        private bool reallyExit;
        private bool closeWhenIdle;
        private bool trayHintShown;
        private bool suppressEvents;
        private bool ejectHintShown;
        private int initRetries;
        private DateTime? changeAt;
        private DateTime? retryInitAt;
        private DateTime nextPollAt = DateTime.MaxValue;
        private DateTime scrubPollAt = DateTime.MaxValue;
        private DateTime? retryScanAt;
        private List<string> retryScanIds;
        private int retryScanCount;
        private DriveInfoForm infoForm;

        // ---- controls -----------------------------------------------------------------------
        private ComboBox cmbDistro;
        private TextBox txtOptions;
        private CheckBox chkAuto, chkExplorer, chkAllDisks, chkStartup;
        private SmoothListView list;
        private Label emptyLabel;
        private Button btnMount, btnUnmount, btnOpen, btnShell, btnCopy, btnUnmountAll, btnRefresh, btnTools;
        private TextBox logBox;
        private ToolStripStatusLabel statusLabel;
        private ToolStripProgressBar progress;
        private ToolStripButton cancelTask;
        private NotifyIcon tray;
        private ToolStripMenuItem trayUnmountAll;
        private ContextMenuStrip toolsMenu;
        private ToolStripMenuItem miInfo, miScrub, miCancelScrub, miCheck, miInstall, miReports;
        private System.Windows.Forms.Timer housekeeping;
        private System.Windows.Forms.Timer logTimer;

        public MainForm(Engine engine, bool startInTray, string preferredDistro)
        {
            this.engine = engine;
            this.preferredDistro = preferredDistro;
            allowShow = !startInTray;
            jobs = new JobQueue(SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext());
            jobs.Changed += OnJobsChanged;
            Log.Written += OnLogWritten;
            BuildUi();
        }

        // =======================================================================================
        //  UI construction
        // =======================================================================================
        private void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = UiKit.AppName;
            Size = new Size(1100, 680);
            MinimumSize = new Size(880, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiKit.BaseFont;
            Icon = UiKit.AppIcon;
            AppSettings settings = engine.State.Settings;
            var tips = new ToolTip();

            // ---- settings bar ----
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 10, 10, 4), WrapContents = true };
            var lblDistro = new Label { Text = "WSL2 distro:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) };
            cmbDistro = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Margin = new Padding(0, 3, 16, 0) };
            var lblOptions = new Label { Text = "Mount options:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) };
            txtOptions = new TextBox { Width = 190, Text = settings.Options, Margin = new Padding(0, 3, 16, 0) };
            tips.SetToolTip(txtOptions, "Optional btrfs-specific options, comma separated, e.g. compress=zstd or subvol=@data. " +
                                        "Generic options such as noatime are rejected by wsl --mount.");
            chkAuto = UiKit.MakeCheckBox("Auto-mount on plug-in", settings.AutoMount);
            chkExplorer = UiKit.MakeCheckBox("Open Explorer after mount", settings.OpenExplorer);
            chkAllDisks = UiKit.MakeCheckBox("Include non-USB disks", settings.ShowAllDisks);
            tips.SetToolTip(chkAllDisks, "Also scan internal SATA/NVMe disks (boot and system disks are always excluded).");
            chkStartup = UiKit.MakeCheckBox("Start in tray at logon", false);
            tips.SetToolTip(chkStartup, "Creates a scheduled task with highest privileges, so there is no UAC prompt at logon.");
            top.Controls.AddRange(new Control[] { lblDistro, cmbDistro, lblOptions, txtOptions, chkAuto, chkExplorer, chkAllDisks, chkStartup });

            // ---- drive list (Space column is owner-drawn as a usage bar) ----
            list = new SmoothListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                GridLines = true,
                OwnerDraw = true
            };
            float scale = CurrentAutoScaleDimensions.Width / 96f;
            string[] columns = { "Status", "Label", "Space", "Disk", "Part", "UUID", "Windows path" };
            int[] widths = { 95, 140, 250, 200, 45, 250, 290 };
            for (int i = 0; i < columns.Length; i++) list.Columns.Add(columns[i], (int)Math.Round(widths[i] * scale));
            list.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += OnDrawSubItem;
            list.MouseMove += (s, e) =>
            {
                ListViewItem hit = list.GetItemAt(e.X, e.Y);
                if (hit != null) list.Invalidate(hit.Bounds);
            };
            list.GotFocus += (s, e) => list.Invalidate();
            list.LostFocus += (s, e) => list.Invalidate();
            list.SelectedIndexChanged += (s, e) => UpdateButtonState();
            list.DoubleClick += OnListDoubleClick;

            emptyLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                BackColor = SystemColors.Window,
                Font = new Font("Segoe UI", 11f),
                Text = "Looking for btrfs drives...\r\n\r\nPlug in a btrfs-formatted USB hard drive - it will appear here automatically."
            };
            list.Controls.Add(emptyLabel);

            // ---- buttons ----
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(10, 8, 10, 6) };
            btnMount = UiKit.MakeButton("Mount", 100);
            btnUnmount = UiKit.MakeButton("Unmount / Eject", 130);
            btnOpen = UiKit.MakeButton("Open in Explorer", 130);
            btnShell = UiKit.MakeButton("Open Linux shell", 130);
            btnCopy = UiKit.MakeButton("Copy path", 100);
            btnUnmountAll = UiKit.MakeButton("Unmount all", 110);
            btnRefresh = UiKit.MakeButton("Refresh", 90);
            btnTools = UiKit.MakeButton("Drive tools...", 115);
            bar.Controls.AddRange(new Control[] { btnMount, btnUnmount, btnOpen, btnShell, btnCopy, btnUnmountAll, btnRefresh, btnTools });

            // ---- log ----
            logBox = new TextBox
            {
                Dock = DockStyle.Bottom,
                Height = 170,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Font = UiKit.LogFont,
                BackColor = Color.FromArgb(250, 250, 250)
            };

            // ---- status bar ----
            var status = new StatusStrip();
            progress = new ToolStripProgressBar { Style = ProgressBarStyle.Marquee, Width = 120, Visible = false };
            cancelTask = new ToolStripButton("Cancel task") { Visible = false, ForeColor = Color.Firebrick };
            statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.AddRange(new ToolStripItem[] { progress, cancelTask, statusLabel });

            Controls.Add(list);
            Controls.Add(top);
            Controls.Add(bar);
            Controls.Add(logBox);
            Controls.Add(status);
            list.BringToFront();

            // ---- drive tools menu (button and right-click) ----
            toolsMenu = new ContextMenuStrip();
            miInfo = new ToolStripMenuItem("Drive info (space and health)...") { Font = UiKit.BoldFont };
            miScrub = new ToolStripMenuItem("Start scrub (verify all data)");
            miCancelScrub = new ToolStripMenuItem("Cancel scrub");
            miCheck = new ToolStripMenuItem("Offline check (read-only)...");
            miInstall = new ToolStripMenuItem("Install btrfs tools");
            miReports = new ToolStripMenuItem("Open check reports folder");
            toolsMenu.Items.AddRange(new ToolStripItem[]
            {
                miInfo, new ToolStripSeparator(), miScrub, miCancelScrub, miCheck, new ToolStripSeparator(), miInstall, miReports
            });
            list.ContextMenuStrip = toolsMenu;

            // ---- tray ----
            var trayMenu = new ContextMenuStrip();
            var trayOpen = new ToolStripMenuItem("Open") { Font = UiKit.BoldFont };
            trayUnmountAll = new ToolStripMenuItem("Unmount all");
            var trayExit = new ToolStripMenuItem("Exit");
            trayMenu.Items.AddRange(new ToolStripItem[] { trayOpen, trayUnmountAll, new ToolStripSeparator(), trayExit });
            tray = new NotifyIcon { Icon = Icon, Text = UiKit.AppName, ContextMenuStrip = trayMenu, Visible = true };

            // ---- timers ----
            housekeeping = new System.Windows.Forms.Timer { Interval = 500 };
            housekeeping.Tick += OnHousekeeping;
            logTimer = new System.Windows.Forms.Timer { Interval = 150 };
            logTimer.Tick += (s, e) => DrainLog();

            // ---- events ----
            btnMount.Click += (s, e) => { VolumeInfo v = SelectedVolume; if (v != null) RequestMount(new List<VolumeInfo> { v }); };
            btnUnmount.Click += (s, e) => RequestUnmount(SelectedVolume);
            btnOpen.Click += (s, e) => OpenInExplorer(SelectedVolume);
            btnShell.Click += (s, e) => OpenShell(SelectedVolume);
            btnCopy.Click += (s, e) => CopyPath(SelectedVolume);
            btnUnmountAll.Click += (s, e) => RequestUnmountAll(false);
            btnRefresh.Click += (s, e) =>
            {
                Log.Info("Refreshing (re-reading all drives)...");
                RequestScan(true, true, null, 0, false);
            };
            btnTools.Click += (s, e) => toolsMenu.Show(btnTools, 0, btnTools.Height);
            cancelTask.Click += (s, e) =>
            {
                jobs.CancelCurrent();
                Log.Warn("Cancelling the current task...");
            };

            toolsMenu.Opening += (s, e) => UpdateToolsMenu();
            miInfo.Click += (s, e) => ShowDriveInfo(SelectedVolume);
            miScrub.Click += (s, e) =>
            {
                VolumeInfo v = SelectedVolume;
                ScrubStatus sc = GetScrub(v);
                StartScrub(v, sc != null && sc.IsResumable);
            };
            miCancelScrub.Click += (s, e) => CancelScrub(SelectedVolume);
            miCheck.Click += (s, e) => OfflineCheck(SelectedVolume);
            miInstall.Click += (s, e) => RequestInstallTools(null, true);
            miReports.Click += (s, e) => Process.Start(new ProcessStartInfo("explorer.exe", "\"" + AppPaths.EnsureChecksDir() + "\"") { UseShellExecute = true });

            cmbDistro.SelectedIndexChanged += (s, e) =>
            {
                if (suppressEvents) return;
                string d = SelectedDistro;
                engine.State.UpdateSettings(x => x.Distro = d ?? string.Empty);
                UpdateButtonState();
            };
            txtOptions.TextChanged += (s, e) =>
            {
                string o = txtOptions.Text.Trim();
                engine.State.UpdateSettings(x => x.Options = o);
            };
            chkAuto.CheckedChanged += (s, e) => { bool v = chkAuto.Checked; engine.State.UpdateSettings(x => x.AutoMount = v); };
            chkExplorer.CheckedChanged += (s, e) => { bool v = chkExplorer.Checked; engine.State.UpdateSettings(x => x.OpenExplorer = v); };
            chkAllDisks.CheckedChanged += (s, e) =>
            {
                bool v = chkAllDisks.Checked;
                engine.State.UpdateSettings(x => x.ShowAllDisks = v);
                RequestScan(false, false, null, 0, false);
            };
            chkStartup.CheckedChanged += (s, e) => { if (!suppressEvents) SetStartupTask(chkStartup.Checked); };

            tray.DoubleClick += (s, e) => ShowMainWindow();
            trayOpen.Click += (s, e) => ShowMainWindow();
            trayUnmountAll.Click += (s, e) => RequestUnmountAll(false);
            trayExit.Click += (s, e) => Close();

            Resize += (s, e) =>
            {
                if (WindowState != FormWindowState.Minimized) return;
                Hide();
                if (!trayHintShown)
                {
                    ShowBalloon(UiKit.AppName, "Still watching for btrfs USB drives. Double-click this icon to reopen.", ToolTipIcon.Info);
                    trayHintShown = true;
                }
            };

            ResumeLayout(false);
            PerformLayout();
            UpdateButtonState();
        }

        // =======================================================================================
        //  Window lifetime: start hidden in tray, single-instance activation, device notifications
        // =======================================================================================
        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (started) return;
            started = true;
            BeginInvoke(new Action(StartUp));
        }

        public void ShowMainWindow()
        {
            allowShow = true;
            Show();
            ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmDeviceChange)
            {
                long w = m.WParam.ToInt64();
                if (w == DbtDevNodesChanged || w == DbtDeviceArrival || w == DbtDeviceRemoveComplete) OnDeviceChanged();
            }
            else if (m.Msg == SingleInstance.ShowMessage && SingleInstance.ShowMessage != 0)
            {
                ShowMainWindow();
                SingleInstance.Acknowledge();
            }
            base.WndProc(ref m);
        }

        private void OnDeviceChanged()
        {
            changeAt = DateTime.Now;   // restart the quiet period on every event
            JobInfo cur = jobs.Current;
            if (cur != null && cur.Name.StartsWith("Ejecting", StringComparison.Ordinal) && !ejectHintShown)
            {
                ejectHintShown = true;
                Log.Warn("A device changed while ejecting. If you unplugged the drive, click \"Cancel task\" in the status bar.");
            }
        }

        private async void StartUp()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Log.Info(string.Format("{0} {1} started. WSL: {2}", UiKit.AppName, version, AppPaths.WslExe));
            logTimer.Start();
            housekeeping.Start();
            nextPollAt = DateTime.Now.AddSeconds(30);
            await InitAsync(false);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (reallyExit || e.CloseReason == CloseReason.WindowsShutDown)
            {
                Cleanup();
                base.OnFormClosing(e);
                return;
            }
            if (!jobs.IsIdle)
            {
                e.Cancel = true;
                JobInfo cur = jobs.Current;
                string name = cur != null ? cur.Name : "A task";
                if (closeWhenIdle)
                {
                    DialogResult answer = UiKit.Show(this,
                        string.Format("'{0}' is still running.\r\n\r\nForce quit now? The task is abandoned. Mounted drives stay mounted " +
                                      "and the keep-alive keeps running.\r\n\r\nIf you were ejecting, do NOT unplug the drive until it " +
                                      "has been ejected successfully (restart the program and eject again).", name),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer == DialogResult.Yes) ForceQuit();
                    return;
                }
                closeWhenIdle = true;
                Log.Info(string.Format("Will close after '{0}' finishes. Close again to force quit.", name));
                return;
            }
            int n = engine.State.MountCount;
            if (n > 0)
            {
                DialogResult answer = UiKit.Show(this,
                    n + " drive(s) are still mounted.\r\n\r\nYes = unmount them (safe to eject), then exit\r\n" +
                    "No = exit and leave them mounted (keep-alive keeps them available)\r\nCancel = stay open",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (answer == DialogResult.Yes)
                {
                    e.Cancel = true;
                    ShowMainWindow();
                    RequestUnmountAll(true);
                    return;
                }
            }
            Cleanup();
            base.OnFormClosing(e);
        }

        private void Cleanup()
        {
            housekeeping.Stop();
            logTimer.Stop();
            Log.Written -= OnLogWritten;
            if (infoForm != null && !infoForm.IsDisposed) infoForm.Close();
            tray.Visible = false;
            tray.Dispose();
        }

        private void ForceQuit()
        {
            reallyExit = true;
            jobs.CancelAll();   // kills any wsl.exe the background task is waiting on
            try
            {
                tray.Visible = false;
                tray.Dispose();
            }
            catch
            {
                // exiting anyway
            }
            Environment.Exit(0);
        }

        // =======================================================================================
        //  Job plumbing
        // =======================================================================================
        private async Task<T> RunJob<T>(string name, Func<CancellationToken, Task<T>> work, JobFlags flags)
        {
            if ((flags & JobFlags.Cancellable) != 0) ejectHintShown = false;
            try
            {
                return await jobs.Run(name, work, flags);
            }
            catch (JobSkippedException)
            {
                // an identical job was already waiting
            }
            catch (OperationCanceledException)
            {
                Log.Warn(name + ": cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error(name + " failed: " + Fmt.Root(ex).Message);
            }
            return default(T);
        }

        private void OnJobsChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
            if (closeWhenIdle && jobs.IsIdle)
            {
                closeWhenIdle = false;
                BeginInvoke(new Action(Close));
            }
        }

        private void OnLogWritten(string line, LogLevel level)
        {
            logQueue.Enqueue(line);
        }

        private void DrainLog()
        {
            if (logQueue.IsEmpty) return;
            var sb = new StringBuilder();
            string line;
            while (logQueue.TryDequeue(out line)) sb.Append(line).Append("\r\n");
            if (logBox.TextLength > 200000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 100000);
            logBox.AppendText(sb.ToString());
        }

        private void OnHousekeeping(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            if (changeAt.HasValue && (now - changeAt.Value).TotalMilliseconds >= 1500)
            {
                changeAt = null;
                RequestSnapshot();
            }
            if (retryInitAt.HasValue && now >= retryInitAt.Value)
            {
                retryInitAt = null;
                RetryInit();
            }
            if (retryScanAt.HasValue && now >= retryScanAt.Value)
            {
                retryScanAt = null;
                RequestScan(false, false, retryScanIds, retryScanCount, false);
            }
            if (scrubs.Values.Any(s => s.IsRunning) && now >= scrubPollAt)
            {
                scrubPollAt = now.AddSeconds(5);
                RequestScrubPoll();
            }
            if (jobs.IsIdle && now >= nextPollAt)
            {
                nextPollAt = now.AddSeconds(30);   // safety net: cheap disk-list check, never touches the drives
                RequestSnapshot();
            }
        }

        // =======================================================================================
        //  Selection and rendering
        // =======================================================================================
        private VolumeInfo SelectedVolume
        {
            get { return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as VolumeInfo : null; }
        }

        private string SelectedDistro
        {
            get { return cmbDistro.SelectedItem as string; }
        }

        public ScrubStatus GetScrub(VolumeInfo v)
        {
            if (v == null || !v.Mounted) return null;
            ScrubStatus s;
            return scrubs.TryGetValue(v.MountName, out s) ? s : null;
        }

        private string RowStatus(VolumeInfo v)
        {
            if (v.Disconnected) return "UNPLUGGED";
            if (v.Mounted)
            {
                ScrubStatus s = GetScrub(v);
                return s != null && s.IsRunning ? string.Format(CultureInfo.CurrentCulture, "Scrub {0:N0}%", s.Percent) : "Mounted";
            }
            return "Ready";
        }

        private void RenderVolumes()
        {
            VolumeInfo selected = SelectedVolume;
            string selectedKey = selected != null ? selected.Key : null;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (VolumeInfo v in volumes)
            {
                var item = new ListViewItem(RowStatus(v)) { Tag = v, UseItemStyleForSubItems = true };
                item.SubItems.Add(v.DisplayLabel);
                item.SubItems.Add(Fmt.SpaceText(v));
                item.SubItems.Add(string.Format("#{0}  {1}", v.DiskNumber, v.Model));
                item.SubItems.Add(v.PartitionNumber > 0 ? v.PartitionNumber.ToString(CultureInfo.InvariantCulture) : "whole");
                item.SubItems.Add(v.Uuid);
                item.SubItems.Add(v.WindowsPath);
                if (v.Disconnected) item.ForeColor = Color.Firebrick;
                else if (v.Mounted)
                {
                    item.ForeColor = Color.ForestGreen;
                    item.Font = UiKit.BoldFont;
                }
                list.Items.Add(item);
                if (selectedKey != null && v.Key == selectedKey) item.Selected = true;
            }
            if (list.Items.Count > 0 && list.SelectedItems.Count == 0) list.Items[0].Selected = true;
            list.EndUpdate();
            emptyLabel.Visible = list.Items.Count == 0;
            emptyLabel.Text = "No btrfs partitions found.\r\n\r\nPlug in a btrfs-formatted USB hard drive - it will appear here automatically.";
            UpdateButtonState();
        }

        private void UpdateStatusTexts()
        {
            foreach (ListViewItem item in list.Items)
            {
                var v = item.Tag as VolumeInfo;
                if (v != null && v.Mounted) item.Text = RowStatus(v);
            }
        }

        private void SetRowText(IEnumerable<string> keys, string text)
        {
            var set = new HashSet<string>(keys, StringComparer.Ordinal);
            foreach (ListViewItem item in list.Items)
            {
                var v = item.Tag as VolumeInfo;
                if (v == null || !set.Contains(v.Key)) continue;
                item.Text = text;
                item.ForeColor = Color.DarkOrange;
            }
        }

        private void OnDrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            if (e.ColumnIndex != SpaceColumn)
            {
                e.DrawDefault = true;
                return;
            }
            try
            {
                DrawSpaceCell(e);
            }
            catch
            {
                e.DrawDefault = true;
            }
        }

        private void DrawSpaceCell(DrawListViewSubItemEventArgs e)
        {
            var v = e.Item.Tag as VolumeInfo;
            Graphics g = e.Graphics;
            Rectangle b = e.Bounds;
            bool selected = e.Item.Selected;
            bool focused = list.Focused;
            Color bg = selected && focused ? SystemColors.Highlight : selected ? SystemColors.Control : SystemColors.Window;
            Color fg = selected && focused ? SystemColors.HighlightText : SystemColors.WindowText;
            using (var brush = new SolidBrush(bg)) g.FillRectangle(brush, b);
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;

            if (v == null || v.SpaceTotal <= 0)
            {
                var na = new Rectangle(b.X + 6, b.Y, Math.Max(0, b.Width - 8), b.Height);
                TextRenderer.DrawText(g, "n/a", list.Font, na, selected && focused ? fg : Color.Gray, flags);
                return;
            }

            double frac = Math.Min(1.0, Math.Max(0.0, v.SpaceUsed / v.SpaceTotal));
            int barWidth = (int)Math.Min(80, Math.Max(30, b.Width / 3.0));
            var barRect = new Rectangle(b.X + 4, b.Y + 4, barWidth, Math.Max(4, b.Height - 9));
            g.FillRectangle(Brushes.Gainsboro, barRect);
            Color fill = frac >= 0.9 ? Color.Firebrick : frac >= 0.75 ? Color.DarkOrange : Color.SteelBlue;
            int fw = (int)Math.Round(barRect.Width * frac);
            if (fw > 0)
            {
                using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, barRect.X, barRect.Y, fw, barRect.Height);
            }
            g.DrawRectangle(Pens.DarkGray, barRect);
            var textRect = new Rectangle(barRect.Right + 6, b.Y, Math.Max(0, b.Right - barRect.Right - 8), b.Height);
            TextRenderer.DrawText(g, Fmt.SpaceText(v), list.Font, textRect, fg, flags);
        }

        private void UpdateButtonState()
        {
            if (btnMount == null) return;
            VolumeInfo v = SelectedVolume;
            bool idle = !jobs.IsMutatingBusy;
            bool hasDistro = SelectedDistro != null;
            bool mounted = v != null && v.Mounted;
            btnMount.Enabled = idle && hasDistro && v != null && !v.Mounted && !v.Disconnected;
            btnUnmount.Enabled = idle && mounted;
            btnOpen.Enabled = mounted && !v.Disconnected;
            btnShell.Enabled = mounted && !v.Disconnected;
            btnCopy.Enabled = mounted;
            int n = engine.State.MountCount;
            btnUnmountAll.Enabled = idle && n > 0;
            trayUnmountAll.Enabled = btnUnmountAll.Enabled;

            JobInfo cur = jobs.Current;
            bool show = cur != null && !cur.Quiet;
            int scrubbing = scrubs.Values.Count(s => s.IsRunning);
            if (show) statusLabel.Text = cur.Name + "...";
            else if (n > 0) statusLabel.Text = string.Format("{0} mounted  |  keep-alive running{1}", n, scrubbing > 0 ? "  |  " + scrubbing + " scrub(s) running" : string.Empty);
            else statusLabel.Text = "Idle  |  watching for USB drives";
            progress.Visible = show;
            cancelTask.Visible = cur != null && cur.Cancellable;
            string trayText = string.Format("{0} - {1} mounted", UiKit.AppName, n);
            tray.Text = trayText.Length > 63 ? trayText.Substring(0, 63) : trayText;   // NotifyIcon limit
        }

        private void UpdateToolsMenu()
        {
            VolumeInfo v = SelectedVolume;
            ScrubStatus sc = GetScrub(v);
            bool running = sc != null && sc.IsRunning;
            bool mountedOk = v != null && v.Mounted && !v.Disconnected;
            miInfo.Enabled = v != null && !v.Disconnected;
            miScrub.Text = sc != null && sc.IsResumable ? "Resume scrub" : "Start scrub (verify all data)";
            miScrub.Enabled = mountedOk && !running;
            miCancelScrub.Enabled = running;
            miCheck.Enabled = v != null && !v.Mounted && !v.Disconnected && !jobs.IsMutatingBusy;
            string d = SelectedDistro;
            miInstall.Text = d != null ? "Install btrfs tools in " + d : "Install btrfs tools";
            miInstall.Enabled = d != null;
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (tray != null && tray.Visible) tray.ShowBalloonTip(4000, title, text, icon);
        }

        public VolumeInfo FindVolume(string key)
        {
            return volumes.FirstOrDefault(v => v.Key == key);
        }

        public bool IsMutatingBusy
        {
            get { return jobs.IsMutatingBusy; }
        }

        // =======================================================================================
        //  Startup
        // =======================================================================================
        private sealed class InitResult
        {
            public List<Distro> Distros;
            public bool StartupTask;
        }

        private async void RetryInit()
        {
            await InitAsync(true);
        }

        private async Task InitAsync(bool isRetry)
        {
            InitResult r = await RunJob(isRetry ? "Looking for WSL distributions" : "Starting up", async ct =>
            {
                List<Distro> distros = await DistroService.ListAsync(ct).ConfigureAwait(false);
                if (!isRetry) await engine.Mounts.SyncMountStateAsync(ct).ConfigureAwait(false);
                bool exists = StartupTask.Exists();
                if (exists && StartupTask.PointsElsewhere())
                {
                    StartupTask.Enable();
                    Log.Info("Updated the logon task to start this program (it pointed at an older version).");
                }
                return new InitResult { Distros = distros, StartupTask = exists };
            }, isRetry ? JobFlags.Quiet : JobFlags.Mutating);

            List<Distro> found = r != null ? r.Distros : new List<Distro>();
            if (r != null)
            {
                suppressEvents = true;
                chkStartup.Checked = r.StartupTask;
                suppressEvents = false;
            }

            string saved = engine.State.Settings.Distro;
            if (found.Count == 0)
            {
                initRetries++;
                if (initRetries <= 6)
                {
                    Log.Warn(string.Format("WSL did not report any distributions (it may still be starting). Retrying in 10 s ({0}/6)...", initRetries));
                    retryInitAt = DateTime.Now.AddSeconds(10);
                }
                else if (string.IsNullOrEmpty(saved))
                {
                    Log.Error("No WSL2 distribution is installed. From an elevated prompt run:  wsl --install -d Ubuntu   then restart this program.");
                }
                if (!string.IsNullOrEmpty(saved) && !cmbDistro.Items.Contains(saved))
                {
                    suppressEvents = true;
                    cmbDistro.Items.Add(saved);
                    cmbDistro.SelectedItem = saved;
                    suppressEvents = false;
                    Log.Info(string.Format("Using the saved distro '{0}' until WSL responds.", saved));
                }
            }
            else
            {
                retryInitAt = null;
                string pick = DistroService.Resolve(!string.IsNullOrEmpty(preferredDistro) ? preferredDistro : saved, found);
                suppressEvents = true;
                cmbDistro.Items.Clear();
                foreach (Distro d in found) cmbDistro.Items.Add(d.Name);
                cmbDistro.SelectedItem = pick;
                suppressEvents = false;
                engine.State.UpdateSettings(x => x.Distro = pick);
                Log.Info(string.Format("Using WSL2 distro '{0}'.", pick));
            }
            UpdateButtonState();
            if (!isRetry) RequestScan(false, false, null, 0, true);
        }

        // =======================================================================================
        //  Scanning and plug-in detection
        // =======================================================================================
        private static string Signature(IEnumerable<string> ids)
        {
            return string.Join(";", ids.OrderBy(x => x, StringComparer.Ordinal));
        }

        private async void RequestScan(bool clearCache, bool sync, List<string> newDiskIds, int retry, bool autoMountAll)
        {
            bool includeAll = engine.State.Settings.ShowAllDisks;
            ScanResult r = await RunJob("Scanning disks", async ct =>
            {
                if (clearCache) engine.Cache.Clear();
                if (sync) await engine.Mounts.SyncMountStateAsync(ct).ConfigureAwait(false);
                return await engine.Scanner.ScanAsync(includeAll, ct).ConfigureAwait(false);
            }, JobFlags.None);

            if (r != null)
            {
                volumes = r.Volumes;
                lastDiskIds = r.Snapshot;
                lastDiskSig = Signature(r.Snapshot);
            }

            // forget scrub state of drives that are no longer mounted
            var mountedNames = new HashSet<string>(volumes.Where(v => v.Mounted).Select(v => v.MountName), StringComparer.Ordinal);
            foreach (string k in scrubs.Keys.ToList())
            {
                if (!mountedNames.Contains(k)) scrubs.Remove(k);
            }
            RenderVolumes();

            // keep an open drive-info window in step with mount/unmount changes
            if (infoForm != null && !infoForm.IsDisposed)
            {
                VolumeInfo nv = FindVolume(infoForm.VolumeKey);
                if (nv != null && nv.Mounted != infoForm.VolumeMounted) await infoForm.RefreshAsync();
            }

            if (newDiskIds != null && newDiskIds.Count > 0)
            {
                List<VolumeInfo> fresh = volumes.Where(v => !v.Mounted && !v.Disconnected && newDiskIds.Contains(v.DiskUniqueId)).ToList();
                if (fresh.Count > 0)
                {
                    if (engine.State.Settings.AutoMount) RequestMount(fresh);
                    else ShowBalloon("btrfs drive detected", string.Join(", ", fresh.Select(v => v.DisplayLabel)) + " is ready to mount.", ToolTipIcon.Info);
                }
                else if (retry < 2)
                {
                    // a freshly plugged disk can take a few seconds before its partitions enumerate
                    retryScanIds = newDiskIds;
                    retryScanCount = retry + 1;
                    retryScanAt = DateTime.Now.AddSeconds(3);
                }
            }

            if (autoMountAll)
            {
                RequestScrubPoll();   // pick up a scrub that was running before this session
                if (engine.State.Settings.AutoMount)
                {
                    List<VolumeInfo> targets = volumes.Where(v => !v.Mounted && !v.Disconnected).ToList();
                    if (targets.Count > 0)
                    {
                        Log.Info("Auto-mount: mounting drives that were already plugged in.");
                        RequestMount(targets);
                    }
                }
            }
        }

        private async void RequestSnapshot()
        {
            bool includeAll = engine.State.Settings.ShowAllDisks;
            List<string> ids = await RunJob("Checking USB devices",
                ct => Task.FromResult(engine.Scanner.GetSnapshot(includeAll)), JobFlags.Quiet | JobFlags.Unique);
            if (ids == null) return;
            string sig = Signature(ids);
            if (sig == lastDiskSig) return;
            List<string> added = ids.Where(x => !lastDiskIds.Contains(x)).ToList();
            List<string> removed = lastDiskIds.Where(x => !ids.Contains(x)).ToList();
            lastDiskSig = sig;
            lastDiskIds = ids;

            List<MountEntry> mounts = engine.State.Mounts;
            foreach (string gone in removed)
            {
                List<MountEntry> lost = mounts.Where(m => m.DiskUniqueId == gone).ToList();
                if (lost.Count > 0)
                {
                    Log.Error(string.Format("A mounted drive was unplugged without unmounting ({0}). Select it and click Unmount to clean up; " +
                                            "run a scrub or offline check on it next time.", string.Join(", ", lost.Select(m => m.Name))));
                    ShowBalloon("Drive removed while mounted", "Always Unmount / Eject before unplugging.", ToolTipIcon.Warning);
                }
                else
                {
                    Log.Info("USB disk removed.");
                }
            }
            if (added.Count > 0) Log.Info("USB disk attached, scanning...");
            RequestScan(false, false, added, 0, false);
        }

        // =======================================================================================
        //  Mount / eject / open
        // =======================================================================================
        private async void RequestMount(List<VolumeInfo> targets)
        {
            string distro = SelectedDistro;
            if (distro == null)
            {
                Log.Error("No WSL2 distribution selected. Install one with: wsl --install -d Ubuntu");
                return;
            }
            targets = targets.Where(v => v != null).ToList();
            if (targets.Count == 0) return;
            AppSettings s = engine.State.Settings;
            SetRowText(targets.Select(v => v.Key), "Mounting...");

            List<Tuple<VolumeInfo, MountEntry>> results = await RunJob("Mounting", async ct =>
            {
                var done = new List<Tuple<VolumeInfo, MountEntry>>();
                foreach (VolumeInfo v in targets)
                {
                    MountEntry e = await engine.Mounts.MountAsync(v, distro, s.Options, ct).ConfigureAwait(false);
                    if (e != null && s.OpenExplorer) await MountManager.OpenInExplorerAsync(e.Distro, e.Name).ConfigureAwait(false);
                    done.Add(Tuple.Create(v, e));
                }
                return done;
            }, JobFlags.Mutating);

            if (results != null)
            {
                foreach (Tuple<VolumeInfo, MountEntry> t in results)
                {
                    if (t.Item2 != null)
                    {
                        ShowBalloon("Drive mounted", string.Format("{0} is available at {1}", t.Item1.DisplayLabel,
                            MountManager.WindowsPath(t.Item2.Distro, t.Item2.Name)), ToolTipIcon.Info);
                    }
                    else
                    {
                        ShowBalloon("Mount failed", t.Item1.DisplayLabel + ": see the log for details.", ToolTipIcon.Error);
                    }
                }
            }
            RequestScan(false, false, null, 0, false);
        }

        private async void RequestUnmount(VolumeInfo v)
        {
            if (v == null) return;
            SetRowText(new[] { v.Key }, "Ejecting...");
            bool ok = await RunJob("Ejecting", ct => engine.Mounts.DismountAsync(v, ct), JobFlags.Mutating | JobFlags.Cancellable);
            if (ok) ShowBalloon("Safe to eject", v.DisplayLabel + " was flushed and detached.", ToolTipIcon.Info);
            RequestScan(false, false, null, 0, false);
        }

        private async void RequestUnmountAll(bool thenExit)
        {
            List<VolumeInfo> snapshot = volumes.ToList();
            SetRowText(snapshot.Where(v => v.Mounted).Select(v => v.Key), "Ejecting...");
            await RunJob("Ejecting all", async ct =>
            {
                await engine.Mounts.DismountAllAsync(snapshot, ct).ConfigureAwait(false);
                return true;
            }, JobFlags.Mutating | JobFlags.Cancellable);

            if (thenExit)
            {
                reallyExit = true;
                Close();
                return;
            }
            if (engine.State.MountCount == 0) ShowBalloon("Safe to eject", "All btrfs drives were flushed and detached.", ToolTipIcon.Info);
            RequestScan(false, false, null, 0, false);
        }

        private async void OpenInExplorer(VolumeInfo v)
        {
            if (v == null || !v.Mounted) return;
            await RunJob("Opening Explorer", async ct =>
            {
                await MountManager.OpenInExplorerAsync(v.Distro, v.MountName).ConfigureAwait(false);
                return true;
            }, JobFlags.None);
        }

        private void OpenShell(VolumeInfo v)
        {
            if (v == null || !v.Mounted) return;
            Process.Start(new ProcessStartInfo(AppPaths.WslExe, Wsl.JoinArguments(new[] { "-d", v.Distro, "--cd", "/mnt/wsl/" + v.MountName }))
            {
                UseShellExecute = true
            });
        }

        private void CopyPath(VolumeInfo v)
        {
            if (v == null || !v.Mounted) return;
            Clipboard.SetText(v.WindowsPath);
            Log.Info("Copied: " + v.WindowsPath);
        }

        private void OnListDoubleClick(object sender, EventArgs e)
        {
            VolumeInfo v = SelectedVolume;
            if (v == null) return;
            if (v.Mounted && !v.Disconnected) OpenInExplorer(v);
            else if (!v.Mounted && !jobs.IsMutatingBusy) RequestMount(new List<VolumeInfo> { v });
        }

        private async void SetStartupTask(bool enable)
        {
            await RunJob("Updating logon task", ct =>
            {
                if (enable) StartupTask.Enable();
                else StartupTask.Disable();
                return Task.FromResult(true);
            }, JobFlags.None);
        }

        // =======================================================================================
        //  Drive tools
        // =======================================================================================
        private void ShowDriveInfo(VolumeInfo v)
        {
            if (v == null) return;
            if (infoForm != null && !infoForm.IsDisposed) infoForm.Close();
            infoForm = new DriveInfoForm(this, v);
            infoForm.FormClosed += (s, e) => infoForm = null;
            infoForm.Show(this);
        }

        public async Task<DriveInfo> ReadDriveInfoAsync(VolumeInfo v)
        {
            DriveInfo info = await RunJob("Reading drive info",
                ct => engine.Maintenance.GetDriveInfoAsync(v.Distro, v.MountName, ct), JobFlags.None);
            if (info == null) return null;
            if (info.ToolsMissing)
            {
                RequestInstallTools("the drive details", false);
                return info;
            }
            if (info.Scrub != null)
            {
                scrubs[info.MountName] = info.Scrub;
                UpdateStatusTexts();
            }
            return info;
        }

        public async void StartScrub(VolumeInfo v, bool resume)
        {
            if (v == null || !v.Mounted || v.Disconnected) return;
            ScrubCommandResult r = await RunJob("Starting scrub",
                ct => engine.Maintenance.StartScrubAsync(v.Distro, v.MountName, resume, ct), JobFlags.None);
            if (r == null) return;
            if (r.ToolsMissing)
            {
                RequestInstallTools("a scrub", false);
                return;
            }
            if (r.Ok)
            {
                scrubs[r.Name] = ScrubStatus.StartedNow();
                UpdateStatusTexts();
                if (infoForm != null && !infoForm.IsDisposed && infoForm.MountName == r.Name) infoForm.ShowScrub(scrubs[r.Name]);
            }
            scrubPollAt = DateTime.Now.AddSeconds(2);
            UpdateButtonState();
        }

        public async void CancelScrub(VolumeInfo v)
        {
            if (v == null || !v.Mounted) return;
            await RunJob("Cancelling scrub", ct => engine.Maintenance.CancelScrubAsync(v.Distro, v.MountName, ct), JobFlags.None);
            RequestScrubPoll();
        }

        private async void RequestScrubPoll()
        {
            List<VolumeInfo> targets = volumes.Where(v => v.Mounted && !v.Disconnected && !string.IsNullOrEmpty(v.MountName)).ToList();
            if (targets.Count == 0) return;
            List<ScrubProgress> results = await RunJob("Checking scrub progress", async ct =>
            {
                var list2 = new List<ScrubProgress>();
                foreach (VolumeInfo t in targets)
                {
                    list2.Add(await engine.Maintenance.GetScrubProgressAsync(t.Distro, t.MountName, ct).ConfigureAwait(false));
                }
                return list2;
            }, JobFlags.Quiet | JobFlags.Unique);
            if (results == null) return;

            foreach (ScrubProgress p in results.Where(x => x != null && !x.ToolsMissing && x.Scrub != null))
            {
                ScrubStatus prev;
                scrubs.TryGetValue(p.Name, out prev);
                scrubs[p.Name] = p.Scrub;
                if (prev != null && prev.IsRunning && !p.Scrub.IsRunning)
                {
                    string msg = string.Format("{0}: scrub {1}, {2}.", p.Name, p.Scrub.Status, BtrfsParsers.ScrubErrorText(p.Scrub));
                    Log.Write(msg, p.Scrub.HasErrors ? LogLevel.Error : LogLevel.Ok);
                    ShowBalloon("Scrub complete", msg, p.Scrub.HasErrors ? ToolTipIcon.Warning : ToolTipIcon.Info);
                }
                if (infoForm != null && !infoForm.IsDisposed && infoForm.MountName == p.Name) infoForm.ShowScrub(p.Scrub);
            }
            UpdateStatusTexts();
            UpdateButtonState();
        }

        public async void OfflineCheck(VolumeInfo v)
        {
            if (v == null) return;
            if (v.Mounted || v.Disconnected)
            {
                UiKit.Show(this, "Unmount (eject) the drive first. An offline check needs the filesystem to be unmounted.",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string distro = SelectedDistro;
            if (distro == null)
            {
                Log.Error("No WSL2 distribution selected.");
                return;
            }
            DialogResult answer = UiKit.Show(this, string.Format(
                "Run a read-only filesystem check on '{0}'?\r\n\r\n" +
                "- Nothing on the drive is changed (btrfs check --readonly).\r\n" +
                "- The drive can't be mounted while the check runs.\r\n" +
                "- On a large drive this can take from several minutes to over an hour. You can cancel at any time from the status bar.\r\n\r\n" +
                "Continue?", v.DisplayLabel), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            SetRowText(new[] { v.Key }, "Checking...");
            CheckResult r = await RunJob(string.Format("Checking {0} (btrfs check)", v.DisplayLabel),
                ct => engine.Maintenance.OfflineCheckAsync(v, distro, ct), JobFlags.Mutating | JobFlags.Cancellable);
            RequestScan(false, false, null, 0, false);
            if (r == null) return;
            if (r.ToolsMissing)
            {
                RequestInstallTools("the offline check", false);
                return;
            }
            if (r.Cancelled)
            {
                UiKit.Show(this, string.Format("The check of '{0}' was cancelled.", r.Label), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (r.Stage != CheckStage.Check)
            {
                UiKit.Show(this, string.Format("The check of '{0}' could not start.\r\n\r\n{1}", r.Label, r.Summary),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (r.Ok)
            {
                ShowBalloon("Check complete", r.Label + ": no errors found.", ToolTipIcon.Info);
                UiKit.Show(this, string.Format("No errors found on '{0}'.\r\n\r\n{1}\r\n\r\nFull report:\r\n{2}", r.Label, r.Summary, r.LogFile),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                ShowBalloon("Check found problems", r.Label + ": see the report.", ToolTipIcon.Warning);
                DialogResult open = UiKit.Show(this, string.Format(
                    "btrfs check reported problems on '{0}' (exit code {1}).\r\n\r\n{2}\r\n\r\n" +
                    "Recommended next steps:\r\n" +
                    "1. Copy any important data off the drive first.\r\n" +
                    "2. Mount it and run a scrub to see which files are affected.\r\n" +
                    "3. Do NOT run 'btrfs check --repair' unless the btrfs documentation or developers advise it; it can make damage worse.\r\n\r\n" +
                    "Open the full report now?", r.Label, r.ExitCode, r.Summary), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (open == DialogResult.Yes && !string.IsNullOrEmpty(r.LogFile))
                {
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + r.LogFile + "\"") { UseShellExecute = true });
                }
            }
        }

        private async void RequestInstallTools(string reason, bool direct)
        {
            string distro = SelectedDistro;
            if (distro == null)
            {
                Log.Error("No WSL2 distribution selected.");
                return;
            }
            if (jobs.IsPending("Installing btrfs tools")) return;
            if (!direct)
            {
                DialogResult answer = UiKit.Show(this, string.Format(
                    "The btrfs tools (btrfs-progs) are not installed in '{0}', so {1} can't run.\r\n\r\n" +
                    "Install them now? This uses the distro's package manager and needs an internet connection.",
                    distro, string.IsNullOrEmpty(reason) ? "this" : reason), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }
            bool ok = await RunJob("Installing btrfs tools", ct => engine.Maintenance.InstallToolsAsync(distro, ct), JobFlags.Cancellable);
            if (ok)
            {
                UiKit.Show(this, string.Format("btrfs tools are installed in '{0}'. Drive info, scrub and offline check are ready to use.", distro),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                UiKit.Show(this, "Installing the btrfs tools failed. The log window shows the package manager output.",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
