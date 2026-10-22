using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FlareAI.FixtureGen
{
    /// <summary>expectations.yaml: the declarative table that says where a rule should fire, keyed by the rule's tags.</summary>
    public sealed class Expectations
    {
        private Dictionary<string, string> _classes = new Dictionary<string, string>(StringComparer.Ordinal);   // context -> class
        private Dictionary<string, string> _default = new Dictionary<string, string>(StringComparer.Ordinal);   // class -> outcome
        private Dictionary<string, List<string>> _tags = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Dictionary<string, string>> _tagClasses = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Dictionary<string, List<string>>> _overrides = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        public List<string> Contexts { get; private set; }

        public static Expectations Load(string path)
        {
            var y = MiniYaml.Parse(File.ReadAllText(path));
            var e = new Expectations();
            var contexts = MiniYaml.Map(y, "contexts");
            e.Contexts = contexts.Keys.ToList();
            foreach (var kv in contexts) e._classes[kv.Key] = (string)kv.Value;
            foreach (var kv in MiniYaml.Map(y, "default")) e._default[kv.Key] = (string)kv.Value;
            foreach (var kv in MiniYaml.Map(y, "tags")) e._tags[kv.Key] = MiniYaml.Strings(kv.Value);
            foreach (var kv in MiniYaml.Map(y, "tag-classes"))
                e._tagClasses[kv.Key] = ((Dictionary<string, object>)kv.Value).ToDictionary(x => x.Key, x => (string)x.Value);
            var ov = MiniYaml.Map(y, "overrides");
            if (ov != null)
                foreach (var kv in ov)
                    e._overrides[kv.Key] = ((Dictionary<string, object>)kv.Value).ToDictionary(x => x.Key, x => MiniYaml.Strings(x.Value));
            return e;
        }

        public string ClassOf(string context) { return _classes[context]; }

        /// <summary>What a Bad case for this rule should do in this context.</summary>
        public string For(string ruleId, ICollection<string> tags, string context)
        {
            Dictionary<string, List<string>> ov;
            if (ruleId != null && _overrides.TryGetValue(ruleId, out ov))
                foreach (var outcome in new[] { Outcome.Fires, Outcome.Clean, Outcome.Any })
                    if (ov.ContainsKey(outcome) && ov[outcome].Contains(context)) return outcome;
            var cls = _classes[context];
            var result = _default[cls];
            foreach (var tag in tags)
            {
                List<string> allowed;
                if (_tags.TryGetValue(tag, out allowed) && !allowed.Contains(context)) return Outcome.Clean;
            }
            foreach (var tag in tags)
            {
                Dictionary<string, string> byClass; string o;
                if (_tagClasses.TryGetValue(tag, out byClass) && byClass.TryGetValue(cls, out o)) result = o;
            }
            return result;
        }
    }

    /// <summary>Facts about a row's pattern text that don't depend on how the engine runs it.</summary>
    public static class RuleShape
    {
        private static readonly Regex RegexChars = new Regex(@"[\\\[\]()?*+|^${}]", RegexOptions.Compiled);

        /// <summary>True when the pattern contains a tag, entity or attribute shape: lint sees text only, so it can never match a topic (#55).</summary>
        public static bool MatchesMarkup(string pattern) { return FlareAI.Core.Style.LintPatterns.MatchesMarkup(pattern); }

        /// <summary>A plain word or phrase in an Avoid cell, as opposed to a regular expression in a Pattern cell.</summary>
        public static bool IsLiteral(string avoid) { return avoid.Length > 0 && !RegexChars.IsMatch(avoid); }
    }
}
