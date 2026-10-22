using System.Collections.Generic;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class GuideEditsTests
    {
        private static XmlDocument Guide()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<html><body><h1>Terminology</h1><ul>" +
                        "<li>Address the reader as \"you\". When you name a group, use the role that can do the task.<ul><li>Bad: The end user must approve the request.</li><li>Good: Administrators must approve the request.</li></ul></li>" +
                        "<li>Insert the product name variable, not the product name as text.</li></ul>" +
                        "<table><tr><th>Avoid</th><th>Use</th></tr><tr><td>prior to</td><td>before</td><td>warning</td><td>Plain words.</td><td>Save your work prior to leaving.</td></tr></table></body></html>");
            return doc;
        }

        [Fact]
        public void FindsTheRulesBulletAndTableRow()
        {
            var doc = Guide();
            var li = GuideEdits.Locate(doc, "Address the reader as \"you\". When you name a group, use the role that can do the task.");
            Assert.Equal("li", li.LocalName);
            Assert.StartsWith("Address the reader", li.InnerText);
            var tr = GuideEdits.Locate(doc, "Avoid 'prior to' → use 'before'. Plain words.");
            Assert.Equal("tr", tr.LocalName);
            Assert.Null(GuideEdits.Locate(doc, "Screenshots and UI labels are the source of truth"));
        }

        [Fact]
        public void InstructionCarriesTheEvidenceAndKeepsTheFormat()
        {
            var item = new TuneItem { Kind = "disputed", Rule = "company:terminology/1", Title = "Writers disagree on company:terminology/1",
                                      Evidence = new List<string> { "raj rejects it (100% of 5)", "\"end user portal\" (raj, transfer.htm)" } };
            var text = GuideEdits.Instruction(item, new GuideRule { Id = "company:terminology/1", Text = "Address the reader as you" }, false);
            Assert.Contains("end user portal", text);
            Assert.Contains("says when it applies", text);
            Assert.Contains("Bad/Good", text);
            Assert.Contains("five cells", GuideEdits.Instruction(item, null, true));
        }
    }
}
