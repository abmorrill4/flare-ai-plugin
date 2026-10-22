using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Top-to-bottom layout for a fixed dialog page: a label column and a field column, where everything that holds prose
    /// is measured with the real font and wraps at the field width, and the next control goes below its measured height.
    /// Nothing is cut off at 100%, 125% or 150%: widths scale with the DPI and heights come from the text.
    /// Controls are grouped in blocks that can be hidden as a unit; <see cref="Reflow"/> closes the gaps.
    /// </summary>
    internal sealed class StackLayout
    {
        /// <summary>A run of controls shown or hidden together.</summary>
        internal sealed class Block
        {
            public readonly List<Control> Items = new List<Control>();
            public int Top, Height;
            public bool Visible = true;
        }

        private readonly Control _host;
        private readonly float _scale;
        private readonly List<Block> _blocks = new List<Block>();
        private Block _current;
        private bool _sealed;   // block heights are final once the page has been laid out

        public int Y;
        public readonly int LabelLeft, FieldLeft, FieldWidth;

        public StackLayout(Control host, float scale)
        {
            _host = host;
            _scale = scale <= 0 ? 1f : scale;
            LabelLeft = S(12); FieldLeft = S(166); FieldWidth = S(420);
            Y = S(12);
            Begin();
        }

        /// <summary>Pixels at this DPI for a length designed at 96 DPI.</summary>
        public int S(int px) { return (int)Math.Round(px * _scale); }

        /// <summary>The page width the layout needs.</summary>
        public int Width { get { return FieldLeft + FieldWidth + S(16); } }

        /// <summary>Full text width, from the label column to the end of the field column.</summary>
        public int FullWidth { get { return FieldLeft + FieldWidth - LabelLeft; } }

        private Font Font { get { return _host.Font; } }

        /// <summary>Starts a new block; the controls added after this belong to it.</summary>
        public Block Begin()
        {
            Close();
            _current = new Block { Top = Y };
            _blocks.Add(_current);
            return _current;
        }

        private void Close() { if (_current != null && !_sealed) _current.Height = Y - _current.Top; }

        /// <summary>Adds a control to the page and the current block, where it already is.</summary>
        public T Add<T>(T c) where T : Control
        {
            _host.Controls.Add(c);
            _current.Items.Add(c);
            return c;
        }

        /// <summary>A label and its field on one row; the label wraps if it needs to and the row grows with it. The label is in the field's Tag.</summary>
        public T Row<T>(string label, T control, int width = 0) where T : Control
        {
            var l = Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(FieldLeft - LabelLeft - S(6), 0), Left = LabelLeft, Top = Y + S(3) });
            control.Left = FieldLeft; control.Top = Y;
            control.Width = width > 0 ? width : FieldWidth;
            Add(control);
            control.Tag = l;
            Y += Math.Max(control.Height, l.PreferredHeight + S(6)) + S(3);
            return control;
        }

        /// <summary>Text under a field (grey) or across the page (<paramref name="full"/>): wraps and pushes the next control down.</summary>
        public Label Note(string text, bool full = false, bool grey = true)
        {
            var left = full ? LabelLeft : FieldLeft;
            var width = full ? FullWidth : FieldWidth;
            var n = Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Left = left, Top = Y, ForeColor = grey ? SystemColors.GrayText : SystemColors.ControlText });
            Y += n.PreferredHeight + S(8);
            return n;
        }

        /// <summary>A heading across the page, in the dialog font made bold.</summary>
        public Label Heading(string text)
        {
            var h = Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(FullWidth, 0), Left = LabelLeft, Top = Y, Font = new Font(Font, FontStyle.Bold) });
            Y += h.PreferredHeight + S(6);
            return h;
        }

        /// <summary>A status line under a field with room for <paramref name="lines"/> wrapped lines (its text arrives later).</summary>
        public LiveLabel Status(int lines, bool full = false)
        {
            var left = full ? LabelLeft : FieldLeft;
            var width = full ? FullWidth : FieldWidth;
            var height = (Font.Height + 1) * lines + S(2);
            var l = Add(new LiveLabel { AutoSize = false, Left = left, Top = Y, Width = width, Height = height, ForeColor = SystemColors.GrayText });
            Y += height + S(4);
            return l;
        }

        /// <summary>A check box whose text wraps instead of being cut off (CheckBox.AutoSize never wraps).</summary>
        public CheckBox Check(string text, bool isChecked, bool full = false)
        {
            var left = full ? LabelLeft : FieldLeft;
            var width = full ? FullWidth : FieldWidth;
            var h = TextRenderer.MeasureText(text, Font, new Size(width - S(22), 0), TextFormatFlags.WordBreak).Height + S(6);
            var c = Add(new CheckBox { Text = text, AutoSize = false, Width = width, Height = Math.Max(S(22), h), Left = left, Top = Y, Checked = isChecked, TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft });
            Y += c.Height + S(3);
            return c;
        }

        /// <summary>Option buttons stacked in one group (arrow keys move between them).</summary>
        public RadioButton[] Choices(string groupName, string[] texts, int indent)
        {
            var panel = Add(new Panel { Left = LabelLeft + indent, Top = Y, Width = FullWidth - indent, AccessibleName = groupName, AccessibleRole = AccessibleRole.Grouping });
            var y = 0;
            var radios = new RadioButton[texts.Length];
            for (var i = 0; i < texts.Length; i++)
            {
                var h = TextRenderer.MeasureText(texts[i], Font, new Size(panel.Width - S(22), 0), TextFormatFlags.WordBreak).Height + S(6);
                radios[i] = new RadioButton { Text = texts[i], AutoSize = false, Left = 0, Top = y, Width = panel.Width, Height = Math.Max(S(22), h), TextAlign = ContentAlignment.TopLeft, CheckAlign = ContentAlignment.TopLeft, TabIndex = i };
                panel.Controls.Add(radios[i]);
                y += radios[i].Height + S(2);
            }
            panel.Height = y;
            Y += y + S(6);
            return radios;
        }

        public void Gap(int px) { Y += S(px); }

        /// <summary>
        /// Sizes a status line to its text (at least <paramref name="minLines"/> lines) and moves everything below it up or
        /// down, so a short status leaves no gap and a long one isn't cut off. Call after its text changes.
        /// </summary>
        public void FitHeight(Control c, int minLines = 1)
        {
            Close(); _sealed = true;
            var text = string.IsNullOrEmpty(c.Text) ? " " : c.Text;
            var measured = TextRenderer.MeasureText(text, c.Font, new Size(c.Width, 0), TextFormatFlags.WordBreak).Height;
            var h = Math.Max((c.Font.Height + 1) * minLines, measured) + S(2);
            var delta = h - c.Height;
            if (delta == 0) return;
            var top = c.Top;
            c.Height = h;
            foreach (var b in _blocks)
            {
                if (b.Items.Contains(c)) b.Height += delta;
                else if (b.Top > top) b.Top += delta;
            }
            foreach (Control o in _host.Controls) if (o != c && o.Top > top) o.Top += delta;
            Y += delta;
        }

        /// <summary>The height the visible blocks use now.</summary>
        public int VisibleHeight()
        {
            Close(); _sealed = true;
            var h = _blocks.Count == 0 ? Y : _blocks[0].Top;
            foreach (var b in _blocks) if (b.Visible) h += b.Height;
            return h;
        }

        /// <summary>Lays the blocks out again from the top, skipping hidden ones; returns the height used.</summary>
        public int Reflow()
        {
            Close(); _sealed = true;
            if (_blocks.Count == 0) return Y;
            var y = _blocks[0].Top;
            foreach (var b in _blocks)
            {
                var offset = y - b.Top;
                foreach (var c in b.Items) { c.Top += offset; c.Visible = b.Visible; }
                b.Top = y;
                if (b.Visible) y += b.Height;
            }
            return y;
        }

        /// <summary>The height the page needs with every block shown.</summary>
        public int FullHeight()
        {
            Close(); _sealed = true;
            var h = _blocks.Count == 0 ? Y : _blocks[0].Top;
            foreach (var b in _blocks) h += b.Height;
            return h;
        }
    }
}
