using System.Collections.Generic;
using FlareAI.Core.Editing;

namespace FlareAI.TestKit.Conformance
{
    /// <summary>
    /// An editor under test: the adapter plus the editor controls an author has (Undo, Redo, Review &gt; Accept/Reject All).
    /// <see cref="Editor.FlareSimDocument"/> implements it; the in-Flare self-test (#82) wraps FlareDocumentAdapter in it.
    /// </summary>
    public interface IConformanceDocument : IDocumentAdapter
    {
        void Undo();
        void Redo();
        void AcceptAll();
        void RejectAll();
        int UndoCount { get; }
    }

    /// <summary>Opens a fresh scratch topic with the given markup in the editor under test.</summary>
    public delegate IConformanceDocument OpenScratch(string xhtml);
}
