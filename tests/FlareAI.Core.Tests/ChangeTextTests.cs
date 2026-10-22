using FlareAI.Core.Editing;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>How changes and rules read to writers (#123 hands-on fixes): deletions read as deletions, rules without arrows.</summary>
    public class ChangeTextTests
    {
        [Theory]
        [InlineData("Please note that y", "Y", "Delete \"Please note that\"")]   // a deleted phrase that takes the next letter's case along
        [InlineData("Simply c", "C", "Delete \"Simply\"")]
        [InlineData("In order to", "To", "Change \"In order to\" to \"To\"")]   // a whole word, not a carried-over letter
        [InlineData("click on", "click", "Change \"click on\" to \"click\"")]
        [InlineData("very", "", "Delete \"very\"")]
        [InlineData("utilize", "use", "Change \"utilize\" to \"use\"")]
        [InlineData("e-mail", "email", "Change \"e-mail\" to \"email\"")]
        [InlineData("OK", "ok", "Change \"OK\" to \"ok\"")]
        public void ARow_SaysDeleteForADeletion_AndChangeOtherwise(string excerpt, string replacement, string row)
        {
            Assert.Equal(row, ChangeText.Row(excerpt, replacement));
        }

        [Fact]
        public void TheCard_StrikesOnlyTheDeletedWords()
        {
            var d = ChangeText.Diff("Please note that y", "Y");
            Assert.Single(d);
            Assert.Equal(DiffKind.Deleted, d[0].Kind);
            Assert.Equal("Please note that", d[0].Text);
            Assert.Contains(ChangeText.Diff("utilize", "use"), s => s.Kind == DiffKind.Inserted);
        }

        [Theory]
        [InlineData("Avoid 'in order to' → use 'to'. Shorter.", "Use 'to' instead of 'in order to'. Shorter.")]
        [InlineData("Avoid 'e-mail' → use 'email'.", "Use 'email' instead of 'e-mail'.")]
        [InlineData("Avoid 'simply' (delete it). It adds nothing.", "Leave out 'simply'. It adds nothing.")]
        [InlineData("Avoid 'whitelist' (rewrite).", "Avoid 'whitelist'.")]
        [InlineData("Write in the second person.", "Write in the second person.")]
        public void RuleText_ReadsWithoutArrows(string stored, string shown)
        {
            Assert.Equal(shown, RuleWording.Plain(stored));
            Assert.Equal(shown, RuleWording.Display(stored, null, null));
            Assert.DoesNotContain("→", RuleWording.Display(stored, null, null));
        }

        [Fact]
        public void CommitIsMentioned_OnlyForAGuideUnderSourceControl()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hs-sc-" + System.Guid.NewGuid().ToString("N"));
            var guide = System.IO.Path.Combine(root, "Guides", "Company");
            System.IO.Directory.CreateDirectory(guide);
            try
            {
                Assert.False(FlareAI.Core.Actions.Team.IsUnderSourceControl(guide));   // a shared folder: nothing to commit
                Assert.Equal("", FlareAI.Core.Actions.Team.CommitHint(guide));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, ".git"));   // a repository above the guide
                Assert.True(FlareAI.Core.Actions.Team.IsUnderSourceControl(guide));
                Assert.Equal(" Commit the style guide so everyone gets it.", FlareAI.Core.Actions.Team.CommitHint(guide));
                Assert.False(FlareAI.Core.Actions.Team.IsUnderSourceControl(null));
            }
            finally { try { System.IO.Directory.Delete(root, true); } catch (System.Exception) { } }
        }

        [Fact]
        public void TheDisableRuleDialog_SaysDisabled_AndReadsItBackAsOff()
        {
            Assert.Equal("Disabled", Severity.Label("off"));
            Assert.Equal("off", Severity.Normalize("Disabled"));
            Assert.Equal("suggestion", Severity.Normalize("Suggestion"));
        }
    }
}
