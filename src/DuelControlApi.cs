using System;

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
        public static bool IsAvailable { get { return ErenshorDuelPlugin.Instance != null; } }
        public static bool HasDedicatedPanel { get { return false; } }
        public static bool IsPanelOpen { get { return false; } }
        public static DuelControlState GetBasicState()
        {
            return new DuelControlState { Active = DuelController.Active, CanStart = DuelController.CanStartNewDuel, Status = DuelController.Status(), EligibleNames = DuelController.EligibleNames() };
        }
        public static string GetStatus()
        {
            string[] names = DuelController.EligibleNames();
            int count = names == null ? 0 : names.Length;
            return DuelController.Active ? "Duel active" : (DuelController.CanStartNewDuel ? "Idle | " + count + " eligible local candidate(s)" : "Cleanup finishing");
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
        public static bool OpenPanel() { return false; }
        public static bool ClosePanel() { return false; }
    }
}
