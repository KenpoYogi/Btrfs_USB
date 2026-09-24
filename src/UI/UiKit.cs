// Btrfs USB Mounter
// Copyright (c) 2026 Jay Weiner
// SPDX-License-Identifier: LicenseRef-MIT-Commons-Clause
//
// Licensed under the MIT License with the Commons Clause License Condition v1.0:
// you may use, copy, modify and distribute it, but not sell it or a product or service
// whose value derives substantially from it. See the LICENSE file.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BtrfsUsbMounter.Core;

namespace BtrfsUsbMounter.UI
{
    internal static class UiKit
    {
        public const string AppName = "Btrfs USB Mounter";

        public static readonly Font BaseFont = new Font("Segoe UI", 9f);
        public static readonly Font BoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font HeadlineFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        public static readonly Font LogFont = new Font("Consolas", 9f);

        public static Icon AppIcon
        {
            get
            {
                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(AppPaths.ExePath);
                    if (icon != null) return icon;
                }
                catch
                {
                    // fall through
                }
                return SystemIcons.Application;
            }
        }

        public static Button MakeButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = 30,
                Margin = new Padding(0, 0, 8, 0),
                FlatStyle = FlatStyle.System,
                UseVisualStyleBackColor = true
            };
        }

        public static Label MakeSectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = BoldFont,
                Margin = new Padding(0, 12, 0, 4)
            };
        }

        public static CheckBox MakeCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Text = text,
                AutoSize = true,
                Checked = isChecked,
                Margin = new Padding(0, 6, 14, 0)
            };
        }

        /// <summary>Owner for dialogs: the form when it is visible, otherwise the desktop.</summary>
        public static IWin32Window OwnerFor(Form form)
        {
            return form != null && form.Visible && !form.IsDisposed ? form : null;
        }

        public static DialogResult Show(Form owner, string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            IWin32Window o = OwnerFor(owner);
            return o != null
                ? MessageBox.Show(o, text, AppName, buttons, icon)
                : MessageBox.Show(text, AppName, buttons, icon, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
        }
    }

    /// <summary>ListView with double buffering (no flicker on refresh or owner drawing).</summary>
    internal sealed class SmoothListView : ListView
    {
        public SmoothListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }

    internal sealed class UsageSegment
    {
        public UsageSegment(string name, double value, Color color)
        {
            Name = name;
            Value = value;
            Color = color;
        }

        public string Name { get; private set; }
        public double Value { get; private set; }
        public Color Color { get; private set; }
    }

    /// <summary>Horizontal stacked bar showing how a filesystem's space is divided.</summary>
    internal sealed class UsageBar : Control
    {
        private List<UsageSegment> segments = new List<UsageSegment>();

        public UsageBar()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 30;
        }

        public IList<UsageSegment> Segments
        {
            get { return segments.AsReadOnly(); }
        }

        public void SetSegments(IEnumerable<UsageSegment> value)
        {
            segments = new List<UsageSegment>(value ?? new UsageSegment[0]);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : SystemColors.Control);
            var outer = new Rectangle(0, 2, Math.Max(1, Width - 1), Math.Max(4, Height - 5));
            double total = 0;
            foreach (UsageSegment s in segments) total += Math.Max(0, s.Value);
            if (total > 0)
            {
                float x = outer.X;
                foreach (UsageSegment s in segments)
                {
                    float w = (float)(outer.Width * Math.Max(0, s.Value) / total);
                    if (w >= 0.5f)
                    {
                        using (var brush = new SolidBrush(s.Color))
                        {
                            g.FillRectangle(brush, x, outer.Y, w, outer.Height);
                        }
                    }
                    x += w;
                }
            }
            g.DrawRectangle(Pens.Gray, outer);
        }
    }
}
