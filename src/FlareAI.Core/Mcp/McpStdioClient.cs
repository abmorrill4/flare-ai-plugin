using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Mcp
{
    /// <summary>
    /// Minimal MCP client over stdio (newline-delimited JSON-RPC 2.0): initialize, then tools/call.
    /// One long-lived server process; calls are serialized. net40-friendly (no async).
    /// </summary>
    public sealed class McpStdioClient : IDisposable
    {
        private readonly Process _proc;
        private readonly BlockingCollection<string> _lines = new BlockingCollection<string>();
        private readonly StringBuilder _stderr = new StringBuilder();
        private readonly object _gate = new object();
        private readonly int _timeoutMs;
        private int _nextId;

        /// <summary>Starts the server and performs the MCP handshake. Throws McpException on failure.</summary>
        public McpStdioClient(string command, string arguments, string workingDirectory, int timeoutMs)
        {
            _timeoutMs = timeoutMs;
            var psi = new ProcessStartInfo(command, arguments ?? "")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory ?? ""
            };
            try { _proc = Process.Start(psi); }
            catch (Exception ex) { throw new McpException("Could not start style server '" + command + "': " + ex.Message, ex); }

            _proc.OutputDataReceived += (s, e) => { if (e.Data != null) _lines.Add(e.Data); else _lines.CompleteAdding(); };
            _proc.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (_stderr) { if (_stderr.Length < 8000) _stderr.AppendLine(e.Data); } };
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            Request("initialize", new JObject
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "FlareAI", ["version"] = "0.1.0" }
            });
            Send(new JObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
        }

        public bool IsAlive { get { try { return !_proc.HasExited; } catch (InvalidOperationException) { return false; } } }

        /// <summary>Calls a tool and returns its result parsed as JSON (from the first text content item).</summary>
        public JToken CallTool(string name, JObject arguments)
        {
            var result = Request("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments ?? new JObject() });
            if ((bool?)result["isError"] == true)
                throw new McpException("Tool " + name + " failed: " + (string)result.SelectToken("content[0].text"));
            var structured = result["structuredContent"];
            if (structured != null && structured.Type == JTokenType.Object) return structured;
            var text = (string)result.SelectToken("content[0].text");
            if (text == null) throw new McpException("Tool " + name + " returned no content.");
            try { return JToken.Parse(text); } catch (JsonException) { return new JValue(text); }
        }

        private JToken Request(string method, JObject parameters)
        {
            lock (_gate)
            {
                var id = Interlocked.Increment(ref _nextId);
                Send(new JObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
                var deadline = DateTime.UtcNow.AddMilliseconds(_timeoutMs);
                while (true)
                {
                    var left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                    string line;
                    if (left <= 0 || !_lines.TryTake(out line, left))
                        throw new McpException(_lines.IsCompleted
                            ? "Style server exited. " + StderrTail()
                            : "Style server did not answer " + method + " within " + _timeoutMs / 1000 + " s.");
                    JObject msg;
                    try { msg = JObject.Parse(line); } catch (JsonException) { continue; } // stray stdout noise
                    if ((int?)msg["id"] != id) continue; // notifications, logs, other ids
                    var err = msg["error"];
                    if (err != null) throw new McpException(method + " failed: " + (string)err["message"]);
                    return msg["result"] ?? new JObject();
                }
            }
        }

        private void Send(JObject message)
        {
            try
            {
                _proc.StandardInput.WriteLine(message.ToString(Formatting.None));
                _proc.StandardInput.Flush();
            }
            catch (Exception ex) { throw new McpException("Style server is not accepting input. " + StderrTail(), ex); }
        }

        private string StderrTail()
        {
            lock (_stderr)
            {
                var s = _stderr.ToString().Trim();
                return s.Length > 600 ? "…" + s.Substring(s.Length - 600) : s;
            }
        }

        public void Dispose()
        {
            try { _proc.StandardInput.Close(); } catch (Exception) { }
            try { if (!_proc.WaitForExit(1000)) _proc.Kill(); } catch (Exception) { }
            _proc.Dispose();
        }
    }

    public sealed class McpException : Exception
    {
        public McpException(string message) : base(message) { }
        public McpException(string message, Exception inner) : base(message, inner) { }
    }
}
