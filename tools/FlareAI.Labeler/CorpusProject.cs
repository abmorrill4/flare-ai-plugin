using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    /// <summary>One block with text, as the labelling tools see it.</summary>
    public sealed class CorpusBlock
    {
        public string File, XPath, Text;
        public List<string> Line;
        public BlockText Detail;
        public string Id { get { return File + "|" + XPath; } }
    }

    /// <summary>A corpus project folder: its labels, split and content files. Reads topics with the scoring <see cref="Xhtml"/> reader.</summary>
    public sealed class CorpusProject
    {
        public string Root { get; private set; }
        public string Name { get; private set; }
        public string LabelsPath { get { return Path.Combine(Root, "labels.jsonl"); } }
        /// <summary>Files named in <c>project.json</c> split.test; null when the project does not list them.</summary>
        public HashSet<string> TestFiles { get; private set; }
        public CorpusBlockTextSource Text { get; private set; }

        private readonly Dictionary<string, XmlDocument> _docs = new Dictionary<string, XmlDocument>(StringComparer.OrdinalIgnoreCase);

        private CorpusProject() { }

        public static CorpusProject Open(string root)
        {
            var p = new CorpusProject { Root = root, Name = Path.GetFileName(root.TrimEnd('\\', '/')) };
            p.Text = new CorpusBlockTextSource(root);
            string pj = Path.Combine(root, "project.json");
            if (File.Exists(pj))
            {
                var j = JObject.Parse(File.ReadAllText(pj, Encoding.UTF8));
                string id = (string)j["id"] ?? (string)j["name"];
                if (!string.IsNullOrEmpty(id)) p.Name = id;
                var test = j["split"] == null ? null : j["split"]["test"] as JArray;
                if (test != null) p.TestFiles = new HashSet<string>(test.Select(t => (string)t), StringComparer.Ordinal);
            }
            return p;
        }

        public List<Label> ReadLabels() { return File.Exists(LabelsPath) ? LabelIO.Read(LabelsPath) : new List<Label>(); }

        /// <summary>Topics and snippets under Content/, project-relative with forward slashes. The style guide and stylesheets are not content.</summary>
        public List<string> ContentFiles()
        {
            var list = new List<string>();
            string content = Path.Combine(Root, "Content");
            if (!Directory.Exists(content)) return list;
            foreach (var f in Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".htm" && ext != ".html" && ext != ".flsnp") continue;
                string rel = f.Substring(Root.Length).TrimStart('\\', '/').Replace('\\', '/');
                if (rel.StartsWith("Content/Resources/StyleGuide/", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("Content/Resources/Stylesheets/", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(rel);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        public static bool IsSnippet(string file) { return file.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase); }

        public XmlDocument Doc(string file)
        {
            XmlDocument d;
            if (_docs.TryGetValue(file, out d)) return d;
            string path = Path.Combine(Root, file.Replace('/', Path.DirectorySeparatorChar));
            try { d = File.Exists(path) ? Xhtml.Load(path) : null; }
            catch (XmlException) { d = null; }
            _docs[file] = d;
            return d;
        }

        /// <summary>Blocks with text in a file, in document order; empty when the file does not parse.</summary>
        public List<CorpusBlock> Blocks(string file)
        {
            var list = new List<CorpusBlock>();
            var doc = Doc(file);
            if (doc == null) return list;
            foreach (var b in Xhtml.Blocks(doc, null))
                list.Add(new CorpusBlock { File = file, XPath = b.XPath, Text = b.Text, Line = Xhtml.LineContext(b.Element), Detail = b });
            return list;
        }

        public BlockText Block(string file, string xpath)
        {
            var doc = Doc(file);
            var el = doc == null ? null : Xhtml.Resolve(doc, xpath);
            return el == null ? null : Xhtml.Extract(el, null);
        }

        /// <summary>The first <c>corpus/schema/defects.yaml</c> found above <paramref name="start"/>.</summary>
        public static string FindUp(string start, string relative)
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                string p = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p) || Directory.Exists(p)) return p;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
