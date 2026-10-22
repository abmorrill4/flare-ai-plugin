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
    /// A review as discrete pieces of work. The list shows every suggestion in reading order, grouped by section;
    /// the card below shows the selected one — what changes (word diff), why (the cited rule, its text and
    /// example), and its impact — with Apply / Reject / Edit / Refine / Check this paragraph again. Applying writes
    /// a tracked change and moves to the next suggestion; the editor never switches away from the topic on its own.
    /// Style-rule results show at once; the AI review arrives section by section.
    /// </summary>
    public sealed class FindingsPanel : UserControl, IReviewHost
    {
        private readonly ActionRunner _runner;
        private readonly IDocumentAdapter _doc;
        private readonly Func<ActionRunner> _newRunner;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly ListView _list;
        private readonly Button _acceptAll, _bulk, _recheck, _ignore, _stop;
        private readonly Label _cost;
        private readonly NoticeLink _notice;   // the last toast, kept here so a keyboard user can still reach its action
        private readonly TextBox _result;
        private int _unitCount, _doneUnits;
        private bool _stopped;
        // card
        private readonly Label _cardHead;
        private readonly RichTextBox _cardDiff, _cardWhy;
        private readonly Button _accept, _goTo, _openFile, _reject, _edit, _refine, _reanalyze, _rule;
        private readonly ContextMenuStrip _rejectMenu;
        private readonly ToolTip _ignoreTip;
        // project check: by rule / by file, the rule card's actions, and batches with progress
        private readonly FlowLayoutPanel _viewBar, _actions, _ruleActions;
        private readonly RadioButton _byRule, _byFile;
        private readonly Button _backToRules, _showSkipped, _fixRule, _reviewRule, _turnOffRule, _skipRule, _batchStop;
        private BatchRun _batch;
        private string _lastRule;   // the rule row to come back to after Review one by one

        private static readonly string SettingsDir = FlareAI.Core.Settings.PluginSettings.SettingsDirectory;
        private static readonly Dismissals Dismissed = Dismissals.Load(System.IO.Path.Combine(SettingsDir, "dismissed.json"));
        private readonly ReviewSession _session;   // the decisions; this pane renders them and forwards clicks
        private BackgroundWorker _worker;
        private bool _reviewing, _started;
        private string _lintNote;
        private bool _aiOff;              // the writer chose no AI: no AI review, and cached AI suggestions stay hidden
        private volatile bool _aiHidden;  // a cached AI review was hidden this check
        private readonly LinkLabel _aiOffNote;

        /// <summary>Opens House Style Settings (the AI-off note's link).</summary>
        public Action OpenSettings { get; set; }
        private volatile string _requestNote;
        private List<StyleFinding> _lint;

        /// <summary>Raised when the author clicks Close; the host hides its pane or window.</summary>
        public event EventHandler CloseRequested;

        /// <summary>"House Style — topic.htm" — for the host's title.</summary>
        public string Title { get; private set; }

        private readonly bool _wholeTopic;
        private IList<string> _files; // project mode: lint these files (no AI), group by file

        /// <summary>A project-wide check: every topic and snippet, style rules only, grouped by file.</summary>
        public static FindingsPanel ForProject(ActionRunner runner, IDocumentAdapter activeDoc, Func<ActionRunner> newRunner,
                                               Func<string, IDocumentAdapter> open, IList<string> files, string projectName)
        {
            var panel = new FindingsPanel(runner, activeDoc, newRunner, open, null, false, files);
            panel.Title = "House Style — project " + projectName + " (" + files.Count + " files)";
            return panel;
        }

        public bool IsProjectReport { get { return _files != null; } }

        /// <summary>
        /// Finds a file open in the editor without opening it (the host's): lets the pane notice a fix undone in another
        /// file (Ctrl+Z there), on row selection and when that document changes.
        /// </summary>
        public Func<string, IDocumentAdapter> FindOpen { set { _session.OpenedDocument = value; } }

        /// <summary>A document changed in the editor: applied fixes that were undone there are live again.</summary>
        public void RecheckApplied()
        {
            if (_reviewing) return;
            _session.RecheckApplied();
        }

        /// <summary>Path of the topic this panel checks.</summary>
        public string TopicPath { get { return _doc.SourcePath; } }

        private readonly Func<string, IDocumentAdapter> _open;
        private readonly ReviewCache _cache;
        private bool _aiOnStart;

        /// <param name="open">Opens another file (a snippet) in Flare and returns its adapter, or null.</param>
        /// <param name="cache">Shared AI review cache (same content = same review, across topics, renames and sessions).</param>
        /// <param name="aiOnStart">False when the panel opens because the author switched topics: only a cached AI
        /// review is shown automatically; a new one runs on Check again (no surprise cost).</param>
        public FindingsPanel(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner, Func<string, IDocumentAdapter> open,
                             ReviewCache cache, bool aiOnStart)
            : this(runner, doc, newRunner, open, cache, aiOnStart, null) { }

        private FindingsPanel(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner, Func<string, IDocumentAdapter> open,
                              ReviewCache cache, bool aiOnStart, IList<string> files)
        {
            _runner = runner; _doc = doc; _newRunner = newRunner; _open = open; _cache = cache; _aiOnStart = aiOnStart; _files = files;
            _session = new ReviewSession(runner, doc, this, Dismissed, files != null);
            _session.ListChanged += Rebuild;
            _session.SelectRequested += Select;
            _session.StatusChanged += text => _status.Text = text;
            _session.BatchReported += ShowResult;
            // Keep the first check's scope: Go to selects text in the editor, which must not shrink a Check again.
            _wholeTopic = string.IsNullOrEmpty(doc.SelectionText);
            Title = "House Style — " + System.IO.Path.GetFileName(doc.SourcePath ?? "");
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(6);
            MinimumSize = new Size(260, 320);

            _status = new LiveLabel { Dock = DockStyle.Top, AutoSize = false, Height = 36, Text = "Checking…" };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee };
            var triage = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            _acceptAll = Btn(triage, "Apply all fixes", (s, e) => { if (_files != null) _session.FixAllAutomatic(); else _session.FixAll(); });
            _bulk = Btn(triage, "Write all as tracked changes", (s, e) => _session.WriteAll());
            _stop = Btn(triage, "Stop AI review", (s, e) => StopReview());
            _stop.Visible = false;
            _batchStop = Btn(triage, "Stop after this file", (s, e) =>
            {
                if (_batch == null) return;
                _batch.Cancel();
                _batchStop.Enabled = false;
                _status.Text = "Stopping after this file. Files already fixed keep their tracked changes.";
            });
            _batchStop.Visible = false;
            _session.RunBatch = RunBatchInPane;

            // Project check: one row per rule (most frequent first) or the suggestions file by file. Remembered.
            _viewBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Visible = files != null };
            _viewBar.Controls.Add(new Label { Text = "View:", AutoSize = true, Margin = new Padding(0, 6, 4, 0) });
            var byFile = files != null && FlareAI.Core.Settings.PluginSettings.Load().ProjectViewByFile;
            _byRule = new RadioButton { Text = "By &rule", AutoSize = true, Checked = !byFile, Margin = new Padding(0, 3, 8, 0) };
            _byFile = new RadioButton { Text = "By &file", AutoSize = true, Checked = byFile, Margin = new Padding(0, 3, 12, 0) };
            _viewBar.Controls.Add(_byRule);
            _viewBar.Controls.Add(_byFile);
            _byRule.CheckedChanged += (s, e) => Safe.Run("View by rule", () => { if (_byRule.Checked) SwitchView(false); });
            _byFile.CheckedChanged += (s, e) => Safe.Run("View by file", () => { if (_byFile.Checked) SwitchView(true); });
            _backToRules = Btn(_viewBar, "All rules", (s, e) => _session.EndRuleReview());
            _showSkipped = Btn(_viewBar, "Show skipped rules", (s, e) => _session.ShowSkippedRules());
            _backToRules.Visible = _showSkipped.Visible = false;

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            _list.Columns.Add("Suggestion");
            _list.Columns.Add("Source");
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => Safe.Run("Select finding", () => { _session.Current = Current(); ShowCard(); _session.OnSelected(Current()); });
            _list.DoubleClick += (s, e) => Safe.Run("Apply", Accept);
            _list.KeyDown += (s, e) => Safe.Run("List key", () => OnListKey(s, e));
            _list.ContextMenuStrip = BuildRowMenu();

            // The card: what, why, impact, and the decision.
            var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            _cardHead = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 34, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            _cardDiff = DiffView.Create();
            _cardDiff.Dock = DockStyle.Top; _cardDiff.Height = 64; _cardDiff.BackColor = SystemColors.Window;
            _cardWhy = DiffView.Create();
            _cardWhy.Dock = DockStyle.Fill; _cardWhy.BackColor = SystemColors.Control;
            var actions = _actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            // A rule row's card: fix it everywhere, step through it, turn it off, or put it aside for now.
            _ruleActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0), Visible = false };
            _fixRule = Btn(_ruleActions, "Fix all…", (s, e) => _session.FixRule(CurrentRule()));
            _reviewRule = Btn(_ruleActions, "Review one by one", (s, e) => ReviewRule());
            _turnOffRule = Btn(_ruleActions, "Turn off rule…", (s, e) => _session.TurnOffRule(CurrentRule()));
            _skipRule = Btn(_ruleActions, "Skip", (s, e) => _session.SkipRule(CurrentRule()));
            _accept = Btn(actions, "Apply", (s, e) => Accept());
            _goTo = Btn(actions, "Go to", (s, e) => _session.GoToCurrent());
            _openFile = Btn(actions, "Open file", (s, e) => _session.OpenFileOfCurrent());
            _reject = Btn(actions, "Reject ▾", (s, e) => _rejectMenu.Show(_reject, new Point(0, _reject.Height)));
            _edit = Btn(actions, "Edit…", (s, e) => _session.Edit());
            _refine = Btn(actions, "Refine…", (s, e) => Refine());
            _reanalyze = Btn(actions, "Check this paragraph again", (s, e) => Reanalyze());
            _rule = Btn(actions, "Guide ↗", (s, e) => { var f = Current(); if (f != null && !string.IsNullOrEmpty(f.Link)) OpenLink(f.Link); });
            _rejectMenu = new ContextMenuStrip();
            _rejectMenu.Items.Add("Wrong — this suggestion is incorrect", null, (s, e) => Safe.Run("Reject", () => _session.Reject("wrong")));
            _rejectMenu.Items.Add("Fine here — correct in this context", null, (s, e) => Safe.Run("Reject", () => _session.Reject("fine here")));
            _rejectMenu.Items.Add("Rule doesn't apply to this text", null, (s, e) => Safe.Run("Reject", () => _session.Reject("rule doesn't apply")));
            _rejectMenu.Items.Add(new ToolStripSeparator());
            _rejectMenu.Items.Add("Skip for now", null, (s, e) => Safe.Run("Skip", _session.Skip));
            card.Controls.Add(_cardWhy);
            card.Controls.Add(_cardDiff);
            card.Controls.Add(_cardHead);
            card.Controls.Add(actions);
            card.Controls.Add(_ruleActions);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, FixedPanel = FixedPanel.Panel2 };
            split.Panel1.Controls.Add(_list);
            // What the last batch did (changed, skipped with the reason, failed) stays here until the next check.
            _result = new TextBox { Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 90, Visible = false, BackColor = SystemColors.Window };
            split.Panel1.Controls.Add(_result);
            split.Panel2.Controls.Add(card);
            // The pane is laid out small first and grows; keep the card at a readable height whenever it does.
            const int CardHeight = 250;
            split.SizeChanged += (s, e) =>
            {
                if (split.Height < CardHeight + 140 || split.Panel2.Height >= CardHeight) return;
                try { split.SplitterDistance = split.Height - CardHeight - split.SplitterWidth; } catch (Exception) { }
            };

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            _recheck = Btn(bar, "Check again", (s, e) => { _aiOnStart = true; Start(); });
            _ignore = Btn(bar, "Turn off this rule…", (s, e) => { var r = CurrentRule(); if (r != null) _session.TurnOffRule(r); else _session.TurnOffRule(); });
            Btn(bar, "Close", (s, e) => { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });

            var tips = new ToolTip();
            tips.SetToolTip(_accept, "Write this suggestion into the topic as a tracked change and go to the next one (Enter or double-click).");
            tips.SetToolTip(_reject, "Don't apply it: say why, so the style guide and the AI can be tuned (Delete).");
            tips.SetToolTip(_edit, "Change the suggested text yourself, then apply it.");
            tips.SetToolTip(_refine, "Ask the AI again for this one suggestion, with your instruction (\"keep SSO\", \"shorter\").");
            tips.SetToolTip(_reanalyze, "Check this paragraph again, for example after you edited it.");
            tips.SetToolTip(_bulk, "Write every suggestion that has exact text (fixes and rewords) into the topic as tracked changes, then review them with Flare's own Accept and Reject (Review tab) or in a review package.");
            tips.SetToolTip(_acceptAll, "Apply every fix as a tracked change. In this topic only, one Ctrl+Z undoes it; if it reaches other files you see a preview first.");
            tips.SetToolTip(_stop, "Stop asking the AI. Results so far are kept; the rest of the paragraphs are not reviewed.");
            if (files != null) tips.SetToolTip(_acceptAll, "Apply every exact fix in every file, except rules you skipped. You see the rules, files and counts first; nothing is written until you click Apply.");
            tips.SetToolTip(_batchStop,"Stop before the next file. Files already fixed keep their tracked changes; the rest are not touched.");
            tips.SetToolTip(_byRule, "One row per rule, most frequent first: fix a rule everywhere at once, or step through it (Alt+R).");
            tips.SetToolTip(_byFile, "Every suggestion, file by file (Alt+F).");
            tips.SetToolTip(_fixRule, "Apply this rule's exact fixes in every file as tracked changes. You see the files and counts first; nothing is written until you click Apply.");
            tips.SetToolTip(_reviewRule, "Step through this rule's suggestions only, with Apply and Go to (Enter).");
            tips.SetToolTip(_skipRule, "Hide this rule until the next check. Nothing is recorded (Delete).");
            _ignoreTip = tips;

            // The last notice (a toast vanishes by itself) stays reachable here, with its action as a focusable link.
            _notice = new NoticeLink { Dock = DockStyle.Top, AutoSize = false, Height = 20, Visible = false, AccessibleRole = AccessibleRole.Link };
            _notice.LinkClicked += (s, e) => { var n = Toast.Last; if (n != null && n.Action != null) n.Action(); };
            Toast.LastChanged += OnNoticeChanged;
            HandleDestroyed += (s, e) => Toast.LastChanged -= OnNoticeChanged;
            OnNoticeChanged();

            Controls.Add(split);      // Fill must be added first so docked bars take their space
            Controls.Add(bar);
            _cost = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 18, ForeColor = SystemColors.GrayText, Text = AiCost.Session() };
            Controls.Add(_cost);
            Controls.Add(_viewBar);
            Controls.Add(triage);
            Controls.Add(_progress);
            Controls.Add(_notice);
            _aiOffNote = new LinkLabel { Dock = DockStyle.Top, AutoSize = false, Height = 20, Visible = false, Text = ReviewCache.AiOffNote + "  Settings", AccessibleRole = AccessibleRole.Link };
            _aiOffNote.LinkArea = new LinkArea(ReviewCache.AiOffNote.Length + 2, "Settings".Length);
            _aiOffNote.LinkClicked += (s, e) => Safe.Run("Settings", () => { var settings = OpenSettings; if (settings != null) settings(); });
            Controls.Add(_aiOffNote);
            Controls.Add(_status);
            NameControls();
            HandleDestroyed += (s, e) => { if (_worker != null && _worker.IsBusy) _worker.CancelAsync(); };
            ShowCard();
        }

        private void OnNoticeChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke((Action)OnNoticeChanged); return; }
            var n = Toast.Last;
            _notice.Visible = n != null;
            if (n == null) return;
            _notice.Links.Clear();
            _notice.Text = n.Action == null ? n.Text : n.Text + "  " + n.ActionLabel;
            if (n.Action != null) _notice.Links.Add(n.Text.Length + 2, n.ActionLabel.Length);
            _notice.AccessibleName = "Last notice: " + n.Text + (n.Action != null ? ". " + n.ActionLabel : "");
            _notice.Announce();
        }

        /// <summary>Accessible names, descriptions and a reading-order tab sequence (status, triage, list, card, bar).</summary>
        private void NameControls()
        {
            A11y.Name(this, "House Style suggestions", "Style suggestions for the open topic, with a card for the selected one.");
            A11y.Name(_progress, "Progress");
            A11y.Name(_list, "Suggestions", "List of suggestions in reading order. Each row starts with its severity: filled circle is an error, triangle a warning, open circle a tip. Enter applies, Delete rejects.");
            A11y.Name(_result, "Result of the last batch");
            A11y.Name(_cardDiff, "What changes", "The text around the change, with removed words struck through and added words underlined.");
            A11y.Name(_cardWhy, "Why", "The reason, the guide rule and its example.");
            A11y.Name(_accept, "Apply", "Applies this suggestion as a tracked change and goes to the next one.");
            A11y.Name(_reject, "Reject", "Opens a menu of reasons for not applying it.");
            A11y.Name(_edit, "Edit", "Change the suggested text yourself, then apply it. Opens a dialog.");
            A11y.Name(_refine, "Refine", "Ask the AI again for this suggestion with your instruction. Opens a dialog.");
            A11y.Name(_rule, "Open guide page", "Opens the guide page for this rule in your browser.");
            A11y.Name(_recheck, "Check again");
            A11y.Name(_ignore, "Turn off this rule", "Turns the selected rule off or changes its level, or asks the style guide owner to. Opens a dialog.");
            // Tab order: notice, triage buttons, list, decision buttons, card text, bottom bar.
            var top = new Control[] { _status, _notice, _acceptAll.Parent, _viewBar, _progress, _list.Parent.Parent, _cost, _recheck.Parent };
            for (var i = 0; i < top.Length; i++) top[i].TabIndex = i;
            A11y.Name(_acceptAll.Parent, "Bulk actions"); A11y.Name(_recheck.Parent, "Panel actions"); A11y.Name(_accept.Parent, "Decision for the selected suggestion");
            A11y.Name(_viewBar, "View", "List the project's suggestions by rule or by file.");
            A11y.Name(_ruleActions, "Actions for the selected rule");
            A11y.Name(_fixRule, "Fix all", "Applies this rule's exact fixes in every file as tracked changes, after a preview of the files and counts. Opens a dialog.");
            A11y.Name(_reviewRule, "Review one by one", "Lists only this rule's suggestions and steps through them.");
            A11y.Name(_skipRule, "Skip", "Hides this rule until the next check.");
            A11y.Name(_backToRules, "All rules", "Ends Review one by one and lists every rule again (Escape).");
            A11y.Name(_batchStop, "Stop after this file");
            var order = new Control[] { _notice, _acceptAll, _bulk, _stop, _batchStop, _byRule, _byFile, _backToRules, _showSkipped, _list, _result,
                                        _accept, _reject, _edit, _refine, _reanalyze, _rule, _fixRule, _reviewRule, _turnOffRule, _skipRule, _cardDiff, _cardWhy, _recheck, _ignore };
            for (var i = 0; i < order.Length; i++) order[i].TabIndex = i;
            A11y.Apply(this);
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
            b.Click += (s, e) => Safe.Run(text, () => click(s, e));
            bar.Controls.Add(b);
            return b;
        }

        private void FitColumns()
        {
            var w = Math.Max(150, _list.ClientSize.Width - 4);
            _list.Columns[0].Width = w * 62 / 100;
            _list.Columns[1].Width = w - _list.Columns[0].Width;
        }

        /// <summary>Right-click on a row: Go to, Open file (the whole topic or snippet, nothing selected), Copy file path.</summary>
        private ContextMenuStrip BuildRowMenu()
        {
            var menu = new ContextMenuStrip();
            var goTo = menu.Items.Add("Go to", null, (s, e) => Safe.Run("Go to", () => _session.GoToCurrent()));
            var open = menu.Items.Add("Open file", null, (s, e) => Safe.Run("Open file", () => _session.OpenFileOfCurrent()));
            menu.Items.Add("Copy file path", null, (s, e) => Safe.Run("Copy file path", () =>
            {
                var path = _session.FilePathOf(Current());
                if (path == null) return;
                try { Clipboard.SetText(path); _status.Text = "Copied " + path; }
                catch (Exception ex) { Diagnostics.Log("copy path", ex); _status.Text = "Could not copy the path: " + ex.Message; }
            }));
            menu.Opening += (s, e) =>
            {
                var p = _list.PointToClient(Cursor.Position);
                var hit = _list.GetItemAt(p.X, p.Y);
                if (hit != null) hit.Selected = true;
                var f = Current();
                if (f == null) { e.Cancel = true; return; }
                goTo.Enabled = !f.IsFixed;
                open.Enabled = _session.FilePathOf(f) != null;
            };
            return menu;
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (RuleRows)
            {
                if (e.KeyCode == Keys.Enter && CurrentRule() != null) { ReviewRule(); e.Handled = true; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete && CurrentRule() != null) { _session.SkipRule(CurrentRule()); e.Handled = true; }
                return;
            }
            if ((e.KeyCode == Keys.Escape || e.KeyCode == Keys.Back) && _session.FocusRule != null) { _session.EndRuleReview(); e.Handled = true; e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Enter && e.Shift) { _session.OpenFileOfCurrent(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter && e.Control) { _session.GoToCurrent(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { _rejectMenu.Show(_reject, new Point(0, _reject.Height)); e.Handled = true; }
        }

        // ---- check -------------------------------------------------------------------------------

        private void Start()
        {
            if (_worker != null && _worker.IsBusy) return;
            _session.Clear(); _list.Items.Clear(); _list.Groups.Clear(); _lintNote = null;
            _aiOff = FlareAI.Core.Settings.PluginSettings.Load().AiOff; _aiHidden = false; _aiOffNote.Visible = false;
            _reviewing = true; _progress.Visible = true; _progress.Style = ProgressBarStyle.Marquee; _status.Text = "Checking style rules (free, no AI)…";
            _result.Visible = false; _stop.Visible = false; _stop.Enabled = true; _stopped = false; _unitCount = 0; _doneUnits = 0;
            ShowCard();

            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            if (_files != null) { StartProject(); return; }
            var cachedAny = false;
            _worker.DoWork += (s, e) =>
            {
                _requestNote = _session.CheckRequests();   // outcomes of the writer's requests to the owners
                var blocks = _wholeTopic ? _runner.PickAllBlocks(_doc) : _runner.PickCheckBlocks(_doc);
                List<StyleFinding> lint;
                try { lint = _runner.Lint(blocks); }
                catch (Exception ex) { Diagnostics.Log("docstyle", ex); lint = new List<StyleFinding>(); _lintNote = "The style rules couldn't be loaded, so only the AI review is shown. See Diagnostics for details."; }
                try { lint.AddRange(FlareAI.Core.Style.StructureCheck.Of(blocks)); }
                catch (Exception ex) { Diagnostics.Log("structure", ex); }
                try { lint.AddRange(_runner.LintSnippets(blocks, _doc.SourcePath)); }
                catch (Exception ex) { Diagnostics.Log("snippets", ex); }
                _session.PrimeSnippetUses(lint);
                // The AI review is split into small units (one block each, by default) reviewed several at a time, so the
                // first cards arrive in seconds and each model call has one focused job. Prompts read the DOM: build them here.
                var units = _runner.PrepareReview(blocks, lint);
                _lint = lint;
                _worker.ReportProgress(0, new object[] { lint, null, 0, units.Count });  // from here the author may edit while we wait
                var done = 0;
                // The first unit alone warms the prompt cache; the rest then read it (see ActionRunner.WarmThenParallel).
                ActionRunner.WarmThenParallel(units.Count, _runner.Parallelism, i =>
                {
                    if (_worker.CancellationPending) return;
                    var hidden = false;
                    var raw = _cache == null ? null : _cache.Shown(units[i].Prompt, _aiOff, out hidden);
                    if (hidden) _aiHidden = true;
                    if (raw != null) cachedAny = true;
                    else if (_aiOnStart && !_aiOff)
                    {
                        raw = _runner.CallReview(units[i]);
                        if (_cache != null) _cache.Put(units[i].Prompt, raw);
                    }
                    var n = System.Threading.Interlocked.Increment(ref done);
                    if (raw != null) _worker.ReportProgress(n, new object[] { raw, units[i], n, units.Count });
                });
                if (_worker.CancellationPending) e.Cancel = true;
            };
            _worker.ProgressChanged += (s, e) =>
            {
                if (IsDisposed) return;
                var p = (object[])e.UserState;
                var found = (List<StyleFinding>)p[0];
                var unit = p[1] as ReviewUnit;
                int doneUnits = (int)p[2], total = (int)p[3];
                _session.Add(unit == null ? found : _runner.AnchorReview(found, unit, _lint)); // DOM work on the UI thread
                _unitCount = total; _doneUnits = doneUnits;
                if (_aiOnStart && !_aiOff && doneUnits < total && !_stopped)
                {
                    // Style-rule results are already in the list, and they cost nothing; only this phase spends.
                    _status.Text = Summary() + "  Style rules are free. Reviewing wording with AI: " + doneUnits + " of " + total + " paragraphs (you can work on these now).";
                    _stop.Visible = true;
                }
                else _status.Text = Summary();
                _cost.Text = AiCost.Session();
            };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                _reviewing = false; _progress.Visible = false; _stop.Visible = false;
                _cost.Text = AiCost.Session();
                if (e.Cancelled)
                {
                    _status.Text = Summary() + "  Stopped: " + _doneUnits + " of " + _unitCount + " paragraphs were reviewed with AI. Results so far are kept. Check again reviews the rest.";
                    UpdateButtons();
                    return;
                }
                if (e.Error != null)
                {
                    Diagnostics.Log("Check style", e.Error);
                    _status.Text = Summary() + "  " + (e.Error.Message == FlareAI.Core.Llm.LlmClientFactory.AiOffMessage
                        ? "AI review is off. Turn it on in House Style > Settings."   // the writer chose No AI: not a failure
                        : "AI review failed: " + e.Error.Message);
                }
                else if (_aiOff)
                    _status.Text = Summary() + "  These come from style rules only (free, no AI used).";
                else if (!_aiOnStart && !cachedAny)
                    _status.Text = Summary() + "  These come from style rules only (free, no AI used). Check again" + (_unitCount > 0 ? " reviews " + Plural.Of(_unitCount, "paragraph") + " with AI." : " adds the AI wording review.");
                else _status.Text = Open().Count == 0 ? "No open suggestions." : Summary();
                if (_lintNote != null) _status.Text += "  " + _lintNote;
                _aiOffNote.Visible = _aiOff && _aiHidden;
                var guide = _runner.GuideNotice;
                if (guide != null) _status.Text += "  " + guide;
                ShowRequestNote();
                var team = Team.Current;
                if (team != null && team.Status != null && team.Status.Behind > 0) _status.Text += "  " + team.Status;
                if (_list.SelectedItems.Count == 0) _session.Next();
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
        }

        /// <summary>Outcomes of the writer's requests (approved, declined), once each: in the status line and as a notice.</summary>
        private void ShowRequestNote()
        {
            var note = _requestNote;
            _requestNote = null;
            if (string.IsNullOrEmpty(note)) return;
            _status.Text += "  " + note;
            try { Toast.Show(FindForm(), note); } catch (Exception ex) { Diagnostics.Log("request notice", ex); }
        }

        private void StopReview()
        {
            if (_worker == null || !_worker.IsBusy) return;
            _stopped = true;
            _stop.Enabled = false;
            _status.Text = "Stopping after the paragraphs already being reviewed…";
            _worker.CancelAsync();
        }

        private void StartProject()
        {
            _status.Text = "Checking " + Plural.Of(_files.Count, "file") + "…";
            _worker.DoWork += (s, e) => { _requestNote = _session.CheckRequests(); e.Result = _runner.LintFiles(_files, (done, total) => _worker.ReportProgress(done * 100 / Math.Max(1, total), done + "/" + total)); };
            _worker.ProgressChanged += (s, e) => { if (!IsDisposed) _status.Text = "Checking " + e.UserState + " files…"; };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Cancelled) return;
                _reviewing = false; _progress.Visible = false;
                if (e.Error != null) { Diagnostics.Log("Check project", e.Error); _status.Text = "Project check failed: " + e.Error.Message; }
                else
                {
                    var found = (List<StyleFinding>)e.Result;
                    _session.Add(found);
                    _status.Text = Summary() + "  Checked " + Plural.Of(_files.Count, "file") + " with style rules only (free, no AI).";
                    var guide = _runner.GuideNotice;
                    if (guide != null) _status.Text += "  " + guide;
                    ShowRequestNote();
                    if (RuleRows && _list.Items.Count > 0) { _list.Items[0].Selected = true; _list.Items[0].Focused = true; }
                }
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
        }

        /// <summary>Redraws the list in the session's order (it changed), keeping the selection.</summary>
        private void Rebuild()
        {
            if (_files != null)
            {
                var reviewing = _session.FocusRule != null;
                _backToRules.Visible = reviewing;
                _showSkipped.Visible = _session.SkippedRuleCount > 0 && !reviewing;
                _showSkipped.Text = "Show skipped rules (" + _session.SkippedRuleCount + ")";
                _list.Columns[0].Text = RuleRows ? "Rule" : "Suggestion";
                _list.Columns[1].Text = RuleRows ? "How many" : "Source";
                if (RuleRows) { RebuildRules(); return; }
            }
            _actions.Visible = true; _ruleActions.Visible = false;
            var keep = Current();
            var focus = _session.FocusRule;
            _list.BeginUpdate();
            _list.Items.Clear(); _list.Groups.Clear();
            foreach (var f in _session.Findings)
            {
                if (focus != null && RuleBacklog.KeyOf(f) != focus) continue;
                var item = new ListViewItem(new[] { A11y.SeverityGlyph(f.Severity) + " " + ProposalText(f), LayerLabel(f) }) { Tag = f, Group = GroupFor(f) };
                item.ToolTipText = A11y.SeverityLabel(f.Severity) + " · " + LayerLabel(f) + " · " + ReadableRule(f) + "\n" + f.Suggestion;
                Restyle(item);
                _list.Items.Add(item);
                if (f == keep) item.Selected = true;
            }
            _list.EndUpdate();
            _session.Current = Current();
            UpdateButtons();
            var changed = Changed;
            if (changed != null) try { changed(this); } catch (Exception ex) { Diagnostics.Log("markers", ex); }
        }

        // ---- project check: by rule ----------------------------------------------------------------

        /// <summary>The list shows one row per rule (a project check, By rule, not reviewing one rule).</summary>
        private bool RuleRows { get { return _files != null && _byRule.Checked && _session.FocusRule == null; } }

        /// <summary>The selected rule row, or null.</summary>
        private BacklogRule CurrentRule()
        {
            return _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as BacklogRule : null;
        }

        /// <summary>One row per rule, most frequent first, keeping the selected rule (or the one just reviewed) selected.</summary>
        private void RebuildRules()
        {
            var cur = CurrentRule();
            var keep = cur != null ? cur.Key : _lastRule;
            _list.BeginUpdate();
            _list.Items.Clear(); _list.Groups.Clear();
            ListViewItem selected = null;
            foreach (var r in _session.Backlog())
            {
                var counts = r.Counts() + (r.Snippets.Count > 0 ? " · in " + Plural.Of(r.Snippets.Count, "snippet") : "");
                var item = new ListViewItem(new[] { A11y.SeverityGlyph(r.Severity) + " " + Shorten(r.Reason, 90), counts }) { Tag = r };
                item.ToolTipText = r.Title + "\n" + r.Reason + "\n" + counts;
                item.ForeColor = r.Severity == "error" ? Color.Firebrick : SystemColors.WindowText;
                _list.Items.Add(item);
                if (r.Key == keep) selected = item;
            }
            if (selected == null && _list.Items.Count > 0 && keep != null) selected = _list.Items[0];   // the rule is done: on to the next biggest
            if (selected != null) { selected.Selected = true; selected.Focused = true; selected.EnsureVisible(); }
            _list.EndUpdate();
            _lastRule = null;
            _list.AccessibleDescription = "Rules, most frequent first, with how many suggestions and files each has. Enter reviews the selected rule one by one; Delete skips it.";
            _session.Current = null;
            ShowCard();
            var changed = Changed;
            if (changed != null) try { changed(this); } catch (Exception ex) { Diagnostics.Log("markers", ex); }
        }

        private void SwitchView(bool byFile)
        {
            try
            {
                var settings = FlareAI.Core.Settings.PluginSettings.Load();
                if (settings.ProjectViewByFile != byFile) { settings.ProjectViewByFile = byFile; settings.Save(); }
            }
            catch (Exception ex) { Diagnostics.Log("save view", ex); }
            if (_session.FocusRule != null) _session.EndRuleReview();
            else Rebuild();
            _list.Focus();
        }

        private void ReviewRule()
        {
            var r = CurrentRule();
            if (r == null) return;
            _lastRule = r.Key;
            _session.ReviewRule(r);
            _list.Focus();
        }

        /// <summary>The card for a rule row: why the rule exists, where it fires, real examples, and what Fix all would do.</summary>
        private void ShowRuleCard(BacklogRule r)
        {
            _cardHead.Text = A11y.SeverityLabel(r.Severity) + "  ·  " + r.Title + "\n" + r.Counts();
            _cardHead.Height = Math.Max(34, TextRenderer.MeasureText(_cardHead.Text, _cardHead.Font, new Size(Math.Max(50, _cardHead.Width), 0), TextFormatFlags.WordBreak).Height + 6);
            var shown = 0;
            foreach (var f in r.Findings)
            {
                if (shown == 3) break;
                if (shown++ > 0) _cardDiff.AppendText("\n");
                DiffView.Segments(_cardDiff, new List<DiffSegment> { new DiffSegment { Kind = DiffKind.Same, Text = BatchPlan.ProjectPath(f.DocumentPath ?? "") + ":  …" + Tail(f.Before, 30) } }, true);
                if (f.Replacement != null) DiffView.Segments(_cardDiff, ReadableDiff.OfText(f.Excerpt, f.Replacement), false);
                else DiffView.Segments(_cardDiff, new List<DiffSegment> { new DiffSegment { Kind = DiffKind.Same, Text = f.Excerpt } }, false);
            }
            Line(_cardWhy, r.Reason, FontStyle.Regular);
            var auto = r.Automatic.Count;
            Line(_cardWhy, auto == r.Occurrences ? "Every one can be fixed automatically: Fix all shows the files and counts before anything is written."
                         : auto > 0 ? auto + " of " + r.Occurrences + " can be fixed automatically; the rest need you (Review one by one)."
                         : "None can be fixed automatically: Review one by one takes you to each, with Go to.", FontStyle.Italic);
            if (r.Snippets.Count > 0)
            {
                var uses = new List<string>();
                foreach (var p in r.Snippets) { int n; uses.Add(System.IO.Path.GetFileName(p) + (_session.KnownSnippetUses(p, out n) ? " (used in " + UsesLabel(n) + ")" : "")); }
                Line(_cardWhy, "In snippets: " + string.Join(", ", uses.ToArray()) + ". A fix there changes every topic that includes it.", FontStyle.Bold);
            }
            const int ShowFiles = 6;
            var files = new List<string>();
            for (var i = 0; i < r.Files.Count && i < ShowFiles; i++) files.Add(BatchPlan.ProjectPath(r.Files[i]));
            Line(_cardWhy, "Files: " + string.Join(", ", files.ToArray()) + (r.Files.Count > ShowFiles ? " and " + (r.Files.Count - ShowFiles) + " more." : "."), FontStyle.Regular);
            _cardDiff.Select(0, 0); _cardWhy.Select(0, 0);
            _list.AccessibleDescription = (r.Title + ". " + r.Counts() + ". " + r.Reason).Replace("\n", " ");
        }

        private static string Tail(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(s.Length - n); }

        /// <summary>
        /// Runs a batch from the pane: a few files at once; more file by file between messages, with progress and
        /// Stop after this file. The list and actions wait until it finishes.
        /// </summary>
        private void RunBatchInPane(BatchRun run)
        {
            if (run.FileCount <= 3) { run.RunToEnd(); return; }
            _batch = run;
            _progress.Style = ProgressBarStyle.Continuous; _progress.Minimum = 0; _progress.Maximum = run.FileCount; _progress.Value = 0; _progress.Visible = true;
            _batchStop.Visible = true; _batchStop.Enabled = true;
            _list.Enabled = false;
            UpdateButtons();
            _status.Text = "Fixing " + Plural.Of(run.FileCount, "file") + "…";
            run.Finished += r => EndBatch(run);
            var timer = new Timer { Interval = 1 };   // WM_TIMER is low priority: clicks and painting get in between files
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var more = false;
                try
                {
                    if (IsDisposed) run.Cancel();
                    more = run.Step();
                    if (more && !IsDisposed)
                    {
                        _progress.Value = Math.Min(run.FileCount, run.FilesDone);
                        if (!run.IsCancelled) _status.Text = "Fixing files: " + run.FilesDone + " of " + run.FileCount + " done. Next: " + BatchPlan.ProjectPath(run.NextFile ?? "");
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("Fix files", ex);
                    run.Cancel();
                    try { more = run.Step(); } catch (Exception ex2) { Diagnostics.Log("Fix files: finish", ex2); more = false; }
                    EndBatch(run);
                }
                if (more) timer.Start(); else timer.Dispose();
            };
            timer.Start();
        }

        /// <summary>The batch is over (finished, stopped or failed): the list and actions are back.</summary>
        private void EndBatch(BatchRun run)
        {
            if (_batch != run) return;
            _batch = null;
            if (IsDisposed) return;
            _progress.Visible = false; _progress.Style = ProgressBarStyle.Marquee;
            _batchStop.Visible = false;
            _list.Enabled = true;
            UpdateButtons();
            _list.Focus();
        }

        /// <summary>Raised whenever the list changes (new findings, apply, reject, check again): the editor markers follow it.</summary>
        public static event Action<FindingsPanel> Changed;

        /// <summary>The topic this panel shows.</summary>
        public IDocumentAdapter Doc { get { return _doc; } }

        /// <summary>Open proposals in the topic itself (not in snippets), for the editor markers.</summary>
        public List<StyleFinding> OpenTopicFindings() { return _session.OpenTopicFindings(); }

        /// <summary>
        /// Live check: replaces the topic's style-rule and structure findings with fresh ones (the author typed); AI findings
        /// stay, and any whose text changed show as stale. Skipped while a review is running.
        /// </summary>
        public void RefreshLint(List<StyleFinding> lint)
        {
            if (_reviewing) return;
            _session.RefreshLint(lint);   // not in a project report
        }

        private ListViewGroup GroupFor(StyleFinding f)
        {
            string key, label;
            if (_files != null) { key = f.DocumentPath ?? ""; label = RelativeToContent(key); }
            else { key = f.Section ?? ""; label = key.Length == 0 ? "(top of topic)" : key; }
            if (f.DocumentPath != null && _files == null)
            {
                int uses;
                label += _session.KnownSnippetUses(f.DocumentPath, out uses) && uses > 1 ? "  — used in " + uses + " files" : "";
            }
            return _list.Groups[key] ?? _list.Groups.Add(key, label);
        }

        private static string ProposalText(StyleFinding f)
        {
            var mark = f.IsFixed ? "✓ " : f.IsStale ? "⚠ " : "";
            switch (f.Kind)
            {
                case "variable": return mark + "\"" + f.Excerpt + "\" → variable " + f.Variable;
                case "restructure": return mark + "\"" + Shorten(f.Excerpt, 48) + "\" — rewrite";
                case "manual": return mark + f.Rule.Substring(f.Rule.IndexOf('.') + 1) + ": \"" + Shorten(f.Excerpt, 36) + "\"";
                default: return mark + (f.Replacement.Length == 0 ? "delete \"" + f.Excerpt + "\"" : "\"" + Shorten(f.Excerpt, 30) + "\" → \"" + Shorten(f.Replacement, 30) + "\"");
            }
        }

        private static string Shorten(string s, int n) { return ReviewSession.Shorten(s, n); }

        private string RelativeToContent(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            while (dir != null && !System.IO.Path.GetFileName(dir).Equals("Content", StringComparison.OrdinalIgnoreCase)) dir = System.IO.Path.GetDirectoryName(dir);
            return dir == null ? System.IO.Path.GetFileName(path) : path.Substring(dir.Length + 1);
        }

        /// <summary>The rule by what it says (tooltips), not by its internal name.</summary>
        private string ReadableRule(StyleFinding f)
        {
            var rule = f.RuleId == null ? null : _runner.RuleFor(f.RuleId);
            return rule != null ? ReviewSession.Shorten(rule.Display, 80) : FlareAI.Core.Style.RuleWording.FromName(f.Rule);
        }

        private static string LayerLabel(StyleFinding f)
        {
            return (f.RequestSent ? "Request sent · " : "") + RuleSource(f);
        }

        private static string RuleSource(StyleFinding f)
        {
            if (f.RuleLayer != null) return "AI · " + SourceName(f.RuleLayer) + ": " + f.RuleSection;
            if (string.IsNullOrEmpty(f.Layer) || f.Layer == "ai") return "AI";
            if (!string.IsNullOrEmpty(f.RuleSection)) return SourceName(f.Layer) + " · " + f.RuleSection;
            return SourceName(f.Layer) + (string.IsNullOrEmpty(f.Rule) ? "" : " · " + f.Rule.Substring(f.Rule.IndexOf('.') + 1));
        }

        /// <summary>Where a rule comes from, in the writer's words: Google, Our rules, This project, AI.</summary>
        internal static string SourceName(string layer) { return ReviewSession.SourceName(layer); }

        private List<StyleFinding> Open() { return _session.Open(); }

        private string Summary() { return _session.Summary(); }

        // ---- the card ----------------------------------------------------------------------------

        private StyleFinding Current()
        {
            return _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as StyleFinding : null;
        }

        private void ShowCard()
        {
            var f = Current();
            _cardDiff.Clear(); _cardWhy.Clear();
            var ruleRow = CurrentRule();
            var rows = RuleRows;
            _actions.Visible = !rows;
            _ruleActions.Visible = rows && ruleRow != null;
            if (ruleRow != null) { ShowRuleCard(ruleRow); UpdateButtons(); return; }
            if (f == null)
            {
                _cardHead.Text = _session.Findings.Count == 0 ? ""
                               : rows ? (_list.Items.Count == 0 ? "" : "Select a rule to see why it matters, where it fires, and fix it everywhere at once.")
                               : "Select a suggestion to see what changes and why.";
                UpdateButtons();
                return;
            }
            var where = f.DocumentPath == null ? ""
                : f.DocumentPath.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase)
                    ? "In snippet " + System.IO.Path.GetFileName(f.DocumentPath) + " \u2014 used in " + UsesLabel(_session.SnippetUses(f.DocumentPath)) + "; applying changes all of them"
                    : "In " + System.IO.Path.GetFileName(f.DocumentPath);
            _cardHead.Text = A11y.SeverityLabel(f.Severity) + "  ·  " + LayerLabel(f) + "  ·  " + KindLabel(f) + (f.IsFixed ? "  ·  applied" : f.IsStale ? "  ·  Text changed: check again" : "")
                             + (where.Length > 0 ? "\n" + where : "");
            // Grow to fit: the snippet line ("used in N topics") must never be cut off.
            _cardHead.Height = Math.Max(34, TextRenderer.MeasureText(_cardHead.Text, _cardHead.Font, new Size(Math.Max(50, _cardHead.Width), 0), TextFormatFlags.WordBreak).Height + 6);
            _list.AccessibleDescription = (_cardHead.Text + ". " + f.Suggestion).Replace("\n", " ");   // what a screen reader says for the selected row

            // What: the words around it, and the change marked.
            if (!string.IsNullOrEmpty(f.Before)) DiffView.Segments(_cardDiff, new List<DiffSegment> { new DiffSegment { Kind = DiffKind.Same, Text = "…" + f.Before } }, true);
            if (f.Replacement != null) DiffView.Segments(_cardDiff, ReadableDiff.OfText(f.Excerpt, f.Replacement), false);
            else
            {
                _cardDiff.SelectionStart = _cardDiff.TextLength;
                _cardDiff.SelectionBackColor = Color.FromArgb(255, 242, 204);
                _cardDiff.SelectionFont = new Font(_cardDiff.Font, FontStyle.Bold);
                _cardDiff.AppendText(f.Excerpt);
            }

            // Why: the reason, the rule as written in the guide, and its example.
            Line(_cardWhy, f.Suggestion, FontStyle.Regular);
            var rule = _runner.RuleFor(f.RuleId);
            // The rule by what it says and where it lives; the id is only a reference for owners (Validate, Tune).
            if (rule != null) Line(_cardWhy, SourceName(rule.Layer) + " · " + rule.Section + ": " + rule.Display, FontStyle.Italic);
            var ex = _runner.ExampleFor(f.RuleId);
            if (ex != null)
            {
                if (!string.IsNullOrEmpty(ex.Bad)) Line(_cardWhy, "✗ " + ex.Bad, FontStyle.Regular);
                if (!string.IsNullOrEmpty(ex.Good)) Line(_cardWhy, "✓ " + ex.Good, FontStyle.Regular);
            }
            if (rule == null && f.Source == "ai") Line(_cardWhy, "General editing judgment (not tied to a guide rule).", FontStyle.Italic);
            if (rule != null && Team.Current != null && Team.Current.IsOwner) Ref(_cardWhy, "Reference: " + rule.Id);   // for the guide owner (Validate, Tune); writers don't need it
            _cardDiff.Select(0, 0); _cardWhy.Select(0, 0);
            UpdateButtons();
        }

        private static string UsesLabel(int n) { return n + (n == 1 ? " topic" : " topics"); }

        private static void Line(RichTextBox box, string text, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            box.SelectionStart = box.TextLength;
            box.SelectionFont = new Font(box.Font, style);
            box.SelectionColor = SystemColors.ControlText;
            box.AppendText((box.TextLength > 0 ? "\n" : "") + text);
        }

        /// <summary>A small grey reference line (the rule's reference: for owners, not something writers need to read).</summary>
        private static void Ref(RichTextBox box, string text)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionFont = new Font(box.Font.FontFamily, box.Font.Size - 1.5f, FontStyle.Regular);
            box.SelectionColor = SystemColors.GrayText;
            box.AppendText((box.TextLength > 0 ? "\n" : "") + text);
        }

        private static string KindLabel(StyleFinding f)
        {
            switch (f.Kind)
            {
                case "mechanical": return "Fix";
                case "wording": return "Reword";
                case "variable": return "Needs you — use the variable";
                case "manual": return "Needs you";
                default: return "Reword";
            }
        }

        // ---- decisions ---------------------------------------------------------------------------

        private void Accept()
        {
            var f = Current();
            switch (_session.Accept())
            {
                case AcceptOutcome.Rewrite: RewriteParagraph(f); break;
                case AcceptOutcome.AltText: SuggestAltText(f); break;
            }
        }

        /// <summary>The AI looks at the image and drafts alt text; the author edits it; it is applied as a tracked change.</summary>
        private void SuggestAltText(StyleFinding f)
        {
            var block = _runner.LocateBlock(_doc, f) ?? f.Block;
            var img = AltText.MissingIn(block);
            if (img == null) { _status.Text = "That image already has alt text (or is gone). Check this paragraph again."; return; }
            var inside = false;
            for (XmlNode n = img; n != null && !inside; n = n.ParentNode) inside = n == block;
            if (!inside) block = img.ParentNode; // the finding sits on the text next to an image-only paragraph
            LlmImageFile image;
            try { image = AltText.Load(_doc.SourcePath, img.GetAttribute("src")); }
            catch (Exception ex) { _status.Text = ex.Message + " Write the alt text in the image's properties."; return; }
            var src = img.GetAttribute("src");
            var context = AltText.Context(block);
            var runner = _newRunner() ?? _runner;
            Busy(true, "Looking at " + System.IO.Path.GetFileName(image.Path) + "…");
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = runner.SuggestAlt(image, context);
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                Busy(false, null);
                if (e.Error != null) { Diagnostics.Log("Alt text", e.Error); _status.Text = "Alt text failed: " + e.Error.Message; return; }
                var suggestion = (string)e.Result;
                var alt = TextPrompt.Ask(this, "Alt text", suggestion == null
                    ? "The AI thinks " + System.IO.Path.GetFileName(image.Path) + " is decorative. Type alt text if it isn't (screen readers will read it):"
                    : "Alt text for " + System.IO.Path.GetFileName(image.Path) + " (edit before applying):", suggestion ?? "", false);
                if (string.IsNullOrEmpty(alt)) return;
                try
                {
                    AltText.Apply(_doc, block, src, alt.Trim());
                    _session.AltTextApplied(f, suggestion, alt.Trim());
                }
                catch (Exception ex) { Diagnostics.Log("Alt text apply", ex); _status.Text = "Could not apply: " + ex.Message; }
            };
            worker.RunWorkerAsync();
        }

        /// <summary>Keeps the per-file outcome in the pane when it has more to say than the status line.</summary>
        private void ShowResult(BatchResult result)
        {
            var many = result.Changed.Count + result.Skipped.Count + result.Failed.Count > 1;
            if (!many && result.Skipped.Count == 0 && result.Failed.Count == 0) return;
            var text = result.Report().Replace("\r\n", "\n").Replace("\n", "\r\n");
            _result.Text = text;
            _result.Height = Math.Min(140, 18 * (text.Split('\n').Length + 1));
            _result.Visible = true;
        }

        private void Refine()
        {
            var f = Current();
            if (f == null || f.IsFixed || f.IsStale) return;
            var instruction = TextPrompt.Ask(this, "Refine suggestion", "How should the AI change this suggestion? (for example: keep the term SSO; make it shorter; keep the passive voice here)", "", true);
            if (string.IsNullOrEmpty(instruction)) return;
            var block = f.DocumentPath == null ? _runner.LocateBlock(_doc, f) : null;
            var context = block != null ? block.InnerText : (f.Before ?? "") + f.Excerpt;
            var runner = _newRunner() ?? _runner;
            Busy(true, "Refining…");
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = runner.Refine(f, context, instruction);
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                Busy(false, null);
                if (e.Error != null) { Diagnostics.Log("Refine", e.Error); _status.Text = "Refine failed: " + e.Error.Message; return; }
                _session.Refined(f, (StyleFinding)e.Result, instruction);
            };
            worker.RunWorkerAsync();
        }

        /// <summary>Checks the selected suggestion's paragraph again (style rules + AI) and replaces its suggestions.</summary>
        private void Reanalyze()
        {
            var f = Current();
            var block = _session.ReanalyzeTarget(f);
            if (block == null) return;
            var blocks = new List<XmlNode> { block };
            Busy(true, "Checking the paragraph again…");
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) =>
            {
                var lint = _runner.Lint(blocks);
                var units = _runner.PrepareReview(blocks, lint, 1);
                var raw = units.Count == 0 ? new List<StyleFinding>() : _runner.CallReview(units[0]);
                e.Result = new object[] { lint, raw, units.Count == 0 ? null : units[0] };
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                Busy(false, null);
                if (e.Error != null) { Diagnostics.Log("Re-analyze", e.Error); _status.Text = "Couldn't check the paragraph again: " + e.Error.Message; return; }
                var parts = (object[])e.Result;
                var lint = (List<StyleFinding>)parts[0];
                var fresh = new List<StyleFinding>(lint);
                if (parts[2] != null) fresh.AddRange(_runner.AnchorReview((List<StyleFinding>)parts[1], (ReviewUnit)parts[2], lint));
                _session.Reanalyzed(f, block, fresh);
            };
            worker.RunWorkerAsync();
        }

        private void RewriteParagraph(StyleFinding f)
        {
            List<StyleFinding> related;
            var block = _session.RewriteTarget(f, out related);
            if (block == null) return;
            var instruction = "Resolve these review findings and change nothing else:\n" + ActionRunner.Summarize(related);
            string applied;
            using (var form = new ResultForm("Rewrite paragraph", _newRunner(), _doc, ActionKind.Ask, instruction, new List<XmlNode> { block }))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                applied = form.AppliedSummary;
            }
            _session.Rewritten(related, applied);
        }

        // ---- navigation --------------------------------------------------------------------------

        /// <summary>The session moved the selection: select that row (null clears the card).</summary>
        private void Select(StyleFinding f)
        {
            if (RuleRows) return;   // rule rows: the rule stays selected (Rebuild picks the next rule when one is done)
            foreach (ListViewItem item in _list.Items)
            {
                var on = item.Tag == f;
                item.Selected = on;
                if (on) { item.Focused = true; item.EnsureVisible(); }
            }
            if (f == null) ShowCard();
        }

        private void Busy(bool on, string text)
        {
            _reviewing = on; _progress.Visible = on;
            if (on) { _progress.Style = ProgressBarStyle.Marquee; _status.Text = text; }
            _cost.Text = AiCost.Session();
            UpdateButtons();
        }

        private void OpenLink(string url)
        {
            // Rules written in a Flare style-guide topic link to that topic: open it in Flare, next to the work.
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var path = new Uri(url).LocalPath;
                    if (_open != null && (path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) && _open(path) != null) return;
                }
                catch (Exception ex) { Diagnostics.Log("guide link", ex); }
                _status.Text = "Could not open " + url;
                return;
            }
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return; // otherwise only web guide pages
            try { System.Diagnostics.Process.Start(url); } catch (Exception ex) { _status.Text = "Could not open " + url + ": " + ex.Message; }
        }

        private static void Restyle(ListViewItem item)
        {
            var f = (StyleFinding)item.Tag;
            var done = f.IsFixed || f.IsStale;
            item.ForeColor = done ? SystemColors.GrayText : (f.Severity == "error" ? Color.Firebrick : SystemColors.WindowText);
            item.Font = f.IsFixed ? new Font(SystemFonts.MessageBoxFont, FontStyle.Strikeout) : f.IsStale ? new Font(SystemFonts.MessageBoxFont, FontStyle.Italic) : SystemFonts.MessageBoxFont;
        }

        private void UpdateButtons()
        {
            var f = Current();
            var open = f != null && !f.IsFixed && !f.IsStale;
            _accept.Enabled = open;
            _goTo.Enabled = f != null && !f.IsFixed;
            _openFile.Enabled = _session.FilePathOf(f) != null;
            _accept.Text = f == null ? "Apply" : f.Rule == "Structure.AltText" && f.DocumentPath == null ? "Suggest alt text…" : f.Kind == "restructure" ? "Rewrite…" : f.Kind == "variable" || f.Kind == "manual" ? "Go to" : "Apply";
            _reject.Enabled = f != null && !f.IsFixed;
            _edit.Enabled = open && f.Kind != "variable" && f.Kind != "manual";
            _refine.Enabled = open && !_reviewing;
            _reanalyze.Enabled = f != null && f.DocumentPath == null && !_reviewing && _files == null;
            _rule.Enabled = f != null && !string.IsNullOrEmpty(f.Link);
            var busy = _batch != null;
            if (_files != null)
            {
                var auto = _session.AutomaticCandidates().Count;
                _acceptAll.Text = "Fix all automatic suggestions… (" + auto + ")";
                _acceptAll.Enabled = auto > 0 && !busy;
                _bulk.Visible = false;
            }
            else
            {
                var mech = _session.FixAllCandidates().Count;
                _acceptAll.Text = "Apply all fixes (" + mech + ")";
                _acceptAll.Enabled = mech > 0;
            }
            var exact = _session.WriteAllCandidates().Count;
            _bulk.Text = "Write all as tracked changes (" + exact + ")";
            _bulk.Enabled = exact > 0 && _files == null;
            _recheck.Enabled = !_reviewing && !busy;
            var r = CurrentRule();
            _ignore.Enabled = (f != null || r != null) && !busy;
            _viewBar.Enabled = !busy;
            _actions.Enabled = !busy;
            _ruleActions.Enabled = !busy;
            if (r != null)
            {
                var n = r.Automatic.Count;
                _fixRule.Text = n == 0 ? "Fix all…" : "Fix all (" + n + ")…";
                _fixRule.Enabled = n > 0;
                _fixRule.AccessibleName = n == 0 ? "Fix all" : "Fix all " + n;
            }
            var request = Team.Current != null && Team.Current.ChangesNeedRequest;
            _turnOffRule.Text = request ? "Request a change…" : "Turn off rule…";
            _turnOffRule.AccessibleName = _turnOffRule.Text;
            _ignore.Text = _session.RuleChangeLabel();
            _ignore.AccessibleName = _ignore.Text;
            _ignoreTip.SetToolTip(_ignore, request
                ? "Ask the style guide owners to turn off or change the selected rule, or to make an exception for this project. They decide; you see the answer on your next check."
                : "Turn the selected rule off, or change its level, for the whole team or just this project.");
        }

        // ---- IReviewHost: dialogs and documents for the session ---------------------------------

        bool IReviewHost.Confirm(string message, string action) { return Notify.Confirm(this, message, action); }

        RuleSettingChoice IReviewHost.ChooseRuleSetting(string ruleText, bool canChangeTeamGuide, bool personalGuide, IList<FlareAI.Core.Style.RuleRef> otherRules)
        {
            using (var form = new RuleSettingForm(ruleText, canChangeTeamGuide, personalGuide, otherRules))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return null;
                return new RuleSettingChoice { ForTeam = form.ForTeam, Setting = form.Setting, Reason = form.Reason, AlsoOthers = form.AlsoOthers };
            }
        }

        RuleSettingChoice IReviewHost.ChooseRequest(RequestQuestion question) { return RequestForm.Ask(this, question); }

        string IReviewHost.AskText(string title, string label, string initial, bool multiline) { return TextPrompt.Ask(this, title, label, initial, multiline); }

        IDocumentAdapter IReviewHost.OpenDocument(string path) { return _open == null ? null : _open(path); }

        bool IReviewHost.PreviewBatch(BatchPlan plan)
        {
            using (var form = new BatchPreviewForm(plan))
                return form.ShowDialog(this) == DialogResult.OK;
        }

        bool IReviewHost.SaveAndClose(IDocumentAdapter doc)
        {
            var fa = doc as FlareAI.Flare.FlareDocumentAdapter;
            if (fa == null || !fa.OpenedByPlugin) return false;
            fa.SaveAndClose();
            return true;
        }

        void IReviewHost.ShowError(string whatHappened, string whatToDo, Exception details, string action) { Notify.Error(this, whatHappened, whatToDo, details, action); }
    }

    /// <summary>A one-field text prompt (Edit / Refine).</summary>
    internal static class TextPrompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string initial, bool multiline)
        {
            using (var form = new Form { Text = "House Style — " + title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(520, multiline ? 190 : 150), Font = SystemFonts.MessageBoxFont, AutoScaleMode = AutoScaleMode.Font })
            {
                var text = new Label { Text = label, Left = 12, Top = 12, Width = 496, Height = 34 };
                var box = new TextBox { Left = 12, Top = 50, Width = 496, Height = multiline ? 84 : 24, Multiline = multiline, AcceptsReturn = false, Text = initial ?? "", ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
                var ok = new Button { Text = "OK", Left = 352, Top = form.ClientSize.Height - 36, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 433, Top = form.ClientSize.Height - 36, Width = 75, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { text, box, ok, cancel });
                A11y.Name(box, label);
                A11y.Apply(form);
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
