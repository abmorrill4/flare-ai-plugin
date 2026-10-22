using System;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Feedback;
using FlareAI.Core.Settings;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// "House Style ran into a problem" (issue #124): shown straight away when House Style hits a bug, instead of an error
    /// box. One plain sentence of what failed, an optional "What were you doing?", Details with exactly the JSON that is
    /// sent, Send and Don't Send, and "Send error reports automatically" (saved when the dialog closes).
    /// </summary>
    public sealed class ErrorReportForm : FeedbackForm
    {
        public const string Title = "House Style ran into a problem";

        private readonly ReportContext _ctx;
        private readonly Exception _error;
        private readonly string _whatFailed;
        private readonly TextBox _note, _details;
        private readonly Button _detailsButton, _send;
        private readonly bool _startedAutomatic;

        /// <summary>The "Send error reports automatically" box.</summary>
        public CheckBox Automatic { get; private set; }

        /// <param name="whatFailed">The plain sentence of what failed ("House Style couldn't lint the project.").</param>
        /// <param name="scale">0 for this screen's DPI; tests pass 1.25.</param>
        public ErrorReportForm(string whatFailed, Exception error, ReportContext ctx, float scale = 0) : base(Title, scale)
        {
            _ctx = ctx; _error = error;
            _whatFailed = string.IsNullOrEmpty(whatFailed) ? "House Style couldn't finish what it was doing." : whatFailed;
            _startedAutomatic = ctx.Settings != null && ctx.Settings.SendErrorReportsAutomatically;

            TextRow(_whatFailed, true, 10);
            _note = Box("What were you doing?", 3, "What were you doing");
            _detailsButton = Row(new Button { Text = "Show Details", AutoSize = true }, 6);
            _detailsButton.Click += (s, e) => Safe.Run("Details", ToggleDetails);
            A11y.Name(_detailsButton, "Show Details", "Shows exactly what is sent.");
            _details = Preview(12);
            A11y.Name(_details, "Details", "Exactly what is sent.");
            ShowRow(_details, false);
            Automatic = Check("Send error reports automatically", _startedAutomatic, 4);
            A11y.Name(Automatic, "Send error reports automatically", "Later errors are sent in the background, with a short notice.");
            FinishRows();

            var dontSend = AddButton("Don't Send", null);
            dontSend.DialogResult = DialogResult.Cancel;
            _send = AddButton("Send", (s, e) => Send(), "Sends the report shown in Details.");
            // Right to left: Don't Send at the right edge, then Send to its left.
            Bar.Controls.SetChildIndex(dontSend, 0);
            Bar.Controls.SetChildIndex(_send, 1);
            _send.TabIndex = 0; dontSend.TabIndex = 1; SaveReport.TabIndex = 2;
            AcceptButton = _send; CancelButton = dontSend;
            _note.TextChanged += (s, e) => { if (_details.Visible) _details.Text = Json(); };
            FormClosed += (s, e) => SaveChoice();
            A11y.Apply(this);
            Relayout();
        }

        /// <summary>The report as it would be sent now (with the note typed so far).</summary>
        protected override string Json()
        {
            var json = ReportBuilder.ToJson(ReportBuilder.ForError(_ctx, _error, _whatFailed, _note.Text, false));
            return json.Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        protected override string ServicePath { get { return "/v1/report"; } }

        private void ToggleDetails()
        {
            var show = !_details.Visible;
            if (show) _details.Text = Json();
            ShowRow(_details, show);
            _detailsButton.Text = show ? "Hide Details" : "Show Details";
            Relayout();
        }

        protected override void OnSendFailed(FeedbackResult result) { _send.Text = "Try Again"; Relayout(); }

        /// <summary>Saves the check box when it changed, unless there is no settings file yet (that would end first-run setup).</summary>
        private void SaveChoice()
        {
            if (Automatic.Checked == _startedAutomatic) return;
            try
            {
                if (FirstRun.IsNeeded()) return;
                var s = PluginSettings.Load();
                s.SendErrorReportsAutomatically = Automatic.Checked;
                s.Save();
            }
            catch (Exception ex) { Diagnostics.Log("save automatic error reports", ex); }
        }
    }
}
