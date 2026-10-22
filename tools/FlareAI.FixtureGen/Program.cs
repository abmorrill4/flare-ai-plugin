using System;
using System.IO;
using System.Linq;

namespace FlareAI.FixtureGen
{
    /// <summary>
    /// Regenerates tests/fixtures/rules from the bundled style guide.
    ///   FlareAI.FixtureGen.exe [--guide styleguide] [--out tests/fixtures/rules] [--check]
    /// --check regenerates in memory and exits 1 when the checked-in fixtures differ.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var root = RepoRoot();
            string guide = Path.Combine(root, "styleguide"), outDir = Path.Combine(root, @"tests\fixtures\rules");
            var check = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--guide") guide = Path.GetFullPath(args[++i]);
                else if (args[i] == "--out") outDir = Path.GetFullPath(args[++i]);
                else if (args[i] == "--check") check = true;
                else { Console.Error.WriteLine("Unknown argument: " + args[i]); return 2; }
            }
            var here = AppDomain.CurrentDomain.BaseDirectory;
            var files = Generator.Generate(guide, Path.Combine(here, "expectations.yaml"), Path.Combine(here, "structure-cases.json"), Path.Combine(here, "templates"));
            if (check)
            {
                var stale = Stale(outDir, files);
                foreach (var s in stale) Console.Error.WriteLine("stale: " + s);
                Console.WriteLine(stale.Length == 0 ? "Rule fixtures are current." : "Rule fixtures are stale: run FlareAI.FixtureGen.exe and commit tests/fixtures/rules.");
                return stale.Length == 0 ? 0 : 1;
            }
            Generator.Write(outDir, files);
            Console.WriteLine("Wrote " + files.Count + " files to " + outDir);
            return 0;
        }

        /// <summary>Files that are missing, changed or no longer generated (line endings ignored).</summary>
        public static string[] Stale(string dir, System.Collections.Generic.SortedDictionary<string, string> files)
        {
            var stale = new System.Collections.Generic.List<string>();
            foreach (var kv in files)
            {
                var p = Path.Combine(dir, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p) || File.ReadAllText(p).Replace("\r\n", "\n") != kv.Value) stale.Add(kv.Key);
            }
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    var rel = f.Substring(dir.TrimEnd('\\').Length + 1).Replace('\\', '/');
                    if (!files.ContainsKey(rel)) stale.Add(rel + " (no longer generated)");
                }
            return stale.ToArray();
        }

        private static string RepoRoot()
        {
            var d = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            if (d == null)
            {
                d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            }
            return d == null ? Directory.GetCurrentDirectory() : d.FullName;
        }
    }
}
