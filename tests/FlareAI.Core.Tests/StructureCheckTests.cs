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
        public void CleanTopicHasNoFindings()
        {
            Assert.Empty(StructureCheck.Of(Body(
                "<h1>Pay bills</h1><p><img src=\"a.png\" alt=\"The Pay Bills page\" /></p><h2>Steps</h2>" +
                "<table><tr><th>Name</th></tr><tr><td>A</td></tr></table><p>See <a href=\"y.htm\">Payee limits</a>.</p>")));
        }
    }
}
