using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlareAI.Core;
using FlareAI.Core.Update;

namespace FlareAI.Updater
{
    /// <summary>
    /// FlareAI.Updater.exe --root "%LOCALAPPDATA%\HouseStyle" --version 0.4.0 [--wait-pid 1234]
    /// Waits for that Flare process (and any other Flare) to exit, then installs the staged version with
    /// <see cref="UpdateSwap"/> and writes update-result.json, which the plugin reads on its next start.
    /// Exit code 0 = updated, 1 = failed (rolled back), 2 = bad arguments or another updater is already waiting.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string root = Arg(args, "--root"), version = Arg(args, "--version"), pid = Arg(args, "--wait-pid");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(version)) return 2;
            bool created;
            using (var mutex = new Mutex(true, @"Local\HouseStyle.Updater", out created))
            {
                if (!created) { Diagnostics.Log("Updater: another updater is already waiting"); return 2; }
                try
                {
                    int id;
                    if (int.TryParse(pid, out id)) WaitFor(id);
                    WaitForNoFlare();
                    Thread.Sleep(1500);   // let Windows release the DLL handles of the closed process
                    Diagnostics.Log("Updater: Flare closed, installing " + version);
                    var result = new UpdateSwap(new UpdateLayout(root)).Apply(version);
                    Diagnostics.Log("Updater: " + (result.Ok ? "updated " + result.From + " -> " + result.To : "failed: " + result.Error));
                    return result.Ok ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("Updater", ex);
                    try { new UpdateResult { Ok = false, To = version, Error = ex.Message, TimeUtc = DateTime.UtcNow }.Write(new UpdateLayout(root).ResultPath); } catch (Exception) { }
                    return 1;
                }
            }
        }

        private static void WaitFor(int id)
        {
            try { using (var p = Process.GetProcessById(id)) p.WaitForExit(); }
            catch (ArgumentException) { /* already gone */ }
        }

        private static void WaitForNoFlare()
        {
            while (Process.GetProcessesByName("Flare").Any()) Thread.Sleep(2000);
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }
    }
}
