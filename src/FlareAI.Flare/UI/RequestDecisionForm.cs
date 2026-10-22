using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Actions;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// An owner decides one request (from Tune style guide): what was asked, why, and the evidence; the owner may adjust
    /// the proposal (level, wording, the rule to add) before approving, or decline with a note for the writer.
    /// </summary>
    internal sealed class RequestDecisionForm : Form
    {
        private readonly GuideRequest _r;
        private readonly ComboBox _setting;
        private readonly TextBox _wording, _note;

        /// <summary><see cref="RequestStatus.Approved"/> or <see cref="RequestStatus.Declined"/>.</summary>
        public string Decision { get; private set; }
        public string Note { get { return _note.Text.Trim(); } }

        /// <summary>The proposal as the owner approves it.</summary>
        public RequestDecision Edits
        {
            get
            {
                return new RequestDecision
                {
                    Note = Note,
                    Setting = _setting == null ? null : FlareAI.Core.Severity.Normalize((string)_setting.SelectedItem),
                    Wording = _wording == null ? null : _wording.Text.Trim(),
                    Candidate = _r.Candidate
                };
            }
        }

        /// <param name="details">What the request says, with its evidence, one line each.</param>
        public RequestDecisionForm(GuideRequest r, IList<string> details)
        {
            _r = r;
            Text = "House Style — Decide a request";
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            const int W = 556;
            var y = 12;
            var title = new Label { Text = r.Title, Left = 12, Top = y, Width = W, Height = 34, AutoEllipsis = true, Font = new Font(Font, FontStyle.Bold) };
            Controls.Add(title); y += 38;
            var info = new TextBox { Left = 12, Top = y, Width = W, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = string.Join(Environment.NewLine, new List<string>(details).ToArray()) };
            Controls.Add(info); y += 138;
            if (r.Kind == RequestKinds.TurnOff || r.Kind == RequestKinds.Severity || r.Kind == RequestKinds.ProjectException)
            {
                Controls.Add(new Label { Text = "Set the rule to:", Left = 12, Top = y + 4, Width = 110 });
                _setting = new ComboBox { Left = 124, Top = y, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
                _setting.Items.AddRange(new object[] { "Off", "Tip", "Warning", "Error" });
                _setting.SelectedItem = FlareAI.Core.Severity.Label(r.Setting ?? "off");
                if (_setting.SelectedIndex < 0) _setting.SelectedIndex = 0;
                Controls.Add(_setting); y += 34;
                A11y.Name(_setting, "Set the rule to", "Change the level before approving, if you like.");
            }
            if (r.Kind == RequestKinds.Wording)
            {
                Controls.Add(new Label { Text = "New wording (edit before approving, if you like):", Left = 12, Top = y, Width = W });
                _wording = new TextBox { Left = 12, Top = y + 20, Width = W, Height = 48, Multiline = true, ScrollBars = ScrollBars.Vertical, Text = r.Wording ?? "" };
                Controls.Add(_wording); y += 74;
                A11y.Name(_wording, "New wording");
            }
            if (r.Kind == RequestKinds.AddRule && r.Candidate != null)
            {
                var edit = new Button { Text = "Edit the rule…", Left = 12, Top = y, AutoSize = true };
                edit.Click += (s, e) => { using (var d = new CandidateEditDialog(r.Candidate)) d.ShowDialog(this); title.Text = r.Title; };
                Controls.Add(edit); y += 34;
                A11y.Name(edit, "Edit the rule", "Change the rule's text, Avoid, Use, severity or reason before approving.");
            }
            Controls.Add(new Label { Text = "Note to the writer (optional, but it helps them most when you decline):", Left = 12, Top = y, Width = W });
            _note = new TextBox { Left = 12, Top = y + 20, Width = W, Height = 48, Multiline = true, ScrollBars = ScrollBars.Vertical };
            Controls.Add(_note); y += 76;
            var approve = new Button { Text = "Approve", Left = 12 + W - 75 * 3 - 12, Top = y, Width = 75 };
            var decline = new Button { Text = "Decline", Left = 12 + W - 75 * 2 - 6, Top = y, Width = 75 };
            var cancel = new Button { Text = "Cancel", Left = 12 + W - 75, Top = y, Width = 75, DialogResult = DialogResult.Cancel };
            approve.Click += (s, e) => { Decision = RequestStatus.Approved; DialogResult = DialogResult.OK; };
            decline.Click += (s, e) => { Decision = RequestStatus.Declined; DialogResult = DialogResult.OK; };
            Controls.Add(approve); Controls.Add(decline); Controls.Add(cancel);
            ClientSize = new Size(W + 24, y + 40);
            CancelButton = cancel;
            A11y.Name(info, "The request", "What was asked, why, and the evidence.");
            A11y.Name(_note, "Note to the writer", "Optional. The writer sees it with the outcome on their next check.");
            A11y.Name(approve, "Approve", r.Kind == RequestKinds.ProjectException
                ? "Approves the exception. The writer's plugin writes it into their project on their next check."
                : "Applies the change to the style guide now.");
            A11y.Name(decline, "Decline", "Nothing changes; the writer sees your note.");
            A11y.Apply(this);
        }
    }
}
