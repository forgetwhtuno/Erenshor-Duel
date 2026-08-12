using System;
using System.Collections.Generic;

namespace ErenshorDuel
{
    internal static class DuelIdentity
    {
        internal static string BuildKey(string displayName, bool hasTrackingIndex, int trackingIndex)
        {
            if (hasTrackingIndex && trackingIndex >= 0)
                return "simindex:" + trackingIndex;

            // Conservative fallback only. Runtime code probes the user-verified
            // SimPlayer.MySimTracking -> SimPlayerTracking.simIndex shape before using an index.
            string name = (displayName ?? string.Empty).Trim().ToLowerInvariant();
            return "name:" + name;
        }

        internal static string RunSelfTests()
        {
            string first = BuildKey("Same Name", true, 101);
            string second = BuildKey("Same Name", true, 102);
            if (string.Equals(first, second, StringComparison.Ordinal))
                return "FAIL identity: distinct tracking indices collapsed";

            Dictionary<string, float> cooldowns = new Dictionary<string, float>(StringComparer.Ordinal);
            cooldowns[first] = 1f;
            if (cooldowns.ContainsKey(second))
                return "FAIL identity: cooldown storage collapsed same-name distinct Sims";

            if (!string.Equals(BuildKey("Dancer", true, 7), BuildKey("Different Display", true, 7), StringComparison.Ordinal))
                return "FAIL identity: stable index should outrank display name";

            if (!string.Equals(BuildKey(" Dancer ", false, -1), BuildKey("dancer", false, -1), StringComparison.Ordinal))
                return "FAIL identity: name fallback normalization";

            return "PASS identity";
        }
    }
}
