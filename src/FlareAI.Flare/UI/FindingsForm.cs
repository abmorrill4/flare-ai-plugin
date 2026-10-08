using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Xml;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Check-style results as a working list, not a report. Modeless, so the author keeps editing:
    /// selecting a row highlights the text in Flare; Fix / Fix all apply word-level tracked changes;
    /// Rewrite paragraph hands findings that need judgment to the model. Lint results show at once,
    /// the model's review is appended when it arrives.
    /// </summary>
    public sealed class FindingsForm : Form
    {
        private readonly ActionRunner _runner;
        private readonly IDocumentAdapter _doc;
        private readonly Func<ActionRunner> _newRunner;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly ListView _list;
        private readonly Button _goTo, _fix, _fixAll, _rewrite, _recheck, _rule;
        private readonly List<StyleFinding> _findings = new List<StyleFinding>();
        private BackgroundWorker _worker;
        private bool _reviewing;
        private string _lintNote;
        private IList<XmlNode> _blocks;
        private List<StyleFinding> _lint;

        public FindingsForm(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner)
        {
            _runner = runner; _doc = doc; _newRunner = newRunner;
            Text = "FlareAI — Check style: " + System.IO.Path.GetFileName(doc.SourcePath ?? "");
            StartPosition = FormStartPosition.Manual;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(720, 460);
            Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
            MinimumSize = new Size(560, 300);
            Font = SystemFonts.MessageBoxFont;
            ShowInTaskbar = false;

            _status = new Label { Left = 10, Top = 10, Width = 690, Height = 18, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _progress = new ProgressBar { Left = 10, Top = 30, Width = 690, Height = 6, Style = ProgressBarStyle.Marquee, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _list = new ListView
            {
                Left = 10, Top = 42, Width = 690, Height = 336, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = true, ShowItemToolTips = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _list.Columns.Add("Text", 230);
            _list.Columns.Add("Fix", 200);
            _list.Columns.Add("Why", 240);
            _list.Groups.Add("error", "Errors");
            _list.Groups.Add("warning", "Warnings");
            _list.Groups.Add("suggestion", "Suggestions");
            _list.SelectedIndexChanged += (s, e) => { UpdateButtons(); if (_list.SelectedItems.Count == 1) GoTo(Selected()[0], false); };
            _list.DoubleClick += (s, e) => { var sel = Selected(); if (sel.Count == 1 && sel[0].CanFix) Fix(sel); };
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Fix(Selected()); e.Handled = true; } };

            var y = 388;
            _goTo = Btn("Go to", 10, y, (s, e) => { var sel = Selected(); if (sel.Count > 0) GoTo(sel[0], true); });
            _fix = Btn("Fix", 96, y, (s, e) => Fix(Selected()));
            _fixAll = Btn("Fix all", 182, y, (s, e) => Fix(_findings));
            _fixAll.Width = 110;
            _rewrite = Btn("Rewrite paragraph…", 298, y, (s, e) => RewriteParagraph());
            _rewrite.Width = 140;
            _rule = Btn("Guide ↗", 444, y, (s, e) => { var sel = Selected(); if (sel.Count == 1 && !string.IsNullOrEmpty(sel[0].Link)) OpenLink(sel[0].Link); });
            _recheck = Btn("Re-check", 530, y, (s, e) => Start());
            var close = Btn("Close", 616, y, (s, e) => Close());
            _recheck.Anchor = close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            var tips = new ToolTip();
            tips.SetToolTip(_fix, "Apply the selected fixes as tracked changes (Enter, or double-click a row).");
            tips.SetToolTip(_fixAll, "Apply every fix that has an exact replacement, as one undoable tracked change.");
            tips.SetToolTip(_rule, "Open the style-guide page for the selected rule.");
            tips.SetToolTip(_rewrite, "Ask the model to rewrite the paragraph to resolve the selected findings; you review it before it is applied.");

            Controls.AddRange(new Control[] { _status, _progress, _list });
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            FormClosing += (s, e) => { if (_worker != null && _worker.IsBusy) _worker.CancelAsync(); };
            Shown += (s, e) => Start();
        }

        private Button Btn(string text, int left, int top, EventHandler click)
        {
            var b = new Button { Text = text, Left = left, Top = top, Width = 80, Height = 26, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            b.Click += click;
            Controls.Add(b);
            return b;
        }

        // ---- check -------------------------------------------------------------------------------

        private void Start()
        {
            if (_worker != null && _worker.IsBusy) return;
            _findings.Clear(); _list.Items.Clear(); _lintNote = null;
            _reviewing = true; _progress.Visible = true; _status.Text = "Linting…";
            UpdateButtons();

            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            _worker.DoWork += (s, e) =>
            {
                var blocks = _runner.PickCheckBlocks(_doc);
                List<StyleFinding> lint;
                try { lint = _runner.Lint(blocks); }
                catch (Exception ex) { Diagnostics.Log("docstyle", ex); lint = new List<StyleFinding>(); _lintNote = "Style linter unavailable (" + ex.Message + ") — showing AI review only."; }
                var prompt = _runner.ReviewPrompt(blocks, lint); // last DOM read on this thread
                _blocks = blocks; _lint = lint;
                _worker.ReportProgress(0, lint);                  // from here the author may edit while we wait
                if (_worker.CancellationPending) { e.Cancel = true; return; }
                e.Result = _runner.CallReview(prompt);
            };
            _worker.ProgressChanged += (s, e) =>
            {
                if (IsDisposed) return;
                Add((List<StyleFinding>)e.UserState);
                _status.Text = Summary() + " Reviewing with AI… (you can fix these now)";
                UpdateButtons();
            };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Cancelled) return;
                _reviewing = false; _progress.Visible = false;
                if (e.Error != null)
                {
                    Diagnostics.Log("Check style", e.Error);
                    _status.Text = Summary() + " AI review failed: " + e.Error.Message;
                }
                else
                {
                    Add(_runner.AnchorReview((List<StyleFinding>)e.Result, _blocks, _lint)); // DOM work on the UI thread
                    _status.Text = _findings.Count == 0 ? "No issues found." : Summary();
                }
                if (_lintNote != null) _status.Text += "  " + _lintNote;
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
        }

        private void Add(List<StyleFinding> found)
        {
            _list.BeginUpdate();
            foreach (var f in found)
            {
                _findings.Add(f);
                var item = new ListViewItem(new[]
                {
                    "\"" + f.Excerpt + "\"",
                    f.Replacement == null ? "(needs rewrite)" : (f.Replacement.Length == 0 ? "delete" : "→ " + f.Replacement),
                    "[" + LayerLabel(f) + "] " + f.Suggestion
                })
                { Tag = f, Group = _list.Groups[f.Severity] ?? _list.Groups["suggestion"] };
                item.ToolTipText = LayerLabel(f) + " · " + f.Rule + (string.IsNullOrEmpty(f.Link) ? "" : "\n" + f.Link) + "\n\"" + f.Excerpt + "\"";
                Restyle(item);
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }

        private static string LayerLabel(StyleFinding f)
        {
            return f.Layer == "company" ? "Company" : f.Layer == "google" ? "Google" : "AI";
        }

        private string Summary()
        {
            int fixable = 0, open = 0;
            foreach (var f in _findings) { if (!f.IsFixed) open++; if (f.CanFix) fixable++; }
            return open + " open issue(s), " + fixable + " with a one-click fix.";
        }

        // ---- actions -----------------------------------------------------------------------------

        private void GoTo(StyleFinding f, bool report)
        {
            if (f.IsFixed) return;
            var ok = false;
            try { ok = _runner.GoTo(_doc, f); } catch (Exception ex) { Diagnostics.Log("GoTo", ex); }
            if (!ok && report) _status.Text = "That text is no longer in the topic (it was edited). Re-check to refresh.";
        }

        private void Fix(List<StyleFinding> findings)
        {
            var todo = findings.FindAll(f => f.CanFix);
            if (todo.Count == 0) { _status.Text = "Nothing selected has a one-click fix. Try Rewrite paragraph."; return; }
            try
            {
                var n = _runner.ApplyFixes(_doc, todo, todo.Count == 1 ? "FlareAI: " + todo[0].Rule : "FlareAI: " + todo.Count + " style fixes");
                foreach (ListViewItem item in _list.Items) Restyle(item);
                _status.Text = Summary() + "  Applied " + n + " fix(es) as tracked changes" + (n < todo.Count ? "; " + (todo.Count - n) + " skipped because the text changed." : ".") + " Ctrl+Z undoes them in one step.";
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Fix", ex);
                _status.Text = "Could not apply: " + ex.Message;
            }
            UpdateButtons();
        }

        private void RewriteParagraph()
        {
            var sel = Selected().FindAll(f => !f.IsFixed);
            if (sel.Count == 0) return;
            var block = _runner.LocateBlock(_doc, sel[0]);
            if (block == null) { _status.Text = "That text is no longer in the topic. Re-check to refresh."; return; }
            var related = _findings.FindAll(f => !f.IsFixed && _runner.LocateBlock(_doc, f) == block);
            var instruction = "Resolve these review findings and change nothing else:\n" + ActionRunner.Summarize(related);
            string applied;
            using (var form = new ResultForm("Rewrite paragraph", _newRunner(), _doc, ActionKind.Ask, instruction, new List<XmlNode> { block }))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                applied = form.AppliedSummary;
            }
            foreach (var f in related) f.IsFixed = true;
            foreach (ListViewItem item in _list.Items) Restyle(item);
            _status.Text = Summary() + "  " + applied + " Re-check to see what is left.";
            UpdateButtons();
        }

        private void OpenLink(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return; // only ever open web guide pages
            try { System.Diagnostics.Process.Start(url); } catch (Exception ex) { _status.Text = "Could not open " + url + ": " + ex.Message; }
        }

        // ---- view helpers ------------------------------------------------------------------------

        private List<StyleFinding> Selected()
        {
            var list = new List<StyleFinding>();
            foreach (ListViewItem item in _list.SelectedItems) list.Add((StyleFinding)item.Tag);
            return list;
        }

        private static void Restyle(ListViewItem item)
        {
            var f = (StyleFinding)item.Tag;
            item.ForeColor = f.IsFixed ? SystemColors.GrayText : (f.Severity == "error" ? Color.Firebrick : SystemColors.WindowText);
            item.Font = f.IsFixed ? new Font(SystemFonts.MessageBoxFont, FontStyle.Strikeout) : SystemFonts.MessageBoxFont;
        }

        private void UpdateButtons()
        {
            var sel = Selected();
            _goTo.Enabled = sel.Count == 1 && !sel[0].IsFixed;
            _fix.Enabled = sel.Exists(f => f.CanFix);
            var fixable = _findings.FindAll(f => f.CanFix).Count;
            _fixAll.Text = fixable > 0 ? "Fix all (" + fixable + ")" : "Fix all";
            _fixAll.Enabled = fixable > 0;
            _rewrite.Enabled = sel.Exists(f => !f.IsFixed);
            _rule.Enabled = sel.Count == 1 && !string.IsNullOrEmpty(sel[0].Link);
            _recheck.Enabled = !_reviewing;
        }
    }
}
