using System;
using System.IO;
using Newtonsoft.Json;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Every accept / reject decision, one JSON line each (%APPDATA%\FlareAI\feedback.jsonl, personal): the raw
    /// material for tuning the AI side of the style guide (per-rule precision, rejection reasons).
    /// </summary>
    public static class FeedbackLog
    {
        private static readonly object Gate = new object();

        public static void Record(string path, StyleFinding f, string topicPath, string decision, string reason = null)
        {
            if (path == null || f == null) return;
            var line = JsonConvert.SerializeObject(new
            {
                at = DateTime.UtcNow.ToString("o"), decision, reason, kind = f.Kind, source = f.Source, layer = f.Layer,
                rule = f.Rule, rule_id = f.RuleId, excerpt = f.Excerpt, replacement = f.Replacement, file = f.DocumentPath ?? topicPath
            }, Formatting.None);
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line + Environment.NewLine, ProjectRules.NoBom);
                }
                catch (Exception ex) { Diagnostics.Log("feedback.jsonl", ex); }
            }
        }
    }
}
