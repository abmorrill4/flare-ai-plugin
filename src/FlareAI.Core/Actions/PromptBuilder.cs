using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace FlareAI.Core.Actions
{
    public enum ActionKind { CheckStyle, Rewrite, Ask }

    /// <summary>
    /// Assembles system + user prompts from embedded templates plus optional rule files shipped next to the
    /// plugin (rules\*.md). Pure function of its inputs so tests can snapshot the output.
    /// </summary>
    public sealed class PromptBuilder
    {
        private readonly Func<string> _guide;

        /// <param name="companyGuide">
        /// Supplies the company style guide (from docstyle). Called on the worker thread when a prompt is built;
        /// may be null, return null, or throw — the baseline still applies.
        /// </param>
        public PromptBuilder(Func<string> companyGuide)
        {
            _guide = companyGuide;
        }

        public string BuildSystem()
        {
            var sb = new StringBuilder(Embedded("system-base.md"));
            string guide = null;
            try { if (_guide != null) guide = _guide(); }
            catch (Exception ex) { Diagnostics.Log("style guide", ex); }
            sb.AppendLine().AppendLine();
            if (!string.IsNullOrEmpty(guide))
            {
                sb.AppendLine("Style guide: layers in order; each later layer overrides the ones before it on any conflict. Every rule has an id in [brackets]; cite it when you apply the rule.").AppendLine();
                sb.Append(guide.Trim());
            }
            else
            {
                sb.Append("Style guide: follow the Google developer documentation style guide (https://developers.google.com/style).");
            }
            return sb.ToString();
        }

        /// <param name="lint">One-line-per-finding summary from the linter, or "".</param>
        public string BuildUser(ActionKind kind, string fragment, string instruction, string lint = "")
        {
            var lintBlock = string.IsNullOrEmpty(lint) ? "(none)" : lint;
            switch (kind)
            {
                case ActionKind.CheckStyle:
                    return Embedded("action-check.md").Replace("{{LINT}}", lintBlock).Replace("{{FRAGMENT}}", fragment);
                case ActionKind.Rewrite:
                    return Embedded("action-rewrite.md")
                        .Replace("{{INSTRUCTION}}", "Task: make this text meet the style guide. Fix every linter finding below, and tighten wordy, vague, or passive sentences into plain, direct instructions and statements. Sentences that are already clear and compliant stay as they are.")
                        .Replace("{{LINT}}", lintBlock)
                        .Replace("{{FRAGMENT}}", fragment);
                case ActionKind.Ask:
                    return Embedded("action-rewrite.md")
                        .Replace("{{INSTRUCTION}}", "Task (the author's instruction): " + (instruction ?? "").Trim())
                        .Replace("{{LINT}}", lintBlock)
                        .Replace("{{FRAGMENT}}", fragment);
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        /// <summary>Second round: the model's own revision still trips the linter.</summary>
        public string BuildRepair(string revisedXml, string lint)
        {
            return Embedded("action-repair.md").Replace("{{LINT}}", lint).Replace("{{FRAGMENT}}", revisedXml);
        }

        /// <summary>One proposal, re-asked with the author's instruction ("keep SSO", "shorter").</summary>
        public string BuildRefine(string context, string excerpt, string proposal, string instruction)
        {
            return Embedded("action-refine.md").Replace("{{CONTEXT}}", context ?? "").Replace("{{EXCERPT}}", excerpt ?? "")
                .Replace("{{PROPOSAL}}", proposal ?? "").Replace("{{INSTRUCTION}}", (instruction ?? "").Trim());
        }

        private static string Embedded(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("FlareAI.Core.Prompts." + name))
            {
                if (s == null) throw new InvalidOperationException("Missing embedded prompt: " + name);
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
