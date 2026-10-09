using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FlareAI.Core.Llm;
using FlareAI.Core.Style;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.GuideCompiler
{
    /// <summary>
    /// Compiles the Google developer documentation style guide snapshot (tools/google-style/source) into rule topics in
    /// styleguide/Content/Resources/StyleGuide/Google, one topic per page, in the same format as the company rules.
    ///
    /// Each page goes to the model with its numbered source units and a strict output contract: rules (prose with Bad/Good
    /// examples, Avoid/Use rows, or patterns), each naming the units it covers, plus the units that aren't rules and why.
    /// Then, per page: every unit must be covered or marked not applicable (else one retry naming the missing units), and
    /// every lint row must pass the guide validator (its Example triggers it, its fix resolves it; else one retry with the
    /// errors, then the failing rows are dropped and their units reported). Writes compiled.json (rule ↔ units) and
    /// coverage.md.
    ///
    ///   FlareAI.GuideCompiler [--pages numbers,lists] [--model claude-opus-5-5] [--effort high] [--parallel 6]
    /// </summary>
    internal static class Program
    {
        public static readonly string[] ContentTags = FlareAI.Core.Style.ContentTags.All;
        public static readonly string[] ProductTags = { "Android", "GoogleCloud", "GoogleWorkspace" };
        private static readonly Dictionary<string, string> TriggerTag = new Dictionary<string, string>
        {
            { "code", "Code" }, { "steps", "Steps" }, { "lists", "Lists" }, { "numbers", "Numbers" }, { "table", "Tables" }, { "links", "Links" }, { "headings", "Headings" },
            { "images", "Images" }, { "ui", "UI" }, { "notes", "Notes" }, { "examples", "Examples" }, { "math", "Math" }, { "api", "API" }, { "footnotes", "Footnotes" }, { "emphasis", "Emphasis" },
            { "quotes", "Quotes" }, { "ellipses", "Ellipses" }, { "slashes", "Slashes" }, { "semicolons", "Semicolons" }, { "parentheses", "Parentheses" }, { "dashes", "Dashes" },
            { "colons", "Colons" }, { "apostrophes", "Apostrophes" }, { "hyphens", "Hyphens" }, { "abbreviations", "Abbreviations" },
        };

        private static int Main(string[] args)
        {
            try
            {
                string Opt(string name, string def) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : def; }
                var repo = Repo();
                var only = Opt("--pages", null);
                return new Compiler(repo, Opt("--model", "claude-opus-5-5"), Opt("--effort", "high"), int.Parse(Opt("--parallel", "6")),
                                    only == null ? null : new HashSet<string>(only.Split(',')), Array.IndexOf(args, "--force") >= 0, Array.IndexOf(args, "--rewrite-topics") >= 0).Run();
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        private static string Repo()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            return d.FullName;
        }

        internal static string TagFor(string trigger) { string t; return trigger != null && TriggerTag.TryGetValue(trigger, out t) ? t : null; }
    }

    internal sealed class Unit { public string Id, Heading, Text; }

    internal sealed class PageJob
    {
        public string Slug, Title, Url, Scope, Trigger;
        public List<Unit> Units;
        public string Part;   // word list: one letter section per job
    }

    internal sealed class Compiler
    {
        private readonly string _repo, _model, _effort;
        private readonly int _parallel;
        private readonly HashSet<string> _only;
        private readonly bool _force, _rewrite;
        private readonly AnthropicClient _llm;
        private readonly object _gate = new object();
        private double _usd;

        public Compiler(string repo, string model, string effort, int parallel, HashSet<string> only, bool force, bool rewrite = false)
        {
            _repo = repo; _model = model; _effort = effort; _parallel = parallel; _only = only; _force = force; _rewrite = rewrite;
            var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? DotEnv(Path.Combine(repo, ".env"), "ANTHROPIC_API_KEY");
            _llm = new AnthropicClient(null, key, model, 900);
        }

        private string Src { get { return Path.Combine(_repo, @"tools\google-style"); } }
        private string OutDir { get { return Path.Combine(_repo, @"styleguide\Content\Resources\StyleGuide\Google"); } }

        public int Run()
        {
            var coverage = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(Src, "coverage.json")))["pages"];
            var index = JObject.Parse(File.ReadAllText(Path.Combine(Src, @"source\index.json")));
            var units = JObject.Parse(File.ReadAllText(Path.Combine(Src, @"source\units.json"), Encoding.UTF8));
            Topic.Terms = ((JArray)units["word-list"]).ToDictionary(u => (string)u["id"], u =>
            {
                var t = (string)u["text"];
                var i = t.IndexOf(" link:", StringComparison.Ordinal);
                if (i < 0) i = t.IndexOf(':');
                return (string)u["kind"] == "dt" && i > 0 ? t.Substring(0, i).Trim() : null;
            });
            if (_rewrite) return RewriteTopics(coverage, index);
            var jobs = new List<PageJob>();
            foreach (var p in index["pages"])
            {
                var slug = (string)p["slug"];
                var c = (JObject)coverage[slug];
                var scope = (string)c["scope"];
                if (scope == "reference" || (_only != null && !_only.Contains(slug))) continue;
                var us = ((JArray)units[slug]).Select(u => new Unit { Id = (string)u["id"], Heading = (string)u["heading"], Text = (string)u["text"] }).ToList();
                var job = new PageJob { Slug = slug, Title = (string)p["title"], Url = (string)p["url"], Scope = scope, Trigger = (string)c["trigger"] };
                if (slug == "word-list")
                    foreach (var g in us.GroupBy(u => u.Heading))   // one letter at a time: small units, enum-sized contracts
                        jobs.Add(new PageJob { Slug = slug, Title = job.Title, Url = job.Url, Scope = scope, Trigger = job.Trigger, Units = g.ToList(), Part = g.Key });
                else { job.Units = us; jobs.Add(job); }
            }
            Console.WriteLine(jobs.Count + " jobs (" + jobs.Select(j => j.Slug).Distinct().Count() + " pages) with " + _model + "/" + _effort);

            // Pages already compiled from the same source (same hash, no errors) are kept unless --force.
            var compiledPath = Path.Combine(Src, "compiled.json");
            var compiled = File.Exists(compiledPath) ? JObject.Parse(File.ReadAllText(compiledPath)) : new JObject();
            var hashes = index["pages"].ToDictionary(p => (string)p["slug"], p => (string)p["sha256"]);
            if (!_force)
                jobs = jobs.Where(j => !(compiled[j.Slug] is JObject c && (string)c["sha256"] == hashes[j.Slug] && !((JArray)c["errors"]).Any())).ToList();
            Console.WriteLine(jobs.Count + " job(s) to run after skipping pages already compiled from the same source.");

            var results = new Dictionary<PageJob, JObject>();
            var remaining = jobs.GroupBy(j => j.Slug).ToDictionary(g => g.Key, g => g.Count());
            var done = 0;
            Directory.CreateDirectory(OutDir);
            Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = _parallel }, job =>
            {
                JObject r;
                try { r = CompileJob(job); }
                catch (Exception ex) { r = new JObject { ["error"] = ex.Message, ["rules"] = new JArray(), ["not_applicable"] = new JArray() }; }
                lock (_gate)
                {
                    results[job] = r;
                    Console.WriteLine("  " + (++done) + "/" + jobs.Count + "  " + job.Slug + (job.Part != null ? " [" + job.Part + "]" : "") + ": " + ((JArray)r["rules"]).Count + " rules" +
                                      (r["error"] != null ? "  ERROR " + r["error"] : "") + (((JArray)r["uncovered"] ?? new JArray()).Count > 0 ? "  uncovered " + ((JArray)r["uncovered"]).Count : "") + "  $" + _usd.ToString("0.00"));
                    // A page is written as soon as all its parts are in (the word list has one part per letter).
                    if (--remaining[job.Slug] == 0)
                    {
                        WritePage(jobs.Where(j => j.Slug == job.Slug).ToList(), results, compiled, hashes[job.Slug]);
                        File.WriteAllText(compiledPath, compiled.ToString(), new UTF8Encoding(false));
                    }
                }
            });
            var md = Coverage.Report(compiled, JObject.Parse(File.ReadAllText(Path.Combine(Src, "coverage.json"))), index);
            File.WriteAllText(Path.Combine(Src, "coverage.md"), md, new UTF8Encoding(false));
            Console.WriteLine(md.Split(new[] { "\n## " }, StringSplitOptions.None)[0]);
            Console.WriteLine("Cost: $" + _usd.ToString("0.00"));
            return 0;
        }

        /// <summary>One topic per page (word-list parts merged in order) and its trace entry.</summary>
        private void WritePage(List<PageJob> parts, Dictionary<PageJob, JObject> results, JObject compiled, string sha)
        {
            var first = parts[0];
            var rules = Topic.Prepare(first, new JArray(parts.SelectMany(j => (JArray)results[j]["rules"])));
            File.WriteAllText(Path.Combine(OutDir, Topic.FileName(first.Slug)), Topic.Write(first, rules), new UTF8Encoding(false));
            compiled[first.Slug] = new JObject
            {
                ["title"] = first.Title, ["url"] = first.Url, ["scope"] = first.Scope, ["sha256"] = sha, ["units"] = parts.Sum(j => j.Units.Count),
                ["rules"] = Topic.WithIds(first.Slug, rules),
                ["not_applicable"] = new JArray(parts.SelectMany(j => (JArray)results[j]["not_applicable"])),
                ["uncovered"] = new JArray(parts.SelectMany(j => (JArray)results[j]["uncovered"] ?? new JArray())),
                ["dropped"] = new JArray(parts.SelectMany(j => (JArray)results[j]["dropped"] ?? new JArray())),
                ["errors"] = new JArray(parts.Where(j => results[j]["error"] != null).Select(j => (string)results[j]["error"]))
            };
        }

        /// <summary>Regenerates every compiled topic from compiled.json (no model calls): after a change to the topic format.</summary>
        private int RewriteTopics(JObject coverage, JObject index)
        {
            var compiledPath = Path.Combine(Src, "compiled.json");
            var compiled = JObject.Parse(File.ReadAllText(compiledPath));
            foreach (var p in compiled.Properties())
            {
                var c = (JObject)p.Value;
                var page = (JObject)coverage[p.Name];
                var job = new PageJob { Slug = p.Name, Title = (string)c["title"], Url = (string)c["url"], Scope = (string)page["scope"], Trigger = (string)page["trigger"] };
                var rules = Topic.Prepare(job, (JArray)c["rules"]);
                File.WriteAllText(Path.Combine(OutDir, Topic.FileName(p.Name)), Topic.Write(job, rules), new UTF8Encoding(false));
                c["rules"] = Topic.WithIds(p.Name, rules);
            }
            File.WriteAllText(compiledPath, compiled.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Rewrote " + compiled.Count + " topics from compiled.json.");
            return 0;
        }

        // ---- one job: ask, check coverage and rows, retry once each ------------------------------------------------

        private JObject CompileJob(PageJob job)
        {
            var schema = Contract(job);
            var prompt = Prompt(job);
            var r = Ask(prompt, schema);
            var missing = Missing(job, r);
            if (missing.Count > 0)
            {
                var again = Ask(prompt + "\n\nYour previous answer left these units neither covered by a rule nor marked not applicable: " + string.Join(", ", missing.ToArray()) +
                                ". Answer again with the complete result: every unit covered or marked not applicable.", schema);
                if (Missing(job, again).Count < missing.Count) r = again;
            }
            var bad = RowErrors(job, r);
            if (bad.Count > 0)
            {
                var again = Ask(prompt + "\n\nIn your previous answer these lint rows failed the guide validator; fix them (or make them prose rules):\n" +
                                string.Join("\n", bad.Select(b => "- " + b.Value).ToArray()) + "\nAnswer again with the complete result.", schema);
                var badAgain = RowErrors(job, again);
                if (badAgain.Count < bad.Count && Missing(job, again).Count <= Missing(job, r).Count) { r = again; bad = badAgain; }
            }
            // Rows that still fail are dropped (their units show as uncovered in the report, never silently).
            var dropped = new JArray();
            var rules = (JArray)r["rules"];
            foreach (var i in bad.Keys.OrderByDescending(i => i)) { dropped.Add(new JObject { ["rule"] = rules[i], ["error"] = bad[i] }); rules.RemoveAt(i); }
            r["dropped"] = dropped;
            r["uncovered"] = new JArray(Missing(job, r).ToArray());
            return r;
        }

        private JObject Ask(string prompt, JObject schema)
        {
            var req = new LlmRequest { Task = "compile", System = System_, User = prompt, Schema = schema, Model = _model, Effort = _effort, MaxTokens = 32000 };
            var started = DateTime.UtcNow;
            var resp = AnthropicClient.Check(_llm.Complete(req), req);
            Usage.Record("compile", _model, resp, (DateTime.UtcNow - started).TotalSeconds);
            lock (_gate) _usd = Usage.Snapshot().Sum(u => u.Usd);
            return resp.Json;
        }

        private static List<string> Missing(PageJob job, JObject r)
        {
            var covered = new HashSet<string>(((JArray)r["rules"]).SelectMany(x => x["units"].Select(u => (string)u)));
            covered.UnionWith(((JArray)r["not_applicable"]).Select(x => (string)x["unit"]));
            return job.Units.Select(u => u.Id).Where(id => !covered.Contains(id)).ToList();
        }

        /// <summary>Lint rows checked by the real guide validator: the page's rows alone in a scratch guide project.</summary>
        private Dictionary<int, string> RowErrors(PageJob job, JObject r)
        {
            var errors = new Dictionary<int, string>();
            var rules = (JArray)r["rules"];
            var rows = Enumerable.Range(0, rules.Count).Where(i => (string)rules[i]["kind"] != "prose").ToList();
            if (rows.Count == 0) return errors;
            var tmp = Path.Combine(Path.GetTempPath(), "guidecompile-" + Guid.NewGuid().ToString("N"));
            var dir = Path.Combine(tmp, @"Content\Resources\StyleGuide\Google");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(tmp, "Check.flprj"), "<CatapultProject />");
            try
            {
                // One row per table, in order, so a validator issue maps back to its rule by row id.
                var only = new JArray(rows.Select(i => rules[i]));
                File.WriteAllText(Path.Combine(dir, "check.htm"), Topic.Write(job, only), new UTF8Encoding(false));
                var engine = new StyleEngine(new[] { tmp }, tmp);
                var layer = engine.Compose(null).Layers[0];
                foreach (var p in layer.Problems) errors[rows[0]] = p.Message;   // unparseable: blame the first row
                var report = engine.Validate(null);
                var idToRule = new Dictionary<string, int>();
                var lintIds = layer.Rules.Where(x => x.Id != null).Select(x => x.Id).ToList();
                for (var k = 0; k < lintIds.Count && k < rows.Count; k++) idToRule[lintIds[k]] = rows[k];
                // Every lint row must be testable: a missing Example is an error here, not just a warning.
                foreach (var issue in report.Issues.Where(i => i.Level == "error" || i.Message.StartsWith("No Example")))
                {
                    var m = Regex.Match(issue.Location ?? "", @"\((google:[^)]+)\)");
                    int idx;
                    if (m.Success && idToRule.TryGetValue(m.Groups[1].Value, out idx)) errors[idx] = (string)rules[idx]["kind"] + " '" + ((string)rules[idx]["avoid"] + (string)rules[idx]["pattern"]) + "': " + issue.Message;
                }
                // A row whose pattern didn't compile never reaches the validator: find it by count.
                if (layer.Rules.Count(x => x.Id != null) < rows.Count)
                    foreach (var i in rows)
                    {
                        var pat = (string)rules[i]["pattern"];
                        if ((string)rules[i]["kind"] == "pattern") { try { new Regex(pat); } catch (ArgumentException e) { errors[i] = "pattern '" + pat + "' is not a valid .NET regular expression: " + e.Message; } }
                    }
            }
            finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
            return errors;
        }

        // ---- prompt and contract ---------------------------------------------------------------------------------------

        private const string System_ = @"You compile the Google developer documentation style guide into rules for an AI reviewer and a linter that check MadCap Flare help topics. You are faithful to the source: you never invent, broaden or soften a rule, and you keep Google's wording where you can. You answer only in the requested JSON format.";

        private static string Prompt(PageJob job)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Page: " + job.Title + (job.Part != null ? " — section " + job.Part : "") + " (" + job.Url + ")");
            sb.AppendLine("Scope: " + (job.Scope == "pack" ? "these rules load only for sections that contain " + Program.TagFor(job.Trigger) + " content (you don't need to tag that)" : job.Scope == "lint" ? "the word list" : "core rules, loaded for every review"));
            sb.AppendLine();
            sb.AppendLine(@"Turn every rule on this page into entries of ""rules"", and list every unit that isn't a rule in ""not_applicable"". Every unit id must appear in at least one rule's ""units"" or in ""not_applicable"".

Kinds of rule:
- prose: one rule an editor applies with judgment. ""text"" is one imperative sentence in Google's words (""Use numerals for numbers 10 and greater."" ""Put conditions before instructions.""). ""bad""/""good"": a ""Not recommended""/""Recommended"" pair from the page, copied as plain text (no ""Recommended:"" label); empty strings when the page gives none. One rule per entry: split lists of separate requirements.
- row: a word or phrase to avoid with a mechanical fix, for the linter. ""avoid"": the words, comma-separated (literal text, case-sensitive if you write capitals). ""use"": the replacement (as many comma-separated as avoid, or one for all), ""(delete)"", or """" to flag only when the fix needs judgment. ""example"": a short sentence that contains one of the avoided words exactly as written (from the page if there is one). Use a row only when the replacement is always right, or when the words should always be flagged; anything that depends on context is prose.
- pattern: a .NET regular expression the linter matches, for formats words can't express (""10MB"" without a space, ""1st"" ordinals, am/pm forms). It must have almost no false positives in normal prose. ""use"" is """" (flag) unless one replacement is always right. ""example"" must contain a match.

Word list entries: ""Don't use X"" or ""use Y, not X"" with a safe replacement → row (severity warning); ""use with caution"" or advice that depends on context → prose, or a flag-only row (severity suggestion) only if the word is almost always wrong. Capitalization/spelling entries (""Capitalize A/B"") → row with the exact casing. Never make a row for a word that is correct in some ordinary sense (""master"" is fine in ""master's degree"" — make that prose).

