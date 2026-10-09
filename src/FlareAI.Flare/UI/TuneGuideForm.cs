using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using FlareAI.Core.Style;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Tune style guide": what the team's review decisions say about each rule, for the style guide owner. Pools every
    /// writer's feedback file, then lists disputed rules (writers split), often-rejected rules, repeated hand edits
    /// (candidate Avoid/Use rows), requests to turn rules off, and rules that work. Each item shows its evidence;
    /// Open rule takes the owner to the rule in the guide, where changes are made as tracked changes.
    /// </summary>
    public sealed class TuneGuideForm : Form
    {
        private readonly Team _team;
        private readonly IStyleService _style;
        private readonly string _project;
        private readonly Func<string, IDocumentAdapter> _open;
        private readonly Func<IDocumentAdapter, ActionRunner> _runnerFor;
        private readonly Button _draft, _prompts;
        private readonly Label _head;
        private readonly ListView _list;
        private readonly RichTextBox _detail;
        private readonly Button _openRule;
        private Dictionary<string, GuideRule> _rules = new Dictionary<string, GuideRule>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string> Groups = new Dictionary<string, string>
        {
            { "disputed", "Writers disagree — decide, reword, or scope the rule" },
            { "review", "Often rejected — reword, demote to prose, or drop" },
            { "edit", "Repeated hand edits — the replacement is probably wrong" },
            { "ignore", "Requests to turn a rule off" },
            { "working", "Working well" }
        };

        /// <param name="runnerFor">An action runner for a style-guide topic opened in Flare (drafts guide edits).</param>
        public TuneGuideForm(Team team, IStyleService style, string project, Func<string, IDocumentAdapter> open, Func<IDocumentAdapter, ActionRunner> runnerFor)
        {
            _team = team; _style = style; _project = project; _open = open; _runnerFor = runnerFor;
            Text = "House Style — Tune style guide";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 600);
            MinimumSize = new Size(600, 400);
            Font = SystemFonts.MessageBoxFont;

            _head = new Label { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 8, 8, 0), Text = "Reading the team's review decisions…" };
            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            _list.Columns.Add("Item", 420); _list.Columns.Add("Accepted", 70); _list.Columns.Add("Rejected", 70); _list.Columns.Add("Writers", 200);
            foreach (var g in Groups) _list.Groups.Add(g.Key, g.Value);
            _list.SelectedIndexChanged += (s, e) => ShowDetail();
            _detail = DiffView.Create();
            _detail.Dock = DockStyle.Bottom; _detail.Height = 170; _detail.BackColor = SystemColors.Control;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var refresh = new Button { Text = "Refresh", AutoSize = true };
            _openRule = new Button { Text = "Open rule in the guide", AutoSize = true, Enabled = false };
            _draft = new Button { Text = "Draft a change with AI…", AutoSize = true, Enabled = false };
            _prompts = new Button { Text = "Customize AI prompts…", AutoSize = true, Enabled = IsOwner };
            refresh.Click += (s, e) => Load();
            _openRule.Click += (s, e) => OpenRule();
            _draft.Click += (s, e) => DraftChange();
            _prompts.Click += (s, e) => CustomizePrompts();
            bar.Controls.AddRange(new Control[] { close, refresh, _openRule, _draft, _prompts });
            var tips = new ToolTip();
            tips.SetToolTip(_draft, "The AI rewrites this rule in the guide from the team's evidence; you review the change and apply it as tracked changes (owners only).");
            tips.SetToolTip(_prompts, @"Copy the AI's instructions into the guide (FlareAI\prompts) so the team can tune them, versioned with the rules (owners only).");
            Controls.Add(_list);
            Controls.Add(_detail);
            Controls.Add(bar);
            Controls.Add(_head);
            CancelButton = close;
            Shown += (s, e) => Load();
        }

        private new void Load()
        {
            _list.Items.Clear(); _detail.Clear();
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) =>
            {
                var files = new List<string>(FeedbackStats.FilesIn(_team == null ? null : _team.FeedbackFolder));
                if (files.Count == 0 && FeedbackLog.PersonalPath != null) files.Add(FeedbackLog.PersonalPath);
                var decisions = FeedbackStats.Load(files);
                var rules = new Dictionary<string, GuideRule>(StringComparer.OrdinalIgnoreCase);
                if (_style != null) try { foreach (var r in _style.Rules(_project)) rules[r.Id] = r; } catch (Exception ex) { Diagnostics.Log("tune: rules", ex); }
                e.Result = new object[] { decisions, FeedbackStats.Analyze(decisions), rules };
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                if (e.Error != null) { Diagnostics.Log("Tune style guide", e.Error); _head.Text = "Could not read feedback: " + e.Error.Message; return; }
                var parts = (object[])e.Result;
                var decisions = (List<Decision>)parts[0];
                var items = (List<TuneItem>)parts[1];
                _rules = (Dictionary<string, GuideRule>)parts[2];
                Header(decisions, items);
                foreach (var it in items)
                {
                    var row = new ListViewItem(new[] { it.Title, it.Accepted.ToString(), it.Rejected.ToString(), string.Join(", ", it.Writers.ToArray()) })
                    { Tag = it, Group = _list.Groups[it.Kind] };
                    _list.Items.Add(row);
                }
                if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            };
            worker.RunWorkerAsync();
        }

        private void Header(List<Decision> decisions, List<TuneItem> items)
        {
            var writers = decisions.Select(d => d.Writer).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var dated = decisions.Where(d => d.At > DateTime.MinValue).ToList();
            var range = dated.Count == 0 ? "" : " (" + dated.Min(d => d.At).ToLocalTime().ToString("d") + " – " + dated.Max(d => d.At).ToLocalTime().ToString("d") + ")";
            var source = _team == null || _team.FeedbackFolder == null ? "your own decisions only — set a team feedback folder in Settings to pool everyone's" : _team.FeedbackFolder;
            var role = _team == null || !_team.IsStrict ? "No owners file: anyone can change the guide."
                     : _team.IsOwner ? "You are a style guide owner." : "Read-only: the owners are " + string.Join(", ", _team.Owners.ToArray()) + ".";
            var actionable = items.Count(i => i.Kind != "working");
            _head.Text = decisions.Count + " decision(s) from " + writers + " writer(s)" + range + ". Source: " + source + ".\n"
                         + (actionable == 0 ? "Nothing needs attention yet. " : actionable + " item(s) to look at. ") + role
                         + (_team != null && _team.Status != null ? "  " + _team.Status : "");
        }

        private TuneItem Current() { return _list.SelectedItems.Count == 1 ? (TuneItem)_list.SelectedItems[0].Tag : null; }

        private void ShowDetail()
        {
            var it = Current();
            _detail.Clear();
            if (it == null) { _openRule.Enabled = false; return; }
            GuideRule rule;
            var known = _rules.TryGetValue(it.Rule ?? "", out rule);
            Write(it.Title, FontStyle.Bold);
            if (known) Write("Rule " + rule.Id + " (" + rule.Layer + " · " + rule.Section + "): " + rule.Text, FontStyle.Italic);
            foreach (var line in it.Evidence) Write("• " + line, FontStyle.Regular);
            Write(Advice(it.Kind), FontStyle.Italic);
            _detail.Select(0, 0);
            _openRule.Enabled = known && !string.IsNullOrEmpty(rule.Link);
            _draft.Enabled = _openRule.Enabled && IsOwner && it.Kind != "working" && rule.Link.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsOwner { get { return _team == null || _team.IsOwner; } }

        /// <summary>Opens the rule's topic, finds the rule's bullet or row, and has the AI draft the change from the evidence.</summary>
        private void DraftChange()
        {
            var it = Current();
            GuideRule rule;
            if (it == null || !_rules.TryGetValue(it.Rule ?? "", out rule)) return;
            try
            {
                var doc = _open == null ? null : _open(new Uri(rule.Link).LocalPath);
                if (doc == null) { MessageBox.Show(this, "Could not open " + rule.Link, "House Style"); return; }
                var block = GuideEdits.Locate(doc.Document, rule.Text);
                if (block == null) { MessageBox.Show(this, "Could not find this rule in " + System.IO.Path.GetFileName(doc.SourcePath) + ". Open it and edit the rule there.", "House Style"); return; }
                var runner = _runnerFor == null ? null : _runnerFor(doc);
                if (runner == null) return;
                var instruction = GuideEdits.Instruction(it, rule, block.LocalName == "tr");
                using (var form = new ResultForm("Draft style guide change", runner, doc, ActionKind.Ask, instruction, new List<System.Xml.XmlNode> { block }) { Lint = false, Task = "draft" })
                {
                    if (form.ShowDialog(this) != DialogResult.OK) return;
                    _head.Text = form.AppliedSummary + " In " + System.IO.Path.GetFileName(doc.SourcePath) + ": review, save and commit the guide, then run Validate style guide.";
                }
            }
            catch (Exception ex) { Diagnostics.Log("tune: draft", ex); MessageBox.Show(this, ex.Message, "House Style — Draft change"); }
        }

        /// <summary>Seeds the guide's FlareAI\prompts folder with the built-in templates (missing ones only) and opens it.</summary>
        private void CustomizePrompts()
        {
            if (_team == null || _team.GuideRoot == null) return;
            try
            {
                var dir = System.IO.Path.Combine(_team.GuideRoot, System.IO.Path.Combine("FlareAI", "prompts"));
                System.IO.Directory.CreateDirectory(dir);
                var added = 0;
                foreach (var name in PromptBuilder.Templates)
                {
                    var file = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(file)) continue;
                    System.IO.File.WriteAllText(file, PromptBuilder.BuiltIn(name), new System.Text.UTF8Encoding(false));
                    added++;
                }
                var readme = System.IO.Path.Combine(dir, "README.txt");
                if (!System.IO.File.Exists(readme))
                    System.IO.File.WriteAllText(readme, "The AI's instructions. A file here replaces the built-in template of the same name for every writer\r\n" +
                        "who uses this style guide. Keep the {{PLACEHOLDERS}}. Delete a file to go back to the built-in version.\r\n" +
                        "Commit changes like any rule change; run Validate style guide > Check prose rules with AI afterwards.\r\n", new System.Text.UTF8Encoding(false));
                _head.Text = (added > 0 ? "Copied " + added + " prompt template(s) to " : "Prompt templates are in ") + dir + ". Edit, then commit with the guide.";
                System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch (Exception ex) { Diagnostics.Log("tune: prompts", ex); MessageBox.Show(this, ex.Message, "House Style — Customize AI prompts"); }
        }

        private static string Advice(string kind)
        {
            switch (kind)
            {
                case "disputed": return "Most splits are about scope: the rule is right for some content and wrong for other. Scope it (a project layer or a separate section), or reword it to say when it applies.";
                case "review": return "Read the rejected excerpts: if the rule is wrong there, narrow its wording or make it a prose rule with an example of the exception.";
                case "edit": return "Writers agree on a better replacement: update the rule's Use column (or add an Avoid/Use row), then run Validate style guide.";
                case "ignore": return "Decide for the team: turn the rule off (Google overrides table), scope it, or answer the writers.";
                default: return "";
            }
        }

        private void Write(string text, FontStyle style)
        {
            if (string.IsNullOrEmpty(text)) return;
            _detail.SelectionStart = _detail.TextLength;
            _detail.SelectionFont = new Font(_detail.Font, style);
            _detail.AppendText((_detail.TextLength > 0 ? "\n" : "") + text);
        }

        private void OpenRule()
        {
            var it = Current();
            GuideRule rule;
            if (it == null || !_rules.TryGetValue(it.Rule ?? "", out rule) || string.IsNullOrEmpty(rule.Link)) return;
            try
            {
                if (rule.Link.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                {
                    var path = new Uri(rule.Link).LocalPath;
                    if (_open != null && _open(path) != null) { Close(); return; }
                }
                else if (rule.Link.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) System.Diagnostics.Process.Start(rule.Link);
            }
            catch (Exception ex) { Diagnostics.Log("tune: open rule", ex); MessageBox.Show(this, "Could not open " + rule.Link + ": " + ex.Message, "House Style"); }
        }
    }
}
