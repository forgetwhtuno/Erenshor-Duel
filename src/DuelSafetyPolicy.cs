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
            // Hostile world combat is intentionally allowed to overlap Practice Duel. Friendly or
            // unresolved third-party effects remain contained so they cannot assist/interfere.
            if (sourceOutsideHostile) return DuelOutsideEffectDisposition.Allow;
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

        // Whether an ARMED post-duel cleanup pass should finalize (call EndPostDuelAttackCleanup)
        // on this tick, given its remaining frame budget and time deadline. Deliberately NOT also
        // usable as the "is anything pending" gate at the top of RunPostDuelAttackCleanup:
        // remainingFrames reaches 0 within a handful of ordinary frames, long before the time
        // deadline, so by the frame the deadline is finally reached remainingFrames is already
        // <=0. Using this exact condition as BOTH the top fast-exit guard AND the bottom finalize
        // decision (the original 0.4.7/0.4.9 shape) meant the fast-exit guard silently returned on
        // the one frame that should have finalized - on every ordinary-speed duel, forever, since
        // nothing else ever called EndPostDuelAttackCleanup() again until application shutdown.
        // RunPostDuelAttackCleanup now gates on an explicit _postDuelCleanupPending flag instead,
        // and calls this function only to decide the actual finalize moment.
        internal static bool ShouldFinalizeCleanupPass(int remainingFrames, float now, float until)
        {
            return remainingFrames <= 0 && now >= until;
        }

        // Starting a practice duel while native autoattack is already engaged would make terminal
        // cleanup own a combat loop that predates the duel. Fail closed instead of trying to
        // reconstruct that unrelated combat state afterward.
        internal static bool CanStartWithPreExistingAutoAttack(bool capabilityAvailable, bool autoAttackActive)
        {
            return capabilityAvailable && !autoAttackActive;
        }

        internal static bool PartyScopeStillMatches(bool wasPartyMemberAtStart, bool isPartyMemberNow)
        {
            return wasPartyMemberAtStart == isPartyMemberNow;
        }

        // NPC targets are actively pinned to the duel opponent while the match owns combat.
        // Restore a saved pre-duel target only when the current target is still one of those
        // duel-owned values. If native gameplay has already selected a third actor (or cleared
        // the target), preserve that newer external state instead of replaying a stale snapshot.
        internal static bool ShouldRestorePreviousNpcTarget(bool currentTargetIsDuelOwned,
            bool previousTargetAlive, bool previousTargetIsDuelist)
        {
            return currentTargetIsDuelOwned && previousTargetAlive && !previousTargetIsDuelist;
        }

        // The short terminal cleanup window exists to extinguish a stale attack loop against the
        // just-finished duel. It must not suppress a new, unrelated combat target selected after
        // the duel ended. Null/duelist targets remain duel-owned cleanup territory.
        internal static bool ShouldSuppressPostDuelAutoAttack(bool currentTargetIsDuelOwned, bool currentTargetIsNull)
        {
            return currentTargetIsDuelOwned || currentTargetIsNull;
        }

        // Duel temporarily removes its participants from nearby-enemy candidate lists. Cleanup may
        // re-add entries that existed before the duel, but must never remove a membership that
        // appeared during the duel because that can be new native/external combat state.
        internal static bool ShouldRestoreInitialEnemyMembership(bool existedAtStart, bool existsNow)
        {
            return existedAtStart && !existsNow;
        }

        // Exact hostile-world actors are allowed to overlap the duel and stay native. Friendly or
        // unresolved direct ingress remains blocked; unknown does not get promoted to a hostile
        // world actor merely because it reached a duelist.
        internal static DuelOutsideEffectDisposition DirectHostileIngress(bool targetIsDuelist,
            bool sourceFriendly, bool sourceOutsideHostile, bool sourceUnknown)
        {
            if (!targetIsDuelist) return DuelOutsideEffectDisposition.Allow;
            if (sourceOutsideHostile) return DuelOutsideEffectDisposition.Allow;
            if (sourceFriendly || sourceUnknown) return DuelOutsideEffectDisposition.Block;
            return DuelOutsideEffectDisposition.Allow;
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

            if (OutsideEffect(true, false, true, false) != DuelOutsideEffectDisposition.Allow)
                return "FAIL safety: outside hostile effect must remain native";
            if (OutsideEffect(true, false, false, true) != DuelOutsideEffectDisposition.Block)
                return "FAIL safety: unsupported unknown effect must block";
            if (OutsideEffect(false, false, true, false) != DuelOutsideEffectDisposition.Allow)
                return "FAIL safety: unrelated outside effect should remain vanilla";

            if (!CancelForSceneMismatch(true, false) || CancelForSceneMismatch(true, true))
                return "FAIL safety: post-zone cancellation";

            if (!ShouldRunCleanup(true, false) || !ShouldRunCleanup(false, true) || ShouldRunCleanup(false, false))
                return "FAIL safety: cleanup gate";
            if (!CanStartWithPreExistingAutoAttack(true, false) || CanStartWithPreExistingAutoAttack(true, true) ||
                CanStartWithPreExistingAutoAttack(false, false))
                return "FAIL safety: pre-existing native autoattack capability must fail closed";
            if (!PartyScopeStillMatches(true, true) || !PartyScopeStillMatches(false, false) ||
                PartyScopeStillMatches(true, false) || PartyScopeStillMatches(false, true))
                return "FAIL safety: party scope transition";
            if (!ShouldRestorePreviousNpcTarget(true, true, false) ||
                ShouldRestorePreviousNpcTarget(false, true, false) ||
                ShouldRestorePreviousNpcTarget(true, true, true))
                return "FAIL safety: NPC target ownership restore";
            if (!ShouldSuppressPostDuelAutoAttack(true, false) ||
                !ShouldSuppressPostDuelAutoAttack(false, true) ||
                ShouldSuppressPostDuelAutoAttack(false, false))
                return "FAIL safety: post-duel autoattack ownership";
            if (!ShouldRestoreInitialEnemyMembership(true, false) ||
                ShouldRestoreInitialEnemyMembership(false, true) ||
                ShouldRestoreInitialEnemyMembership(false, false))
                return "FAIL safety: nearby-enemy cleanup must be additive only";
            if (DirectHostileIngress(true, false, false, true) != DuelOutsideEffectDisposition.Block ||
                DirectHostileIngress(true, false, true, false) != DuelOutsideEffectDisposition.Allow ||
                DirectHostileIngress(true, true, false, false) != DuelOutsideEffectDisposition.Block ||
                DirectHostileIngress(false, false, true, false) != DuelOutsideEffectDisposition.Allow)
                return "FAIL safety: direct hostile ingress/world overlap";
            if (!ShouldEmitTerminalEvent(true) || ShouldEmitTerminalEvent(false))
                return "FAIL safety: terminal event idempotence";

            if (ShouldRestorePreviousTarget(true, true) || !ShouldRestorePreviousTarget(true, false))
                return "FAIL safety: terminal cleanup must not restore duel target";
            if (ApplyVirtualDamageOnce(745, 506) != 239 || ApplyVirtualDamageOnce(239, 506) != 1)
                return "FAIL safety: virtual damage must be applied once per event";
            int healthyAfterSmallHit = ApplyVirtualDamageOnce(1000, 12);
            if (healthyAfterSmallHit != 988 || ReachedYieldThreshold(healthyAfterSmallHit, 1000, 5))
                return "FAIL safety: one small hit must not instant-yield a healthy duelist";
            if (ReachedYieldThreshold(239, 3146, 5) || !ReachedYieldThreshold(157, 3146, 5))
                return "FAIL safety: virtual yield threshold";
            if (ThirdPartyHealChangesVirtualHealth())
                return "FAIL safety: third-party heal changed virtual health";

            if (ShouldFinalizeCleanupPass(1, 10f, 0.75f))
                return "FAIL safety: cleanup must not finalize while frame budget remains";
            if (ShouldFinalizeCleanupPass(0, 0.5f, 0.75f))
                return "FAIL safety: cleanup must not finalize before the time deadline";
            if (!ShouldFinalizeCleanupPass(0, 0.75f, 0.75f))
                return "FAIL safety: cleanup must finalize once both the frame and time budgets are spent";
            if (!ShouldFinalizeCleanupPass(-4, 0.9f, 0.75f))
                return "FAIL safety: cleanup must still finalize once frames have run past zero";

            // Regression proof for the exact discovered live defect: simulate
            // RunPostDuelAttackCleanup's real per-tick shape at a nominal 60fps for two full
            // seconds (comfortably past the 0.75s deadline) two ways, using this SAME production
            // decision function both times.
            //
            // OLD shape (0.4.7-0.4.9): the same ShouldFinalizeCleanupPass condition gated BOTH the
            // top fast-exit AND the bottom finalize decision. This must NEVER finalize, because
            // frames reaches 0 (from the 6-frame budget) long before 0.75s elapses, so by the time
            // the deadline is reached the top guard already sees remainingFrames<=0 && now>=until
            // and returns before the bottom check - the actual historical bug.
            {
                int frames = 6; const float until = 0.75f; bool finalizedOld = false;
                for (float now = 0f; now <= 2f; now += 1f / 60f)
                {
                    if (ShouldFinalizeCleanupPass(frames, now, until)) break; // old top guard fires here
                    frames--;
                    if (ShouldFinalizeCleanupPass(frames, now, until)) { finalizedOld = true; break; }
                }
                if (finalizedOld)
                    return "FAIL safety: regression simulation of the OLD combined-guard shape unexpectedly finalized - this proof is stale";
            }

            // NEW shape: an explicit pending flag gates the top of RunPostDuelAttackCleanup instead
            // of re-checking the finalize condition; ShouldFinalizeCleanupPass is consulted only
            // once, at the bottom, to decide whether THIS tick is the one that finalizes. This must
            // reach finalization well within the simulated two seconds.
            {
                int frames = 6; const float until = 0.75f; bool pending = true; bool finalizedNew = false;
                for (float now = 0f; now <= 2f && pending; now += 1f / 60f)
                {
                    frames--;
                    if (ShouldFinalizeCleanupPass(frames, now, until)) { pending = false; finalizedNew = true; }
                }
                if (!finalizedNew)
                    return "FAIL safety: pending-flag-guarded cleanup must actually finalize during ordinary 60fps ticking";
            }

            return "PASS safety";
        }
    }
}
