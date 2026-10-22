using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Style
{
    /// <summary>One form of a term and how often the content uses it.</summary>
    public sealed class TermForm
    {
        public string Text { get; set; }
        public int Count { get; set; }
        /// <summary>The plural counted with this form ("members" for "member"), or null.</summary>
        public string Plural { get; set; }
    }

    /// <summary>A sentence from the content (or the author's notes) that shows a candidate rule.</summary>
    public sealed class CandidateExample { public string Text { get; set; } public string File { get; set; } public string Form { get; set; } }

    /// <summary>
    /// A proposed style-guide rule: mined from the project's content (spelling variants, competing words,
    /// capitalization, heading case) or extracted by the AI from notes or an old guide. Becomes a table row
    /// (Avoid/Use/Severity/Why/Example, enforced by the linter) or a bullet with Bad/Good (applied by the AI).
    /// </summary>
    public sealed class RuleCandidate
    {
        public string Id { get; set; }
        /// <summary>spelling, synonym, capitalization, heading, or notes.</summary>
        public string Kind { get; set; }
        public string Title { get; set; }
        public List<TermForm> Forms { get; set; } = new List<TermForm>();
        public List<CandidateExample> Examples { get; set; } = new List<CandidateExample>();
        /// <summary>The preferred form (the most used, until the owner picks another).</summary>
        public string Prefer { get; set; }
        // As a table row
        public string Avoid { get; set; }
        public string Use { get; set; }
        public string Severity { get; set; } = "warning";
        public string Why { get; set; }
        public string Example { get; set; }
        // As a bullet
        public string Text { get; set; }
        public string Bad { get; set; }
        public string Good { get; set; }
        /// <summary>Where a notes-extracted rule came from (quoted from the notes).</summary>
        public string Quote { get; set; }
        public int Impact { get; set; }

        public bool IsRow { get { return !string.IsNullOrEmpty(Avoid); } }

        /// <summary>The owner picked another preferred form: avoid the others, and use an example that breaks the new rule.</summary>
        public void Choose(string form)
        {
            if (Forms.Count == 0 || form == null) return;
            Prefer = form;
            var chosen = Forms.FirstOrDefault(f => f.Text == form);
            var avoid = new List<string>();
            var use = new List<string>();
            foreach (var o in Forms.Where(f => f.Text != form))
            {
                avoid.Add(o.Text); use.Add(form);
                if (o.Plural != null) { avoid.Add(o.Plural); use.Add(chosen != null && chosen.Plural != null ? chosen.Plural : form); }
            }
            var others = Forms.Where(f => f.Text != form).Select(f => f.Text).ToList();
            Avoid = string.Join(", ", avoid.ToArray());
            Use = string.Join(", ", use.ToArray());
            var ex = Examples.FirstOrDefault(e => e.Form != form && others.Contains(e.Form));
            Example = ex == null ? "" : ex.Text;
        }
    }

    public static class GuideBuilder
    {
        /// <summary>Candidates from the docstyle server's mine_style result.</summary>
        public static List<RuleCandidate> ParseMined(JObject result)
        {
            var list = new List<RuleCandidate>();
            foreach (var c in result["candidates"] ?? new JArray())
            {
                var rc = new RuleCandidate
                {
                    Id = (string)c["id"], Kind = (string)c["kind"], Title = (string)c["title"], Prefer = (string)c["prefer"], Impact = (int?)c["impact"] ?? 0
                };
                foreach (var f in c["forms"] ?? new JArray()) rc.Forms.Add(new TermForm { Text = (string)f["text"], Count = (int?)f["count"] ?? 0, Plural = (string)f["plural"] });
                foreach (var e in c["examples"] ?? new JArray()) rc.Examples.Add(new CandidateExample { Text = (string)e["text"], File = (string)e["file"], Form = (string)e["form"] });
                var row = c["row"] as JObject;
                if (row != null) { rc.Avoid = (string)row["avoid"]; rc.Use = (string)row["use"]; rc.Severity = (string)row["severity"] ?? "warning"; rc.Why = (string)row["why"]; rc.Example = (string)row["example"]; }
                var bullet = c["bullet"] as JObject;
                if (bullet != null) { rc.Text = (string)bullet["text"]; rc.Bad = (string)bullet["bad"]; rc.Good = (string)bullet["good"]; }
                list.Add(rc);
            }
            return list;
        }

        /// <summary>Notes split into paragraph-sized pieces (blank lines; small paragraphs merged), each read on its own.</summary>
        public static List<string> Chunks(string notes, int min = 400, int max = 3000)
        {
            var chunks = new List<string>();
            var cur = new StringBuilder();
            foreach (var para in System.Text.RegularExpressions.Regex.Split((notes ?? "").Replace("\r\n", "\n"), @"\n\s*\n"))
            {
                var p = para.Trim();
                if (p.Length == 0) continue;
                if (cur.Length > 0 && (cur.Length >= min || cur.Length + p.Length > max)) { chunks.Add(cur.ToString()); cur.Clear(); }
                if (cur.Length > 0) cur.Append("\n\n");
                cur.Append(p);
            }
            if (cur.Length > 0) chunks.Add(cur.ToString());
            return chunks;
        }

        /// <summary>Candidates from the extract contract's rules (or a bare JSON array of them, from older answers).</summary>
        public static List<RuleCandidate> ParseExtracted(string modelText)
        {
            var s = (modelText ?? "").Trim();
            if (s.StartsWith("{")) return FromExtracted((JArray)(JObject.Parse(s)["rules"] ?? new JArray()));
            int start = s.IndexOf('['), end = s.LastIndexOf(']');
            if (start < 0 || end <= start) throw new FormatException("Model did not return a JSON array.");
            return FromExtracted(JArray.Parse(s.Substring(start, end - start + 1)));
        }

        public static List<RuleCandidate> FromExtracted(JArray rules)
        {
            var list = new List<RuleCandidate>();
            var n = 0;
            foreach (var c in rules)
            {
                var avoid = ((string)c["kind"] == "prose" ? "" : ((string)c["avoid"] ?? "")).Trim();
                var rc = new RuleCandidate
                {
                    Id = "notes:" + (++n), Kind = "notes", Title = (string)c["title"] ?? (string)c["text"] ?? avoid,
                    Avoid = avoid.Length > 0 ? avoid : null, Use = (string)c["use"] ?? "", Why = (string)c["why"], Example = (string)c["example"],
                    Text = Empty((string)c["text"]), Bad = Empty((string)c["bad"]), Good = Empty((string)c["good"]), Quote = (string)c["quote"]
                };
                var sev = Severity.Normalize((string)c["severity"] ?? "warning");
                rc.Severity = sev == "error" || sev == "suggestion" ? sev : "warning";
                if (rc.IsRow || !string.IsNullOrEmpty(rc.Text)) list.Add(rc);
            }
            return list;
        }

        private static string Empty(string s) { return string.IsNullOrEmpty(s) ? null : s; }

        /// <summary>The topic Build style guide adds rules to.</summary>
        public const string ContentTopic = "From-our-content.htm";

        /// <summary>
        /// Adds accepted candidates to the guide rule changes go to (the team's, or the writer's own, created on first use;
        /// never the read-only guide in the plugin folder): <see cref="ContentTopic"/> in its StyleGuide folder.
        /// Returns the topic written; <paramref name="added"/> is how many rules were added.
        /// </summary>
        public static string AddToGuide(FlareAI.Core.Actions.Team team, IList<RuleCandidate> accepted, out int added)
        {
            if (team == null) throw new ArgumentNullException("team");
            var root = team.WritableGuide();
            var folder = Directory.GetFiles(root, "*.flprj").Length > 0 ? Path.Combine(root, @"Content\Resources\StyleGuide") : root;
            var topic = Path.Combine(folder, ContentTopic);
            added = Write(topic, accepted, "From our content");
            return topic;
        }

        /// <summary>
        /// Writes accepted candidates into the guide: rows go into the Avoid/Use table and bullets into the list of the
        /// topic <paramref name="topicPath"/> (created with both when missing). Returns how many were added.
        /// </summary>
        public static int Write(string topicPath, IList<RuleCandidate> accepted, string title)
        {
            var doc = new XmlDocument { PreserveWhitespace = false };
            if (File.Exists(topicPath)) doc.Load(topicPath);
            else doc.LoadXml("<?xml version=\"1.0\" encoding=\"utf-8\"?><html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\"><head><title>" +
                             Esc(title) + "</title></head><body><h1>" + Esc(title) + "</h1></body></html>");
            var body = (XmlElement)doc.SelectSingleNode("//*[local-name()='body']");
            var ns = body.NamespaceURI;
            XmlElement table = null, list = null;
            foreach (XmlElement t in body.SelectNodes(".//*[local-name()='table']"))
            {
                var head = t.SelectSingleNode(".//*[local-name()='tr']");
                if (head != null && head.InnerText.Replace(" ", "").StartsWith("AvoidUse", StringComparison.OrdinalIgnoreCase)) { table = t; break; }
            }
            foreach (XmlElement u in body.SelectNodes("./*[local-name()='ul']")) list = u;

            var added = 0;
            foreach (var c in accepted)
            {
                if (c.IsRow)
                {
                    if (table == null)
                    {
                        body.AppendChild(El(doc, ns, "h2", "Terms"));
                        table = El(doc, ns, "table", null);
                        var tr = El(doc, ns, "tr", null);
                        foreach (var h in new[] { "Avoid", "Use", "Severity", "Why", "Example" }) tr.AppendChild(El(doc, ns, "th", h));
                        table.AppendChild(tr);
                        body.AppendChild(table);
                    }
                    var row = El(doc, ns, "tr", null);
                    foreach (var cell in new[] { c.Avoid, c.Use, c.Severity, c.Why, c.Example }) row.AppendChild(El(doc, ns, "td", cell ?? ""));
                    table.AppendChild(row);
                }
                else
                {
                    if (list == null)
                    {
                        body.AppendChild(El(doc, ns, "h2", "Conventions"));
                        list = El(doc, ns, "ul", null);
                        body.AppendChild(list);
                    }
                    var li = El(doc, ns, "li", c.Text);
                    if (!string.IsNullOrEmpty(c.Bad) || !string.IsNullOrEmpty(c.Good))
                    {
                        var sub = El(doc, ns, "ul", null);
                        if (!string.IsNullOrEmpty(c.Bad)) sub.AppendChild(El(doc, ns, "li", "Bad: " + c.Bad));
                        if (!string.IsNullOrEmpty(c.Good)) sub.AppendChild(El(doc, ns, "li", "Good: " + c.Good));
                        li.AppendChild(sub);
                    }
                    list.AppendChild(li);
                }
                added++;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(topicPath));
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (var w = XmlWriter.Create(topicPath, settings)) doc.Save(w);
            return added;
        }

        private static XmlElement El(XmlDocument doc, string ns, string name, string text)
        {
            var e = doc.CreateElement(name, ns);
            if (text != null) e.InnerText = text;
            return e;
        }

        private static string Esc(string s) { return System.Security.SecurityElement.Escape(s ?? ""); }
    }
}
