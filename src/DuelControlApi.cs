using System;
using ForgottenRoads.StandaloneUi;

namespace ErenshorDuel
{
    public sealed class DuelControlState
    {
        public bool Active;
        public bool CanStart;
        public string Status;
        public string[] EligibleNames;
    }

    public static class DuelControlApi
    {
        public const int ApiVersion = 1;
        public const string ModuleId = "duel";
        public static bool IsAvailable { get { return ErenshorDuelPlugin.Instance != null && ErenshorDuelPlugin.Instance.RuntimeHooksReady; } }
        public static bool HasDedicatedPanel { get { return true; } }
        public static bool IsPanelOpen { get { return StandaloneFallbackUi.IsOpen; } }
        public static DuelControlState GetBasicState()
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null || !plugin.RuntimeHooksReady)
                return new DuelControlState { Active = false, CanStart = false, Status = GetStatus(), EligibleNames = new string[0] };
            return new DuelControlState { Active = DuelController.Active, CanStart = DuelController.CanStartNewDuel, Status = DuelController.Status(), EligibleNames = DuelController.EligibleNames() };
        }
        // Names to surface in full before falling back to a bare count. This mod has no dedicated
        // Hub panel with per-Sim buttons (see StandaloneFallbackUi's fixed action list), so this
        // status line is the standalone fallback's only way to make eligible Sims discoverable.
        private const int MaxNamedEligible = 6;

        public static string GetStatus()
        {
            ErenshorDuelPlugin plugin = ErenshorDuelPlugin.Instance;
            if (plugin == null) return "Practice Duels unavailable";
            if (!plugin.RuntimeHooksReady) return "Compatibility unavailable" + (string.IsNullOrWhiteSpace(plugin.RuntimeHookFailure) ? string.Empty : " (" + plugin.RuntimeHookFailure + ")");
            if (DuelController.Active) return "Duel active";
            if (!DuelController.CanStartNewDuel) return "Cleanup finishing";
            string[] names = DuelController.EligibleNames();
            int count = names == null ? 0 : names.Length;
            if (count == 0) return "Idle | 0 eligible local candidate(s)";
            return "Idle | " + count + " eligible: " + DescribeEligible(names);
        }

        private static string DescribeEligible(string[] names)
        {
            int shown = Math.Min(names.Length, MaxNamedEligible);
            string joined = string.Join(", ", names, 0, shown);
            return names.Length > shown ? joined + ", +" + (names.Length - shown) + " more" : joined;
        }
        public static bool TryChallenge(string simName)
        {
            ErenshorDuelPlugin p = ErenshorDuelPlugin.Instance;
            return p != null && p.RequestControlChallenge(simName);
        }
        public static bool TryStop()
        {
            ErenshorDuelPlugin p = ErenshorDuelPlugin.Instance;
            if (p == null) return false;
            if (!DuelController.Active) return true;
            return p.RequestControlStop();
        }
        public static bool OpenPanel() { return StandaloneFallbackUi.Open(); }
        public static bool ClosePanel() { return StandaloneFallbackUi.Close(); }
    }
}
