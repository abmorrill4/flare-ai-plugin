using System;
using System.Windows.Forms;
using FlareAI.Core.Feedback;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Help &gt; Report a Problem (issue #124): what happened (required), what the writer expected, and whether to include
    /// diagnostic details (on). Preview shows exactly what is sent.
    /// </summary>
    public sealed class ReportProblemForm : FeedbackForm
    {
        public const string Sent = "Thanks. Your report was sent.";

        private readonly ReportContext _ctx;
        private readonly TextBox _what, _expected;
        private readonly CheckBox _diagnostics;
        private readonly Button _send, _preview;

        public ReportProblemForm(ReportContext ctx, float scale = 0) : base(Notify.Caption("Report a Problem"), scale)
        {
            _ctx = ctx;
            _what = Box("What happened?", 4, "What happened");
            _expected = Box("What did you expect?", 3, "What did you expect");
            _diagnostics = Check("Include diagnostic details", true, 4);
            A11y.Name(_diagnostics, "Include diagnostic details", "Adds the House Style and Flare versions, Windows, settings without keys, and the end of the diagnostics log.");
            FinishRows();

            var cancel = AddButton("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            _send = AddButton("Send", (s, e) => Send(), "Sends the report shown in Preview.");
            _preview = AddButton("Preview", (s, e) => Notify.Report(this, "Preview", Json()), "Shows exactly what is sent.");
            Bar.Controls.SetChildIndex(cancel, 0);
            Bar.Controls.SetChildIndex(_send, 1);
            Bar.Controls.SetChildIndex(_preview, 2);
            _send.TabIndex = 0; cancel.TabIndex = 1; _preview.TabIndex = 2; SaveReport.TabIndex = 3;
            CancelButton = cancel;
            _send.Enabled = false;
            _what.TextChanged += (s, e) => _send.Enabled = _what.Text.Trim().Length > 0;
            A11y.Apply(this);
            Relayout();
        }

        /// <summary>The "What happened?" box (tests type into it).</summary>
        public TextBox WhatHappened { get { return _what; } }

        protected override string Json()
        {
            var json = ReportBuilder.ToJson(ReportBuilder.ForProblem(_ctx, _what.Text, _expected.Text, _diagnostics.Checked));
            return json.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        protected override string ServicePath { get { return "/v1/report"; } }

        protected override void SetBusy(bool busy)
        {
            base.SetBusy(busy);
            if (!busy) _send.Enabled = _what.Text.Trim().Length > 0;
        }

        protected override void OnSent(FeedbackResult result)
        {
            Notify.Info(this, Sent, "Report a Problem");
            base.OnSent(result);
        }

        protected override void OnSendFailed(FeedbackResult result) { _send.Text = "Try Again"; Relayout(); }
    }
}
