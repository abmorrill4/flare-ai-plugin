using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;
using FlareAI.Core.Llm;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Runs the action on a BackgroundWorker (net40-friendly), shows progress, then either the findings list
    /// (Check style) or the proposed XHTML with Apply/Cancel (Rewrite/Ask). Apply is only enabled when the
    /// validator passed; the DOM write happens on the UI thread after the dialog closes.
    /// </summary>
    public sealed class ResultForm : Form
    {
        private readonly ActionRunner _runner;
        private readonly IDocumentAdapter _doc;
        private readonly ActionKind _kind;
        private readonly string _instruction;
        private readonly string _title;

        private readonly Label _status;
        private readonly ProgressBar _progress;
        private readonly TextBox _output;
        private readonly Button _apply, _close;
        private readonly BackgroundWorker _worker;
        private RewriteProposal _proposal;
        private readonly IList<XmlNode> _blocks;

        public ResultForm(string title, ActionRunner runner, IDocumentAdapter doc, ActionKind kind, string instruction)
            : this(title, runner, doc, kind, instruction, null) { }

        /// <param name="blocks">Rewrite these blocks instead of the editor selection (used by the findings list).</param>
        public ResultForm(string title, ActionRunner runner, IDocumentAdapter doc, ActionKind kind, string instruction, IList<XmlNode> blocks)
        {
            _runner = runner; _doc = doc; _kind = kind; _instruction = instruction; _title = title; _blocks = blocks;

            Text = "FlareAI — " + title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(500, 300);
            Font = SystemFonts.MessageBoxFont;

            _status = new Label { Left = 12, Top = 12, Width = 736, Height = 20, Text = "Contacting model…", Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _progress = new ProgressBar { Left = 12, Top = 36, Width = 736, Height = 14, Style = ProgressBarStyle.Marquee, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            _output = new TextBox { Left = 12, Top = 60, Width = 736, Height = 410, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Font = new Font(FontFamily.GenericMonospace, 9f), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
            _apply = new Button { Text = "Apply as tracked changes", Left = 540, Top = 482, Width = 160, Enabled = false, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            _close = new Button { Text = "Cancel", Left = 706, Top = 482, Width = 42, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            _close.Width = 60; _close.Left = 688;
            _apply.Click += (s, e) => OnApply();
            Controls.AddRange(new Control[] { _status, _progress, _output, _apply, _close });
            CancelButton = _close;

            _worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            _worker.DoWork += Work;
            _worker.RunWorkerCompleted += Done;
            Shown += (s, e) => _worker.RunWorkerAsync();
            FormClosing += (s, e) => { if (_worker.IsBusy) _worker.CancelAsync(); };
        }

        private void Work(object sender, DoWorkEventArgs e)
        {
            e.Result = _blocks != null ? _runner.Rewrite(_doc, _blocks, _instruction) : _runner.Rewrite(_doc, _instruction);
        }

        private void Done(object sender, RunWorkerCompletedEventArgs e)
        {
            _progress.Visible = false;
            if (IsDisposed || _worker.CancellationPending) return; // user closed the dialog — never write afterwards

            if (e.Error != null)
            {
                Diagnostics.Log(_title, e.Error);
                _status.Text = "Failed.";
                _output.Text = e.Error is LlmException || e.Error is InvalidOperationException ? e.Error.Message : e.Error.ToString();
                _close.Text = "Close";
                return;
            }

            _proposal = (RewriteProposal)e.Result;
            if (_proposal.Validation.IsValid)
            {
                var notes = new List<string>(_proposal.Validation.Warnings);
                notes.AddRange(_proposal.Notes);
                _status.Text = (_proposal.RemainingLint.Count == 0 ? "Proposed edit passes the style linter." : "Proposed edit — " + _proposal.RemainingLint.Count + " linter issue(s) remain (listed below).")
                               + (notes.Count > 0 ? "  " + string.Join("  ", notes.ToArray()) : "") + "  Applies as tracked changes.";
                var sb = new StringBuilder(_proposal.ProposedXml);
                if (_proposal.RemainingLint.Count > 0)
                    sb.AppendLine().AppendLine().AppendLine("--- still flagged by the linter ---").Append(ActionRunner.Summarize(_proposal.RemainingLint));
                _output.Text = sb.ToString();
                _apply.Enabled = true;
            }
            else
            {
                _status.Text = "Rejected — the model's output would damage the topic structure. Nothing was changed.";
                _output.Text = _proposal.Validation + Environment.NewLine + Environment.NewLine + "--- raw response ---" + Environment.NewLine + _proposal.Raw.Text;
                _close.Text = "Close";
            }
        }

        private void OnApply()
        {
            try
            {
                _runner.Apply(_doc, _proposal, "FlareAI " + _title);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Apply", ex);
                MessageBox.Show("Could not apply the change: " + ex.Message, "FlareAI", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
