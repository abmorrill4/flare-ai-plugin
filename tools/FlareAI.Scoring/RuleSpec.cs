using System;
using System.Collections.Generic;

namespace FlareAI.Scoring
{
    /// <summary>Matching of label <c>rules</c> entries against a finding's rule.</summary>
    /// <remarks>
    /// Entries: <c>*</c> any finding; <c>lint</c> any non-AI finding (lint and structure); <c>ai</c> any AI finding;
    /// <c>ai:&lt;kind&gt;</c> an AI finding of that judgment kind (or whose rule id is exactly that);
    /// <c>layer:*</c> every rule of a layer (<c>google:*</c>); <c>layer:page/*</c> a family (<c>google:word-list/*</c>);
    /// otherwise an exact rule id (<c>google:word-list/221</c>, <c>structure:AltText</c>). Comparison ignores case.
    /// </remarks>
    public static class RuleSpec
    {
        public static bool Matches(string spec, ScoreFinding f)
        {
            if (string.IsNullOrEmpty(spec)) return false;
            if (spec == "*") return true;
            if (spec == "lint") return !f.IsAi;
            if (spec == "ai") return f.IsAi;
            string id = f.RuleId ?? "";
            if (spec.StartsWith("ai:", StringComparison.OrdinalIgnoreCase))
            {
                if (!f.IsAi) return false;
                string kind = spec.Substring(3);
                return string.Equals(f.Kind, kind, StringComparison.OrdinalIgnoreCase) || string.Equals(id, spec, StringComparison.OrdinalIgnoreCase);
            }
            if (spec.EndsWith("/*", StringComparison.Ordinal))
                return id.StartsWith(spec.Substring(0, spec.Length - 1), StringComparison.OrdinalIgnoreCase);
            if (spec.EndsWith(":*", StringComparison.Ordinal))
                return id.StartsWith(spec.Substring(0, spec.Length - 1), StringComparison.OrdinalIgnoreCase);
            return string.Equals(id, spec, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Any(IEnumerable<string> specs, ScoreFinding f)
        {
            if (specs == null) return false;
            foreach (var s in specs) if (Matches(s, f)) return true;
            return false;
        }

        /// <summary>Layer of a rule spec, for attributing misses: the part before the first colon, <c>lint</c> for <c>*</c>/<c>lint</c>.</summary>
        public static string LayerOf(string spec)
        {
            if (string.IsNullOrEmpty(spec) || spec == "*" || spec == "lint") return "lint";
            if (spec == "ai") return "ai";
            int i = spec.IndexOf(':');
            return i > 0 ? spec.Substring(0, i).ToLowerInvariant() : "other";
        }
    }
}
