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
        // Current party membership is itself authoritative proof of locality/scope for the
        // original, previously-working party-duel path. Party Sims must NOT be additionally
        // gated by the same-scene predicate the nearby non-party Sim work introduced -- that
        // predicate is scoped to the nearby non-party category only. See DuelController's
        // FindSim/NearbySummary/EvaluateEligibility for where InLocalPlayerScene/IsPartyMember
        // are computed.
        internal bool IsPartyMember;
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
            // Scene-locality is the nearby non-party category's requirement. A current party
            // member proves locality/scope by party membership itself and skips this gate --
            // restoring the original working party-duel path regardless of the same-scene predicate.
            if (!input.IsPartyMember && !input.InLocalPlayerScene) return DuelEligibilityDecision.WrongScene;
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

            // REGRESSION TEST: a current party member standing right beside the player (e.g.
            // "Dancer") must reach Eligible even when the same-scene predicate the nearby-Sim
            // work introduced would otherwise fail (InLocalPlayerScene = false, mirroring the
            // player's persistent-scene mismatch symptom that produced "eligibility=wrong_scene"
            // for every real party Sim). Party membership alone must satisfy locality/scope.
            DuelEligibilityInput partySimWrongScenePredicate = good;
            partySimWrongScenePredicate.IsPartyMember = true;
            partySimWrongScenePredicate.InLocalPlayerScene = false;
            if (Evaluate(partySimWrongScenePredicate) != DuelEligibilityDecision.Eligible)
                return "FAIL eligibility: party Sim must not be gated by the nearby-Sim scene predicate";

            // Nearby non-party Sim in a different loaded zone must still be rejected.
            DuelEligibilityInput nonPartyWrongScene = good;
            nonPartyWrongScene.IsPartyMember = false;
            nonPartyWrongScene.InLocalPlayerScene = false;
            if (Evaluate(nonPartyWrongScene) != DuelEligibilityDecision.WrongScene)
                return "FAIL eligibility: non-party Sim in another zone must still be rejected";

            DuelEligibilityInput missing = good;
            missing.HasCombatComponents = false;
            if (Evaluate(missing) != DuelEligibilityDecision.MissingCombatComponents)
                return "FAIL eligibility: required combat components";

            return "PASS eligibility";
        }
    }
}
