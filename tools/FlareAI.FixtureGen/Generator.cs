using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FlareAI.Core.Style;
using Newtonsoft.Json;

namespace FlareAI.FixtureGen
{
    /// <summary>The bundled guide as the fixtures see it: Google then Company, with no overrides (rule behaviour, not policy).</summary>
    public sealed class GuideSet
    {
        public GuideLayer Google, Company;

        /// <summary>
        /// Loads the layers the way StyleEngine does for a team guide that is also the bundled one. Overrides are cleared:
        /// company defaults that turn Google rows off (#52) are policy, and the fixtures test what every row does when on.
        /// For the same reason the guide covers every product a row is tagged for (#57): a Product.Android row is on for an
        /// Android team, and off (never linting) for a guide without that product, which the compiler tests check.
        /// </summary>
        public static GuideSet Load(string guideRoot)
        {
            var g = new GuideSet
            {
                Google = GuideLayer.Load(guideRoot, "google", "Google"),
                Company = GuideLayer.Load(guideRoot, null, null, "Google")
            };
            g.Google.Overrides.Clear();
            g.Company.Overrides.Clear();
            foreach (var p in g.Rows.SelectMany(r => r.Products).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList())
                if (!g.Company.ProductTags.Contains(p)) g.Company.ProductTags.Add(p);
            return g;
        }

        public Composition Compose(params GuideLayer[] extra)
        {
            return new Composition(new[] { Google, Company }.Concat(extra).ToList());
        }

        public IEnumerable<LintRule> Rows { get { return Google.Rules.Concat(Company.Rules).Where(r => r.Id != null); } }

        /// <summary>Hash of the guide's topic files (line endings normalised), so a changed guide is visible in the manifest.</summary>
        public static string Stamp(string guideRoot)
        {
            var files = Directory.GetFiles(Path.Combine(guideRoot, @"Content\Resources\StyleGuide"), "*.*", SearchOption.AllDirectories)
                .OrderBy(f => f.Substring(guideRoot.Length).Replace('\\', '/'), StringComparer.Ordinal).ToList();
            using (var sha = SHA1.Create())
            {
                var sb = new StringBuilder();
                foreach (var f in files)
                {
                    var text = File.ReadAllText(f).Replace("\r\n", "\n");
                    sb.Append(f.Substring(guideRoot.Length).Replace('\\', '/')).Append('\n').Append(text).Append('\u0001');
                }
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
            }
        }
    }

    /// <summary>Turns the guide's lint rows into rule fixtures: the row's own Example, Avoid terms and Use wording, placed in contexts.</summary>
    public static class Generator
    {
        private const int MaxAvoidTerms = 3;

        /// <returns>Relative path (forward slashes) to file text, for tests/fixtures/rules.</returns>
        public static SortedDictionary<string, string> Generate(string guideRoot, string expectationsPath, string structurePath, string templateDir)
        {
            var guide = GuideSet.Load(guideRoot);
            var expect = Expectations.Load(expectationsPath);
            var contexts = ContextSet.Load(templateDir);
            var missing = expect.Contexts.Where(c => !contexts.Has(c)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException("No template for context(s): " + string.Join(", ", missing.ToArray()));

            var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var fixtures = guide.Rows.Select(r => Build(r, expect)).ToList();
            foreach (var group in fixtures.GroupBy(f => f.Layer + "/" + f.Page).OrderBy(g => g.Key, StringComparer.Ordinal))
                files[group.Key + ".json"] = Json(group.OrderBy(f => f.Row).ToList());

            files["structure/structure.json"] = File.ReadAllText(structurePath).Replace("\r\n", "\n");
            var manifest = new Manifest
            {
                GuideStamp = GuideSet.Stamp(guideRoot),
                Rules = fixtures.Count,
                MarkupOnlyRules = fixtures.Count(f => f.MarkupOnly),
                BadCases = fixtures.Sum(f => f.Bad.Count),
                GoodCases = fixtures.Sum(f => f.Good.Count),
                Contexts = expect.Contexts,
                Files = files.Keys.ToList()
            };
            files["manifest.json"] = Json(manifest);
            return files;
        }

        public static void Write(string outDir, SortedDictionary<string, string> files)
        {
            if (Directory.Exists(outDir))
                foreach (var old in Directory.GetFiles(outDir, "*.json", SearchOption.AllDirectories)) File.Delete(old);
            foreach (var kv in files)
            {
                var path = Path.Combine(outDir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, kv.Value, new UTF8Encoding(false));
            }
        }

        private static string Json(object o) { return JsonConvert.SerializeObject(o, Formatting.Indented).Replace("\r\n", "\n") + "\n"; }

        private static RuleFixture Build(LintRule r, Expectations expect)
        {
            var tags = r.Applies.OrderBy(t => t, StringComparer.Ordinal).ToList();
            var patterns = r.Swaps.Select(s => s.Key.ToString()).ToList();
            var f = new RuleFixture
            {
                Id = r.Id,
                Layer = r.Layer,
                Page = System.IO.Path.GetFileNameWithoutExtension(r.File),
                Row = r.Row,
                Severity = r.Severity,
                Action = r.Action,
                Tags = tags,
                Avoid = r.Avoid,
                Use = r.Use ?? "",
                Example = r.Example ?? "",
                IgnoreCase = r.IgnoreCase,
                MarkupOnly = patterns.Any(RuleShape.MatchesMarkup),
                Bad = new List<BadCase>(),
                Good = new List<GoodCase>()
            };
            if (f.MarkupOnly) return f;   // lint never sees markup: one check (the pattern), no context cases

            Func<string, bool, BadCase> bad = (text, full) => new BadCase
            {
                Text = text,
                Expect = (full ? expect.Contexts : new List<string> { "paragraph" }).GroupBy(c => expect.For(r.Id, tags, c))
                    .OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => string.Join(", ", g.ToArray()))
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Action<string, string, bool> add = (source, text, full) =>
            {
                if (string.IsNullOrWhiteSpace(text) || !seen.Add(text)) return;
                var b = bad(text, full);
                b.Source = source;
                f.Bad.Add(b);
            };
            add("example", r.Example, true);
            var terms = r.Avoid.Where(RuleShape.IsLiteral).Take(MaxAvoidTerms).ToList();
            for (var i = 0; i < terms.Count; i++)
            {
                add(i == 0 ? "avoid" : "avoid-variant", terms[i], i == 0);
                if (r.IgnoreCase)
                {
                    add("avoid-variant", terms[i].ToLowerInvariant(), false);
                    add("avoid-variant", char.ToUpperInvariant(terms[i][0]) + terms[i].Substring(1).ToLowerInvariant(), false);
                    add("avoid-variant", terms[i].ToUpperInvariant(), false);
                }
            }

            // Good: what the guide tells the writer to write instead, first letter up (it starts a heading, a cell, a label).
            if (r.Action == "replace" && !string.IsNullOrWhiteSpace(r.Use) && !GuideLayer.IsFlagOnly(r.Use) && !GuideLayer.IsDelete(r.Use))
            {
                var use = r.Use.Trim();
                if (char.IsLower(use[0])) use = char.ToUpperInvariant(use[0]) + use.Substring(1);
                f.Good.Add(new GoodCase
                {
                    Source = "use",
                    Text = use,
                    Contexts = string.Join(", ", expect.Contexts.Where(c => expect.ClassOf(c) != "literal").ToArray())
                });
            }
            return f;
        }
    }
}
