using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.Core.Update
{
    /// <summary>What the updater did, written to <c>update-result.json</c> and shown once on the next start.</summary>
    public sealed class UpdateResult
    {
        public bool Ok { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        /// <summary>Why it failed, in words for the writer. Null on success.</summary>
        public string Error { get; set; }
        public DateTime TimeUtc { get; set; }

        public static UpdateResult Read(string path)
        {
            try { return File.Exists(path) ? JsonConvert.DeserializeObject<UpdateResult>(File.ReadAllText(path, Encoding.UTF8)) : null; }
            catch (Exception) { return null; }
        }

        public void Write(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Installs a staged update while Flare is closed (run by FlareAI.Updater.exe): moves <c>staged\&lt;ver&gt;\FlareAI</c>
    /// to <c>versions\&lt;ver&gt;</c>, carries the IT policy file over, and repoints <c>current</c> with a rename dance
    /// (<c>current.new</c> made beside it, <c>current</c> renamed to <c>current.old</c>, <c>current.new</c> renamed to
    /// <c>current</c>) so <c>current</c> is never missing for longer than one rename. If anything fails, <c>current</c>
    /// goes back to the previous version. The previous version stays in <c>versions\</c> for rollback; older ones are removed.
    /// </summary>
    public sealed class UpdateSwap
    {
        private readonly UpdateLayout _layout;

        public UpdateSwap(UpdateLayout layout) { _layout = layout; }

        /// <summary>Test seam: runs after <c>current</c> points at the new version, before it is accepted (throw to force a rollback).</summary>
        public Action<string> AfterRepoint;

        public UpdateResult Apply(string version)
        {
            var result = new UpdateResult { To = version, TimeUtc = DateTime.UtcNow };
            var current = _layout.Current;
            string previousTarget = null;
            try
            {
                if (!Junction.IsLink(current)) throw new UpdateException("House Style isn't installed in the per-user layout (" + current + " is not a link).");
                previousTarget = Junction.FinalPath(current);
                result.From = UpdateLayout.VersionIn(current);

                var payload = UpdateStager.ReadyPayload(_layout, version);
                var target = _layout.VersionDir(version);
                if (payload != null)
                {
                    if (previousTarget != null && string.Equals(Junction.FinalPath(target), previousTarget, StringComparison.OrdinalIgnoreCase))
                        throw new UpdateException("Version " + version + " is already installed.");
                    UpdateStager.DeleteDir(target);
                    Directory.CreateDirectory(_layout.Versions);
                    Directory.Move(payload, target);
                }
                else if (!File.Exists(Path.Combine(target, "FlareAI.Flare.dll")))
                    throw new UpdateException("No downloaded update for " + version + " was found.");

                var policy = Path.Combine(current, UpdateLayout.PolicyFileName);
                if (File.Exists(policy)) File.Copy(policy, Path.Combine(target, UpdateLayout.PolicyFileName), true);

                Repoint(target);
                try
                {
                    var now = UpdateLayout.VersionIn(current);
                    if (SemVer.Compare(now, version) != 0) throw new UpdateException("After the update the plugin folder holds " + (now ?? "nothing") + ", not " + version + ".");
                    var hook = AfterRepoint;
                    if (hook != null) hook(target);
                }
                catch (Exception)
                {
                    if (previousTarget != null) Repoint(previousTarget);
                    throw;
                }

                result.Ok = true;
                UpdateStager.DeleteDir(_layout.StagedDir(version));
                Prune(target, previousTarget);
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Error = ex is UpdateException ? ex.Message : "The files couldn't be swapped (" + ex.Message + ").";
                Diagnostics.Log("Update swap to " + version, ex);
            }
            try { result.Write(_layout.ResultPath); } catch (Exception ex) { Diagnostics.Log("update result", ex); }
            return result;
        }

        /// <summary>Points <c>current</c> at <paramref name="target"/> with the rename dance; restores the old link on failure.</summary>
        public void Repoint(string target)
        {
            var current = _layout.Current;
            var fresh = current + ".new";
            var old = current + ".old";
            if (Junction.IsLink(fresh)) Junction.Remove(fresh);
            if (Junction.IsLink(old)) Junction.Remove(old);
            Junction.Create(fresh, target);
            var hadCurrent = Directory.Exists(current);
            if (hadCurrent) Directory.Move(current, old);
            try { Directory.Move(fresh, current); }
            catch (Exception)
            {
                if (hadCurrent && !Directory.Exists(current)) Directory.Move(old, current);
                try { Junction.Remove(fresh); } catch (Exception) { }
                throw;
            }
            try { if (hadCurrent) Junction.Remove(old); } catch (Exception ex) { Diagnostics.Log("remove " + old, ex); }
        }

        /// <summary>Removes versions other than the new and previous ones (best effort; a locked folder stays).</summary>
        private void Prune(string keep, string previous)
        {
            if (!Directory.Exists(_layout.Versions)) return;
            foreach (var dir in Directory.GetDirectories(_layout.Versions))
            {
                var real = Junction.FinalPath(dir) ?? dir;
                if (Same(real, keep) || Same(real, previous)) continue;
                UpdateStager.DeleteDir(dir);
            }
        }

        private static bool Same(string a, string b)
        {
            if (a == null || b == null) return false;
            var fa = Junction.FinalPath(a) ?? a; var fb = Junction.FinalPath(b) ?? b;
            return string.Equals(fa.TrimEnd('\\'), fb.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
