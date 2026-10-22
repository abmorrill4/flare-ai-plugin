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
    /// on writes an "on" row into the writer's guide (removing that row returns to the default). When the guide has an
    /// owners file and is read-only for this writer, turning a guide rule (or a default) back on becomes a request to the
    /// owners; a personal guide, and a project's own exceptions, change directly. Exceptions an owner approved from a
    /// request are flagged.
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
        private readonly HashSet<RuleOverride> _guideEntries = new HashSet<RuleOverride>();

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
            foreach (var c in new[] { "Rule", "Source", "Applies to", "Setting", "Changed by", "Date", "Note", "Request" }) _list.Columns.Add(c);
            _list.Columns[0].Width = 280; _list.Columns[1].Width = 70; _list.Columns[2].Width = 140; _list.Columns[3].Width = 60;
            _list.Columns[4].Width = 120; _list.Columns[5].Width = 80; _list.Columns[6].Width = 200; _list.Columns[7].Width = 120;
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
            A11y.Name(_list, "Turned-off rules", "Columns: rule, source, applies to, setting, changed by, date, note, request (approved by an owner).");
            A11y.Name(_turnOn, "Turn back on", "Turns the selected rule back on (or, for a rule you turned back on, returns it to the default) from the next check. On a read-only guide, asks the owners instead.");
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);
            Shown += (s, e) => Reload(null);
        }

        /// <summary>Guide rules go to the owners as a request (read-only guide, or not an owner).</summary>
        private bool Requesting { get { return _team != null && _team.ChangesNeedRequest; } }

        private void UpdateButton()
        {
            var o = Current();
            var request = o != null && _guideEntries.Contains(o) && Requesting;
            var toDefault = o != null && o.IsTurnOn;
            _turnOn.Text = request ? (toDefault ? "Request the default…" : "Request turning it back on…") : toDefault ? "Use the default" : "Turn back on";
            _turnOn.AccessibleName = _turnOn.Text;
            _turnOn.Enabled = o != null && (!o.IsDefault || _team != null);   // a default needs a guide to record the "on" row
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
                _guideEntries.Clear();
                var inGuides = RuleOverrides.InEffect(roots, _isDefault, root => _team != null && _team.IsPersonal ? "My style guide" : "Team style guide (" + Path.GetFileName(root.TrimEnd('\\')) + ")");
                foreach (var o in inGuides) _guideEntries.Add(o);   // guide rows and bundled defaults: owners decide on a read-only guide
                entries.AddRange(inGuides);
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
                var item = new ListViewItem(new[] { name, rule != null ? Cap(rule.Layer) : LayerOf(o), o.Scope, Severity.Label(o.Setting ?? ""), o.By, o.Date, o.Note, o.RequestId != null ? "Approved request" : "" }) { Tag = o, ToolTipText = o.RuleId + " · " + o.File };
                _list.Items.Add(item);
            }
            _list.ShowItemToolTips = true;
            if (_team != null) _team.Recheck();
            var role = Requesting ? "  " + _team.AccessNotice + " Your project's own exceptions you can turn back on yourself." : "";
            _head.Text = (done != null ? done + "  " : "") +
                         (entries.Count == 0 ? "No rules are turned off or changed." : Plural.Of(entries.Count, "rule") + " turned off or changed (Default: shipped with House Style). Select one and choose Turn back on to apply it again.") + role;
            if (_list.Items.Count > 0) _list.Items[0].Selected = true;
            UpdateButton();
        }

        private void TurnBackOn()
        {
            var o = Current();
            if (o == null) return;
            var guide = _guideEntries.Contains(o);
            if (guide && _team != null) _team.Recheck();   // before any write: permissions or the owners list may have changed
            if (guide && Requesting) { RequestTurnOn(o); return; }
            try
            {
                var file = RuleOverrides.TurnBackOn(o, guide ? _team : null);   // a project's own exception: no owner needed
                Reload((o.IsTurnOn ? "Back to the default: " : "Turned back on: ") + o.RuleId + " (in " + file + "). It applies from the next check.");
            }
            catch (GuideWriteException ex) when (guide && _team != null && _team.IsStrict && !_team.IsPersonal)
            {
                Diagnostics.Log("turn rule back on", ex);
                _team.ProbeGuide();
                if (Notify.Confirm(this, "House Style couldn't write to " + o.File + ": " + ex.Message + "\n\nNothing was changed. Ask the style guide owners (" + _team.OwnerNames + ") to turn it back on instead?", "Turned-off rules"))
                    RequestTurnOn(o);
            }
            catch (Exception ex)
            {
                Diagnostics.Log("turn rule back on", ex);
                Notify.Error(this, "House Style couldn't turn " + o.RuleId + " back on.", ex.Message + "\n\nNothing was changed. File: " + o.File, ex, "Turned-off rules");
            }
        }

        /// <summary>Asks the owners to turn a guide rule back on, with a reason.</summary>
        private void RequestTurnOn(RuleOverride o)
        {
            var reason = RequestForm.AskReason(this, (o.IsTurnOn ? "Return to the default: " : "Turn back on: ") + o.RuleId + (string.IsNullOrEmpty(o.Note) ? "" : " (turned off because: " + o.Note + ")"),
                "This will be sent to the style guide owners (" + _team.OwnerNames + "). They decide in Tune style guide; you see the answer on your next check." +
                (_team.AccessNotice == null ? "" : Environment.NewLine + _team.AccessNotice));
            if (reason == null) return;
            var r = RuleChanges.TurnOnRequest(_team, o, reason);
            string problem;
            try { problem = RuleChanges.Send(_team, r); }
            catch (Exception ex) { Diagnostics.Log("request turn on", ex); Notify.Error(this, "House Style couldn't send your request.", ex.Message + "\n\nYour reason: " + reason, ex, "Turned-off rules"); return; }
            Reload(problem == null ? "Request sent to the owners to " + (o.IsTurnOn ? "return " + o.RuleId + " to the default." : "turn " + o.RuleId + " back on.") : "Request saved but not sent yet: " + problem + ". House Style sends it on your next check.");
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
