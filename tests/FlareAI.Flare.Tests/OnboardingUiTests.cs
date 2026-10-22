using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FlareAI.Core;
using FlareAI.Core.Actions;
using FlareAI.Core.Settings;
using FlareAI.Flare.UI;
using FlareAI.TestKit.Editor;
using FlareAI.TestKit.Llm;
using Xunit;

namespace FlareAI.Flare.Tests
{
    /// <summary>
    /// Onboarding UI (#123) laid out for real: setup's role step, the guided first lint's hint strip in the issues pane at
    /// Flare's narrow docked width, and the getting-started checklist in the empty pane. The writer's data folder is a
    /// temp folder, so no real settings or onboarding state are read or written.
    /// </summary>
    [Collection("Panes")]
    public class OnboardingUiTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "hs-ui-" + Guid.NewGuid().ToString("N"));

        public OnboardingUiTests()
        {
            Directory.CreateDirectory(_root);
            Paths.Root = _root;
            GuidedTour.Stop();
        }

        public void Dispose()
        {
            GuidedTour.Stop();
            Paths.Root = null;
            try { Directory.Delete(_root, true); } catch (Exception) { }
        }

        private const string Ns = "xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"";

        [Fact]
        public void Setup_StartsWithTheRoleQuestion_InFourSteps_AndTheOwnerAnswerLeadsWithTheTeamFolder()
        {
            Sta(() =>
            {
                using (var f = new SetupForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
                {
                    f.Show();
                    Pump(null, 200);
                    var labels = All(f).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
                    Assert.Contains("Step 1 of 4", labels);
                    Assert.Contains("How will you use House Style?", labels);
                    var roles = All(f).OfType<RadioButton>().Where(r => r.Visible).ToList();
                    Assert.Equal(new[] { "To check my own writing", "To set up the style guide for my team" }, roles.Select(r => r.Text).ToArray());
                    Assert.True(roles[0].Checked, "a new writer starts as a writer");
                    var next = All(f).OfType<Button>().Single(b => b.Text == "Next");
                    foreach (var r in roles)
                    {
                        var at = f.RectangleToClient(r.RectangleToScreen(r.ClientRectangle));
                        Assert.True(at.Bottom <= next.Top, "'" + r.Text + "' overlaps the buttons");
                        Assert.True(at.Right <= f.ClientSize.Width, "'" + r.Text + "' is cut off");
                    }
                    Assert.False(All(f).OfType<Button>().Single(b => b.Text == "Back").Visible);

                    roles[1].Checked = true;   // a style guide owner
                    next.PerformClick();       // to AI
                    Pump(null, 100);
                    Assert.Contains("Step 2 of 4", All(f).OfType<Label>().Select(l => l.Text));
                    All(f).OfType<Button>().Single(b => b.Text == "Skip this step").PerformClick();   // to the guide step
                    Pump(null, 100);
                    var visible = All(f).OfType<Label>().Where(l => l.Visible && l.Parent.Visible).Select(l => l.Text).ToList();
                    Assert.Contains("Where is your team's style guide?", visible);
                    Assert.True(All(f).OfType<RadioButton>().Single(r => r.Text == "My team's shared style guide folder").Checked);
                }
            });
        }

        [Theory]
        [InlineData(150, 640)]
        [InlineData(180, 640)]
        [InlineData(180, 500)]
        public void TourHintStrip_FitsANarrowPane_AboveEverythingElse_WithSkipTour(int width, int height)
        {
            Sta(() =>
            {
                var tour = GuidedTour.Start(true);
                GuidedTour.Notify(TourEvent.TopicLinted);   // step 2 of 5
                using (var form = Host(width, height))
                {
                    var panel = Pane("<p>In order to move money, open the Transfers page.</p>");
                    Show(form, panel);
                    var strip = All(panel).Single(c => c.AccessibleName == "Practice");
                    var text = strip.Controls.OfType<Label>().Single();
                    Func<string, Button> button = t => strip.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<Button>().Single(b => b.Text == t);
                    Assert.True(strip.Visible);
                    Assert.Equal("Step 2 of 5. Select an issue and click Change.", text.Text);
                    Assert.True(button("Skip Tour").Visible);
                    Assert.False(button("Next").Visible);                  // House Style sees this step happen
                    Assert.False(button("Done").Visible);
                    CheckStrip(strip, text, panel, width + "x" + height + " step 2");
                    var status = All(panel).OfType<Label>().First(l => l.Dock == DockStyle.Top && !(l is LinkLabel) && l.Parent == panel);
                    Assert.True(strip.Bottom <= status.Top, "the strip overlaps the status line");

                    // The Review tab step: House Style can't see it, so the strip offers Next.
                    GuidedTour.Notify(TourEvent.Changed); GuidedTour.Notify(TourEvent.Undone); GuidedTour.Notify(TourEvent.RuleDisabled);
                    Pump(null, 100);
                    Assert.Equal(TourStep.ReviewTab, tour.Step);
                    Assert.StartsWith("Step 5 of 5.", text.Text);
                    Assert.True(button("Next").Visible);
                    Assert.True(button("Skip Tour").Visible);
                    CheckStrip(strip, text, panel, width + "x" + height + " step 5");

                    // Next, clicked as the writer clicks it: the finish line with Done, and it stays.
                    button("Next").PerformClick();
                    Pump(null, 300);
                    Assert.Equal(TourStep.Finished, tour.Step);
                    Assert.Same(tour, GuidedTour.Current);
                    Assert.True(strip.Visible, "the strip should stay for the finish line");
                    Assert.Equal("You're ready. Lint Project lints every topic.", text.Text);
                    Assert.True(button("Done").Visible);
                    Assert.False(button("Next").Visible);
                    Assert.False(button("Skip Tour").Visible);
                    CheckStrip(strip, text, panel, width + "x" + height + " finished");

                    // Done ends the tour and hides the strip.
                    button("Done").PerformClick();
                    Pump(null, 300);
                    Assert.Null(GuidedTour.Current);
                    Assert.False(strip.Visible);
                    Assert.True(Onboarding.Load().TourDone);
                    Assert.True(Onboarding.Load().IsDone(ChecklistItem.AcceptOnReviewTab));
                }
            });
        }

        /// <summary>The strip's sentence shows in full and its buttons stay inside the pane.</summary>
        private static void CheckStrip(Control strip, Label text, Control pane, string where)
        {
            where += ": " + strip.Bounds + " in " + pane.ClientSize + " ";
            var at = pane.RectangleToClient(strip.RectangleToScreen(strip.ClientRectangle));
            Assert.True(at.Left >= 0 && at.Right <= pane.ClientSize.Width, where + "runs past the pane's edge");
            var w = text.Width - text.Padding.Horizontal;
            var need = TextRenderer.MeasureText(text.Text, text.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Assert.True(text.Height - text.Padding.Vertical >= need, where + "the sentence is cut off (" + need + " px of text)");
            foreach (var b in All(strip).OfType<Button>().Where(b => b.Visible))
            {
                var r = strip.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
                Assert.True(r.Right <= strip.ClientSize.Width && r.Bottom <= strip.ClientSize.Height, where + "'" + b.Text + "' is cut off");
            }
        }

        [Theory]
        [InlineData(150, 640)]
        [InlineData(260, 800)]
        public void EmptyPane_ShowsTheGettingStartedChecklist_ThatFits_AndHideHidesIt(int width, int height)
        {
            Sta(() =>
            {
                using (var form = Host(width, height))
                {
                    var panel = Pane("<p>Open the Transfers page.</p>");
                    Show(form, panel);
                    var list = All(panel).OfType<ListView>().Single();
                    Assert.Equal(0, list.Items.Count);
                    var checklist = All(panel).OfType<LinkLabel>().Single(l => l.AccessibleName == "Getting started");
                    Assert.True(checklist.Visible, "the empty pane should show the checklist");
                    Assert.StartsWith("Getting started", checklist.Text);
                    Assert.Contains("✓ Lint a topic", checklist.Text);              // this lint ticked it
                    Assert.Contains("○ Change an issue", checklist.Text);
                    Assert.Contains("Try an AI rewrite", checklist.Text);
                    CheckFits(checklist, panel, width + "x" + height);

                    Click(checklist, "hide");
                    Pump(null, 100);
                    Assert.False(checklist.Visible);
                    Assert.True(Onboarding.Load().ChecklistHidden);
                }
            });
        }

        // ---- helpers ---------------------------------------------------------------------------------------------

        private static Form Host(int width, int height)
        {
            return new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false,
                              FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(width, height), Font = SystemFonts.MessageBoxFont };
        }

        private static FindingsPanel Pane(string body)
        {
            var doc = FlareSimDocument.FromXml("<html " + Ns + "><body><h1>Transfers</h1>" + body + "</body></html>");
            var runner = new ActionRunner(new ScriptedLlmClient().When(r => true, r => ScriptedLlmClient.Reply.Text("{\"findings\":[]}")(r)), new PromptBuilder(null), 1024);
            return new FindingsPanel(runner, doc, () => runner, p => null, null, true);
        }

        private static void Show(Form form, FindingsPanel panel)
        {
            panel.Dock = DockStyle.Fill;
            form.Controls.Add(panel);
            form.Show();
            panel.Begin();
            Pump(() => All(panel).OfType<ProgressBar>().All(p => !p.Visible) && !All(panel).OfType<Label>().Any(l => l.Text == "Linting…"), 15000);
            Pump(null, 200);
        }

        /// <summary>The label shows all of its wrapped text inside the pane: as tall as its text, never wider than the pane.</summary>
        private static void CheckFits(LinkLabel label, Control pane, string where)
        {
            where += ": " + label.Bounds + " in " + pane.ClientSize + " ";
            var screen = pane.RectangleToClient(label.RectangleToScreen(label.ClientRectangle));
            Assert.True(screen.Left >= 0 && screen.Right <= pane.ClientSize.Width, where + "runs past the pane's edge");
            var w = label.Width - label.Padding.Horizontal;
            var text = TextRenderer.MeasureText(label.Text, label.Font, new Size(w, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Assert.True(label.Height - label.Padding.Vertical >= text, where + "is cut off (" + text + " px of text)");
        }

        /// <summary>Clicks the link whose data is <paramref name="data"/>, as a mouse or keyboard user would.</summary>
        private static void Click(LinkLabel label, string data)
        {
            var link = label.Links.Cast<LinkLabel.Link>().Single(l => (l.LinkData as string) == data);
            typeof(LinkLabel).GetMethod("OnLinkClicked", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(label, new object[] { new LinkLabelLinkClickedEventArgs(link) });
        }

        private static IEnumerable<Control> All(Control c)
        {
            foreach (Control k in c.Controls) { yield return k; foreach (var d in All(k)) yield return d; }
        }

        private static void Pump(Func<bool> done, int ms = 15000)
        {
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                if (done != null && done()) { Application.DoEvents(); return; }
                Thread.Sleep(10);
            }
            if (done != null) throw new TimeoutException("The pane didn't finish linting.");
        }

        private static void Sta(Action body)
        {
            Exception error = null;
            var t = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(60000)) throw new TimeoutException("The UI test didn't finish in 60 s.");
            if (error != null) throw new Exception(error.Message, error);
        }
    }
}
