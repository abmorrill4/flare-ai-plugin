using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FlareAI.Core.Actions;

namespace FlareAI.Core.Style
{
    /// <summary>One rule a guide or project turns off (or changes the level of): what, where, who, when and why.</summary>
    public sealed class RuleOverride
    {
        /// <summary>The rule's id (google:quotation-marks/2), a linter rule name, or an uncited AI judgment's name.</summary>
        public string RuleId { get; set; }
        /// <summary>"off" or a severity.</summary>
        public string Setting { get; set; }
        public string Note { get; set; }
        public string By { get; set; }
        /// <summary>yyyy-MM-dd, or empty when not recorded.</summary>
        public string Date { get; set; }
        /// <summary>The topic or overrides.ini that holds it.</summary>
        public string File { get; set; }
        /// <summary>Where it applies, for display ("Team style guide", "My style guide", "Project Docs").</summary>
        public string Scope { get; set; }
        /// <summary>True for "# ai-ignore: name" lines: an AI judgment without a rule id.</summary>
        public bool IsAiName { get; set; }
        /// <summary>The request an owner approved for this exception ("[approved request r-…]" in its note), or null.</summary>
        public string RequestId { get; set; }
    }

    /// <summary>
    /// The turned-off rules of a guide or project, for review: the rows of its Rule/Setting tables and the lines of its
    /// overrides.ini (where "# by NAME on DATE: note" above a line records who, when and why). Turn back on removes one.
    /// </summary>
    public static class RuleOverrides
    {
        private static readonly Regex Meta = new Regex(@"^#\s*by\s+(.+?)\s+on\s+(\d{4}-\d{2}-\d{2})(?::\s*(.*))?$", RegexOptions.IgnoreCase);
        private const string AiPrefix = "# ai-ignore:";
        private static readonly Regex Approved = new Regex(@"\s*\[approved request ([^\]\s]+)\]\s*$", RegexOptions.IgnoreCase);

        /// <summary>A note marked as an owner-approved request, so Turned-off rules can flag it.</summary>
        public static string WithRequest(string note, string requestId)
        {
            return ((note ?? "").Trim() + " [approved request " + requestId + "]").Trim();
        }

        /// <summary>Every override in a guide or Flare project (its tables and overrides.ini), labelled with <paramref name="scope"/>.</summary>
        public static List<RuleOverride> Read(string root, string scope)
        {
            var list = new List<RuleOverride>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;
            list.AddRange(GuideOverrides.Read(root));
            var ini = Path.Combine(GuideOverrides.StyleFolder(root), "overrides.ini");
            if (System.IO.File.Exists(ini)) list.AddRange(ReadIni(ini));
            foreach (var o in list) o.Scope = scope;
            // The same rule twice in one file: the later line is the one in effect.
            return list.GroupBy(o => o.File + "|" + o.RuleId.ToLowerInvariant()).Select(g => g.Last()).ToList();
        }

        /// <summary>The lines of an overrides.ini ("rule = off"; "# ai-ignore: name"), with who/when/why when recorded.</summary>
        public static List<RuleOverride> ReadIni(string path)
        {
            var list = new List<RuleOverride>();
            Match meta = null;
            foreach (var raw in System.IO.File.ReadAllLines(path, Encoding.UTF8))
            {
                var l = raw.Trim().TrimStart('﻿');
                var m = Meta.Match(l);
                if (m.Success) { meta = m; continue; }
                RuleOverride o = null;
                if (l.StartsWith(AiPrefix, StringComparison.OrdinalIgnoreCase))
                    o = new RuleOverride { RuleId = l.Substring(AiPrefix.Length).Trim(), Setting = "off", IsAiName = true };
                else if (l.Length > 0 && !l.StartsWith("#") && l.IndexOf('=') > 0)
                    o = new RuleOverride { RuleId = l.Substring(0, l.IndexOf('=')).Trim(), Setting = GuideLayer.Setting(l.Substring(l.IndexOf('=') + 1).Trim()) };
                if (o != null && o.RuleId.Length > 0)
                {
                    o.File = path;
                    o.By = meta == null ? "" : meta.Groups[1].Value.Trim();
                    o.Date = meta == null ? "" : meta.Groups[2].Value;
                    o.Note = meta == null ? "" : meta.Groups[3].Value.Trim();
                    var approved = Approved.Match(o.Note);
                    if (approved.Success) { o.RequestId = approved.Groups[1].Value; o.Note = o.Note.Substring(0, approved.Index).Trim(); }
                    list.Add(o);
                }
                meta = null;   // a note belongs to the line right below it
            }
            return list;
        }

        /// <summary>The comment written above an overrides.ini line: who, when, and why.</summary>
        public static string MetaLine(string by, DateTime when, string note)
        {
            var clean = Regex.Replace(note ?? "", @"\s+", " ").Trim();
            return "# by " + Regex.Replace(string.IsNullOrEmpty(by) ? "unknown" : by, @"\s+", "_") + " on " + when.ToString("yyyy-MM-dd") + (clean.Length > 0 ? ": " + clean : "");
        }

        /// <summary>
        /// Turns the rule back on: removes its row or line (and the who/when note above it). Only owners may change a guide
        /// that has an owners file. Throws <see cref="GuideWriteException"/> naming the file when it can't be written.
        /// </summary>
        public static void TurnBackOn(RuleOverride o, Team team)
        {
            if (o == null) return;
            if (team != null) team.EnsureOwner();
            if (o.File.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) RemoveFromIni(o.File, o.RuleId, o.IsAiName);
            else GuideOverrides.Remove(o.File, o.RuleId);
        }

        private static void RemoveFromIni(string path, string ruleId, bool aiName)
        {
            var lines = System.IO.File.ReadAllLines(path, Encoding.UTF8).ToList();
            var kept = new List<string>();
            foreach (var raw in lines)
            {
                var l = raw.Trim().TrimStart('﻿');
                var match = aiName
                    ? l.StartsWith(AiPrefix, StringComparison.OrdinalIgnoreCase) && string.Equals(l.Substring(AiPrefix.Length).Trim(), ruleId, StringComparison.OrdinalIgnoreCase)
                    : !l.StartsWith("#") && l.IndexOf('=') > 0 && string.Equals(l.Substring(0, l.IndexOf('=')).Trim(), ruleId, StringComparison.OrdinalIgnoreCase);
                if (!match) { kept.Add(raw); continue; }
                if (kept.Count > 0 && Meta.IsMatch(kept[kept.Count - 1].Trim())) kept.RemoveAt(kept.Count - 1);
            }
            if (kept.Count == lines.Count) return;
            try { System.IO.File.WriteAllText(path, string.Join("\r\n", kept.ToArray()).TrimEnd() + "\r\n", ProjectRules.NoBom); }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException) { throw new GuideWriteException(path, ex); }
        }
    }
}
