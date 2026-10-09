using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Xml;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// Writes a rule change into the team's style guide: a row of a Rule / Setting / Reason table in the guide's own
    /// (non-Google) topics. Owners pick the rule from a finding; they never type its id.
    /// </summary>
    public static class GuideOverrides
    {
        public const string DefaultTopic = "Google-overrides.htm";

        /// <summary>Sets <paramref name="ruleId"/> to off / suggestion / warning / error for the whole team. Returns the topic written.</summary>
        public static string Set(string guideRoot, string ruleId, string setting, string reason)
        {
            if (string.IsNullOrEmpty(ruleId)) throw new ArgumentException("This finding has no rule id to change.");
            var folder = Path.Combine(guideRoot, @"Content\Resources\StyleGuide");
            var google = Path.Combine(folder, "Google") + Path.DirectorySeparatorChar;
            // The first topic with a Rule/Setting table (the starter's "Changes to the Google guide"), else a new one.
            foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.htm", SearchOption.AllDirectories).Where(f => !f.StartsWith(google, StringComparison.OrdinalIgnoreCase)).OrderBy(f => f) : Enumerable.Empty<string>())
            {
                var doc = new XmlDocument { PreserveWhitespace = true };
                try { doc.Load(file); } catch (XmlException) { continue; }
                var table = doc.SelectNodes("//*[local-name()='table']").Cast<XmlElement>().FirstOrDefault(IsOverrideTable);
                if (table == null) continue;
                Upsert(table, ruleId, setting, reason);
                GuideLayer.Save(doc, file);
                return file;
            }
            var company = Path.Combine(folder, "Company");
            Directory.CreateDirectory(company);
            var path = Path.Combine(company, DefaultTopic);
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<html xmlns:MadCap=\"http://www.madcapsoftware.com/Schemas/MadCap.xsd\">\n    <head><title>Changes to the Google guide</title></head>\n    <body>\n" +
                "        <h1>Changes to the Google guide</h1>\n        <p>Rows in this table turn rules off, or change their level, for the whole team.</p>\n" +
                "        <table>\n            <tr><th>Rule</th><th>Setting</th><th>Reason</th></tr>\n" + Row(ruleId, setting, reason) + "        </table>\n    </body>\n</html>\n", new System.Text.UTF8Encoding(false));
            return path;
        }

        private static bool IsOverrideTable(XmlElement table)
        {
            var first = table.SelectSingleNode(".//*[local-name()='tr']");
            if (first == null) return false;
            var head = first.ChildNodes.Cast<XmlNode>().Where(c => c.LocalName == "th" || c.LocalName == "td").Select(c => c.InnerText.Trim().ToLowerInvariant()).ToList();
            return head.Any(h => h.StartsWith("rule")) && head.Any(h => h.StartsWith("setting") || h.StartsWith("level") || h.StartsWith("status"));
        }

        private static void Upsert(XmlElement table, string ruleId, string setting, string reason)
        {
            var rows = table.SelectNodes(".//*[local-name()='tr']").Cast<XmlElement>().ToList();
            var existing = rows.Skip(1).FirstOrDefault(r => r.FirstChild != null && string.Equals(r.ChildNodes.Cast<XmlNode>().First(c => c.NodeType == XmlNodeType.Element).InnerText.Trim(), ruleId, StringComparison.OrdinalIgnoreCase));
            var frag = table.OwnerDocument.CreateDocumentFragment();
            frag.InnerXml = Row(ruleId, setting, reason).Trim();
            var row = (XmlElement)frag.FirstChild;
            if (row.NamespaceURI != table.NamespaceURI)   // match the topic's namespace (plain XHTML or the default xmlns)
            {
                var x = new XmlDocument(); x.LoadXml("<t xmlns=\"" + table.NamespaceURI + "\">" + Row(ruleId, setting, reason).Trim() + "</t>");
                row = (XmlElement)table.OwnerDocument.ImportNode(x.DocumentElement.FirstChild, true);
            }
            var parent = rows.Count > 0 ? rows[rows.Count - 1].ParentNode : table;
            if (existing != null) existing.ParentNode.ReplaceChild(row, existing);
            else
            {
                parent.AppendChild(table.OwnerDocument.CreateTextNode("\n            "));
                parent.AppendChild(row);
                parent.AppendChild(table.OwnerDocument.CreateTextNode("\n        "));
            }
        }

        private static string Row(string ruleId, string setting, string reason)
        {
            return "            <tr><td>" + SecurityElement.Escape(ruleId) + "</td><td>" + SecurityElement.Escape(setting) + "</td><td>" + SecurityElement.Escape(reason ?? "") + "</td></tr>\n";
        }
    }
}
