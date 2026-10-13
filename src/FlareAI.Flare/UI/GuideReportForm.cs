using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "Validate style guide": tests the guide itself. Step 1 (free, instant): every rule-table row against its
    /// Example, contradictions, unreadable topics. Step 2 (on request, costs model calls): every prose rule with
    /// Bad/Good examples against the AI reviewer, so you know which written rules the AI actually applies.
    /// </summary>
    public sealed class GuideReportForm : Form
    {
        private readonly IStyleService _style;
        private readonly Func<ActionRunner> _runner;
        private readonly string _project;
        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly TextBox _text;
        private readonly Button _ai;
        private readonly StringBuilder _report = new StringBuilder();
        private IList<GuideExample> _examples;

        public GuideReportForm(IStyleService style, Func<ActionRunner> runner, string project)
        {
            _style = style; _runner = runner; _project = project;
            Text = "House Style — Validate style guide";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 560);
            MinimumSize = new Size(520, 320);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            _status = new LiveLabel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(8, 8, 8, 0), Text = "Testing the guide's rule tables…" };
            _progress = new ProgressBar { Dock = DockStyle.Top, Height = 5, Style = ProgressBarStyle.Marquee };
            _text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font(FontFamily.GenericMonospace, 9f) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            _ai = new Button { Text = "Check prose rules with AI…", AutoSize = true, Enabled = false };
            _ai.Click += (s, e) => CheckWithAi();
            bar.Controls.Add(close);
            bar.Controls.Add(_ai);
            Controls.Add(_text);
            Controls.Add(bar);
            Controls.Add(_progress);
            Controls.Add(_status);
            CancelButton = close;
            A11y.Name(_text, "Style guide validation report", "Read-only report. Use the arrow keys or Page Down to read it.");
            A11y.Name(bar, "Dialog buttons");
            A11y.Name(_progress, "Progress");
            A11y.Apply(this);
            Shown += (s, e) => Validate();
        }

        private new void Validate()
        {
            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) =>
            {
                // Owners (or a writer with no team) give new rules their permanent keys first: ids that never shift.
                var keyed = 0;
                var engine = _style as StyleEngine;
                if (engine != null && (Team.Current == null || Team.Current.IsOwner)) keyed = engine.AssignKeys();
                e.Result = new object[] { _style.Validate(_project), _style.Examples(_project), keyed };
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                _progress.Visible = false;
                if (e.Error != null) { Diagnostics.Log("Validate guide", e.Error); _status.Text = "Could not validate: " + e.Error.Message; return; }
                var parts = (object[])e.Result;
                var report = (GuideReport)parts[0];
                _examples = (IList<GuideExample>)parts[1];
                Write(report);
                var keyed = (int)parts[2];
                _status.Text = (keyed > 0 ? "Gave " + keyed + " new rule(s) permanent ids.  " : "") + (report.Errors == 0 ? "Rule tables pass." : report.Errors + " error(s) to fix.") +
                               (_examples.Count > 0 ? "  " + _examples.Count + " prose rule(s) have Bad/Good examples you can check against the AI." : "");
                _ai.Enabled = _examples.Count > 0;
            };
            worker.RunWorkerAsync();
        }

        private void Write(GuideReport r)
        {
            _report.AppendLine("Layers (later wins): " + string.Join(" → ", r.Layers.ToArray()));
            _report.AppendLine(r.TableRules + " table rule(s), " + r.TestedByExample + " tested by their Example. " +
                               r.ProseRules + " prose rule(s), " + r.ProseRulesWithExamples + " with Bad/Good examples.");
            _report.AppendLine(r.Errors + " error(s), " + r.Warnings + " warning(s).").AppendLine();
            foreach (var level in new[] { "error", "warning" })
            {
                var list = r.Issues.FindAll(i => i.Level == level);
                if (list.Count == 0) continue;
                _report.AppendLine(level == "error" ? "ERRORS — these rules don't work as written:" : "WARNINGS:");
                foreach (var i in list)
                    _report.AppendLine("  " + i.File + (string.IsNullOrEmpty(i.Location) ? "" : ", " + i.Location) + " [" + i.Layer + "]").AppendLine("    " + i.Message);
                _report.AppendLine();
            }
            if (r.Issues.Count == 0) _report.AppendLine("Every rule-table row is triggered by its Example and fixed by its replacement.").AppendLine();
            _text.Text = _report.ToString();
        }

        private void CheckWithAi()
        {
            var calls = 0;
            foreach (var ex in _examples) calls += (string.IsNullOrEmpty(ex.Bad) ? 0 : 1) + (string.IsNullOrEmpty(ex.Good) ? 0 : 1);
            if (MessageBox.Show(this, "Check " + _examples.Count + " prose rule(s) against the AI reviewer?\n\nThis makes " + calls + " model call(s).",
                    "House Style", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            var runner = _runner();
            if (runner == null) return;
            _ai.Enabled = false;
            _progress.Visible = true; _progress.Style = ProgressBarStyle.Continuous; _progress.Value = 0;
            var worker = new BackgroundWorker { WorkerReportsProgress = true };
            worker.DoWork += (s, e) => e.Result = runner.VerifyExamples(_examples, (done, total) => worker.ReportProgress(done * 100 / Math.Max(1, total), done + "/" + total));
            worker.ProgressChanged += (s, e) => { if (!IsDisposed) { _progress.Value = e.ProgressPercentage; _status.Text = "Asking the AI… " + e.UserState; } };
            worker.RunWorkerCompleted += (s, e) =>
            {
                if (IsDisposed) return;
                _progress.Visible = false;
                if (e.Error != null) { Diagnostics.Log("Verify examples", e.Error); _status.Text = "AI check failed: " + e.Error.Message; return; }
                var results = (List<ExampleResult>)e.Result;
                var passed = results.FindAll(x => x.Passed).Count;
                _report.AppendLine("AI CHECK — does the reviewer apply the prose rules?");
                foreach (var x in results)
                {
                    var notes = new List<string>();
                    if (x.BadCaught.HasValue) notes.Add(x.BadCaught.Value ? "Bad example caught" : "Bad example MISSED");
                    if (x.GoodClean.HasValue) notes.Add(x.GoodClean.Value ? "Good example clean" : "Good example wrongly flagged");
                    _report.AppendLine("  " + (x.Passed ? "✓ " : "✗ ") + x.Example.Id + "  " + Shorten(x.Example.Text, 70)).AppendLine("      " + string.Join("; ", notes.ToArray()));
                }
                _report.AppendLine().AppendLine(passed + " of " + results.Count + " prose rule(s) applied as written. For a missed rule: sharpen its wording, add a clearer Bad example, or make it a table row (deterministic).");
                _text.Text = _report.ToString();
                _status.Text = "AI applies " + passed + " of " + results.Count + " prose rule(s) as written.";
            };
            worker.RunWorkerAsync();
        }

        private static string Shorten(string s, int n) { s = s ?? ""; return s.Length <= n ? s : s.Substring(0, n) + "…"; }
    }
}
