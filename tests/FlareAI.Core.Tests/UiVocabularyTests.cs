using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>
    /// The UI consistency check (issues #6 and #9). The Flare project can't be referenced from tests, so this scans the
    /// source text: every string literal in src/FlareAI.Flare and src/FlareAI.Core (comments, Diagnostics.Log calls and
    /// identifiers excluded) must be free of the terms the glossary retired (docs/plans/ux/review-deliberate.md 5.1).
    /// A legitimate non-UI use (AI prompt text, internal keys, file names) goes in <see cref="Allowed"/> with a reason.
    /// </summary>
    public class UiVocabularyTests
    {
        private static readonly string[][] Banned =
        {
            new[] { "mechanical", @"\bmechanical" },
            new[] { "linter", @"\blinters?\b" },
            new[] { "lint", @"\blint(?:ed|ing|s)?\b" },
            new[] { "layer", @"\blayers?\b" },
            new[] { "rule id", @"\brule ids?\b" },
            new[] { "XHTML", @"\bXHTML\b" },
            new[] { "proposal", @"\bpropos(?:al|als|e all|ed)\b" },
            new[] { "Re-analyze", @"\bre-?(?:analy[sz]e|check)\b" },
            new[] { "AI >", @"\bAI\s*>" },
            new[] { "FlareAI (as a name)", @"\bFlareAI(?![\\/.\-_A-Za-z0-9])" },
        };

        /// <summary>file-name regex, literal regex, reason. A literal matching both is skipped.</summary>
        private static readonly string[][] Allowed =
        {
            new[] { @"PromptBuilder\.cs$", ".*", "text sent to the AI model, not shown to writers" },
            new[] { @"Contracts\.cs$", ".*", "JSON schema descriptions sent to the AI model" },
            new[] { @"UiVocabularyTests\.cs$", ".*", "this file" },
            new[] { @"\.cs$", @"^(mechanical|layer|lint|proposed|FlareAI|FlareAI\\layer\.txt)$", "stored tokens, JSON keys and the FlareAI folder name inside a guide project; never shown" },
            new[] { @"StyleEngine\.cs$", @"^Rules for what this text contains|^Layer: |overrides the layers above", "guide text sent to the AI model" },
            new[] { @"ActionRunner\.cs$", @"no exact text proposed", "sent to the AI model" },
        };

        [Fact]
        public void UiStringsUseOnlyGlossaryTerms()
        {
            var root = RepoRoot();
            var violations = new List<string>();
            foreach (var dir in new[] { "src/FlareAI.Flare", "src/FlareAI.Core" })
                foreach (var file in Directory.GetFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories))
                {
                    var rel = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
                    foreach (var lit in Literals(File.ReadAllText(file)))
                    {
                        if (Allowed.Any(a => Regex.IsMatch(rel, a[0]) && Regex.IsMatch(lit.Text, a[1], RegexOptions.Singleline))) continue;
                        foreach (var b in Banned)
                            if (Regex.IsMatch(lit.Text, b[1], RegexOptions.IgnoreCase))
                                violations.Add(rel + ":" + lit.Line + "  [" + b[0] + "]  " + Shorten(lit.Text));
                    }
                }
            Assert.True(violations.Count == 0, "Banned UI terms (use the glossary, docs/plans/ux/review-deliberate.md 5.1):\n" + string.Join("\n", violations.ToArray()));
        }

        // ---- the command table (issue #9) ----------------------------------------------------------------------

        [Fact]
        public void EveryCommandHasOneLabelTooltipKeyTipAndIcon()
        {
            var all = FlareAI.Flare.Commands.All;
            Assert.Equal(all.Count, all.Select(c => c.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(all.Count, all.Select(c => c.KeyTip).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var c in all)
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Label), c.Id + " has no label");
                Assert.False(string.IsNullOrWhiteSpace(c.Tooltip), c.Id + " has no tooltip");
                Assert.False(string.IsNullOrWhiteSpace(c.KeyTip), c.Id + " has no key tip");
                Assert.False(string.IsNullOrWhiteSpace(c.Icon), c.Id + " has no icon");
                Assert.False(string.IsNullOrWhiteSpace(c.Group), c.Id + " has no group");
            }
            // Every menu, toolbar and right-click entry is a command from the table.
            foreach (var c in FlareAI.Flare.Commands.Menu.Concat(FlareAI.Flare.Commands.Toolbar).Concat(FlareAI.Flare.Commands.Context))
                Assert.True(c == null || all.Contains(c));
            // Every command is reachable from the Tool Strip menu too.
            foreach (var c in all) Assert.Contains(c, FlareAI.Flare.Commands.Menu);
        }

        [Fact]
        public void CommandLabelsAreNotRetypedOutsideTheTable()
        {
            var root = RepoRoot();
            var labels = FlareAI.Flare.Commands.All.SelectMany(c => new[] { c.Label, c.ContextLabel }).ToList();
            var bad = new List<string>();
            foreach (var file in new[] { "src/FlareAI.Flare/Plugin.cs", "src/FlareAI.Flare/EditorIntegration.cs" })
                foreach (var lit in Literals(File.ReadAllText(Path.Combine(root, file))))
                    if (labels.Contains(lit.Text)) bad.Add(file + ":" + lit.Line + "  \"" + lit.Text + "\" (use Commands.*)");
            Assert.True(bad.Count == 0, string.Join("\n", bad.ToArray()));
        }

        [Fact]
        public void EveryCommandIsInTheUserGuide()
        {
            var guide = string.Join("\n", Directory.GetFiles(Path.Combine(RepoRoot(), "docs/user-guide/Content"), "*.htm").Select(File.ReadAllText).ToArray());
            var missing = FlareAI.Flare.Commands.All.Where(c => guide.IndexOf(c.Label.TrimEnd('…'), StringComparison.OrdinalIgnoreCase) < 0).Select(c => c.Label).ToList();
            Assert.True(missing.Count == 0, "Not in docs/user-guide/Content: " + string.Join(", ", missing.ToArray()));
        }

        [Fact]
        public void ScannerFindsLiteralsAndSkipsCommentsAndLogs()
        {
            var src = "// \"mechanical\"\nvar a = \"x lint y\";\nDiagnostics.Log(\"lint ok\", ex);\n/* \"layer\" */ var b = @\"c:\\q\"\"r\";\nvar c = '\"';\nvar d = $\"n {1} layer\";\n";
            var lits = Literals(src).ToList();
            Assert.Equal(new[] { "x lint y", "c:\\q\"r", "n {1} layer" }, lits.Select(l => l.Text).ToArray());
            Assert.Equal(2, lits[0].Line);
        }

        private static string Shorten(string s) { s = s.Replace("\r", " ").Replace("\n", " "); return s.Length > 90 ? s.Substring(0, 90) + "..." : s; }

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            Assert.NotNull(d);
            return d.FullName;
        }

        private sealed class Literal { public string Text; public int Line; }

        /// <summary>String literals of a C# source file, outside comments and outside Diagnostics.Log statements.</summary>
        private static IEnumerable<Literal> Literals(string s)
        {
            var i = 0; var line = 1; var stmtStart = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '\n') { line++; i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/')) { if (s[i] == '\n') line++; i++; }
                    i += 2; continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < s.Length && s[i] != '\'') { if (s[i] == '\\') i++; i++; }
                    i++; continue;
                }
                if (c == ';' || c == '{' || c == '}') { stmtStart = i + 1; i++; continue; }
                var verbatim = (c == '@' && i + 1 < s.Length && s[i + 1] == '"') || (c == '$' && i + 2 < s.Length && s[i + 1] == '@' && s[i + 2] == '"') || (c == '@' && i + 2 < s.Length && s[i + 1] == '$' && s[i + 2] == '"');
                var regular = c == '"' || (c == '$' && i + 1 < s.Length && s[i + 1] == '"');
                if (!verbatim && !regular) { i++; continue; }
                while (s[i] != '"') i++;
                i++;
                var startLine = line; var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var ch = s[i];
                    if (verbatim && ch == '"' && i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    if (ch == '"') break;
                    if (!verbatim && ch == '\\' && i + 1 < s.Length) { sb.Append(s[i + 1] == 'n' ? '\n' : s[i + 1]); i += 2; continue; }
                    if (ch == '\n') line++;
                    sb.Append(ch); i++;
                }
                i++;
                var before = s.Substring(stmtStart, Math.Max(0, Math.Min(i, s.Length) - stmtStart));
                if (before.Contains("Diagnostics.Log(")) continue;
                yield return new Literal { Text = sb.ToString(), Line = startLine };
            }
        }
    }
}
