using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FlareAI.Core.Actions
{
    /// <summary>One rule's share of a batch: how many fixes, in which files, with a few real before/after examples.</summary>
    public sealed class RuleGroup
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public List<StyleFinding> Findings { get; } = new List<StyleFinding>();
        public List<string> Files { get; } = new List<string>();
        public List<string> Examples { get; } = new List<string>();
        /// <summary>Unticked rules are left out of the batch.</summary>
        public bool Selected { get; set; }
    }

    /// <summary>A snippet in a batch that other topics include: a fix there changes all of them.</summary>
    public sealed class SharedSnippet
    {
        public string Path { get; set; }
        /// <summary>How many topics and snippets include it.</summary>
        public int Uses { get; set; }
        public int Fixes { get; set; }
    }

    /// <summary>A file the batch will not touch, and why (read-only, not found).</summary>
    public sealed class SkippedFile
    {
        public string Path { get; set; }
        public string Reason { get; set; }
        public int Fixes { get; set; }
    }

    /// <summary>
    /// What a batch of fixes will do, before it does it: per rule the count and files, up to three real
    /// before-and-after examples, and the files that will be skipped. One policy for every batch: a batch that
    /// reaches a file other than the open topic is previewed; a batch only in the open topic writes directly.
    /// </summary>
    public sealed class BatchPlan
    {
        public const int MaxExamples = 3;

        public List<RuleGroup> Groups { get; } = new List<RuleGroup>();
        public List<SkippedFile> Skipped { get; } = new List<SkippedFile>();
        /// <summary>Snippets the batch changes that other topics include: flagged in the preview.</summary>
        public List<SharedSnippet> SharedSnippets { get; } = new List<SharedSnippet>();
        public string OpenTopicPath { get; private set; }

        /// <param name="skipReason">Returns why a file can't be written (for example "read-only"), or null when it can.</param>
        /// <param name="snippetUses">Optional: how many topics use a snippet, shown next to it.</param>
        public static BatchPlan Build(IEnumerable<StyleFinding> fixes, string openTopicPath, Func<string, string> skipReason, Func<string, int> snippetUses = null)
        {
            var plan = new BatchPlan { OpenTopicPath = openTopicPath };
            var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fixes)
            {
                var path = f.DocumentPath ?? openTopicPath;
                if (path != null && !IsOpenTopic(path, openTopicPath) && skipReason != null)
                {
                    string why;
                    if (!reasons.TryGetValue(path, out why)) reasons[path] = why = skipReason(path);
                    if (why != null)
                    {
                        var s = plan.Skipped.Find(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                        if (s == null) plan.Skipped.Add(s = new SkippedFile { Path = path, Reason = why });
                        s.Fixes++;
                        continue;
                    }
                }
                var key = f.Rule ?? "";
                var g = plan.Groups.Find(x => x.Key == key);
                if (g == null)
                {
                    g = new RuleGroup { Key = key, Selected = true, Label = !string.IsNullOrEmpty(f.RuleSection) ? f.RuleSection : key.Substring(key.IndexOf('.') + 1) };
                    plan.Groups.Add(g);
                }
                g.Findings.Add(f);
                if (RuleBacklog.IsSnippet(path) && snippetUses != null)
                {
                    var shared = plan.SharedSnippets.Find(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
                    if (shared == null) plan.SharedSnippets.Add(shared = new SharedSnippet { Path = path, Uses = snippetUses(path) });
                    shared.Fixes++;
                }
                var label = FileLabel(path, snippetUses);
                if (!g.Files.Contains(label)) g.Files.Add(label);
                if (g.Examples.Count < MaxExamples)
                {
                    var ex = Example(f);
                    if (!g.Examples.Contains(ex)) g.Examples.Add(ex);
                }
            }
            plan.Groups.Sort((a, b) => b.Findings.Count.CompareTo(a.Findings.Count));
            plan.SharedSnippets.RemoveAll(x => x.Uses < 2);
            plan.SharedSnippets.Sort((a, b) => b.Uses.CompareTo(a.Uses));
            return plan;
        }

        /// <summary>
        /// The warning for snippets other topics include, or null: "3 fixes are in snippets that other topics include:
        /// Note.flsnp (used in 14 topics). Changing a snippet changes every topic that includes it."
        /// </summary>
        public string SnippetNote()
        {
            var selected = SharedSnippets.FindAll(x => Groups.Exists(g => g.Selected && g.Findings.Exists(f => string.Equals(f.DocumentPath, x.Path, StringComparison.OrdinalIgnoreCase))));
            if (selected.Count == 0) return null;
            var fixes = 0;
            var names = new List<string>();
            foreach (var x in selected)
            {
                fixes += x.Fixes;
                if (names.Count < 3) names.Add(Path.GetFileName(x.Path) + " (used in " + x.Uses + " topics)");
            }
            if (selected.Count > 3) names.Add("and " + (selected.Count - 3) + " more");
            return (fixes == 1 ? "1 change is" : fixes + " changes are") + " in " + (selected.Count == 1 ? "a snippet" : "snippets") + " that other topics include: " +
                   string.Join(", ", names.ToArray()) + ". Changing a snippet changes every topic that includes it.";
        }

        /// <summary>The path inside the project's Content folder ("accounts\about.htm"): many topics share a file name.</summary>
        public static string ProjectPath(string path)
        {
            var i = path.IndexOf("\\Content\\", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? path.Substring(i + 9) : Path.GetFileName(path);
        }

        private static bool IsOpenTopic(string path, string open) { return open != null && string.Equals(path, open, StringComparison.OrdinalIgnoreCase); }

        private static string FileLabel(string path, Func<string, int> snippetUses)
        {
            var name = path == null ? "this topic" : ProjectPath(path);
            if (path != null && string.Equals(Path.GetExtension(path), ".flsnp", StringComparison.OrdinalIgnoreCase) && snippetUses != null)
            {
                var n = snippetUses(path);
                name += " (snippet, used in " + n + " " + (n == 1 ? "topic" : "topics") + ")";
            }
            return name;
        }

        private static string Example(StyleFinding f)
        {
            var from = "\"" + (f.Excerpt ?? "") + "\"";
            if (string.IsNullOrEmpty(f.Replacement)) return "delete " + from;
            return "change " + from + " to \"" + f.Replacement + "\"";
        }

        public List<StyleFinding> SelectedFindings
        {
            get
            {
                var all = new List<StyleFinding>();
                foreach (var g in Groups) if (g.Selected) all.AddRange(g.Findings);
                return all;
            }
        }

        public int SelectedCount { get { return SelectedFindings.Count; } }

        /// <summary>Distinct files (the open topic counts as one) the ticked rules will change.</summary>
        public int SelectedFileCount
        {
            get
            {
                var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in SelectedFindings) files.Add(f.DocumentPath ?? OpenTopicPath ?? "");
                return files.Count;
            }
        }

        /// <summary>True when any fix lands outside the open topic: the author must see and confirm the batch first.</summary>
        public bool NeedsPreview
        {
            get
            {
                foreach (var g in Groups)
                    foreach (var f in g.Findings)
                        if (f.DocumentPath != null && !IsOpenTopic(f.DocumentPath, OpenTopicPath)) return true;
                return false;
            }
        }

        /// <summary>"12 changes in 5 files" for the heading or the Change button.</summary>
        public static string Counts(int fixes, int files)
        {
            return fixes + (fixes == 1 ? " change" : " changes") + " in " + files + (files == 1 ? " file" : " files");
        }

        /// <summary>The files and examples for one rule, as plain lines.</summary>
        public static string Details(RuleGroup g)
        {
            var sb = new StringBuilder();
            sb.AppendLine(g.Label + ": " + Counts(g.Findings.Count, g.Files.Count));
            sb.AppendLine();
            const int ShowFiles = 8;
            for (var i = 0; i < g.Files.Count && i < ShowFiles; i++) sb.AppendLine("  " + g.Files[i]);
            if (g.Files.Count > ShowFiles) sb.AppendLine("  and " + (g.Files.Count - ShowFiles) + " more");
            sb.AppendLine();
            sb.AppendLine("Examples:");
            foreach (var e in g.Examples) sb.AppendLine("  " + e);
            return sb.ToString();
        }

        /// <summary>Why a file can't be written, or null when it can.</summary>
        public static string WhyNotWritable(string path)
        {
            try
            {
                if (!File.Exists(path)) return "not found";
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) return "read-only";
            }
            catch (Exception) { return "can't be read"; }
            return null;
        }

        /// <summary>The confirmation for one snippet fix: it changes every topic that includes the snippet.</summary>
        public static string SnippetQuestion(string snippetPath, int topics)
        {
            return "This changes " + Path.GetFileName(snippetPath) + ", used in " + topics + (topics == 1 ? " topic" : " topics") + ". Make the change?";
        }
    }

    /// <summary>What a batch did, file by file: changed, skipped (with the reason), failed.</summary>
    public sealed class BatchResult
    {
        public sealed class Entry { public string Path; public int Fixes; public string Reason; public bool Saved; }

        public List<Entry> Changed { get; } = new List<Entry>();
        public List<Entry> Skipped { get; } = new List<Entry>();
        public List<Entry> Failed { get; } = new List<Entry>();
        /// <summary>Files a stopped batch never reached: not changed.</summary>
        public List<Entry> NotStarted { get; } = new List<Entry>();

        public void AddChanged(string path, int fixes, bool savedAndClosed) { Changed.Add(new Entry { Path = path, Fixes = fixes, Saved = savedAndClosed }); }
        public void AddSkipped(string path, int fixes, string reason) { Skipped.Add(new Entry { Path = path, Fixes = fixes, Reason = reason }); }
        public void AddFailed(string path, int fixes, string reason) { Failed.Add(new Entry { Path = path, Fixes = fixes, Reason = reason }); }
        public void AddNotStarted(string path, int fixes) { NotStarted.Add(new Entry { Path = path, Fixes = fixes, Reason = "stopped before this file" }); }

        public int FixesApplied { get { var n = 0; foreach (var e in Changed) n += e.Fixes; return n; } }
        public int SavedFiles { get { return Changed.FindAll(e => e.Saved).Count; } }

        /// <summary>One honest sentence. Ctrl+Z is promised only for edits still open in the editor.</summary>
        public string Status()
        {
            var sb = new StringBuilder();
            var fixes = FixesApplied;
            sb.Append(fixes == 0 ? "No changes were made" : "Made " + fixes + (fixes == 1 ? " tracked change" : " tracked changes"));
            var saved = SavedFiles;
            var open = Changed.Count - saved;
            if (fixes > 0)
            {
                if (saved > 0)
                    sb.Append(". Changes in " + saved + (saved == 1 ? " file are" : " files are") + " tracked and saved. Accept them on Flare's Review tab"
                              + (open > 0 ? ". The open topic can also use Ctrl+Z" : ""));
                else sb.Append(". Ctrl+Z undoes them");
            }
            var skippedFixes = 0;
            foreach (var e in Skipped) skippedFixes += e.Fixes;
            if (skippedFixes > 0) sb.Append(". Skipped " + skippedFixes + (skippedFixes == 1 ? " change" : " changes") + " (see the list)");
            if (Failed.Count == 1) sb.Append(". Could not change " + Name(Failed[0].Path) + " (" + Failed[0].Reason + ")");
            else if (Failed.Count > 1) sb.Append(". Could not change " + Failed.Count + " files (see the list)");
            if (NotStarted.Count > 0) sb.Append(". Stopped. " + NotStarted.Count + (NotStarted.Count == 1 ? " file was" : " files were") + " not changed");
            return sb.Append('.').ToString();
        }

        /// <summary>The list shown in the pane after a batch.</summary>
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Status());
            foreach (var e in Changed) sb.AppendLine("Changed: " + Name(e.Path) + " (" + e.Fixes + ")");
            foreach (var e in Skipped) sb.AppendLine("Skipped: " + Name(e.Path) + " (" + e.Fixes + (e.Fixes == 1 ? " change, " : " changes, ") + e.Reason + ")");
            foreach (var e in Failed) sb.AppendLine("Failed: " + Name(e.Path) + " (" + e.Reason + ")");
            foreach (var e in NotStarted) sb.AppendLine("Not changed: " + Name(e.Path) + " (" + e.Fixes + (e.Fixes == 1 ? " change, " : " changes, ") + e.Reason + ")");
            return sb.ToString().TrimEnd();
        }

        private static string Name(string path) { return string.IsNullOrEmpty(path) ? "this topic" : Path.GetFileName(path); }
    }

    /// <summary>Plain wording for the AI cost shown in the pane footer.</summary>
    public static class AiCost
    {
        public static string Session(int calls, double usd)
        {
            if (calls == 0) return "AI this session: no calls yet";
            var cost = usd < 0.01 ? "under $0.01" : "about $" + usd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return "AI this session: " + calls + (calls == 1 ? " call" : " calls") + " (" + cost + ")";
        }

        public static string Session() { int c; double u; FlareAI.Core.Llm.Usage.Totals(out c, out u); return Session(c, u); }
    }
}
