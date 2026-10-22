using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.Scoring
{
    /// <summary>Reads and writes <c>labels.jsonl</c> (one JSON object per line, UTF-8 without BOM, LF).</summary>
    public static class LabelIO
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { Formatting = Formatting.None };

        public static List<Label> Read(string path)
        {
            var list = new List<Label>();
            int n = 0;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                n++;
                if (line.Trim().Length == 0) continue;
                try { list.Add(JsonConvert.DeserializeObject<Label>(line)); }
                catch (JsonException ex) { throw new InvalidDataException(path + " line " + n + ": " + ex.Message, ex); }
            }
            return list;
        }

        public static List<Label> ReadMany(IEnumerable<string> paths)
        {
            var all = new List<Label>();
            foreach (var p in paths) all.AddRange(Read(p));
            return all;
        }

        public static void Write(string path, IEnumerable<Label> labels)
        {
            var sb = new StringBuilder();
            foreach (var l in labels) sb.Append(JsonConvert.SerializeObject(l, Settings)).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static string ToLine(Label l) { return JsonConvert.SerializeObject(l, Settings); }
    }
}
