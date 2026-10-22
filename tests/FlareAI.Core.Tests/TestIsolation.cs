using System;
using System.IO;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("FlareAI.Core.Tests.IsolatedTestFramework", "FlareAI.Core.Tests")]

namespace FlareAI.Core.Tests
{
    /// <summary>
    /// Runs the whole test assembly with the writer's data folder redirected to a temp folder, so no test can write to the
    /// real %APPDATA%\FlareAI (diag.log, settings, caches), whatever it forgets to redirect itself.
    /// </summary>
    public sealed class IsolatedTestFramework : XunitTestFramework
    {
        private readonly string _root;

        public IsolatedTestFramework(IMessageSink sink) : base(sink)
        {
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
}
