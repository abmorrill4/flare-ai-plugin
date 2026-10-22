using System;
using System.Linq;
using System.Windows.Forms;
using FlareAI.Core.Feedback;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// Report False Positive, from an issue's right-click menu (issue #124): pick a reason, add a note if you like, and
    /// choose whether the surrounding sentence goes too (off). Sends the rule, the flagged words and the version. The
    /// issue stays in the list; the writer can still Ignore it.
    /// </summary>
    public sealed class FalsePositiveForm : FeedbackForm
    {
        public const string Sent = "Thanks. Your report was sent.";

        /// <summary>The reasons as the writer reads them, in the order of <see cref="FalsePositiveReason.All"/>.</summary>
        public static readonly string[] ReasonTexts = { "The text is correct as written", "The suggested change is wrong", "This rule doesn't fit our docs" };

        private readonly ReportContext _ctx;
        private readonly string _ruleId, _ruleName, _flagged, _sentence;
        private readonly RadioButton[] _reasons;
        private readonly TextBox _note;
        private readonly CheckBox _context;
        private readonly Button _send;

        /// <param name="sentence">The sentence around the flagged words (sent only when the writer ticks the box), or null.</param>
        public FalsePositiveForm(string ruleId, string ruleName, string flaggedText, string sentence, ReportContext ctx, float scale = 0)
            : base(Notify.Caption("Report False Positive"), scale)
        {
            _ctx = ctx; _ruleId = ruleId; _ruleName = string.IsNullOrEmpty(ruleName) ? ruleId : ruleName; _flagged = flaggedText ?? ""; _sentence = sentence;

            TextRow(_ruleName, true, 2);
            TextRow("“" + Short(_flagged, 200) + "”", false, 10);
            TextRow("Why is this issue wrong?", false, 2);
            _reasons = Choices("Why is this issue wrong", ReasonTexts);
            _note = Box("Note", 2, "Note");
            _context = Check("Include the surrounding sentence", false, 4);
            _context.Enabled = !string.IsNullOrEmpty(sentence);
            FinishRows();

            var cancel = AddButton("Cancel", null);
            cancel.DialogResult = DialogResult.Cancel;
            _send = AddButton("Send", (s, e) => Send(), "Sends the rule, the flagged words and your reason.");
            Bar.Controls.SetChildIndex(cancel, 0);
            Bar.Controls.SetChildIndex(_send, 1);
            _send.TabIndex = 0; cancel.TabIndex = 1; SaveReport.TabIndex = 2;
            CancelButton = cancel;
            _send.Enabled = false;
            foreach (var r in _reasons) r.CheckedChanged += (s, e) => _send.Enabled = Reason != null;
            A11y.Apply(this);
            Relayout();
        }

        /// <summary>The reason buttons (tests pick one).</summary>
        public RadioButton[] Reasons { get { return _reasons; } }

        /// <summary>The "Include the surrounding sentence" box.</summary>
        public CheckBox IncludeSentence { get { return _context; } }

        private string Reason
        {
            get
            {
                for (var i = 0; i < _reasons.Length; i++) if (_reasons[i].Checked) return FalsePositiveReason.All[i];
                return null;
            }
        }

        /// <summary>The report as it would be sent now.</summary>
        public string PayloadJson() { return Json(); }

        protected override string Json()
        {
            return ReportBuilder.ToJson(ReportBuilder.ForFalsePositive(_ctx, _ruleId, _ruleName, Reason ?? FalsePositiveReason.TextCorrect, _flagged,
                                                                       _context.Checked ? _sentence : null, _note.Text));
        }

        protected override string ServicePath { get { return "/v1/false-positive"; } }

        protected override void SetBusy(bool busy)
        {
            base.SetBusy(busy);
            if (!busy) { _send.Enabled = Reason != null; _context.Enabled = !string.IsNullOrEmpty(_sentence); }
        }

        protected override void OnSendFailed(FeedbackResult result) { _send.Text = "Try Again"; Relayout(); }

        private static string Short(string s, int n)
        {
            s = string.Join(" ", (s ?? "").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToArray());
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
