namespace ErenshorDuel
{
    internal enum DuelEligibilityDecision
    {
        Eligible,
        NotSimPlayer,
        Inactive,
        WrongScene,
        Dead,
        RemoteCoop,
        MissingCombatComponents,
        CampConflict,
        TooFar,
        UnsafeRealCombat
    }

    internal struct DuelEligibilityInput
    {
        internal bool IsSimPlayer;
        internal bool ActiveInHierarchy;
        internal bool InLocalPlayerScene;
        internal bool Alive;
        internal bool RemoteCoop;
        internal bool HasCombatComponents;
        internal bool CampConflict;
        internal float Distance;
        internal float MaximumDistance;
        internal bool UnsafeRealCombat;
    }

    internal static class DuelEligibilityPolicy
    {
        internal static DuelEligibilityDecision Evaluate(DuelEligibilityInput input)
        {
            if (!input.IsSimPlayer) return DuelEligibilityDecision.NotSimPlayer;
            if (!input.ActiveInHierarchy) return DuelEligibilityDecision.Inactive;
            if (!input.InLocalPlayerScene) return DuelEligibilityDecision.WrongScene;
            if (!input.Alive) return DuelEligibilityDecision.Dead;
            if (input.RemoteCoop) return DuelEligibilityDecision.RemoteCoop;
            if (!input.HasCombatComponents) return DuelEligibilityDecision.MissingCombatComponents;
            if (input.CampConflict) return DuelEligibilityDecision.CampConflict;
            if (input.MaximumDistance > 0f && input.Distance > input.MaximumDistance) return DuelEligibilityDecision.TooFar;
            if (input.UnsafeRealCombat) return DuelEligibilityDecision.UnsafeRealCombat;
            return DuelEligibilityDecision.Eligible;
        }

        internal static string Token(DuelEligibilityDecision decision)
        {
            switch (decision)
            {
                case DuelEligibilityDecision.Eligible: return "eligible";
                case DuelEligibilityDecision.NotSimPlayer: return "not_simplayer";
                case DuelEligibilityDecision.Inactive: return "inactive";
                case DuelEligibilityDecision.WrongScene: return "wrong_scene";
                case DuelEligibilityDecision.Dead: return "dead";
                case DuelEligibilityDecision.RemoteCoop: return "remote_coop";
                case DuelEligibilityDecision.MissingCombatComponents: return "missing_combat_components";
                case DuelEligibilityDecision.CampConflict: return "camp_conflict";
                case DuelEligibilityDecision.TooFar: return "too_far";
                case DuelEligibilityDecision.UnsafeRealCombat: return "real_combat";
                default: return "unknown";
            }
        }

        internal static string RunSelfTests()
        {
            DuelEligibilityInput good = new DuelEligibilityInput
            {
                IsSimPlayer = true,
                ActiveInHierarchy = true,
                InLocalPlayerScene = true,
                Alive = true,
                HasCombatComponents = true,
                Distance = 10f,
                MaximumDistance = 25f
            };
            if (Evaluate(good) != DuelEligibilityDecision.Eligible)
                return "FAIL eligibility: valid local Sim";

            DuelEligibilityInput ordinaryNpc = good;
            ordinaryNpc.IsSimPlayer = false;
            if (Evaluate(ordinaryNpc) != DuelEligibilityDecision.NotSimPlayer)
                return "FAIL eligibility: ordinary NPC rejection";

            DuelEligibilityInput remote = good;
            remote.RemoteCoop = true;
            if (Evaluate(remote) != DuelEligibilityDecision.RemoteCoop)
                return "FAIL eligibility: remote COOP rejection";

            DuelEligibilityInput tooFar = good;
            tooFar.Distance = 25.01f;
            if (Evaluate(tooFar) != DuelEligibilityDecision.TooFar)
                return "FAIL eligibility: challenge distance";

            DuelEligibilityInput combat = good;
            combat.UnsafeRealCombat = true;
            if (Evaluate(combat) != DuelEligibilityDecision.UnsafeRealCombat)
                return "FAIL eligibility: real combat rejection";

            DuelEligibilityInput wrongScene = good;
            wrongScene.InLocalPlayerScene = false;
            if (Evaluate(wrongScene) != DuelEligibilityDecision.WrongScene)
                return "FAIL eligibility: same loaded player-scene requirement";

            DuelEligibilityInput missing = good;
            missing.HasCombatComponents = false;
            if (Evaluate(missing) != DuelEligibilityDecision.MissingCombatComponents)
                return "FAIL eligibility: required combat components";

            return "PASS eligibility";
        }
    }
}
