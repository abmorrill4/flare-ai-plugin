using System;

namespace FlareAI.TestKit.Conformance
{
    /// <summary>A conformance expectation that did not hold.</summary>
    public sealed class ConformanceFailure : Exception
    {
        public ConformanceFailure(string message) : base(message) { }
    }

    /// <summary>Framework-free assertions, so the suite runs inside Flare as well as under xUnit.</summary>
    public static class Check
    {
        public static void That(bool condition, string message)
        {
            if (!condition) throw new ConformanceFailure(message);
        }

        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual))
                throw new ConformanceFailure(what + ": expected <" + expected + "> but was <" + actual + ">");
        }

        /// <summary>Runs <paramref name="action"/> and returns the exception of type <typeparamref name="T"/> it threw.</summary>
        public static T Throws<T>(Action action, string what) where T : Exception
        {
            try { action(); }
            catch (T ex) { return ex; }
            catch (Exception ex) { throw new ConformanceFailure(what + ": expected " + typeof(T).Name + " but got " + ex.GetType().Name + ": " + ex.Message); }
            throw new ConformanceFailure(what + ": expected " + typeof(T).Name + " but nothing was thrown");
        }
    }
}
