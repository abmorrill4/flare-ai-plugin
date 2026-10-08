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
    /// Check-style results as a working list, not a report. Lives in a docked Flare pane (or a floating window
    /// as fallback), so the author keeps editing: selecting a row highlights the text in Flare; Fix / Fix all
    /// apply word-level tracked changes; Rewrite paragraph hands findings that need judgment to the model.
    /// Lint results show at once, the model's review is appended when it arrives. Layout adapts to a narrow sidebar.
    /// </summary>
    public sealed class FindingsPanel : UserControl
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
        private bool _reviewing, _started;
        private string _lintNote;
        private IList<XmlNode> _blocks;
        private List<StyleFinding> _lint;

        /// <summary>Raised when the author clicks Close; the host hides its pane or window.</summary>
        public event EventHandler CloseRequested;

        /// <summary>"Check style: topic.htm" — for the host's title.</summary>
        public string Title { get; private set; }

        public FindingsPanel(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner)
        {
            _runner = runner; _doc = doc; _newRunner = newRunner;
            Title = "FlareAI — " + System.IO.Path.GetFileName(doc.SourcePath ?? "");
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(6);
            MinimumSize = new Size(260, 220);

            _status = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 34, Text = "Linting…" };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee };
            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = true, ShowItemToolTips = true };
            _list.Columns.Add("Text");
            _list.Columns.Add("Fix");
            _list.Columns.Add("Why");
            _list.Groups.Add("error", "Errors");
            _list.Groups.Add("warning", "Warnings");
            _list.Groups.Add("suggestion", "Suggestions");
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => { UpdateButtons(); if (_list.SelectedItems.Count == 1) GoTo(Selected()[0], false); };
            _list.DoubleClick += (s, e) => { var sel = Selected(); if (sel.Count == 1 && sel[0].CanFix) Fix(sel); };
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Fix(Selected()); e.Handled = true; } };

            // Buttons wrap onto more rows when the pane is narrow.
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            _fixAll = Btn(bar, "Fix all", (s, e) => Fix(_findings));
            _fix = Btn(bar, "Fix", (s, e) => Fix(Selected()));
            _rewrite = Btn(bar, "Rewrite paragraph…", (s, e) => RewriteParagraph());
            _goTo = Btn(bar, "Go to", (s, e) => { var sel = Selected(); if (sel.Count > 0) GoTo(sel[0], true); });
            _rule = Btn(bar, "Guide ↗", (s, e) => { var sel = Selected(); if (sel.Count == 1 && !string.IsNullOrEmpty(sel[0].Link)) OpenLink(sel[0].Link); });
            _recheck = Btn(bar, "Re-check", (s, e) => Start());
            Btn(bar, "Close", (s, e) => { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });
            var tips = new ToolTip();
            tips.SetToolTip(_fix, "Apply the selected fixes as tracked changes (Enter, or double-click a row).");
            tips.SetToolTip(_fixAll, "Apply every fix that has an exact replacement, as one undoable tracked change.");
            tips.SetToolTip(_rule, "Open the style-guide page for the selected rule.");
            tips.SetToolTip(_rewrite, "Ask the model to rewrite the paragraph to resolve the selected findings; you review it before it is applied.");

            Controls.Add(_list);      // Fill must be added first so docked bars take their space
            Controls.Add(bar);
            Controls.Add(_progress);
            Controls.Add(_status);
            HandleDestroyed += (s, e) => { if (_worker != null && _worker.IsBusy) _worker.CancelAsync(); };
        }

        /// <summary>Starts the first check. Hosts call this once the panel is placed (it needs a window handle).</summary>
        public void Begin()
        {
            if (_started) return;
            _started = true;
            if (IsHandleCreated) BeginInvoke((Action)Start);
            else HandleCreated += (s, e) => BeginInvoke((Action)Start);
        }

        private static Button Btn(FlowLayoutPanel bar, string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Height = 26, Margin = new Padding(0, 0, 4, 4) };
            b.Click += click;
            bar.Controls.Add(b);
            return b;
        }

        private void FitColumns()
        {
            var w = Math.Max(150, _list.ClientSize.Width - 4);
            _list.Columns[0].Width = w * 34 / 100;
            _list.Columns[1].Width = w * 24 / 100;
            _list.Columns[2].Width = w - _list.Columns[0].Width - _list.Columns[1].Width;
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
                    f.Variable != null ? "Go to, then Insert ▸ Variable ▸ " + f.Variable : f.Replacement == null ? "(needs rewrite)" : (f.Replacement.Length == 0 ? "delete" : "→ " + f.Replacement),
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
            if (string.IsNullOrEmpty(f.Layer) || f.Layer == "ai") return "AI";
            return char.ToUpperInvariant(f.Layer[0]) + f.Layer.Substring(1); // Google, Company, Project, or a custom layer's folder name
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
