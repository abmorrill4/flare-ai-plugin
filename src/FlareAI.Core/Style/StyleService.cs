using System;
using System.Collections.Generic;

namespace FlareAI.Core.Style
{
    /// <summary>One deterministic lint hit inside one text run.</summary>
    public sealed class LintHit
    {
        public int Index { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string Match { get; set; }
        /// <summary>Exact replacement for Match; null when the fix needs judgment.</summary>
        public string Replacement { get; set; }
        public string Rule { get; set; }
        public string Severity { get; set; }
        public string Message { get; set; }
        /// <summary>"google" (baseline) or "company" (layered on top).</summary>
        public string Layer { get; set; }
        /// <summary>Guide page explaining the rule, when known.</summary>
        public string Link { get; set; }
        /// <summary>Set when Match is a typed copy of this project variable's value (fix = insert the variable).</summary>
        public string Variable { get; set; }
        /// <summary>Citable guide id when the rule comes from a guide table (for example "company:terminology/3").</summary>
        public string RuleId { get; set; }
    }

    /// <summary>One citable rule of the layered guide.</summary>
    public sealed class GuideRule
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public string Section { get; set; }
        public string Text { get; set; }
        public string Link { get; set; }
        public string Bad { get; set; }
        public string Good { get; set; }
        /// <summary>The guide topic it comes from.</summary>
        public string Source { get; set; }
        /// <summary>Content condition tags: the rule applies only to sections with this content (empty = everywhere).</summary>
        public List<string> Applies = new List<string>();
        /// <summary>Product condition tags: the rule applies only to these products (empty = every product).</summary>
        public List<string> Products = new List<string>();
    }

    /// <summary>A prose rule with Bad/Good examples, used to check that the AI applies it.</summary>
    public sealed class GuideExample
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Bad { get; set; }
        public string Good { get; set; }
    }

    public sealed class GuideIssue
    {
        public string Level { get; set; }
        public string Layer { get; set; }
        public string File { get; set; }
        public string Location { get; set; }
        public string Message { get; set; }
    }

    public sealed class GuideReport
    {
        public List<GuideIssue> Issues = new List<GuideIssue>();
        public int TableRules, TestedByExample, ProseRules, ProseRulesWithExamples, Errors, Warnings;
        public List<string> Layers = new List<string>();
    }

    /// <summary>The style guide and linter. Implemented in process by <see cref="StyleEngine"/>; faked in tests.</summary>
    public interface IStyleService
    {
        /// <param name="headings">Indices of runs that are headings (heading rules apply only to them).</param>
        /// <param name="project">Path inside a Flare project to add its project layer (variables, glossary, StyleGuide), or null.</param>
        IList<LintHit> Lint(IList<string> texts, IList<int> headings, string project);
        /// <summary>The layered style guide (Google → configured layers → project) for the system prompt.</summary>
        string Guide(string project);
        /// <summary>Every citable rule of the layered guide (same call as <see cref="Guide"/>, cached with it).</summary>
        IList<GuideRule> Rules(string project);
        /// <summary>Tests the guide itself (table rows against their examples, contradictions, unreadable topics).</summary>
        GuideReport Validate(string project);
        /// <summary>Prose rules that carry Bad/Good examples.</summary>
        IList<GuideExample> Examples(string project);
    }
}
