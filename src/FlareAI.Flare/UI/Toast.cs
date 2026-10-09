using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// A small notice at the bottom right of Flare's window that goes away by itself: "3 style issues in about.htm — Show".
    /// Never takes focus, never blocks; clicking it runs the action.
    /// </summary>
    internal sealed class Toast : Form
    {
        private static Toast _open;

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */; return cp; }
        }

        private Toast(string text, string actionLabel, Action action)
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            BackColor = Color.FromArgb(45, 45, 48); ForeColor = Color.White; Font = SystemFonts.MessageBoxFont; Padding = new Padding(12, 10, 12, 10);
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(340, 0), Left = 12, Top = 10, ForeColor = Color.White };
            Controls.Add(label);
            var w = Math.Max(240, label.PreferredWidth + 24);
            var h = label.PreferredHeight + 20;
            if (action != null)
            {
                var link = new LinkLabel { Text = actionLabel, AutoSize = true, LinkColor = Color.FromArgb(120, 180, 255), Left = 12, Top = h };
                link.LinkClicked += (s, e) => { Close(); action(); };
                Controls.Add(link);
                h += 24;
            }
            ClientSize = new Size(w, h);
            var timer = new Timer { Interval = 7000 };
            timer.Tick += (s, e) => { timer.Stop(); if (!IsDisposed) Close(); };
            timer.Start();
            Click += (s, e) => Close();
            label.Click += (s, e) => { if (action != null) { Close(); action(); } };
        }

        /// <summary>Shows the notice over the owner's bottom-right corner, replacing any notice still showing.</summary>
        public static void Show(Form owner, string text, string actionLabel = null, Action action = null)
        {
            try
            {
                if (_open != null && !_open.IsDisposed) _open.Close();
                var t = new Toast(text, actionLabel, action);
                var r = owner != null ? owner.Bounds : Screen.PrimaryScreen.WorkingArea;
                t.Location = new Point(r.Right - t.Width - 24, r.Bottom - t.Height - 48);
                _open = t;
                t.Show(owner);
            }
            catch (Exception ex) { FlareAI.Core.Diagnostics.Log("toast", ex); }
        }
    }
}
