using System;
using System.Collections.Generic;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// A batch of fixes being written, one file per <see cref="Step"/>: each file is one undo step, as tracked changes.
    /// The pane steps it between messages so it can show progress and stop between files; tests and small batches
    /// step it to the end at once. A stopped batch leaves the remaining files untouched and says so.
    /// </summary>
    public sealed class BatchRun
    {
        private readonly ReviewSession _session;
        private readonly List<List<StyleFinding>> _files;
        private readonly string _operation;
        private readonly bool _reachesOtherFiles;
        private int _next;
        private bool _cancelled;

        internal BatchRun(ReviewSession session, List<List<StyleFinding>> files, string operation, BatchPlan plan, bool reachesOtherFiles)
        {
            _session = session; _files = files; _operation = operation; _reachesOtherFiles = reachesOtherFiles;
            Result = new BatchResult();
            if (plan != null) foreach (var sk in plan.Skipped) Result.AddSkipped(sk.Path, sk.Fixes, sk.Reason);
        }

        /// <summary>What happened so far, file by file.</summary>
        public BatchResult Result { get; private set; }

        /// <summary>Files the batch writes to.</summary>
        public int FileCount { get { return _files.Count; } }

        /// <summary>Files already written (or attempted).</summary>
        public int FilesDone { get { return _next; } }

        /// <summary>The file the next <see cref="Step"/> writes, or null when none is left.</summary>
        public string NextFile { get { return _next < _files.Count ? _session.FilePathOf(_files[_next][0]) : null; } }

        public bool IsCancelled { get { return _cancelled; } }
        public bool IsFinished { get; private set; }

        /// <summary>Raised once, after the last file (or after a stop), with the per-file outcome.</summary>
        public event Action<BatchResult> Finished;

        /// <summary>Stops before the next file. The file being written is finished first; nothing is half-written.</summary>
        public void Cancel() { _cancelled = true; }

        /// <summary>Writes the next file. Returns true while files remain; false once the batch has finished.</summary>
        public bool Step()
        {
            if (IsFinished) return false;
            if (!_cancelled && _next < _files.Count)
            {
                _session.ApplyFile(_files[_next], _operation, Result);
                _next++;
                if (_next < _files.Count && !_cancelled) return true;
            }
            Finish();
            return false;
        }

        /// <summary>Writes every remaining file now.</summary>
        public BatchResult RunToEnd()
        {
            while (Step()) { }
            return Result;
        }

        private void Finish()
        {
            IsFinished = true;
            for (var i = _next; i < _files.Count; i++) Result.AddNotStarted(_session.FilePathOf(_files[i][0]), _files[i].Count);
            _session.FinishBatch(Result, _reachesOtherFiles);
            var h = Finished;
            if (h != null) h(Result);
        }
    }
}
