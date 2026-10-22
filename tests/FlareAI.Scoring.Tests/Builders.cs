using System.Collections.Generic;
using System.IO;
using FlareAI.Scoring;

namespace FlareAI.Scoring.Tests
{
    internal static class B
    {
        public const string File = "Content/a.htm";
        public const string Block = "/html/body/p[1]";

        private static int _n;

        public static Label Violation(string quote, string rule = "google:tone/22", string prefix = null, string suffix = null, string block = Block, string file = File, string status = "verified", string kind = "exclaim")
        {
            return Make(LabelKind.Violation, quote, new[] { rule }, prefix, suffix, block, file, status, kind);
        }

        public static Label Clean(string quote, string rule = "*", string label = LabelKind.Clean, string block = Block, string file = File, string status = "verified")
        {
            var l = Make(label, quote, new[] { rule }, null, null, block, file, status, null);
            l.Reason = "test";
            return l;
        }

        public static Label Complete(string block = Block, string rule = "lint", string file = File)
        {
            return Make(LabelKind.BlockComplete, null, new[] { rule }, null, null, block, file, "verified", null);
        }

        public static Label Ambiguous(string quote, string block = Block, string file = File)
        {
            return Make(LabelKind.Ambiguous, quote, new[] { "*" }, null, null, block, file, "verified", null);
        }

        public static Label Make(string kind, string quote, string[] rules, string prefix, string suffix, string block, string file, string status, string defectKind)
        {
            return new Label
            {
                Id = "P/L" + (++_n).ToString("D4"), File = file, Block = block, Quote = quote, Prefix = prefix, Suffix = suffix,
                Kind = kind, DefectKind = defectKind, Rules = new List<string>(rules), Origin = "seeded", Status = status,
                By = new List<string> { "generator" }, Split = "dev"
            };
        }

        /// <summary>Finding with positions in a block.</summary>
        public static ScoreFinding At(int start, int end, string rule = "google:tone/22", string excerpt = null, string block = Block, string file = File, string origin = "lint")
        {
            return new ScoreFinding { Id = "F" + (++_n), File = file, Block = block, Start = start, End = end, Excerpt = excerpt, RuleId = rule, Origin = origin };
        }

        /// <summary>Finding that only knows its file and flagged text (v1 bench shape).</summary>
        public static ScoreFinding Text(string excerpt, string rule = "google:tone/22", string file = File, string origin = "lint")
        {
            return new ScoreFinding { Id = "F" + (++_n), File = file, Excerpt = excerpt, RuleId = rule, Origin = origin };
        }

        public static ScoreOptions WithText(string text, string block = Block, string file = File)
        {
            return new ScoreOptions { TextSource = new DictionaryBlockTextSource().Add(file, block, text) };
        }

        public static ScoreReport Run(Label[] labels, ScoreFinding[] findings, ScoreOptions o = null)
        {
            return Scorer.Score(labels, findings, o);
        }

        public static string RepoRoot()
        {
            string dir = System.AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(Path.Combine(dir, "FlareAI.sln"))) dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new DirectoryNotFoundException("FlareAI.sln not found above " + System.AppDomain.CurrentDomain.BaseDirectory);
            return dir;
        }
    }
}