Fields: ""heading"": the page section the rule belongs to (use the unit's heading). ""severity"": error for clear mistakes, warning for most rules, suggestion for preferences. ""why"": at most 12 words, or """". ""applies"": content tags only when the rule matters solely for sections containing that kind of content (a rule about numbered steps → Steps; a rule about heading text → Headings; a rule about link text → Links); [] otherwise. ""products"": only when the guide says the entry applies only to Android, Google Cloud or Google Workspace documentation; [] otherwise. Unused fields are """".

not_applicable: introductions, ""for more information, see…"" pointers, notes about how Google's own site is built, and anything that isn't an instruction for writing a help topic. ""reason"": a few words.");
            sb.AppendLine();
            sb.AppendLine("Units:");
            foreach (var u in job.Units) sb.AppendLine(u.Id + " [" + u.Heading + "] " + u.Text);
            return sb.ToString();
        }

        private static JObject Contract(PageJob job)
        {
            var ids = new JArray(job.Units.Select(u => (object)u.Id).ToArray());
            JObject Str() { return new JObject { ["type"] = "string" }; }
            JObject Enum(JArray v) { return new JObject { ["type"] = "string", ["enum"] = v }; }
            JObject Obj(JObject props) { return new JObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false, ["required"] = new JArray(props.Properties().Select(p => (object)p.Name).ToArray()) }; }
            var rule = Obj(new JObject
            {
                ["heading"] = Str(), ["kind"] = Enum(new JArray("prose", "row", "pattern")), ["text"] = Str(), ["bad"] = Str(), ["good"] = Str(),
                ["avoid"] = Str(), ["use"] = Str(), ["pattern"] = Str(), ["severity"] = Enum(new JArray("error", "warning", "suggestion")), ["why"] = Str(), ["example"] = Str(),
                ["applies"] = new JObject { ["type"] = "array", ["items"] = Enum(new JArray(Program.ContentTags.Cast<object>().ToArray())) },
                ["products"] = new JObject { ["type"] = "array", ["items"] = Enum(new JArray(Program.ProductTags.Cast<object>().ToArray())) },
                ["units"] = new JObject { ["type"] = "array", ["items"] = Enum(ids) },
            });
            return Obj(new JObject
            {
                ["rules"] = new JObject { ["type"] = "array", ["items"] = rule },
                ["not_applicable"] = new JObject { ["type"] = "array", ["items"] = Obj(new JObject { ["unit"] = Enum(ids), ["reason"] = Str() }) },
            });
        }

        private static string DotEnv(string path, string name)
        {
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
                if (line.StartsWith(name + "=")) return line.Substring(name.Length + 1).Trim().Trim('"');
            return null;
        }
    }

    /// <summary>Writes compiled rules as a Flare topic in the guide format: bullets with Bad/Good, Avoid/Use and Pattern tables.</summary>
    internal static class Topic
    {
        /// <summary>The rules as they go into the topic: word-list judgment rules become Term rows (same order the guide numbers them).</summary>
        public static JArray Prepare(PageJob job, JArray rules)
        {
            var outArr = new JArray();
            foreach (var r in rules)
            {
                var o = (JObject)r.DeepClone();
                o.Remove("id");
                if (job.Slug == "word-list" && (string)o["kind"] == "prose" && TermsOf(o) != null) { o["kind"] = "term"; o["term"] = TermsOf(o); }
                outArr.Add(o);
            }
            return outArr;
        }

        private static string WriteWordList(PageJob job, JArray asTerms)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\">\n    <head><title>")
              .Append(X(job.Title)).Append("</title></head>\n    <body>\n        <h1>").Append(X(job.Title)).Append("</h1>\n");
            sb.Append("        <p>Compiled from the Google developer documentation style guide, <a href=\"").Append(X(job.Url)).Append("\">").Append(X(job.Title))
              .Append("</a>, licensed CC BY 4.0. Generated: edit the company rules instead, and turn Google rules off or change them there. Term rows reach the AI only when the text uses the term.</p>\n");
            var ordered = Ordered(asTerms);
            foreach (var h in ordered.Select(r => (string)r["heading"]).Distinct())
            {
                sb.Append("        <h2>").Append(X(h)).Append("</h2>\n");
                var prose = ordered.Where(r => (string)r["heading"] == h && (string)r["kind"] == "prose").ToList();
                if (prose.Count > 0)
                {
                    sb.Append("        <ul>\n");
                    foreach (var r in prose) sb.Append("            <li").Append(Conditions(job, r)).Append(">").Append(X((string)r["text"])).Append("</li>\n");
                    sb.Append("        </ul>\n");
                }
                var terms = ordered.Where(r => (string)r["heading"] == h && (string)r["kind"] == "term").ToList();
                if (terms.Count > 0)
                {
                    sb.Append("        <table>\n            <tr><th>Term</th><th>Rule</th><th>Bad</th><th>Good</th></tr>\n");
                    foreach (var r in terms)
                        sb.Append("            <tr").Append(Conditions(job, r)).Append("><td>").Append(X((string)r["term"])).Append("</td><td>").Append(X((string)r["text"]))
                          .Append("</td><td>").Append(X((string)r["bad"])).Append("</td><td>").Append(X((string)r["good"])).Append("</td></tr>\n");
                    sb.Append("        </table>\n");
                }
                foreach (var kind in new[] { "row", "pattern" }) AppendTable(sb, job, ordered, h, kind);
            }
            sb.Append("    </body>\n</html>\n");
            return sb.ToString();
        }

        private static void AppendTable(StringBuilder sb, PageJob job, List<JToken> ordered, string h, string kind)
        {
            var rows = ordered.Where(r => (string)r["heading"] == h && (string)r["kind"] == kind).ToList();
            if (rows.Count == 0) return;
            sb.Append("        <table>\n            <tr><th>").Append(kind == "row" ? "Avoid" : "Pattern").Append("</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>\n");
            foreach (var r in rows)
                sb.Append("            <tr").Append(Conditions(job, r)).Append("><td>").Append(X(kind == "row" ? (string)r["avoid"] : (string)r["pattern"])).Append("</td><td>")
                  .Append(X((string)r["use"])).Append("</td><td>").Append(X((string)r["severity"])).Append("</td><td>").Append(X((string)r["why"]))
                  .Append("</td><td>").Append(X((string)r["example"])).Append("</td></tr>\n");
            sb.Append("        </table>\n");
        }

        public static string FileName(string slug) { return string.Join("-", slug.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)).ToArray()) + ".htm"; }

        private static string X(string s) { return SecurityElement.Escape(s ?? ""); }

        private static string Conditions(PageJob job, JToken r)
        {
            var c = new List<string>();
            var page = job.Scope == "pack" ? Program.TagFor(job.Trigger) : null;
            if (page != null) c.Add("Content." + page);
            foreach (var a in r["applies"] ?? new JArray()) if (!c.Contains("Content." + a)) c.Add("Content." + a);
            foreach (var p in r["products"] ?? new JArray()) c.Add("Product." + p);
            return c.Count == 0 ? "" : " MadCap:conditions=\"" + string.Join(",", c.ToArray()) + "\"";
        }

        /// <summary>The guide gives ids in document order per topic: prose bullets and table rows share one counter.</summary>
        public static JArray WithIds(string slug, JArray rules)
        {
            var ordered = Ordered(rules);
            var outArr = new JArray();
            var topic = GuideLayerSlug(Path.GetFileNameWithoutExtension(FileName(slug)));
            for (var i = 0; i < ordered.Count; i++) { var o = (JObject)ordered[i].DeepClone(); o["id"] = "google:" + topic + "/" + (i + 1); outArr.Add(o); }
            return outArr;
        }

        private static string GuideLayerSlug(string s) { var r = Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); return r.Length == 0 ? "x" : r; }

        /// <summary>Rules grouped by heading (in first-seen order); within a heading: prose bullets, then rows, then patterns.</summary>
        private static List<JToken> Ordered(JArray rules)
        {
            var headings = rules.Select(r => (string)r["heading"]).Distinct().ToList();
            var list = new List<JToken>();
            foreach (var h in headings)
                foreach (var kind in new[] { "prose", "term", "row", "pattern" })
                    list.AddRange(rules.Where(r => (string)r["heading"] == h && (string)r["kind"] == kind));
            return list;
        }

        /// <summary>Word list: the term(s) a rule is about, from the source units it covers ("admin link: Write out…" → "admin").</summary>
        public static Dictionary<string, string> Terms;

        private static string TermsOf(JToken r)
        {
            if (Terms == null) return null;
            var terms = (r["units"] ?? new JArray()).Select(u => { string t; return Terms.TryGetValue((string)u, out t) ? t : null; }).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
            return terms.Count == 0 ? null : string.Join(", ", terms.ToArray());
        }

        public static string Write(PageJob job, JArray rules)
        {
            // Word-list judgment rules are about one term each: a Term/Rule table, so the AI gets each only when the text uses it.
            if (job.Slug == "word-list") return WriteWordList(job, rules);
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\">\n    <head><title>")
              .Append(X(job.Title)).Append("</title></head>\n    <body>\n        <h1>").Append(X(job.Title)).Append("</h1>\n");
            sb.Append("        <p>Compiled from the Google developer documentation style guide, <a href=\"").Append(X(job.Url)).Append("\">").Append(X(job.Title))
              .Append("</a>, licensed CC BY 4.0. Generated: edit the company rules instead, and turn Google rules off or change them there.</p>\n");
            var ordered = Ordered(rules);
            foreach (var h in ordered.Select(r => (string)r["heading"]).Distinct())
            {
                sb.Append("        <h2>").Append(X(h)).Append("</h2>\n");
                var prose = ordered.Where(r => (string)r["heading"] == h && (string)r["kind"] == "prose").ToList();
                if (prose.Count > 0)
                {
                    sb.Append("        <ul>\n");
                    foreach (var r in prose)
                    {
                        sb.Append("            <li").Append(Conditions(job, r)).Append(">").Append(X((string)r["text"]));
                        var bad = (string)r["bad"]; var good = (string)r["good"];
                        if (!string.IsNullOrEmpty(bad) || !string.IsNullOrEmpty(good))
                        {
                            sb.Append("<ul>");
                            if (!string.IsNullOrEmpty(bad)) sb.Append("<li>Bad: ").Append(X(bad)).Append("</li>");
                            if (!string.IsNullOrEmpty(good)) sb.Append("<li>Good: ").Append(X(good)).Append("</li>");
                            sb.Append("</ul>");
                        }
                        sb.Append("</li>\n");
                    }
                    sb.Append("        </ul>\n");
                }
                foreach (var kind in new[] { "row", "pattern" })
                {
                    var rows = ordered.Where(r => (string)r["heading"] == h && (string)r["kind"] == kind).ToList();
                    if (rows.Count == 0) continue;
                    sb.Append("        <table>\n            <tr><th>").Append(kind == "row" ? "Avoid" : "Pattern").Append("</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>\n");
                    foreach (var r in rows)
                        sb.Append("            <tr").Append(Conditions(job, r)).Append("><td>").Append(X(kind == "row" ? (string)r["avoid"] : (string)r["pattern"])).Append("</td><td>")
                          .Append(X((string)r["use"])).Append("</td><td>").Append(X((string)r["severity"])).Append("</td><td>").Append(X((string)r["why"]))
                          .Append("</td><td>").Append(X((string)r["example"])).Append("</td></tr>\n");
                    sb.Append("        </table>\n");
                }
            }
            sb.Append("    </body>\n</html>\n");
            return sb.ToString();
        }
    }

    /// <summary>coverage.md: per page, units, rules, not-applicable units with reasons, and anything uncovered or dropped.</summary>
    internal static class Coverage
    {
        public static string Report(JObject compiled, JObject coverage, JObject index)
        {
            var pages = (JObject)coverage["pages"];
            int units = 0, covered = 0, na = 0, uncovered = 0, dropped = 0, prose = 0, rows = 0, patterns = 0;
            var lines = new List<string>();
            foreach (var p in index["pages"])
            {
                var slug = (string)p["slug"];
                var scope = (string)pages[slug]["scope"];
                if (scope == "reference") { lines.Add("| " + slug + " | reference | " + (int)p["units"] + " | — | — | — | " + (string)pages[slug]["why"] + " |"); continue; }
                var c = compiled[slug] as JObject;
                if (c == null) { lines.Add("| " + slug + " | " + scope + " | " + (int)p["units"] + " | not compiled | | | |"); continue; }
                var r = (JArray)c["rules"];
                int u = (int)c["units"], n = ((JArray)c["not_applicable"]).Select(x => (string)x["unit"]).Distinct().Count(), un = ((JArray)c["uncovered"]).Count, d = ((JArray)c["dropped"]).Count;
                units += u; na += n; uncovered += un; dropped += d; covered += u - un - n;
                prose += r.Count(x => (string)x["kind"] == "prose"); rows += r.Count(x => (string)x["kind"] == "row"); patterns += r.Count(x => (string)x["kind"] == "pattern");
                lines.Add("| " + slug + " | " + scope + (pages[slug]["trigger"] != null ? ":" + pages[slug]["trigger"] : "") + " | " + u + " | " + r.Count + " | " + n + " | " + (un + d) + " | " +
                          string.Join("; ", ((JArray)c["errors"]).Select(e => (string)e).ToArray()) + " |");
            }
            var sb = new StringBuilder();
            sb.AppendLine("# Google style guide: compile coverage");
            sb.AppendLine();
            sb.AppendLine(units + " source units in compiled pages: " + covered + " covered by rules, " + na + " not applicable (with reasons), " + uncovered + " uncovered, " + dropped + " rules dropped by the validator. " +
                          (prose + rows + patterns) + " rules: " + prose + " prose (AI), " + rows + " lint rows, " + patterns + " patterns.");
            sb.AppendLine();
            sb.AppendLine("## Pages\n\n| Page | Scope | Units | Rules | Not applicable | Uncovered + dropped | Errors / note |\n|---|---|---|---|---|---|---|");
            foreach (var l in lines) sb.AppendLine(l);
            sb.AppendLine("\n## Uncovered units and dropped rules\n");
            foreach (var c in compiled.Properties())
            {
                foreach (var u in (JArray)c.Value["uncovered"]) sb.AppendLine("- " + u);
                foreach (var d in (JArray)c.Value["dropped"]) sb.AppendLine("- dropped in " + c.Name + ": " + d["error"]);
            }
            sb.AppendLine("\n## Not applicable (reasons)\n");
            foreach (var c in compiled.Properties())
                foreach (var g in ((JArray)c.Value["not_applicable"]).GroupBy(x => (string)x["reason"]))
                    sb.AppendLine("- " + c.Name + ": " + g.Key + " (" + string.Join(", ", g.Select(x => (string)x["unit"]).ToArray()) + ")");
            return sb.ToString();
        }
    }
}
