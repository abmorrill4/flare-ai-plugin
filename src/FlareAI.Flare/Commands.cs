using System.Collections.Generic;

namespace FlareAI.Flare
{
    /// <summary>One command: the same label, tooltip, key tip and icon wherever it appears.</summary>
    internal sealed class CommandInfo
    {
        public string Id, Label, Tooltip, KeyTip, Icon, Group;
        /// <summary>The label in Flare's right-click menu, which also holds Flare's own items.</summary>
        public string ContextLabel { get { return "House Style: " + Label; } }
    }

    /// <summary>
    /// The one place command text lives. The ribbon, the Tool Strip menu and toolbar, and the editor's right-click menu
    /// are all built from this table, so a command cannot be named differently in two places. Adding a command here
    /// (and a handler in Plugin.cs) is all it takes; there is no limit on how many commands there are.
    /// </summary>
    internal static class Commands
    {
        public const string GroupAssist = "Assist", GroupGuide = "Style guide", GroupOutput = "Output", GroupSettings = "Settings";

        public static readonly CommandInfo CheckStyle = new CommandInfo
        {
            Id = "check", Label = "Check style", KeyTip = "C", Icon = "check", Group = GroupAssist,
            Tooltip = "Check this topic against the style guide and list the suggestions."
        };
        public static readonly CommandInfo Rewrite = new CommandInfo
        {
            Id = "rewrite", Label = "Rewrite selection", KeyTip = "R", Icon = "rewrite", Group = GroupAssist,
            Tooltip = "Ask the AI to rewrite the selected text. You see the change before it is written."
        };
        public static readonly CommandInfo Ask = new CommandInfo
        {
            Id = "ask", Label = "Ask…", KeyTip = "A", Icon = "ask", Group = GroupAssist,
            Tooltip = "Give the AI an instruction for the selected text, such as \"make this shorter\"."
        };
        public static readonly CommandInfo CheckProject = new CommandInfo
        {
            Id = "project", Label = "Check project", KeyTip = "P", Icon = "project", Group = GroupAssist,
            Tooltip = "Check every topic and snippet in the project against the style guide. Free: no AI is used."
        };
        public static readonly CommandInfo TopicAssists = new CommandInfo
        {
            Id = "assist", Label = "Topic assists", KeyTip = "I", Icon = "assist", Group = GroupAssist,
            Tooltip = "Draft a description, index keywords, synonyms, micro content and glossary terms for this topic."
        };
        public static readonly CommandInfo ValidateGuide = new CommandInfo
        {
            Id = "validate", Label = "Validate style guide", KeyTip = "V", Icon = "validate", Group = GroupGuide,
            Tooltip = "Test the style guide's rules against their examples."
        };
        public static readonly CommandInfo StyleRules = new CommandInfo
        {
            Id = "rules", Label = "Style rules", KeyTip = "U", Icon = "rules", Group = GroupGuide,
            Tooltip = "See every rule in the style guide, search and filter them, and turn rules off or back on."
        };
        public static readonly CommandInfo TuneGuide = new CommandInfo
        {
            Id = "tune", Label = "Tune style guide", KeyTip = "T", Icon = "tune", Group = GroupGuide,
            Tooltip = "See what your team's review decisions say about each rule, and change the rules."
        };
        public static readonly CommandInfo BuildGuide = new CommandInfo
        {
            Id = "build", Label = "Build style guide", KeyTip = "B", Icon = "build", Group = GroupGuide,
            Tooltip = "Draft new rules from your project's content and from your notes."
        };
        public static readonly CommandInfo ExportLlm = new CommandInfo
        {
            Id = "llm", Label = "Export for LLMs", KeyTip = "L", Icon = "llm", Group = GroupOutput,
            Tooltip = "Add Markdown for each topic, plus llms.txt, to a built HTML output."
        };
        public static readonly CommandInfo Settings = new CommandInfo
        {
            Id = "settings", Label = "Settings", KeyTip = "S", Icon = "settings", Group = GroupSettings,
            Tooltip = "Choose the AI provider and key, the style guide, and what House Style does on its own."
        };
        public static readonly CommandInfo Diagnostics = new CommandInfo
        {
            Id = "diag", Label = "Diagnostics", KeyTip = "D", Icon = "diag", Group = GroupSettings,
            Tooltip = "Show what House Style sees in the open topic, to copy into a support request."
        };

        /// <summary>Ribbon order. Groups appear in the order of their first command.</summary>
        public static readonly IList<CommandInfo> All = new List<CommandInfo>
        {
            CheckStyle, Rewrite, Ask, CheckProject, TopicAssists,
            StyleRules, ValidateGuide, TuneGuide, BuildGuide,
            ExportLlm,
            Settings, Diagnostics
        };

        /// <summary>The commands that need an open topic and a selection or paragraph, offered on right-click.</summary>
        public static readonly IList<CommandInfo> Context = new List<CommandInfo> { CheckStyle, Rewrite, Ask };

        /// <summary>The Tool Strip menu, in order; null is a separator.</summary>
        public static readonly IList<CommandInfo> Menu = new List<CommandInfo>
        {
            CheckStyle, Rewrite, Ask, CheckProject, TopicAssists, null,
            StyleRules, ValidateGuide, TuneGuide, BuildGuide, null,
            ExportLlm, null,
            Settings, Diagnostics
        };

        /// <summary>The Tool Strip toolbar (the most used commands).</summary>
        public static readonly IList<CommandInfo> Toolbar = new List<CommandInfo> { CheckStyle, Rewrite, Ask };
    }
}
