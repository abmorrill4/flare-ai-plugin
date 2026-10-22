using System;
using System.Collections.Generic;
using System.Linq;

namespace FlareAI.Core.Style
{
    /// <summary>Where a piece of text sits in a topic: line kinds (step, cell, note, …) and inline kinds (UI label, link text, …).</summary>
    [Flags]
    public enum TextContext
    {
        None = 0,
        // ---- line ----
        Heading = 1 << 0,
        /// <summary>A numbered procedure step (the nearest list item is in an ol), or a paragraph inside one.</summary>
        Steps = 1 << 1,
        /// <summary>Any list item (ol, ul) or definition-list term or description.</summary>
        Lists = 1 << 2,
        TableCell = 1 << 3,
        TableHeader = 1 << 4,
        TableCaption = 1 << 5,
        /// <summary>A figure caption.</summary>
        Caption = 1 << 6,
        /// <summary>A note, tip, warning or caution (by class, or a "Note:" lead-in).</summary>
        Notes = 1 << 7,
        /// <summary>The clickable head of a MadCap drop-down, toggler or expanding text.</summary>
        Hotspot = 1 << 8,
        // ---- line or inline ----
        /// <summary>A blockquote line, or a q span.</summary>
        Quote = 1 << 9,
        /// <summary>A pre line, or the placeholder that stands for inline code.</summary>
        Code = 1 << 10,
        // ---- inline ----
        /// <summary>A UI label: b/strong or a UI-control span. It must match the product verbatim.</summary>
        UI = 1 << 11,
        /// <summary>Link text: a href or MadCap:xref.</summary>
        Link = 1 << 12,
        Emphasis = 1 << 13,

        Inline = Quote | Code | UI | Link | Emphasis,
        Tables = TableCell | TableHeader | TableCaption,
    }

    /// <summary>A run of a line's text that sits in inline markup.</summary>
    public sealed class ContextSpan
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public TextContext Context { get; set; }
    }

    /// <summary>One line of lint input with its context.</summary>
    public sealed class LintLine
    {
        public string Text { get; set; }
        /// <summary>Where the line sits (line kinds; inline kinds are in <see cref="Spans"/>).</summary>
        public TextContext Context { get; set; }
        /// <summary>
        /// False when the caller knows only whether the line is a heading (MCP, tests, the old Lint overload): then only
        /// Headings scoping applies, as before context-aware lint.
        /// </summary>
        public bool Known { get; set; }
        public List<ContextSpan> Spans = new List<ContextSpan>();

        /// <summary>A line whose context isn't known beyond whether it is a heading.</summary>
        public static LintLine Plain(string text, bool heading = false)
        {
            return new LintLine { Text = text, Context = heading ? TextContext.Heading : TextContext.None };
        }

        /// <summary>The inline context of the characters [start, end), or of any of them.</summary>
        public TextContext Overlapping(int start, int end)
        {
            var c = TextContext.None;
            foreach (var s in Spans) if (s.Start < end && start < s.Start + s.Length) c |= s.Context;
            return c;
        }
    }

    /// <summary>
    /// A lint rule's Content scope, enforced per hit. Restricting tags (Headings, Steps, Lists, Tables, Notes, Images) let the
    /// rule fire only where the line is that kind of text; a rule with several must be allowed by each. Tags that describe the
    /// text itself (Numbers, Quotes, Dashes, Abbreviations, …) and topic tags (Links, Code, API, Examples, Math) don't
    /// restrict: the pattern already needs what they describe. UI labels quote the product, so only rules tagged UI (about UI
    /// wording) and variable rules fire inside them. Mirrors tools/FlareAI.FixtureGen/expectations.yaml.
    /// </summary>
    public sealed class LintScope
    {
        private readonly List<Func<TextContext, TextContext, bool>> _must = new List<Func<TextContext, TextContext, bool>>();
        private readonly bool _headings;
        private readonly bool _inLabels;

        /// <summary>The Content tags that restrict where a rule fires.</summary>
        public static readonly string[] Restricting = { "Headings", "Steps", "Lists", "Tables", "Notes", "Images" };

        public LintScope(ICollection<string> applies, bool inLabels)
        {
            _inLabels = inLabels;
            foreach (var t in applies ?? new string[0])
            {
                switch (t.ToLowerInvariant())
                {
                    case "headings": _headings = true; _must.Add((line, span) => (line & TextContext.Heading) != 0); break;
                    case "steps": _must.Add((line, span) => (line & TextContext.Steps) != 0); break;
                    case "lists": _must.Add((line, span) => (line & (TextContext.Lists | TextContext.Steps)) != 0); break;
                    case "tables": _must.Add((line, span) => (line & TextContext.Tables) != 0); break;
                    case "notes": _must.Add((line, span) => (line & TextContext.Notes) != 0); break;
                    // Rules about referring to images ("the image below", "Figure 2") are about running text: not headings,
                    // table headers, drop-down heads or link text.
                    case "images":
                        _must.Add((line, span) => (line & (TextContext.Heading | TextContext.TableHeader | TextContext.Hotspot)) == 0 && (span & TextContext.Link) == 0);
                        break;
                }
            }
        }

        public static LintScope For(LintRule r)
        {
            var aboutUi = r.Applies.Any(a => string.Equals(a, "UI", StringComparison.OrdinalIgnoreCase));
            return new LintScope(r.Applies, aboutUi || r.Variable != null || r.LocalName == "Variables");
        }

        /// <summary>True when a hit on [start, end) of this line is in the rule's scope.</summary>
        public bool Allows(LintLine line, int start, int end)
        {
            var lc = line.Context;
            if (!line.Known) return !_headings || (lc & TextContext.Heading) != 0;
            var span = line.Overlapping(start, end);
            if (!_inLabels && (span & TextContext.UI) != 0) return false;
            foreach (var m in _must) if (!m(lc, span)) return false;
            return true;
        }
    }
}
