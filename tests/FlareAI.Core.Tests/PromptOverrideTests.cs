using System;
using System.IO;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class PromptOverrideTests
    {
        [Fact]
        public void ATeamOverrideReplacesTheBuiltInTemplate()
        {
            var dir = Path.Combine(Path.GetTempPath(), "prompts-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                var builder = new PromptBuilder(null);
                builder.OverrideFolder = dir;
                Assert.Contains("senior technical editor", builder.BuildUser(ActionKind.CheckStyle, "<p>x</p>", null));
                File.WriteAllText(Path.Combine(dir, "action-check.md"), "Be strict about banking terms.\n{{LINT}}\n{{FRAGMENT}}");
                var user = builder.BuildUser(ActionKind.CheckStyle, "<p>x</p>", null, "- lint");
                Assert.StartsWith("Be strict about banking terms.", user);
                Assert.Contains("<p>x</p>", user);
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
