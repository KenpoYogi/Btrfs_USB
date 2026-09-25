// Linux USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using LinuxUsbMounter.Core;

namespace LinuxUsbMounter.UI
{
    /// <summary>What the drive-info window needs from the main window.</summary>
    internal interface IDriveActions
    {
        VolumeInfo FindVolume(string key);
        Task<DriveInfo> ReadDriveInfoAsync(VolumeInfo volume);
        void StartScrub(VolumeInfo volume, bool resume);
        void CancelScrub(VolumeInfo volume);
        void OfflineCheck(VolumeInfo volume);
        ScrubStatus GetScrub(VolumeInfo volume);
        bool IsMutatingBusy { get; }
    }

    internal sealed class DriveInfoForm : Form
    {
        private readonly IDriveActions actions;
        private VolumeInfo volume;
        private string scrubStatus = string.Empty;

        private readonly Label head;
        private readonly Label sub;
        private readonly UsageBar bar;
        private readonly FlowLayoutPanel legend;
        private readonly ListView alloc;
        private readonly Label healthSummary;
        private readonly ListView health;
        private readonly Label scrubText;
        private readonly ProgressBar scrubBar;
        private readonly Button btnScrub;
        private readonly Button btnCancelScrub;
        private readonly Button btnCheck;
        private readonly Button btnRefresh;
        private readonly Button btnClose;

        public DriveInfoForm(IDriveActions actions, VolumeInfo volume)
        {
            this.actions = actions;
            this.volume = volume;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = string.Format("{0} - drive info", volume.DisplayLabel);
            Size = new Size(720, 780);
            MinimumSize = new Size(620, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiKit.BaseFont;
            Icon = UiKit.AppIcon;
            ShowInTaskbar = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                Padding = new Padding(14),
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            head = new Label { AutoSize = true, Font = UiKit.HeadlineFont, Text = "Reading drive information..." };
            sub = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(660, 0),
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 2, 0, 8),
                Text = string.Format("UUID {0}  |  disk {1} {2}", volume.Uuid, volume.DiskNumber, volume.Model)
            };
            bar = new UsageBar { Dock = DockStyle.Fill };
            legend = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };

