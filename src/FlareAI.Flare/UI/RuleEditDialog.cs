using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Edits a rule already in the style guide (from Rules): a table row's cells under the table's own headings, or a
    /// bullet's wording with its Bad and Good examples. <see cref="Fields"/> holds the edited values on OK.
    /// </summary>
    internal sealed class RuleEditDialog : Form
    {
        private static readonly string[] Levels = { "error", "warning", "suggestion" };
        private readonly List<KeyValuePair<string, string>> _fields;
        private readonly List<Control> _boxes = new List<Control>();

        public List<KeyValuePair<string, string>> Fields { get; private set; }

        public RuleEditDialog(IList<KeyValuePair<string, string>> fields)
        {
            _fields = fields.ToList();
            Text = Notify.Caption("Edit Rule");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 120 + 62 * _fields.Count);
            MinimumSize = new Size(420, 220);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10, 10, 10, 0), AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var f in _fields)
            {
                Control box;
                if (f.Key.Equals("Severity", StringComparison.OrdinalIgnoreCase) && (f.Value.Length == 0 || Levels.Contains(f.Value.ToLowerInvariant())))
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
                    combo.Items.AddRange(Levels);
                    combo.SelectedItem = f.Value.Length == 0 ? "warning" : f.Value.ToLowerInvariant();
                    box = combo;
                }
                else
                {
                    var wide = f.Value.Length > 40 || new[] { "Rule", "Why", "Example", "Bad", "Good" }.Contains(f.Key, StringComparer.OrdinalIgnoreCase);
                    // Enter saves (AcceptsReturn stays off), so a multiline box is for wrapping only.
                    box = new TextBox { Text = f.Value, Dock = DockStyle.Fill, Multiline = wide, Height = wide ? 54 : 23, ScrollBars = wide ? ScrollBars.Vertical : ScrollBars.None };
                }
                A11y.Name(box, f.Key);
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label { Text = f.Key + ":", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 5, 8, 0) });
                box.Margin = new Padding(3, 3, 3, 6);
                grid.Controls.Add(box);
                _boxes.Add(box);
            }
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Save", AutoSize = true };
            ok.Click += (s, e) => Save();
            bar.Controls.AddRange(new Control[] { cancel, ok });
            Controls.Add(grid); Controls.Add(bar);
            AcceptButton = ok; CancelButton = cancel;
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);
        }

        private void Save()
        {
            if (_boxes[0].Text.Trim().Length == 0)
            {
                Notify.Info(this, _fields[0].Key + " can't be empty. To remove the rule, use Delete Rule.", "Edit Rule");
                _boxes[0].Focus();
                return;
            }
            Fields = _fields.Select((f, i) => new KeyValuePair<string, string>(f.Key, _boxes[i].Text.Trim())).ToList();
            DialogResult = DialogResult.OK;
        }
    }
}
