using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using FlareAI.Core.Editing;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// The editable text of a unit of work (one block, or a few), numbered r1, r2, …: what the model sees and the only
    /// thing it may refer to or change. The model never writes markup; it names a run and gives text.
    /// </summary>
    public sealed class RunMap
    {
        public List<string> Ids { get; private set; }
        public Dictionary<string, XmlText> Runs { get; private set; }
        /// <summary>Each run's text when the map was made: answers are checked against this, off the UI thread.</summary>
        public Dictionary<string, string> Texts { get; private set; }
        /// <summary>The unit as XHTML with every editable run marked ⟦r3⟧text⟦/r3⟧ — context and addresses in one.</summary>
        public string Marked { get; private set; }

        public static RunMap Of(IList<XmlNode> blocks)
        {
            var map = new RunMap { Ids = new List<string>(), Runs = new Dictionary<string, XmlText>(StringComparer.Ordinal), Texts = new Dictionary<string, string>(StringComparer.Ordinal) };
            var live = TextRuns.Of(blocks).Where(r => r.Value.Trim().Length > 0).ToList();
            // Mark a clone so the live document is untouched: the clone's runs come in the same order.
            var clones = blocks.Select(b => b.CloneNode(true)).ToList();
            var cloneRuns = TextRuns.Of(clones).Where(r => r.Value.Trim().Length > 0).ToList();
            for (var i = 0; i < live.Count && i < cloneRuns.Count; i++)
            {
                var id = "r" + (i + 1);
                map.Ids.Add(id);
                map.Runs[id] = live[i];
                map.Texts[id] = live[i].Value;
                cloneRuns[i].Value = "⟦" + id + "⟧" + cloneRuns[i].Value + "⟦/" + id + "⟧";
            }
            // As the author reads it: pending deletions gone, pending insertions plain text (runs map one-to-one).
            map.Marked = XhtmlFragment.Serialize(AcceptedView.Of(clones));
            return map;
        }
    }

    /// <summary>
    /// Output contracts: one JSON schema per task, enforced by the API (structured outputs), plus the checks a schema
    /// can't express (an excerpt is verbatim in its run, replacement text has no markup). Enums carry the closed sets:
    /// run ids of this unit, rule ids of this guide, severities.
    /// </summary>
    public static class Contracts
    {
        public static readonly string[] Severities = { "error", "warning", "suggestion" };

        // ---- schema helpers ----------------------------------------------------------------------

        private static JObject Obj(params KeyValuePair<string, JObject>[] props)
        {
            var p = new JObject();
            foreach (var kv in props) p[kv.Key] = kv.Value;
            return new JObject
            {
                ["type"] = "object", ["properties"] = p, ["additionalProperties"] = false,
                ["required"] = new JArray(props.Select(kv => (object)kv.Key).ToArray())
            };
        }

        private static KeyValuePair<string, JObject> P(string name, JObject schema) { return new KeyValuePair<string, JObject>(name, schema); }
        private static JObject Str(string description = null) { var o = new JObject { ["type"] = "string" }; if (description != null) o["description"] = description; return o; }
        private static JObject NullableStr(string description) { return new JObject { ["anyOf"] = new JArray(new JObject { ["type"] = "string" }, new JObject { ["type"] = "null" }), ["description"] = description }; }
        private static JObject Bool(string description) { return new JObject { ["type"] = "boolean", ["description"] = description }; }
        private static JObject Enum(IEnumerable<string> values, string description = null) { var o = new JObject { ["type"] = "string", ["enum"] = new JArray(values.Distinct().Select(v => (object)v).ToArray()) }; if (description != null) o["description"] = description; return o; }
        private static JObject Arr(JObject items) { return new JObject { ["type"] = "array", ["items"] = items }; }

        // ---- review ------------------------------------------------------------------------------

        /// <summary>
        /// Findings for one unit: each names a run of the unit and a rule of the guide (or "general"). The schema is the
        /// same for every call: it is part of the cached prompt prefix, so per-unit enums (run ids, rule ids) would make
        /// every call write the cache instead of reading it. Run and rule ids are checked in code instead
        /// (<see cref="ReadReview"/>, <see cref="KeepCitable"/>).
        /// </summary>
        public static readonly JObject ReviewSchema = Obj(P("findings", Arr(Obj(
                P("run_id", Str("The run the problem is in, for example r3.")),
                P("excerpt", Str("Exact text copied from that run: the shortest unique span that has the problem.")),
                P("rule_id", Str("The id of the style-guide rule applied, from the [brackets] in the guide; \"general\" only when no rule covers it.")),
                P("rule", Str("2-4 word name of the problem.")),
                P("severity", Enum(Severities)),
                P("replacement", NullableStr("Exact plain text to put in place of the excerpt, or null if it needs a rewrite.")),
                P("why", Str("At most 15 words."))))));

        /// <summary>A rule id the model wasn't given (or made up) is cleared: the finding stays, uncited.</summary>
        public static void KeepCitable(List<StyleFinding> findings, ICollection<string> allowed)
        {
            if (allowed == null) return;
            foreach (var f in findings)
                if (f.RuleId != null && !allowed.Contains(f.RuleId)) { Diagnostics.Log("Review: cleared rule id " + f.RuleId + " (not sent for this text)"); f.RuleId = null; }
        }

        /// <summary>Anchors the model's findings in the unit's runs; drops what breaks the contract (logged).</summary>
        public static List<StyleFinding> ReadReview(JObject json, RunMap map, out int dropped)
        {
            var list = new List<StyleFinding>();
            dropped = 0;
            foreach (var f in json["findings"] ?? new JArray())
            {
                var runId = (string)f["run_id"];
                var excerpt = (string)f["excerpt"] ?? "";
                string text;
                if (excerpt.Length == 0 || runId == null || !map.Texts.TryGetValue(runId, out text)) { dropped++; continue; }
                if (text.IndexOf(excerpt, StringComparison.Ordinal) < 0)
                {
                    // Cited the wrong run but quoted real text: find the run that has it.
                    var other = map.Ids.FirstOrDefault(id => map.Texts[id].IndexOf(excerpt, StringComparison.Ordinal) >= 0);
                    if (other == null) { dropped++; continue; }
                    runId = other;
                }
                var replacement = f["replacement"] == null || f["replacement"].Type == JTokenType.Null ? null : (string)f["replacement"];
                if (replacement != null && (replacement.IndexOf('<') >= 0 || replacement.IndexOf('⟦') >= 0 || replacement == excerpt)) replacement = null;
                var rid = (string)f["rule_id"];
                list.Add(new StyleFinding
                {
                    Source = "ai", Layer = "ai", RunId = runId, RuleId = rid == "general" ? null : rid, Rule = (string)f["rule"] ?? "",
                    Severity = ((string)f["severity"] ?? "suggestion").ToLowerInvariant(), Excerpt = excerpt, Replacement = replacement,
                    Suggestion = (string)f["why"] ?? ""
                });
            }
            return list;
        }

        // ---- rewrite -----------------------------------------------------------------------------

        /// <summary>A rewrite as new text for changed runs only; structure can't change on this path. Same schema every call (cache); run ids are checked in <see cref="ReadRewrite"/>.</summary>
        public static readonly JObject RewriteSchema = Rewrite();

        private static JObject Rewrite()
        {
            return Obj(
                P("changes", Arr(Obj(
                    P("run_id", Str("The run to change, for example r3.")),
                    P("text", Str("The complete new text of that run: plain text, no markup, no run markers."))))),
                P("needs_restructure", Bool("True only if the task can't be done by changing run text (for example splitting one step into two).")),
                P("note", Str("Empty, or one sentence: what restructuring is needed, or what information only the author can supply.")));
        }

        /// <summary>New text per run, or the reasons the answer breaks the contract.</summary>
        public static Dictionary<string, string> ReadRewrite(JObject json, RunMap map, List<string> errors, out bool needsRestructure, out string note)
        {
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            needsRestructure = (bool?)json["needs_restructure"] ?? false;
            note = (string)json["note"] ?? "";
            foreach (var c in json["changes"] ?? new JArray())
            {
                var id = (string)c["run_id"];
                var text = (string)c["text"] ?? "";
                string old;
                if (id == null || !map.Texts.TryGetValue(id, out old)) { errors.Add("Unknown run " + id + "."); continue; }
                if (changes.ContainsKey(id)) { errors.Add("Run " + id + " changed twice."); continue; }
                if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0 || text.IndexOf('⟦') >= 0) { errors.Add("Run " + id + " contains markup or run markers; give plain text only."); continue; }
                // A run right after inline markup continues its sentence ("<b>Transfers</b> page. Each…"): models tend to
                // read it as a new sentence and drop the words that finish the old one. Bench: Haiku did, every time.
                var head = ContinuationHead(map, id, old);
                if (head != null && !text.TrimStart().StartsWith(head, StringComparison.Ordinal))
                { errors.Add("Run " + id + " must still start with \"" + head + "\": it finishes the sentence begun before it."); continue; }
                // Keep the run's own leading/trailing space: it separates it from the markup around it.
                var lead = old.Substring(0, old.Length - old.TrimStart().Length);
                var trail = old.Substring(old.TrimEnd().Length);
                text = lead + text.Trim() + trail;
                if (text.Trim().Length == 0) text = lead.Length > 0 ? lead : (trail.Length > 0 ? trail : " "); // a deleted run keeps one space
                if (text != old) changes[id] = text;
            }
            return changes;
        }

        /// <summary>The words that finish a sentence begun before this run (up to its first . ! ?), when the run follows inline markup mid-sentence.</summary>
        private static string ContinuationHead(RunMap map, string id, string old)
        {
            XmlText run;
            if (!map.Runs.TryGetValue(id, out run) || !(run.PreviousSibling is XmlElement)) return null;
            var t = old.TrimStart();
            if (t.Length == 0 || char.IsUpper(t[0])) return null;
            var end = t.IndexOfAny(new[] { '.', '!', '?' });
            return end < 0 ? null : t.Substring(0, end + 1);
        }

        // ---- small tasks ---------------------------------------------------------------------------

        public static JObject Refine()
        {
            return Obj(P("replacement", NullableStr("Exact plain text to put in place of the text to change; the text itself if it should stay.")),
                       P("why", Str("At most 15 words.")));
        }

        public static JObject Alt()
        {
            return Obj(P("decorative", Bool("True if the image adds nothing to the text.")),
                       P("alt", Str("One sentence, at most 125 characters; empty when decorative.")));
        }

        public static JObject AssistMeta()
        {
            return Obj(P("description", Str("What the reader can do or learn here, at most 155 characters.")),
                       P("keywords", Arr(Str("Index term; 'Parent:Child' for a subentry."))));
        }

        public static JObject AssistSynonyms()
        {
            return Obj(P("groups", Arr(Obj(P("term", Str("A term in the topic.")), P("alternatives", Arr(Str("A word readers might search for instead.")))))));
        }

        public static JObject AssistAnswers()
        {
            return Obj(P("micro_content", Arr(Obj(P("phrase", Str("A question or phrase a reader would search for.")), P("response", Str("1-2 sentences taken from the topic."))))),
                       P("glossary", Arr(Obj(P("term", Str()), P("definition", Str("One sentence from the topic's context."))))));
        }

        /// <summary>Rules extracted from notes: a row (linter-enforced) or a prose rule, each quoting its source.</summary>
        public static JObject Extract()
        {
            return Obj(P("rules", Arr(Obj(
                P("kind", Enum(new[] { "row", "prose" }, "row: a word or phrase to replace or avoid; prose: a rule that needs judgment.")),
                P("title", Str("Short name.")),
                P("avoid", Str("row: the words to avoid, comma-separated; prose: empty.")),
                P("use", Str("row: replacements, as many comma-separated as avoid, or \"(delete)\", or empty to only flag; prose: empty.")),
                P("severity", Enum(Severities)),
                P("why", Str("At most 12 words.")),
                P("example", Str("row: a short sentence containing an avoided word; prose: empty.")),
                P("text", Str("prose: the rule as one imperative sentence; row: empty.")),
                P("bad", Str("prose: a short sentence that breaks the rule; row: empty.")),
                P("good", Str("prose: the same sentence fixed; row: empty.")),
                P("quote", Str("The words in the notes this rule comes from, copied exactly."))))));
        }
    }
}
