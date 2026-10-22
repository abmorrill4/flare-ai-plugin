using System.Drawing;
using System.Windows.Forms;
using FlareAI.Flare.UI;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>
    /// The dialog layout used by Settings and first-run setup: a status line is as tall as its text, so a one-line status
    /// leaves no gap before the note under it; a hidden block (a note for the other choice) leaves no gap either.
    /// </summary>
    public class StackLayoutTests
    {
        private const string Long = "✓ Guide (120 rules). Read-only for you: only its owners (lead@example.com, backup@example.com) change its rules. " +
                                    "Your suggested changes go to them. This line is long enough to wrap onto several lines in the field column.";

        [Theory]
        [InlineData(1f)]
        [InlineData(1.25f)]
        public void AStatusLine_FitsItsText_AndTheNoteBelowFollows(float scale)
        {
            using (var page = new Panel { Font = SystemFonts.MessageBoxFont, Size = new Size(800, 600) })
            {
                var g = new StackLayout(page, scale);
                var status = g.Status(1);
                var note = g.Note("Leave the list empty to use Google's developer documentation style guide plus your own changes.");
                g.Reflow();
                var gap = note.Top - status.Bottom;

                status.Text = "Using Google's guide plus your own changes.";
                g.FitHeight(status);
                Assert.Equal(gap, note.Top - status.Bottom);                     // one line: the note sits right under it
                Assert.True(status.Height <= (page.Font.Height + 1) + g.S(2));

                status.Text = Long;
                g.FitHeight(status);
                Assert.True(status.Height > 2 * page.Font.Height);              // several lines: nothing cut off…
                Assert.Equal(gap, note.Top - status.Bottom);                     // …and the note moved down with it

                status.Text = "Checking…";
                g.FitHeight(status);
                Assert.Equal(gap, note.Top - status.Bottom);                     // and back up: no gap left behind
            }
        }

        [Fact]
        public void TheNoteForTheOtherChoice_IsHidden_AndLeavesNoGap()
        {
            using (var page = new Panel { Font = SystemFonts.MessageBoxFont, Size = new Size(800, 600) })
            {
                var g = new StackLayout(page, 1f);
                var choices = g.Choices("Whose style rules", new[] { "Google's guide plus my own changes", "My team's shared style guide folder" }, g.S(8));
                var googleNote = g.Begin();
                g.Note("Google's developer documentation style guide is built in.", true);
                var folder = g.Begin();
                var box = g.Row("Team style guide", new TextBox());
                var full = g.FullHeight();

                googleNote.Visible = false; folder.Visible = true;   // the team's folder is chosen
                g.Reflow();
                Assert.Equal(choices[0].Parent.Bottom + g.S(6), box.Top);
                Assert.True(g.VisibleHeight() < full);

                googleNote.Visible = true; folder.Visible = false;   // Google's guide is chosen
                g.Reflow();
                Assert.False(box.Visible);
                Assert.True(g.VisibleHeight() < full);
            }
        }
    }
}
