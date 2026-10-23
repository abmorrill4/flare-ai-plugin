using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Excluded Files" (from a Lint Project report): the files and folders this project leaves out of Lint Project, and
    /// Include Again. The list is the project's own (<see cref="LintExclusions"/>), shared by everyone on the project.
    /// </summary>
    internal sealed class ExcludedFilesForm : Form
    {
        private readonly string _anyPath;
        private readonly Label _head;
        private readonly ListBox _list;
        private readonly Button _include;

        public ExcludedFilesForm(string anyPath)
        {
            _anyPath = anyPath;
            Text = Notify.Caption("Excluded Files");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 320);
            MinimumSize = new Size(360, 240);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            _head = new LiveLabel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 0) };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            _list.SelectedIndexChanged += (s, e) => _include.Enabled = _list.SelectedItem != null;
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            _include = new Button { Text = "Include Again", AutoSize = true, Enabled = false };
            _include.Click += (s, e) => Safe.Run("Include again", IncludeAgain);
            bar.Controls.AddRange(new Control[] { close, _include });
            Controls.Add(_list);
            Controls.Add(bar);
            Controls.Add(_head);
            CancelButton = close;
            A11y.Name(_list, "Excluded files and folders");
            A11y.Name(_include, "Include Again", "Lint Project lints this file or folder again.");
            A11y.Name(bar, "Dialog buttons");
            A11y.Apply(this);
            Shown += (s, e) => Reload(null);
        }

        private void Reload(string done)
        {
            _list.Items.Clear();
            try { foreach (var entry in LintExclusions.Load(_anyPath)) _list.Items.Add(entry); }
            catch (Exception ex) { Diagnostics.Log("excluded files", ex); _head.Text = "Could not read the excluded files: " + ex.Message; return; }
            _head.Text = (done != null ? done + "  " : "") + (_list.Items.Count == 0
                ? "Lint Project lints every topic and snippet in this project."
                : "Lint Project leaves these out for everyone on this project. Lint Topic still lints an excluded topic.");
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _include.Enabled = _list.SelectedItem != null;
        }

        private void IncludeAgain()
        {
            var entry = _list.SelectedItem as string;
            if (entry == null) return;
            try { LintExclusions.Remove(_anyPath, entry); }
            catch (Exception ex) { Diagnostics.Log("include again", ex); Notify.Failed(this, "include " + entry + " again", ex, "Excluded Files"); return; }
            Reload("Included " + entry + ". It is linted the next time you lint the project.");
        }
    }
}
