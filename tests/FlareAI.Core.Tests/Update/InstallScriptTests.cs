using System;
using System.Diagnostics;
using System.IO;
using FlareAI.Core.Update;
using Xunit;

namespace FlareAI.Core.Tests.Update
{
    /// <summary>
    /// scripts\release\Install.ps1 under Windows PowerShell 5.1 (what testers have): it parses, and the per-user install
    /// runs end to end against a fake Flare (FLARE_ROOT) and a fake %LOCALAPPDATA%, so no admin rights are needed.
    /// </summary>
    public class InstallScriptTests
    {
        private static string Script { get { return Path.Combine(WhatsNewTests.RepoRoot(), "scripts", "release", "Install.ps1"); } }

        private static string PowerShell51
        {
            get
            {
                var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
                return File.Exists(p) ? p : null;
            }
        }

        [Fact]
        public void ParsesInWindowsPowerShell51()
        {
            if (PowerShell51 == null) return;
            var r = Run("-NoProfile -Command \"$e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile('" + Script + "', [ref]$null, [ref]$e); " +
                        "if ($e.Count) { $e | ForEach-Object { $_.ToString() }; exit 1 }; $PSVersionTable.PSVersion.Major\"", null);
            Assert.True(r.Code == 0, r.Output);
            Assert.Equal("5", r.Output.Trim());
        }

        [Fact]
        public void PerUserInstall_LinksFlare_ReinstallKeepsData_AndMigratesACopyInstall()
        {
            if (PowerShell51 == null) return;
            using (var t = new TempDir())
            {
                // A fake Flare 99 outside Program Files (so no elevation), with an old copy install holding an IT policy.
                var flare = t["MadCap Flare 99"];
                Directory.CreateDirectory(Path.Combine(flare, "Flare.app"));
                File.WriteAllText(Path.Combine(flare, "Flare.app", "Flare.exe"), "");
                var plugins = Path.Combine(flare, @"Flare.app\Plugins\FlareAI");
                FakePlugin.Write(plugins, "0.7.0");
                File.WriteAllText(Path.Combine(plugins, UpdateLayout.PolicyFileName), "{\"autoUpdate\": false}");

                var kit = t["unzipped"];
                FakePlugin.Write(Path.Combine(kit, "FlareAI"), "0.8.0");
                File.Copy(Script, Path.Combine(kit, "Install.ps1"));
                var local = t["Local"];
                Directory.CreateDirectory(local);

                var first = Install(kit, flare, local);
                Assert.True(first.Code == 0, first.Output);
                var layout = new UpdateLayout(Path.Combine(local, "HouseStyle"));
                Assert.True(Junction.IsLink(plugins), "Plugins\\FlareAI should be a link now");
                Assert.True(Junction.IsLink(layout.Current));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(plugins));
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), UpdateLayout.PolicyFileName)), "IT policy carried over");
                string why;
                Assert.True(layout.Manages(plugins, out why), why);

                // Something only the writer has (a previous version kept for rollback) must survive a re-install.
                FakePlugin.Write(layout.VersionDir("0.7.5"), "0.7.5");
                var second = Install(kit, flare, local);
                Assert.True(second.Code == 0, second.Output);
                Assert.True(Junction.IsLink(plugins));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(plugins));
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.7.5"), "FlareAI.Flare.dll")), "re-install deleted files behind a link");
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), "StyleGuide", "rules.txt")));
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), UpdateLayout.PolicyFileName)));

                // -NoJunction over the per-user install: unlinks, never empties the per-user folder.
                var copy = Install(kit, flare, local, "-NoJunction");
                Assert.True(copy.Code == 0, copy.Output);
                Assert.False(Junction.IsLink(plugins));
                Assert.Equal("0.8.0", UpdateLayout.VersionIn(plugins));
                Assert.True(File.Exists(Path.Combine(layout.VersionDir("0.8.0"), "FlareAI.Flare.dll")));
                Assert.False(layout.Manages(plugins, out why));
            }
        }

        private static Result Install(string kit, string flare, string local, string extra = "")
        {
            return Run("-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(kit, "Install.ps1") + "\" -Silent -Flare 99 " + extra,
                       psi => { psi.EnvironmentVariables["FLARE_ROOT"] = flare; psi.EnvironmentVariables["LOCALAPPDATA"] = local; psi.EnvironmentVariables["HOUSESTYLE_INSTALL_TEST"] = "1"; });
        }

        private struct Result { public int Code; public string Output; }

        private static Result Run(string args, Action<ProcessStartInfo> env)
        {
            var psi = new ProcessStartInfo(PowerShell51, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (env != null) env(psi);
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(120000);
                return new Result { Code = p.ExitCode, Output = output + err.Result };
            }
        }
    }
}
