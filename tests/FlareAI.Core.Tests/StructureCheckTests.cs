using System.Collections.Generic;
using System.Linq;
using System.Xml;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class StructureCheckTests
    {
        private static List<XmlNode> Body(string html)
        {
            var doc = new XmlDocument();
            doc.LoadXml("<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><body>" + html + "</body></html>");
            return doc.DocumentElement["body"].ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
        }

        [Fact]
        public void FindsAccessibilityAndStructureProblems()
        {
            var found = StructureCheck.Of(Body(
                "<h1>Pay bills</h1><p>The payee list: <img src=\"a.png\" /></p><h3>Skipped a level</h3>" +
                "<h1>Second title</h1><table><tr><td>Name</td><td>Amount</td></tr></table>" +
                "<p>For details, <a href=\"x.htm\">click here</a>. Or see <a href=\"y.htm\">Payee limits</a>.</p>" +
                "<p>Call 800-555-0100 or write to help@bank.example, or visit www.bank.example/help.</p>"));
            var rules = found.Select(f => f.Rule).ToList();
            Assert.Contains("Structure.AltText", rules);
            Assert.Contains("Structure.HeadingOrder", rules);
            Assert.Contains("Structure.OneH1", rules);
            Assert.Contains("Structure.TableHeader", rules);
            Assert.Single(found, f => f.Rule == "Structure.LinkText");
            Assert.Equal(3, found.Count(f => f.Rule == "Structure.HardCoded"));
            Assert.All(found, f => Assert.Equal("manual", f.Kind));
            Assert.Equal("The payee list:", found.Single(f => f.Rule == "Structure.AltText").Excerpt.Trim()); // anchored on text before the image
            Assert.Equal("800-555-0100", found.First(f => f.Rule == "Structure.HardCoded").Excerpt);
        }

        [Fact]
        public void ImageAloneInItsParagraph_IsFlagged_AndFoundFromTheFinding()
        {
            // Flare's usual pattern: the image has a paragraph of its own, with no text to anchor on (bench: 10/10 missed).
            var blocks = Body("<h1>Pay bills</h1><p>Open the payee list.</p><p><img src=\"a.png\" /></p>");
            var f = Assert.Single(StructureCheck.Of(blocks), x => x.Rule == "Structure.AltText");
            Assert.Equal("Open the payee list.", f.Excerpt);
            Assert.NotNull(FlareAI.Core.Actions.AltText.MissingIn(f.Block));
        }

        [Fact]
        public void CodeIsLiteral_NotLinted_NotEditable()
        {
            var blocks = Body("<p>Edit <code>www.bank.example/FlareAI/owners.txt</code> to add owners.</p><pre>call 800-555-0100</pre>");
            Assert.Empty(StructureCheck.Of(blocks));                                        // no "hard-coded URL / phone"
            Assert.DoesNotContain(TextRuns.Of(blocks), r => r.Value.Contains("owners.txt")); // no AI run can touch it
            var line = Assert.Single(TextLines.Of(blocks, null), l => l.Text.Contains("to add owners"));
            Assert.DoesNotContain("owners.txt", line.Text);
        }

        [Fact]
        public void MarkerCss_AddressesEachBlockBySiblingChain_ColoredByWorstSeverity()
        {
            var blocks = Body("<h1>T</h1><p>One</p><ol><li>A</li><li>B</li></ol><MadCap:dropDown><p>In</p></MadCap:dropDown>");
            var li2 = blocks[2].ChildNodes[1];
            Assert.Equal("body > h1 + p", FlareAI.Core.Editing.MarkerCss.SelectorFor(blocks[1]));
            Assert.Equal("body > h1 + p + ol > li + li", FlareAI.Core.Editing.MarkerCss.SelectorFor(li2));
            Assert.Equal("body > h1 + p + ol + MadCap|dropDown > p", FlareAI.Core.Editing.MarkerCss.SelectorFor(blocks[3].FirstChild));
            var css = FlareAI.Core.Editing.MarkerCss.Css(new[] { new KeyValuePair<XmlNode, string>(blocks[1], "suggestion"), new KeyValuePair<XmlNode, string>(blocks[1], "error") });
            Assert.Contains("body > h1 + p { border-left: 3px solid #c62828;", css);   // worst severity wins
            Assert.Single(css.Split('\n'), l => l.StartsWith("body"));
        }

        [Fact]
        public void CleanTopicHasNoFindings()
        {
            Assert.Empty(StructureCheck.Of(Body(
                "<h1>Pay bills</h1><p><img src=\"a.png\" alt=\"The Pay Bills page\" /></p><h2>Steps</h2>" +
                "<table><tr><th>Name</th></tr><tr><td>A</td></tr></table><p>See <a href=\"y.htm\">Payee limits</a>.</p>")));
        }
    }
}
