using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Actions;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Request a change": what the writer asks the style guide owners for (the whole team, or an exception for this
    /// project), the level or new wording, and why. Says up front that it goes to the owners; the reason is required.
    /// Also used, without the rule choices, for "Request turning it back on" and "Request adding rules".
    /// </summary>
    internal sealed class RequestForm : Form
    {
        private readonly RadioButton _team, _project;
        private readonly ComboBox _setting;
        private readonly TextBox _wording, _reason;
        private readonly string _originalWording;

        public bool ForTeam { get { return _team == null || _team.Checked; } }
        /// <summary>The stored token: off, suggestion, warning or error.</summary>
        public string Setting { get { return _setting == null ? null : FlareAI.Core.Severity.Normalize((string)_setting.SelectedItem); } }
        /// <summary>The proposed wording when the writer changed it, else null.</summary>
        public string Wording { get { return _wording == null || _wording.Text.Trim() == (_originalWording ?? "").Trim() ? null : _wording.Text.Trim(); } }
        public string Reason { get { return _reason.Text.Trim(); } }

        /// <summary>A rule change: scope, level, wording (when the owners can reword the rule) and reason.</summary>
        public RequestForm(RequestQuestion q) : this(q, true) { }

        /// <summary>Just the reason: <paramref name="what"/> says what is asked (turn back on, add rules).</summary>
        public RequestForm(string what, string notice) : this(new RequestQuestion { RuleText = what, Notice = notice }, false) { }

        private RequestForm(RequestQuestion q, bool choices)
        {
            Text = "House Style — Request Change";
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            const int W = 496;
            var y = 12;
            var rule = new Label { Text = q.RuleText ?? "", Left = 12, Top = y, Width = W, Height = 34, AutoEllipsis = true };
            Controls.Add(rule); y += 38;
            var notice = new Label { Text = q.Notice ?? "", Left = 12, Top = y, Width = W, Height = 48, ForeColor = SystemColors.GrayText };
            Controls.Add(notice); y += 52;
            if (choices)
            {
                _team = new RadioButton { Text = "For the whole team (the owners change the style guide)", Left = 12, Top = y, Width = W, Enabled = q.CanAskTeam };
                _project = new RadioButton { Text = "For this project only (an exception, and the team rule stays enabled)", Left = 12, Top = y + 24, Width = W, Enabled = q.CanAskProject };
                var forProject = (q.ForProject || !q.CanAskTeam) && q.CanAskProject;
                _team.Checked = !forProject; _project.Checked = forProject;
                Controls.Add(_team); Controls.Add(_project); y += 56;
                Controls.Add(new Label { Text = "Set the rule to:", Left = 12, Top = y + 4, Width = 110 });
                _setting = new ComboBox { Left = 124, Top = y, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
                _setting.Items.AddRange(new object[] { "Off", "Suggestion", "Warning", "Error" });
                _setting.SelectedItem = FlareAI.Core.Severity.Label(q.Setting ?? "off");
                if (_setting.SelectedIndex < 0) _setting.SelectedIndex = 0;
                Controls.Add(_setting); y += 34;
                if (q.Wording != null)
                {
                    _originalWording = q.Wording;
                    Controls.Add(new Label { Text = "Or ask for new wording. Edit the rule here, and the level above is ignored.", Left = 12, Top = y, Width = W });
                    _wording = new TextBox { Left = 12, Top = y + 20, Width = W, Height = 48, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = q.Wording };
                    Controls.Add(_wording); y += 74;
                    _team.CheckedChanged += (s, e) => _wording.Enabled = _team.Checked;
                    _wording.Enabled = _team.Checked;
                }
            }
            Controls.Add(new Label { Text = "Reason (required, the owners read it to decide):", Left = 12, Top = y, Width = W });
            _reason = new TextBox { Left = 12, Top = y + 20, Width = W, Height = 48, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = q.Reason ?? "" };
            Controls.Add(_reason); y += 76;
            var ok = new Button { Text = "Send request", Left = 12 + W - 75 - 6 - 100, Top = y, Width = 100, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 12 + W - 75, Top = y, Width = 75, DialogResult = DialogResult.Cancel };
            ok.Enabled = Reason.Length > 0;
            _reason.TextChanged += (s, e) => ok.Enabled = Reason.Length > 0;
            Controls.Add(ok); Controls.Add(cancel);
            ClientSize = new Size(W + 24, y + 40);
            AcceptButton = ok; CancelButton = cancel;
            A11y.Name(rule, "Rule", q.RuleText);
            A11y.Name(notice, "Where this goes", q.Notice);
            if (_team != null) { A11y.Name(_team, _team.Text); A11y.Name(_project, _project.Text); A11y.Name(_setting, "Set the rule to"); }
            if (_wording != null) A11y.Name(_wording, "New wording", "Edit the rule's text to ask the owners to change its wording.");
            A11y.Name(_reason, "Why", "Required. The style guide owners read this to decide.");
            A11y.Name(ok, "Send request", "Sends this request to the style guide owners.");
            A11y.Apply(this);
            Shown += (s, e) => _reason.Focus();
        }

        /// <summary>Asks for a rule change; null when cancelled.</summary>
        public static RuleSettingChoice Ask(IWin32Window owner, RequestQuestion q)
        {
            using (var form = new RequestForm(q))
            {
                if (form.ShowDialog(owner) != DialogResult.OK) return null;
                return new RuleSettingChoice { ForTeam = form.ForTeam, Setting = form.Setting, Reason = form.Reason, Wording = form.Wording };
            }
        }

        /// <summary>Asks only for the reason; null when cancelled.</summary>
        public static string AskReason(IWin32Window owner, string what, string notice)
        {
            using (var form = new RequestForm(what, notice))
                return form.ShowDialog(owner) == DialogResult.OK ? form.Reason : null;
        }
    }
}
