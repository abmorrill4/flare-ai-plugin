using System;
using System.Drawing;
using System.Windows.Forms;
using FlareAI.Core.Actions;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Shown before a batch of fixes reaches files other than the open topic: per rule the count and files, real
    /// examples, and a tick box for each rule. Nothing is written until the author clicks Apply.
    /// </summary>
    internal sealed class BatchPreviewForm : Form
    {
        private readonly BatchPlan _plan;
        private readonly ListView _rules;
        private readonly TextBox _details;
        private readonly Button _apply;
        private readonly Label _snippets;
        private bool _loading = true;

        public BatchPreviewForm(BatchPlan plan)
        {
            _plan = plan;
            Text = "House Style — Apply fixes";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(560, 460);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            var intro = new Label
            {
                Left = 12, Top = 10, Width = 536, Height = 54,
                Text = "These fixes reach files other than the one you have open. Untick any rule you don't want. " +
                       "Every change is a tracked change. Files that aren't open are saved and closed afterwards; review them in Flare's Review tab."
            };
            _rules = new ListView { Left = 12, Top = 68, Width = 536, Height = 150, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable };
            _rules.Columns.Add("Rule", 330);
            _rules.Columns.Add("Fixes", 70);
            _rules.Columns.Add("Files", 70);
            foreach (var g in plan.Groups)
            {
                var item = new ListViewItem(new[] { g.Label, g.Findings.Count.ToString(), g.Files.Count.ToString() }) { Tag = g, Checked = true };
                _rules.Items.Add(item);
            }
            _details = new TextBox { Left = 12, Top = 226, Width = 536, Height = 120, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
            // Snippets other topics include: one fix there changes all of them, so say so before Apply.
            _snippets = new Label { Left = 12, Top = 350, Width = 536, Height = 34, ForeColor = Color.DarkRed };
            var skipped = new Label { Left = 12, Top = 386, Width = 536, Height = 34, ForeColor = SystemColors.GrayText, Text = SkippedText() };
            _apply = new Button { Left = 372, Top = 424, Width = 100, Height = 26, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 478, Top = 424, Width = 70, Height = 26, DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { intro, _rules, _details, _snippets, skipped, _apply, cancel });
            AcceptButton = _apply; CancelButton = cancel;
            A11y.Name(_rules, "Rules to apply", "Untick a rule to leave it out. Columns: rule, number of fixes, number of files.");
            A11y.Name(_details, "Examples for the selected rule");

            _rules.ItemChecked += (s, e) => { if (_loading) return; ((RuleGroup)e.Item.Tag).Selected = e.Item.Checked; Refresh2(); };
            _rules.SelectedIndexChanged += (s, e) => ShowDetails();
            _loading = false;
            if (_rules.Items.Count > 0) _rules.Items[0].Selected = true;
            Refresh2();
            A11y.Apply(this);
        }

        private string SkippedText()
        {
            if (_plan.Skipped.Count == 0) return "";
            var names = new System.Collections.Generic.List<string>();
            foreach (var s in _plan.Skipped) names.Add(System.IO.Path.GetFileName(s.Path) + " (" + s.Reason + ")");
            return "Skipped, not written: " + string.Join(", ", names.ToArray());
        }

        private void ShowDetails()
        {
            _details.Text = _rules.SelectedItems.Count == 1 ? BatchPlan.Details((RuleGroup)_rules.SelectedItems[0].Tag).Replace("\n", "\r\n") : "";
        }

        private void Refresh2()
        {
            var n = _plan.SelectedCount;
            _apply.Text = n == 0 ? "Apply" : "Apply " + n;
            _apply.Enabled = n > 0;
            _apply.AccessibleName = n == 0 ? "Apply" : "Apply " + n + " fixes";
            Text = "House Style — " + BatchPlan.Counts(n, _plan.SelectedFileCount);
            _snippets.Text = _plan.SnippetNote() ?? "";
            _snippets.AccessibleName = _snippets.Text;
        }
    }
}
