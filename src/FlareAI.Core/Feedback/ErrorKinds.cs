using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using FlareAI.Core.Llm;

namespace FlareAI.Core.Feedback
{
    /// <summary>
    /// Which failures are bugs worth a report (issue #124), and which are ordinary conditions with their own plain message:
    /// a file that is locked, read-only or missing, no network or an AI service error, a style guide that can't be written,
    /// Windows refusing to open a link. Those keep the plain message and never show the report dialog.
    /// </summary>
    public static class ErrorKinds
    {
        /// <summary>True for conditions the writer or their computer caused, not House Style.</summary>
        public static bool IsExpected(Exception ex)
        {
            ex = Unwrap(ex);
            if (ex == null) return true;
            return ex is IOException                          // locked, missing, disk full, read-only guide (GuideWriteException)
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException
                || ex is WebException                         // no network, proxy, timeout
                || ex is LlmException                         // no key, AI service errors
                || ex is Win32Exception                       // Windows couldn't open a link or program
                || ex is OperationCanceledException
                || ex is TimeoutException
                || ex is FlareAI.Core.Update.UpdateException
                || ex is System.Xml.XmlException              // a topic or guide file that isn't well-formed
                || ex is Newtonsoft.Json.JsonException;       // a settings or guide file that was edited by hand
        }

        /// <summary>A bug in House Style: not an expected condition.</summary>
        public static bool IsBug(Exception ex) { return ex != null && !IsExpected(ex); }

        /// <summary>The exception inside reflection and type-initializer wrappers.</summary>
        public static Exception Unwrap(Exception ex)
        {
            while (ex != null && (ex is TargetInvocationException || ex is TypeInitializationException) && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        private static readonly Regex Frame = new Regex(@"^\s*at\s+(.+?)(?:\s+in\s+.+:line\s+\d+)?\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>Frames of a .NET stack trace ("Ns.Class.Method"), without arguments, file paths or line numbers.</summary>
        public static List<string> Frames(string stack)
        {
            var list = new List<string>();
            foreach (Match m in Frame.Matches(stack ?? ""))
            {
                var f = m.Groups[1].Value;
                var paren = f.IndexOf('(');
                list.Add((paren >= 0 ? f.Substring(0, paren) : f).Trim());
            }
            return list;
        }

        private static bool Own(string frame) { return frame.StartsWith("FlareAI.", StringComparison.Ordinal) || frame.StartsWith("HouseStyle.", StringComparison.Ordinal); }

        private static bool Framework(string frame)
        {
            return frame.StartsWith("System.", StringComparison.Ordinal) || frame.StartsWith("Microsoft.", StringComparison.Ordinal)
                || frame.StartsWith("MS.", StringComparison.Ordinal) || frame.StartsWith("Newtonsoft.", StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the exception was thrown by House Style code: the first frame that isn't .NET itself is ours (so a
        /// NullReferenceException in our method, or an ArgumentException from .NET called by us, counts; an error inside
        /// Flare's own code does not). Used where an error could be Flare's: the editor hooks and the dispatcher hook.
        /// </summary>
        public static bool ThrownByUs(Exception ex)
        {
            ex = Unwrap(ex);
            if (ex == null) return false;
            foreach (var f in Frames(ex.StackTrace))
            {
                if (Own(f)) return true;
                if (!Framework(f)) return false;
            }
            return false;
        }

        /// <summary>Any House Style frame anywhere in the stack (our code was on the way).</summary>
        public static bool TouchesUs(Exception ex)
        {
            ex = Unwrap(ex);
            if (ex == null) return false;
            foreach (var f in Frames(ex.ToString())) if (Own(f)) return true;
            return false;
        }

        /// <summary>
        /// What makes two errors "the same": the exception type and the first three House Style frames (the service adds
        /// the version). Matches the service's signature, so one error is reported once per session.
        /// </summary>
        public static string Signature(Exception ex)
        {
            ex = Unwrap(ex);
            if (ex == null) return "none";
            var all = Frames(ex.StackTrace);
            var own = all.FindAll(Own);
            var top = (own.Count > 0 ? own : all);
            return ex.GetType().FullName + "|" + string.Join("|", top.GetRange(0, Math.Min(3, top.Count)).ToArray());
        }
    }

    /// <summary>The error signatures already reported (or declined) in this session: each one is offered once.</summary>
    public sealed class SessionReports
    {
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly object _gate = new object();

        /// <summary>The one for this Flare session.</summary>
        public static readonly SessionReports Current = new SessionReports();

        /// <summary>True the first time this signature is seen in the session; false after that.</summary>
        public bool TryClaim(string signature)
        {
            lock (_gate) return _seen.Add(signature ?? "");
        }

        /// <summary>True the first time this error's signature is seen in the session.</summary>
        public bool TryClaim(Exception ex) { return TryClaim(ErrorKinds.Signature(ex)); }

        public void Clear() { lock (_gate) _seen.Clear(); }
    }
}
