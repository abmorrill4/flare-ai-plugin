using System.Collections.Generic;
using Newtonsoft.Json;

namespace FlareAI.Core.Feedback
{
    /// <summary>
    /// The size limits of the feedback service (tools/feedback-worker/src/validate.ts, LIMITS), enforced here before
    /// sending so a long note or log never gets the whole report rejected. Lengths are UTF-16 code units, as on the server.
    /// </summary>
    public static class FeedbackLimits
    {
        public const int Version = 32, FlareVersion = 64, Os = 128, Text = 4000;
        public const int ExceptionType = 256, ExceptionMessage = 2000, Stack = 32000, DiagLog = 64 * 1024;
        public const int SettingsKeys = 100, SettingKey = 64, SettingValue = 500;
        public const int RuleId = 128, RuleName = 200, FlaggedText = 200, FpContext = 1000, FpNote = 1000;
    }

    /// <summary>The three reasons a writer can give for a false positive, as the service spells them.</summary>
    public static class FalsePositiveReason
    {
        public const string TextCorrect = "text_correct", ChangeWrong = "change_wrong", RuleDoesntFit = "rule_doesnt_fit";
        public static readonly string[] All = { TextCorrect, ChangeWrong, RuleDoesntFit };
    }

    /// <summary>The exception part of an error report.</summary>
    public sealed class ExceptionInfo
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("stack")] public string Stack { get; set; }
    }

    /// <summary>
    /// Body of <c>POST /v1/report</c>. Exactly the service's schema: it rejects unknown fields. Null fields are left out.
    /// </summary>
    public sealed class ReportPayload
    {
        public const string KindError = "error", KindProblem = "problem";

        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("plugin_version")] public string PluginVersion { get; set; }
        [JsonProperty("flare_version")] public string FlareVersion { get; set; }
        [JsonProperty("os")] public string Os { get; set; }
        [JsonProperty("automatic")] public bool Automatic { get; set; }
        [JsonProperty("what_happened")] public string WhatHappened { get; set; }
        [JsonProperty("expected")] public string Expected { get; set; }
        [JsonProperty("user_note")] public string UserNote { get; set; }
        [JsonProperty("exception")] public ExceptionInfo Exception { get; set; }
        [JsonProperty("diag_log_tail")] public string DiagLogTail { get; set; }
        /// <summary>Settings with nothing secret in them (<see cref="ReportBuilder.SafeSettings"/>).</summary>
        [JsonProperty("settings")] public SortedDictionary<string, object> Settings { get; set; }
    }

    /// <summary>Body of <c>POST /v1/false-positive</c>. Exactly the service's schema; null fields are left out.</summary>
    public sealed class FalsePositivePayload
    {
        [JsonProperty("client_id")] public string ClientId { get; set; }
        [JsonProperty("plugin_version")] public string PluginVersion { get; set; }
        [JsonProperty("rule_id")] public string RuleId { get; set; }
        [JsonProperty("rule_name")] public string RuleName { get; set; }
        [JsonProperty("reason")] public string Reason { get; set; }
        [JsonProperty("flagged_text")] public string FlaggedText { get; set; }
        [JsonProperty("context")] public string Context { get; set; }
        [JsonProperty("note")] public string Note { get; set; }
    }
}
