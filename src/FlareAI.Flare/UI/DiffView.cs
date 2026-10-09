using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Editing;

namespace FlareAI.Flare.UI
{
    /// <summary>Paints a <see cref="ReadableDiff"/> into a RichTextBox: removed text struck red, added text green.</summary>
    internal static class DiffView
    {
        private static readonly Color Removed = Color.FromArgb(176, 0, 32), RemovedBack = Color.FromArgb(253, 231, 233);
        private static readonly Color Added = Color.FromArgb(0, 104, 55), AddedBack = Color.FromArgb(226, 245, 233);

        public static RichTextBox Create()
        {
            return new RichTextBox { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = SystemColors.Window, DetectUrls = false, Font = SystemFonts.MessageBoxFont, ScrollBars = RichTextBoxScrollBars.Vertical };
        }

        /// <param name="dimUnchanged">Grey out lines with no changes, so the eye goes to the edits.</param>
        public static void Render(RichTextBox box, IList<DiffLine> lines, bool dimUnchanged)
        {
            box.Clear();
            foreach (var line in lines)
            {
                var dim = dimUnchanged && !line.Changed;
                Append(box, line.Prefix, dim ? SystemColors.GrayText : SystemColors.ControlDarkDark, box.BackColor, FontStyle.Regular, box.Font);
                Segments(box, line.Segments, dim);
                box.AppendText("\n");
            }
            box.Select(0, 0);
        }

        public static void Segments(RichTextBox box, IList<DiffSegment> segments, bool dim)
        {
            foreach (var s in segments)
            {
                if (s.Kind == DiffKind.Same) Append(box, s.Text, dim ? SystemColors.GrayText : SystemColors.WindowText, box.BackColor, FontStyle.Regular, box.Font);
                else if (s.Kind == DiffKind.Deleted) Append(box, s.Text, Removed, RemovedBack, FontStyle.Strikeout, box.Font);
                else Append(box, s.Text, Added, AddedBack, FontStyle.Underline, box.Font);
            }
        }

        private static void Append(RichTextBox box, string text, Color fore, Color back, FontStyle style, Font font)
        {
            if (string.IsNullOrEmpty(text)) return;
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = fore;
            box.SelectionBackColor = back;
            box.SelectionFont = new Font(font, style);
            box.AppendText(text);
        }
    }
}
