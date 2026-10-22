using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Who is writing, who owns the company style guide, and where shared feedback goes. Strict mode (an owners file
    /// exists in the guide) means only owners change shared rules; everyone else's rule changes become proposals.
    /// </summary>
    public sealed class Team
    {
        /// <summary>Root of the company style guide (a Flare project or folder), or null.</summary>
        public string GuideRoot { get; private set; }
        /// <summary>This writer's id: git user.email, or the Windows user name.</summary>
        public string Writer { get; private set; }
        public IList<string> Owners { get; private set; }
        /// <summary>Shared feedback folder (each writer appends to their own file), or null for personal-only.</summary>
        public string FeedbackFolder { get; private set; }
        /// <summary>
        /// What the shared feedback folder records about who made each decision (the guide's FlareAI/attribution.txt):
        /// false = "counts" (the default: no identity, only an anonymous per-machine id), true = "names" (the writer's email).
        /// </summary>
        public bool ShareNames { get; private set; }
        /// <summary>The identity written into feedback records and used to name the shared file, per the attribution setting.</summary>
        public string FeedbackWriter { get { return ShareNames ? Writer : AnonymousId(); } }
        public bool IsStrict { get { return Owners.Count > 0; } }
        /// <summary>The guide's git state, filled in by a background check (null until then, or when not in git).</summary>
        public GuideStatus Status { get; set; }
        public bool IsOwner { get { return !IsStrict || Contains(Owners, Writer) || Contains(Owners, Environment.UserName); } }

        /// <summary>
        /// The read-only guide (bundled with the plugin) that <see cref="GuideRoot"/> is layered after, when GuideRoot is the
        /// writer's own guide (it is created empty on its first write, named like this guide's layer); null when GuideRoot is
        /// a team guide.
        /// </summary>
        public string SeedFrom { get; set; }
        /// <summary>True when rule changes go to the writer's own guide (no team guide is configured).</summary>
        public bool IsPersonal { get { return SeedFrom != null; } }

        /// <summary>
        /// The guide to write rule changes into, ready to write: the writer's own guide is created (empty, layered after
        /// <see cref="SeedFrom"/>) on first need. Throws <see cref="FlareAI.Core.Style.GuideWriteException"/> naming the folder when that fails.
        /// </summary>
        public string WritableGuide()
        {
            if (GuideRoot == null) throw new InvalidOperationException("No style guide is set up. Choose one in Settings.");
            if (SeedFrom != null) FlareAI.Core.Style.GuideSeed.Ensure(SeedFrom, GuideRoot);
            return GuideRoot;
        }

        /// <summary>Throws unless this writer may change the shared guide's rules (an owner, or the guide has no owners file).</summary>
        public void EnsureOwner()
        {
            if (!IsOwner)
                throw new UnauthorizedAccessException("Only the style guide owners (" + string.Join(", ", new List<string>(Owners).ToArray()) + ") can change the team's rules. Use Suggest a change instead.");
        }

        /// <summary>The team context in effect (set at startup and when Settings change).</summary>
        public static Team Current { get; set; }

        public static Team Load(string guideRoot, string feedbackFolder, string writer = null)
        {
            var t = new Team
            {
                GuideRoot = string.IsNullOrEmpty(guideRoot) ? null : guideRoot.TrimEnd('\\', '/'),
                FeedbackFolder = string.IsNullOrEmpty(feedbackFolder) ? null : feedbackFolder.Trim(),
                Writer = writer ?? WriterId(),
                Owners = new List<string>()
            };
            var owners = t.GuideRoot == null ? null : Path.Combine(t.GuideRoot, Path.Combine("FlareAI", "owners.txt"));
            try
            {
                if (owners != null && File.Exists(owners))
                    foreach (var line in File.ReadAllLines(owners))
                    {
                        var id = line.Split('#')[0].Trim();
                        if (id.Length > 0) t.Owners.Add(id);
                    }
            }
            catch (Exception ex) { Diagnostics.Log("owners.txt", ex); }
            t.ShareNames = ReadAttribution(t.GuideRoot);
            return t;
        }

        /// <summary>True when the guide's FlareAI/attribution.txt says <c>attribution = names</c>; anything else (or no file) is counts.</summary>
        public static bool ReadAttribution(string guideRoot)
        {
            if (string.IsNullOrEmpty(guideRoot)) return false;
            try
            {
                var file = Path.Combine(guideRoot, Path.Combine("FlareAI", "attribution.txt"));
                if (!File.Exists(file)) return false;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Split('#')[0].Trim();
                    var eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim().Equals("attribution", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(eq + 1).Trim().Equals("names", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { Diagnostics.Log("attribution.txt", ex); }
            return false;
        }

        /// <summary>Where the anonymous writer id lives; the host may override (tests do). Default <see cref="Paths.WriterId"/> (%APPDATA%\FlareAI\writer-id.txt).</summary>
        public static string AnonymousIdPath { get; set; }

        private static readonly object IdGate = new object();
        private static readonly string SessionId = "writer-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        /// <summary>
        /// A random id for this machine, created once and stored locally. It is not derived from the email or user name,
        /// so it can't be reversed to a person; it only lets the owner count distinct writers.
        /// </summary>
        public static string AnonymousId()
        {
            lock (IdGate)
            {
                var path = AnonymousIdPath ?? Paths.WriterId;
                try
                {
                    if (File.Exists(path))
                    {
                        var existing = File.ReadAllText(path).Trim();
                        if (existing.Length > 0) return existing;
                    }
                    var id = "writer-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, id);
                    return id;
                }
                catch (Exception ex) { Diagnostics.Log("writer-id", ex); return SessionId; }
            }
        }

        /// <summary>The writer's file in the shared feedback folder, or null.</summary>
        public string SharedFeedbackFile
        {
            get { return FeedbackFolder == null ? null : Path.Combine(FeedbackFolder, Safe(FeedbackWriter) + ".jsonl"); }
        }

        private static string WriterId()
        {
            var email = Git.Run(null, "config --get user.email", 5000);
            return string.IsNullOrEmpty(email) ? Environment.UserName : email.Trim();
        }

        private static bool Contains(IList<string> list, string id)
        {
            foreach (var x in list) if (string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Safe(string id)
        {
            var sb = new StringBuilder();
            foreach (var c in id ?? "writer") sb.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// "CompanyStyle @ a1b2c3d" plus "· 3 change(s) behind — pull it" when the guide is a git checkout that is behind
        /// its upstream. Fetches first when asked (no prompts; short timeout). Null when the guide is not in git.
        /// </summary>
        public static GuideStatus ReadStatus(string guideRoot, bool fetch)
        {
            if (string.IsNullOrEmpty(guideRoot) || !Directory.Exists(guideRoot)) return null;
            var head = Git.Run(guideRoot, "rev-parse --short HEAD", 5000);
            if (string.IsNullOrEmpty(head)) return null;
            if (fetch) Git.Run(guideRoot, "fetch --quiet", 20000);
            var behind = Git.Run(guideRoot, "rev-list --count HEAD..@{upstream}", 5000);
            int n;
            return new GuideStatus { Name = Path.GetFileName(guideRoot), Commit = head.Trim(), Behind = int.TryParse((behind ?? "").Trim(), out n) ? n : 0 };
        }
    }

    public sealed class GuideStatus
    {
        public string Name { get; set; }
        public string Commit { get; set; }
        public int Behind { get; set; }
        public override string ToString()
        {
            return "Style guide " + Name + " @ " + Commit + (Behind > 0 ? " — " + Behind + " change(s) behind: pull it to get the team's latest rules." : "");
        }
    }

    /// <summary>Runs git without prompts. Returns stdout, or null when git is missing, fails, or times out.</summary>
    public static class Git
    {
        public static string Run(string workDir, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo("git", (workDir == null ? "" : "-C \"" + workDir + "\" ") + args)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEndAsyncCompat();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } return null; }
                    return p.ExitCode == 0 ? output() : null;
                }
            }
            catch (Exception) { return null; }
        }

        // net40 has no ReadToEndAsync on StreamReader: read on a pool thread so a slow fetch can't deadlock the pipe.
        public static Func<string> ReadToEndAsyncCompat(this StreamReader reader)
        {
            string result = null;
            var done = new System.Threading.ManualResetEvent(false);
            System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { result = reader.ReadToEnd(); } catch (Exception) { } done.Set(); });
            return () => { done.WaitOne(5000); return result; };
        }
    }
}
