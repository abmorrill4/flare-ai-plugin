using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FlareAI.Flare
{
    /// <summary>
    /// The fallback when the feedback service can't take a report (issue #124): the report saved as a zip in
    /// Documents\House Style Reports (report.json, exactly what would have been sent, and the diag.log tail it carries as
    /// diag-log-tail.txt), plus an email draft to the configured address. No Flare dependencies (tests link it).
    /// </summary>
    internal static class ReportZip
    {
        public const string FolderName = "House Style Reports";

        /// <summary>Where a saved report goes when there is no email address for it: a new issue on the project.</summary>
        public const string IssuesUrl = "https://github.com/abmorrill4/flare-ai-plugin/issues/new";

        public const string SavedForGitHub = "House Style couldn't send the report. It's saved in Documents\\House Style Reports. Attach it to a new issue on GitHub.";
        public const string SavedForEmail = "House Style couldn't send the report. It's saved in Documents\\House Style Reports. Attach it to the email.";
        public const string NotSaved = "House Style couldn't send the report or save it in Documents\\House Style Reports.";
        public const string OpenIssuesLabel = "Open GitHub Issues", EmailLabel = "Email Report", OpenFolderLabel = "Open Folder";

        /// <summary>The explorer.exe arguments that open the folder with the zip selected.</summary>
        public static string SelectInFolder(string zipPath) { return "/select,\"" + zipPath + "\""; }

        /// <summary>Documents\House Style Reports.</summary>
        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), FolderName); }
        }

        /// <summary>Writes the zip and returns its path. <paramref name="json"/> is the report as it would have been sent.</summary>
        public static string Save(string json, string folder = null)
        {
            folder = folder ?? DefaultFolder;
            Directory.CreateDirectory(folder);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            var path = Path.Combine(folder, "House Style report " + stamp + ".zip");
            for (var i = 2; File.Exists(path); i++) path = Path.Combine(folder, "House Style report " + stamp + " (" + i + ").zip");
            string log = null;
            try { log = (string)JObject.Parse(json)["diag_log_tail"]; } catch (Exception) { }
            using (var fs = new FileStream(path, FileMode.CreateNew))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "report.json", json);
                if (!string.IsNullOrEmpty(log)) Add(zip, "diag-log-tail.txt", log);
            }
            return path;
        }

        private static void Add(ZipArchive zip, string name, string text)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(text);
        }

        /// <summary>
        /// A mailto: link for an email draft to <paramref name="email"/> that names the saved zip (a mailto link can't attach
        /// it), or null when no address is set.
        /// </summary>
        public static string MailtoLink(string email, string zipPath)
        {
            if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') < 1) return null;
            var body = "I couldn't send this House Style report, so it is saved here. Please attach it to this email." + "\r\n\r\n" + zipPath;
            return "mailto:" + Uri.EscapeDataString(email.Trim()).Replace("%40", "@") + "?subject=" + Uri.EscapeDataString("House Style report") + "&body=" + Uri.EscapeDataString(body);
        }
    }
}
