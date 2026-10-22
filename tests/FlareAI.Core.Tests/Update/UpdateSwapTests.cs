using System;
using System.IO;
using FlareAI.Core.Update;
using Xunit;

namespace FlareAI.Core.Tests.Update
{
    /// <summary>The updater's swap on a temp folder, with real junctions (mklink /J needs no admin rights).</summary>
    public class UpdateSwapTests
    {
        /// <summary>HouseStyle\versions\0.8.0, current -> it, and a "Plugins\FlareAI" junction -> current (as Install.cmd makes).</summary>
        private static UpdateLayout Installed(TempDir t, string version = "0.8.0")
        {
            var layout = new UpdateLayout(t["HouseStyle"]);
            FakePlugin.Write(layout.VersionDir(version), version);
            Junction.Create(layout.Current, layout.VersionDir(version));
            Directory.CreateDirectory(t["Flare.app\\Plugins"]);
            Junction.Create(t["Flare.app\\Plugins\\FlareAI"], layout.Current);
            return layout;
        }

        private static void StageReady(UpdateLayout layout, string version)
        {
            FakePlugin.Write(Path.Combine(layout.StagedDir(version), "FlareAI"), version);
            File.WriteAllText(Path.Combine(layout.StagedDir(version), ".verified"), "x");
        }

        [Fact]
        public void Apply_SwapsCurrent_KeepsThePreviousVersion_AndWritesTheResult()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                File.WriteAllText(Path.Combine(layout.Current, UpdateLayout.PolicyFileName), "{\"autoUpdate\": true}");
                StageReady(layout, "0.9.0");

                var r = new UpdateSwap(layout).Apply("0.9.0");

                Assert.True(r.Ok, r.Error);
                Assert.Equal("0.8.0", r.From);
                Assert.Equal("0.9.0", r.To);
                Assert.True(Junction.IsLink(layout.Current));
                Assert.Equal("0.9.0", UpdateLayout.VersionIn(layout.Current));
                Assert.Equal("0.9.0", UpdateLayout.VersionIn(t["Flare.app\\Plugins\\FlareAI"]));   // Flare sees it through its link
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), "FlareAI.Flare.dll")));   // rollback copy kept
                Assert.True(File.Exists(Path.Combine(layout.Current, UpdateLayout.PolicyFileName)));      // IT policy carried over
                Assert.False(Directory.Exists(layout.StagedDir("0.9.0")));
                Assert.False(Directory.Exists(layout.Current + ".old"));
                Assert.False(Directory.Exists(layout.Current + ".new"));
                var saved = UpdateResult.Read(layout.ResultPath);
                Assert.True(saved.Ok);
                Assert.Equal("0.9.0", saved.To);
            }
        }

        [Fact]
        public void Apply_WhenTheNewVersionFailsItsCheck_RollsBack()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                StageReady(layout, "0.9.0");
                var swap = new UpdateSwap(layout) { AfterRepoint = _ => { throw new UpdateException("Simulated failure after the swap."); } };

                var r = swap.Apply("0.9.0");

                Assert.False(r.Ok);
                Assert.Equal("Simulated failure after the swap.", r.Error);
                Assert.True(Junction.IsLink(layout.Current));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(layout.Current));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(t["Flare.app\\Plugins\\FlareAI"]));
                Assert.False(UpdateResult.Read(layout.ResultPath).Ok);
            }
        }

        [Fact]
        public void Apply_WithAPayloadOfTheWrongVersion_RollsBack()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                FakePlugin.Write(Path.Combine(layout.StagedDir("0.9.0"), "FlareAI"), "0.7.0");   // says 0.9.0, holds 0.7.0
                File.WriteAllText(Path.Combine(layout.StagedDir("0.9.0"), ".verified"), "x");
                var r = new UpdateSwap(layout).Apply("0.9.0");
                Assert.False(r.Ok);
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(layout.Current));
            }
        }

        [Fact]
        public void Apply_WithNothingStaged_FailsAndChangesNothing()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                var r = new UpdateSwap(layout).Apply("0.9.0");
                Assert.False(r.Ok);
                Assert.Contains("No downloaded update", r.Error);
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(layout.Current));
            }
        }

        [Fact]
        public void Apply_OnACopyInstall_Refuses()
        {
            using (var t = new TempDir())
            {
                var layout = new UpdateLayout(t["HouseStyle"]);
                FakePlugin.Write(layout.Current, "0.8.0");   // a real folder, not a link
                StageReady(layout, "0.9.0");
                var r = new UpdateSwap(layout).Apply("0.9.0");
                Assert.False(r.Ok);
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(layout.Current));
            }
        }

        [Fact]
        public void Apply_PrunesAllButTheNewAndPreviousVersions()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t, "0.8.0");
                FakePlugin.Write(layout.VersionDir("0.7.0"), "0.7.0");
                StageReady(layout, "0.9.0");
                Assert.True(new UpdateSwap(layout).Apply("0.9.0").Ok);
                Assert.False(Directory.Exists(layout.VersionDir("0.7.0")));
                Assert.True(Directory.Exists(layout.VersionDir("0.8.0")));
                Assert.True(Directory.Exists(layout.VersionDir("0.9.0")));
            }
        }

        [Fact]
        public void Repoint_IsRepeatable_AndJunctionRemovalNeverDeletesTheTarget()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                FakePlugin.Write(layout.VersionDir("0.9.0"), "0.9.0");
                var swap = new UpdateSwap(layout);
                swap.Repoint(layout.VersionDir("0.9.0"));
                swap.Repoint(layout.VersionDir("0.8.0"));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(layout.Current));
                Junction.Remove(layout.Current);
                Assert.False(Directory.Exists(layout.Current));
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), "FlareAI.Flare.dll")));
                Assert.Throws<IOException>(() => Junction.Remove(layout.VersionDir("0.8.0")));   // a real folder is refused
            }
        }

        [Fact]
        public void Manages_OnlyThePerUserLayout()
        {
            using (var t = new TempDir())
            {
                var layout = Installed(t);
                string why;
                Assert.True(layout.Manages(t["Flare.app\\Plugins\\FlareAI"], out why), why);
                Assert.True(layout.Manages(layout.Current, out why), why);

                FakePlugin.Write(t["ProgramFiles\\Plugins\\FlareAI"], "0.8.0");   // -NoJunction copy
                Assert.False(layout.Manages(t["ProgramFiles\\Plugins\\FlareAI"], out why));
                Assert.False(new UpdateLayout(t["Nobody"]).Manages(t["Flare.app\\Plugins\\FlareAI"], out why));
            }
        }
    }
}
