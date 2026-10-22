using System;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Settings;

namespace FlareAI.Flare.UI
{
    /// <summary>
    /// The plugin side of onboarding: ticks checklist items, moves the guided first lint on, and shows each first-time
    /// note once. State lives in <see cref="Onboarding"/> (Core); panes listen to <see cref="Changed"/> to redraw.
    /// </summary>
    internal static class GettingStarted
    {
        /// <summary>The checklist or tour changed: panes redraw their checklist and hint strip.</summary>
        public static event Action Changed;

        public static Onboarding Load() { return Onboarding.Load(); }

        /// <summary>Ticks a checklist item (saved once).</summary>
        public static void Mark(ChecklistItem item)
        {
            try
            {
                var o = Onboarding.Load();
                if (!o.Mark(item)) return;
                o.Save();
                Raise();
            }
            catch (Exception ex) { Diagnostics.Log("checklist", ex); }
        }

        /// <summary>Hides the checklist for good.</summary>
        public static void HideChecklist()
        {
            var o = Onboarding.Load();
            o.ChecklistHidden = true;
            o.Save();
            Raise();
        }

        /// <summary>Shows a first-time note as a notice, the first time only.</summary>
        public static void Note(Form owner, FirstTimeNote note)
        {
            try
            {
                var o = Onboarding.Load();
                var text = o.TakeNote(note);
                if (text == null) return;
                o.Save();
                Toast.Show(owner, text);
            }
            catch (Exception ex) { Diagnostics.Log("first-time note", ex); }
        }

        /// <summary>Tells the tour what happened, and ticks the matching checklist item.</summary>
        public static void Did(TourEvent e)
        {
            switch (e)
            {
                case TourEvent.TopicLinted: Mark(ChecklistItem.LintTopic); break;
                case TourEvent.Changed: Mark(ChecklistItem.ChangeIssue); break;
                case TourEvent.RuleDisabled: Mark(ChecklistItem.DisableRule); break;
            }
            var wasReview = GuidedTour.Current != null && GuidedTour.Current.Step == TourStep.ReviewTab;
            if (GuidedTour.Notify(e))
            {
                if (wasReview) Mark(ChecklistItem.AcceptOnReviewTab);
                if (GuidedTour.Current != null && GuidedTour.Current.Step == TourStep.Finished) Finished();
            }
        }

        /// <summary>The last step is done: remembered per writer.</summary>
        private static void Finished()
        {
            try { var o = Onboarding.Load(); o.TourDone = true; o.TourOffered = true; o.Save(); }
            catch (Exception ex) { Diagnostics.Log("tour done", ex); }
        }

        /// <summary>Skip tour, or Done after the last line.</summary>
        public static void EndTour()
        {
            try { var o = Onboarding.Load(); o.TourOffered = true; o.Save(); }
            catch (Exception ex) { Diagnostics.Log("tour end", ex); }
            GuidedTour.Stop();
        }

        private static void Raise()
        {
            var h = Changed;
            if (h == null) return;
            try { h(); } catch (Exception ex) { Diagnostics.Log("getting started", ex); }
        }
    }
}
