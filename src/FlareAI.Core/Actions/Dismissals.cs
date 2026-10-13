using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Findings the author dismissed ("not an issue here"), remembered per file by rule + text + context so they
    /// stay hidden on later checks and across sessions. Stored in %APPDATA%\FlareAI\dismissed.json (personal).
    /// </summary>
    public sealed class Dismissals
    {
        private readonly string _path;
        private readonly HashSet<string> _keys;
        private readonly object _gate = new object();

        private Dismissals(string path, IEnumerable<string> keys)
        {
            _path = path;
            _keys = new HashSet<string>(keys ?? new string[0], StringComparer.Ordinal);
        }

        public static Dismissals Load(string path)
        {
            try
            {
                if (path != null && File.Exists(path))
                    return new Dismissals(path, JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path, Encoding.UTF8)));
            }
            catch (Exception ex) { Diagnostics.Log("dismissed.json", ex); }
            return new Dismissals(path, null);
        }

        public bool IsDismissed(StyleFinding f, string topicPath)
        {
            lock (_gate) return _keys.Contains(Key(f, topicPath));
        }

        public void Dismiss(StyleFinding f, string topicPath)
        {
            lock (_gate)
            {
                if (!_keys.Add(Key(f, topicPath)) || _path == null) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path));
                    File.WriteAllText(_path, JsonConvert.SerializeObject(new List<string>(_keys), Formatting.Indented), ProjectRules.NoBom);
                }
                catch (Exception ex) { Diagnostics.Log("dismissed.json", ex); }
            }
        }

        private static string Key(StyleFinding f, string topicPath)
        {
            var file = (f.DocumentPath ?? topicPath ?? "").ToLowerInvariant();
            return file + "|" + f.Rule + "|" + f.Excerpt + "|" + (f.Before ?? "");
        }
    }

    /// <summary>
    /// "Ignore this rule in this project": the project's own style layer (Content/Resources/StyleGuide/overrides.ini),
    /// so the decision is shared with the team through source control. Linter rules become Vale "Rule = NO" lines;
    /// AI rule names (free text) become "# ai-ignore: name" lines that the plugin filters on.
    /// </summary>
    public static class ProjectRules
    {
        public static string OverridesPath(string anyProjectPath)
        {
            var dir = string.IsNullOrEmpty(anyProjectPath) ? null : new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(anyProjectPath)));
            while (dir != null && dir.GetFiles("*.flprj").Length == 0) dir = dir.Parent;
            return dir == null ? null : Path.Combine(dir.FullName, @"Content\Resources\StyleGuide\overrides.ini");
        }

        internal static readonly Encoding NoBom = new UTF8Encoding(false);

        public static bool IsLinterRule(StyleFinding f) { return f.Layer != "ai" && f.Rule != null && f.Rule.Contains("."); }

        /// <param name="setting">off, suggestion, warning or error.</param>
        public static void Ignore(string anyProjectPath, StyleFinding f, string setting = "off")
        {
            var path = OverridesPath(anyProjectPath);
            if (path == null) throw new InvalidOperationException("This topic is not inside a Flare project.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // A guide rule by its permanent id (lint and AI alike); an uncited AI judgment by its name.
            var line = f.RuleId != null ? f.RuleId + " = " + setting
                     : IsLinterRule(f) ? f.Rule + " = " + (setting == "off" ? "NO" : setting)
                     : "# ai-ignore: " + f.Rule;
            var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿') : "# Rules turned off for this project (House Style, \"Turn off this rule\"). Vale syntax: Style.Rule = NO\r\n";
            if (existing.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0) return;
            File.WriteAllText(path, existing.TrimEnd() + "\r\n" + line + "\r\n", NoBom); // ini readers choke on a BOM
        }

        /// <summary>AI rule names this project ignores (lower case).</summary>
        public static HashSet<string> IgnoredAiRules(string anyProjectPath)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = OverridesPath(anyProjectPath);
            if (path == null || !File.Exists(path)) return set;
            foreach (var l in File.ReadAllLines(path, Encoding.UTF8))
                if (l.StartsWith("# ai-ignore:", StringComparison.OrdinalIgnoreCase)) set.Add(l.Substring("# ai-ignore:".Length).Trim());
            return set;
        }
    }
}
