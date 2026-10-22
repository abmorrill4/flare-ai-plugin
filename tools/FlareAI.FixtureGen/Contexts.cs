using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace FlareAI.FixtureGen
{
    /// <summary>The context templates (templates/*.htm): a case text placed in a paragraph, a step, a UI label, a link, a cell, ...</summary>
    public sealed class ContextSet
    {
        public const string MadCapNs = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";
        private readonly Dictionary<string, string> _templates = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> Names { get; private set; }

        /// <summary>Resolves what a MadCap:variable shows, so a case next to a variable reads like the real line.</summary>
        public static string VariableValue(string name) { return "Acme"; }

        public static ContextSet Load(string templateDir)
        {
            var set = new ContextSet { Names = new List<string>() };
            foreach (var f in Directory.GetFiles(templateDir, "*.htm").OrderBy(x => x, StringComparer.Ordinal))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                set._templates[name] = File.ReadAllText(f).Trim();
                set.Names.Add(name);
            }
            return set;
        }

        public bool Has(string context) { return _templates.ContainsKey(context); }

        /// <summary>The body-level blocks of a topic holding <paramref name="text"/> in <paramref name="context"/>.</summary>
        public List<XmlNode> Render(string context, string text)
        {
            return Body(_templates[context].Replace("{{text}}", Escape(text)));
        }

        public static List<XmlNode> Body(string xhtml)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml("<html xmlns:MadCap=\"" + MadCapNs + "\"><body>" + xhtml + "</body></html>");
            return doc.DocumentElement["body"].ChildNodes.Cast<XmlNode>().Where(n => n.NodeType == XmlNodeType.Element).ToList();
        }

        public static string Escape(string s) { return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }
    }
}
