using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace FlareAI.CorpusKit
{
    internal sealed class TopicEntry
    {
        [JsonProperty("path")] public string Path;
        [JsonProperty("title")] public string Title;
        [JsonProperty("source")] public string Source;
    }

    /// <summary>What <c>import</c> learned about the project; <c>scaffold</c> reads it. Kept at the project root as corpus-kit.json.</summary>
    internal sealed class Manifest
    {
        public const string FileName = "corpus-kit.json";
        [JsonProperty("name")] public string Name;
        [JsonProperty("topics")] public List<TopicEntry> Topics = new List<TopicEntry>();
        [JsonProperty("variables")] public SortedDictionary<string, string> Variables = new SortedDictionary<string, string>(StringComparer.Ordinal); // "Set.Name" -> value
        [JsonProperty("snippets")] public List<string> Snippets = new List<string>();
        [JsonProperty("altDropped")] public List<string> AltDropped = new List<string>();
        [JsonProperty("warnings")] public List<string> Warnings = new List<string>();

        public static Manifest Load(string projectRoot)
        {
            var p = System.IO.Path.Combine(projectRoot, FileName);
            return File.Exists(p) ? JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(p)) : null;
        }

        public void Save(string projectRoot)
        {
            File.WriteAllText(System.IO.Path.Combine(projectRoot, FileName), JsonConvert.SerializeObject(this, Formatting.Indented) + "\n", new UTF8Encoding(false));
        }
    }

    internal sealed class ImportOptions
    {
        public string Src, Out, Name;
        public List<VarMap> Vars = new List<VarMap>();
        public double AltDropRate;
        public long Seed = 1;
        public int SnippetMin = 3;
        public string UiStyle = "bold";
        public string DefaultSection = "general";
    }

    /// <summary>Markdown or HTML files to Flare topics, snippets and variables.</summary>
    internal static class Importer
    {
        private static readonly string[] MdExt = { ".md", ".markdown" };
        private static readonly string[] HtmlExt = { ".html", ".htm" };

        public static Manifest Run(ImportOptions o)
        {
            var srcRoot = Path.GetFullPath(o.Src);
            if (!Directory.Exists(srcRoot)) throw new UsageException("Source folder not found: " + o.Src);
            var outRoot = Path.GetFullPath(o.Out);
            Directory.CreateDirectory(outRoot);

            var files = Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => IsDoc(f) && !Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("_index", StringComparison.Ordinal) && IsDoc(f))
                .Select(f => new { Full = f, Rel = Fx.Rel(srcRoot, f) })
                .OrderBy(f => f.Rel, StringComparer.Ordinal).ToList();
            if (files.Count == 0) throw new UsageException("No .md or .html files under " + o.Src);

            // 1. topic paths and link map
            var linkMap = new Dictionary<string, string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var topicRel = new Dictionary<string, string>();
            foreach (var f in files)
            {
                var dir = Fx.Norm(Path.GetDirectoryName(f.Rel) ?? "");
                var section = dir.Length == 0 ? o.DefaultSection : string.Join("/", dir.Split('/').Select(Fx.Slug).Where(s => s.Length > 0));
                if (section.Length == 0) section = o.DefaultSection;
                var stem = Path.GetFileNameWithoutExtension(f.Rel);
                var slug = Fx.Slug(stem);
                if (slug == "index" || slug == "readme" || slug == "") slug = "about";
                var rel = "Content/" + section + "/" + slug + ".htm";
                int k = 2;
                while (!used.Add(rel)) rel = "Content/" + section + "/" + slug + "-" + (k++) + ".htm";
                topicRel[f.Rel] = rel;
                linkMap[f.Rel.ToLowerInvariant()] = rel;
            }

            // 2. convert
            var manifest = new Manifest { Name = o.Name ?? Path.GetFileName(outRoot) };
            var images = new Dictionary<string, string>();
            var docs = new List<KeyValuePair<TopicEntry, XDocument>>();
            foreach (var f in files)
            {
                var text = File.ReadAllText(f.Full);
                string title = null, html;
                bool md = MdExt.Contains(Path.GetExtension(f.Full).ToLowerInvariant());
                if (md)
                {
                    var body = Markdown.StripFrontMatter(text, out title);
                    html = Markdown.ToHtml(text);
                }
                else html = text;
                var root = LenientHtml.Parse(html);
                title = title ?? FlareConverter.TitleOf(root) ?? Humanize(Path.GetFileNameWithoutExtension(f.Rel));
                var cx = new ConvertContext
                {
                    SourceRel = f.Rel, SourceFull = f.Full, TopicRel = topicRel[f.Rel], LinkMap = linkMap, ImageMap = images,
                    Warnings = manifest.Warnings, AltDropped = manifest.AltDropped, UiStyle = o.UiStyle, AltDropRate = o.AltDropRate, Seed = o.Seed
                };
                var nodes = FlareConverter.Convert(root, cx);
                var bodyEl = new XElement("body", nodes);
                if (bodyEl.Element("h1") == null) bodyEl.AddFirst(new XElement("h1", title));
                FlareConverter.ApplyVariables(bodyEl, o.Vars);
                var css = Fx.RelativeTo(Path.GetDirectoryName(cx.TopicRel) ?? "", "Content/Resources/Stylesheets/Default.css");
                var doc = Fx.NewTopic(title, css, bodyEl.Nodes().ToList());
                var entry = new TopicEntry { Path = cx.TopicRel, Title = title, Source = f.Rel };
                docs.Add(new KeyValuePair<TopicEntry, XDocument>(entry, doc));
                manifest.Topics.Add(entry);
            }
            foreach (var v in o.Vars) manifest.Variables[v.Name] = v.Value;

            // 3. repeated blocks to snippets
            ExtractSnippets(docs, o.SnippetMin, outRoot, manifest);

            // 4. write
            foreach (var d in docs) Fx.Save(d.Value, Path.Combine(outRoot, d.Key.Path.Replace('/', Path.DirectorySeparatorChar)));
            foreach (var kv in images)
            {
                var dst = Path.Combine(outRoot, kv.Value.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(kv.Key, dst, true);
            }
            manifest.Save(outRoot);
            return manifest;
        }

        private static bool IsDoc(string f)
        {
            var e = Path.GetExtension(f).ToLowerInvariant();
            return MdExt.Contains(e) || HtmlExt.Contains(e);
        }

        private static string Humanize(string stem)
        {
            var s = Regex.Replace(stem, @"[-_]+", " ").Trim();
            return s.Length == 0 ? "Untitled" : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        private static readonly HashSet<string> SnippetBlocks = new HashSet<string>(StringComparer.Ordinal) { "p", "ul", "ol", "table", "pre", "blockquote" };

        private static void ExtractSnippets(List<KeyValuePair<TopicEntry, XDocument>> docs, int min, string outRoot, Manifest manifest)
        {
            if (min < 2) return;
            var byKey = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
            var docOf = new Dictionary<XElement, int>();
            for (int i = 0; i < docs.Count; i++)
            {
                foreach (var e in Fx.Body(docs[i].Value).Elements().Where(e => SnippetBlocks.Contains(e.Name.LocalName)))
                {
                    if (Regex.Replace(e.Value, @"\s+", " ").Trim().Length < 50) continue;
                    var key = e.ToString(SaveOptions.DisableFormatting);
                    List<XElement> l;
                    if (!byKey.TryGetValue(key, out l)) byKey[key] = l = new List<XElement>();
                    l.Add(e); docOf[e] = i;
                }
            }
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in byKey.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value.Select(e => docOf[e]).Distinct().Count() < min) continue;
                var first = kv.Value[0];
                var words = Regex.Matches(first.Value, @"[A-Za-z0-9]+").Cast<Match>().Take(4).Select(m => m.Value);
                var slug = Fx.Slug(string.Join(" ", words));
                if (slug.Length == 0) slug = "snippet";
                var name = slug; int k = 2;
                while (!names.Add(name)) name = slug + "-" + (k++);
                var snipRel = "Content/Resources/Snippets/" + name + ".flsnp";
                var snip = new XDocument(new XDeclaration("1.0", "utf-8", null),
                    new XElement("html", new XAttribute(XNamespace.Xmlns + "MadCap", Fx.MadCap.NamespaceName), new XElement("head"), new XElement("body", new XElement(first))));
                Fx.Save(snip, Path.Combine(outRoot, snipRel.Replace('/', Path.DirectorySeparatorChar)));
                manifest.Snippets.Add(snipRel);
                foreach (var e in kv.Value)
                {
                    var topic = docs[docOf[e]].Key.Path;
                    e.ReplaceWith(new XElement(Fx.MadCap + "snippetBlock", new XAttribute("src", Fx.RelativeTo(Path.GetDirectoryName(topic) ?? "", snipRel))));
                }
            }
        }
    }
}
