using System;
using System.IO;
using Newtonsoft.Json;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Every accept / reject / edit / refine decision, one JSON line each: in the writer's personal file
    /// (%APPDATA%\FlareAI\feedback.jsonl) and, when the team has a shared feedback folder, in that writer's own file
    /// there (append-only per writer, so syncing never conflicts). The raw material for tuning the style guide.
    /// </summary>
    public static class FeedbackLog
    {
        private static readonly object Gate = new object();

        /// <summary>The personal log; set by the host.</summary>
        public static string PersonalPath { get; set; }

        public static void Record(StyleFinding f, string topicPath, string decision, string reason = null)
        {
            if (f == null) return;
            var team = Team.Current;
            var line = JsonConvert.SerializeObject(new
            {
                at = DateTime.UtcNow.ToString("o"), writer = team == null ? Environment.UserName : team.Writer, decision, reason,
                kind = f.Kind, source = f.Source, layer = f.Layer, rule = f.Rule, rule_id = f.RuleId, rule_hash = WordingHash(f.RuleText),
                excerpt = f.Excerpt, replacement = f.Replacement, file = f.DocumentPath ?? topicPath
            }, Formatting.None);
            Append(PersonalPath, line);
            if (team != null) Append(team.SharedFeedbackFile, line);
        }

        /// <summary>A short fingerprint of a rule's wording: decisions about an earlier wording can be told apart.</summary>
        public static string WordingHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var norm = System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"\W+", " ").Trim();
            using (var sha = System.Security.Cryptography.SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(norm)), 0, 4).Replace("-", "").ToLowerInvariant();
        }

        private static void Append(string path, string line)
        {
            if (string.IsNullOrEmpty(path)) return;
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line + Environment.NewLine, ProjectRules.NoBom);
                }
                catch (Exception ex) { Diagnostics.Log("feedback " + path, ex); }
            }
        }
    }
}
