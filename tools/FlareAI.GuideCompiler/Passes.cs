using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FlareAI.Core.Style;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.GuideCompiler
{
    /// <summary>
    /// The maintainer decisions the passes read (tools/google-style): owners.json (who owns a span shared by several pages, #56),
    /// products.json (Product tags the source doesn't spell out, #57) and fixes.json (row fixes settled by hand, #59).
    /// A missing file is an empty decision set.
    /// </summary>
    internal sealed class PassConfig
    {
        public JObject Owners = new JObject(), Products = new JObject(), Fixes = new JObject();

        public static PassConfig Load(string dir)
        {
            Func<string, JObject> read = f => { var p = Path.Combine(dir, f); return File.Exists(p) ? JObject.Parse(File.ReadAllText(p, Encoding.UTF8)) : new JObject(); };
            return new PassConfig { Owners = read("owners.json"), Products = read("products.json"), Fixes = read("fixes.json") };
        }

        /// <summary>Writes owners.json back with the owners the canonical pass chose ("resolved"), so a recompile keeps them.</summary>
        public void SaveOwners(string dir)
        {
            File.WriteAllText(Path.Combine(dir, "owners.json"), Owners.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        }

        /// <summary>term (lower case) → owner rule id: the explicit decisions.</summary>
        public Dictionary<string, string> Decisions()
        {
            return ((JArray)Owners["decisions"] ?? new JArray()).ToDictionary(d => ((string)d["term"]).ToLowerInvariant(), d => (string)d["owner"]);
        }

        public JObject Resolved
        {
            get { var r = Owners["resolved"] as JObject; if (r == null) { r = new JObject(); Owners["resolved"] = r; } return r; }
        }
    }

    /// <summary>A compiled rule and where it sits.</summary>
    internal sealed class Compiled
    {
        public string Slug;
        public JObject Page, Rule;
        public string Id { get { return Topic.RuleId(Slug, (string)Rule["key"]); } }
        public string Kind { get { return (string)Rule["kind"]; } }
        public bool IsLint { get { return Kind == "row" || Kind == "pattern"; } }
    }

    /// <summary>
    /// Deterministic passes over compiled.json (every page at once, no model calls). Each edits the compiled rules in place and
    /// notes what it did on the page ("passes"), which coverage.md lists:
    ///   1. Term scope (#58): a word-list Term cell names its own entry only; rules that claim another entry's units are noted.
    ///   2. Products (#57): Product tags from the source (an entry or section about Android, Google Cloud or Workspace
    ///      documentation, or a rule that says so) and from products.json, on every kind of rule, lint rows included.
    ///   3. Fix decisions (fixes.json): replacements settled by hand.
    ///   4. Canonical owner (#56): every lint row's Avoid terms and Example are probed against every other row; a span that
    ///      a higher-ranked rule also flags (same words, at least as broad a scope) is removed from the lower one, and a row
    ///      left with nothing is dropped. Its source units move to the owner, its key is retired. owners.json ranks pages and
    ///      settles conflicts; the choices are recorded there.
    ///   5. Fixes that create findings (#59): each row's fix is applied to its Example and the result linted with the whole
    ///      guide. A new finding gets a trailing comma tried ("for example,"), else the row becomes flag-only.
    ///   6. Validate: the guide validator over the result; its errors are noted.
    /// </summary>
    internal static class Passes
    {
        /// <summary>Structural content tags: where a rule fires once lint honours scope (#53). Punctuation tags (Numbers, Hyphens, ...) don't narrow a pattern.</summary>
        private static readonly HashSet<string> Structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Headings", "Steps", "Lists", "Tables", "Notes", "Links", "UI", "Examples", "API", "Code", "Images", "Captions" };

        public static List<string> Run(JObject compiled, JObject coverage, PassConfig cfg)
        {
            var log = new List<string>();
            ScopeTerms(compiled, log);
            TagProducts(compiled, cfg, log);
            ApplyFixDecisions(compiled, cfg, log);
            for (var round = 0; round < 3 && Canonicalize(compiled, coverage, cfg, log) > 0; round++) { }
            CheckFixes(compiled, coverage, log);
            ValidateAll(compiled, coverage, log);
            return log;
        }

        // ---- helpers ------------------------------------------------------------------------------------------------

        public static IEnumerable<Compiled> All(JObject compiled)
        {
            foreach (var p in compiled.Properties())
                foreach (var r in ((JArray)p.Value["rules"]).OfType<JObject>().ToList())
                    yield return new Compiled { Slug = p.Name, Page = (JObject)p.Value, Rule = r };
        }

        /// <summary>The page job a topic is written with (title, link, scope and pack trigger).</summary>
        public static PageJob JobOf(string slug, JObject page, JObject coverage)
        {
            var c = coverage == null ? null : coverage[slug] as JObject;
            return new PageJob { Slug = slug, Title = (string)page["title"], Url = (string)page["url"], Scope = c == null ? (string)page["scope"] : (string)c["scope"], Trigger = c == null ? null : (string)c["trigger"] };
        }

        private static void Note(JObject page, string text, List<string> log)
        {
            var notes = page["passes"] as JArray;
            if (notes == null) { notes = new JArray(); page["passes"] = notes; }
            if (!notes.Any(n => (string)n == text)) notes.Add(text);
            log.Add(text);
        }

        /// <summary>The Avoid cell as the guide reads it (comma, semicolon or newline separated).</summary>
        public static List<string> Split(string cell) { return Regex.Split(cell ?? "", @"[,;\n]+").Select(a => a.Trim()).Where(a => a.Length > 0).ToList(); }

        /// <summary>The Use cell as the guide reads it: one value per Avoid term, or one for all ("for example," is one value).</summary>
        public static List<string> Uses(string use)
        {
            use = use ?? "";
            var uses = Split(use);
            if (use.TrimEnd().EndsWith(",") && uses.Count == 1) uses = new List<string> { use.Trim() };
            return uses;
        }

        private static bool IsFix(string use) { return !GuideLayer.IsFlagOnly(use) && !GuideLayer.IsDelete(use); }

        /// <summary>Structural scope: the rule's own tags plus the pack trigger stamped on every row of its page.</summary>
        private static HashSet<string> Scope(Compiled c, JObject coverage)
        {
            var s = new HashSet<string>(((JArray)c.Rule["applies"] ?? new JArray()).Select(a => (string)a).Where(Structural.Contains), StringComparer.OrdinalIgnoreCase);
            var page = coverage == null ? null : coverage[c.Slug] as JObject;
            if (page != null && (string)page["scope"] == "pack") { var t = Program.TagFor((string)page["trigger"]); if (t != null && Structural.Contains(t)) s.Add(t); }
            return s;
        }

        /// <summary>True when <paramref name="o"/> fires everywhere <paramref name="r"/> does: unscoped, or scoped to a superset.</summary>
        private static bool AtLeastAsBroad(HashSet<string> o, HashSet<string> r) { return o.Count == 0 || (r.Count > 0 && r.IsSubsetOf(o)); }

        private static HashSet<string> ProductSet(JObject r) { return new HashSet<string>(((JArray)r["products"] ?? new JArray()).Select(p => (string)p), StringComparer.OrdinalIgnoreCase); }

        // ---- 1. term scope (#58) ------------------------------------------------------------------------------------

        private static void ScopeTerms(JObject compiled, List<string> log)
        {
            foreach (var c in All(compiled).Where(c => c.Slug == "word-list" && (c.Kind == "term" || c.Kind == "prose")))
            {
                var term = Topic.TermsOf(c.Rule);
                var claimed = Topic.ClaimedTerms(c.Rule);
                if (claimed.Count > 1)
                    Note(c.Page, c.Id + " claims units of several entries (" + string.Join("; ", claimed.ToArray()) + "); its Term is its own entry, '" + term + "'", log);
                if (c.Kind == "term" && term != null && (string)c.Rule["term"] != term)
                {
                    Note(c.Page, c.Id + " Term '" + (string)c.Rule["term"] + "' → '" + term + "'", log);
                    c.Rule["term"] = term;
                }
            }
        }

        // ---- 2. products (#57) --------------------------------------------------------------------------------------

        private static void TagProducts(JObject compiled, PassConfig cfg, List<string> log)
        {
            // names: what the source calls each product. A rule is about a product when it is scoped to it ("In Android
            // documentation, ...", "For Google Cloud ..."), sits under a section named for it (topical pages), or its fix
            // writes the product's name into text that didn't have it.
            var names = ((JObject)cfg.Products["names"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => "(?:" + (string)p.Value + ")");
            var entries = ((JObject)cfg.Products["entries"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => p.Value.Select(x => Norm((string)x)).ToList());
            var rules = ((JObject)cfg.Products["rules"] ?? new JObject()).Properties().ToDictionary(p => p.Name, p => p.Value.Select(x => (string)x).ToList(), StringComparer.OrdinalIgnoreCase);
            foreach (var c in All(compiled))
            {
                var have = ProductSet(c.Rule);
                var add = new List<KeyValuePair<string, string>>();   // product, why
                var own = (string)c.Rule["text"] + "\n" + (string)c.Rule["why"];
                foreach (var n in names)
                {
                    var scoped = new Regex(@"(?:^|[.;:!?]\s+|\n)\s*(?:In|For|Within)\s+(?:the\s+)?" + n.Value + @"\b", RegexOptions.CultureInvariant);
                    var named = new Regex(@"\b" + n.Value + @"\b", RegexOptions.CultureInvariant);
                    if (scoped.IsMatch(own)) add.Add(new KeyValuePair<string, string>(n.Key, "the rule is scoped to it"));
                    if (c.Slug != "word-list" && named.IsMatch((string)c.Rule["heading"] ?? "")) add.Add(new KeyValuePair<string, string>(n.Key, "its section is about it"));
                    if (c.IsLint && IsFix((string)c.Rule["use"]) && named.IsMatch((string)c.Rule["use"] ?? "") && !Split((string)c.Rule["avoid"]).Any(a => named.IsMatch(a)))
                        add.Add(new KeyValuePair<string, string>(n.Key, "its fix writes the product name"));
                    if (c.Slug == "word-list")
                    {
                        var def = Definition(c.Rule, Topic.EntryTerm(c.Rule) ?? (string)c.Rule["heading"]);
                        if (def != null && Regex.IsMatch(def, @"^\s*(?:In|For)\s+(?:the\s+)?" + n.Value + @"\b", RegexOptions.CultureInvariant))
                            add.Add(new KeyValuePair<string, string>(n.Key, "its entry is for " + n.Key + " documentation"));
                    }
                }
                if (c.Slug == "word-list")
                {
                    var entry = Topic.EntryTerm(c.Rule) ?? (string)c.Rule["heading"];
                    foreach (var e in entries) if (entry != null && e.Value.Contains(Norm(entry))) add.Add(new KeyValuePair<string, string>(e.Key, "products.json lists the entry"));
                }
                List<string> listed;
                if (rules.TryGetValue(c.Id, out listed)) foreach (var p in listed) add.Add(new KeyValuePair<string, string>(p, "products.json lists the rule"));
                foreach (var a in add.Where(a => !have.Contains(a.Key)).GroupBy(a => a.Key).Select(g => g.First()))
                {
                    have.Add(a.Key);
                    ((JArray)(c.Rule["products"] ?? (c.Rule["products"] = new JArray()))).Add(a.Key);
                    Note(c.Page, c.Id + " Product." + a.Key + " (" + a.Value + ")", log);
                }
            }
        }

        private static string Norm(string s) { return Regex.Replace((s ?? "").ToLowerInvariant(), @"[\s""“”']+", " ").Trim(); }

        /// <summary>The entry's own definition: the text after "term link:" in its unit, or after a glued "term link".</summary>
        private static string Definition(JObject rule, string entry)
        {
            if (Topic.UnitTexts == null || entry == null) return null;
            foreach (var u in (rule["units"] ?? new JArray()).Select(x => (string)x))
            {
                string text;
                if (!Topic.UnitTexts.TryGetValue(u, out text)) continue;
                var at = text.IndexOf(entry + " link", StringComparison.Ordinal);
                if (at < 0) continue;
                var rest = text.Substring(at + entry.Length + 5).TrimStart(':', ' ');
                return rest;
            }
            return null;
        }

        // ---- 3. fix decisions (fixes.json) ------------------------------------------------------------------------------

        private static void ApplyFixDecisions(JObject compiled, PassConfig cfg, List<string> log)
        {
            var byId = All(compiled).ToDictionary(c => c.Id, c => c);
            foreach (var d in ((JArray)cfg.Fixes["rules"] ?? new JArray()).OfType<JObject>())
            {
                Compiled c;
                if (!byId.TryGetValue((string)d["rule"], out c)) continue;   // already gone (dropped as a duplicate)
                var why = (string)d["reason"] ?? "";
                var removed = (d["remove"] ?? new JArray()).Select(x => (string)x).Where(t => RemoveTerm(c, t)).ToList();
                foreach (var t in removed) Note(c.Page, c.Id + " drops '" + t + "' (fixes.json: " + why + ")", log);
                if (removed.Count > 0) RepairExample(c, log, removed);
                if (d["use"] != null && (string)c.Rule["use"] != (string)d["use"])
                {
                    Note(c.Page, c.Id + " Use '" + (string)c.Rule["use"] + "' → '" + (string)d["use"] + "' (fixes.json: " + why + ")", log);
                    c.Rule["use"] = (string)d["use"];
                }
                foreach (var f in new[] { "pattern", "example", "severity", "why" })
                    if (d[f] != null && (string)c.Rule[f] != (string)d[f]) { c.Rule[f] = (string)d[f]; Note(c.Page, c.Id + " " + f + " → '" + (string)d[f] + "' (fixes.json)", log); }
            }
        }

        /// <summary>Removes one Avoid term from a row, with its own Use value when the row has one per term. False when absent.</summary>
        private static bool RemoveTerm(Compiled c, string term)
        {
            var avoid = Split((string)c.Rule["avoid"]);
            var i = avoid.FindIndex(a => a == term);
            if (i < 0) return false;
            var uses = Uses((string)c.Rule["use"]);
            if (uses.Count == avoid.Count && avoid.Count > 1) { uses.RemoveAt(i); c.Rule["use"] = string.Join(", ", uses.ToArray()); }
            avoid.RemoveAt(i);
            c.Rule["avoid"] = string.Join(", ", avoid.ToArray());
            return true;
        }

        // ---- 4. canonical owner (#56) -------------------------------------------------------------------------------

        /// <summary>A probe text. Variant: the term as written (0), lower case (1), or capitalized for a sentence start (2).</summary>
        private sealed class Probe { public string Text, Term; public bool Heading; public int Variant; }

        /// <summary>Raw match spans of a rule in a text (what the rule claims, before same-span collapsing); -1 swap = any.</summary>
        private static List<int[]> Spans(LintRule r, string text, bool heading, int swap = -1)
        {
            var spans = new List<int[]>();
            if (r.Applies.Contains("Headings") && !heading) return spans;
            for (var i = 0; i < r.Swaps.Count; i++)
            {
                if (swap >= 0 && i != swap) continue;
                foreach (Match m in r.Swaps[i].Key.Matches(text))
                {
                    if (m.Length == 0) continue;
                    var use = r.Swaps[i].Value;
                    if (r.Action == "replace" && use != null && string.Equals(m.Value, use, r.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;   // already right
                    spans.Add(new[] { m.Index, m.Length, i });
                }
            }
            return spans;
        }

        private static IEnumerable<Probe> TermProbes(string t)
        {
            var vs = new List<string> { t, t.ToLowerInvariant(), char.ToUpperInvariant(t[0]) + t.Substring(1) };
            for (var k = 0; k < vs.Count; k++)
            {
                if (vs.IndexOf(vs[k]) < k) continue;
                foreach (var text in new[] { vs[k], "Use " + vs[k] + " here.", vs[k] + " is used here.", "We say (" + vs[k] + ") now." })
                    foreach (var h in new[] { false, true }) yield return new Probe { Text = text, Term = vs[k], Heading = h, Variant = k };
            }
        }

        private sealed class Unit
        {
            public Compiled C; public LintRule R; public HashSet<string> Scope, Products;
            public int Tier, Key; public bool Flag;
        }

        /// <summary>One canonicalisation round. Returns how many terms or rows it removed.</summary>
        private static int Canonicalize(JObject compiled, JObject coverage, PassConfig cfg, List<string> log)
        {
            var topical = ((JArray)cfg.Owners["topical"] ?? new JArray()).OfType<JObject>().ToList();
            var decisions = cfg.Decisions();
            var resolved = cfg.Resolved;
            var byId = All(compiled).Where(c => c.IsLint).ToDictionary(c => c.Id, c => c);
            List<Unit> units;
            using (var s = new Scratch(compiled, coverage))
                units = s.Comp.Rules.Where(r => r.Id != null && byId.ContainsKey(r.Id)).Select(r =>
                {
                    var c = byId[r.Id];
                    var tier = topical.FindIndex(t => (string)t["page"] == c.Slug && (t["kinds"] == null || t["kinds"].Any(k => (string)k == c.Kind)));
                    int key; int.TryParse((string)c.Rule["key"], out key);
                    return new Unit { C = c, R = r, Scope = Scope(c, coverage), Products = ProductSet(c.Rule), Tier = tier >= 0 ? tier : c.Slug == "word-list" ? 100 : 200, Key = key == 0 ? int.MaxValue : key, Flag = !IsFix(r.Use) };
                }).ToList();

            // The owner among rules that flag the same words: the decision for these words, then the recorded owner, then
            // page rank (topical pages, the Word-List, the rest), unscoped before scoped, a fix before flag-only, the rule whose
            // entry is these words, the older key.
            Func<Unit, string, IComparable[]> rank = (u, words) =>
            {
                string d, rec = null; var w = words.ToLowerInvariant();
                decisions.TryGetValue(w, out d);
                if (resolved[w] != null) rec = (string)resolved[w];
                var heading = Norm((string)u.C.Rule["heading"]);
                return new IComparable[] { d == null ? 1 : d == u.C.Id ? 0 : 2, rec == u.C.Id ? 0 : 1, u.Tier, u.Scope.Count == 0 ? 0 : 1, u.Flag ? 1 : 0,
                                           heading.Length > 0 && (heading == Norm(words) || Topic.TermForms(heading).Any(f => Norm(f) == Norm(words))) ? 0 : 1, u.Key, u.C.Id };
            };
            Func<Unit, Unit, string, bool> outranks = (o, r, words) =>
            {
                string d;
                if (decisions.TryGetValue(words.ToLowerInvariant(), out d) && (d == o.C.Id || d == r.C.Id)) return d == o.C.Id;
                if (!o.Products.SetEquals(r.Products) || !AtLeastAsBroad(o.Scope, r.Scope)) return false;
                var a = rank(o, words); var b = rank(r, words);
                for (var i = 0; i < a.Length; i++) { var x = a[i].CompareTo(b[i]); if (x != 0) return x < 0; }
                return false;
            };

            // Probe texts: per row term (its case variants in a few sentences), per pattern its Example and Bad text plus every
            // text of the guide and its source where it matches (a pattern can be broader than its examples: it is dropped only
            // when all of that is owned elsewhere).
            var pool = (Topic.UnitTexts ?? new Dictionary<string, string>()).Values
                .Concat(All(compiled).SelectMany(c => new[] { (string)c.Rule["example"], (string)c.Rule["bad"], (string)c.Rule["good"] }))
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
            var probes = new List<Probe>();
            var probeOf = new Dictionary<Unit, List<KeyValuePair<int, List<int>>>>();   // unit → (swap index or -1, probe indexes)
            foreach (var u in units)
            {
                var list = new List<KeyValuePair<int, List<int>>>();
                if (u.C.Kind == "row")
                    for (var i = 0; i < u.R.Avoid.Count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(u.R.Avoid[i])) continue;
                        var idx = new List<int>();
                        foreach (var p in TermProbes(u.R.Avoid[i])) { idx.Add(probes.Count); probes.Add(p); }
                        list.Add(new KeyValuePair<int, List<int>>(i, idx));
                    }
                else
                {
                    var idx = new List<int>();
                    var texts = new[] { u.R.Example, (string)u.C.Rule["bad"] }.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    texts.AddRange(pool.Where(t => !texts.Contains(t) && Spans(u.R, t, true).Count > 0));
                    foreach (var t in texts)
                        foreach (var h in new[] { false, true }) { idx.Add(probes.Count); probes.Add(new Probe { Text = t, Heading = h }); }
                    list.Add(new KeyValuePair<int, List<int>>(-1, idx));
                }
                probeOf[u] = list;
            }

            // Every rule's spans in every probe, once.
            var spanCache = new Dictionary<int, Dictionary<Unit, List<int[]>>>();
            Func<int, Dictionary<Unit, List<int[]>>> spansIn = pi =>
            {
                Dictionary<Unit, List<int[]>> d;
                if (spanCache.TryGetValue(pi, out d)) return d;
                d = new Dictionary<Unit, List<int[]>>();
                foreach (var u in units) { var sp = Spans(u.R, probes[pi].Text, probes[pi].Heading); if (sp.Count > 0) d[u] = sp; }
                spanCache[pi] = d;
                return d;
            };

            var removed = 0;
            var gone = new HashSet<Unit>();                                   // dropped this round: they cover nothing any more
            var goneSwaps = new Dictionary<Unit, HashSet<int>>();             // terms removed this round
            Func<Unit, int[], bool> live = (o, x) => !gone.Contains(o) && !(goneSwaps.ContainsKey(o) && goneSwaps[o].Contains(x[2]));
            foreach (var u in units)
            {
                var removeTerms = new List<KeyValuePair<int, string>>();
                var owners = new HashSet<Unit>();
                var chosen = new List<KeyValuePair<string, Unit>>();
                var merge = new List<KeyValuePair<Unit, string>>();   // sentence-start forms the owner gains with the term
                var allCovered = true;
                foreach (var kv in probeOf[u])
                {
                    var covered = true; var any = false;
                    var termOwners = new List<Unit>();
                    var startForms = new HashSet<string>();
                    string words = null;
                    foreach (var pi in kv.Value)
                    {
                        var mine = Spans(u.R, probes[pi].Text, probes[pi].Heading, kv.Key);
                        if (mine.Count == 0) continue;
                        var others = spansIn(pi);
                        foreach (var m in mine)
                        {
                            words = words ?? (kv.Key >= 0 ? u.R.Avoid[kv.Key] : probes[pi].Text.Substring(m[0], m[1]));
                            var w = words;
                            var by = others.Where(o => o.Key != u && o.Value.Any(x => x[0] == m[0] && x[1] == m[1] && live(o.Key, x)) && outranks(o.Key, u, w)).Select(o => o.Key).ToList();
                            if (by.Count == 0)
                            {
                                // A sentence-start capital the owner spells case-sensitively: the owner can take that form over.
                                if (probes[pi].Variant == 2) { startForms.Add(probes[pi].Term); continue; }
                                covered = false; break;
                            }
                            any = true;
                            termOwners.Add(by.OrderBy(o => o, Comparer<Unit>.Create((a, b) => { var ra = rank(a, w); var rb = rank(b, w); for (var i = 0; i < ra.Length; i++) { var x = ra[i].CompareTo(rb[i]); if (x != 0) return x; } return 0; })).First());
                        }
                        if (!covered) break;
                    }
                    if (!any || !covered) { allCovered = false; continue; }
                    if (startForms.Count > 0 && !(termOwners[0].C.Kind == "row" && Uses((string)termOwners[0].C.Rule["use"]).Count <= 1)) { allCovered = false; continue; }
                    foreach (var f in startForms) merge.Add(new KeyValuePair<Unit, string>(termOwners[0], f));
                    owners.UnionWith(termOwners);
                    chosen.Add(new KeyValuePair<string, Unit>(words, termOwners[0]));
                    if (kv.Key >= 0) removeTerms.Add(new KeyValuePair<int, string>(kv.Key, u.R.Avoid[kv.Key]));
                }
                if (owners.Count == 0) continue;
                var ownerIds = string.Join(", ", owners.Select(o => o.C.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
                var drop = u.C.Kind == "pattern" ? allCovered : removeTerms.Count == u.R.Avoid.Count(a => !string.IsNullOrWhiteSpace(a));
                if (!drop && u.C.Kind == "pattern") continue;   // a pattern is never cut down, only dropped when all of it is owned elsewhere
                foreach (var ch in chosen) resolved[ch.Key.ToLowerInvariant()] = ch.Value.C.Id;
                foreach (var mf in merge)
                {
                    var avoid = Split((string)mf.Key.C.Rule["avoid"]);
                    if (avoid.Contains(mf.Value)) continue;
                    avoid.Add(mf.Value);
                    mf.Key.C.Rule["avoid"] = string.Join(", ", avoid.ToArray());
                    Note(mf.Key.C.Page, mf.Key.C.Id + " takes over '" + mf.Value + "' from " + u.C.Id, log);
                }
                if (drop)
                {
                    Drop(u.C, owners.Select(o => o.C).ToList(), "duplicate of " + ownerIds, log);
                    gone.Add(u);
                    removed++;
                }
                else
                {
                    goneSwaps[u] = new HashSet<int>(removeTerms.Select(t => t.Key));
                    foreach (var t in removeTerms)
                        if (RemoveTerm(u.C, t.Value)) { removed++; Note(u.C.Page, u.C.Id + " drops '" + t.Value + "' (owned by " + ownerIds + ")", log); }
                    RepairExample(u.C, log, removeTerms.Select(t => t.Value).ToList());
                }
            }

            // Explicit drops: nested overlaps that no probe shows as the same span (owners.json "drop").
            var all = All(compiled).ToDictionary(c => c.Id, c => c);
            foreach (var d in ((JArray)cfg.Owners["drop"] ?? new JArray()).OfType<JObject>())
            {
                Compiled c, o;
                if (all.TryGetValue((string)d["rule"], out c) && all.TryGetValue((string)d["owner"], out o)) { Drop(c, new List<Compiled> { o }, "owners.json: " + (string)d["why"], log); removed++; }
            }
            return removed;
        }

        /// <summary>Removes a rule: its source units move to the owner(s) (coverage holds), its key is retired (never reused).</summary>
        private static void Drop(Compiled c, List<Compiled> owners, string why, List<string> log)
        {
            ((JArray)c.Page["rules"]).Remove(c.Rule);
            foreach (var o in owners)
            {
                var units = (JArray)(o.Rule["units"] ?? (o.Rule["units"] = new JArray()));
                foreach (var u in (c.Rule["units"] ?? new JArray()).Select(x => (string)x)) if (!units.Any(x => (string)x == u)) units.Add(u);
            }
            var retired = c.Page["retired"] as JArray;
            if (retired == null) { retired = new JArray(); c.Page["retired"] = retired; }
            if (!retired.Any(x => (string)x["key"] == (string)c.Rule["key"]))
                retired.Add(new JObject { ["key"] = c.Rule["key"], ["text"] = (string)c.Rule["avoid"] + (string)c.Rule["pattern"], ["retired"] = DateTime.UtcNow.ToString("yyyy-MM-dd"), ["reason"] = why });
            Note(c.Page, c.Id + " dropped (" + (string)c.Rule["avoid"] + (string)c.Rule["pattern"] + "): " + why, log);
        }

        /// <summary>
        /// After terms were removed, the Example must still contain one: a source sentence that does, else the old Example
        /// with a remaining term in place of the removed one, else a plain sentence with the term.
        /// </summary>
        private static void RepairExample(Compiled c, List<string> log, List<string> removedTerms)
        {
            var avoid = Split((string)c.Rule["avoid"]);
            var ex = (string)c.Rule["example"] ?? "";
            Func<string, string> rx = t => @"(?<![\w])" + Regex.Escape(t).Replace(@"\ ", @"\s+") + @"(?![\w])";
            Func<string, string, bool> has = (text, t) => Regex.IsMatch(text, rx(t), t == t.ToLowerInvariant() ? RegexOptions.IgnoreCase : RegexOptions.None);
            if (avoid.Count == 0 || avoid.Any(t => has(ex, t))) return;
            string found = null;
            foreach (var u in (c.Rule["units"] ?? new JArray()).Select(x => (string)x))
            {
                string text;
                if (Topic.UnitTexts == null || !Topic.UnitTexts.TryGetValue(u, out text)) continue;
                text = Regex.Replace(text, @"\b(?:Not recommended|Recommended|Also recommended)\s*:\s*", "\n");
                text = Regex.Replace(text, @"(?:^|(?<=[.!?]\s))[^.:\n]{1,80}? link:?\s", "\n");   // word-list entry labels ("unarchive link: ...")
                var sentences = Regex.Split(text, @"(?<=[.!?])\s+|\n").Select(x => Regex.Replace(x.Trim(), @"\s+([,.;:!?])", "$1")).Where(x => x.Length > 12 && x.Length < 200 && avoid.Any(t => has(x, t))).ToList();
                found = sentences.FirstOrDefault(x => !removedTerms.Any(t => has(x, t))) ?? sentences.FirstOrDefault();
                if (found != null) break;
            }
            if (found == null)
                foreach (var gone in removedTerms)
                    if (has(ex, gone)) { found = Regex.Replace(ex, rx(gone), avoid[0].Replace("$", "$$"), gone == gone.ToLowerInvariant() ? RegexOptions.IgnoreCase : RegexOptions.None); break; }
            found = found ?? "Avoid " + avoid[0] + " in this sentence.";
            Note(c.Page, c.Id + " Example → '" + found + "'", log);
            c.Rule["example"] = found;
        }

        // ---- 5. fixes that create findings (#59) ------------------------------------------------------------------------

        private static void CheckFixes(JObject compiled, JObject coverage, List<string> log)
        {
            var byId = All(compiled).Where(c => c.IsLint).ToDictionary(c => c.Id, c => c);
            List<CreatedFinding> bad;
            using (var s = new Scratch(compiled, coverage)) bad = Created(s.Comp, byId);
            foreach (var b in bad)
            {
                var c = b.Row;
                var use = (string)c.Rule["use"];
                var uses = Uses(use);
                // "for example" where the guide wants "for example,": the new finding is on the inserted words and a word follows.
                // One value for every Avoid term (the engine re-capitalizes at a sentence start and drops the comma before punctuation).
                string comma = null;
                if (b.OnInsertedWords && uses.Count > 0 && !use.TrimEnd().EndsWith(",") && uses.All(x => string.Equals(x, uses[0], StringComparison.OrdinalIgnoreCase)))
                    comma = (uses.Any(x => x == x.ToLowerInvariant()) ? uses.First(x => x == x.ToLowerInvariant()) : uses[0]) + ",";
                if (comma != null)
                {
                    c.Rule["use"] = comma;
                    List<CreatedFinding> again;
                    using (var s = new Scratch(compiled, coverage)) again = Created(s.Comp, new Dictionary<string, Compiled> { { c.Id, c } });
                    if (again.Count == 0) { Note(c.Page, c.Id + " Use '" + use + "' → '" + comma + "' (" + b.Why + ")", log); continue; }
                    c.Rule["use"] = use;
                }
                c.Rule["use"] = "";
                Note(c.Page, c.Id + " flag-only, was Use '" + use + "' (" + b.Why + ")", log);
            }
        }

        private sealed class CreatedFinding { public Compiled Row; public string Why; public bool OnInsertedWords; }

        /// <summary>
        /// Rows whose fix, applied to their Example (and to each bare Avoid term), leaves a finding that wasn't there. A finding
        /// of a rule that already flagged the replaced words is the same finding moved, not a new one.
        /// </summary>
        private static List<CreatedFinding> Created(Composition comp, Dictionary<string, Compiled> rows)
        {
            var result = new List<CreatedFinding>();
            foreach (var r in comp.Rules.Where(r => r.Id != null && rows.ContainsKey(r.Id) && r.Action == "replace"))
            {
                var texts = new List<string>();
                if (!string.IsNullOrEmpty(r.Example)) texts.Add(r.Example);
                if (rows[r.Id].Kind == "row") texts.AddRange(r.Avoid.Where(a => a.Length > 0));
                foreach (var text in texts)
                {
                    var before = comp.Lint(new[] { text }, null, true);
                    var mine = before.Where(h => h.RuleId == r.Id && h.Replacement != null).ToList();
                    if (mine.Count == 0) continue;
                    var chosen = new List<LintHit>();
                    foreach (var h in mine.OrderByDescending(h => h.Length))
                        if (!chosen.Any(x => h.Start < x.Start + x.Length && x.Start < h.Start + h.Length)) chosen.Add(h);
                    var moved = new HashSet<string>(before.Where(b => b.RuleId != r.Id && chosen.Any(h => b.Start < h.Start + h.Length && h.Start < b.Start + b.Length)).Select(b => b.RuleId));
                    var sb = new StringBuilder();
                    var inserted = new List<int[]>();
                    var at = 0;
                    foreach (var h in chosen.OrderBy(h => h.Start))
                    {
                        sb.Append(text, at, h.Start - at);
                        inserted.Add(new[] { sb.Length, sb.Length + h.Replacement.Length });
                        sb.Append(h.Replacement);
                        at = h.Start + h.Length;
                    }
                    sb.Append(text.Substring(at));
                    var t = sb.ToString();
                    var created = comp.Lint(new[] { t }, null, true).FirstOrDefault(h => h.RuleId != r.Id && !moved.Contains(h.RuleId) && !before.Any(b => b.RuleId == h.RuleId && b.Match == h.Match));
                    if (created == null) continue;
                    var onInserted = inserted.Any(s => created.Start < s[1] && s[0] < created.Start + created.Length && s[1] < t.Length && char.IsWhiteSpace(t[s[1]]));
                    result.Add(new CreatedFinding { Row = rows[r.Id], OnInsertedWords = onInserted, Why = "the fix '" + t + "' is flagged by " + created.RuleId + " ('" + created.Match + "')" });
                    break;
                }
            }
            return result;
        }

        // ---- 6. validate -------------------------------------------------------------------------------------------------

        private static void ValidateAll(JObject compiled, JObject coverage, List<string> log)
        {
            var bySlug = compiled.Properties().ToDictionary(p => Topic.RuleId(p.Name, "x").Replace("/x", "/"), p => (JObject)p.Value);
            using (var s = new Scratch(compiled, coverage))
                foreach (var i in s.Comp.Validate().Issues.Where(i => i.Level == "error"))
                {
                    var m = Regex.Match(i.Location ?? "", @"\((google:[^/]+/)");
                    JObject page;
                    if (m.Success && bySlug.TryGetValue(m.Groups[1].Value, out page)) Note(page, "validator: " + i.Location + ": " + i.Message, log);
                    else log.Add("validator: " + i.File + " " + i.Location + ": " + i.Message);
                }
        }
    }

    /// <summary>The compiled guide written to a scratch guide project (every product condition defined, so product rows lint too).</summary>
    internal sealed class Scratch : IDisposable
    {
        public readonly string Root;
        public readonly Composition Comp;

        public Scratch(JObject compiled, JObject coverage)
        {
            Root = Path.Combine(Path.GetTempPath(), "guidepasses-" + Guid.NewGuid().ToString("N"));
            var dir = Path.Combine(Root, @"Content\Resources\StyleGuide\Google");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(Root, @"Project\ConditionTagSets"));
            File.WriteAllText(Path.Combine(Root, "Check.flprj"), "<CatapultProject />");
            File.WriteAllText(Path.Combine(Root, @"Project\ConditionTagSets\Product.flcts"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<CatapultConditionTagSet>" + string.Join("", Program.ProductTags.Select(p => "<ConditionTag Name=\"" + p + "\" />").ToArray()) + "</CatapultConditionTagSet>\n");
            foreach (var p in compiled.Properties())
            {
                var job = Passes.JobOf(p.Name, (JObject)p.Value, coverage);
                File.WriteAllText(Path.Combine(dir, Topic.FileName(p.Name)), Topic.Write(job, Topic.Prepare(job, (JArray)p.Value["rules"])), new UTF8Encoding(false));
            }
            Comp = new StyleEngine(new[] { Root }, Root).Compose(null);
        }

        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
