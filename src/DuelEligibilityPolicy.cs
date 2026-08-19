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

        // Single source of the human-readable rejection text. DuelController.ReportEligibilityFailure
        // (chat) and the standalone Sim Actions fallback UI (inline label) both call this instead of
        // each carrying their own copy of the wording, so the two surfaces can never drift apart.
        internal static string DescribeForUi(DuelEligibilityDecision decision)
        {
            switch (decision)
            {
                case DuelEligibilityDecision.Eligible: return string.Empty;
                case DuelEligibilityDecision.RemoteCoop: return "Remote COOP humans/proxies cannot be challenged.";
                case DuelEligibilityDecision.MissingCombatComponents: return "That Sim is missing required local combat components.";
                case DuelEligibilityDecision.CampConflict: return "End Hunt Camp before starting a duel. Relax does not block friendly duels.";
                case DuelEligibilityDecision.TooFar: return "Move closer before challenging that Sim.";
                case DuelEligibilityDecision.UnsafeRealCombat: return "That challenge is unsafe while real combat is active.";
                case DuelEligibilityDecision.Dead: return "That Sim is not alive.";
                case DuelEligibilityDecision.WrongScene: return "That Sim is not in your current zone.";
                case DuelEligibilityDecision.Inactive: return "That Sim is no longer present.";
                case DuelEligibilityDecision.NotSimPlayer: return "Choose a living local SimPlayer in the current scene.";
                default: return "Choose a living local SimPlayer in the current scene.";
            }
        }

        // A hard-invalid decision means the actor reference itself is no longer usable at all (gone,
        // never loaded, or not the right kind of object) -- there is nothing to wait out. Every other
        // rejection (too far, camp conflict, unsafe combat, remote authority) is situational: the same
        // Sim reference stays meaningful and may become eligible again without picking a new target.
        // The standalone fallback UI uses this distinction to decide "cancel the arrangement and
        // explain why" versus "keep showing the same Sim with a disabled action and a live reason."
        internal static bool IsHardInvalid(DuelEligibilityDecision decision)
        {
            switch (decision)
            {
                case DuelEligibilityDecision.NotSimPlayer:
                case DuelEligibilityDecision.Inactive:
                case DuelEligibilityDecision.Dead:
                case DuelEligibilityDecision.WrongScene:
                    return true;
                default:
                    return false;
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

            if (IsHardInvalid(DuelEligibilityDecision.NotSimPlayer) != true ||
                IsHardInvalid(DuelEligibilityDecision.Inactive) != true ||
                IsHardInvalid(DuelEligibilityDecision.Dead) != true ||
                IsHardInvalid(DuelEligibilityDecision.WrongScene) != true)
                return "FAIL eligibility: gone/never-valid decisions must be hard-invalid";
            if (IsHardInvalid(DuelEligibilityDecision.RemoteCoop) || IsHardInvalid(DuelEligibilityDecision.TooFar) ||
                IsHardInvalid(DuelEligibilityDecision.CampConflict) || IsHardInvalid(DuelEligibilityDecision.UnsafeRealCombat) ||
                IsHardInvalid(DuelEligibilityDecision.MissingCombatComponents) || IsHardInvalid(DuelEligibilityDecision.Eligible))
                return "FAIL eligibility: situational/recoverable decisions must not be hard-invalid";

            if (string.IsNullOrEmpty(DescribeForUi(DuelEligibilityDecision.TooFar)) ||
                string.IsNullOrEmpty(DescribeForUi(DuelEligibilityDecision.RemoteCoop)) ||
                DescribeForUi(DuelEligibilityDecision.Eligible) != string.Empty)
                return "FAIL eligibility: DescribeForUi must give a concrete reason for every rejection and nothing for Eligible";

            return "PASS eligibility";
        }
    }
}
