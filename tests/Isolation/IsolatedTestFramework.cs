using System;
using System.IO;
using FlareAI.Core;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FlareAI.TestIsolation
{
    /// <summary>
    /// Shared by every test project (linked in; each adds <c>[assembly: TestFramework("FlareAI.TestIsolation.IsolatedTestFramework", "&lt;its assembly&gt;")]</c>).
    /// Runs the whole assembly with the writer's data folder redirected to a temp folder, so no test can write to the real
    /// %APPDATA%\FlareAI (diag.log, settings, caches), whatever it forgets to redirect itself.
    /// </summary>
    public sealed class IsolatedTestFramework : XunitTestFramework
    {
        private readonly string _root;

        /// <summary>The real diag.log as it was when the run started: <see cref="RealDataGuard"/> fails if it changes.</summary>
        internal static readonly string RealDiagLog = Path.Combine(Paths.DefaultRoot, "diag.log");
        internal static long RealLength = -1;
        internal static DateTime RealWritten;

        public IsolatedTestFramework(IMessageSink sink) : base(sink)
        {
            try
            {
                if (File.Exists(RealDiagLog)) { var i = new FileInfo(RealDiagLog); RealLength = i.Length; RealWritten = i.LastWriteTimeUtc; }
            }
            catch (Exception) { }
            _root = Path.Combine(Path.GetTempPath(), "flareai-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_root);
            Paths.Root = _root;
            DisposalTracker.Add(new Cleanup(_root));
            AppDomain.CurrentDomain.ProcessExit += (o, e) => new Cleanup(_root).Dispose();   // the runner may not dispose the framework
        }

        private sealed class Cleanup : IDisposable
        {
            private readonly string _root;
            public Cleanup(string root) { _root = root; }
            public void Dispose() { try { Paths.Root = null; Directory.Delete(_root, true); } catch (Exception) { } }
        }
    }

    /// <summary>Fails when the test run touched the real %APPDATA%\FlareAI\diag.log (or never redirected the data folder).</summary>
    public class RealDataGuard
    {
        [Fact]
        public void TestRun_DoesNotTouchTheRealDataFolder()
        {
            Assert.True(Paths.IsRedirected, "Paths.Root is not redirected to a temp folder");
            Assert.NotEqual(Path.GetFullPath(Paths.DefaultRoot).TrimEnd('\\'), Path.GetFullPath(Paths.Root).TrimEnd('\\'));
            var real = IsolatedTestFramework.RealDiagLog;
            if (!File.Exists(real)) return;
            var now = new FileInfo(real);
            Assert.True(now.Length == IsolatedTestFramework.RealLength && now.LastWriteTimeUtc == IsolatedTestFramework.RealWritten,
                        "A test wrote to the real " + real + ": redirect Paths.Root (or Diagnostics) to a temp folder.");
        }
    }
}
