using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Actions;
using FlareAI.Core.Mcp;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class StyleFlowTests
    {
        private static ActionRunner Runner(FakeLlmClient llm, IStyleService style)
        {
            return new ActionRunner(llm, new PromptBuilder(style == null ? (Func<string>)null : style.Guide), 1024, style);
        }

        private static string P1(FakeDocumentAdapter doc) { return doc.Document.SelectSingleNode("//body/p[1]").InnerText; }

        [Fact]
        public void CheckStyle_LintFindingsAreFixableAsOneTextOperation()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var runner = Runner(new FakeLlmClient(r => "[]"), new FakeStyleService());
            var findings = runner.CheckStyle(doc);

            var utilize = findings.Single(f => f.Rule == "plain-words.utilize");
            Assert.Equal("lint", utilize.Source);
            Assert.True(utilize.CanFix);
            Assert.All(findings.Where(f => f.Rule == "person.the-user"), f => Assert.False(f.CanFix));

            Assert.Equal(1, runner.ApplyFixes(doc, findings, "FlareAI fix"));
            Assert.Contains("able to use SSO", P1(doc));
            Assert.Single(doc.TextOperations);
            Assert.True(utilize.IsFixed);
            Assert.Equal(0, runner.ApplyFixes(doc, findings, "again")); // fixed findings are not re-applied
        }

        [Fact]
        public void Review_GetsLintSummaryAndGuide_KeepsOnlyVerbatimNonDuplicateFindings()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var llm = new FakeLlmClient(r => @"[
              {""rule"":""passive"",""severity"":""warning"",""excerpt"":""they have been granted the appropriate permissions by their administrator"",""replacement"":""your administrator has granted you the required permissions"",""why"":""Active voice.""},
              {""rule"":""made up"",""severity"":""error"",""excerpt"":""text that is not in the topic"",""replacement"":null,""why"":""x""},
              {""rule"":""repeat"",""severity"":""warning"",""excerpt"":""utilize"",""replacement"":""use"",""why"":""x""},
              {""rule"":""markup"",""severity"":""warning"",""excerpt"":""SSO"",""replacement"":""<b>SSO</b>"",""why"":""x""}
            ]");
            var findings = Runner(llm, new FakeStyleService()).CheckStyle(doc);

            Assert.Contains("\"utilize\" → \"use\"", llm.Requests[0].User);
            Assert.Contains("members", llm.Requests[0].System);
            var ai = findings.Where(f => f.Source == "ai").ToList();
            Assert.Equal(new[] { "passive", "markup" }, ai.Select(f => f.Rule).ToArray());
            Assert.True(ai[0].CanFix);
            Assert.False(ai[1].CanFix); // replacement with markup is not a safe text fix
            Assert.NotNull(ai[0].Block);
        }

        [Fact]
        public void ApplyFixes_RelocatesAfterEarlierEditsInTheSameRun()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var runner = Runner(new FakeLlmClient(r => @"[{""rule"":""wordy"",""severity"":""warning"",""excerpt"":""prior to attempting to proceed with"",""replacement"":""before starting"",""why"":""Shorter.""}]"), new FakeStyleService());
            var findings = runner.CheckStyle(doc);

            Assert.Equal(1, runner.ApplyFixes(doc, findings.Where(f => f.Source == "lint"), "lint fixes"));
            Assert.Equal(1, runner.ApplyFixes(doc, findings.Where(f => f.Source == "ai"), "ai fix"));
            Assert.Contains("use SSO", P1(doc));
            Assert.Contains("administrator before starting the configuration", P1(doc));
        }

        [Fact]
        public void PromptFragment_HasNoRepeatedNamespaceDeclarations()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var llm = new FakeLlmClient(r => "[]");
            Runner(llm, null).CheckStyle(doc);
            Assert.Contains("<MadCap:variable name=\"General.ProductName\" />", llm.Requests[0].User);
            Assert.DoesNotContain("xmlns:MadCap", llm.Requests[0].User);
        }

        [Fact]
        public void GoTo_SelectsTheExcerpt()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var runner = Runner(new FakeLlmClient(r => "[]"), new FakeStyleService());
            var f = runner.CheckStyle(doc).First();
            Assert.True(runner.GoTo(doc, f));
            Assert.Equal(f.Excerpt, doc.LastSelected);
        }

        [Fact]
        public void Rewrite_LintsItsOwnOutput_AndRepairsOnce()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[1]/text()[1]");
            var calls = 0;
            var llm = new FakeLlmClient(r => ++calls == 1
                ? "<revised><p>To utilize SSO, ask your administrator for access. <MadCap:variable name=\"General.ProductName\" /> supports SAML 2.0 and OIDC.</p></revised>"
                : "<revised><p>To use SSO, ask your administrator for access. <MadCap:variable name=\"General.ProductName\" /> supports SAML 2.0 and OIDC.</p></revised>");
            var proposal = Runner(llm, new FakeStyleService()).Rewrite(doc, null);

            Assert.Equal(2, llm.Requests.Count);
            Assert.Contains("\"utilize\" → \"use\"", llm.Requests[0].User);   // original lint in the first prompt
            Assert.Contains("still flags", llm.Requests[1].User);             // repair round
            Assert.True(proposal.Validation.IsValid, proposal.Validation.ToString());
            Assert.Empty(proposal.RemainingLint);
            Assert.Contains("To use SSO", proposal.ProposedXml);
        }

        [Fact]
        public void Rewrite_WithLinterDown_StillWorks_AndSaysSo()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var proposal = Runner(new FakeLlmClient(FakeLlmClient.EchoRevised), new BrokenStyleService()).Rewrite(doc, null);
            Assert.True(proposal.Validation.IsValid);
            Assert.Contains(proposal.Notes, n => n.Contains("linter unavailable"));
        }

        [Fact]
        public void Docstyle_OverRealMcp_LintsAndServesGuide()
        {
            // Integration: the shipped server through the real stdio client. Skips quietly when uv is absent.
            var server = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\mcp\docstyle\server.py"));
            if (!File.Exists(server) || !OnPath("uv.exe")) return;
            using (var svc = new McpStyleService("uv", "run --quiet --script \"" + server + "\"", Path.GetDirectoryName(server), 120000))
            {
                var hits = svc.Lint(new[] { "Configure Single Sign-On", "It supports SAML 2.0.", "In order to utilize SSO, click here." }, new[] { 0 });
                Assert.Contains(hits, h => h.Index == 0 && h.Match == "Single Sign-On" && h.Replacement == "single sign-on");
                Assert.DoesNotContain(hits, h => h.Index == 1);
                Assert.Contains(hits, h => h.Index == 2 && h.Match == "utilize" && h.Replacement == "use" && h.Layer == "company");
                Assert.Contains(hits, h => h.Rule == "Company.LinkText" && h.Replacement == null && h.Link.Contains("link-text"));
                var guide = svc.Guide();
                Assert.Contains("Google developer documentation style guide", guide);
                Assert.Contains("members", guide);
            }
        }

        private static bool OnPath(string exe)
        {
            return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Any(d => { try { return File.Exists(Path.Combine(d.Trim(), exe)); } catch (ArgumentException) { return false; } });
        }

        private sealed class BrokenStyleService : IStyleService
        {
            public System.Collections.Generic.IList<LintHit> Lint(System.Collections.Generic.IList<string> texts, System.Collections.Generic.IList<int> headings) { throw new McpException("server exited"); }
            public string Guide() { throw new McpException("server exited"); }
        }
    }
}