            alloc = MakeList(new[] { "Type", "Profile", "Allocated", "Used", "Use %" }, new[] { 130, 80, 110, 110, 70 });
            healthSummary = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(660, 0),
                Font = UiKit.BoldFont,
                Margin = new Padding(0, 0, 0, 4)
            };
            health = MakeList(new[] { "Counter", "Value", "Device" }, new[] { 200, 90, 200 });

            scrubText = new Label { AutoSize = true, MaximumSize = new Size(660, 0), Margin = new Padding(0, 0, 0, 6) };
            scrubBar = new ProgressBar { Maximum = 1000, Height = 16, Dock = DockStyle.Fill, Visible = false };

            var scrubButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 0, 0) };
            btnScrub = UiKit.MakeButton("Start scrub", 120);
            btnCancelScrub = UiKit.MakeButton("Cancel scrub", 120);
            btnCheck = UiKit.MakeButton("Offline check (read-only)...", 190);
            btnScrub.Enabled = btnCancelScrub.Enabled = btnCheck.Enabled = false;
            scrubButtons.Controls.AddRange(new Control[] { btnScrub, btnCancelScrub, btnCheck });

            var bottom = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 14, 0, 0)
            };
            btnClose = UiKit.MakeButton("Close", 90);
            btnRefresh = UiKit.MakeButton("Refresh", 90);
            bottom.Controls.AddRange(new Control[] { btnClose, btnRefresh });

            var rows = new List<KeyValuePair<Control, float>>
            {
                Row(head, 0), Row(sub, 0), Row(bar, 34), Row(legend, 0),
                Row(UiKit.MakeSectionLabel("Allocation"), 0), Row(alloc, 136),
                Row(UiKit.MakeSectionLabel("Health (device error counters)"), 0), Row(healthSummary, 0), Row(health, 131),
                Row(UiKit.MakeSectionLabel("Scrub (verifies checksums of all data and metadata)"), 0),
                Row(scrubText, 0), Row(scrubBar, 22), Row(scrubButtons, 0), Row(bottom, 0)
            };
            layout.RowCount = rows.Count;
            for (int i = 0; i < rows.Count; i++)
            {
                layout.RowStyles.Add(rows[i].Value > 0
                    ? new RowStyle(SizeType.Absolute, rows[i].Value)
                    : new RowStyle(SizeType.AutoSize));
                layout.Controls.Add(rows[i].Key, 0, i);
            }
            Controls.Add(layout);
            AcceptButton = btnClose;
            CancelButton = btnClose;

            btnClose.Click += (s, e) => Close();
            btnRefresh.Click += async (s, e) => await RefreshAsync();
            btnScrub.Click += (s, e) =>
            {
                btnScrub.Enabled = false;
                actions.StartScrub(this.volume, scrubStatus == "aborted" || scrubStatus == "interrupted");
            };
            btnCancelScrub.Click += (s, e) =>
            {
                btnCancelScrub.Enabled = false;
                actions.CancelScrub(this.volume);
            };
            btnCheck.Click += (s, e) =>
            {
                VolumeInfo v = this.volume;
                Close();
                actions.OfflineCheck(v);
            };
            Shown += async (s, e) => await RefreshAsync();

            ResumeLayout(false);
            PerformLayout();
        }

        public string VolumeKey { get { return volume.Key; } }
        public string MountName { get { return volume.MountName; } }
        public bool VolumeMounted { get { return volume.Mounted; } }

        private static KeyValuePair<Control, float> Row(Control c, float height)
        {
            return new KeyValuePair<Control, float>(c, height);
        }

        private static ListView MakeList(string[] columns, int[] widths)
        {
            var lv = new SmoothListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Dock = DockStyle.Fill
            };
            for (int i = 0; i < columns.Length; i++) lv.Columns.Add(columns[i], widths[i]);
            return lv;
        }

        /// <summary>Re-reads the drive (it may have been mounted or unmounted since the window opened).</summary>
        public async Task RefreshAsync()
        {
            VolumeInfo latest = actions.FindVolume(volume.Key);
            if (latest != null) volume = latest;
            if (volume.Mounted && !volume.Disconnected)
            {
                head.Text = "Reading drive information...";
                DriveInfo info = await actions.ReadDriveInfoAsync(volume);
                if (IsDisposed) return;
                if (info == null)
                {
                    head.Text = "Could not read drive information (see the log).";
                }
                else if (info.ToolsMissing)
                {
                    head.Text = "btrfs tools needed";
                    sub.Text = "Drive details need btrfs-progs inside your WSL distro.";
                }
                else
                {
                    ShowDriveInfo(info);
                }
            }
            else
            {
                ShowFromSuperblock();
            }
        }

        private void SetLegend(IList<UsageSegment> segments)
        {
            bar.SetSegments(segments);
            legend.SuspendLayout();
            legend.Controls.Clear();
            foreach (UsageSegment s in segments.Where(x => x.Value > 0))
            {
                legend.Controls.Add(new Label
                {
                    Width = 12,
                    Height = 12,
                    BackColor = s.Color,
                    BorderStyle = BorderStyle.FixedSingle,
                    Margin = new Padding(0, 4, 4, 0)
                });
                legend.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = string.Format("{0}: {1}", s.Name, Fmt.BytesZero(s.Value)),
                    Margin = new Padding(0, 2, 16, 4)
                });
            }
            legend.ResumeLayout();
        }

        private void ShowFromSuperblock()
        {
            alloc.Items.Clear();
            health.Items.Clear();
            if (volume.SpaceTotal > 0)
            {
                head.Text = string.Format("~{0} free of {1}", Fmt.BytesZero(volume.SpaceFree), Fmt.Bytes(volume.SpaceTotal));
                SetLegend(new[]
                {
                    new UsageSegment("Used", volume.SpaceUsed, Color.DodgerBlue),
                    new UsageSegment("Free (approx.)", volume.SpaceFree, Color.Gainsboro)
                });
            }
            else
            {
                head.Text = "Drive not mounted";
                SetLegend(new UsageSegment[0]);
            }
            sub.Text = volume.Disconnected
                ? "This drive is unplugged."
                : "Not mounted: these figures come from the filesystem header and are approximate. Mount the drive for exact usage, allocation details and error counters.";
            healthSummary.Text = "Error counters are available while the drive is mounted.";
            healthSummary.ForeColor = Color.DimGray;
            scrubText.Text = "A scrub runs on a mounted drive. For an unmounted drive use \"Offline check (read-only)\".";
            scrubBar.Visible = false;
            btnScrub.Enabled = false;
            btnCancelScrub.Enabled = false;
            btnCheck.Enabled = !volume.Disconnected && !actions.IsMutatingBusy;
        }

        private void ShowDriveInfo(DriveInfo info)
        {
            UsageInfo u = info.Usage;
            double free = u.BestFree;
            double usedPct = u.DeviceSize > 0 ? 100.0 * (u.DeviceSize - free) / u.DeviceSize : 0;
            head.Text = string.Format("{0} free of {1}", Fmt.BytesZero(free), Fmt.Bytes(u.DeviceSize));
            sub.Text = string.Format(CultureInfo.CurrentCulture,
                "{0:N1}% used  |  worst-case free (min): {1}  |  data ratio {2}, metadata ratio {3}  |  mounted at /mnt/wsl/{4}",
                usedPct, Fmt.BytesZero(u.FreeMin), u.DataRatio, u.MetadataRatio, volume.MountName);

            // allocation in raw device bytes (DUP/RAID1 profiles store two copies)
            double dataUsed = 0, dataSize = 0, metaUsed = 0, metaSize = 0, sysSize = 0;
            foreach (BlockGroup b in u.Blocks)
            {
                int k = BtrfsParsers.ProfileRatio(b.Profile);
                switch (b.Type)
                {
                    case "Data": dataUsed += b.Used * k; dataSize += b.Size * k; break;
                    case "Metadata": metaUsed += b.Used * k; metaSize += b.Size * k; break;
                    case "System": sysSize += b.Size * k; break;
                }
            }
            SetLegend(new[]
            {
                new UsageSegment("Data", dataUsed, Color.DodgerBlue),
                new UsageSegment("Data (allocated, empty)", Math.Max(0, dataSize - dataUsed), Color.LightSkyBlue),
                new UsageSegment("Metadata", metaUsed, Color.SeaGreen),
                new UsageSegment("Metadata (reserved)", Math.Max(0, metaSize - metaUsed), Color.PaleGreen),
                new UsageSegment("System", sysSize, Color.SlateGray),
                new UsageSegment("Unallocated", u.Unallocated, Color.Gainsboro)
            });

            alloc.BeginUpdate();
            alloc.Items.Clear();
            foreach (BlockGroup b in u.Blocks)
            {
                var it = new ListViewItem(b.Type);
                it.SubItems.Add(b.Profile);
                it.SubItems.Add(Fmt.BytesZero(b.Size));
                it.SubItems.Add(Fmt.BytesZero(b.Used));
                it.SubItems.Add(b.Size > 0 ? (100.0 * b.Used / b.Size).ToString("N1", CultureInfo.CurrentCulture) + "%" : string.Empty);
                alloc.Items.Add(it);
            }
            var reserve = new ListViewItem("Global reserve");
            reserve.SubItems.AddRange(new[] { string.Empty, Fmt.BytesZero(u.GlobalReserve), Fmt.BytesZero(u.GlobalReserveUsed), string.Empty });
            alloc.Items.Add(reserve);
            var unalloc = new ListViewItem("Unallocated");
            unalloc.SubItems.AddRange(new[] { string.Empty, Fmt.BytesZero(u.Unallocated), string.Empty, string.Empty });
            alloc.Items.Add(unalloc);
            alloc.EndUpdate();

            health.BeginUpdate();
            health.Items.Clear();
            long errTotal = 0;
            foreach (DeviceStat st in info.Stats)
            {
                var it = new ListViewItem(st.Counter.Replace('_', ' '));
                it.SubItems.Add(st.Value.ToString(CultureInfo.CurrentCulture));
                it.SubItems.Add(st.Device);
                if (st.Value > 0) it.ForeColor = Color.Firebrick;
                health.Items.Add(it);
                errTotal += st.Value;
            }
            health.EndUpdate();
            if (info.Stats.Count == 0)
            {
                healthSummary.Text = "Could not read the error counters (see the log).";
                healthSummary.ForeColor = Color.DimGray;
            }
            else if (errTotal == 0)
            {
                healthSummary.Text = "No device errors recorded.";
                healthSummary.ForeColor = Color.ForestGreen;
            }
            else
            {
                healthSummary.Text = errTotal + " error(s) recorded. The counters persist until reset, so compare with earlier readings; " +
                                     "rising numbers mean the drive or its cable/enclosure is failing.";
                healthSummary.ForeColor = Color.Firebrick;
            }
            btnCheck.Enabled = false;
            ShowScrub(info.Scrub);
        }

        /// <summary>Called by the main window on every scrub progress update for this drive.</summary>
        public void ShowScrub(ScrubStatus s)
        {
            if (IsDisposed || s == null) return;
            scrubStatus = s.Status;
            scrubText.Text = BtrfsParsers.ScrubText(s);
            scrubBar.Visible = s.IsRunning || s.IsResumable;
            scrubBar.Value = (int)Math.Round(Math.Min(1000, Math.Max(0, s.Percent * 10)));
            btnScrub.Text = s.IsResumable ? "Resume scrub" : "Start scrub";
            btnScrub.Enabled = !s.IsRunning && volume.Mounted && !volume.Disconnected;
            btnCancelScrub.Enabled = s.IsRunning;
        }
    }
}
