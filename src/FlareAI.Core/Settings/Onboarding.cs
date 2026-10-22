using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace FlareAI.Core.Settings
{
    /// <summary>The getting-started checklist, in the order the pane lists it.</summary>
    public enum ChecklistItem
    {
        LintTopic = 0,
        ChangeIssue = 1,
        AcceptOnReviewTab = 2,
        DisableRule = 3,
        LintProject = 4,
        AiRewrite = 5
    }

    /// <summary>A note shown once, the first time something happens.</summary>
    public enum FirstTimeNote
    {
        FirstChange = 0,
        FirstAiRewrite = 1
    }

    /// <summary>
    /// What a writer has seen and done while getting started: the welcome, the guided first lint, the checklist, the
    /// first-time notes and the first Lint Project. Per user (onboarding.json under <see cref="Paths.Root"/>). Never
    /// throws: a missing or damaged file starts over, and a file that can't be written only means a note may show again.
    /// </summary>
    public sealed class Onboarding
    {
        /// <summary>How long the checklist stays in the pane's empty state.</summary>
        public static readonly TimeSpan ChecklistDays = TimeSpan.FromDays(7);

        /// <summary>When House Style first started for this writer (UTC).</summary>
        public DateTime? FirstStart { get; set; }
        /// <summary>The guided first lint was offered (run, skipped or finished): it doesn't start on its own again.</summary>
        public bool TourOffered { get; set; }
        /// <summary>The writer finished the guided first lint.</summary>
        public bool TourDone { get; set; }
        /// <summary>The writer chose Hide on the checklist.</summary>
        public bool ChecklistHidden { get; set; }
        /// <summary>The first Lint Project has run (later ones list every rule at once).</summary>
        public bool ProjectLinted { get; set; }
        /// <summary>Checklist items done.</summary>
        public List<ChecklistItem> Done { get; set; }
        /// <summary>First-time notes already shown.</summary>
        public List<FirstTimeNote> NotesShown { get; set; }

        [JsonIgnore] public string FilePath { get; private set; }

        public Onboarding()
        {
            Done = new List<ChecklistItem>();
            NotesShown = new List<FirstTimeNote>();
        }

        /// <summary>The file in effect: onboarding.json under <see cref="Paths.Root"/>.</summary>
        public static string DefaultPath { get { return Paths.In("onboarding.json"); } }

        public static Onboarding Load() { return Load(DefaultPath); }

        public static Onboarding Load(string path)
        {
            Onboarding o = null;
            try { if (File.Exists(path)) o = JsonConvert.DeserializeObject<Onboarding>(File.ReadAllText(path)); }
            catch (Exception ex) { Diagnostics.Log("onboarding: read", ex); }
            o = o ?? new Onboarding();
            if (o.Done == null) o.Done = new List<ChecklistItem>();
            if (o.NotesShown == null) o.NotesShown = new List<FirstTimeNote>();
            o.FilePath = path;
            return o;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception ex) { Diagnostics.Log("onboarding: write", ex); }
        }

        /// <summary>Remembers the first start (once). True the very first time.</summary>
        public bool Started(DateTime nowUtc)
        {
            if (FirstStart.HasValue) return false;
            FirstStart = nowUtc;
            return true;
        }

        // ---- checklist -------------------------------------------------------------------------------------------

        /// <summary>The checklist's text for an item.</summary>
        public static string Label(ChecklistItem item)
        {
            switch (item)
            {
                case ChecklistItem.LintTopic: return "Lint a topic";
                case ChecklistItem.ChangeIssue: return "Change an issue";
                case ChecklistItem.AcceptOnReviewTab: return "Accept a change on the Review tab";
                case ChecklistItem.DisableRule: return "Disable a rule";
                case ChecklistItem.LintProject: return "Lint the project";
                default: return "Try an AI rewrite";
            }
        }

        /// <summary>The items listed: every one, except the AI rewrite when AI is off.</summary>
        public static IList<ChecklistItem> Items(bool aiOn)
        {
            var list = new List<ChecklistItem>();
            foreach (ChecklistItem i in Enum.GetValues(typeof(ChecklistItem)))
                if (aiOn || i != ChecklistItem.AiRewrite) list.Add(i);
            return list;
        }

        public bool IsDone(ChecklistItem item) { return Done.Contains(item); }

        /// <summary>Ticks an item. True when it wasn't ticked before (then save).</summary>
        public bool Mark(ChecklistItem item)
        {
            if (Done.Contains(item)) return false;
            Done.Add(item);
            return true;
        }

        /// <summary>Items done, of those listed.</summary>
        public int DoneCount(bool aiOn)
        {
            var n = 0;
            foreach (var i in Items(aiOn)) if (IsDone(i)) n++;
            return n;
        }

        /// <summary>
        /// The checklist shows in the pane's empty state during the first week, until every listed item is done or the
        /// writer hides it.
        /// </summary>
        public bool ChecklistVisible(DateTime nowUtc, bool aiOn)
        {
            if (ChecklistHidden) return false;
            if (FirstStart.HasValue && nowUtc - FirstStart.Value > ChecklistDays) return false;
            return DoneCount(aiOn) < Items(aiOn).Count;
        }

        // ---- first-time notes ------------------------------------------------------------------------------------

        public static string NoteText(FirstTimeNote note)
        {
            return note == FirstTimeNote.FirstChange
                ? "The change is in your topic as a tracked change. Accept it on the Review tab to keep it."
                : "Each AI rewrite costs a few cents. The running total is at the bottom of this pane.";
        }

        /// <summary>The note's text the first time it is asked for, then null.</summary>
        public string TakeNote(FirstTimeNote note)
        {
            if (NotesShown.Contains(note)) return null;
            NotesShown.Add(note);
            return NoteText(note);
        }
    }
}
