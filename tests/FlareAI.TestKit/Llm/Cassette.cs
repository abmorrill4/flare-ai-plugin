using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FlareAI.Core.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.TestKit.Llm
{
    /// <summary>
    /// One recorded model exchange, stored as <c>&lt;cassette dir&gt;/&lt;task&gt;-&lt;key 16 hex&gt;.json</c>. Format
    /// <c>flareai-cassette/1</c> (full description in tests/FlareAI.TestKit/README.md):
    /// <code>
    /// {
    ///   "format": "flareai-cassette/1",
    ///   "key": "&lt;sha256 hex of the request digest&gt;",
    ///   "task": "review",
    ///   "recorded_at": "2026-10-21T12:00:00.0000000Z",
    ///   "request":  { "model", "effort", "max_tokens", "system_sha256", "packs_sha256", "terms_sha256", "schema_sha256", "images", "user" },
    ///   "response": { "text", "model", "stop_reason",
    ///                 "usage": { "input_tokens", "output_tokens", "cache_read_input_tokens", "cache_creation_input_tokens", "ephemeral_1h_input_tokens" } }
    /// }
    /// </code>
    /// The key covers task, system, packs, terms, user, schema and image bytes; model, effort and max_tokens are recorded
    /// for reading but do not change the key. No header, key or credential is ever part of a cassette.
    /// </summary>
    public static class Cassette
    {
        public const string Format = "flareai-cassette/1";
        public const string Redacted = "[REDACTED]";

        /// <summary>The request digest: SHA-256 (hex) over the canonical JSON of the parts that decide the answer.</summary>
        public static string Key(LlmRequest r)
        {
            var images = new JArray();
            if (r.Images != null) foreach (var i in r.Images) images.Add(i.MediaType + ":" + Sha256(i.Base64));
            var canonical = new JObject
            {
                ["task"] = r.Task ?? "",
                ["system"] = r.System ?? "",
                ["packs"] = r.SystemPacks ?? "",
                ["terms"] = r.SystemTerms ?? "",
                ["user"] = r.User ?? "",
                ["schema"] = r.Schema == null ? "" : r.Schema.ToString(Formatting.None),
                ["images"] = images
            };
            return Sha256(canonical.ToString(Formatting.None));
        }

        public static string FileName(LlmRequest r)
        {
            var task = Regex.Replace(string.IsNullOrEmpty(r.Task) ? "request" : r.Task, @"[^A-Za-z0-9_-]", "_");
            return task + "-" + Key(r).Substring(0, 16) + ".json";
        }

        public static JObject Build(LlmRequest r, LlmResponse resp, DateTime recordedAtUtc)
        {
            return new JObject
            {
                ["format"] = Format,
                ["key"] = Key(r),
                ["task"] = r.Task,
                ["recorded_at"] = recordedAtUtc.ToString("o"),
                ["request"] = new JObject
                {
                    ["model"] = r.Model,
                    ["effort"] = r.Effort,
                    ["max_tokens"] = r.MaxTokens,
                    ["system_sha256"] = Sha256(r.System ?? ""),
                    ["packs_sha256"] = Sha256(r.SystemPacks ?? ""),
                    ["terms_sha256"] = Sha256(r.SystemTerms ?? ""),
                    ["schema_sha256"] = r.Schema == null ? null : Sha256(r.Schema.ToString(Formatting.None)),
                    ["images"] = r.Images == null ? 0 : r.Images.Count,
                    ["user"] = r.User
                },
                ["response"] = new JObject
                {
                    ["text"] = resp.Text,
                    ["model"] = resp.Model,
                    ["stop_reason"] = resp.StopReason,
                    ["usage"] = new JObject
                    {
                        ["input_tokens"] = resp.InputTokens,
                        ["output_tokens"] = resp.OutputTokens,
                        ["cache_read_input_tokens"] = resp.CacheReadTokens,
                        ["cache_creation_input_tokens"] = resp.CacheWriteTokens,
                        ["ephemeral_1h_input_tokens"] = resp.CacheWrite1hTokens
                    }
                }
            };
        }

        /// <summary>The recorded response (Json is filled by the client's contract check, as for a live call).</summary>
        public static LlmResponse Response(JObject cassette)
        {
            var r = (JObject)cassette["response"];
            var u = r["usage"] as JObject ?? new JObject();
            return new LlmResponse
            {
                Text = (string)r["text"],
                Model = (string)r["model"],
                StopReason = (string)r["stop_reason"],
                InputTokens = (int?)u["input_tokens"] ?? 0,
                OutputTokens = (int?)u["output_tokens"] ?? 0,
                CacheReadTokens = (int?)u["cache_read_input_tokens"] ?? 0,
                CacheWriteTokens = (int?)u["cache_creation_input_tokens"] ?? 0,
                CacheWrite1hTokens = (int?)u["ephemeral_1h_input_tokens"] ?? 0
            };
        }

        /// <summary>
        /// Writes the cassette with every secret replaced by <see cref="Redacted"/>, then re-reads the file and refuses to
        /// leave it on disk if a secret, an API-key pattern or an auth header name survived.
        /// </summary>
        public static void Write(string path, JObject cassette, IEnumerable<string> secrets)
        {
            var text = Scrub(cassette.ToString(Formatting.Indented), secrets);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
            var leak = FindLeak(File.ReadAllText(path, Encoding.UTF8), secrets);
            if (leak != null)
            {
                File.Delete(path);
                throw new InvalidOperationException("Refused to write a cassette containing " + leak + ".");
            }
        }

        private static readonly Regex KeyPattern = new Regex(@"sk-ant-[A-Za-z0-9_\-]{6,}", RegexOptions.Compiled);
        private static readonly string[] HeaderNames = { "x-api-key", "x-bf-vk", "authorization" };

        public static string Scrub(string text, IEnumerable<string> secrets)
        {
            if (text == null) return null;
            if (secrets != null)
                foreach (var s in secrets)
                    if (!string.IsNullOrEmpty(s) && s.Length >= 6) text = text.Replace(s, Redacted);
            return KeyPattern.Replace(text, Redacted);
        }

        /// <summary>What leaked, or null.</summary>
        public static string FindLeak(string text, IEnumerable<string> secrets)
        {
            if (secrets != null)
                foreach (var s in secrets)
                    if (!string.IsNullOrEmpty(s) && s.Length >= 6 && text.Contains(s)) return "a configured secret";
            if (KeyPattern.IsMatch(text)) return "an API key";
            foreach (var h in HeaderNames)
                if (text.IndexOf("\"" + h + "\"", StringComparison.OrdinalIgnoreCase) >= 0) return "the header name " + h;
            return null;
        }

        public static string Sha256(string s)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
                var sb = new StringBuilder(64);
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
