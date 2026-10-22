using System;

namespace FlareAI.TestKit.Workspace
{
    /// <summary>
    /// A clock tests move by hand. TestKit stamps tracked changes and cassettes with it; Core still reads the real clock
    /// (it has no clock seam), so assert on Core timestamps by range, not equality.
    /// </summary>
    public sealed class FakeClock
    {
        private DateTime _now;
        private readonly object _gate = new object();

        public FakeClock() : this(new DateTime(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc)) { }
        public FakeClock(DateTime start) { _now = start.Kind == DateTimeKind.Utc ? start : start.ToUniversalTime(); }

        public DateTime UtcNow { get { lock (_gate) return _now; } }

        /// <summary>Moves the clock forward and returns the new time.</summary>
        public DateTime Advance(TimeSpan by)
        {
            lock (_gate) { _now = _now.Add(by); return _now; }
        }

        /// <summary>The current time, then a tick forward: successive stamps are distinct and ordered.</summary>
        public DateTime Tick()
        {
            lock (_gate) { var t = _now; _now = _now.AddMilliseconds(1); return t; }
        }
    }
}
