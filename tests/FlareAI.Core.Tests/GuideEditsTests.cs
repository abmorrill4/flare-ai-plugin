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
                        "<li>Refer to end users of the product as \"members\", not \"customers\" or \"users\".<ul><li>Bad: Customers can reset their own PIN.</li><li>Good: Members can reset their own PIN.</li></ul></li>" +
                        "<li>Refer to the bank or credit union as \"the financial institution\".</li></ul>" +
                        "<table><tr><th>Avoid</th><th>Use</th></tr><tr><td>prior to</td><td>before</td><td>warning</td><td>Plain words.</td><td>Save your work prior to leaving.</td></tr></table></body></html>");
            return doc;
        }

        [Fact]
        public void FindsTheRulesBulletAndTableRow()
        {
            var doc = Guide();
            var li = GuideEdits.Locate(doc, "Refer to end users of the product as \"members\", not \"customers\" or \"users\".");
            Assert.Equal("li", li.LocalName);
            Assert.StartsWith("Refer to end users", li.InnerText);
            var tr = GuideEdits.Locate(doc, "Avoid 'prior to' → use 'before'. Plain words.");
            Assert.Equal("tr", tr.LocalName);
            Assert.Null(GuideEdits.Locate(doc, "Screenshots and UI labels are the source of truth"));
        }

        [Fact]
        public void InstructionCarriesTheEvidenceAndKeepsTheFormat()
        {
            var item = new TuneItem { Kind = "disputed", Rule = "company:terminology/1", Title = "Writers disagree on company:terminology/1",
                                      Evidence = new List<string> { "raj rejects it (100% of 5)", "\"customer service\" (raj, transfer.htm)" } };
            var text = GuideEdits.Instruction(item, new GuideRule { Id = "company:terminology/1", Text = "Refer to end users as members" }, false);
            Assert.Contains("customer service", text);
            Assert.Contains("says when it applies", text);
            Assert.Contains("Bad/Good", text);
            Assert.Contains("five cells", GuideEdits.Instruction(item, null, true));
        }
    }
}
