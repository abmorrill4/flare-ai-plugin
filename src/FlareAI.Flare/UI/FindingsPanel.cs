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
        private readonly Button _acceptAll, _recheck, _ignore;
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

        /// <summary>"FlareAI — topic.htm" — for the host's title.</summary>
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
        /// <param name="cache">Shared AI review cache (same content = same review, across topics, renames and sessions).</param>
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
            MinimumSize = new Size(260, 320);

            _status = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 36, Text = "Linting…" };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee };
            var triage = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            _acceptAll = Btn(triage, "Accept all mechanical", (s, e) => AcceptAllMechanical());

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
            _ignore = Btn(bar, "Ignore rule…", (s, e) => IgnoreRule());
            Btn(bar, "Close", (s, e) => { var h = CloseRequested; if (h != null) h(this, EventArgs.Empty); });

            var tips = new ToolTip();
            tips.SetToolTip(_accept, "Apply this proposal as a tracked change and go to the next one (Enter or double-click).");
            tips.SetToolTip(_reject, "Not applying it: say why, so the guide and the AI can be tuned (Delete).");
            tips.SetToolTip(_edit, "Change the proposed text yourself, then apply it.");
            tips.SetToolTip(_refine, "Ask the AI again for this one proposal, with your instruction (\"keep SSO\", \"shorter\").");
            tips.SetToolTip(_reanalyze, "Review this paragraph again (after you edited it).");
            tips.SetToolTip(_acceptAll, "Apply every exact rule fix in this topic as one tracked change (one Ctrl+Z undoes it).");
            tips.SetToolTip(_ignore, "Turn the selected rule off for this whole project (written to the project's StyleGuide folder, shared with your team).");

            Controls.Add(split);      // Fill must be added first so docked bars take their space
            Controls.Add(bar);
            Controls.Add(triage);
            Controls.Add(_progress);
            Controls.Add(_status);
            HandleDestroyed += (s, e) => { if (_worker != null && _worker.IsBusy) _worker.CancelAsync(); };
            ShowCard();
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
            _reviewing = true; _progress.Visible = true; _progress.Style = ProgressBarStyle.Marquee; _status.Text = "Linting…";
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
                // Long topics are reviewed section by section, so the first cards arrive in seconds.
                var sections = ReviewOrder.Sections(blocks);
                var prompts = new List<string>();
                foreach (var sec in sections) prompts.Add(_runner.ReviewPrompt(sec, lint.FindAll(f => f.DocumentPath == null && sec.Exists(b => b == f.Block || FlareAI.Core.Style.TextRuns.BlockOf(b) == f.Block))));
                _lint = lint;
                _worker.ReportProgress(0, new object[] { lint, null, 0, sections.Count });  // from here the author may edit while we wait
                for (var i = 0; i < sections.Count; i++)
                {
                    if (_worker.CancellationPending) { e.Cancel = true; return; }
                    var raw = _cache == null ? null : _cache.Get(prompts[i]);
                    if (raw != null) cachedAny = true;
                    else if (_aiOnStart)
                    {
                        raw = _runner.CallReview(prompts[i]);
                        if (_cache != null) _cache.Put(prompts[i], raw);
                    }
                    if (raw != null) _worker.ReportProgress(i + 1, new object[] { raw, sections[i], i + 1, sections.Count });
                }
            };
            _worker.ProgressChanged += (s, e) =>
            {
                if (IsDisposed) return;
                var p = (object[])e.UserState;
                var found = (List<StyleFinding>)p[0];
                var section = (List<XmlNode>)p[1];
                int done = (int)p[2], total = (int)p[3];
                Add(section == null ? found : _runner.AnchorReview(found, section, _lint)); // DOM work on the UI thread
                if (_aiOnStart && done < total)
                    _status.Text = Summary() + "  Reviewing with AI" + (total > 1 ? " — section " + (done + 1) + " of " + total : "") + "… (you can work on these now)";
                else _status.Text = Summary();
            };
            _worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed || e.Cancelled) return;
                _reviewing = false; _progress.Visible = false;
                if (e.Error != null)
                {
                    Diagnostics.Log("Check style", e.Error);
                    _status.Text = Summary() + "  AI review failed: " + e.Error.Message;
                }
                else if (!_aiOnStart && !cachedAny) _status.Text = Summary() + "  Linter only — click Re-check for the AI review.";
                else _status.Text = Open().Count == 0 ? "No open proposals." : Summary();
                if (_lintNote != null) _status.Text += "  " + _lintNote;
                var team = Team.Current;
                if (team != null && team.Status != null && team.Status.Behind > 0) _status.Text += "  " + team.Status;
                if (_list.SelectedItems.Count == 0) Next();
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
                var item = new ListViewItem(new[] { ProposalText(f), LayerLabel(f) }) { Tag = f, Group = GroupFor(f) };
                item.ToolTipText = LayerLabel(f) + " · " + f.Rule + "\n" + f.Suggestion;
                Restyle(item);
                _list.Items.Add(item);
                if (f == keep) item.Selected = true;
            }
            _list.EndUpdate();
            UpdateButtons();
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
            var where = f.DocumentPath == null ? "" : "In snippet " + System.IO.Path.GetFileNameWithoutExtension(f.DocumentPath);
            int uses;
            if (f.DocumentPath != null && _usage.TryGetValue(f.DocumentPath, out uses)) where += " — changes " + uses + " file(s) that use it";
            _cardHead.Text = LayerLabel(f) + "  ·  " + KindLabel(f) + (f.IsFixed ? "  ·  accepted" : f.IsStale ? "  ·  text changed — Re-analyze" : "")
                             + (where.Length > 0 ? "\n" + where : "");

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
            if (rule != null) Line(_cardWhy, "Rule " + rule.Id + ": " + rule.Text, FontStyle.Italic);
            var ex = _runner.ExampleFor(f.RuleId);
            if (ex != null)
            {
                if (!string.IsNullOrEmpty(ex.Bad)) Line(_cardWhy, "✗ " + ex.Bad, FontStyle.Regular);
                if (!string.IsNullOrEmpty(ex.Good)) Line(_cardWhy, "✓ " + ex.Good, FontStyle.Regular);
            }
            if (rule == null && f.Source == "ai") Line(_cardWhy, "General editing judgment (not tied to a guide rule).", FontStyle.Italic);
            _cardDiff.Select(0, 0); _cardWhy.Select(0, 0);
            UpdateButtons();
        }

        private static void Line(RichTextBox box, string text, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            box.SelectionStart = box.TextLength;
            box.SelectionFont = new Font(box.Font, style);
            box.SelectionColor = SystemColors.ControlText;
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
            if (f.IsStale) { _status.Text = "That text changed since the review. Re-analyze the paragraph."; return; }
            if (f.Kind == "variable")
            {
                GoTo(f, true);
                _status.Text = "The text is selected: use Insert ▸ Variable ▸ " + f.Variable + " (Flare replaces the selection).";
                return;
            }
            if (f.Kind == "restructure") { RewriteParagraph(f); return; }
            if (f.Kind == "manual")
            {
                GoTo(f, true);
                _status.Text = "Fix it in the editor (" + f.Suggestion + "), then Re-analyze.";
                return;
            }
            if (Apply(new List<StyleFinding> { f }, "FlareAI: " + f.Rule) > 0)
            {
                FeedbackLog.Record(f, _doc.SourcePath, "accepted");
                Next();
            }
        }

        private void AcceptAllMechanical()
        {
            var todo = _findings.FindAll(f => f.CanFix && f.Kind == "mechanical" && (f.DocumentPath == null || _files != null));
            if (todo.Count == 0) return;
            var files = GroupByDocument(todo).Count;
            if (files > 3 && MessageBox.Show(this, "Apply " + todo.Count + " fixes in " + files + " files?\n\nEach file opens in Flare and gets the fixes as tracked changes (one undo step per file). Nothing is saved automatically.",
                    "FlareAI", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var n = Apply(todo, todo.Count == 1 ? "FlareAI: " + todo[0].Rule : "FlareAI: " + todo.Count + " style fixes");
            foreach (var f in todo) if (f.IsFixed) FeedbackLog.Record(f, _doc.SourcePath, "accepted");
            var inSnippets = _findings.FindAll(f => f.CanFix && f.DocumentPath != null && _files == null).Count;
            if (inSnippets > 0) _status.Text += "  " + inSnippets + " more in snippets: select one to apply it there.";
            Next();
        }

        /// <summary>
        /// Applies exact replacements as tracked changes, one undo step per document. Snippet fixes open the snippet
        /// (that is where they must be tracked), then the topic is brought back to the front.
        /// </summary>
        private int Apply(List<StyleFinding> todo, string operation)
        {
            var n = 0;
            try
            {
                foreach (var group in GroupByDocument(todo))
                {
                    var doc = DocOf(group[0], true);
                    if (doc == null) { Diagnostics.Log("Apply: could not open " + group[0].DocumentPath); continue; }
                    n += _runner.ApplyFixes(doc, group, operation);
                }
                if (_files == null && todo.Exists(f => f.DocumentPath != null) && _open != null) _open(_doc.SourcePath); // back to the topic
                Rebuild();
                _status.Text = Summary() + "  Applied " + n + " as tracked change(s)" + (n < todo.Count ? "; " + (todo.Count - n) + " skipped because the text changed." : ".") + " Ctrl+Z undoes them.";
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Apply", ex);
                _status.Text = "Could not apply: " + ex.Message;
            }
            return n;
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
            if (Apply(new List<StyleFinding> { edited }, "FlareAI: " + f.Rule + " (edited)") > 0)
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
                var raw = _runner.CallReview(_runner.ReviewPrompt(blocks, lint));
                e.Result = new object[] { lint, raw };
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                Busy(false, null);
                if (e.Error != null) { Diagnostics.Log("Re-analyze", e.Error); _status.Text = "Re-analyze failed: " + e.Error.Message; return; }
                var parts = (object[])e.Result;
                var lint = (List<StyleFinding>)parts[0];
                var fresh = new List<StyleFinding>(lint);
                fresh.AddRange(_runner.AnchorReview((List<StyleFinding>)parts[1], blocks, lint));
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
            var file = ProjectRules.OverridesPath(f.DocumentPath ?? _doc.SourcePath);
            if (file == null) { _status.Text = "This topic is not inside a Flare project."; return; }
            if (MessageBox.Show(this, "Turn off \"" + f.Rule + "\" for this whole project?\n\nThis writes to\n" + file +
                    "\nwhich is shared with your team through source control. Delete the line to turn the rule back on.",
                    "FlareAI — Ignore rule", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            try
            {
                ProjectRules.Ignore(f.DocumentPath ?? _doc.SourcePath, f);
                if (f.Layer == "ai") _ignoredAi.Add(f.Rule);
                FeedbackLog.Record(f, _doc.SourcePath, "rule ignored");
                _findings.RemoveAll(x => x.Rule == f.Rule);
                Rebuild();
                _status.Text = Summary() + "  \"" + f.Rule + "\" is off for this project.";
            }
            catch (Exception ex) { Diagnostics.Log("Ignore rule", ex); _status.Text = "Could not write " + file + ": " + ex.Message; }
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
                Before = f.Before, Block = f.Block, Layer = f.Layer, Link = f.Link, RuleId = f.RuleId, RuleLayer = f.RuleLayer, RuleSection = f.RuleSection,
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
            _accept.Text = f == null ? "Accept" : f.Kind == "restructure" ? "Rewrite…" : f.Kind == "variable" || f.Kind == "manual" ? "Go to" : "Accept";
            _reject.Enabled = f != null && !f.IsFixed;
            _edit.Enabled = open && f.Kind != "variable" && f.Kind != "manual";
            _refine.Enabled = open && !_reviewing;
            _reanalyze.Enabled = f != null && f.DocumentPath == null && !_reviewing && _files == null;
            _rule.Enabled = f != null && !string.IsNullOrEmpty(f.Link);
            var mech = _findings.FindAll(x => x.CanFix && x.Kind == "mechanical" && (x.DocumentPath == null || _files != null)).Count;
            _acceptAll.Text = "Accept all mechanical (" + mech + ")";
            _acceptAll.Enabled = mech > 0;
            _recheck.Enabled = !_reviewing;
            _ignore.Enabled = f != null;
        }
    }

    /// <summary>A one-field text prompt (Edit / Refine).</summary>
    internal static class TextPrompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string initial, bool multiline)
        {
            using (var form = new Form { Text = "FlareAI — " + title, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ClientSize = new Size(520, multiline ? 190 : 150), Font = SystemFonts.MessageBoxFont })
            {
                var text = new Label { Text = label, Left = 12, Top = 12, Width = 496, Height = 34 };
                var box = new TextBox { Left = 12, Top = 50, Width = 496, Height = multiline ? 84 : 24, Multiline = multiline, AcceptsReturn = false, Text = initial ?? "", ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
                var ok = new Button { Text = "OK", Left = 352, Top = form.ClientSize.Height - 36, Width = 75, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Left = 433, Top = form.ClientSize.Height - 36, Width = 75, DialogResult = DialogResult.Cancel };
                form.Controls.AddRange(new Control[] { text, box, ok, cancel });
                form.AcceptButton = ok; form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
            }
        }
    }
}
