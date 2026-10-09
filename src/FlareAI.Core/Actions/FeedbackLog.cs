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
                kind = f.Kind, source = f.Source, layer = f.Layer, rule = f.Rule, rule_id = f.RuleId,
                excerpt = f.Excerpt, replacement = f.Replacement, file = f.DocumentPath ?? topicPath
            }, Formatting.None);
            Append(PersonalPath, line);
            if (team != null) Append(team.SharedFeedbackFile, line);
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
