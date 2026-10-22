using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlareAI.Core.Llm;
using FlareAI.TestKit.Llm;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.Scenarios.Smoke
{
    /// <summary>The recorded, scripted and HTTP-mocked model clients behave as documented.</summary>
    public class LlmHarnessTests : IDisposable
    {
        private const string FakeKey = "sk-ant-api03-TESTKEY-never-written-0123456789";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "flareai-cassettes-" + Guid.NewGuid().ToString("N"));

        public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

        private static LlmRequest Review(string user = "Text: ⟦r1⟧We utilize it.⟦/r1⟧")
        {
            return new LlmRequest
            {
                Task = "review", System = "You review docs.", SystemPacks = "packs", SystemTerms = "terms", User = user, Model = "claude-sonnet-5-5",
                Schema = JObject.Parse("{\"type\":\"object\",\"properties\":{\"findings\":{\"type\":\"array\"}}}")
            };
        }

        // ---- ReplayLlmClient ------------------------------------------------------------------

        [Fact]
        public void Strict_MissingCassette_FailsAndIsListed()
        {
            var replay = new ReplayLlmClient(_dir);
            var ex = Assert.Throws<CassetteMissingException>(() => replay.Complete(Review()));
            Assert.Contains("FLAREAI_RECORD=1", ex.Message);
            Assert.Single(replay.Misses);
            Assert.Throws<CassetteMissingException>(() => replay.AssertNoMisses());
        }

        [Fact]
        public void Record_ThenStrictReplay_ReturnsTheRecordedAnswer()
        {
            var live = new ScriptedLlmClient().Always("{\"findings\":[]}");
            var recorder = new ReplayLlmClient(_dir, ReplayMode.Record, live, new[] { FakeKey });
            recorder.Complete(Review());
            Assert.Single(Directory.GetFiles(_dir, "review-*.json"));

            var replay = new ReplayLlmClient(_dir);
            var resp = replay.Complete(Review());
            Assert.Equal("{\"findings\":[]}", resp.Text);
            Assert.NotNull(resp.Json);                 // the contract check ran, as for a live call
            Assert.Equal(1, replay.Calls);
            Assert.Empty(replay.Misses);
            Assert.Equal(1, live.Calls);               // only the recording reached the live client
        }

        [Fact]
        public void Key_CoversTheRequestParts_ButNotTheModel()
        {
            var a = Review();
            var b = Review(); b.Model = "claude-opus-5-5"; b.Effort = "high";
            Assert.Equal(Cassette.Key(a), Cassette.Key(b));
            foreach (var change in new Action<LlmRequest>[] { r => r.Task = "rewrite", r => r.System += "!", r => r.SystemPacks += "!", r => r.SystemTerms += "!", r => r.User += "!", r => r.Schema = null })
            {
                var c = Review();
                change(c);
                Assert.NotEqual(Cassette.Key(a), Cassette.Key(c));
            }
        }

        [Fact]
        public void Cassette_HasTheDocumentedShape()
        {
            new ReplayLlmClient(_dir, ReplayMode.Record, new ScriptedLlmClient().Always("{\"findings\":[]}")).Complete(Review());
            var c = JObject.Parse(File.ReadAllText(Directory.GetFiles(_dir).Single()));
            Assert.Equal(Cassette.Format, (string)c["format"]);
            Assert.Equal(Cassette.Key(Review()), (string)c["key"]);
            foreach (var p in new[] { "model", "effort", "max_tokens", "system_sha256", "packs_sha256", "terms_sha256", "schema_sha256", "images", "user" })
                Assert.True(((JObject)c["request"]).Property(p) != null, "request." + p);
            foreach (var p in new[] { "input_tokens", "output_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "ephemeral_1h_input_tokens" })
                Assert.True(((JObject)c["response"]["usage"]).Property(p) != null, "usage." + p);
        }

        [Fact]
        public void Record_NeverWritesKeysOrAuthHeaders()
        {
            // The worst case: the key shows up in the prompt and in the answer. It must still never reach disk.
            var leaky = new ScriptedLlmClient().When(r => true, r => new LlmResponse { Text = "{\"findings\":[],\"note\":\"" + FakeKey + " x-api-key\"}", StopReason = "end_turn" });
            var recorder = new ReplayLlmClient(_dir, ReplayMode.Record, leaky, new[] { FakeKey });
            recorder.Complete(Review("Text: ⟦r1⟧my key is " + FakeKey + "⟦/r1⟧"));
            recorder.Complete(Review("Text: ⟦r1⟧another key sk-ant-admin01-ZZZZZZZZZZ⟦/r1⟧"));
            foreach (var f in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(f);
                Assert.DoesNotContain(FakeKey, text);
                Assert.DoesNotContain("sk-ant-", text);
                Assert.DoesNotContain("\"x-api-key\"", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("\"authorization\"", text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(Cassette.Redacted, text);
            }
        }

        [Fact]
        public void Record_RefusesAFileThatWouldStillLeak()
        {
            var path = Path.Combine(_dir, "x.json");
            var leak = new JObject { ["headers"] = new JObject { ["x-api-key"] = "whatever" } };
            Assert.Throws<InvalidOperationException>(() => Cassette.Write(path, leak, new string[0]));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void Replay_TruncatedCassette_FailsLikeALiveCall()
        {
            // A live failure throws before anything is written, so a failure cassette is written by hand.
            var req = new LlmRequest { Task = "review", User = "x" };
            Cassette.Write(Path.Combine(_dir, Cassette.FileName(req)), Cassette.Build(req, new LlmResponse { Text = "{\"fi", StopReason = "max_tokens" }, DateTime.UtcNow), null);
            var ex = Assert.Throws<LlmException>(() => new ReplayLlmClient(_dir).Complete(new LlmRequest { Task = "review", User = "x" }));
            Assert.Equal(LlmFailure.Truncated, ex.Failure);
        }

        // ---- ScriptedLlmClient -----------------------------------------------------------------

        [Theory]
        [InlineData(LlmFailure.Http, 401)]
        [InlineData(LlmFailure.Http, 403)]
        [InlineData(LlmFailure.Http, 429)]
        [InlineData(LlmFailure.Http, 500)]
        [InlineData(LlmFailure.Network, 0)]
        [InlineData(LlmFailure.Truncated, 0)]
        [InlineData(LlmFailure.Refusal, 0)]
        [InlineData(LlmFailure.Contract, 0)]
        public void Scripted_ProducesEveryFailure(LlmFailure failure, int status)
        {
            var client = ScriptedLlmClient.Failing(failure, status);
            var ex = Assert.Throws<LlmException>(() => client.Complete(Review()));
            Assert.Equal(failure, ex.Failure);
            Assert.Equal(status, ex.StatusCode);
            if (status > 0) Assert.StartsWith("HTTP " + status + ":", ex.Message);
        }

        [Fact]
        public void Scripted_CoversEveryLlmFailureValue()
        {
            foreach (LlmFailure f in Enum.GetValues(typeof(LlmFailure)))
                Assert.Equal(f, Assert.Throws<LlmException>(() => ScriptedLlmClient.Failing(f).Complete(Review())).Failure);
        }

        [Fact]
        public void Scripted_QueueFirst_ThenRules_ThenLoudFailure()
        {
            var client = new ScriptedLlmClient().ThenFail(LlmFailure.Http, 429).WhenTask("review", "{\"findings\":[]}");
            Assert.Equal(429, Assert.Throws<LlmException>(() => client.Complete(Review())).StatusCode);
            Assert.NotNull(client.Complete(Review()).Json);
            Assert.Throws<InvalidOperationException>(() => client.Complete(new LlmRequest { Task = "rewrite" }));
            Assert.Equal(3, client.Calls);
        }

        // ---- MockLlmServer over the real HTTP clients -------------------------------------------

        private static AnthropicClient Anthropic(MockLlmServer s) { return new AnthropicClient(s.BaseUrl, FakeKey, "claude-sonnet-5-5", 10); }
        private static BifrostClient Gateway(MockLlmServer s) { return new BifrostClient(s.BaseUrl, "vk-test-virtual-key", "claude-sonnet-5-5", 10); }

        [Fact]
        public void Mock_ServesAnthropicShapedAnswers()
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(MockReply.Ok("{\"findings\":[]}"));
                var resp = Anthropic(s).Complete(Review());
                Assert.NotNull(resp.Json);
                Assert.Equal(120, resp.InputTokens);
                var req = s.Received.Single();
                Assert.Equal("/v1/messages", req.Path);
                Assert.Equal(FakeKey, req.Headers["x-api-key"]);
            }
        }

        [Fact]
        public void Mock_ServesGatewayShapedAnswers()
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(MockReply.Ok("{\"findings\":[]}"), MockReply.Ok("{\"fi", "max_tokens"));
                Assert.NotNull(Gateway(s).Complete(Review()).Json);
                Assert.Equal(LlmFailure.Truncated, Assert.Throws<LlmException>(() => Gateway(s).Complete(Review())).Failure);
                Assert.True(s.Received.All(r => r.IsGateway && r.Headers["x-bf-vk"] == "vk-test-virtual-key"));
            }
        }

        [Theory]
        [InlineData(401, false)]
        [InlineData(403, false)]
        [InlineData(429, false)]
        [InlineData(500, false)]
        [InlineData(529, false)]
        [InlineData(401, true)]
        [InlineData(403, true)]
        [InlineData(429, true)]
        [InlineData(500, true)]
        public void Mock_HttpErrors_ReachTheClientWithStatusAndMessage(int status, bool gateway)
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(status == 401 ? MockReply.Unauthorized() : status == 403 ? MockReply.ModelDenied() : status == 429 ? MockReply.RateLimited() : MockReply.ServerError(status));
                ILlmClient client = gateway ? (ILlmClient)Gateway(s) : Anthropic(s);
                var ex = Assert.Throws<LlmException>(() => client.Complete(Review()));
                Assert.Equal(LlmFailure.Http, ex.Failure);
                Assert.Equal(status, ex.StatusCode);
                Assert.StartsWith("HTTP " + status + ": ", ex.Message);
                Assert.DoesNotContain("{", ex.Message);          // the error body's message, not raw JSON
                Assert.DoesNotContain(FakeKey, ex.ToString());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Mock_MalformedBody_Fails(bool gateway)
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(MockReply.Malformed());
                ILlmClient client = gateway ? (ILlmClient)Gateway(s) : Anthropic(s);
                Assert.ThrowsAny<Exception>(() => client.Complete(Review()));
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Mock_TruncatedBody_Fails(bool gateway)
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(MockReply.TruncatedBody("{\"findings\":[{\"run_id\":\"r1\",\"excerpt\":\"utilize\"}]}"));
                ILlmClient client = gateway ? (ILlmClient)Gateway(s) : Anthropic(s);
                Assert.ThrowsAny<Exception>(() => client.Complete(Review()));
            }
        }

        [Fact]
        public void Mock_SlowAnswer_ArrivesLate()
        {
            using (var s = new MockLlmServer())
            {
                s.Enqueue(MockReply.Ok("{\"findings\":[]}").After(400));
                var sw = Stopwatch.StartNew();
                Assert.NotNull(Anthropic(s).Complete(Review()).Json);
                Assert.True(sw.ElapsedMilliseconds >= 350, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        [Fact]
        public void Mock_DefaultReply_AnswersWhenTheQueueIsEmpty()
        {
            using (var s = new MockLlmServer { Default = MockReply.Ok("{\"findings\":[]}") })
            {
                Anthropic(s).Complete(Review());
                Anthropic(s).Complete(Review());
                Assert.Equal(2, s.Received.Count);
            }
        }
    }
}
