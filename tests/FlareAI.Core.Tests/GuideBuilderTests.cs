using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class GuideBuilderTests
    {
        private static RuleCandidate Mined()
        {
            var json = JObject.Parse(@"{""candidates"":[{""id"":""synonym:sign-in"",""kind"":""synonym"",""title"":""One word for signing in"",
                ""forms"":[{""text"":""sign in"",""count"":4},{""text"":""log in"",""count"":2}],""prefer"":""sign in"",""impact"":2,
                ""examples"":[{""text"":""Sign in daily."",""file"":""a.htm"",""form"":""sign in""},{""text"":""Log in to see statements."",""file"":""a.htm"",""form"":""log in""}],
                ""row"":{""avoid"":""log in"",""use"":""sign in"",""severity"":""warning"",""why"":""Team usage."",""example"":""Log in to see statements.""}}]}");
            return GuideBuilder.ParseMined(json)[0];
        }

        [Fact]
        public void MinedCandidateParsesAndTheOwnerCanPickTheOtherForm()
        {
            var c = Mined();
            Assert.True(c.IsRow);
            Assert.Equal("log in", c.Avoid);
            c.Choose("log in");
            Assert.Equal("sign in", c.Avoid);
            Assert.Equal("log in", c.Use);
            Assert.Equal("Sign in daily.", c.Example); // the example must break the new rule
        }

        [Fact]
        public void ChoosingAFormPairsPlurals()
        {
            var c = new RuleCandidate { Kind = "synonym", Forms = new List<TermForm> {
                new TermForm { Text = "user", Count = 4, Plural = "users" }, new TermForm { Text = "member", Count = 2, Plural = "members" }, new TermForm { Text = "customer", Count = 1, Plural = "customers" } } };
            c.Choose("member");
            Assert.Equal("user, users, customer, customers", c.Avoid);
            Assert.Equal("member, members, member, members", c.Use);
        }

        [Fact]
        public void ExtractedRulesKeepRowsAndBulletsAndDropEmptyOnes()
        {
            var list = GuideBuilder.ParseExtracted(@"[{""title"":""Easy"",""avoid"":""easy, simply"",""use"":""(delete)"",""severity"":""ERROR"",""why"":""Condescending."",""example"":""It is easy to set up."",""quote"":""we never say easy""},
                {""title"":""Lead with the goal"",""text"":""Start each step with the goal."",""bad"":""Click Save to keep it."",""good"":""To keep it, click Save."",""quote"":""goal first""},
                {""title"":""nothing""}]");
            Assert.Equal(2, list.Count);
            Assert.True(list[0].IsRow);
            Assert.Equal("error", list[0].Severity);
            Assert.False(list[1].IsRow);
            Assert.Equal("To keep it, click Save.", list[1].Good);
        }

        [Fact]
        public void WritesRowsAndBulletsIntoANewTopicThenAppends()
        {
            var path = Path.Combine(Path.GetTempPath(), "guide-" + Guid.NewGuid(), "From-our-content.htm");
            try
            {
                var bullet = new RuleCandidate { Kind = "heading", Text = "Use title case for headings.", Bad = "Change your settings", Good = "Change Your Settings" };
                Assert.Equal(2, GuideBuilder.Write(path, new List<RuleCandidate> { Mined(), bullet }, "From our content"));
                Assert.Equal(1, GuideBuilder.Write(path, new List<RuleCandidate> { Mined() }, "From our content"));
                var doc = new XmlDocument();
                doc.Load(path);
                Assert.Equal(1, doc.SelectNodes("//*[local-name()='table']").Count);
                Assert.Equal(3, doc.SelectNodes("//*[local-name()='tr']").Count);           // header + 2 rows
                Assert.Equal("Bad: Change your settings", doc.SelectSingleNode("//*[local-name()='ul']/*[local-name()='li']/*[local-name()='ul']/*[local-name()='li']").InnerText);
                Assert.Equal("From our content", doc.SelectSingleNode("//*[local-name()='h1']").InnerText);
            }
            finally { Directory.Delete(Path.GetDirectoryName(path), true); }
        }
    }
}
