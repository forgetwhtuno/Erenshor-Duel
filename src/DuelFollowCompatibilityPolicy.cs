using System;

namespace ErenshorDuel
{
    internal enum FollowHealthClassification
    {
        Healthy,
        Absent,
        Unhealthy
    }

    // Pure classification of Follow's FollowControlApi.GetStatus() string. Kept separate from
    // DuelFollowCompatibility (which does the actual reflection/AppDomain work) so the classification
    // rule itself -- the exact thing that decides whether Duel's fallback stands down -- is testable
    // without a loaded Follow assembly or Unity.
    internal static class DuelFollowCompatibilityPolicy
    {
        // FollowControlApi.GetStatus() returns exactly "Follow unavailable" when ErenshorFollowPlugin
        // is not loaded, and starts with "Compatibility unavailable" when it is loaded but its runtime
        // hooks failed. Every other value means the plugin is loaded and functional -- Follow's Sim
        // Actions system runs under the same readiness gate as the rest of that plugin's Update loop.
        private const string AbsentStatus = "Follow unavailable";
        private const string UnhealthyPrefix = "Compatibility unavailable";

        internal static FollowHealthClassification ClassifyStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return FollowHealthClassification.Absent;
            if (string.Equals(status, AbsentStatus, StringComparison.Ordinal)) return FollowHealthClassification.Absent;
            if (status.StartsWith(UnhealthyPrefix, StringComparison.Ordinal)) return FollowHealthClassification.Unhealthy;
            return FollowHealthClassification.Healthy;
        }

        internal static string RunSelfTests()
        {
            if (ClassifyStatus(null) != FollowHealthClassification.Absent)
                return "FAIL follow-compat: null status classifies as Absent";
            if (ClassifyStatus(string.Empty) != FollowHealthClassification.Absent)
                return "FAIL follow-compat: empty status classifies as Absent";
            if (ClassifyStatus("Follow unavailable") != FollowHealthClassification.Absent)
                return "FAIL follow-compat: exact absent sentinel classifies as Absent";
            if (ClassifyStatus("Compatibility unavailable (InvalidOperationException)") != FollowHealthClassification.Unhealthy)
                return "FAIL follow-compat: hook-failure sentinel classifies as Unhealthy";
            if (ClassifyStatus("Travel idle") != FollowHealthClassification.Healthy)
                return "FAIL follow-compat: an ordinary live status classifies as Healthy";
            if (ClassifyStatus("Expedition: Traveling -> Hidden") != FollowHealthClassification.Healthy)
                return "FAIL follow-compat: an active-expedition status classifies as Healthy";
            return "PASS follow-compat";
        }
    }
}
