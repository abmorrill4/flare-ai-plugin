using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Turned-off rules" (from Tune style guide): every rule the style guide or the open project turns off or changes,
    /// with who, when and why, and Turn back on. The bundled guide's own changes are listed as "Default": turning one back
    /// on writes an "on" row into the writer's own guide (removing that row returns to the default). Only style guide
    /// owners can turn rules back on when the guide has an owners file.
    /// </summary>
    internal sealed class TurnedOffRulesForm : Form
    {
        private readonly Team _team;
        private readonly IStyleService _style;
        private readonly string _project;
        private readonly Func<IList<string>> _guideRoots;
        private readonly Func<string, bool> _isDefault;
        private readonly string _openProject;
        private readonly Label _head;
        private readonly ListView _list;
        private readonly Button _turnOn;

        public TurnedOffRulesForm(Team team, IStyleService style, string project, Func<IList<string>> guideRoots, string openProject, Func<string, bool> isDefault = null)
        {
            _team = team; _style = style; _project = project; _guideRoots = guideRoots; _openProject = openProject; _isDefault = isDefault;
            Text = "House Style — Turned-off rules";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 440);
            MinimumSize = new Size(560, 300);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            _head = new LiveLabel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 0) };
            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
            foreach (var c in new[] { "Rule", "Source", "Applies to", "Setting", "Changed by", "Date", "Note" }) _list.Columns.Add(c);
            _list.Columns[0].Width = 280; _list.Columns[1].Width = 70; _list.Columns[2].Width = 140; _list.Columns[3].Width = 60;
            _list.Columns[4].Width = 120; _list.Columns[5].Width = 80; _list.Columns[6].Width = 200;
            _list.SelectedIndexChanged += (s, e) => UpdateButton();
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            _turnOn = new Button { Text = "Turn back on", AutoSize = true, Enabled = false };
            _turnOn.Click += (s, e) => TurnBackOn();
            bar.Controls.AddRange(new Control[] { close, _turnOn });
            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(_head);
            CancelButton = close;
            A11y.Name(_list, "Turned-off rules", "Columns: rule, source, applies to, setting, changed by, date, note.");
            A11y.Name(_turnOn, "Turn back on", "Turns the selected rule back on (or, for a rule you turned back on, returns it to the default) from the next check.");
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);
            Shown += (s, e) => Reload(null);
        }

        private bool CanChange { get { return _team == null || _team.IsOwner; } }

        private void UpdateButton()
        {
            var o = Current();
            _turnOn.Text = o != null && o.IsTurnOn ? "Use the default" : "Turn back on";
            _turnOn.Enabled = CanChange && o != null && (!o.IsDefault || _team != null);
        }

        private RuleOverride Current() { return _list.SelectedItems.Count == 1 ? (RuleOverride)_list.SelectedItems[0].Tag : null; }

        private void Reload(string done)
        {
            _list.Items.Clear();
            var entries = new List<RuleOverride>();
            var rules = new Dictionary<string, GuideRule>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var roots = _guideRoots == null ? new List<string>() : new List<string>(_guideRoots());
                entries.AddRange(RuleOverrides.InEffect(roots, _isDefault, root => _team != null && _team.IsPersonal ? "My style guide" : "Team style guide (" + Path.GetFileName(root.TrimEnd('\\')) + ")"));
                var project = StyleEngine.FlareProjectRoot(_openProject);
                if (project != null && !roots.Any(r => string.Equals(Path.GetFullPath(r).TrimEnd('\\'), project.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)))
                    entries.AddRange(RuleOverrides.Read(project, "Project " + Path.GetFileName(project)));
                if (_style != null) foreach (var r in _style.Rules(_project)) rules[r.Id] = r;
            }
            catch (Exception ex) { Diagnostics.Log("turned-off rules", ex); _head.Text = "Could not read the turned-off rules: " + ex.Message; return; }

            foreach (var o in entries)
            {
                GuideRule rule;
                var name = rules.TryGetValue(o.RuleId, out rule) ? rule.Section + ": " + rule.Text : o.RuleId + (o.IsAiName ? " (AI judgment)" : "");
                var item = new ListViewItem(new[] { name, rule != null ? Cap(rule.Layer) : LayerOf(o), o.Scope, Severity.Label(o.Setting ?? ""), o.By, o.Date, o.Note }) { Tag = o, ToolTipText = o.RuleId + " · " + o.File };
                _list.Items.Add(item);
            }
            _list.ShowItemToolTips = true;
            var role = CanChange ? "" : "  Read-only: only the style guide owners (" + string.Join(", ", _team.Owners.ToArray()) + ") can turn rules back on.";
            _head.Text = (done != null ? done + "  " : "") +
                         (entries.Count == 0 ? "No rules are turned off or changed." : Plural.Of(entries.Count, "rule") + " turned off or changed (Default: shipped with House Style). Select one and choose Turn back on to apply it again.") + role;
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            UpdateButton();
        }

        private void TurnBackOn()
        {
            var o = Current();
            if (o == null) return;
            try
            {
                var file = RuleOverrides.TurnBackOn(o, _team);
                Reload((o.IsTurnOn ? "Back to the default: " : "Turned back on: ") + o.RuleId + " (in " + file + "). It applies from the next check.");
            }
            catch (Exception ex)
            {
                Diagnostics.Log("turn rule back on", ex);
                Notify.Error(this, "House Style couldn't turn " + o.RuleId + " back on.", ex.Message + "\n\nNothing was changed. File: " + o.File, ex, "Turned-off rules");
            }
        }

        private static string LayerOf(RuleOverride o)
        {
            if (o.IsAiName) return "AI";
            var cut = o.RuleId.IndexOfAny(new[] { ':', '.' });
            return cut > 0 ? Cap(o.RuleId.Substring(0, cut)) : "Google";   // a bare name is a Google rule
        }

        private static string Cap(string s) { return string.IsNullOrEmpty(s) ? "" : char.ToUpperInvariant(s[0]) + s.Substring(1); }
    }
}
