namespace ErenshorDuel
{
    internal enum DuelFallbackMode
    {
        Closed,
        SimMenu,
        ChoosingOpponent,
        Confirm
    }

    // Pure state-machine rules for the minimal standalone Sim Actions fallback (DuelSimActionsFallback
    // supplies live measurements -- Follow health, eligibility, which Sim was clicked -- and applies
    // whatever mode this returns). No Unity/game types, so the actual click-to-click arrangement flow
    // is regression-testable without a running game.
    internal static class DuelSimActionsFallbackPolicy
    {
        // Duel never opens or keeps open any fallback UI while Follow's own Sim Actions system is
        // healthy -- there must be exactly one Sim Actions interaction regardless of load order.
        internal static bool ShouldStandDown(bool followSimActionsHealthy)
        {
            return followSimActionsHealthy;
        }

        // A world click on a Sim only selects a second Sim while explicitly arranging a spectator
        // duel. Every other mode (Closed, SimMenu, Confirm) treats a Sim click as the one universal
        // entry point: open/refresh the menu for whatever was actually clicked.
        internal static bool IsChoosingOpponentClick(DuelFallbackMode mode)
        {
            return mode == DuelFallbackMode.ChoosingOpponent;
        }

        // "Arrange Sim Duel" only has meaning from an open SimMenu; it is a no-op from any other mode.
        internal static DuelFallbackMode AfterArrange(DuelFallbackMode current)
        {
            return current == DuelFallbackMode.SimMenu ? DuelFallbackMode.ChoosingOpponent : current;
        }

        // The same Sim cannot become both sides of the arrangement, and an ineligible candidate is
        // never accepted either -- both cases stay in ChoosingOpponent so the player can pick again;
        // only a distinct, eligible second Sim advances to the Start/Cancel confirmation.
        internal static DuelFallbackMode AfterOpponentSelected(DuelFallbackMode current, bool sameSimAsFirst, bool eligible)
        {
            if (current != DuelFallbackMode.ChoosingOpponent) return current;
            if (sameSimAsFirst || !eligible) return DuelFallbackMode.ChoosingOpponent;
            return DuelFallbackMode.Confirm;
        }

        // Cancel always returns to a clean SimMenu for the first Sim (never leaves stale second-Sim
        // selection state behind) unless nothing was open to begin with.
        internal static DuelFallbackMode AfterCancel(DuelFallbackMode current)
        {
            return current == DuelFallbackMode.Closed ? DuelFallbackMode.Closed : DuelFallbackMode.SimMenu;
        }

        // Zoning or any other gameplay-not-ready condition clears every selection back to fully Closed;
        // a stale arrangement must never survive a zone transition.
        internal static DuelFallbackMode OnGameplayNotReady()
        {
            return DuelFallbackMode.Closed;
        }

        // The first Sim becoming hard-invalid (gone, never a valid actor, dead, wrong zone) invalidates
        // the whole interaction -- there is no menu left to show.
        internal static DuelFallbackMode OnFirstSimInvalid()
        {
            return DuelFallbackMode.Closed;
        }

        // The second Sim becoming hard-invalid only invalidates the arrangement; the first Sim (and a
        // menu for it) is still perfectly valid, so this returns to SimMenu rather than closing outright.
        internal static DuelFallbackMode OnSecondSimInvalid(DuelFallbackMode current)
        {
            return current == DuelFallbackMode.Confirm ? DuelFallbackMode.SimMenu : current;
        }

        internal static string RunSelfTests()
        {
            if (!ShouldStandDown(true) || ShouldStandDown(false))
                return "FAIL fallback-policy: stand-down must track Follow health exactly";

            if (IsChoosingOpponentClick(DuelFallbackMode.SimMenu) || IsChoosingOpponentClick(DuelFallbackMode.Closed) ||
                IsChoosingOpponentClick(DuelFallbackMode.Confirm) || !IsChoosingOpponentClick(DuelFallbackMode.ChoosingOpponent))
                return "FAIL fallback-policy: only ChoosingOpponent treats a world click as opponent selection";

            if (AfterArrange(DuelFallbackMode.SimMenu) != DuelFallbackMode.ChoosingOpponent)
                return "FAIL fallback-policy: Arrange Sim Duel enters choose-second-Sim mode";
            if (AfterArrange(DuelFallbackMode.Closed) != DuelFallbackMode.Closed)
                return "FAIL fallback-policy: Arrange Sim Duel is a no-op outside SimMenu";

            if (AfterOpponentSelected(DuelFallbackMode.ChoosingOpponent, true, true) != DuelFallbackMode.ChoosingOpponent)
                return "FAIL fallback-policy: the same Sim cannot be selected twice";
            if (AfterOpponentSelected(DuelFallbackMode.ChoosingOpponent, false, false) != DuelFallbackMode.ChoosingOpponent)
                return "FAIL fallback-policy: an ineligible second Sim does not advance to Confirm";
            if (AfterOpponentSelected(DuelFallbackMode.ChoosingOpponent, false, true) != DuelFallbackMode.Confirm)
                return "FAIL fallback-policy: a distinct eligible second Sim produces a valid spectator request";
            if (AfterOpponentSelected(DuelFallbackMode.SimMenu, false, true) != DuelFallbackMode.SimMenu)
                return "FAIL fallback-policy: opponent selection outside ChoosingOpponent must not change mode";

            if (AfterCancel(DuelFallbackMode.Confirm) != DuelFallbackMode.SimMenu ||
                AfterCancel(DuelFallbackMode.ChoosingOpponent) != DuelFallbackMode.SimMenu)
                return "FAIL fallback-policy: Cancel clears selection state back to a clean SimMenu";
            if (AfterCancel(DuelFallbackMode.Closed) != DuelFallbackMode.Closed)
                return "FAIL fallback-policy: Cancel from Closed stays Closed";

            if (OnGameplayNotReady() != DuelFallbackMode.Closed)
                return "FAIL fallback-policy: zoning/not-ready clears every selection to Closed";

            if (OnFirstSimInvalid() != DuelFallbackMode.Closed)
                return "FAIL fallback-policy: a hard-invalid first Sim closes the whole interaction";
            if (OnSecondSimInvalid(DuelFallbackMode.Confirm) != DuelFallbackMode.SimMenu)
                return "FAIL fallback-policy: a hard-invalid second Sim falls back to SimMenu, not Closed";
            if (OnSecondSimInvalid(DuelFallbackMode.SimMenu) != DuelFallbackMode.SimMenu)
                return "FAIL fallback-policy: second-Sim invalidation outside Confirm is a no-op";

            return "PASS fallback-policy";
        }
    }
}
