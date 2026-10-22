using System;
using System.IO;
using System.IO.Compression;
using FlareAI.Core.Update;

namespace FlareAI.Flare
{
    /// <summary>
    /// Unzips an update (System.IO.Compression, which Core's net40 target lacks). Refuses entries that would land
    /// outside the target folder ("..\" or rooted names). Compiled into the Core tests too.
    /// </summary>
    internal sealed class ZipExtractor : IArchiveExtractor
    {
        public void Extract(string zipPath, string folder)
        {
            var root = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('/', '\\');
                    if (name.Length == 0) continue;
                    var dest = Path.GetFullPath(Path.Combine(root, name));
                    if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entry outside the folder: " + entry.FullName);
                    if (name.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, false);
                }
            }
        }
    }
}
