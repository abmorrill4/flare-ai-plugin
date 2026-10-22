using System;
using System.Collections.Generic;
using System.IO;
using FlareAI.Core.Llm;
using FlareAI.Core.Settings;
using FlareAI.TestKit.Workspace;
using Newtonsoft.Json.Linq;

namespace FlareAI.TestKit.Llm
{
    public enum ReplayMode
    {
        /// <summary>Answers only from cassettes; a missing cassette fails the call (and is listed in <see cref="ReplayLlmClient.Misses"/>).</summary>
        Strict,
        /// <summary>Calls the real model through the inner client and (over)writes the cassette, secrets scrubbed.</summary>
        Record
    }

    /// <summary>Strict replay found no cassette for a request.</summary>
    public sealed class CassetteMissingException : Exception
    {
        public string Path { get; private set; }
        public CassetteMissingException(string path, string task)
            : base("No cassette for this " + (task ?? "request") + " request: " + path + ". Record it with FLAREAI_RECORD=1 (key from .env via scripts\\Load-DotEnv.ps1).")
        {
            Path = path;
        }
    }

    /// <summary>
    /// <see cref="ILlmClient"/> that answers from recorded cassettes (see <see cref="Cassette"/> for the format), keyed by
    /// a hash of the request. Replayed responses go through the same contract check as a live call
    /// (<see cref="AnthropicClient.Check"/>), so truncation, refusal and bad JSON behave as they would live.
    /// </summary>
    public sealed class ReplayLlmClient : ILlmClient
    {
        public const string RecordVariable = "FLAREAI_RECORD";

        private readonly ILlmClient _inner;
        private readonly List<string> _secrets = new List<string>();
        private readonly object _gate = new object();
        private readonly List<LlmRequest> _requests = new List<LlmRequest>();
        private readonly List<string> _misses = new List<string>();
        private readonly List<string> _used = new List<string>();
        private readonly FakeClock _clock;

        /// <param name="directory">The scenario's cassette folder (normally tests/cassettes/&lt;scenario&gt;).</param>
        /// <param name="mode">Strict replay, or record through <paramref name="inner"/>.</param>
        /// <param name="inner">The live client used in record mode.</param>
        /// <param name="secrets">Values never to be written (the key the inner client uses).</param>
        public ReplayLlmClient(string directory, ReplayMode mode = ReplayMode.Strict, ILlmClient inner = null, IEnumerable<string> secrets = null, FakeClock clock = null)
        {
            if (mode == ReplayMode.Record && inner == null) throw new ArgumentException("Record mode needs a live client.", "inner");
            Directory = directory;
            Mode = mode;
            _inner = inner;
            _clock = clock;
            if (secrets != null) _secrets.AddRange(secrets);
            foreach (var name in new[] { PluginSettings.EnvKeyName, "BIFROST_VK" })
            {
                var v = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrEmpty(v)) _secrets.Add(v);
            }
        }

        /// <summary>
        /// Strict replay of tests/cassettes/&lt;scenario&gt;, or, when FLAREAI_RECORD=1, record mode over a live
        /// AnthropicClient (key from ANTHROPIC_API_KEY or the repo's .env; model from FLAREAI_RECORD_MODEL or the default).
        /// </summary>
        public static ReplayLlmClient ForScenario(string scenario)
        {
            var dir = System.IO.Path.Combine(Repo.CassettesRoot, scenario);
            if (Environment.GetEnvironmentVariable(RecordVariable) != "1") return new ReplayLlmClient(dir);
            var key = Environment.GetEnvironmentVariable(PluginSettings.EnvKeyName) ?? Repo.DotEnv(PluginSettings.EnvKeyName);
            if (string.IsNullOrEmpty(key)) throw new InvalidOperationException(RecordVariable + "=1 but no " + PluginSettings.EnvKeyName + " in the environment or .env.");
            var model = Environment.GetEnvironmentVariable("FLAREAI_RECORD_MODEL") ?? PluginSettings.DefaultModel;
            return new ReplayLlmClient(dir, ReplayMode.Record, new AnthropicClient(null, key, model, 300), new[] { key });
        }

        public string Directory { get; private set; }
        public ReplayMode Mode { get; private set; }

        /// <summary>Every request seen, in order.</summary>
        public IList<LlmRequest> Requests { get { lock (_gate) return _requests.ToArray(); } }
        public int Calls { get { lock (_gate) return _requests.Count; } }
        /// <summary>Cassette paths that strict replay could not find (Core may swallow the exception; assert on this).</summary>
        public IList<string> Misses { get { lock (_gate) return _misses.ToArray(); } }
        /// <summary>Cassette files read or written, in order.</summary>
        public IList<string> Used { get { lock (_gate) return _used.ToArray(); } }

        /// <summary>Throws when any request had no cassette.</summary>
        public void AssertNoMisses()
        {
            var m = Misses;
            if (m.Count > 0) throw new CassetteMissingException(m[0], "(" + m.Count + " missing)");
        }

        public string PathFor(LlmRequest request) { return System.IO.Path.Combine(Directory, Cassette.FileName(request)); }

        public LlmResponse Complete(LlmRequest request)
        {
            var path = PathFor(request);
            lock (_gate) _requests.Add(request);
            if (Mode == ReplayMode.Record)
            {
                var live = _inner.Complete(request);
                var at = _clock != null ? _clock.UtcNow : DateTime.UtcNow;
                Cassette.Write(path, Cassette.Build(request, live, at), _secrets);
                lock (_gate) _used.Add(path);
                return live;
            }
            if (!File.Exists(path))
            {
                lock (_gate) _misses.Add(path);
                throw new CassetteMissingException(path, request.Task);
            }
            var cassette = JObject.Parse(File.ReadAllText(path));
            if ((string)cassette["format"] != Cassette.Format) throw new InvalidDataException("Unknown cassette format in " + path);
            if ((string)cassette["key"] != Cassette.Key(request)) throw new InvalidDataException("Cassette key mismatch in " + path);
            lock (_gate) _used.Add(path);
            return AnthropicClient.Check(Cassette.Response(cassette), request);
        }
    }
}
