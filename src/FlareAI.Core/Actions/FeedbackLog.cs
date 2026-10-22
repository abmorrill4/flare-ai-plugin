using System;
using System.IO;
using Newtonsoft.Json;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Every accept / reject / edit / refine decision, one JSON line each: in the writer's personal file
    /// (%APPDATA%\FlareAI\feedback.jsonl) and, when the team has a shared feedback folder, in that writer's own file
    /// there (append-only per writer, so syncing never conflicts). The raw material for tuning the style guide.
    /// Identity: with the guide's default <c>attribution = counts</c> a record carries only an anonymous per-machine id; only
    /// <c>attribution = names</c> writes the writer's email (see <see cref="Team.FeedbackWriter"/>).
    /// </summary>
    public static class FeedbackLog
    {
        private static readonly object Gate = new object();

        /// <summary>The personal log; set by the host.</summary>
        public static string PersonalPath { get; set; }

        /// <summary>Records the decision. Returns the file that couldn't be written (logged), or null when every write worked.</summary>
        public static string Record(StyleFinding f, string topicPath, string decision, string reason = null)
        {
            if (f == null) return null;
            var team = Team.Current;
            var line = JsonConvert.SerializeObject(new
            {
                at = DateTime.UtcNow.ToString("o"), writer = team == null ? Team.AnonymousId() : team.FeedbackWriter, decision, reason,
                kind = f.Kind, source = f.Source, layer = f.Layer, rule = f.Rule, rule_id = f.RuleId, rule_hash = WordingHash(f.RuleText),
                excerpt = f.Excerpt, replacement = f.Replacement, file = f.DocumentPath ?? topicPath
            }, Formatting.None);
            var personal = Append(PersonalPath, line);
            var shared = team == null ? null : Append(team.SharedFeedbackFile, line);
            return shared ?? personal;
        }

        /// <summary>A short fingerprint of a rule's wording: decisions about an earlier wording can be told apart.</summary>
        public static string WordingHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var norm = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"\W+", " ").Trim();
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(norm)), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Null when written (or there is nowhere to write), else the path.</summary>
        private static string Append(string path, string line)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line + Environment.NewLine, ProjectRules.NoBom);
                    return null;
                }
                catch (Exception ex) { Diagnostics.Log("feedback " + path, ex); return path; }
            }
        }
    }
}
