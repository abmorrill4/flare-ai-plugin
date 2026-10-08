using FlareAI.Core.Style;

namespace FlareAI.Core.Actions
{
    /// <summary>Outcome of checking one prose rule's examples against the AI reviewer. Null = no such example.</summary>
    public sealed class ExampleResult
    {
        public GuideExample Example { get; set; }
        public bool? BadCaught { get; set; }
        public bool? GoodClean { get; set; }
        public bool Passed { get { return BadCaught != false && GoodClean != false; } }
    }
}
