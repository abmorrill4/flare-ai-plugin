using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Style;

namespace FlareAI.Flare.UI
{
    /// <summary>Edits one candidate rule before it is added to the guide: a table row (Avoid/Use/severity/why/example) or a prose rule.</summary>
    internal sealed class CandidateEditDialog : Form
    {
        private readonly RuleCandidate _c;
        private readonly TextBox _avoid, _use, _why, _example, _text, _bad, _good;
        private readonly ComboBox _severity;

        public CandidateEditDialog(RuleCandidate c)
        {
            _c = c;
            Text = "House Style — Edit rule";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(560, c.IsRow ? 340 : 280);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10, 10, 10, 0), AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (c.IsRow)
            {
                _avoid = Field(grid, "Avoid (comma-separated):", c.Avoid, false);
                _use = Field(grid, "Use instead:", c.Use, false);
                _severity = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
                _severity.Items.AddRange(new object[] { "error", "warning", "suggestion" });
                _severity.SelectedItem = _severity.Items.Contains(c.Severity) ? c.Severity : "warning";
                A11y.Name(_severity, "Severity", "error, warning or suggestion.");
                Row(grid, "Severity:", _severity);
                _why = Field(grid, "Why:", c.Why, true);
                _example = Field(grid, "Example (must break the rule):", c.Example, true);
            }
            else
            {
                _text = Field(grid, "Rule:", c.Text, true);
                _bad = Field(grid, "Bad example:", c.Bad, true);
                _good = Field(grid, "Good example:", c.Good, true);
            }
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Save changes", AutoSize = true };
            ok.Click += (s, e) => Save();
            bar.Controls.AddRange(new Control[] { cancel, ok });
            Controls.Add(grid); Controls.Add(bar);
            AcceptButton = ok; CancelButton = cancel;
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);
        }

        private static TextBox Field(TableLayoutPanel grid, string label, string value, bool multiline)
        {
            // Enter saves (AcceptsReturn stays off), so a multiline box is for wrapping only.
            var box = new TextBox { Text = value ?? "", Dock = DockStyle.Fill, Multiline = multiline, Height = multiline ? 54 : 23, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None };
            A11y.Name(box, label.TrimEnd(':'));
            Row(grid, label, box);
            return box;
        }

        private static void Row(TableLayoutPanel grid, string label, Control box)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 5, 8, 0) });
            box.Margin = new Padding(3, 3, 3, 6);
            grid.Controls.Add(box);
        }

        private void Save()
        {
            if (_c.IsRow)
            {
                if (_avoid.Text.Trim().Length == 0) { Notify.Info(this, "Avoid can't be empty. Remove the rule instead if you don't want it.", "Edit rule"); _avoid.Focus(); return; }
                _c.Avoid = _avoid.Text.Trim(); _c.Use = _use.Text.Trim(); _c.Severity = (string)_severity.SelectedItem;
                _c.Why = _why.Text.Trim(); _c.Example = _example.Text.Trim();
                _c.Prefer = null; // hand-edited: the preferred-form picker no longer applies
                _c.Forms.Clear();
            }
            else
            {
                if (_text.Text.Trim().Length == 0) { Notify.Info(this, "The rule can't be empty. Remove it instead if you don't want it.", "Edit rule"); _text.Focus(); return; }
                if (_c.Title == _c.Text) _c.Title = _text.Text.Trim();
                _c.Text = _text.Text.Trim(); _c.Bad = _bad.Text.Trim(); _c.Good = _good.Text.Trim();
            }
            DialogResult = DialogResult.OK;
        }
    }
}
