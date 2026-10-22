using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FlareAI.Core.Update
{
    /// <summary>
    /// The "What's new" notes shipped with the plugin (<c>whats-new.md</c> at the plugin root). A small Markdown subset:
    /// <c>## 0.4.0</c> starts a version's section; inside it <c>###</c> headings, <c>- </c> bullets, paragraphs, and
    /// <c>[text](https://...)</c> links. Entries are written from the writer's side: "You asked … → now …".
    /// </summary>
    public sealed class WhatsNew
    {
        public const string FileName = "whats-new.md";

        public enum Kind { Heading, Bullet, Paragraph }

        /// <summary>A piece of a line: plain text, or a link when <see cref="Url"/> is set.</summary>
        public sealed class Run
        {
            public string Text;
            public string Url;
        }

        public sealed class Block
        {
            public Kind Kind;
            public List<Run> Runs = new List<Run>();
            public string PlainText { get { return string.Concat(Runs.Select(r => r.Text).ToArray()); } }
        }

        public sealed class Section
        {
            public string Version;
            /// <summary>The heading text after the version ("0.4.0 — October 2026" gives "October 2026"); may be empty.</summary>
            public string Title;
            public List<Block> Blocks = new List<Block>();
        }

        public List<Section> Sections = new List<Section>();

        public static WhatsNew Load(string pluginDir)
        {
            try
            {
                var path = Path.Combine(pluginDir ?? "", FileName);
                return File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new WhatsNew();
            }
            catch (Exception ex) { Diagnostics.Log("whats-new", ex); return new WhatsNew(); }
        }

        private static readonly Regex VersionHeading = new Regex(@"^##\s+v?(\d+(?:\.\d+){0,2}(?:-[0-9A-Za-z.\-]+)?)\s*(?:[—–:\-]\s*)?(.*)$");
        private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\(([^)\s]+)\)");

        public static WhatsNew Parse(string markdown)
        {
            var doc = new WhatsNew();
            Section section = null;
            Block para = null;
            foreach (var raw in (markdown ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var t = line.Trim();
                if (t.StartsWith("## ") && !t.StartsWith("### "))
                {
                    var m = VersionHeading.Match(t);
                    section = m.Success ? new Section { Version = m.Groups[1].Value, Title = m.Groups[2].Value.Trim() } : null;
                    if (section != null) doc.Sections.Add(section);
                    para = null;
                    continue;
                }
                if (section == null) continue;   // the file's title and intro
                if (t.Length == 0) { para = null; continue; }
                if (t.StartsWith("#"))
                {
                    section.Blocks.Add(Make(Kind.Heading, t.TrimStart('#').Trim()));
                    para = null;
                }
                else if (t.StartsWith("- ") || t.StartsWith("* "))
                {
                    para = Make(Kind.Bullet, t.Substring(2).Trim());
                    section.Blocks.Add(para);
                }
                else if (para != null && (raw.StartsWith("  ") || para.Kind == Kind.Paragraph))
                {
                    var more = Make(para.Kind, " " + t);   // a wrapped line continues the bullet or paragraph
                    para.Runs.AddRange(more.Runs);
                }
                else
                {
                    para = Make(Kind.Paragraph, t);
                    section.Blocks.Add(para);
                }
            }
            return doc;
        }

        private static Block Make(Kind kind, string text)
        {
            var b = new Block { Kind = kind };
            text = text.Replace("**", "").Replace("`", "");
            int at = 0;
            foreach (Match m in Link.Matches(text))
            {
                if (m.Index > at) b.Runs.Add(new Run { Text = text.Substring(at, m.Index - at) });
                var url = m.Groups[2].Value;
                b.Runs.Add(new Run { Text = m.Groups[1].Value, Url = IsWebLink(url) ? url : null });
                at = m.Index + m.Length;
            }
            if (at < text.Length) b.Runs.Add(new Run { Text = text.Substring(at) });
            return b;
        }

        /// <summary>Only http(s) links open; anything else is shown as text.</summary>
        public static bool IsWebLink(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);
        }

        /// <summary>
        /// The sections to show the writer, newest first: everything after <paramref name="seen"/> up to
        /// <paramref name="current"/>; when nothing was seen yet, the current version's section. Empty when up to date.
        /// </summary>
        public List<Section> Unseen(string seen, string current)
        {
            if (!string.IsNullOrEmpty(seen) && SemVer.Compare(seen, current) >= 0) return new List<Section>();
            return Sections.Where(s => SemVer.Compare(s.Version, current) <= 0 &&
                                       (string.IsNullOrEmpty(seen) ? SemVer.Compare(s.Version, current) == 0 : SemVer.Compare(s.Version, seen) > 0))
                           .OrderByDescending(s => SemVer.Parse(s.Version)).ToList();
        }

        /// <summary>Every section up to <paramref name="current"/>, newest first (the What's new command).</summary>
        public List<Section> UpTo(string current)
        {
            return Sections.Where(s => SemVer.Compare(s.Version, current) <= 0 || SemVer.Parse(current) == null)
                           .OrderByDescending(s => SemVer.Parse(s.Version)).ToList();
        }
    }
}
