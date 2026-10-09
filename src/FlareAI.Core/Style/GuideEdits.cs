using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// Turns a Tune-report item into a drafted change to the style guide: finds the rule's list item or table row in
    /// the guide topic, and writes the instruction for the AI from the team's evidence. The owner reviews the draft
    /// as a readable diff and applies it as tracked changes.
    /// </summary>
    public static class GuideEdits
    {
        private static readonly Regex Word = new Regex(@"[\p{L}\p{N}']+", RegexOptions.Compiled);

        /// <summary>The list item, table row or paragraph whose text best matches the rule's text, or null.</summary>
        public static XmlNode Locate(XmlDocument doc, string ruleText)
        {
            var body = doc == null ? null : BlockSelector.FindBody(doc);
            if (body == null || string.IsNullOrEmpty(ruleText)) return null;
            var want = Words(ruleText);
            XmlNode best = null;
            var bestScore = 0.0;
            foreach (XmlNode n in body.SelectNodes(".//*[local-name()='li' or local-name()='tr' or local-name()='p']"))
            {
                // A bullet's own text (its nested Bad/Good list is part of the rule's block but not of its wording).
                var own = n.LocalName == "li" ? OwnText(n) : Spaced(n);
                var have = Words(own);
                if (have.Count == 0) continue;
                var common = 0;
                foreach (var w in want) if (have.Contains(w)) common++;
                var score = (double)common / (want.Count + have.Count - common);
                if (score > bestScore) { bestScore = score; best = n; }
            }
            return bestScore >= 0.3 ? best : null;
        }

        private static string OwnText(XmlNode li)
        {
            var sb = new StringBuilder();
            foreach (XmlNode c in li.ChildNodes)
                if (!(c.NodeType == XmlNodeType.Element && (c.LocalName == "ul" || c.LocalName == "ol"))) sb.Append(Spaced(c)).Append(' ');
            return sb.ToString();
        }

        /// <summary>Text with a space between text nodes, so table cells don't run together.</summary>
        private static string Spaced(XmlNode n)
        {
            if (n.NodeType == XmlNodeType.Text || n.NodeType == XmlNodeType.CDATA) return n.Value;
            var sb = new StringBuilder();
            foreach (XmlNode t in n.SelectNodes(".//text()")) sb.Append(t.Value).Append(' ');
            return sb.ToString();
        }

        private static HashSet<string> Words(string s)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Word.Matches(s ?? "")) { var w = m.Value.Trim('\'').ToLowerInvariant(); if (w.Length > 0) set.Add(w); }
            return set;
        }

        /// <summary>What the AI should change in the rule, given what the team's decisions show.</summary>
        public static string Instruction(TuneItem item, GuideRule rule, bool isTableRow)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are editing the team's style guide, not product documentation. This block is one rule: " +
                          (isTableRow ? "a table row with cells Avoid, Use, Severity, Why, Example." : "a bullet, optionally with a nested list of \"Bad: …\" and \"Good: …\" examples."));
            sb.AppendLine("Rule " + (rule == null ? item.Rule : rule.Id + ": " + rule.Text));
            sb.AppendLine().AppendLine("What the writers' review decisions show:");
            sb.AppendLine("- " + item.Title);
            foreach (var e in item.Evidence) sb.AppendLine("- " + e);
            sb.AppendLine();
            switch (item.Kind)
            {
                case "disputed":
                    sb.Append("Writers disagree. Reword the rule so it says when it applies and when it doesn't, based on the excerpts; keep it one rule. ");
                    break;
                case "review":
                    sb.Append("The rule is often rejected. Narrow its wording to the cases where it is right, or state the exception the excerpts show. ");
                    break;
                case "edit":
                    sb.Append("Writers keep replacing the text with the same alternative. Make that alternative the rule's replacement (the Use cell, or the rule's wording). ");
                    break;
                case "ignore":
                    sb.Append("Writers asked to turn the rule off for the reasons above. Add the exception they need to the rule's wording instead of dropping it. ");
                    break;
            }
            sb.Append(isTableRow
                ? "Keep exactly one row with the same five cells. In Avoid and Use, keep comma-separated lists paired one to one. Keep the Example a sentence that breaks the rule."
                : "Keep one bullet. If it has Bad/Good examples, keep them and update them so Bad still breaks the rule and Good follows it; add a Bad/Good pair if the change needs one.");
            sb.Append(" Change nothing else.");
            return sb.ToString();
        }
    }
}
