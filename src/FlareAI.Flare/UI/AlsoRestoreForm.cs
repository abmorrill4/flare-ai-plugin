using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Shown by Turn back on when the rule was turned off together with others (rules that flag the same words):
    /// "Also turn back on N rules turned off with it", checked by default; the mirror of the Turn off dialog.
    /// </summary>
    internal sealed class AlsoRestoreForm : Form
    {
        private readonly CheckBox _also;

        public bool AlsoOthers { get { return _also.Checked; } }

        /// <param name="ruleText">The rule being turned back on.</param>
        /// <param name="names">The companion rules, one line each.</param>
        /// <param name="verb">"Turn back on" or "Request turning back on".</param>
        public AlsoRestoreForm(string ruleText, IList<string> names, string verb)
        {
            Text = "House Style — " + verb;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(new Label { Text = ruleText, Left = 12, Top = 12, Width = 496, Height = 48, AutoEllipsis = true });
            var lines = new List<string>();
            foreach (var n in names) lines.Add("•  " + (n.Length > 80 ? n.Substring(0, 79) + "…" : n));
            _also = new CheckBox
            {
                Text = "Also enable " + names.Count + (names.Count == 1 ? " rule that was" : " rules that were") + " disabled with it",
                Left = 12, Top = 66, Width = 496, Checked = true
            };
            var list = new Label { Text = string.Join(Environment.NewLine, lines.ToArray()), Left = 32, Top = 90, Width = 476, Height = 16 * lines.Count + 4, AutoEllipsis = true };
            Controls.Add(_also); Controls.Add(list);
            var bottom = 90 + list.Height + 12;
            var ok = new Button { Text = "Continue", Left = 352, Top = bottom, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 433, Top = bottom, Width = 75, DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
            ClientSize = new Size(520, bottom + 40);
            A11y.Name(_also, _also.Text, string.Join(", ", lines.ToArray()));
            A11y.Apply(this);
        }
    }
}
