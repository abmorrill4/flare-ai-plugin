using System.Collections.Generic;
using System.Linq;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class GroundingTests
    {
        [Fact]
        public void AiFindings_KeepRealCitations_DropMadeUpOnes()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var llm = new FakeLlmClient(r => @"[
              {""rule_id"":""company:terminology/1"",""rule"":""audience"",""severity"":""warning"",""excerpt"":""the user should simply ensure"",""replacement"":null,""why"":""Say administrators.""},
              {""rule_id"":""company:invented/9"",""rule"":""made up"",""severity"":""warning"",""excerpt"":""appropriate permissions"",""replacement"":null,""why"":""x""}
            ]");
            var style = new FakeStyleService();
            var runner = new ActionRunner(llm, new PromptBuilder(() => style.Guide(null)), 1024, style);
            var ai = runner.CheckStyle(doc).Where(f => f.Source == "ai").ToList();

            Assert.Contains("rule_id", llm.Requests[0].User);                    // the prompt asks for citations
            var cited = ai.Single(f => f.Rule == "audience");
            Assert.Equal("company:terminology/1", cited.RuleId);
            Assert.Equal("Audience", cited.RuleSection);
            Assert.Equal("file:///C:/guide/Terminology.htm", cited.Link);
            Assert.Null(ai.Single(f => f.Rule == "made up").RuleId);              // unknown id -> "general"
        }

        [Fact]
        public void VerifyExamples_PassesWhenBadIsCitedAndGoodIsClean()
        {
            var style = new FakeStyleService();
            var llm = new FakeLlmClient(r => r.User.Contains("The end user")
                ? @"[{""rule_id"":""company:terminology/1"",""rule"":""audience"",""severity"":""warning"",""excerpt"":""The end user"",""replacement"":""Administrators"",""why"":""x""}]"
                : "[]");
            var runner = new ActionRunner(llm, new PromptBuilder(() => style.Guide(null)), 1024, style);
            var results = runner.VerifyExamples(new List<GuideExample>
            {
                new GuideExample { Id = "company:terminology/1", Bad = "The end user approves the request.", Good = "Administrators approve the request." },
                new GuideExample { Id = "company:terminology/1", Bad = "Someone approves it.", Good = "" }
            });
            Assert.True(results[0].Passed);
            Assert.False(results[1].BadCaught);
            Assert.Null(results[1].GoodClean);
            Assert.Equal(4 - 1, llm.Requests.Count);
        }
    }
}
