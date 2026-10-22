using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Turn off / change rule…" for style guide owners: the rule as written, where the change applies (the whole team,
    /// through the style guide, or this project only), its new level, and why. No rule ids to type.
    /// </summary>
    internal sealed class RuleSettingForm : Form
    {
        private readonly RadioButton _team, _project;
        private readonly ComboBox _setting;
        private readonly TextBox _reason;
        private readonly CheckBox _others;

        public bool ForTeam { get { return _team.Checked; } }
        /// <summary>The stored token: off, suggestion, warning or error.</summary>
        public string Setting { get { return FlareAI.Core.Severity.Normalize((string)_setting.SelectedItem); } }
        /// <summary>True when the writer also wants the other rules that flag the same words turned off (same scope, setting and note).</summary>
        public bool AlsoOthers { get { return _others != null && _others.Checked; } }
        public string Reason { get { return _reason.Text.Trim(); } }

        /// <param name="personalGuide">True when there is no team guide: the change goes to the writer's own style guide.</param>
        /// <param name="setting">The level to preselect (default Off).</param>
        /// <param name="canChangeProject">False when no project is open (Style rules without a topic).</param>
        /// <param name="title">The dialog's action, for its caption (default "Turn off this rule").</param>
        public RuleSettingForm(string ruleText, bool canChangeTeamGuide, bool personalGuide = false, IList<FlareAI.Core.Style.RuleRef> otherRules = null,
                               string setting = null, bool canChangeProject = true, string title = null)
        {
            Text = "House Style — " + (title ?? "Disable Rule");
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 270); Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(new Label { Text = ruleText, Left = 12, Top = 12, Width = 496, Height = 48, AutoEllipsis = true });
            _team = new RadioButton { Text = personalGuide ? "For all my projects (writes it into my style guide)" : "For the whole team (writes it into the style guide)",
                                      Left = 12, Top = 66, Width = 496, Checked = canChangeTeamGuide, Enabled = canChangeTeamGuide };
            _project = new RadioButton { Text = "For this project only", Left = 12, Top = 90, Width = 496, Checked = !canChangeTeamGuide, Enabled = canChangeProject };
            Controls.Add(_team); Controls.Add(_project);
            Controls.Add(new Label { Text = "Set the rule to:", Left = 12, Top = 124, Width = 110 });
            _setting = new ComboBox { Left = 124, Top = 120, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            _setting.Items.AddRange(new object[] { "Off", "Suggestion", "Warning", "Error" });
            _setting.SelectedItem = FlareAI.Core.Severity.Label(setting ?? "off");
            if (_setting.SelectedIndex < 0) _setting.SelectedIndex = 0;
            Controls.Add(_setting);
            Controls.Add(new Label { Text = "Reason (the team sees it in the guide):", Left = 12, Top = 156, Width = 496 });
            _reason = new TextBox { Left = 12, Top = 176, Width = 496 };
            Controls.Add(_reason);
            var extra = 0;
            if (otherRules != null && otherRules.Count > 0)
            {
                var names = new List<string>();
                foreach (var r in otherRules) names.Add("•  " + (r.Name.Length > 80 ? r.Name.Substring(0, 79) + "…" : r.Name));   // one line each
                _others = new CheckBox { Text = (setting == null || setting == "off" ? "Also disable " : "Also change ") + otherRules.Count + (otherRules.Count == 1 ? " other rule" : " other rules") + " that flag the same words", Left = 12, Top = 208, Width = 496, Checked = true };
                var list = new Label { Text = string.Join(Environment.NewLine, names.ToArray()), Left = 32, Top = 232, Width = 476, Height = 16 * names.Count + 4, AutoEllipsis = true };
                Controls.Add(_others); Controls.Add(list);
                extra = 28 + 16 * names.Count;
                A11y.Name(_others, _others.Text, string.Join(", ", names.ToArray()));
            }
            ClientSize = new Size(520, 270 + extra);
            var ok = new Button { Text = "OK", Left = 352, Top = 228 + extra, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 433, Top = 228 + extra, Width = 75, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            A11y.Name(_team, _team.Text);
            A11y.Name(_project, "For this project only");
            A11y.Name(_setting, "Set the rule to");
            A11y.Name(_reason, "Why", "The team sees this reason in the guide.");
            A11y.Apply(this);
        }
    }
}
