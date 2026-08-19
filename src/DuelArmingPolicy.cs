namespace ErenshorDuel
{
    // WHEN duel combat becomes armed, and WHEN it is safe to write native targeting state.
    //
    // Preparing/Countdown mean "the duel is NOT armed yet": 3 / 2 / 1 / GO, then combat starts.
    // Only the proposed duel PAIR is held back - unrelated world combat (hostile mobs, outside PvE,
    // any NPC that is not one of these two participants) stays completely native. There is no arena
    // bubble and nothing is globally frozen.
    //
    // This file is pure decision logic with no UnityEngine/Lunaris/game dependency so the contract
    // is deterministically testable outside the game.
    internal static class DuelArmingPolicy
    {
        // The single definition of "armed". Preparing and Countdown are deliberately excluded.
        internal static bool IsArmed(DuelLifecycleState state)
        {
            return state == DuelLifecycleState.Active;
        }

        // Narrow duel-pair admission gate for native combat entry (NPC.Combat) and for the
        // attribution re-pin. Every argument is about THIS actor/target edge only:
        //
        //   actorIsParticipant   - the acting NPC is one of the two duel participants
        //   targetIsOpponent     - its current target is exactly its duel opponent
        //   targetIsOutsideHostile - its current target is a verified hostile world actor
        //
        // A non-participant actor is never blocked. A participant fighting a real hostile world
        // actor is never blocked - that is real PvE and outranks the duel. Only the exact
        // participant<->participant edge is refused, and only before GO.
        internal static bool ShouldBlockParticipantCombat(bool actorIsParticipant, bool targetIsOpponent,
            bool targetIsOutsideHostile, DuelLifecycleState state)
        {
            if (!actorIsParticipant) return false;
            if (targetIsOutsideHostile) return false;
            if (!targetIsOpponent) return false;
            return !IsArmed(state);
        }

        // Pre-GO the pair must not stay pinned on each other either, or native AI simply re-enters
        // combat on the next frame. Same edge, same exclusions.
        internal static bool ShouldDisarmDuelPairTarget(bool actorIsParticipant, bool targetIsOpponent,
            bool targetIsOutsideHostile, DuelLifecycleState state)
        {
            return ShouldBlockParticipantCombat(actorIsParticipant, targetIsOpponent, targetIsOutsideHostile, state);
        }

        // The duel pair is armed exactly once, at Countdown -> Active. Arming is a property of the
        // state itself, so it cannot be applied twice for one session.
        internal static bool ShouldArmDuelPair(DuelLifecycleState state)
        {
            return IsArmed(state);
        }

        // Re-entrancy guard for native NPC.Combat().
        //
        // Verified against the installed Assembly-CSharp.dll: NPC.Combat() executes
        //
        //     IL_0314  call   NPC::PerformMeleeHit(int, bool)
        //     IL_031A  ldfld  Character NPC::CurrentAggroTarget
        //     IL_0324  stfld  float Character::RecentDirectHit
        //
        // storing into CurrentAggroTarget with NO null guard at all, immediately after the melee
        // hit returns. A duel whose yield threshold is reached inside that hit calls Stop()
        // synchronously from the damage prefix, and terminal cleanup then nulls CurrentAggroTarget
        // while that native frame is still on the stack - so the store at IL_0324 dereferences
        // null. That is the observed NullReferenceException in NPC.Combat/NPC.DoNonRaidBehavior
        // after a spectator duel. The repair is to defer the write until the native frame has
        // returned, not to catch the exception and not to leave a fabricated target behind.
        internal static bool ShouldDeferAggroTargetWrite(bool insideNativeCombat)
        {
            return insideNativeCombat;
        }

        internal static string RunSelfTests()
        {
            // 1-4: the pair cannot arm before GO, in either duel mode (mode is not an input here -
            // both player-vs-Sim and spectator reach this through the same participant edge).
            if (IsArmed(DuelLifecycleState.Preparing)) return "FAIL arming: preparing must not be armed";
            if (IsArmed(DuelLifecycleState.Countdown)) return "FAIL arming: countdown must not be armed";
            if (IsArmed(DuelLifecycleState.Idle)) return "FAIL arming: idle must not be armed";
            if (IsArmed(DuelLifecycleState.Cleaning)) return "FAIL arming: cleaning must not be armed";
            if (!IsArmed(DuelLifecycleState.Active)) return "FAIL arming: active must be armed";

            if (!ShouldBlockParticipantCombat(true, true, false, DuelLifecycleState.Preparing))
                return "FAIL arming: participant pair admitted during preparing";
            if (!ShouldBlockParticipantCombat(true, true, false, DuelLifecycleState.Countdown))
                return "FAIL arming: participant pair admitted during countdown";
            if (ShouldBlockParticipantCombat(true, true, false, DuelLifecycleState.Active))
                return "FAIL arming: participant pair blocked after GO";

            // 6: world hostile combat is never blocked, in any state, for anyone.
            if (ShouldBlockParticipantCombat(false, true, false, DuelLifecycleState.Preparing))
                return "FAIL arming: a non-participant was blocked";
            if (ShouldBlockParticipantCombat(false, false, true, DuelLifecycleState.Countdown))
                return "FAIL arming: an unrelated world NPC was blocked";
            if (ShouldBlockParticipantCombat(true, false, true, DuelLifecycleState.Preparing))
                return "FAIL arming: a participant's real hostile-world fight was blocked before GO";
            if (ShouldBlockParticipantCombat(true, false, true, DuelLifecycleState.Active))
                return "FAIL arming: a participant's real hostile-world fight was blocked after GO";
            if (ShouldBlockParticipantCombat(true, false, false, DuelLifecycleState.Countdown))
                return "FAIL arming: a participant target that is not its opponent was blocked";

            // Disarm mirrors the block decision exactly, so the pair cannot re-enter on the next frame.
            if (!ShouldDisarmDuelPairTarget(true, true, false, DuelLifecycleState.Preparing))
                return "FAIL arming: pre-GO pair pin was not disarmed";
            if (!ShouldDisarmDuelPairTarget(true, true, false, DuelLifecycleState.Countdown))
                return "FAIL arming: countdown pair pin was not disarmed";
            if (ShouldDisarmDuelPairTarget(true, true, false, DuelLifecycleState.Active))
                return "FAIL arming: an armed duel pin was disarmed";
            if (ShouldDisarmDuelPairTarget(true, false, true, DuelLifecycleState.Preparing))
                return "FAIL arming: a real hostile-world pin was disarmed";

            // 5: arming happens exactly at Active and nowhere else.
            int armedStates = 0;
            DuelLifecycleState[] all = new DuelLifecycleState[]
            {
                DuelLifecycleState.Idle, DuelLifecycleState.Preparing, DuelLifecycleState.Countdown,
                DuelLifecycleState.Active, DuelLifecycleState.Cleaning
            };
            for (int i = 0; i < all.Length; i++) if (ShouldArmDuelPair(all[i])) armedStates++;
            if (armedStates != 1) return "FAIL arming: duel pair arms in more than one lifecycle state";

            // Re-entrancy contract.
            if (!ShouldDeferAggroTargetWrite(true)) return "FAIL arming: write inside native combat was not deferred";
            if (ShouldDeferAggroTargetWrite(false)) return "FAIL arming: write outside native combat was deferred";

            return "PASS arming";
        }
    }
}
