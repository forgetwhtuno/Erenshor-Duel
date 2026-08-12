using System;

namespace ErenshorDuel
{
    internal enum DuelOutsideEffectDisposition
    {
        Allow,
        Block,
        Cancel
    }

    internal static class DuelSafetyPolicy
    {
        internal static bool AllowPreExistingPetEngagement(bool fighting, bool actorIsDuelist,
            bool petWasPresentAtStart, bool ownerIsDuelist, bool targetsOpponent)
        {
            return fighting && !actorIsDuelist && petWasPresentAtStart && ownerIsDuelist && targetsOpponent;
        }

        internal static bool AllowGroupAssistCall(bool duelActive, bool requestedTargetIsDuelist)
        {
            return !duelActive || !requestedTargetIsDuelist;
        }

        internal static DuelOutsideEffectDisposition OutsideEffect(bool targetIsDuelist,
            bool sourceFriendly, bool sourceOutsideHostile, bool sourceUnknown)
        {
            if (!targetIsDuelist) return DuelOutsideEffectDisposition.Allow;
            if (sourceOutsideHostile) return DuelOutsideEffectDisposition.Cancel;
            if (sourceFriendly || sourceUnknown) return DuelOutsideEffectDisposition.Block;
            return DuelOutsideEffectDisposition.Allow;
        }

        internal static bool CancelForSceneMismatch(bool duelActive, bool playerSceneMatchesStart)
        {
            return duelActive && !playerSceneMatchesStart;
        }

        internal static bool ShouldRunCleanup(bool active, bool hasResidualParticipantState)
        {
            return active || hasResidualParticipantState;
        }

        internal static bool ShouldEmitTerminalEvent(bool wasActive)
        {
            return wasActive;
        }

        internal static bool ShouldRestorePreviousTarget(bool previousTargetAlive, bool previousTargetIsDuelist)
        {
            return previousTargetAlive && !previousTargetIsDuelist;
        }

        internal static int ApplyVirtualDamageOnce(int virtualHp, int damage)
        {
            return Math.Max(1, virtualHp - Math.Max(0, damage));
        }

        internal static bool ReachedYieldThreshold(int virtualHp, int maximumHp, int percent)
        {
            return maximumHp > 0 && virtualHp * 100 <= maximumHp * percent;
        }

        internal static bool ThirdPartyHealChangesVirtualHealth() { return false; }

        internal static string RunSelfTests()
        {
            if (!AllowPreExistingPetEngagement(true, false, true, true, true))
                return "FAIL safety: pre-existing duel pet routing";
            if (AllowPreExistingPetEngagement(true, false, false, true, true))
                return "FAIL safety: newly created pet admitted";
            if (AllowPreExistingPetEngagement(true, false, true, true, false))
                return "FAIL safety: duel pet allowed outside opponent";

            if (AllowGroupAssistCall(true, true) || !AllowGroupAssistCall(true, false))
                return "FAIL safety: group assist suppression";

            if (OutsideEffect(true, false, true, false) != DuelOutsideEffectDisposition.Cancel)
                return "FAIL safety: outside hostile effect must cancel";
            if (OutsideEffect(true, false, false, true) != DuelOutsideEffectDisposition.Block)
                return "FAIL safety: unsupported unknown effect must block";
            if (OutsideEffect(false, false, true, false) != DuelOutsideEffectDisposition.Allow)
                return "FAIL safety: unrelated outside effect should remain vanilla";

            if (!CancelForSceneMismatch(true, false) || CancelForSceneMismatch(true, true))
                return "FAIL safety: post-zone cancellation";

            if (!ShouldRunCleanup(true, false) || !ShouldRunCleanup(false, true) || ShouldRunCleanup(false, false))
                return "FAIL safety: cleanup gate";
            if (!ShouldEmitTerminalEvent(true) || ShouldEmitTerminalEvent(false))
                return "FAIL safety: terminal event idempotence";

            if (ShouldRestorePreviousTarget(true, true) || !ShouldRestorePreviousTarget(true, false))
                return "FAIL safety: terminal cleanup must not restore duel target";
            if (ApplyVirtualDamageOnce(745, 506) != 239 || ApplyVirtualDamageOnce(239, 506) != 1)
                return "FAIL safety: virtual damage must be applied once per event";
            if (ReachedYieldThreshold(239, 3146, 5) || !ReachedYieldThreshold(157, 3146, 5))
                return "FAIL safety: virtual yield threshold";
            if (ThirdPartyHealChangesVirtualHealth())
                return "FAIL safety: third-party heal changed virtual health";

            return "PASS safety";
        }
    }
}
