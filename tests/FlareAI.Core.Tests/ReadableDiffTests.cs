using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FlareAI.Core.Editing;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class ReadableDiffTests
    {
        private const string Ns = "xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"";

        private static IList<XmlNode> Blocks(string body)
        {
            var doc = new XmlDocument();
            doc.LoadXml("<html " + Ns + "><body>" + body + "</body></html>");
            return doc.DocumentElement["body"].ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
        }

        private static string Render(List<DiffLine> lines)
        {
            return string.Join("\n", lines.Select(l => l.Prefix + string.Concat(l.Segments.Select(s =>
                s.Kind == DiffKind.Same ? s.Text : s.Kind == DiffKind.Deleted ? "[-" + s.Text + "-]" : "{+" + s.Text + "+}"))));
        }

        [Fact]
        public void WordChangesInAParagraphAreMarkedInline()
        {
            var diff = ReadableDiff.Of(Blocks("<p>In order to sign in, click <b>Save</b>.</p>"), Blocks("<p>To sign in, click <b>Save</b>.</p>"));
            Assert.Equal("[-In order to-]{+To+} sign in, click Save.", Render(diff));
        }

        [Fact]
        public void ListItemsAreNumberedAndSplitStepsShowAsInsertions()
        {
            var before = Blocks("<ol><li><p>Go to Settings.</p></li><li><p>Enter the URL and click Save.</p></li></ol>");
            var after = Blocks("<ol><li><p>Go to Settings.</p></li><li><p>Enter the URL.</p></li><li><p>Click Save.</p></li></ol>");
            var text = Render(ReadableDiff.Of(before, after));
            Assert.Equal("1. Go to Settings.\n2. Enter the URL[- and click Save-].\n3. {+Click Save.+}", text);
        }

        [Fact]
        public void VariablesAndSnippetsReadAsPlaceholders()
        {
            var b = Blocks("<p>Use <MadCap:variable name=\"Company.Product\" /> with <MadCap:snippetText src=\"../Snip/Warn.flsnp\" />.</p>");
            var diff = ReadableDiff.Of(b, b);
            Assert.Equal("Use [Company.Product] with [snippet Warn].", Render(diff));
            Assert.False(diff[0].Changed);
        }

        [Fact]
        public void HeadingsGetAMarkerAndUnchangedLinesStay()
        {
            var diff = ReadableDiff.Of(Blocks("<h2>Steps</h2><p>Same.</p>"), Blocks("<h2>Procedure</h2><p>Same.</p>"));
            Assert.Equal("## [-Steps-]{+Procedure+}\nSame.", Render(diff));
            Assert.True(diff[0].Changed);
            Assert.False(diff[1].Changed);
        }

        [Fact]
        public void SpanDiffForCards()
        {
            var segs = ReadableDiff.OfText("utilize the app", "use the app");
            Assert.Equal(new[] { DiffKind.Deleted, DiffKind.Inserted, DiffKind.Same }, segs.Select(s => s.Kind).ToArray());
        }
    }
}
