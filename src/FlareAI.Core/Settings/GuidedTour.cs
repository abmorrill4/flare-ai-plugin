using System;

namespace FlareAI.Core.Settings
{
    /// <summary>The steps of the guided first lint, in order.</summary>
    public enum TourStep
    {
        LintTopic = 0,
        Change = 1,
        Undo = 2,
        DisableRule = 3,
        ReviewTab = 4,
        Finished = 5
    }

    /// <summary>Something the writer did that a tour step waits for.</summary>
    public enum TourEvent
    {
        /// <summary>A topic was linted (Lint Topic, or the pane following the open topic).</summary>
        TopicLinted,
        /// <summary>An issue was changed in the topic (Change, Change All, Make Automatic Changes).</summary>
        Changed,
        /// <summary>A change was undone in the editor (Ctrl+Z) and its issue is back.</summary>
        Undone,
        /// <summary>A rule was disabled, or a change request for it was sent to the owners.</summary>
        RuleDisabled,
        /// <summary>The writer clicked Next on a step House Style can't see happen.</summary>
        Next
    }

    /// <summary>
    /// The guided first lint: five short steps, each one plain sentence in a hint strip at the top of the issues pane.
    /// A step ends when the writer actually does it (an event from the pane); the Review tab step can't be seen from a
    /// plugin, so it ends on Next. One tour runs at a time (<see cref="Current"/>).
    /// </summary>
    public sealed class GuidedTour
    {
        /// <summary>The last line, shown when the steps are done.</summary>
        public const string FinishedText = "You're ready. Lint Project lints every topic.";

        /// <summary>The tour in progress, or null.</summary>
        public static GuidedTour Current { get; private set; }

        /// <summary>Raised when a tour starts, moves on, finishes or is skipped (on the thread that caused it).</summary>
        public static event Action Changed;

        public TourStep Step { get; private set; }

        /// <summary>True when the writer is practicing on the sample project (so the hints can name it).</summary>
        public bool Sample { get; private set; }

        /// <summary>Starts a tour (replacing any other) at step 1.</summary>
        public static GuidedTour Start(bool sample)
        {
            Current = new GuidedTour { Step = TourStep.LintTopic, Sample = sample };
            Diagnostics.Log("Tour: started (" + (sample ? "sample project" : "open topic") + "), step 1");
            Raise();
            return Current;
        }

        /// <summary>Ends the tour now (Skip tour, or Done after the last line).</summary>
        public static void Stop()
        {
            if (Current == null) return;
            Diagnostics.Log("Tour: ended at " + Number(Current.Step));
            Current = null;
            Raise();
        }

        /// <summary>Tells the tour in progress (if any) what happened. True when it moved on.</summary>
        public static bool Notify(TourEvent e)
        {
            var t = Current;
            if (t == null || !t.Advance(e)) return false;
            Raise();
            return true;
        }

        /// <summary>Moves to the next step when <paramref name="e"/> is what this step waits for.</summary>
        public bool Advance(TourEvent e)
        {
            if (Step == TourStep.Finished || WaitsFor(Step) != e) return false;
            var from = Step;
            Step = Step + 1;
            Diagnostics.Log("Tour: " + Number(from) + " -> " + Number(Step) + " (" + e + ")");
            return true;
        }

        /// <summary>What a step waits for.</summary>
        public static TourEvent WaitsFor(TourStep step)
        {
            switch (step)
            {
                case TourStep.LintTopic: return TourEvent.TopicLinted;
                case TourStep.Change: return TourEvent.Changed;
                case TourStep.Undo: return TourEvent.Undone;
                case TourStep.DisableRule: return TourEvent.RuleDisabled;
                default: return TourEvent.Next;
            }
        }

        /// <summary>"step 3", or "finished", for the log.</summary>
        private static string Number(TourStep s) { return s == TourStep.Finished ? "finished" : "step " + ((int)s + 1); }

        /// <summary>True when the step can't be seen from House Style, so the strip offers Next.</summary>
        public bool NeedsNext { get { return Step == TourStep.ReviewTab; } }

        /// <summary>"Step 2 of 5", or empty after the last step.</summary>
        public string Progress { get { return Step == TourStep.Finished ? "" : "Step " + ((int)Step + 1) + " of 5"; } }

        /// <summary>The hint for the current step: one plain sentence.</summary>
        public string Hint { get { return HintFor(Step); } }

        public static string HintFor(TourStep step)
        {
            switch (step)
            {
                case TourStep.LintTopic: return "Click Lint Topic on the House Style tab.";
                case TourStep.Change: return "Select an issue and click Change.";
                case TourStep.Undo: return "Press Ctrl+Z in the topic to undo the change.";
                case TourStep.DisableRule: return "Select an issue you don't want and click Disable Rule.";
                case TourStep.ReviewTab: return "Open Flare's Review tab and accept your tracked changes. Then click Next.";
                default: return FinishedText;
            }
        }

        private static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            try { h(); } catch (Exception ex) { Diagnostics.Log("tour", ex); }
        }
    }
}
