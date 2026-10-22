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
        public bool IsOwner { get { return !IsStrict || IsListedOwner; } }
        /// <summary>True when owners.txt names this writer (by email or Windows user name).</summary>
        public bool IsListedOwner { get { return Contains(Owners, Writer) || Contains(Owners, Environment.UserName); } }

        /// <summary>
        /// Whether the last probe could write to the guide's FlareAI folder (null until probed). Folder permissions are what
        /// protect a shared guide: owners read-write, writers read-only.
        /// </summary>
        public bool? GuideWritable { get; private set; }
        /// <summary>Why the last probe could not write to the guide, or null.</summary>
        public string GuideProblem { get; private set; }

        /// <summary>
        /// Tests write access now (a create-and-delete probe in the guide's FlareAI folder; file attributes are not trusted).
        /// Called on load and before every guide write. Returns why the guide can't be written, or null.
        /// </summary>
        public string ProbeGuide()
        {
            string why;
            GuideWritable = GuideAccess.CanWrite(GuideRoot, out why);
            GuideProblem = why;
            return why;
        }

        /// <summary>The cached probe result (probes once when not yet known).</summary>
        public bool CanWriteGuide
        {
            get
            {
                if (GuideWritable == null) ProbeGuide();
                return GuideWritable == true;
            }
        }

        /// <summary>
        /// True when a rule change must go to the owners as a request instead of being written: the guide has an owners file,
        /// it is a team guide, and either this writer isn't an owner or the guide folder is read-only for them. Teams without
        /// owners.txt, and personal guides, never need requests.
        /// </summary>
        public bool ChangesNeedRequest
        {
            get { return IsStrict && !IsPersonal && GuideRoot != null && (!IsListedOwner || !CanWriteGuide); }
        }

        /// <summary>The owners, for messages ("a@x.com, b@x.com").</summary>
        public string OwnerNames { get { return string.Join(", ", new List<string>(Owners).ToArray()); } }

        /// <summary>
        /// What the writer should know about how their changes reach the guide (null when changes apply directly): read-only,
        /// an owner who can't write (what to fix), or a writer the folder lets write although owners.txt doesn't list them.
        /// </summary>
        public string AccessNotice
        {
            get
            {
                if (!ChangesNeedRequest) return null;
                if (IsListedOwner)
                    return "You're a style guide owner, but House Style can't write to " + GuideRoot + (GuideProblem == null ? "" : " (" + GuideProblem + ")") +
                           ". Ask whoever manages that folder for write access. Until then, changes go to the owners as requests.";
                if (CanWriteGuide)
                    return "The guide folder lets you write, but owners.txt doesn't list you, so your changes go to the owners (" + OwnerNames +
                           ") as requests. Tell them the folder is more open than the owners list says.";
                return "You can read this guide; changes go to the owners (" + OwnerNames + ") as requests.";
            }
        }

        /// <summary>
        /// The read-only guide (bundled with the plugin) that <see cref="GuideRoot"/> is copied from on its first write, when
        /// GuideRoot is the writer's own guide; null when GuideRoot is a team guide.
        /// </summary>
        public string SeedFrom { get; set; }
        /// <summary>True when rule changes go to the writer's own guide (no team guide is configured).</summary>
        public bool IsPersonal { get { return SeedFrom != null; } }

        /// <summary>
        /// The guide to write rule changes into, ready to write: the writer's own guide is created from <see cref="SeedFrom"/>
        /// on first need. Throws <see cref="FlareAI.Core.Style.GuideWriteException"/> naming the folder when that fails.
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

    /// <summary>Whether a guide folder can be written: a real create-and-delete probe, because shares report attributes unreliably.</summary>
    public static class GuideAccess
    {
        /// <summary>True when a file can be created and deleted in the guide's FlareAI folder; otherwise false and why.</summary>
        public static bool CanWrite(string guideRoot, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(guideRoot)) { why = "no style guide is set up"; return false; }
            try
            {
                if (!Directory.Exists(guideRoot)) { why = "the folder can't be reached"; return false; }
                var dir = Path.Combine(guideRoot, "FlareAI");
                var created = false;
                if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); created = true; }
                var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
                // DeleteOnClose asks for delete access up front: the probe both creates and deletes, and leaves nothing behind.
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                if (File.Exists(probe)) File.Delete(probe);
                if (created) try { Directory.Delete(dir); } catch (Exception) { }
                return true;
            }
            catch (UnauthorizedAccessException) { why = "you have read-only access"; return false; }
            catch (IOException ex) { why = ex.Message.TrimEnd('.', ' '); return false; }
            catch (Exception ex) { Diagnostics.Log("guide write probe", ex); why = ex.Message.TrimEnd('.', ' '); return false; }
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
