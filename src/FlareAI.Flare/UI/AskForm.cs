using System;
using System.Drawing;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>Free-form instruction for the current selection.</summary>
    public sealed class AskForm : Form
    {
        private readonly TextBox _text;
        public string Instruction { get { return _text.Text.Trim(); } }

        public AskForm()
        {
            Text = "House Style — Ask";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ClientSize = new Size(520, 200);
            Font = SystemFonts.MessageBoxFont;

            var label = new Label { Text = "What should I do with the selected text? (whole topic if nothing is selected)", Left = 12, Top = 12, Width = 496, Height = 32 };
            _text = new TextBox { Left = 12, Top = 48, Width = 496, Height = 96, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
            var ok = new Button { Text = "Run", Left = 352, Top = 156, Width = 75, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 433, Top = 156, Width = 75, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => { if (Instruction.Length == 0) { DialogResult = DialogResult.None; _text.Focus(); } };

            Controls.AddRange(new Control[] { label, _text, ok, cancel });
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
