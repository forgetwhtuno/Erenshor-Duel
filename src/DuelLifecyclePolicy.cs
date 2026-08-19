namespace ErenshorDuel
{
    internal enum DuelLifecycleState
    {
        Idle,
        Preparing,
        Countdown,
        Active,
        Cleaning
    }

    internal enum DuelLifecycleTrigger
    {
        ChallengeAccepted,
        PreparationElapsed,
        CountdownElapsed,
        Terminal,
        CleanupComplete
    }

    internal static class DuelLifecyclePolicy
    {
        internal static bool IsSessionActive(DuelLifecycleState state)
        {
            return state == DuelLifecycleState.Preparing ||
                   state == DuelLifecycleState.Countdown ||
                   state == DuelLifecycleState.Active;
        }

        internal static bool IsCombatActive(DuelLifecycleState state)
        {
            return state == DuelLifecycleState.Active;
        }

        internal static bool CanStart(DuelLifecycleState state)
        {
            return state == DuelLifecycleState.Idle;
        }

        internal static bool TryTransition(DuelLifecycleState current, DuelLifecycleTrigger trigger,
            out DuelLifecycleState next)
        {
            next = current;
            switch (current)
            {
                case DuelLifecycleState.Idle:
                    if (trigger == DuelLifecycleTrigger.ChallengeAccepted)
                    {
                        next = DuelLifecycleState.Preparing;
                        return true;
                    }
                    return false;

                case DuelLifecycleState.Preparing:
                    if (trigger == DuelLifecycleTrigger.PreparationElapsed)
                    {
                        next = DuelLifecycleState.Countdown;
                        return true;
                    }
                    if (trigger == DuelLifecycleTrigger.Terminal)
                    {
                        next = DuelLifecycleState.Cleaning;
                        return true;
                    }
                    return false;

                case DuelLifecycleState.Countdown:
                    if (trigger == DuelLifecycleTrigger.CountdownElapsed)
                    {
                        next = DuelLifecycleState.Active;
                        return true;
                    }
                    if (trigger == DuelLifecycleTrigger.Terminal)
                    {
                        next = DuelLifecycleState.Cleaning;
                        return true;
                    }
                    return false;

                case DuelLifecycleState.Active:
                    if (trigger == DuelLifecycleTrigger.Terminal)
                    {
                        next = DuelLifecycleState.Cleaning;
                        return true;
                    }
                    return false;

                case DuelLifecycleState.Cleaning:
                    if (trigger == DuelLifecycleTrigger.CleanupComplete)
                    {
                        next = DuelLifecycleState.Idle;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        internal static string RunSelfTests()
        {
            DuelLifecycleState state = DuelLifecycleState.Idle;
            if (!CanStart(state) || IsSessionActive(state)) return "FAIL lifecycle: idle";
            if (!TryTransition(state, DuelLifecycleTrigger.ChallengeAccepted, out state) || state != DuelLifecycleState.Preparing)
                return "FAIL lifecycle: idle -> preparing";
            if (CanStart(state) || !IsSessionActive(state)) return "FAIL lifecycle: preparing ownership";
            if (!TryTransition(state, DuelLifecycleTrigger.PreparationElapsed, out state) || state != DuelLifecycleState.Countdown)
                return "FAIL lifecycle: preparing -> countdown";
            if (!TryTransition(state, DuelLifecycleTrigger.CountdownElapsed, out state) || state != DuelLifecycleState.Active)
                return "FAIL lifecycle: countdown -> active";
            if (!IsCombatActive(state)) return "FAIL lifecycle: active combat phase";
            if (!TryTransition(state, DuelLifecycleTrigger.Terminal, out state) || state != DuelLifecycleState.Cleaning)
                return "FAIL lifecycle: active -> cleaning";
            // Cleaning is a real inter-duel gate, not a hidden extension of Active: no further
            // virtual-combat mutation is admissible once Terminal has fired, and no challenge may
            // start until the CleanupComplete trigger has actually been observed.
            if (CanStart(state) || IsSessionActive(state) || IsCombatActive(state))
                return "FAIL lifecycle: cleaning gate must reject both new challenges and virtual combat";
            if (!TryTransition(state, DuelLifecycleTrigger.CleanupComplete, out state) || state != DuelLifecycleState.Idle)
                return "FAIL lifecycle: cleaning -> idle";
            if (!CanStart(state)) return "FAIL lifecycle: a challenge must be accepted immediately once cleanup completes";

            DuelLifecycleState duplicate;
            if (TryTransition(DuelLifecycleState.Active, DuelLifecycleTrigger.ChallengeAccepted, out duplicate))
                return "FAIL lifecycle: duplicate challenge admitted";
            if (TryTransition(DuelLifecycleState.Cleaning, DuelLifecycleTrigger.Terminal, out duplicate))
                return "FAIL lifecycle: duplicate terminal admitted";
            if (!TryTransition(DuelLifecycleState.Preparing, DuelLifecycleTrigger.Terminal, out duplicate) ||
                duplicate != DuelLifecycleState.Cleaning)
                return "FAIL lifecycle: preparation cancellation";

            // Repeated duels must return to the same clean Idle state every time; Cleaning is a
            // hard inter-duel gate rather than a hidden post-session mutation window.
            for (int i = 0; i < 10; i++)
            {
                DuelLifecycleState repeated = DuelLifecycleState.Idle;
                if (!TryTransition(repeated, DuelLifecycleTrigger.ChallengeAccepted, out repeated) ||
                    !TryTransition(repeated, DuelLifecycleTrigger.PreparationElapsed, out repeated) ||
                    !TryTransition(repeated, DuelLifecycleTrigger.CountdownElapsed, out repeated) ||
                    !TryTransition(repeated, DuelLifecycleTrigger.Terminal, out repeated) ||
                    repeated != DuelLifecycleState.Cleaning || CanStart(repeated) ||
                    !TryTransition(repeated, DuelLifecycleTrigger.CleanupComplete, out repeated) ||
                    repeated != DuelLifecycleState.Idle || !CanStart(repeated))
                    return "FAIL lifecycle: repeated duel cycle " + i;
            }

            return "PASS lifecycle";
        }
    }
}
