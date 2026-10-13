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

        public bool ForTeam { get { return _team.Checked; } }
        /// <summary>The stored token: off, suggestion, warning or error.</summary>
        public string Setting { get { return FlareAI.Core.Severity.Normalize((string)_setting.SelectedItem); } }
        public string Reason { get { return _reason.Text.Trim(); } }

        public RuleSettingForm(string ruleText, bool canChangeTeamGuide)
        {
            Text = "House Style — Turn off this rule";
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 270); Font = SystemFonts.MessageBoxFont;
            Controls.Add(new Label { Text = ruleText, Left = 12, Top = 12, Width = 496, Height = 48, AutoEllipsis = true });
            _team = new RadioButton { Text = "For the whole team (writes it into the style guide)", Left = 12, Top = 66, Width = 496, Checked = canChangeTeamGuide, Enabled = canChangeTeamGuide };
            _project = new RadioButton { Text = "For this project only", Left = 12, Top = 90, Width = 496, Checked = !canChangeTeamGuide };
            Controls.Add(_team); Controls.Add(_project);
            Controls.Add(new Label { Text = "Set the rule to:", Left = 12, Top = 124, Width = 110 });
            _setting = new ComboBox { Left = 124, Top = 120, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            _setting.Items.AddRange(new object[] { "Off", "Tip", "Warning", "Error" });
            _setting.SelectedIndex = 0;
            Controls.Add(_setting);
            Controls.Add(new Label { Text = "Why (the team sees this in the guide):", Left = 12, Top = 156, Width = 496 });
            _reason = new TextBox { Left = 12, Top = 176, Width = 496 };
            Controls.Add(_reason);
            var ok = new Button { Text = "Apply", Left = 352, Top = 228, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 433, Top = 228, Width = 75, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
