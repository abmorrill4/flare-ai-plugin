using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FlareAI.Core.Editing;
using FlareAI.TestKit.Editor;

namespace FlareAI.TestKit.Conformance
{
    /// <summary>Who a conformance case applies to.</summary>
    public enum ConformanceKind
    {
        /// <summary>Flare 22 behaviour: holds for FlareSimDocument and for FlareDocumentAdapter inside Flare.</summary>
        Flare,
        /// <summary>A path the plugin must never take; FlareSim refuses it. Never run inside Flare (it crashes or corrupts).</summary>
        SimGuard,
        /// <summary>Needs a FlareSim control the adapter does not expose (raw Select(string), Save checks).</summary>
        SimOnly
    }

    public sealed class ConformanceCase
    {
        public string Name { get; set; }
        public ConformanceKind Kind { get; set; }
        /// <summary>The documented behaviour, in one line (docs/flare-api-notes.md).</summary>
        public string Behaviour { get; set; }
        public Action<OpenScratch> Run { get; set; }
        public override string ToString() { return Name; }
    }

    public sealed class ConformanceResult
    {
        public ConformanceCase Case { get; set; }
        public bool Passed { get; set; }
        public bool Skipped { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// The adapter conformance suite: one case per documented Flare 22 write behaviour ("Verified write behavior" and
    /// "Variables and redo" in docs/flare-api-notes.md). xUnit runs it against FlareSimDocument; the in-Flare self-test
    /// runs the <see cref="ConformanceKind.Flare"/> cases against FlareDocumentAdapter on a scratch topic, so the fake is
    /// checked against the real editor on every release.
    /// </summary>
    public static class AdapterConformance
    {
        /// <summary>The scratch topic every case starts from.</summary>
        public const string Topic =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\">\n" +
            "    <head>\n        <title>Conformance</title>\n    </head>\n" +
            "    <body>\n" +
            "        <h1>Conformance topic</h1>\n" +
            "        <p>Check your settings before you start.</p>\n" +
            "        <p>Open <MadCap:variable name=\"General.ProductName\" /> and sign in.</p>\n" +
            "        <p class=\"note\">Utilize the menu to utilize the tools.</p>\n" +
            "    </body>\n" +
            "</html>";

        public static readonly IList<ConformanceCase> Cases = new List<ConformanceCase>
        {
            new ConformanceCase
            {
                Name = "Edit_IsOneUndoStep", Kind = ConformanceKind.Flare,
                Behaviour = "One StartOperation/EndOperation around many edits = one Ctrl+Z.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    doc.Edit("two fixes", () =>
                    {
                        Check.That(Replace(doc, 0, "Check", "Verify"), "first span applied");
                        Check.That(Replace(doc, 2, "Utilize", "Use"), "second span applied");
                    });
                    Check.Equal(1, doc.UndoCount, "undo steps after one Edit");
                    doc.Undo();
                    Check.Equal(original, doc.Document.OuterXml, "topic after one Undo");
                }
            },
            new ConformanceCase
            {
                Name = "ReplaceSpan_OutsideEdit_Throws", Kind = ConformanceKind.SimGuard,
                Behaviour = "Word-level changes belong to an operation; a span edit outside Edit is a plugin bug.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var run = Run(doc, 0);
                    Check.Throws<InvalidOperationException>(() => doc.ReplaceSpan(run, 0, 5, "Verify"), "ReplaceSpan outside Edit");
                }
            },
            new ConformanceCase
            {
                Name = "ReplaceSpan_WritesTrackedChangeMarkup", Kind = ConformanceKind.Flare,
                Behaviour = "ReplaceText with TrackChange gives native word-level MadCap:change insert/delete markup.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("fix", () => Replace(doc, 0, "Check", "Verify"));
                    var p = Block(doc, 0);
                    var changes = Changes(p);
                    var removeIds = Ids(doc, "RemoveChange");
                    var addIds = Ids(doc, "AddChange");
                    Check.That(changes.Any(c => c.InnerText == "Check" && removeIds.Contains(ChangeId(c))), "a deletion holds the old word and carries a RemoveChange id");
                    Check.That(changes.Any(c => c.InnerText == "Verify" && addIds.Contains(ChangeId(c))), "an insertion holds the new word and carries an AddChange id");
                    Check.Equal("Verify your settings before you start.", AcceptedView.Of(new List<XmlNode> { p })[0].InnerText, "accepted view");
                }
            },
            new ConformanceCase
            {
                Name = "Operation_HasOneChangeIdPair", Kind = ConformanceKind.Flare,
                Behaviour = "Flare assigns one Add/Remove change id pair per operation.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("first", () =>
                    {
                        Replace(doc, 0, "settings", "options");
                        Replace(doc, 2, "Utilize", "Use");
                    });
                    var first = AllChangeIds(doc);
                    Check.Equal(2, first.Count, "distinct change ids after one operation with two edits");
                    doc.Edit("second", () => Replace(doc, 1, "sign in", "log in"));
                    var second = AllChangeIds(doc);
                    Check.Equal(4, second.Count, "distinct change ids after a second operation");
                    Check.Equal(2, Ids(doc, "AddChange").Count, "AddChange entries (one per operation)");
                    Check.Equal(2, Ids(doc, "RemoveChange").Count, "RemoveChange entries (one per operation)");
                }
            },
            new ConformanceCase
            {
                Name = "ChangeIds_ReachHeadAtOperationEnd", Kind = ConformanceKind.Flare,
                Behaviour = "Inside an operation MadCap:change elements exist at once; their changeData ids reach the head only at EndOperation.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("fix", () =>
                    {
                        Replace(doc, 0, "Check", "Verify");
                        var ids = AllChangeIds(doc);
                        Check.That(ids.Count > 0, "change markup is written immediately");
                        Check.Equal(0, Ids(doc, "AddChange").Count + Ids(doc, "RemoveChange").Count, "head changeData entries during the operation");
                    });
                    foreach (var id in AllChangeIds(doc))
                        Check.That(Ids(doc, "AddChange").Contains(id) || Ids(doc, "RemoveChange").Contains(id), "id " + id + " is in the head after the operation");
                }
            },
            new ConformanceCase
            {
                Name = "SpanEdit_SplitsTheTextNode", Kind = ConformanceKind.Flare,
                Behaviour = "Every edit splits text nodes: a reference taken before the edit no longer holds the old text (re-locate before each edit).",
                Run = open =>
                {
                    var doc = open(Topic);
                    var run = Run(doc, 0);
                    var text = run.Value;
                    var at = text.IndexOf("before", StringComparison.Ordinal);
                    doc.Edit("fix", () => doc.ReplaceSpan(run, at, 6, "until"));
                    Check.That(run.ParentNode == null || run.Value != text, "the old text node no longer holds the whole sentence");
                    Check.That(Block(doc, 0).ChildNodes.Count > 1, "the paragraph now has several nodes");
                    var stale = false;
                    doc.Edit("stale", () => stale = doc.ReplaceSpan(run, text.IndexOf("start", StringComparison.Ordinal), 5, "begin"));
                    Check.That(!stale, "an offset into the old text is refused on the split node");
                }
            },
            new ConformanceCase
            {
                Name = "SelectText_SelectsFirstMatch", Kind = ConformanceKind.SimOnly,
                Behaviour = "Select(string) selects the first match: a span that isn't unique in its text node hits the wrong word (\"you\" in \"your\").",
                Run = open =>
                {
                    var doc = (FlareSimDocument)open(Topic);
                    doc.WidenSpans = false;   // raw Select(string), without the adapter's widening
                    var run = Run(doc, 0);
                    var second = run.Value.LastIndexOf("you", StringComparison.Ordinal);
                    Check.That(doc.SelectText(run, second, 3), "selected");
                    Check.Equal(run.Value.IndexOf("you", StringComparison.Ordinal), doc.SelectionStart, "selection start (first match, inside \"your\")");
                    doc.Edit("fix", () => doc.ReplaceSpan(run, second, 3, "we"));
                    Check.Equal("Check wer settings before you start.", Accepted(doc, 0), "the edit landed on the first match");
                }
            },
            new ConformanceCase
            {
                Name = "SelectText_WidenedSpan_HitsTheIntendedWord", Kind = ConformanceKind.Flare,
                Behaviour = "Widening the span until it is unique in its text node (TextRuns.UniqueSpan) and re-typing the extra lands the edit on the right word.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var run = Run(doc, 0);
                    var second = run.Value.LastIndexOf("you", StringComparison.Ordinal);
                    Check.That(doc.SelectText(run, second, 3), "selected");
                    Check.That(doc.SelectionText.Contains("you") && run.Value.IndexOf(doc.SelectionText, StringComparison.Ordinal) == run.Value.LastIndexOf(doc.SelectionText, StringComparison.Ordinal), "selection is unique in the run");
                    doc.Edit("fix", () => doc.ReplaceSpan(run, second, 3, "we"));
                    Check.Equal("Check your settings before we start.", Accepted(doc, 0), "accepted text");
                }
            },
            new ConformanceCase
            {
                Name = "ReplaceBlocks_CopiesIntoTheOldElement", Kind = ConformanceKind.Flare,
                Behaviour = "ReplaceDocumentNode keeps the target (same object) and copies the new node's content and attributes into it; the new node is never attached.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var p = Block(doc, 2);
                    var replacement = NewParagraph(doc, "tip", "Use the menu.");
                    doc.ReplaceBlocks("rewrite", new List<XmlNode> { p }, new List<XmlNode> { replacement });
                    Check.That(Block(doc, 2) == p, "the old element is still in the topic");
                    Check.That(replacement.ParentNode == null, "the new node is not attached");
                    Check.Equal("tip", ((XmlElement)p).GetAttribute("class"), "attributes copied");
                    Check.Equal(1, doc.UndoCount, "undo steps");
                    Check.That(TrackedChanges.HasPending(new List<XmlNode> { p }), "the replacement is tracked");
                    Check.Equal("Use the menu.", Accepted(doc, 2), "accepted view");
                }
            },
            new ConformanceCase
            {
                Name = "ReplaceBlocks_TextNodeTarget_Throws", Kind = ConformanceKind.Flare,
                Behaviour = "ReplaceDocumentNode on a text node throws (\"Cannot set the 'InnerXml'…\"): element targets only.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    var run = Run(doc, 0);
                    var replacement = doc.Document.CreateTextNode("Verify your settings.");
                    Check.Throws<Exception>(() => doc.ReplaceBlocks("bad", new List<XmlNode> { run }, new List<XmlNode> { replacement }), "ReplaceBlocks on a text node");
                    Check.Equal(original, doc.Document.OuterXml, "topic unchanged");
                }
            },
            new ConformanceCase
            {
                Name = "ReplaceBlocks_NodeFromAnotherDocument_Throws", Kind = ConformanceKind.Flare,
                Behaviour = "New nodes must already be imported into the topic's DOM.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var other = new XmlDocument();
                    other.LoadXml("<p>Foreign.</p>");
                    Check.Throws<Exception>(() => doc.ReplaceBlocks("bad", new List<XmlNode> { Block(doc, 0) }, new List<XmlNode> { other.DocumentElement }), "ReplaceBlocks with a foreign node");
                }
            },
            new ConformanceCase
            {
                Name = "UndoRedo_RoundTrip", Kind = ConformanceKind.Flare,
                Behaviour = "Word-level changes made by ReplaceText undo and redo cleanly.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    doc.Edit("fix", () => Replace(doc, 0, "settings", "options"));
                    var edited = doc.Document.OuterXml;
                    doc.Undo();
                    Check.Equal(original, doc.Document.OuterXml, "after Undo");
                    doc.Redo();
                    Check.Equal(edited, doc.Document.OuterXml, "after Redo");
                }
            },
            new ConformanceCase
            {
                Name = "AcceptAll_LeavesNoChangeMarkup", Kind = ConformanceKind.Flare,
                Behaviour = "Review > Accept All keeps insertions, drops deletions, and removes change markup and changeData.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("fixes", () =>
                    {
                        Replace(doc, 0, "Check", "Verify");
                        Replace(doc, 2, "Utilize", "Use");
                    });
                    doc.AcceptAll();
                    Check.Equal(0, AllChangeIds(doc).Count, "change ids left");
                    Check.That(doc.Document.GetElementsByTagName("changeData", BlockSelector.MadCapNs).Count == 0, "no changeData left");
                    Check.Equal("Verify your settings before you start.", Block(doc, 0).InnerText, "first paragraph");
                    Check.Equal("Use the menu to utilize the tools.", Block(doc, 2).InnerText, "third paragraph");
                }
            },
            new ConformanceCase
            {
                Name = "RejectAll_RestoresTheOriginal", Kind = ConformanceKind.Flare,
                Behaviour = "Review > Reject All returns the topic to its text before the tracked changes.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    doc.Edit("fixes", () =>
                    {
                        Replace(doc, 0, "Check", "Verify");
                        Replace(doc, 2, "Utilize", "Use");
                    });
                    doc.RejectAll();
                    Check.Equal(original, doc.Document.OuterXml, "topic after Reject All");
                }
            },
            new ConformanceCase
            {
                Name = "Redo_NodeOperationWithVariable_Throws", Kind = ConformanceKind.SimGuard,
                Behaviour = "A tracked node operation whose content holds a MadCap:variable undoes fine but crashes Flare on Redo.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    var p = Block(doc, 1);
                    var replacement = (XmlElement)doc.Document.ImportNode(p, true);
                    replacement.LastChild.Value = " and log in.";
                    doc.ReplaceBlocks("rewrite", new List<XmlNode> { p }, new List<XmlNode> { replacement });
                    doc.Undo();
                    Check.Equal(original, doc.Document.OuterXml, "Undo works");
                    Check.Throws<InvalidOperationException>(() => doc.Redo(), "Redo of a node operation with a variable");
                }
            },
            new ConformanceCase
            {
                Name = "Redo_NodeOperationWithoutVariable_Works", Kind = ConformanceKind.Flare,
                Behaviour = "The same node replacement without a variable redoes fine.",
                Run = open =>
                {
                    var doc = open(Topic);
                    var original = doc.Document.OuterXml;
                    doc.ReplaceBlocks("rewrite", new List<XmlNode> { Block(doc, 2) }, new List<XmlNode> { NewParagraph(doc, "note", "Use the menu.") });
                    var edited = doc.Document.OuterXml;
                    doc.Undo();
                    Check.Equal(original, doc.Document.OuterXml, "after Undo");
                    doc.Redo();
                    Check.Equal(edited, doc.Document.OuterXml, "after Redo");
                }
            },
            new ConformanceCase
            {
                Name = "Redo_SpanEditBesideVariable_Works", Kind = ConformanceKind.Flare,
                Behaviour = "Word-level edits next to a variable take the redo-safe path.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("fix", () => Replace(doc, 1, "sign in", "log in"));
                    var edited = doc.Document.OuterXml;
                    doc.Undo();
                    doc.Redo();
                    Check.Equal(edited, doc.Document.OuterXml, "after Redo");
                }
            },
            new ConformanceCase
            {
                Name = "SpanEdits_RightMostFirst_AllLand", Kind = ConformanceKind.Flare,
                Behaviour = "Several edits in one text node, applied right-most first in one operation, all land where meant.",
                Run = open =>
                {
                    var doc = open(Topic);
                    doc.Edit("fixes", () =>
                    {
                        var run = Run(doc, 2);
                        Check.That(doc.ReplaceSpan(run, run.Value.IndexOf("utilize", StringComparison.Ordinal), 7, "use"), "right-most edit");
                        Check.That(doc.ReplaceSpan(run, 0, 7, "Use"), "left edit on the same (split) node");
                    });
                    Check.Equal("Use the menu to use the tools.", Accepted(doc, 2), "accepted text");
                    Check.Equal(1, doc.UndoCount, "undo steps");
                }
            },
            new ConformanceCase
            {
                Name = "Save_WithSelectionMarker_Throws", Kind = ConformanceKind.SimOnly,
                Behaviour = "ReplaceText leaves <SelectionMarker/> elements; saved into the topic they misplace the next replacement.",
                Run = open =>
                {
                    var doc = (FlareSimDocument)open(Topic);
                    var p = Block(doc, 0);
                    p.AppendChild(doc.Document.CreateElement("SelectionMarker"));
                    Check.Throws<InvalidOperationException>(() => doc.Save(System.IO.Path.GetTempFileName()), "Save with a marker in the DOM");
                    Check.That(doc.UntrackedWrites.Count == 1, "the direct DOM write is reported as untracked");
                }
            },
        };

        public static ConformanceCase Find(string name)
        {
            foreach (var c in Cases) if (c.Name == name) return c;
            throw new ArgumentException("No conformance case " + name);
        }

        /// <summary>
        /// Runs every case that applies. <paramref name="insideFlare"/> runs only <see cref="ConformanceKind.Flare"/> cases
        /// (the others crash Flare or need FlareSim controls) and reports the rest as skipped.
        /// </summary>
        public static IList<ConformanceResult> RunAll(OpenScratch open, bool insideFlare)
        {
            var results = new List<ConformanceResult>();
            foreach (var c in Cases)
            {
                if (insideFlare && c.Kind != ConformanceKind.Flare) { results.Add(new ConformanceResult { Case = c, Skipped = true, Message = c.Kind.ToString() }); continue; }
                try { c.Run(open); results.Add(new ConformanceResult { Case = c, Passed = true }); }
                catch (Exception ex) { results.Add(new ConformanceResult { Case = c, Message = ex.GetType().Name + ": " + ex.Message }); }
            }
            return results;
        }

        // ---- helpers ------------------------------------------------------------------------------

        /// <summary>The n-th paragraph (p) of the body.</summary>
        public static XmlElement Block(IDocumentAdapter doc, int n)
        {
            var ps = new List<XmlElement>();
            foreach (XmlNode c in BlockSelector.FindBody(doc.Document).ChildNodes)
                if (c.NodeType == XmlNodeType.Element && c.LocalName == "p") ps.Add((XmlElement)c);
            return ps[n];
        }

        /// <summary>The longest live text node of paragraph n.</summary>
        public static XmlText Run(IDocumentAdapter doc, int n)
        {
            XmlText best = null;
            foreach (XmlNode t in Block(doc, n).SelectNodes(".//text()"))
                if (t is XmlText && (best == null || t.Value.Length > best.Value.Length)) best = (XmlText)t;
            return best;
        }

        /// <summary>Replaces the first occurrence of <paramref name="find"/> in paragraph n's text nodes (current DOM).</summary>
        public static bool Replace(IDocumentAdapter doc, int n, string find, string with)
        {
            foreach (XmlNode t in Block(doc, n).SelectNodes(".//text()"))
            {
                var at = t.Value.IndexOf(find, StringComparison.Ordinal);
                if (at >= 0 && t is XmlText && !InChange(t)) return doc.ReplaceSpan((XmlText)t, at, find.Length, with);
            }
            return false;
        }

        private static bool InChange(XmlNode t)
        {
            return t.ParentNode != null && t.ParentNode.LocalName == "change" && t.ParentNode.NamespaceURI == BlockSelector.MadCapNs;
        }

        public static string Accepted(IDocumentAdapter doc, int n)
        {
            return AcceptedView.Of(new List<XmlNode> { Block(doc, n) })[0].InnerText;
        }

        private static XmlElement NewParagraph(IDocumentAdapter doc, string cls, string text)
        {
            var p = doc.Document.CreateElement("p");
            p.SetAttribute("class", cls);
            p.AppendChild(doc.Document.CreateTextNode(text));
            return p;
        }

        private static List<XmlElement> Changes(XmlNode scope)
        {
            var list = new List<XmlElement>();
            foreach (XmlNode e in ((XmlElement)scope).GetElementsByTagName("change", BlockSelector.MadCapNs)) list.Add((XmlElement)e);
            return list;
        }

        private static string ChangeId(XmlElement e) { return e.GetAttribute("changes", BlockSelector.MadCapNs); }

        private static HashSet<string> AllChangeIds(IDocumentAdapter doc)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var ns = new XmlNamespaceManager(doc.Document.NameTable);
            ns.AddNamespace("MadCap", BlockSelector.MadCapNs);
            foreach (XmlElement e in doc.Document.SelectNodes("//*[@MadCap:changes]", ns))
                foreach (var id in ChangeId(e).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) ids.Add(id);
            return ids;
        }

        private static HashSet<string> Ids(IDocumentAdapter doc, string kind)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlElement e in doc.Document.GetElementsByTagName(kind, BlockSelector.MadCapNs)) ids.Add(e.GetAttribute("id", BlockSelector.MadCapNs));
            return ids;
        }
    }
}
