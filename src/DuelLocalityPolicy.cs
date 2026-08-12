using System;

namespace ErenshorDuel
{
    internal enum DuelLocalityDecision
    {
        Local,
        PlayerUnstable,
        NotLoaded,
        WrongZone
    }

    internal struct DuelLocalityInput
    {
        // Whether the player Character is alive and active. Deliberately NOT a scene
        // comparison: the player's persistent Character/PlayerControl GameObject lives in
        // Unity's DontDestroyOnLoad scene, not in the loaded Erenshor zone, so requiring
        // "player.gameObject.scene == active zone" rejects every real local Sim.
        internal bool PlayerStable;
        internal bool SimLoaded;
        internal string SimSceneName;
        internal string ActiveZoneSceneName;
    }

    // Separates "current loaded Erenshor zone" from "player's persistent Unity scene". The
    // only locality yardstick for a candidate Sim is whether its own GameObject belongs to
    // SceneManager.GetActiveScene() -- the same signal every visibly-nearby Sim already
    // satisfies. Never compare a Sim's scene against the player's own GameObject scene.
    internal static class DuelLocalityPolicy
    {
        internal static DuelLocalityDecision Evaluate(DuelLocalityInput input)
        {
            if (!input.PlayerStable) return DuelLocalityDecision.PlayerUnstable;
            if (!input.SimLoaded) return DuelLocalityDecision.NotLoaded;
            if (!string.Equals(input.SimSceneName, input.ActiveZoneSceneName, StringComparison.Ordinal))
                return DuelLocalityDecision.WrongZone;
            return DuelLocalityDecision.Local;
        }

        internal static bool IsLocal(DuelLocalityInput input) { return Evaluate(input) == DuelLocalityDecision.Local; }

        internal static string Token(DuelLocalityDecision decision)
        {
            switch (decision)
            {
                case DuelLocalityDecision.Local: return "local";
                case DuelLocalityDecision.PlayerUnstable: return "player_unstable";
                case DuelLocalityDecision.NotLoaded: return "not_loaded";
                case DuelLocalityDecision.WrongZone: return "wrong_zone";
                default: return "unknown";
            }
        }

        internal static string RunSelfTests()
        {
            DuelLocalityInput local = new DuelLocalityInput
            {
                PlayerStable = true,
                SimLoaded = true,
                SimSceneName = "Hidden",
                ActiveZoneSceneName = "Hidden"
            };
            if (Evaluate(local) != DuelLocalityDecision.Local)
                return "FAIL locality: player in DontDestroyOnLoad with Sim in the active zone must be local";

            DuelLocalityInput wrongZone = local;
            wrongZone.SimSceneName = "Brake";
            if (Evaluate(wrongZone) != DuelLocalityDecision.WrongZone)
                return "FAIL locality: Sim in a different loaded zone than the active zone must be rejected";

            DuelLocalityInput notLoaded = local;
            notLoaded.SimLoaded = false;
            if (Evaluate(notLoaded) != DuelLocalityDecision.NotLoaded)
                return "FAIL locality: unloaded/destroyed Sim GameObject must be rejected";

            DuelLocalityInput unstablePlayer = local;
            unstablePlayer.PlayerStable = false;
            if (Evaluate(unstablePlayer) != DuelLocalityDecision.PlayerUnstable)
                return "FAIL locality: unstable player state must be rejected regardless of Sim scene";

            // The player's own persistent scene never enters the comparison at all -- proven by
            // construction above, since PlayerStable carries no scene value. A regression that
            // reintroduced "sim.scene == player.scene" would fail the first case, since a real
            // local Sim's scene ("Hidden") never equals the player's persistent scene
            // ("DontDestroyOnLoad").
            return "PASS locality";
        }
    }
}
