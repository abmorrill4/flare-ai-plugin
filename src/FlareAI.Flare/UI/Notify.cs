using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Every message the plugin shows in a dialog goes through here, so captions, icons, button order and wording
    /// are the same everywhere:
    ///   caption   "House Style", or "House Style — &lt;action&gt;" when the message belongs to one command;
    ///   Info      information icon, OK;
    ///   Warn      warning icon, OK (something did not work, and what to do);
    ///   Error     error icon, "what happened" + "what to do", OK and, when there is something to copy, Copy details;
    ///   Confirm   question icon, OK / Cancel (Yes / No only for "Open the folder?"-style follow-ups).
    /// Background work never uses these: it reports in the pane or a toast.
    /// </summary>
    internal static class Notify
    {
        public const string Product = "House Style";

        /// <summary>"House Style" or "House Style — action".</summary>
        public static string Caption(string action) { return string.IsNullOrEmpty(action) ? Product : Product + " — " + action; }

        public static void Info(IWin32Window owner, string message, string action = null)
        {
            MessageBox.Show(owner, message, Caption(action), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Warn(IWin32Window owner, string message, string action = null)
        {
            MessageBox.Show(owner, message, Caption(action), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>OK / Cancel question. True for OK.</summary>
        public static bool Confirm(IWin32Window owner, string message, string action = null)
        {
            return MessageBox.Show(owner, message, Caption(action), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }

        /// <summary>Yes / No follow-up question ("Open the folder?"). True for Yes.</summary>
        public static bool AskYesNo(IWin32Window owner, string message, string action = null)
        {
            return MessageBox.Show(owner, message, Caption(action), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        /// <summary>
        /// An error: what happened, then what to do. <paramref name="details"/> (an exception or any text) is not shown;
        /// the Copy details button puts it on the clipboard for a support request.
        /// </summary>
        public static void Error(IWin32Window owner, string whatHappened, string whatToDo, object details = null, string action = null)
        {
            var text = whatHappened + (string.IsNullOrEmpty(whatToDo) ? "" : "\n\n" + whatToDo);
            var copy = details == null ? null : details + "\n\nLog: " + Diagnostics.LogPath;
            using (var dlg = new MessageDialog(Caption(action), text, SystemIcons.Error, copy)) dlg.ShowDialog(owner);
        }

        /// <summary>The standard failure: "House Style couldn't &lt;what&gt;." plus the standard next step.</summary>
        public static void Failed(IWin32Window owner, string what, Exception ex, string action = null)
        {
            Error(owner, "House Style couldn't " + what + ".",
                  "Try again. If it keeps happening, choose Copy details and send them to your style guide owner or support.", ex, action);
        }

        /// <summary>A read-only text (Diagnostics) with a Copy button.</summary>
        public static void Report(IWin32Window owner, string action, string text)
        {
            using (var dlg = new ReportDialog(Caption(action), text)) dlg.ShowDialog(owner);
        }

        public static void CopyToClipboard(string text)
        {
            try { Clipboard.SetText(text); } catch (Exception ex) { Diagnostics.Log("clipboard", ex); }
        }
    }

    /// <summary>The Error dialog: icon, message that wraps, optional Copy details, OK.</summary>
    internal sealed class MessageDialog : Form
    {
        public MessageDialog(string caption, string message, Icon icon, string copyText)
        {
            Text = caption;
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;

            var pic = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(14, 16) };
            var label = new Label { Text = message, AutoSize = true, MaximumSize = new Size(380, 0), Location = new Point(14 + 32 + 14, 16) };
            Controls.Add(pic); Controls.Add(label);
            var bodyBottom = Math.Max(pic.Bottom, label.PreferredHeight + 16);
            var width = Math.Max(340, 14 + 32 + 14 + Math.Min(380, label.PreferredWidth) + 18);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
            Controls.Add(ok);
            ClientSize = new Size(width, bodyBottom + 20 + 26 + 12);
            ok.Location = new Point(ClientSize.Width - 12 - ok.PreferredSize.Width - 4, bodyBottom + 20);
            if (copyText != null)
            {
                var copy = new Button { Text = "Copy details", AutoSize = true, MinimumSize = new Size(100, 26) };
                copy.Click += (s, e) => { Notify.CopyToClipboard(copyText); copy.Text = "Copied"; };
                Controls.Add(copy);
                copy.Location = new Point(ok.Left - 8 - copy.PreferredSize.Width, ok.Top);
            }
            AcceptButton = ok; CancelButton = ok;
        }
    }

    /// <summary>Read-only text with Copy and Close (Diagnostics).</summary>
    internal sealed class ReportDialog : Form
    {
        public ReportDialog(string caption, string text)
        {
            Text = caption;
            Font = SystemFonts.MessageBoxFont;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            MinimizeBox = false; MaximizeBox = true;
            ClientSize = new Size(620, 440);
            MinimumSize = new Size(380, 240);

            var box = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                Font = new Font(FontFamily.GenericMonospace, SystemFonts.MessageBoxFont.Size), Dock = DockStyle.Fill, BackColor = SystemColors.Window
            };
            box.Select(0, 0);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
            var close = new Button { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 26) };
            var copy = new Button { Text = "Copy", AutoSize = true, MinimumSize = new Size(80, 26) };
            copy.Click += (s, e) => { Notify.CopyToClipboard(text); copy.Text = "Copied"; };
            bar.Controls.Add(close); bar.Controls.Add(copy);
            Controls.Add(box); Controls.Add(bar);
            AcceptButton = close; CancelButton = close;
        }
    }
}
