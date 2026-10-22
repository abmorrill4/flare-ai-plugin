using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler.Tests
{
    /// <summary>A throwaway corpus project folder: topics, optional project.json, labels.</summary>
    internal sealed class TempProject : IDisposable
    {
        public string Root { get; private set; }
        public string Adjudication { get { return Path.Combine(Root, "adjudication"); } }

        public TempProject(string name = "demo")
        {
            Root = Path.Combine(Path.GetTempPath(), "labeler-" + Guid.NewGuid().ToString("N"), name);
            Directory.CreateDirectory(Path.Combine(Root, "Content"));
        }

        public TempProject Topic(string file, string body)
        {
            string path = Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>t</title></head><body>" + body + "</body></html>", new UTF8Encoding(false));
            return this;
        }

        public TempProject Split(params string[] testFiles)
        {
            File.WriteAllText(Path.Combine(Root, "project.json"), new JObject { ["id"] = Path.GetFileName(Root), ["split"] = new JObject { ["test"] = new JArray(testFiles) } }.ToString(), new UTF8Encoding(false));
            return this;
        }

        public CorpusProject Open() { return CorpusProject.Open(Root); }

        public void Dispose()
        {
            try { Directory.Delete(Path.GetDirectoryName(Root), true); } catch (IOException) { }
        }

        private static int _n;

        public string Name { get { return Path.GetFileName(Root); } }

        public Label Label(string kind, string file, string block, string quote, string defect = null, string[] rules = null, string status = "verified", string origin = "seeded", string[] by = null, string split = "dev", string prefix = null, string suffix = null)
        {
            var l = new Label
            {
                Id = Name + "/L" + (++_n).ToString("D4"), File = file, Block = block, Quote = quote, Prefix = prefix, Suffix = suffix, Kind = kind, DefectKind = defect,
                Rules = new List<string>(rules ?? new[] { "lint" }), Origin = origin, Status = status, By = new List<string>(by ?? new[] { "generator" }), Split = split
            };
            if (kind == LabelKind.Clean || kind == LabelKind.Exempt) l.Reason = "test";
            return l;
        }

        public static Taxonomy Taxonomy()
        {
            string path = Path.Combine(Path.GetTempPath(), "labeler-defects-" + Guid.NewGuid().ToString("N") + ".yaml");
            File.WriteAllText(path, string.Join("\n", new[]
            {
                "- id: exclaim", "  group: tone", "  detect: lint", "  status: active", "  summary: Exclamation mark in running text.", "  bad: Your new transfer is ready to go!",
                "- id: utilize", "  group: plain-language", "  detect: lint", "  status: active", "  summary: Needless formal verb.", "  bad: You can utilize the list.",
                "- id: passive", "  group: voice", "  detect: ai", "  status: active", "  summary: Passive voice that hides the actor.", "  bad: Each item is reviewed.",
                "- id: alt_missing", "  group: accessibility", "  detect: structure", "  status: active", "  summary: Image without alt text.", "  bad: img",
                "- id: phone", "  group: format", "  detect: structure", "  status: active", "  summary: Phone number format.", "  bad: 555-1234",
            }) + "\n", new UTF8Encoding(false));
            try { return FlareAI.Labeler.Taxonomy.Load(path); } finally { File.Delete(path); }
        }
    }
}
