using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlareAI.Core.Update
{
    /// <summary>
    /// A semantic version (<c>1.2.3</c>, <c>v1.2.3-beta.2</c>, <c>1.2.3+abc</c>) ordered by SemVer 2.0 rules: a pre-release
    /// sorts before its release, numeric identifiers compare as numbers, build metadata is ignored. A missing minor or
    /// patch counts as 0 ("1.2" = "1.2.0").
    /// </summary>
    public sealed class SemVer : IComparable<SemVer>
    {
        public int Major { get; private set; }
        public int Minor { get; private set; }
        public int Patch { get; private set; }
        /// <summary>The pre-release identifiers ("beta", "2"); empty for a release.</summary>
        public string[] PreRelease { get; private set; }

        public bool IsPreRelease { get { return PreRelease.Length > 0; } }

        /// <summary>Parses a version or tag; null when it isn't one.</summary>
        public static SemVer Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var s = text.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
            var plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);
            var pre = new string[0];
            var dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1).Split('.');
                s = s.Substring(0, dash);
                foreach (var p in pre) if (p.Length == 0) return null;
            }
            var parts = s.Split('.');
            if (parts.Length < 1 || parts.Length > 4) return null;   // 4: a .NET assembly version "1.2.3.0"
            var nums = new int[3];
            for (int i = 0; i < parts.Length; i++)
            {
                int n;
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n)) return null;
                if (i < 3) nums[i] = n;
            }
            return new SemVer { Major = nums[0], Minor = nums[1], Patch = nums[2], PreRelease = pre };
        }

        /// <summary>Negative when <paramref name="a"/> is older than <paramref name="b"/>; an unparsable version is the oldest.</summary>
        public static int Compare(string a, string b)
        {
            var x = Parse(a); var y = Parse(b);
            if (x == null) return y == null ? 0 : -1;
            return y == null ? 1 : x.CompareTo(y);
        }

        public int CompareTo(SemVer other)
        {
            if (other == null) return 1;
            int c = Major.CompareTo(other.Major); if (c != 0) return c;
            c = Minor.CompareTo(other.Minor); if (c != 0) return c;
            c = Patch.CompareTo(other.Patch); if (c != 0) return c;
            if (!IsPreRelease) return other.IsPreRelease ? 1 : 0;
            if (!other.IsPreRelease) return -1;
            for (int i = 0; i < Math.Min(PreRelease.Length, other.PreRelease.Length); i++)
            {
                c = ComparePart(PreRelease[i], other.PreRelease[i]);
                if (c != 0) return c;
            }
            return PreRelease.Length.CompareTo(other.PreRelease.Length);
        }

        private static int ComparePart(string a, string b)
        {
            long x, y;
            bool nx = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out x);
            bool ny = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out y);
            if (nx && ny) return x.CompareTo(y);
            if (nx) return -1;   // numeric identifiers sort before alphanumeric ones
            if (ny) return 1;
            return string.CompareOrdinal(a, b);
        }

        public override string ToString()
        {
            var core = Major + "." + Minor + "." + Patch;
            return IsPreRelease ? core + "-" + string.Join(".", PreRelease) : core;
        }
    }
}
