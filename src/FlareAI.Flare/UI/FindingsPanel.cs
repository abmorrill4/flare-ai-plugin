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
    /// A review as discrete pieces of work. The list shows every proposal in reading order, grouped by section;
    /// the card below shows the selected one — what changes (word diff), why (the cited rule, its text and
    /// example), and its impact — with Accept / Reject / Edit / Refine / Re-analyze. Accepting writes a tracked
    /// change and moves to the next proposal; the editor never switches away from the topic on its own.
    /// Lint results show at once; the AI review arrives section by section.
    /// </summary>
    public sealed class FindingsPanel : UserControl
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
        private readonly Button _accept, _reject, _edit, _refine, _reanalyze, _rule;
        private readonly ContextMenuStrip _rejectMenu;

        private static readonly string SettingsDir = FlareAI.Core.Settings.PluginSettings.SettingsDirectory;
        private static readonly Dismissals Dismissed = Dismissals.Load(System.IO.Path.Combine(SettingsDir, "dismissed.json"));
        private HashSet<string> _ignoredAi;
        private readonly List<StyleFinding> _findings = new List<StyleFinding>();
        private readonly Dictionary<string, int> _usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private BackgroundWorker _worker;
        private bool _reviewing, _started;
        private string _lintNote;
        private List<StyleFinding> _lint;

        /// <summary>Raised when the author clicks Close; the host hides its pane or window.</summary>
        public event EventHandler CloseRequested;

        /// <summary>"House Style — topic.htm" — for the host's title.</summary>
        public string Title { get; private set; }

        private readonly bool _wholeTopic;
        private IList<string> _files; // project mode: lint these files (no AI), group by file

        /// <summary>A project-wide check: every topic and snippet, linter only, grouped by file.</summary>
        public static FindingsPanel ForProject(ActionRunner runner, IDocumentAdapter activeDoc, Func<ActionRunner> newRunner,
                                               Func<string, IDocumentAdapter> open, IList<string> files, string projectName)
        {
            var panel = new FindingsPanel(runner, activeDoc, newRunner, open, null, false) { _files = files };
            panel.Title = "House Style — project " + projectName + " (" + files.Count + " files)";
            return panel;
        }

        public bool IsProjectReport { get { return _files != null; } }

        /// <summary>Path of the topic this panel checks.</summary>
        public string TopicPath { get { return _doc.SourcePath; } }

        private readonly Func<string, IDocumentAdapter> _open;
        private readonly ReviewCache _cache;
        private bool _aiOnStart;

        /// <param name="open">Opens another file (a snippet) in Flare and returns its adapter, or null.</param>
        /// <param name="cache">Shared AI review cache (same content = same review, across topics, renames and sessions).</param>
        /// <param name="aiOnStart">False when the panel opens because the author switched topics: only a cached AI
        /// review is shown automatically; a new one runs on Re-check (no surprise cost).</param>
        public FindingsPanel(ActionRunner runner, IDocumentAdapter doc, Func<ActionRunner> newRunner, Func<string, IDocumentAdapter> open,
                             ReviewCache cache, bool aiOnStart)
        {
            _runner = runner; _doc = doc; _newRunner = newRunner; _open = open; _cache = cache; _aiOnStart = aiOnStart;
            // Keep the first check's scope: Go to selects text in the editor, which must not shrink a Re-check.
            _wholeTopic = string.IsNullOrEmpty(doc.SelectionText);
            Title = "House Style — " + System.IO.Path.GetFileName(doc.SourcePath ?? "");
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(6);
            MinimumSize = new Size(260, 320);

            _status = new LiveLabel { Dock = DockStyle.Top, AutoSize = false, Height = 36, Text = "Linting…" };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee };
            var triage = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            _acceptAll = Btn(triage, "Accept all mechanical", (s, e) => AcceptAllMechanical());
            _bulk = Btn(triage, "Propose all as tracked changes", (s, e) => ProposeAll());
            _stop = Btn(triage, "Stop AI review", (s, e) => StopReview());
            _stop.Visible = false;

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowItemToolTips = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            _list.Columns.Add("Proposal");
            _list.Columns.Add("Rule");
            _list.Resize += (s, e) => FitColumns();
            _list.SelectedIndexChanged += (s, e) => { ShowCard(); var f = Current(); if (f != null && f.DocumentPath == null) GoTo(f, false); };
            _list.DoubleClick += (s, e) => Accept();
            _list.KeyDown += OnListKey;

            // The card: what, why, impact, and the decision.
            var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
            _cardHead = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 34, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };
            _cardDiff = DiffView.Create();
            _cardDiff.Dock = DockStyle.Top; _cardDiff.Height = 64; _cardDiff.BackColor = SystemColors.Window;
            _cardWhy = DiffView.Create();
            _cardWhy.Dock = DockStyle.Fill; _cardWhy.BackColor = SystemColors.Control;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
            _accept = Btn(actions, "Accept", (s, e) => Accept());
            _reject = Btn(actions, "Reject ▾", (s, e) => _rejectMenu.Show(_reject, new Point(0, _reject.Height)));
            _edit = Btn(actions, "Edit…", (s, e) => EditAndAccept());
            _refine = Btn(actions, "Refine…", (s, e) => Refine());
            _reanalyze = Btn(actions, "Re-analyze", (s, e) => Reanalyze());
            _rule = Btn(actions, "Guide ↗", (s, e) => { var f = Current(); if (f != null && !string.IsNullOrEmpty(f.Link)) OpenLink(f.Link); });
            _rejectMenu = new ContextMenuStrip();
            _rejectMenu.Items.Add("Wrong — the proposal is incorrect", null, (s, e) => Reject("wrong"));
            _rejectMenu.Items.Add("Fine here — correct in this context", null, (s, e) => Reject("fine here"));
            _rejectMenu.Items.Add("Rule doesn't apply to this text", null, (s, e) => Reject("rule doesn't apply"));
            _rejectMenu.Items.Add(new ToolStripSeparator());
            _rejectMenu.Items.Add("Skip for now", null, (s, e) => Next());
            card.Controls.Add(_cardWhy);
            card.Controls.Add(_cardDiff);
            card.Controls.Add(_cardHead);
            card.Controls.Add(actions);

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
            _recheck = Btn(bar, "Re-check", (s, e) => { _aiOnStart = true; Start(); });
            _ignore = Btn(bar, "Turn off rule…", (s, e) => IgnoreRule());
            Btn(bar, "Close", (s, e) => { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });

            var tips = new ToolTip();
            tips.SetToolTip(_accept, "Apply this proposal as a tracked change and go to the next one (Enter or double-click).");
            tips.SetToolTip(_reject, "Not applying it: say why, so the guide and the AI can be tuned (Delete).");
            tips.SetToolTip(_edit, "Change the proposed text yourself, then apply it.");
            tips.SetToolTip(_refine, "Ask the AI again for this one proposal, with your instruction (\"keep SSO\", \"shorter\").");
            tips.SetToolTip(_reanalyze, "Review this paragraph again (after you edited it).");
            tips.SetToolTip(_bulk, "Write every proposal that has exact text (rule fixes and AI wording) into the topic as tracked changes, then review them with Flare's own Accept/Reject (Review tab) or in a review package.");
            tips.SetToolTip(_acceptAll, "Apply every exact rule fix as tracked changes. In this topic only, one Ctrl+Z undoes it; if it reaches other files you see a preview first.");
            tips.SetToolTip(_stop, "Stop asking the AI. Results so far are kept; the rest of the paragraphs are not reviewed.");
            tips.SetToolTip(_ignore, "Turn the selected rule off for this whole project (written to the project's StyleGuide folder, shared with your team).");

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
            Controls.Add(triage);
            Controls.Add(_progress);
            Controls.Add(_notice);
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
            A11y.Name(this, "House Style findings", "Style proposals for the open topic, with a card for the selected one.");
            A11y.Name(_progress, "Progress");
            A11y.Name(_list, "Style proposals", "List of proposals in reading order. Each row starts with its severity: filled circle is an error, triangle a warning, open circle a tip. Enter accepts, Delete rejects.");
            A11y.Name(_result, "Result of the last batch");
            A11y.Name(_cardDiff, "What changes", "The text around the change, with removed words struck through and added words underlined.");
            A11y.Name(_cardWhy, "Why", "The reason, the guide rule and its example.");
            A11y.Name(_accept, "Accept", "Applies this proposal as a tracked change and goes to the next one.");
            A11y.Name(_reject, "Reject", "Opens a menu of reasons for not applying it.");
            A11y.Name(_edit, "Edit", "Change the proposed text yourself, then apply it. Opens a dialog.");
            A11y.Name(_refine, "Refine", "Ask the AI again for this proposal with your instruction. Opens a dialog.");
            A11y.Name(_rule, "Open guide page", "Opens the guide page for this rule in your browser.");
            A11y.Name(_recheck, "Re-check");
            A11y.Name(_ignore, "Turn off rule", "Turns the selected rule off or changes its level. Opens a dialog.");
            // Tab order: notice, triage buttons, list, decision buttons, card text, bottom bar.
            var top = new Control[] { _status, _notice, _acceptAll.Parent, _progress, _list.Parent.Parent, _cost, _recheck.Parent };
            for (var i = 0; i < top.Length; i++) top[i].TabIndex = i;
            A11y.Name(_acceptAll.Parent, "Bulk actions"); A11y.Name(_recheck.Parent, "Panel actions"); A11y.Name(_accept.Parent, "Decision for the selected proposal");
            var order = new Control[] { _notice, _acceptAll, _bulk, _stop, _list, _result, _accept, _reject, _edit, _refine, _reanalyze, _rule, _cardDiff, _cardWhy, _recheck, _ignore };
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
            b.Click += click;
            bar.Controls.Add(b);
            return b;
        }

        private void FitColumns()
        {
            var w = Math.Max(150, _list.ClientSize.Width - 4);
            _list.Columns[0].Width = w * 62 / 100;
            _list.Columns[1].Width = w - _list.Columns[0].Width;
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { _rejectMenu.Show(_reject, new Point(0, _reject.Height)); e.Handled = true; }
        }

        // ---- check -------------------------------------------------------------------------------

        private void Start()
        {
            if (_worker != null && _worker.IsBusy) return;
            _findings.Clear(); _list.Items.Clear(); _list.Groups.Clear(); _lintNote = null;
            _reviewing = true; _progress.Visible = true; _progress.Style = ProgressBarStyle.Marquee; _status.Text = "Checking style rules (free, no AI)…";
            _result.Visible = false; _stop.Visible = false; _stop.Enabled = true; _stopped = false; _unitCount = 0; _doneUnits = 0;
            ShowCard();

            _worker = new BackgroundWorker { WorkerReportsProgress = true, WorkerSupportsCancellation = true };
            if (_files != null) { StartProject(); return; }
            var cachedAny = false;
            _worker.DoWork += (s, e) =>
            {
                var blocks = _wholeTopic ? _runner.PickAllBlocks(_doc) : _runner.PickCheckBlocks(_doc);
                List<StyleFinding> lint;
                try { lint = _runner.Lint(blocks); }
                catch (Exception ex) { Diagnostics.Log("docstyle", ex); lint = new List<StyleFinding>(); _lintNote = "Style linter unavailable (" + ex.Message + ") — showing AI review only."; }
                try { lint.AddRange(FlareAI.Core.Style.StructureCheck.Of(blocks)); }
                catch (Exception ex) { Diagnostics.Log("structure", ex); }
                try { lint.AddRange(_runner.LintSnippets(blocks, _doc.SourcePath)); }
                catch (Exception ex) { Diagnostics.Log("snippets", ex); }
                foreach (var f in lint)
                    if (f.DocumentPath != null && !_usage.ContainsKey(f.DocumentPath)) _usage[f.DocumentPath] = ActionRunner.SnippetUsage(f.DocumentPath);
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
                    var raw = _cache == null ? null : _cache.Get(units[i].Prompt);
                    if (raw != null) cachedAny = true;
                    else if (_aiOnStart)
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
                Add(unit == null ? found : _runner.AnchorReview(found, unit, _lint)); // DOM work on the UI thread
                _unitCount = total; _doneUnits = doneUnits;
                if (_aiOnStart && doneUnits < total && !_stopped)
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
                    _status.Text = Summary() + "  Stopped: " + _doneUnits + " of " + _unitCount + " paragraphs were reviewed with AI. Results so far are kept. Re-check reviews the rest.";
                    UpdateButtons();
                    return;
                }
                if (e.Error != null)
                {
                    Diagnostics.Log("Check style", e.Error);
                    _status.Text = Summary() + "  AI review failed: " + e.Error.Message;
                }
                else if (!_aiOnStart && !cachedAny)
                    _status.Text = Summary() + "  These come from style rules only (free, no AI used). Re-check" + (_unitCount > 0 ? " reviews " + _unitCount + " paragraphs with AI." : " adds the AI wording review.");
                else _status.Text = Open().Count == 0 ? "No open proposals." : Summary();
                if (_lintNote != null) _status.Text += "  " + _lintNote;
                var team = Team.Current;
                if (team != null && team.Status != null && team.Status.Behind > 0) _status.Text += "  " + team.Status;
                if (_list.SelectedItems.Count == 0) Next();
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
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
                    _status.Text = found.Count + " issue(s) in " + files.Count + " of " + _files.Count + " files (linter only). Accept opens the file.";
                }
                UpdateButtons();
            };
            _worker.RunWorkerAsync();
        }

        private void Add(List<StyleFinding> found)
        {
            if (_ignoredAi == null) _ignoredAi = ProjectRules.IgnoredAiRules(_doc.SourcePath);
            foreach (var f in found)
            {
                if (Dismissed.IsDismissed(f, _doc.SourcePath)) continue;
                if (f.Layer == "ai" && _ignoredAi.Contains(f.Rule ?? "")) continue;
                if (f.Layer != "ai" && f.RuleSection == null)
                {
                    var rule = _runner.RuleFor(f.RuleId);   // company/project table rules: label by guide section
                    if (rule != null) f.RuleSection = rule.Section;
                }
                if (f.RuleText == null && f.RuleId != null)
                {
                    var cited = _runner.RuleFor(f.RuleId);   // its wording now: feedback records a fingerprint of it
                    if (cited != null) f.RuleText = cited.Text;
                }
                _findings.Add(f);
            }
            Rebuild();
        }

        /// <summary>Re-sorts into reading order and redraws the list, keeping the selection.</summary>
        private void Rebuild()
        {
            var keep = Current();
            if (_files == null) ReviewOrder.Annotate(_doc.Document, _findings);
            _findings.Sort((a, b) => _files != null
                ? string.Compare(a.DocumentPath, b.DocumentPath, StringComparison.OrdinalIgnoreCase)
                : a.Order.CompareTo(b.Order));
            _list.BeginUpdate();
            _list.Items.Clear(); _list.Groups.Clear();
            foreach (var f in _findings)
            {
                var item = new ListViewItem(new[] { A11y.SeverityGlyph(f.Severity) + " " + ProposalText(f), LayerLabel(f) }) { Tag = f, Group = GroupFor(f) };
                item.ToolTipText = A11y.SeverityLabel(f.Severity) + " · " + LayerLabel(f) + " · " + f.Rule + "\n" + f.Suggestion;
                Restyle(item);
                _list.Items.Add(item);
                if (f == keep) item.Selected = true;
            }
            _list.EndUpdate();
            UpdateButtons();
            var changed = Changed;
            if (changed != null) try { changed(this); } catch (Exception ex) { Diagnostics.Log("markers", ex); }
        }

        /// <summary>Raised whenever the list changes (new findings, accept, reject, refresh): the editor markers follow it.</summary>
        public static event Action<FindingsPanel> Changed;

        /// <summary>The topic this panel shows.</summary>
        public IDocumentAdapter Doc { get { return _doc; } }

        /// <summary>Open proposals in the topic itself (not in snippets), for the editor markers.</summary>
        public List<StyleFinding> OpenTopicFindings() { return _findings.FindAll(f => !f.IsFixed && !f.IsStale && f.DocumentPath == null); }

        /// <summary>
        /// Live check: replaces the topic's linter and structure findings with fresh ones (the author typed); AI findings
        /// stay, and any whose text changed show as stale. Skipped while a review is running.
        /// </summary>
        public void RefreshLint(List<StyleFinding> lint)
        {
            if (_reviewing || _files != null) return;
            _findings.RemoveAll(f => f.Source != "ai" && f.DocumentPath == null && !f.IsFixed);
            Add(lint);
        }

        private ListViewGroup GroupFor(StyleFinding f)
        {
            string key, label;
            if (_files != null) { key = f.DocumentPath ?? ""; label = RelativeToContent(key); }
            else { key = f.Section ?? ""; label = key.Length == 0 ? "(top of topic)" : key; }
            if (f.DocumentPath != null && _files == null)
            {
                int uses;
                label += _usage.TryGetValue(f.DocumentPath, out uses) && uses > 1 ? "  — used in " + uses + " files" : "";
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

        private static string Shorten(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }

        private string RelativeToContent(string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            while (dir != null && !System.IO.Path.GetFileName(dir).Equals("Content", StringComparison.OrdinalIgnoreCase)) dir = System.IO.Path.GetDirectoryName(dir);
            return dir == null ? System.IO.Path.GetFileName(path) : path.Substring(dir.Length + 1);
        }

        private static string LayerLabel(StyleFinding f)
        {
            if (f.RuleLayer != null) return "AI · " + Cap(f.RuleLayer) + ": " + f.RuleSection;
            if (string.IsNullOrEmpty(f.Layer) || f.Layer == "ai") return "AI";
            if (!string.IsNullOrEmpty(f.RuleSection)) return Cap(f.Layer) + " · " + f.RuleSection;
            return Cap(f.Layer) + (string.IsNullOrEmpty(f.Rule) ? "" : " · " + f.Rule.Substring(f.Rule.IndexOf('.') + 1));
        }

        private static string Cap(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        private List<StyleFinding> Open() { return _findings.FindAll(f => !f.IsFixed && !f.IsStale); }

        private string Summary()
        {
            var open = Open();
            if (open.Count == 0) return "No open proposals.";
            int mech = 0, wording = 0, rewrite = 0, manual = 0;
            var sections = new HashSet<string>();
            foreach (var f in open)
            {
                sections.Add(f.Section ?? "");
                if (f.Kind == "mechanical") mech++; else if (f.Kind == "wording") wording++; else if (f.Kind == "manual") manual++; else rewrite++;
            }
            return open.Count + " proposal(s)" + (sections.Count > 1 ? " in " + sections.Count + " sections" : "") + ": "
                   + mech + " mechanical · " + wording + " wording · " + rewrite + " need a rewrite" + (manual > 0 ? " · " + manual + " to fix by hand" : "") + ".";
        }

        // ---- the card ----------------------------------------------------------------------------

        private StyleFinding Current()
        {
            return _list.SelectedItems.Count == 1 ? (StyleFinding)_list.SelectedItems[0].Tag : null;
        }

        private void ShowCard()
        {
            var f = Current();
            _cardDiff.Clear(); _cardWhy.Clear();
            if (f == null)
            {
                _cardHead.Text = _findings.Count == 0 ? "" : "Select a proposal to see what changes and why.";
                UpdateButtons();
                return;
            }
            var where = f.DocumentPath == null ? "" : "In snippet " + System.IO.Path.GetFileName(f.DocumentPath) + " \u2014 used in " + UsesLabel(SnippetUses(f.DocumentPath)) + "; applying changes all of them";
            _cardHead.Text = A11y.SeverityLabel(f.Severity) + "  ·  " + LayerLabel(f) + "  ·  " + KindLabel(f) + (f.IsFixed ? "  ·  accepted" : f.IsStale ? "  ·  Text changed: check again" : "")
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
            if (rule != null) Line(_cardWhy, (rule.Layer == "google" ? "Google" : rule.Layer == "project" ? "This project" : "Company") + " · " + rule.Section + ": " + rule.Text, FontStyle.Italic);
            var ex = _runner.ExampleFor(f.RuleId);
            if (ex != null)
            {
                if (!string.IsNullOrEmpty(ex.Bad)) Line(_cardWhy, "✗ " + ex.Bad, FontStyle.Regular);
                if (!string.IsNullOrEmpty(ex.Good)) Line(_cardWhy, "✓ " + ex.Good, FontStyle.Regular);
            }
            if (rule == null && f.Source == "ai") Line(_cardWhy, "General editing judgment (not tied to a guide rule).", FontStyle.Italic);
            if (rule != null) Ref(_cardWhy, "Rule id " + rule.Id);
            _cardDiff.Select(0, 0); _cardWhy.Select(0, 0);
            UpdateButtons();
        }

        private int SnippetUses(string path)
        {
            int uses;
            if (!_usage.TryGetValue(path, out uses)) _usage[path] = uses = ActionRunner.SnippetUsage(path);
            return uses;
        }

        private static string UsesLabel(int n) { return n + (n == 1 ? " topic" : " topics"); }

        /// <summary>One question before a snippet changes: it is shared, so every topic that includes it changes too.</summary>
        private bool ConfirmSnippet(StyleFinding f)
        {
            if (f.DocumentPath == null || !f.DocumentPath.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase)) return true;
            return MessageBox.Show(this, BatchPlan.SnippetQuestion(f.DocumentPath, SnippetUses(f.DocumentPath)) + "\n\nThe change is tracked.",
                "House Style", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }

        private static void Line(RichTextBox box, string text, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            box.SelectionStart = box.TextLength;
            box.SelectionFont = new Font(box.Font, style);
            box.SelectionColor = SystemColors.ControlText;
            box.AppendText((box.TextLength > 0 ? "\n" : "") + text);
        }

        /// <summary>A small grey reference line (rule ids: for owners, not something writers need to read).</summary>
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
                case "mechanical": return "rule fix";
                case "wording": return "AI wording";
                case "variable": return "use the variable";
                case "manual": return "fix in the editor";
                default: return "needs a rewrite";
            }
        }

        // ---- decisions ---------------------------------------------------------------------------

        private void Accept()
        {
            var f = Current();
            if (f == null || f.IsFixed) return;
            if (f.IsStale) { _status.Text = "Text changed: check again. Re-analyze the paragraph."; return; }
            if (f.Kind == "variable")
            {
                GoTo(f, true);
                _status.Text = "The text is selected: use Insert ▸ Variable ▸ " + f.Variable + " (Flare replaces the selection).";
                return;
            }
            if (f.Kind == "restructure") { RewriteParagraph(f); return; }
            if (f.Rule == "Structure.AltText" && f.DocumentPath == null) { SuggestAltText(f); return; }
            if (f.Kind == "manual")
            {
                GoTo(f, true);
                _status.Text = "Fix it in the editor (" + f.Suggestion + "), then Re-analyze.";
                return;
            }
            if (!ConfirmSnippet(f)) return;
            if (Apply(new List<StyleFinding> { f }, "House Style: " + f.Rule) > 0)
            {
                FeedbackLog.Record(f, _doc.SourcePath, "accepted");
                Next();
            }
        }

        /// <summary>The AI looks at the image and drafts alt text; the author edits it; it is applied as a tracked change.</summary>
        private void SuggestAltText(StyleFinding f)
        {
            var block = _runner.LocateBlock(_doc, f) ?? f.Block;
            var img = AltText.MissingIn(block);
            if (img == null) { _status.Text = "That image already has alt text (or is gone). Re-analyze."; return; }
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
                    f.IsFixed = true;
                    FeedbackLog.Record(f, _doc.SourcePath, suggestion != null && alt.Trim() == suggestion ? "accepted" : "edited", alt.Trim());
                    Rebuild();
                    _status.Text = Summary() + "  Alt text added as a tracked change.";
                    Next();
                }
                catch (Exception ex) { Diagnostics.Log("Alt text apply", ex); _status.Text = "Could not apply: " + ex.Message; }
            };
            worker.RunWorkerAsync();
        }

        private void AcceptAllMechanical()
        {
            var todo = _findings.FindAll(f => f.CanFix && f.Kind == "mechanical" && (f.DocumentPath == null || _files != null));
            if (todo.Count == 0) return;
            // One policy for every batch: anything that reaches a file other than the open topic is previewed first;
            // a batch only in the open topic writes directly (one Ctrl+Z).
            var plan = BatchPlan.Build(todo, _doc.SourcePath, BatchPlan.WhyNotWritable, ActionRunner.SnippetUsage);
            if ((plan.NeedsPreview || plan.Skipped.Count > 0) && plan.Groups.Count > 0)
            {
                using (var form = new BatchPreviewForm(plan))
                    if (form.ShowDialog(this) != DialogResult.OK) return;
            }
            var chosen = plan.SelectedFindings;
            if (chosen.Count == 0 && plan.Skipped.Count == 0) return;
            ApplyBatch(chosen, chosen.Count == 1 ? "House Style: " + chosen[0].Rule : "House Style: " + chosen.Count + " style fixes", plan);
            foreach (var f in chosen) if (f.IsFixed) FeedbackLog.Record(f, _doc.SourcePath, "accepted");
            var inSnippets = _findings.FindAll(f => f.CanFix && f.DocumentPath != null && _files == null).Count;
            if (inSnippets > 0) _status.Text += "  " + inSnippets + " more in snippets: select one to apply it there.";
            Next();
        }

        /// <summary>Bulk mode: every exact proposal in the topic becomes a tracked change, for review in Flare's own tools.</summary>
        private void ProposeAll()
        {
            var todo = _findings.FindAll(f => f.CanFix && (f.Kind == "mechanical" || f.Kind == "wording") && f.DocumentPath == null);
            if (todo.Count == 0) return;
            if (MessageBox.Show(this, "Write " + todo.Count + " proposal(s) into the topic as tracked changes?\n\nReview them in Flare's Review tab (Accept / Reject change), " +
                    "or send the topic out in a review package. One Ctrl+Z removes them all.", "House Style — Propose all", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var n = Apply(todo, "House Style: " + todo.Count + " proposals");
            foreach (var f in todo) if (f.IsFixed) FeedbackLog.Record(f, _doc.SourcePath, "proposed", "bulk");
            _status.Text = Summary() + "  Wrote " + n + " tracked change(s) — review them in the Review tab. Proposals that need a rewrite or a manual fix are still listed.";
            Next();
        }

        /// <summary>Applies exact replacements as tracked changes; returns how many were applied.</summary>
        private int Apply(List<StyleFinding> todo, string operation)
        {
            return ApplyBatch(todo, operation, null).FixesApplied;
        }

        /// <summary>
        /// Applies exact replacements as tracked changes, one undo step per document, and reports what happened to each
        /// file. Files that can't be written (read-only, missing) are skipped, not attempted. Snippet fixes open the
        /// snippet (that is where they must be tracked), then the topic is brought back to the front.
        /// </summary>
        private BatchResult ApplyBatch(List<StyleFinding> todo, string operation, BatchPlan plan)
        {
            var result = new BatchResult();
            if (plan != null) foreach (var sk in plan.Skipped) result.AddSkipped(sk.Path, sk.Fixes, sk.Reason);
            try
            {
                foreach (var group in GroupByDocument(todo))
                {
                    var path = group[0].DocumentPath ?? _doc.SourcePath;
                    var elsewhere = group[0].DocumentPath != null && !string.Equals(path, _doc.SourcePath, StringComparison.OrdinalIgnoreCase);
                    if (elsewhere)
                    {
                        var why = BatchPlan.WhyNotWritable(path);
                        if (why != null) { result.AddSkipped(path, group.Count, why); continue; }
                    }
                    try
                    {
                        var doc = DocOf(group[0], true);
                        if (doc == null) { Diagnostics.Log("Apply: could not open " + path); result.AddFailed(path, group.Count, "could not open it in Flare"); continue; }
                        var n = _runner.ApplyFixes(doc, group, operation);
                        var saved = false;
                        // Project-wide: a file opened just for this is saved and closed (one tab per file crashes Flare at ~100).
                        var fa = doc as FlareAI.Flare.FlareDocumentAdapter;
                        if (_files != null && fa != null && fa.OpenedByPlugin && doc != _doc)
                        {
                            try { fa.SaveAndClose(); saved = true; }
                            catch (Exception ex) { Diagnostics.Log("Apply: save/close " + doc.SourcePath, ex); }
                        }
                        if (n > 0) result.AddChanged(path, n, saved);
                        if (n < group.Count) result.AddSkipped(path, group.Count - n, "text changed");
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log("Apply " + path, ex);
                        result.AddFailed(path, group.Count, ex.Message);
                    }
                }
                if (_files == null && todo.Exists(f => f.DocumentPath != null) && _open != null) _open(_doc.SourcePath); // back to the topic
                Rebuild();
                _status.Text = Summary() + "  " + result.Status();
                if (_files == null && result.Changed.Exists(e => !string.Equals(e.Path, _doc.SourcePath, StringComparison.OrdinalIgnoreCase)))
                    _status.Text += " The snippet stays open in its own tab, unsaved.";
                ShowResult(result);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Apply", ex);
                _status.Text = "Could not apply: " + ex.Message;
            }
            return result;
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

        private void Reject(string reason)
        {
            var f = Current();
            if (f == null) return;
            Dismissed.Dismiss(f, _doc.SourcePath);
            FeedbackLog.Record(f, _doc.SourcePath, "rejected", reason);
            var next = NextAfter(f);
            _findings.Remove(f);
            Rebuild();
            Select(next);
            _status.Text = Summary() + "  Rejected (" + reason + "); it stays hidden for this text.";
        }

        private void EditAndAccept()
        {
            var f = Current();
            if (f == null || f.IsFixed || f.IsStale) return;
            var text = TextPrompt.Ask(this, "Edit proposal", "Replace \"" + Shorten(f.Excerpt, 80) + "\" with:", f.Replacement ?? f.Excerpt, false);
            if (text == null || text == f.Excerpt) return;
            var edited = Clone(f);
            edited.Replacement = text;
            if (!ConfirmSnippet(f)) return;
            if (Apply(new List<StyleFinding> { edited }, "House Style: " + f.Rule + " (edited)") > 0)
            {
                f.IsFixed = true;
                FeedbackLog.Record(edited, _doc.SourcePath, "edited", f.Replacement);
                Rebuild();
                Next();
            }
        }

        private void Refine()
        {
            var f = Current();
            if (f == null || f.IsFixed || f.IsStale) return;
            var instruction = TextPrompt.Ask(this, "Refine proposal", "How should the AI change this proposal? (for example: keep the term SSO; make it shorter; keep the passive voice here)", "", true);
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
                var refined = (StyleFinding)e.Result;
                FeedbackLog.Record(f, _doc.SourcePath, "refined", instruction);
                if (refined.Replacement != null && refined.Replacement == f.Excerpt)
                {
                    // The author's instruction amounts to "keep it": nothing to apply.
                    Select(f);
                    Reject("fine here (refined: " + refined.Suggestion + ")");
                    return;
                }
                _findings[_findings.IndexOf(f)] = refined;
                Rebuild();
                Select(refined);
                _status.Text = Summary() + "  Refined. Accept, edit, or refine again.";
            };
            worker.RunWorkerAsync();
        }

        /// <summary>Reviews the selected proposal's paragraph again (lint + AI) and replaces its proposals.</summary>
        private void Reanalyze()
        {
            var f = Current();
            if (f == null || f.DocumentPath != null) { _status.Text = "Re-analyze works on the topic's paragraphs."; return; }
            var block = _runner.LocateBlock(_doc, f) ?? f.Block;
            if (block == null || block.ParentNode == null) { _status.Text = "That paragraph is gone. Re-check the topic."; return; }
            var blocks = new List<XmlNode> { block };
            Busy(true, "Re-analyzing the paragraph…");
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
                if (e.Error != null) { Diagnostics.Log("Re-analyze", e.Error); _status.Text = "Re-analyze failed: " + e.Error.Message; return; }
                var parts = (object[])e.Result;
                var lint = (List<StyleFinding>)parts[0];
                var fresh = new List<StyleFinding>(lint);
                if (parts[2] != null) fresh.AddRange(_runner.AnchorReview((List<StyleFinding>)parts[1], (ReviewUnit)parts[2], lint));
                _findings.RemoveAll(x => !x.IsFixed && x.DocumentPath == null && (x.Block == block || (x.IsStale && x.Block == f.Block)));
                Add(fresh);
                Select(fresh.Count > 0 ? fresh[0] : null);
                _status.Text = Summary() + "  Paragraph re-analyzed: " + fresh.Count + " proposal(s).";
            };
            worker.RunWorkerAsync();
        }

        private void RewriteParagraph(StyleFinding f)
        {
            if (f.DocumentPath != null) { _status.Text = "Open the snippet and use Rewrite selection there."; return; }
            var block = _runner.LocateBlock(_doc, f);
            if (block == null) { _status.Text = "That text is no longer in the topic. Re-analyze or Re-check."; return; }
            var related = _findings.FindAll(x => !x.IsFixed && x.DocumentPath == null && _runner.LocateBlock(_doc, x) == block);
            var instruction = "Resolve these review findings and change nothing else:\n" + ActionRunner.Summarize(related);
            string applied;
            using (var form = new ResultForm("Rewrite paragraph", _newRunner(), _doc, ActionKind.Ask, instruction, new List<XmlNode> { block }))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                applied = form.AppliedSummary;
            }
            foreach (var x in related) { x.IsFixed = true; FeedbackLog.Record(x, _doc.SourcePath, "accepted", "rewrite"); }
            Rebuild();
            _status.Text = Summary() + "  " + applied;
            Next();
        }

        private void IgnoreRule()
        {
            var f = Current();
            if (f == null) { _status.Text = "Select a proposal whose rule you want to turn off."; return; }
            var team = Team.Current;
            if (team != null && !team.IsOwner)
            {
                // Strict ownership: writers propose, the style-guide owner decides (Tune style guide shows the requests).
                var why = TextPrompt.Ask(this, "Propose turning off a rule", "Ask the style guide owner (" + string.Join(", ", new List<string>(team.Owners).ToArray()) + ") to turn off \"" + f.Rule + "\". Why?", "", true);
                if (why == null) return;
                FeedbackLog.Record(f, _doc.SourcePath, "propose-ignore", why.Trim());
                _status.Text = Summary() + "  Sent to the style guide owner" + (team.FeedbackFolder == null ? " (set a team feedback folder in Settings so they receive it)." : ".");
                return;
            }
            // Owners (or a writer with no team): turn the rule off or change its level, for the team or this project.
            var rule = _runner.RuleFor(f.RuleId);
            var guide = team != null ? team.GuideRoot : null;
            var described = rule != null ? (rule.Layer == "google" ? "Google" : "Company") + " · " + rule.Section + ": " + rule.Text : "\"" + f.Rule + "\" (" + (f.Layer == "ai" ? "AI judgment" : f.Layer) + ")";
            using (var form = new FlareAI.Flare.UI.RuleSettingForm(described, rule != null && guide != null))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string where;
                    if (form.ForTeam) where = FlareAI.Core.Style.GuideOverrides.Set(guide, rule.Id, form.Setting, form.Reason);
                    else { ProjectRules.Ignore(f.DocumentPath ?? _doc.SourcePath, f, form.Setting); where = ProjectRules.OverridesPath(f.DocumentPath ?? _doc.SourcePath); }
                    FeedbackLog.Record(f, _doc.SourcePath, form.Setting == "off" ? "rule ignored" : "rule changed", form.Reason);
                    if (form.Setting == "off")
                    {
                        if (f.Layer == "ai" && f.RuleId == null) _ignoredAi.Add(f.Rule);
                        _findings.RemoveAll(x => f.RuleId != null ? x.RuleId == f.RuleId : x.Rule == f.Rule);
                        Rebuild();
                    }
                    _status.Text = Summary() + "  Rule set to " + form.Setting + (form.ForTeam ? " for the team (in " + System.IO.Path.GetFileName(where) + "; commit it so everyone gets it)." : " for this project.");
                }
                catch (Exception ex) { Diagnostics.Log("Change rule", ex); _status.Text = "Could not change the rule: " + ex.Message; }
            }
        }

        // ---- navigation --------------------------------------------------------------------------

        /// <summary>Selects the next open proposal after the current one (wrapping), or clears the card.</summary>
        private void Next()
        {
            var cur = Current();
            Select(NextAfter(cur));
        }

        private StyleFinding NextAfter(StyleFinding cur)
        {
            var open = Open();
            if (open.Count == 0) return null;
            var start = cur == null ? -1 : _findings.IndexOf(cur);
            for (var i = 1; i <= _findings.Count; i++)
            {
                var f = _findings[(start + i + _findings.Count) % _findings.Count];
                if (f != cur && !f.IsFixed && !f.IsStale) return f;
            }
            return null;
        }

        private void Select(StyleFinding f)
        {
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

        /// <summary>The document a finding lives in: the topic, or its snippet (opened in Flare on demand).</summary>
        private IDocumentAdapter DocOf(StyleFinding f, bool open)
        {
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
            var doc = DocOf(f, report);
            if (doc == null) { if (report) _status.Text = "Could not open " + f.DocumentPath; return; }
            var ok = false;
            try { ok = _runner.GoTo(doc, f); } catch (Exception ex) { Diagnostics.Log("GoTo", ex); }
            if (!ok && report) _status.Text = "That text is no longer in the topic (it was edited). Re-analyze to refresh.";
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

        private static StyleFinding Clone(StyleFinding f)
        {
            return new StyleFinding
            {
                Source = f.Source, Rule = f.Rule, Severity = f.Severity, Excerpt = f.Excerpt, Suggestion = f.Suggestion, Replacement = f.Replacement,
                Before = f.Before, Block = f.Block, Layer = f.Layer, Link = f.Link, RuleId = f.RuleId, RuleLayer = f.RuleLayer, RuleSection = f.RuleSection, RuleText = f.RuleText,
                DocumentPath = f.DocumentPath, Variable = f.Variable, Section = f.Section, Order = f.Order
            };
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
            _accept.Text = f == null ? "Accept" : f.Rule == "Structure.AltText" && f.DocumentPath == null ? "Suggest alt text…" : f.Kind == "restructure" ? "Rewrite…" : f.Kind == "variable" || f.Kind == "manual" ? "Go to" : "Accept";
            _reject.Enabled = f != null && !f.IsFixed;
            _edit.Enabled = open && f.Kind != "variable" && f.Kind != "manual";
            _refine.Enabled = open && !_reviewing;
            _reanalyze.Enabled = f != null && f.DocumentPath == null && !_reviewing && _files == null;
            _rule.Enabled = f != null && !string.IsNullOrEmpty(f.Link);
            var mech = _findings.FindAll(x => x.CanFix && x.Kind == "mechanical" && (x.DocumentPath == null || _files != null)).Count;
            _acceptAll.Text = "Accept all mechanical (" + mech + ")";
            _acceptAll.Enabled = mech > 0;
            var exact = _findings.FindAll(x => x.CanFix && (x.Kind == "mechanical" || x.Kind == "wording") && x.DocumentPath == null).Count;
            _bulk.Text = "Propose all as tracked changes (" + exact + ")";
            _bulk.Enabled = exact > 0 && _files == null;
            _recheck.Enabled = !_reviewing;
            _ignore.Enabled = f != null;
        }
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
