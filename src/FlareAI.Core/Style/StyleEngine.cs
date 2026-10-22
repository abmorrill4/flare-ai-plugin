using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// The layered style guide, in process: Google (the Google/ folder of the guide project, or the copy bundled with
    /// the plugin), then the guide project's own rules, then the open Flare project (its variables, glossary and
    /// StyleGuide folder). Later layers win. Lint runs here too: every lint rule is a row of a guide table.
    /// </summary>
    public sealed class StyleEngine : IStyleService
    {
        private readonly Func<IEnumerable<string>> _roots;
        private readonly string _bundled;
        private readonly object _gate = new object();
        private readonly Dictionary<string, KeyValuePair<string, Composition>> _cache = new Dictionary<string, KeyValuePair<string, Composition>>(StringComparer.OrdinalIgnoreCase);

        /// <param name="guideRoots">The team's style guide projects (usually one), in order.</param>
        /// <param name="bundledGuide">The style guide project shipped with the plugin: its Google/ folder is the baseline when a guide has none.</param>
        public StyleEngine(IEnumerable<string> guideRoots, string bundledGuide)
            : this(() => guideRoots, bundledGuide)
        {
        }

        /// <param name="guideRoots">
        /// The guide projects in effect, asked again before each use: a guide created after the engine (the writer's own,
        /// made on their first rule change) becomes a layer without a restart.
        /// </param>
        public StyleEngine(Func<IEnumerable<string>> guideRoots, string bundledGuide)
        {
            _roots = guideRoots ?? (() => new string[0]);
            _bundled = bundledGuide;
        }

        /// <summary>The guide projects that exist now, in order.</summary>
        private List<string> Guides()
        {
            return (_roots() ?? new string[0]).Where(g => !string.IsNullOrEmpty(g) && Directory.Exists(g)).ToList();
        }

        // ---- composition ---------------------------------------------------------------------------------------

        public Composition Compose(string project)
        {
            var guides = Guides();
            var root = FlareProjectRoot(project);
            if (root != null && guides.Any(g => SamePath(g, root))) root = null;   // the guide is already a layer
            var key = (root ?? "") + "|" + string.Join("|", guides.ToArray());
            // Any change to a guide or project file (a rule turned off, a row added) changes the stamp: the next use reloads.
            var stamp = Stamp(guides.Concat(new[] { GoogleRoot(guides), root }).Where(p => p != null));
            lock (_gate)
            {
                KeyValuePair<string, Composition> c;
                if (_cache.TryGetValue(key, out c) && c.Key == stamp) return c.Value;
            }
            var layers = new List<GuideLayer> { GuideLayer.Load(GoogleRoot(guides), "google", "Google") };
            foreach (var g in guides) layers.Add(GuideLayer.Load(g, null, null, "Google"));
            if (root != null) layers.Add(GuideLayer.Load(root, "project"));
            var comp = new Composition(layers);
            lock (_gate) _cache[key] = new KeyValuePair<string, Composition>(stamp, comp);
            return comp;
        }

        /// <summary>Forgets every composition: the next use reads the guides again.</summary>
        public void Invalidate() { lock (_gate) _cache.Clear(); }

        /// <summary>The project whose Google/ folder is the baseline: the first team guide that has one, else the bundled guide.</summary>
        private string GoogleRoot(List<string> guides)
        {
            foreach (var g in guides)
                if (Directory.Exists(Path.Combine(g, @"Content\Resources\StyleGuide\Google"))) return g;
            return _bundled;
        }

        /// <summary>Changes when any guide file changes (cheap: names, sizes and write times, no reading).</summary>
        private static string Stamp(IEnumerable<string> roots)
        {
            long n = 0, latest = 0;
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (var dir in new[] { @"Content\Resources\StyleGuide", @"Project\VariableSets", @"Project\Glossaries", "" })
                {
                    var d = dir.Length == 0 ? r : Path.Combine(r, dir);
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in dir.Length == 0 ? Directory.GetFiles(d) : Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        n += fi.Length + f.Length;
                        latest = Math.Max(latest, fi.LastWriteTimeUtc.Ticks);
                    }
                }
            return n + ":" + latest;
        }

        public static string FlareProjectRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var d = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
                for (; d != null; d = d.Parent) if (d.Exists && d.GetFiles("*.flprj").Length > 0) return d.FullName;
            }
            catch (Exception) { }
            return null;
        }

        private static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        // ---- IStyleService ---------------------------------------------------------------------------------------

        public IList<LintHit> Lint(IList<string> texts, IList<int> headings, string project)
        {
            return Compose(project).Lint(texts, headings);
        }

        public IList<LintHit> Lint(IList<LintLine> lines, string project)
        {
            return Compose(project).Lint(lines);
        }

        public string Guide(string project) { return Compose(project).Guide(); }

        /// <summary>
        /// [0] the rules that always apply; [1] the rules this unit calls for: packs for its content, and Term/Rule rows
        /// for words it uses. Rules for products the guide doesn't cover are in neither.
        /// </summary>
        public string[] GuideFor(string project, GuideScope scope)
        {
            var comp = Compose(project);
            Func<GuideRule, bool> core, packs;
            Selectors(comp, scope, out core, out packs);
            // [1] content packs repeat across sections (worth caching); [2] word rules are particular to this text.
            return new[] { comp.Guide(core), comp.Guide(r => packs(r) && r.Triggers.Count == 0, packsOnly: true), comp.Guide(r => packs(r) && r.Triggers.Count > 0, packsOnly: true) };
        }

        public IList<string> RuleIdsFor(string project, GuideScope scope)
        {
            var comp = Compose(project);
            Func<GuideRule, bool> core, packs;
            Selectors(comp, scope, out core, out packs);
            return comp.Items.Where(r => core(r) || packs(r)).Select(r => r.Id).ToList();
        }

        /// <summary>
        /// Always-on: untagged prose rules. Packs: rules tagged for content the unit has, and Term rows for words it uses.
        /// Never: lint rows (the linter enforces them) and rules for products the guide doesn't cover.
        /// </summary>
        private static void Selectors(Composition comp, GuideScope scope, out Func<GuideRule, bool> core, out Func<GuideRule, bool> packs)
        {
            scope = scope ?? new GuideScope();
            var always = scope.Always;
            Func<GuideRule, bool> applies = r => r.Applies.Count == 0 || r.Applies.Any(scope.Content.Contains) || r.Applies.Any(always.Contains);
            Func<GuideRule, bool> eligible = r => !r.IsLint && comp.ForOurProducts(r.Products) && !comp.IsOff(r.Id);
            // Always-on: untagged rules plus the packs nearly every section needs; one block, cached for an hour.
            core = r => eligible(r) && r.Triggers.Count == 0 && (r.Applies.Count == 0 || r.Applies.Any(always.Contains));
            packs = r => eligible(r) && (r.Triggers.Count > 0 ? applies(r) && r.TriggeredBy(scope.Text)
                                                              : r.Applies.Count > 0 && !r.Applies.Any(always.Contains) && r.Applies.Any(scope.Content.Contains));
        }

        public IList<GuideRule> Rules(string project) { return Compose(project).Items; }

        public bool IsOff(string project, string ruleId) { return Compose(project).IsOff(ruleId); }

        public IList<GuideExample> Examples(string project)
        {
            return Compose(project).Items.Where(i => i.Bad.Length > 0 || i.Good.Length > 0)
                .Select(i => new GuideExample { Id = i.Id, Text = i.Text, Bad = i.Bad, Good = i.Good }).ToList();
        }

        public GuideReport Validate(string project) { return Compose(project).Validate(); }

        /// <summary>
        /// Writes permanent keys into the team guide's own rules (not the generated Google folder, not docs projects).
        /// For style guide owners: run before Validate. Returns how many rules got keys.
        /// </summary>
        public int AssignKeys()
        {
            var n = 0;
            // Never the guide bundled with the plugin: it is read-only, and plugin updates replace it.
            foreach (var g in Guides().Where(g => _bundled == null || !SamePath(g, _bundled))) n += GuideLayer.Load(g, null, null, "Google").AssignKeys();
            lock (_gate) _cache.Clear();
            return n;
        }

        /// <summary>Terms every layer's glossary defines (for topic assists: don't propose what's already there).</summary>
        public List<string> GlossaryTerms(string project)
        {
            return Compose(project).Layers.SelectMany(l => l.Glossary).Select(g => g.Term).Where(t => !string.IsNullOrEmpty(t)).ToList();
        }

        public List<GlossaryEntry> Glossary(string project) { return Compose(project).Layers.SelectMany(l => l.Glossary).ToList(); }

        /// <summary>Candidate rules from a Flare project's own content, minus what the layered guide already flags.</summary>
        public List<RuleCandidate> Mine(string project, out int files)
        {
            var root = FlareProjectRoot(project);
            if (root == null) throw new InvalidOperationException("Not inside a Flare project: " + project);
            var result = StyleMiner.Mine(root);
            files = result.Files;
            var comp = Compose(project);
            var forms = result.Candidates.SelectMany(c => c.Forms.Select(f => f.Text)).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
            var flagged = new HashSet<string>(forms.Count == 0 ? new string[0] : comp.Lint(forms, null).Select(h => forms[h.Index]));
            return result.Candidates.Where(c => c.Kind == "heading" || !c.Forms.Where(f => f.Text != c.Prefer).All(f => flagged.Contains(f.Text))).ToList();
        }
    }

    /// <summary>An ordered set of layers: the guide text, the citable rules and the compiled lint rules.</summary>
    public sealed class Composition
    {
        private static readonly Regex SentenceStart = new Regex(@"(^|[.!?:]\s+)$", RegexOptions.Compiled);
        private static readonly Dictionary<string, int> Rank = new Dictionary<string, int> { { "error", 0 }, { "warning", 1 }, { "suggestion", 2 } };

        public List<GuideLayer> Layers { get; private set; }
        public List<GuideRule> Items { get; private set; }
        public List<LintRule> Rules { get; private set; }
        private readonly Dictionary<string, string> _overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Composition(List<GuideLayer> layers)
        {
            Layers = layers;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in layers)
            {
                var b = GuideLayer.Camel(l.Name); var name = b; var i = 2;
                while (!taken.Add(name)) name = b + i++;
                l.Style = name;
                foreach (var r in l.Rules) r.Name = name + "." + r.LocalName;
                foreach (var o in l.Overrides) _overrides[o.Key.IndexOf('.') < 0 && o.Key.IndexOf(':') < 0 ? "Google." + o.Key : o.Key] = o.Value;   // later layers win
            }
            Items = layers.SelectMany(l => l.Items).ToList();
            Rules = layers.SelectMany(l => l.Rules).ToList();
            OurProducts = new HashSet<string>(layers.Skip(1).SelectMany(l => l.ProductTags), StringComparer.OrdinalIgnoreCase);
        }

        /// <param name="include">Which rules to include (null = all).</param>
        /// <param name="packsOnly">Label the result as the rule packs for this text (the always-on rules come separately).</param>
        public string Guide(Func<GuideRule, bool> include = null, bool packsOnly = false)
        {
            var parts = new List<string>();
            for (var i = 0; i < Layers.Count; i++)
            {
                var g = Layers[i].Guide(include).Trim();
                if (g.Length > 0) parts.Add("# " + (packsOnly ? "Rules for what this text contains, layer: " : "Layer: ") + Layers[i].Name + " (" + (i == 0 ? "baseline" : "overrides the layers above") + ")\n\n" + g);
            }
            return parts.Count == 0 ? "" : string.Join("\n\n---\n\n", parts.ToArray()) + "\n";
        }

        /// <summary>Products the guide covers: the Product condition tags its own projects define (the Google layer defines none).</summary>
        public HashSet<string> OurProducts { get; private set; }

        /// <summary>A rule tagged for products applies only when the guide covers one of them.</summary>
        public bool ForOurProducts(ICollection<string> products)
        {
            return products == null || products.Count == 0 || products.Any(OurProducts.Contains);
        }

        /// <summary>True when a Rule/Setting table (or overrides.ini) turns this rule off: then neither the linter nor the AI uses it.</summary>
        public bool IsOff(string ruleId)
        {
            string v;
            return ruleId != null && _overrides.TryGetValue(ruleId, out v) && v == "off";
        }

        /// <summary>"off", a severity, or null: what the layers above say about this rule.</summary>
        private string Override(LintRule r)
        {
            string v;
            if (r.Id != null && _overrides.TryGetValue(r.Id, out v)) return v;
            return _overrides.TryGetValue(r.Name, out v) ? v : null;
        }

        // ---- lint --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Exact findings in plain-text lines whose context isn't known beyond "heading or not" (MCP, tests, the miner).
        /// Rules scoped to headings (Content.Headings) run on heading lines only; other Content scopes aren't enforced.
        /// </summary>
        /// <param name="all">Every rule's hits, overlapping ones included (validation tests each rule on its own).</param>
        public List<LintHit> Lint(IList<string> texts, IList<int> headings, bool all = false)
        {
            var heads = new HashSet<int>(headings ?? new int[0]);
            var lines = new List<LintLine>(texts.Count);
            for (var i = 0; i < texts.Count; i++) lines.Add(LintLine.Plain(texts[i], heads.Contains(i)));
            return Lint(lines, all);
        }

        /// <summary>
        /// Exact findings in lines that carry their context (from <see cref="TextLine.ToLint"/>): each rule fires only where its
        /// Content scope allows (<see cref="LintScope"/>), and only rules about UI wording fire inside UI labels.
        /// Several rules on the same words make one finding: the owner (later layer, citable, fixable, severity, canonical
        /// owner, then guide order) with every other rule on those words in <see cref="LintHit.AlsoRules"/>.
        /// </summary>
        /// <param name="all">Every rule's hits, overlapping ones included (validation tests each rule on its own).</param>
        public List<LintHit> Lint(IList<LintLine> lines, bool all = false)
        {
            var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Layers.Count; i++) rank[Layers[i].Name] = i;
            var hits = new List<LintHit>();
            var order = new Dictionary<LintHit, int>();
            var active = Rules.Select((r, n) => new { r, n }).Where(x => ForOurProducts(x.r.Products))
                              .Select(x => new { x.r, o = Override(x.r), scope = LintScope.For(x.r), x.n }).Where(x => x.o != "off").ToList();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? LintLine.Plain("");
                var text = (line.Text ?? "").Replace('\r', ' ').Replace('\n', ' ');
                foreach (var x in active)
                {
                    var r = x.r;
                    foreach (var swap in r.Swaps)
                        foreach (Match m in swap.Key.Matches(text))
                        {
                            if (m.Length == 0) continue;
                            if (!x.scope.Allows(line, m.Index, m.Index + m.Length)) continue;
                            var hit = Hit(r, swap.Value, text, m.Index, m.Index + m.Length, x.o);
                            if (hit == null) continue;
                            hit.Index = i;
                            hits.Add(hit);
                            order[hit] = x.n;
                        }
                }
            }
            if (all) return hits;
            // Same span flagged by several rules: one finding. The owner is the latest layer (it overrides), then a citable
            // rule, a fixable one, the most severe, the layer's canonical owner (rule-owners.txt), then guide order: the
            // same input always names the same rule.
            var kept = new List<LintHit>();
            var byKey = new Dictionary<string, LintHit>();
            foreach (var h in hits.OrderBy(h => h.Index).ThenBy(h => h.Start).ThenBy(h => h.Length)
                         .ThenBy(h => -(rank.ContainsKey(h.Layer) ? rank[h.Layer] : 0)).ThenBy(h => h.RuleId == null).ThenBy(h => h.Replacement == null)
                         .ThenBy(h => Rank.ContainsKey(h.Severity) ? Rank[h.Severity] : 3)
                         .ThenBy(h => OwnerRank(h.RuleId)).ThenBy(h => order[h]))
            {
                LintHit first;
                if (byKey.TryGetValue(h.Index + ":" + h.Start + ":" + h.Length, out first)) first.AlsoFlaggedBy(h);   // turning off the shown rule would otherwise expose this one
                else { byKey[h.Index + ":" + h.Start + ":" + h.Length] = h; kept.Add(h); }
            }
            // A hit inside a longer one ("Please" inside "Please note that") is the same problem: show the longer only, and
            // list the inner hit's rules (with the rules it already stood for) on it.
            var shown = new List<LintHit>();
            foreach (var h in kept)
            {
                var outer = kept.Where(o => o != h && o.Index == h.Index && o.Length > h.Length && o.Start <= h.Start && h.Start + h.Length <= o.Start + o.Length)
                                .OrderByDescending(o => o.Length).ThenBy(o => o.Start).FirstOrDefault();
                if (outer == null) shown.Add(h);
                else outer.AlsoFlaggedBy(h);
            }
            return shown;
        }

        private Dictionary<string, int> _owners;

        /// <summary>Position of the rule in its layer's canonical owner list (rule-owners.txt); unlisted rules come after listed ones.</summary>
        private int OwnerRank(string ruleId)
        {
            if (_owners == null)
            {
                var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in Layers) foreach (var id in l.Owners) if (!d.ContainsKey(id)) d[id] = d.Count;
                _owners = d;
            }
            int n;
            return ruleId != null && _owners.TryGetValue(ruleId, out n) ? n : int.MaxValue;
        }

        private static LintHit Hit(LintRule r, string use, string text, int start, int end, string severity)
        {
            var match = text.Substring(start, end - start);
            string replacement = null, message;
            if (r.Variable == null && r.LocalName == "Variables")
            {
                message = "'" + match + "' is the value of variable " + use + ": insert the variable instead of typing it.";
                return new LintHit { Start = start, Length = end - start, Match = match, Rule = r.Name, RuleId = r.Id, Layer = r.Layer, Variable = use, Severity = severity ?? r.Severity, Message = message, Link = r.Link };
            }
            if (r.Action == "replace")
            {
                if (string.Equals(match, use, r.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;   // already right
                replacement = MatchCase(text, start, use);
                // "for example," before a comma or at the end: don't double the punctuation.
                if (replacement.EndsWith(",") && (end >= text.Length || ",;:.".IndexOf(text[end]) >= 0)) replacement = replacement.TrimEnd(',');
                message = "Use '" + replacement + "' instead of '" + match + "'.";
            }
            else if (r.Action == "remove")
            {
                if (end < text.Length && text[end] == ' ') end++;   // take the following space with the words
                if (SentenceStart.IsMatch(text.Substring(0, start)) && end < text.Length)
                {
                    replacement = char.ToUpperInvariant(text[end]).ToString();   // re-capitalize what now starts the sentence
                    end++;
                }
                else replacement = "";
                message = "Delete '" + match.Trim() + "'.";
                match = text.Substring(start, end - start);
            }
            else message = "Avoid '" + match + "'.";
            if (!string.IsNullOrEmpty(r.Why)) message += " " + r.Why;
            return new LintHit { Start = start, Length = end - start, Match = match, Replacement = replacement, Rule = r.Name, RuleId = r.Id, Layer = r.Layer, Severity = severity ?? r.Severity, Message = message, Link = r.Link };
        }

        /// <summary>Capitalize the replacement only where a sentence starts; elsewhere the rule's spelling is the point.</summary>
        private static string MatchCase(string text, int start, string repl)
        {
            if (repl.Length > 0 && char.IsLower(repl[0]) && SentenceStart.IsMatch(text.Substring(0, start))) return char.ToUpperInvariant(repl[0]) + repl.Substring(1);
            return repl;
        }

        // ---- validation --------------------------------------------------------------------------------------------

        /// <summary>
        /// Tests the guide: every row's Example must trigger it and its fix must resolve it, the Use wording must not trip a
        /// rule, rows must not replace each other, topics must parse. Also counts prose rules and their examples.
        /// </summary>
        public GuideReport Validate()
        {
            var report = new GuideReport { Layers = Layers.Select(l => l.Name).ToList() };
            report.Issues.AddRange(Layers.SelectMany(l => l.Problems));
            var rows = Rules.Where(r => r.Id != null && Override(r) != "off").ToList();   // a rule the team turned off is not tested: it never fires
            Action<string, LintRule, string> issue = (level, r, msg) => report.Issues.Add(new GuideIssue { Level = level, Layer = r.Layer, File = r.File, Location = "table row " + r.Row + " (" + r.Id + ")", Message = msg });

            foreach (var r in rows.Where(r => r.Action == "replace" && r.Avoid.Any(a => string.Equals(a, r.Use, r.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))))
                issue("error", r, "Avoid and Use are the same ('" + r.Use + "'): the rule can never fire.");
            var withEx = rows.Where(r => !string.IsNullOrEmpty(r.Example)).ToList();
            foreach (var r in rows.Where(r => string.IsNullOrEmpty(r.Example))) issue("warning", r, "No Example: add one so the rule can be tested.");
            if (withEx.Count > 0)
            {
                var heads = Enumerable.Range(0, withEx.Count).Where(i => withEx[i].Applies.Contains("Headings")).ToList();
                var hits = Lint(withEx.Select(r => r.Example).ToList(), heads, true);
                var fixedTexts = new List<string>(); var fixedRows = new List<LintRule>();
                for (var i = 0; i < withEx.Count; i++)
                {
                    var r = withEx[i];
                    var mine = hits.Where(h => h.Index == i && h.RuleId == r.Id).ToList();
                    // A row for products this guide doesn't cover never lints here, so its Example can't be checked against it.
                    if (mine.Count == 0) { if (ForOurProducts(r.Products)) issue("error", r, "The Example doesn't trigger this rule: '" + r.Example + "'."); continue; }
                    if (mine.All(h => h.Replacement != null))
                    {
                        var t = r.Example;
                        // One rule can match overlapping spans ("please note that", "please"): keep the longest, then apply right to left.
                        var chosen = new List<LintHit>();
                        foreach (var h in mine.OrderByDescending(h => h.Length))
                            if (!chosen.Any(c => h.Start < c.Start + c.Length && c.Start < h.Start + h.Length)) chosen.Add(h);
                        foreach (var h in chosen.OrderByDescending(h => h.Start))
                            t = t.Substring(0, h.Start) + h.Replacement + t.Substring(h.Start + h.Length);
                        fixedTexts.Add(t); fixedRows.Add(r);
                    }
                }
                if (fixedTexts.Count > 0)
                {
                    var heads2 = Enumerable.Range(0, fixedRows.Count).Where(i => fixedRows[i].Applies.Contains("Headings")).ToList();
                    var again = Lint(fixedTexts, heads2, true);
                    for (var i = 0; i < fixedRows.Count; i++)
                        if (again.Any(h => h.Index == i && h.RuleId == fixedRows[i].Id)) issue("error", fixedRows[i], "Applying the fix doesn't resolve the rule: '" + fixedTexts[i] + "'.");
                }
            }
            var uses = rows.Where(r => r.Action == "replace").ToList();
            if (uses.Count > 0)
            {
                var hits = Lint(uses.Select(r => r.Use).ToList(), null);
                for (var i = 0; i < uses.Count; i++)
                    foreach (var h in hits.Where(h => h.Index == i && h.Replacement != null))
                    {
                        var same = h.RuleId == uses[i].Id;
                        issue(same ? "error" : "warning", uses[i], "The 'Use' wording '" + uses[i].Use + "' is itself flagged by " + (same ? "this same rule" : h.RuleId ?? h.Rule) + ": " + h.Message);
                    }
            }
            // Contradictions: one row's Use is another row's Avoid, and the other way round.
            for (var i = 0; i < rows.Count; i++)
                for (var j = i + 1; j < rows.Count; j++)
                {
                    LintRule a = rows[i], b = rows[j];
                    if (b.Avoid.Any(x => string.Equals(x, a.Use, StringComparison.OrdinalIgnoreCase)) && a.Avoid.Any(x => string.Equals(x, b.Use, StringComparison.OrdinalIgnoreCase)))
                        issue("error", a, "Contradicts " + b.Id + " (" + b.File + " row " + b.Row + "): each replaces the other.");
                }
            // Where a later layer meets an earlier one: the same word with the same fix is redundant; with a different fix
            // the later layer wins (worth knowing, not an error).
            for (var i = 0; i < rows.Count; i++)
                for (var j = i + 1; j < rows.Count; j++)
                {
                    LintRule a = rows[i], b = rows[j];
                    if (a.Layer == b.Layer) continue;
                    var shared = b.Avoid.Where(x => a.Avoid.Any(y => string.Equals(x, y, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (shared.Count == 0) continue;
                    var same = string.Equals(a.Use ?? "", b.Use ?? "", StringComparison.OrdinalIgnoreCase);
                    issue("warning", b, same ? "Duplicates " + a.Id + " ('" + shared[0] + "'): the " + a.Layer + " rule already does this; remove one."
                                             : "Overrides " + a.Id + " for '" + shared[0] + "': " + b.Layer + " says '" + b.Use + "', " + a.Layer + " says '" + a.Use + "'. The " + b.Layer + " rule wins.");
                }
            // Override tables must name rules that exist.
            var known = new HashSet<string>(Rules.Select(r => r.Id).Where(x => x != null).Concat(Rules.Select(r => r.Name)).Concat(Items.Select(x => x.Id)), StringComparer.OrdinalIgnoreCase);
            foreach (var l in Layers)
                foreach (var o in l.Overrides)
                {
                    var key = o.Key.IndexOf('.') < 0 && o.Key.IndexOf(':') < 0 ? "Google." + o.Key : o.Key;
                    if (!known.Contains(key))
                        report.Issues.Add(new GuideIssue { Level = "error", Layer = l.Name, File = "", Location = "override " + o.Key, Message = "No rule '" + o.Key + "': use a rule's reference from the guide, for example google:numbers/3." });
                }
            // Rules without a permanent key: their ids would shift when rules are added or moved.
            foreach (var l in Layers)
            {
                var unkeyed = l.Items.Count(i => i.Element != null && !i.HasKey);
                if (unkeyed > 0)
                    report.Issues.Add(new GuideIssue { Level = "warning", Layer = l.Name, File = "", Location = "keys", Message = unkeyed + " rule(s) have no permanent key yet. Validating as a style guide owner assigns them (no other change to the topics)." });
            }
            report.TableRules = rows.Count;
            report.TestedByExample = withEx.Count;
            report.ProseRules = Items.Count - rows.Count;
            report.ProseRulesWithExamples = Items.Count(i => i.Bad.Length > 0 || i.Good.Length > 0);
            report.Errors = report.Issues.Count(x => x.Level == "error");
            report.Warnings = report.Issues.Count(x => x.Level == "warning");
            return report;
        }
    }
}
