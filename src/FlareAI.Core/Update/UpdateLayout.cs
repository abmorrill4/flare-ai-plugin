using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FlareAI.Core.Update
{
    /// <summary>
    /// The per-user install that self-updates (issue #101):
    /// <code>
    /// %LOCALAPPDATA%\HouseStyle\
    ///   versions\0.4.0\        the plugin files of one version (FlareAI.Flare.dll, StyleGuide\, ...)
    ///   versions\0.3.2\        the previous version, kept for rollback
    ///   current               junction -> versions\0.4.0
    ///   staged\0.5.0\          a downloaded, verified update waiting for Flare to close
    ///   updater\              a copy of FlareAI.Updater.exe that does the swap (so it locks nothing in versions\)
    ///   update-state.json     last check, latest version seen
    ///   update-result.json    what the last swap did, read once by the plugin on the next start
    /// ...\Flare.app\Plugins\FlareAI  junction -> %LOCALAPPDATA%\HouseStyle\current (made once by Install.cmd, as admin)
    /// </code>
    /// </summary>
    public sealed class UpdateLayout
    {
        public const string PolicyFileName = "update-policy.json";

        public UpdateLayout(string root) { Root = Path.GetFullPath(root); }

        /// <summary>The real layout: <c>%LOCALAPPDATA%\HouseStyle</c>.</summary>
        public static UpdateLayout Default
        {
            get { return new UpdateLayout(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HouseStyle")); }
        }

        public string Root { get; private set; }
        public string Current { get { return Path.Combine(Root, "current"); } }
        public string Versions { get { return Path.Combine(Root, "versions"); } }
        public string Staged { get { return Path.Combine(Root, "staged"); } }
        public string UpdaterDir { get { return Path.Combine(Root, "updater"); } }
        public string StatePath { get { return Path.Combine(Root, "update-state.json"); } }
        public string ResultPath { get { return Path.Combine(Root, "update-result.json"); } }
        public string VersionDir(string version) { return Path.Combine(Versions, version); }
        public string StagedDir(string version) { return Path.Combine(Staged, version); }

        /// <summary>
        /// True when the running plugin (<paramref name="pluginDir"/>, usually <c>Flare.app\Plugins\FlareAI</c>) resolves
        /// into this layout's <c>versions\</c> folder, so the writer can update it without admin rights. False for the
        /// Program Files copy layout (<c>Install.ps1 -NoJunction</c>), a developer install, or another Windows user's junction.
        /// </summary>
        public bool Manages(string pluginDir, out string reason)
        {
            if (!Directory.Exists(Versions)) { reason = "not installed per user (no " + Versions + ")"; return false; }
            var real = Junction.FinalPath(pluginDir);
            var versions = Junction.FinalPath(Versions);
            if (real == null || versions == null) { reason = "cannot resolve the plugin folder"; return false; }
            if (!PathUnder(real, versions)) { reason = "plugin folder " + real + " is not under " + versions; return false; }
            reason = null;
            return true;
        }

        /// <summary>The version installed in <paramref name="dir"/>: FlareAI.Flare.dll's product version, without "+commit". Null if none.</summary>
        public static string VersionIn(string dir)
        {
            try
            {
                var dll = Path.Combine(dir, "FlareAI.Flare.dll");
                if (!File.Exists(dll)) return null;
                var v = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
                if (string.IsNullOrEmpty(v)) return null;
                var plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : v;
            }
            catch (Exception) { return null; }
        }

        public static bool PathUnder(string path, string folder)
        {
            var p = path.TrimEnd('\\', '/') + "\\";
            var f = folder.TrimEnd('\\', '/') + "\\";
            return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Directory junctions (no admin rights needed, unlike symbolic links). Never delete a junction recursively: in
    /// Windows PowerShell 5.1 and older .NET code that deletes the files of the folder it points to. <see cref="Remove"/>
    /// removes only the link.
    /// </summary>
    public static class Junction
    {
        public static bool IsLink(string path)
        {
            try { return Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (Exception) { return false; }
        }

        /// <summary>Creates <paramref name="link"/> pointing at <paramref name="target"/> (mklink /J). Throws on failure.</summary>
        public static void Create(string link, string target)
        {
            if (Directory.Exists(link) || File.Exists(link)) throw new IOException(link + " already exists.");
            var psi = new ProcessStartInfo("cmd.exe", "/d /c mklink /J \"" + link + "\" \"" + target + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0 || !IsLink(link)) throw new IOException("Could not create the junction " + link + " -> " + target + ": " + err.Trim());
            }
        }

        /// <summary>Removes the junction itself; the folder it points to is untouched. No-op when absent; throws if it is a real folder.</summary>
        public static void Remove(string link)
        {
            if (!Directory.Exists(link)) return;
            if (!IsLink(link)) throw new IOException(link + " is a real folder, not a junction.");
            Directory.Delete(link, false);   // RemoveDirectory: removes the reparse point only
        }

        /// <summary>The fully resolved path of an existing folder (all junctions followed), or null.</summary>
        public static string FinalPath(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return null;
                using (var h = CreateFile(path, 0, 7 /* share all */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0x02000000 /* BACKUP_SEMANTICS */, IntPtr.Zero))
                {
                    if (h.IsInvalid) return Path.GetFullPath(path);
                    var sb = new StringBuilder(1024);
                    var n = GetFinalPathNameByHandle(h, sb, sb.Capacity, 0);
                    if (n == 0 || n > sb.Capacity) return Path.GetFullPath(path);
                    var s = sb.ToString();
                    if (s.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + s.Substring(8);
                    if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) return s.Substring(4);
                    return s;
                }
            }
            catch (Exception) { return null; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int size, int flags);
    }
}
