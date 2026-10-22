using System;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FlareAI.GuideCompiler.Tests
{
    /// <summary>
    /// The compiler validation gate (#62): the bundled Google layer, loaded for a guide that covers every product, passes every
    /// mechanical check of the accuracy audit. A recompile that brings a finding class back fails here, with counts per check.
    /// </summary>
    public class GuideValidationTests
    {
        private readonly ITestOutputHelper _out;
        public GuideValidationTests(ITestOutputHelper output) { _out = output; }

        private static string Root
        {
            get
            {
                var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
                if (d == null) throw new InvalidOperationException("FlareAI.sln not found");
                return d.FullName;
            }
        }

        [Fact]
        public void The_bundled_google_layer_passes_every_check()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "guidechecks-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(tmp, @"Project\ConditionTagSets"));
                File.WriteAllText(Path.Combine(tmp, "Team.flprj"), "<CatapultProject />");
                File.WriteAllText(Path.Combine(tmp, @"Project\ConditionTagSets\Product.flcts"),
                    "<CatapultConditionTagSet>" + string.Join("", Program.ProductTags.Select(p => "<ConditionTag Name=\"" + p + "\" />").ToArray()) + "</CatapultConditionTagSet>");
                var comp = new StyleEngine(new[] { tmp }, Path.Combine(Root, "styleguide")).Compose(null);
                var compiled = JObject.Parse(File.ReadAllText(Path.Combine(Root, @"tools\google-style\compiled.json"), Encoding.UTF8));
                var cfg = PassConfig.Load(Path.Combine(Root, @"tools\google-style"));
                var r = GuideChecks.Run(comp, compiled, cfg);
                _out.WriteLine(GuideChecks.Summary(r));
                var failing = r.Where(kv => kv.Value.Count > 0).ToList();
                Assert.True(failing.Count == 0, GuideChecks.Summary(r) + "\n" +
                    string.Join("\n", failing.SelectMany(kv => kv.Value.Take(15).Select(f => "  " + kv.Key + ": " + f)).ToArray()));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        [Fact]
        public void A_row_that_matches_markup_is_reported_and_not_loaded()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "guidemarkup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var dir = Path.Combine(tmp, @"Content\Resources\StyleGuide");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(tmp, "Team.flprj"), "<CatapultProject />");
                File.WriteAllText(Path.Combine(dir, "Rules.htm"),
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><html><body><h1>Rules</h1><table>" +
                    "<tr><th>Pattern</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>" +
                    "<tr><td>&lt;br\\s*/?&gt;\\s*&lt;br\\s*/?&gt;</td><td></td><td>warning</td><td></td><td>One.&lt;br /&gt;&lt;br /&gt;Two.</td></tr>" +
                    "<tr><td>\\bcolspan\\s*=</td><td></td><td>warning</td><td></td><td>&lt;td colspan=\"2\"&gt;</td></tr>" +
                    "<tr><td>&amp;ndash;</td><td>-</td><td>warning</td><td></td><td>8&amp;ndash;20</td></tr>" +
                    "<tr><td>\\butilize\\b</td><td>use</td><td>warning</td><td></td><td>We utilize it.</td></tr>" +
                    "</table></body></html>");
                var layer = GuideLayer.Load(tmp, "team");
                Assert.Single(layer.Rules);
                Assert.Equal(@"\butilize\b", layer.Rules[0].Avoid[0]);
                Assert.Equal(3, layer.Problems.Count(p => p.Message.StartsWith(GuideLayer.MarkupOnly)));
                var engine = new StyleEngine(new[] { tmp }, Path.Combine(Root, "styleguide"));
                var team = engine.Compose(null).Layers[1].Name;
                var report = engine.Validate(null);
                var markup = report.Issues.Where(i => i.Layer == team && i.Message.StartsWith(GuideLayer.MarkupOnly)).ToList();
                Assert.Equal(new[] { team + ":rules/1", team + ":rules/2", team + ":rules/3" }, markup.Select(i => i.Location.Substring(i.Location.IndexOf('(') + 1).TrimEnd(')')).ToArray());
                Assert.DoesNotContain(engine.Compose(null).Rules, r => r.Layer == team && r.Id != team + ":rules/4");
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }
    }
}
