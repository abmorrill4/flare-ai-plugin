using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Feedback;
using FlareAI.Core.Settings;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// The shared frame of the three feedback dialogs (issue #124): a column of rows measured with the real font at the
    /// dialog's DPI (nothing cut off at 100%, 125% or 150%), a button bar, and the send step: the report goes in the
    /// background, and when the service can't take it the dialog says so and offers Save Report (a zip in
    /// Documents\House Style Reports, plus an email draft when an address is set in settings).
    /// </summary>
    public abstract class FeedbackForm : Form
    {
        private readonly float _scale;
        private readonly List<Control> _rows = new List<Control>();
        private readonly Dictionary<Control, int> _lines = new Dictionary<Control, int>();
        private readonly Dictionary<Control, int> _gapAfter = new Dictionary<Control, int>();
        private BackgroundWorker _worker;
        private string _sentJson;

        /// <summary>The button bar (right to left: the last added button is leftmost).</summary>
        protected readonly FlowLayoutPanel Bar;
        /// <summary>Under the rows: "Sending…", or why the report wasn't sent.</summary>
        protected readonly Label Status;
        /// <summary>
        /// Shown when sending failed and the report is saved: "Open GitHub Issues", or "Email Report" when IT set an address
        /// in settings (ReportEmail).
        /// </summary>
        protected readonly Button SendElsewhere;
        /// <summary>Shown with <see cref="SendElsewhere"/>: opens Documents\House Style Reports with the zip selected.</summary>
        protected readonly Button OpenFolder;

        /// <summary>Opens a URL, a mailto: link or "explorer /select" for a saved report. Tests replace it so nothing opens.</summary>
        public static Action<string> Launch = DefaultLaunch;

        /// <summary>Where a report that couldn't be sent is saved; null is Documents\House Style Reports. Tests use a temp folder.</summary>
        public static string SaveFolder;

        /// <summary>The zip the report was saved in when it couldn't be sent (null until then).</summary>
        public string SavedPath { get; private set; }

        /// <summary>The service's answer once sent.</summary>
        public FeedbackResult Result { get; private set; }

        /// <param name="scale">The DPI scale to lay out at; 0 uses this window's DPI. Tests pass 1.25 to check 125%.</param>
        protected FeedbackForm(string caption, float scale)
        {
            Text = caption;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;   // laid out below at the real DPI and font
            var dpi = DeviceDpi / 96f;
            _scale = scale > 0 ? scale : dpi;
            var font = SystemFonts.MessageBoxFont;
            // A test asking for another scale than this screen's gets the font size Windows would use at that scale.
            Font = Math.Abs(_scale - dpi) < 0.01f ? font : new Font(font.FontFamily, font.SizeInPoints * _scale / dpi, font.Style);

            Status = new LiveLabel { AutoSize = false, Visible = false, ForeColor = SystemColors.ControlText };
            SendElsewhere = new Button { Text = ReportZip.OpenIssuesLabel, AutoSize = true, Visible = false };
            SendElsewhere.Click += (s, e) => Safe.Run(SendElsewhere.Text, () => { var mail = Mailto; Launch(mail ?? ReportZip.IssuesUrl); });
            OpenFolder = new Button { Text = ReportZip.OpenFolderLabel, AutoSize = true, Visible = false };
            OpenFolder.Click += (s, e) => Safe.Run(ReportZip.OpenFolderLabel, () => { if (SavedPath != null) Launch(ReportZip.SelectInFolder(SavedPath)); });
            A11y.Name(OpenFolder, ReportZip.OpenFolderLabel, "Opens Documents\\House Style Reports with the report selected.");
            Bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, WrapContents = true, AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
            Controls.Add(Bar);
        }

        /// <summary>Pixels at this dialog's DPI for a length designed at 96 DPI.</summary>
        protected int S(int px) { return (int)Math.Round(px * _scale); }

        /// <summary>The width every row takes.</summary>
        protected int ContentWidth { get { return S(420); } }

        private int Margin_ { get { return S(14); } }

        // ---- rows -------------------------------------------------------------------------------------------------

        /// <summary>Adds a row; rows are stacked top to bottom at the full content width.</summary>
        protected T Row<T>(T c, int gapAfter = 8) where T : Control
        {
            Controls.Add(c);
            _rows.Add(c);
            _gapAfter[c] = gapAfter;
            return c;
        }

        /// <summary>A line of text that wraps.</summary>
        protected Label TextRow(string text, bool bold = false, int gapAfter = 8)
        {
            var l = Row(new Label { Text = text, AutoSize = false, UseMnemonic = false }, gapAfter);
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            return l;
        }

        /// <summary>A label and a multi-line box under it.</summary>
        protected TextBox Box(string label, int lines, string accessibleName)
        {
            var l = Row(new Label { Text = label, AutoSize = false }, 2);
            var b = Row(new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, WordWrap = true });
            _lines[b] = lines;
            A11y.Name(b, accessibleName);
            l.TabIndex = b.TabIndex;
            return b;
        }

        /// <summary>A check box whose text wraps.</summary>
        protected CheckBox Check(string text, bool isChecked, int gapAfter = 8)
        {
            return Row(new CheckBox { Text = text, Checked = isChecked, AutoSize = false, TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft }, gapAfter);
        }

        /// <summary>Option buttons in one group.</summary>
        protected RadioButton[] Choices(string groupName, string[] texts)
        {
            var panel = Row(new Panel { AccessibleName = groupName, AccessibleRole = AccessibleRole.Grouping });
            var radios = new RadioButton[texts.Length];
            for (var i = 0; i < texts.Length; i++)
            {
                radios[i] = new RadioButton { Text = texts[i], AutoSize = false, TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft, TabIndex = i };
                panel.Controls.Add(radios[i]);
            }
            return radios;
        }

        /// <summary>A read-only box in a fixed-width font: exactly what is sent.</summary>
        protected TextBox Preview(int lines)
        {
            var b = Row(new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = SystemColors.Window,
                Font = new Font(FontFamily.GenericMonospace, Font.SizeInPoints * 0.95f)
            });
            _lines[b] = lines;
            return b;
        }

        /// <summary>A button in the bar. Add them right to left: the default button first.</summary>
        protected Button AddButton(string text, EventHandler click, string description = null)
        {
            var b = new Button { Text = text, AutoSize = true, Margin = new Padding(S(8), 0, 0, 0) };
            if (click != null) b.Click += (s, e) => Safe.Run(text, () => click(s, e));
            if (description != null) A11y.Name(b, text.TrimEnd('…'), description);
            Bar.Controls.Add(b);
            return b;
        }

        /// <summary>Adds the status line and the buttons for a report that couldn't be sent; call after the rows.</summary>
        protected void FinishRows()
        {
            Row(Status, 6);
            ShowRow(Status, false);
            foreach (var b in new[] { OpenFolder, SendElsewhere })
            {
                Bar.Controls.Add(b);
                b.Margin = new Padding(S(8), 0, 0, 0);
            }
        }

        /// <summary>Puts the dialog's own buttons first in tab order, then Open GitHub Issues and Open Folder.</summary>
        protected void TabOrder(params Control[] first)
        {
            var i = 0;
            foreach (var c in first) c.TabIndex = i++;
            SendElsewhere.TabIndex = i++;
            OpenFolder.TabIndex = i;
        }

        /// <summary>The email draft for a saved report, when IT set an address in settings; null otherwise.</summary>
        private string Mailto
        {
            get { return SavedPath == null ? null : ReportZip.MailtoLink(PluginSettings.Load().ReportEmail, SavedPath); }
        }

        /// <summary>Places the visible rows top to bottom, then the button bar, and sizes the dialog to fit.</summary>
        protected void Relayout()
        {
            var x = Margin_;
            var y = Margin_;
            var w = ContentWidth;
            for (var i = 0; i < _rows.Count; i++) _rows[i].TabIndex = i;   // reading order is row order, hidden rows included
            Bar.TabIndex = _rows.Count;
            foreach (var c in _rows)
            {
                if (!IsShown(c)) continue;
                c.Left = x; c.Top = y; c.Width = w;
                c.Height = HeightOf(c, w);
                y += c.Height + S(_gapAfter[c]);
            }
            foreach (Control b in Bar.Controls) b.MinimumSize = new Size(S(80), Math.Max(S(26), Font.Height + S(10)));
            Bar.Width = w;
            Bar.MaximumSize = new Size(w, 0);
            var barH = Bar.GetPreferredSize(new Size(w, 0)).Height;
            Bar.Height = barH;
            Bar.Left = x; Bar.Top = y + S(6);
            ClientSize = new Size(w + 2 * x, Bar.Bottom + Margin_);
        }

        private readonly HashSet<Control> _hidden = new HashSet<Control>();

        /// <summary>Shows or hides a row; call <see cref="Relayout"/> after.</summary>
        protected void ShowRow(Control c, bool show)
        {
            if (show) _hidden.Remove(c); else _hidden.Add(c);
            c.Visible = show;
        }

        private bool IsShown(Control c) { return !_hidden.Contains(c); }

        private int HeightOf(Control c, int w)
        {
            const TextFormatFlags wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            int lines;
            if (_lines.TryGetValue(c, out lines)) return c.Font.Height * lines + S(8);
            if (c is Label)
            {
                var text = string.IsNullOrEmpty(c.Text) ? " " : c.Text;
                return TextRenderer.MeasureText(text, c.Font, new Size(w, 0), wrap).Height + S(2);
            }
            if (c is CheckBox || c is RadioButton)
                return Math.Max(S(22), TextRenderer.MeasureText(c.Text, c.Font, new Size(w - S(24), 0), wrap).Height + S(6));
            if (c is Panel && c.Controls.Count > 0 && c.Controls[0] is RadioButton)
            {
                var y = 0;
                foreach (Control r in c.Controls)
                {
                    r.Left = 0; r.Top = y; r.Width = w;
                    r.Height = HeightOf(r, w);
                    y += r.Height + S(2);
                }
                return y;
            }
            if (c is Button) { c.Width = c.PreferredSize.Width; return Math.Max(S(26), c.PreferredSize.Height); }
            return c.Height;
        }

        // ---- sending ----------------------------------------------------------------------------------------------

        /// <summary>The JSON that is sent, exactly (also what Details and Preview show).</summary>
        protected abstract string Json();

        /// <summary>The service path: "/v1/report" or "/v1/false-positive".</summary>
        protected abstract string ServicePath { get; }

        /// <summary>Sends <see cref="Json"/> in the background; the dialog stays open, its inputs locked, until the answer.</summary>
        protected void Send()
        {
            if (_worker != null && _worker.IsBusy) return;
            _sentJson = Json();
            SetBusy(true);
            Status.Text = "Sending…";
            ShowRow(Status, true); Relayout();
            var url = PluginSettings.Load().EffectiveFeedbackUrl;
            var json = _sentJson;
            var path = ServicePath;
            _worker = new BackgroundWorker();
            _worker.DoWork += (s, e) => e.Result = new FeedbackClient(url).Post(path, json);
            // Back on this dialog's thread even when the worker was started without a WinForms context.
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke((Action)(() => Done(e, path)));
                else Done(e, path);
            };
            _worker.RunWorkerAsync();
        }

        private void Done(RunWorkerCompletedEventArgs e, string path)
        {
            if (IsDisposed) return;
            var r = e.Error != null ? new FeedbackResult { Failure = FeedbackFailure.Network, Error = e.Error.Message } : (FeedbackResult)e.Result;
            Diagnostics.Log("Feedback " + path + ": " + r);
            SetBusy(false);
            if (r.Ok) { Result = r; OnSent(r); return; }
            SaveAsZip();
            Relayout();
            OnSendFailed(r);
            Result = r;
        }

        /// <summary>The report arrived. By default the dialog closes with OK.</summary>
        protected virtual void OnSent(FeedbackResult result)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>The report didn't arrive; it is saved, and the status says where to send it.</summary>
        protected virtual void OnSendFailed(FeedbackResult result) { }

        /// <summary>Locks the inputs while sending.</summary>
        protected virtual void SetBusy(bool busy)
        {
            UseWaitCursor = busy;
            foreach (Control c in _rows) if (c != Status) c.Enabled = !busy;
            foreach (Control b in Bar.Controls) b.Enabled = !busy;
        }

        /// <summary>
        /// Saves the report that couldn't be sent as a zip and says where to send it: a new GitHub issue, or the email
        /// address IT set in settings.
        /// </summary>
        private void SaveAsZip()
        {
            try
            {
                SavedPath = ReportZip.Save(_sentJson ?? Json(), SaveFolder);
                Diagnostics.Log("Feedback saved as a zip");
                var email = Mailto != null;
                Status.Text = email ? ReportZip.SavedForEmail : ReportZip.SavedForGitHub;
                SendElsewhere.Text = email ? ReportZip.EmailLabel : ReportZip.OpenIssuesLabel;
                A11y.Name(SendElsewhere, SendElsewhere.Text, email ? "Opens an email to your IT department. Attach the saved report." : "Opens a new issue on GitHub. Attach the saved report.");
                SendElsewhere.Visible = OpenFolder.Visible = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("save report", ex);
                Status.Text = ReportZip.NotSaved;
            }
        }

        private static void DefaultLaunch(string target)
        {
            try
            {
                if (target.StartsWith("/select,", StringComparison.Ordinal)) System.Diagnostics.Process.Start("explorer.exe", target);
                else System.Diagnostics.Process.Start(target);
            }
            catch (Exception ex) { Diagnostics.Log("open saved report", ex); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_worker != null && _worker.IsBusy && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }   // wait for the answer (10 s at most)
            base.OnFormClosing(e);
        }
    }
}
