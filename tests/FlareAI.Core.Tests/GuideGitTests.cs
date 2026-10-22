using System;
using System.IO;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>The guide folder is shared, so its git config is someone else's: reading its status must not run their programs.</summary>
    public class GuideGitTests
    {
        [Fact]
        public void ReadStatus_WithFetch_DoesNotRunCommandsFromTheGuidesGitConfig()
        {
            if (Git.Run(null, "--version", 10000) == null) return;   // no git on this machine: nothing to check
            var root = Path.Combine(Path.GetTempPath(), "hs-git-" + Guid.NewGuid().ToString("N"));
            var marker = Path.Combine(root, "ran.txt");
            try
            {
                Directory.CreateDirectory(root);
                Assert.NotNull(Git.Run(root, "init -q", 10000));
                File.WriteAllText(Path.Combine(root, "a.txt"), "a");
                Git.Run(root, "add a.txt", 10000);
                Assert.NotNull(Git.Run(root, "-c user.email=t@example.com -c user.name=t commit -q -m a", 10000));
                // What a hostile teammate could put in a shared .git/config: an ssh remote whose "ssh" is any command.
                var cmd = "cmd /c echo x > \"" + marker.Replace("\\", "/") + "\"";
                Git.Run(root, "config core.sshCommand \"" + cmd.Replace("\"", "\\\"") + "\"", 10000);
                Git.Run(root, "remote add origin ssh://example.invalid/x.git", 10000);

                var status = Team.ReadStatus(root, true);

                Assert.NotNull(status);
                Assert.False(File.Exists(marker));
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }
    }
}
