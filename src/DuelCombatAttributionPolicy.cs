namespace ErenshorDuel
{
    // Which side of the duel an actor is. Deliberately slot-based rather than "player/sim": in
    // spectator mode the FirstParticipant slot holds a Sim, not the local player, so any rule
    // written in terms of "the player" would silently mis-handle Sim-vs-Sim.
    internal enum DuelCombatRole
    {
        None,
        FirstParticipant,
        SecondParticipant
    }

    // Pure attribution rules for who a duel participant must currently be fighting.
    //
    // Native Erenshor builds its combat-log line directly from the acting NPC's GameObject name and
    // its CurrentAggroTarget's GameObject name (NPC.PerformMeleeHit, and the NPCUses skill variant).
    // It never consults Duel. So the ONLY way to make the player-facing text correct is to make sure
    // the targeting state native code reads is correct at the moment it reads it - which is what
    // these rules decide. Kept Unity-free and game-free so the attribution contract itself is
    // regression-testable outside the game.
    internal static class DuelCombatAttributionPolicy
    {
        internal static DuelCombatRole OpponentOf(DuelCombatRole role)
        {
            if (role == DuelCombatRole.FirstParticipant) return DuelCombatRole.SecondParticipant;
            if (role == DuelCombatRole.SecondParticipant) return DuelCombatRole.FirstParticipant;
            return DuelCombatRole.None;
        }

        // True when a participant's current target must be corrected back to its duel opponent.
        //
        // A duelist aimed at itself, at nothing, or at the wrong participant would make native text
        // render an impossible line (most visibly "<Sim> attacks <Sim>") and would resolve the hit
        // against the wrong actor. A duelist aimed at a genuine hostile-world enemy is NOT corrected:
        // real PvE aggro legitimately outranks the duel pin, and that exception already exists in the
        // controller's own per-frame pin.
        internal static bool ShouldRepin(DuelCombatRole actorRole, DuelCombatRole acquiredRole,
            bool acquiredIsHostileWorld)
        {
            if (actorRole == DuelCombatRole.None) return false;
            if (acquiredIsHostileWorld) return false;
            return acquiredRole != OpponentOf(actorRole);
        }

        // An actor may never be its own duel opponent. Used as an explicit invariant so an aliasing
        // regression (both slots resolving to one actor) fails a test instead of shipping as
        // "<Sim> attacks <Sim>".
        internal static bool RolesAreDistinct(DuelCombatRole first, DuelCombatRole second)
        {
            return first != DuelCombatRole.None && second != DuelCombatRole.None && first != second;
        }

        internal static string RunSelfTests()
        {
            // 1/2: player-vs-Sim attribution both directions. FirstParticipant is the local player
            // in this mode; SecondParticipant is the challenged Sim.
            if (OpponentOf(DuelCombatRole.FirstParticipant) != DuelCombatRole.SecondParticipant)
                return "FAIL attribution: player -> Sim opponent mapping";
            if (OpponentOf(DuelCombatRole.SecondParticipant) != DuelCombatRole.FirstParticipant)
                return "FAIL attribution: Sim -> player opponent mapping";

            // 3/4: spectator attribution uses the exact same slot mapping, so Sim-vs-Sim cannot
            // collapse both sides onto one identity.
            if (OpponentOf(OpponentOf(DuelCombatRole.FirstParticipant)) != DuelCombatRole.FirstParticipant)
                return "FAIL attribution: spectator first/second must round-trip";
            if (OpponentOf(DuelCombatRole.None) != DuelCombatRole.None)
                return "FAIL attribution: a non-participant has no duel opponent";

            // 5: attacker and victim can never alias.
            if (RolesAreDistinct(DuelCombatRole.FirstParticipant, DuelCombatRole.FirstParticipant))
                return "FAIL attribution: a participant must never be its own opponent";
            if (RolesAreDistinct(DuelCombatRole.SecondParticipant, DuelCombatRole.SecondParticipant))
                return "FAIL attribution: second participant must never be its own opponent";
            if (!RolesAreDistinct(DuelCombatRole.FirstParticipant, DuelCombatRole.SecondParticipant))
                return "FAIL attribution: the two duel slots must be distinct";
            if (RolesAreDistinct(DuelCombatRole.None, DuelCombatRole.FirstParticipant))
                return "FAIL attribution: a non-participant is not a valid duel side";

            // The self-target case that produced the live "<Sim> attacks <Sim>" text.
            if (!ShouldRepin(DuelCombatRole.SecondParticipant, DuelCombatRole.SecondParticipant, false))
                return "FAIL attribution: a duelist parked on ITSELF must be corrected";
            if (!ShouldRepin(DuelCombatRole.FirstParticipant, DuelCombatRole.FirstParticipant, false))
                return "FAIL attribution: first participant parked on itself must be corrected";
            if (!ShouldRepin(DuelCombatRole.SecondParticipant, DuelCombatRole.None, false))
                return "FAIL attribution: a duelist parked on a non-participant must be corrected";

            // Correct targeting is left completely alone.
            if (ShouldRepin(DuelCombatRole.SecondParticipant, DuelCombatRole.FirstParticipant, false))
                return "FAIL attribution: correct opponent targeting must not be disturbed";
            if (ShouldRepin(DuelCombatRole.FirstParticipant, DuelCombatRole.SecondParticipant, false))
                return "FAIL attribution: correct opponent targeting must not be disturbed (reverse)";

            // 6/7/10: legitimate hostile-world PvE aggro outranks the duel pin and stays vanilla,
            // and a non-participant NPC is never touched by this rule at all.
            if (ShouldRepin(DuelCombatRole.SecondParticipant, DuelCombatRole.None, true))
                return "FAIL attribution: hostile-world PvE target must outrank the duel pin";
            if (ShouldRepin(DuelCombatRole.FirstParticipant, DuelCombatRole.None, true))
                return "FAIL attribution: hostile-world PvE target must outrank the duel pin (first)";
            if (ShouldRepin(DuelCombatRole.None, DuelCombatRole.FirstParticipant, false))
                return "FAIL attribution: a non-participant NPC must never be re-pinned by the duel";
            if (ShouldRepin(DuelCombatRole.None, DuelCombatRole.None, false))
                return "FAIL attribution: ordinary world combat must remain untouched";

            return "PASS attribution";
        }
    }
}
