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
        private readonly Button _goTo, _fix, _fixAll, _rewrite, _recheck, _rule, _dismiss, _ignore;
        private static readonly Dismissals Dismissed = Dismissals.Load(System.IO.Path.Combine(FlareAI.Core.Settings.PluginSettings.SettingsDirectory, "dismissed.json"));
        private HashSet<string> _ignoredAi;
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

        private readonly bool _wholeTopic;
        private IList<string> _files; // project mode: lint these files (no AI), group by file

        /// <summary>A project-wide check: every topic and snippet, linter only, grouped by file.</summary>
        public static FindingsPanel ForProject(ActionRunner runner, IDocumentAdapter activeDoc, Func<ActionRunner> newRunner,
                                               Func<string, IDocumentAdapter> open, IList<string> files, string projectName)
        {
            var panel = new FindingsPanel(runner, activeDoc, newRunner, open, null, false) { _files = files };
            panel.Title = "FlareAI — project " + projectName + " (" + files.Count + " files)";
            return panel;
        }

        public bool IsProjectReport { get { return _files != null; } }

        /// <summary>Path of the topic this panel checks.</summary>
        public string TopicPath { get { return _doc.SourcePath; } }

        private readonly Func<string, IDocumentAdapter> _open;
        private readonly ReviewCache _cache;
        private bool _aiOnStart;

        /// <param name="open">Opens another file (a snippet) in Flare and returns its adapter, or null.</param>
        /// <param name="cache">Shared AI review cache (same content = same review, across topics and renames).</param>
        /// <param name="aiOnStart">False when the panel opens because the author switched topics: only a cached AI
        /// review is shown automatically; a new one runs on Re-check (no surprise cost).</param>
        public FindingsPanel(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner, Func<string, IDocumentAdapter> open,
                             ReviewCache cache, bool aiOnStart)
        {
            _runner = runner; _doc = doc; _newRunner = newRunner; _open = open; _cache = cache; _aiOnStart = aiOnStart;
            // Keep the first check's scope: Go to selects text in the editor, which must not shrink a Re-check.
            _wholeTopic = string.IsNullOrEmpty(doc.SelectionText);
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
            _list.DoubleClick += (s, e) => { var sel = Selected(); if (sel.Count != 1) return; if (sel[0].CanFix) Fix(sel); else GoTo(sel[0], true); };
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Fix(Selected()); e.Handled = true; } };

            // Buttons wrap onto more rows when the pane is narrow.
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            _fixAll = Btn(bar, "Fix all", (s, e) => Fix(_findings));
            _fix = Btn(bar, "Fix", (s, e) => Fix(Selected()));
            _rewrite = Btn(bar, "Rewrite paragraph…", (s, e) => RewriteParagraph());
            _goTo = Btn(bar, "Go to", (s, e) => { var sel = Selected(); if (sel.Count > 0) GoTo(sel[0], true); });
            _rule = Btn(bar, "Guide ↗", (s, e) => { var sel = Selected(); if (sel.Count == 1 && !string.IsNullOrEmpty(sel[0].Link)) OpenLink(sel[0].Link); });
            _dismiss = Btn(bar, "Dismiss", (s, e) => Dismiss());
            _ignore = Btn(bar, "Ignore rule…", (s, e) => IgnoreRule());
            _recheck = Btn(bar, "Re-check", (s, e) => { _aiOnStart = true; Start(); });
            Btn(bar, "Close", (s, e) => { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });
            var tips = new ToolTip();
            tips.SetToolTip(_fix, "Apply the selected fixes as tracked changes (Enter, or double-click a row).");
            tips.SetToolTip(_fixAll, "Apply every fix that has an exact replacement, as one undoable tracked change.");
            tips.SetToolTip(_rule, "Open the style-guide page for the selected rule.");
            tips.SetToolTip(_dismiss, "Not an issue here: hide the selected findings and keep them hidden on later checks (just for you).");
            tips.SetToolTip(_ignore, "Turn the selected rule off for this whole project (written to the project's StyleGuide folder, shared with your team).");
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
            if (_files != null) { StartProject(); return; }
            _worker.DoWork += (s, e) =>
            {
                var blocks = _wholeTopic ? _runner.PickAllBlocks(_doc) : _runner.PickCheckBlocks(_doc);
                List<StyleFinding> lint;
                try { lint = _runner.Lint(blocks); }
                catch (Exception ex) { Diagnostics.Log("docstyle", ex); lint = new List<StyleFinding>(); _lintNote = "Style linter unavailable (" + ex.Message + ") — showing AI review only."; }
                try { lint.AddRange(_runner.LintSnippets(blocks, _doc.SourcePath)); }
                catch (Exception ex) { Diagnostics.Log("snippets", ex); }
                var prompt = _runner.ReviewPrompt(blocks, lint); // last DOM read on this thread
                _blocks = blocks; _lint = lint;
                _worker.ReportProgress(0, lint);                  // from here the author may edit while we wait
                if (_worker.CancellationPending) { e.Cancel = true; return; }
                var cached = _cache == null ? null : _cache.Get(prompt);
                if (cached != null) { e.Result = cached; return; }
                if (!_aiOnStart) { e.Result = null; return; }    // switched topics: don't spend on a review unasked
                var raw = _runner.CallReview(prompt);
                if (_cache != null) _cache.Put(prompt, raw);
                e.Result = raw;
            };
            _worker.ProgressChanged += (s, e) =>
            {
                if (IsDisposed) return;
                Add((List<StyleFinding>)e.UserState);
                _status.Text = Summary() + (_aiOnStart ? " Reviewing with AI… (you can fix these now)" : "");
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
                else if (e.Result == null)
                {
                    _status.Text = Summary() + "  Linter only — click Re-check for the AI review.";
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

        private void StartProject()
        {
            _status.Text = "Linting " + _files.Count + " files…";
            _worker.DoWork += (s, e) => e.Result = _runner.LintFiles(_files, (done, total) => _worker.ReportProgress(done * 100 / Math.Max(1, total), done + "/" + total));
            _worker.ProgressChanged += (s, e) => { if (!IsDisposed) _status.Text = "Linting " + e.UserState + " files…"; };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Cancelled) return;
                _reviewing = false; _progress.Visible = false;
                if (e.Error != null) { Diagnostics.Log("Check project", e.Error); _status.Text = "Project check failed: " + e.Error.Message; }
                else
                {
                    var found = (List<StyleFinding>)e.Result;
                    Add(found);
                    var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in found) files.Add(f.DocumentPath);
                    _status.Text = found.Count + " issue(s) in " + files.Count + " of " + _files.Count + " files (linter only). Go to opens the topic.";
                }
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
        }

        private void Add(List<StyleFinding> found)
        {
            if (_ignoredAi == null) _ignoredAi = ProjectRules.IgnoredAiRules(_doc.SourcePath);
            _list.BeginUpdate();
            foreach (var f in found)
            {
                if (Dismissed.IsDismissed(f, _doc.SourcePath)) continue;
                if (f.Layer == "ai" && _ignoredAi.Contains(f.Rule ?? "")) continue;
                _findings.Add(f);
                var item = new ListViewItem(new[]
                {
                    (f.DocumentPath == null || _files != null ? "" : "[" + System.IO.Path.GetFileNameWithoutExtension(f.DocumentPath) + "] ") + "\"" + f.Excerpt + "\"",
                    f.Variable != null ? "Go to, then Insert ▸ Variable ▸ " + f.Variable : f.Replacement == null ? "(needs rewrite)" : (f.Replacement.Length == 0 ? "delete" : "→ " + f.Replacement),
                    "[" + LayerLabel(f) + "] " + f.Suggestion
                })
                { Tag = f, Group = _files != null ? FileGroup(f.DocumentPath) : (_list.Groups[f.Severity] ?? _list.Groups["suggestion"]) };
                item.ToolTipText = LayerLabel(f) + " · " + f.Rule + (string.IsNullOrEmpty(f.Link) ? "" : "\n" + f.Link) + "\n\"" + f.Excerpt + "\"";
                Restyle(item);
                _list.Items.Add(item);
            }
            _list.EndUpdate();
        }

        private void Dismiss()
        {
            var sel = Selected();
            if (sel.Count == 0) return;
            foreach (var f in sel) Dismissed.Dismiss(f, _doc.SourcePath);
            RemoveRows(f => sel.Contains(f));
            _status.Text = Summary() + "  Dismissed " + sel.Count + " finding(s); they stay hidden for this text.";
        }

        private void IgnoreRule()
        {
            var sel = Selected();
            if (sel.Count != 1) { _status.Text = "Select one finding whose rule you want to turn off."; return; }
            var f = sel[0];
            var file = ProjectRules.OverridesPath(f.DocumentPath ?? _doc.SourcePath);
            if (file == null) { _status.Text = "This topic is not inside a Flare project."; return; }
            if (MessageBox.Show(this, "Turn off \"" + f.Rule + "\" for this whole project?\n\nThis writes to\n" + file +
                    "\nwhich is shared with your team through source control. Delete the line to turn the rule back on.",
                    "FlareAI — Ignore rule", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                ProjectRules.Ignore(f.DocumentPath ?? _doc.SourcePath, f);
                if (f.Layer == "ai") _ignoredAi.Add(f.Rule);
                RemoveRows(x => x.Rule == f.Rule);
                _status.Text = Summary() + "  \"" + f.Rule + "\" is off for this project.";
            }
            catch (Exception ex) { Diagnostics.Log("Ignore rule", ex); _status.Text = "Could not write " + file + ": " + ex.Message; }
        }

        private void RemoveRows(Predicate<StyleFinding> match)
        {
            _list.BeginUpdate();
            for (var i = _list.Items.Count - 1; i >= 0; i--)
                if (match((StyleFinding)_list.Items[i].Tag)) _list.Items.RemoveAt(i);
            _list.EndUpdate();
            _findings.RemoveAll(match);
            UpdateButtons();
        }

        private static List<List<StyleFinding>> GroupByDocument(List<StyleFinding> findings)
        {
            var groups = new List<List<StyleFinding>>();
            foreach (var f in findings)
            {
                var g = groups.Find(x => x[0].DocumentPath == f.DocumentPath);
                if (g == null) groups.Add(g = new List<StyleFinding>());
                g.Add(f);
            }
            groups.Sort((a, b) => (a[0].DocumentPath == null ? 0 : 1).CompareTo(b[0].DocumentPath == null ? 0 : 1));
            return groups;
        }

        private static string LayerLabel(StyleFinding f)
        {
            if (string.IsNullOrEmpty(f.Layer) || f.Layer == "ai") return "AI";
            return char.ToUpperInvariant(f.Layer[0]) + f.Layer.Substring(1); // Google, Company, Project, or a custom layer's folder name
        }

        private ListViewGroup FileGroup(string path)
        {
            var key = path ?? "";
            var g = _list.Groups[key];
            if (g == null)
            {
                var root = _files.Count > 0 ? CommonRoot() : "";
                g = _list.Groups.Add(key, root.Length > 0 && key.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? key.Substring(root.Length) : System.IO.Path.GetFileName(key));
            }
            return g;
        }

        private string CommonRoot()
        {
            var content = System.IO.Path.GetDirectoryName(_files[0]);
            while (content != null && !System.IO.Path.GetFileName(content).Equals("Content", StringComparison.OrdinalIgnoreCase))
                content = System.IO.Path.GetDirectoryName(content);
            return content == null ? "" : content + System.IO.Path.DirectorySeparatorChar;
        }

        private string Summary()
        {
            int fixable = 0, open = 0;
            foreach (var f in _findings) { if (!f.IsFixed) open++; if (f.CanFix) fixable++; }
            return open + " open issue(s), " + fixable + " with a one-click fix.";
        }

        // ---- actions -----------------------------------------------------------------------------

        /// <summary>The document a finding lives in: the topic, or its snippet (opened in Flare on demand).</summary>
        private IDocumentAdapter DocOf(StyleFinding f, bool open)
        {
            // Edits and selections go through the editor's selection, so the target must be the active document:
            // re-activate the topic too (after a snippet fix the snippet's tab is in front).
            var path = f.DocumentPath ?? _doc.SourcePath;
            if (open && _open != null && path != null)
            {
                var opened = _open(path);
                if (opened != null && f.DocumentPath == null) return _doc; // keep the adapter whose DOM the findings came from
                if (opened != null) return opened;
            }
            return f.DocumentPath == null ? _doc : null;
        }

        private void GoTo(StyleFinding f, bool report)
        {
            if (f.IsFixed) return;
            // Selecting a row in a snippet doesn't switch documents on its own; Go to (button/double-click) does.
            var doc = DocOf(f, report);
            if (doc == null) { if (report) _status.Text = "Could not open " + f.DocumentPath; return; }
            var ok = false;
            try { ok = _runner.GoTo(doc, f); } catch (Exception ex) { Diagnostics.Log("GoTo", ex); }
            if (!ok && report) _status.Text = "That text is no longer in the topic (it was edited). Re-check to refresh.";
        }

        private void Fix(List<StyleFinding> findings)
        {
            var todo = findings.FindAll(f => f.CanFix);
            if (todo.Count == 0) { _status.Text = "Nothing selected has a one-click fix. Try Rewrite paragraph."; return; }
            var fileCount = GroupByDocument(todo).Count;
            if (fileCount > 3 && MessageBox.Show(this, "Apply " + todo.Count + " fixes in " + fileCount + " files?\n\nEach file opens in Flare and gets the fixes as tracked changes (one undo step per file). Nothing is saved automatically.",
                    "FlareAI", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                var n = 0;
                // One operation per document: the topic, then each snippet (opened in Flare so the fix is tracked there).
                foreach (var group in GroupByDocument(todo))
                {
                    var doc = DocOf(group[0], true);
                    if (doc == null) { Diagnostics.Log("Fix: could not open " + group[0].DocumentPath); continue; }
                    n += _runner.ApplyFixes(doc, group, group.Count == 1 ? "FlareAI: " + group[0].Rule : "FlareAI: " + group.Count + " style fixes");
                }
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
            var sel = Selected().FindAll(f => !f.IsFixed && f.DocumentPath == null);
            if (sel.Count == 0) { _status.Text = "Rewrite paragraph works on the topic; for a snippet, open it and run Rewrite there."; return; }
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
            _dismiss.Enabled = sel.Count > 0;
            _ignore.Enabled = sel.Count == 1;
        }
    }
}
