using System;

namespace ErenshorDuel
{
    internal enum DuelSocialDecision
    {
        Accept,
        Decline,
        DeclineLowHealth,
        DeclineRecentDuel,
        DeclineLevelMismatch
    }

    // Who asked for this duel. The long social recent-duel cooldown exists to stop AI-driven
    // challenge spam; it was never meant to make a deliberate player request unusable for minutes
    // after a fight the player just watched finish. Separating the two lets the social cooldown stay
    // exactly as strict as before for autonomous sources while an explicit request only has to clear
    // the real lifecycle/eligibility gates plus a tiny technical debounce.
    internal enum DuelRequestOrigin
    {
        // A deliberate, player-initiated request: /eduel <Sim>, /eduel <A> vs <B>, the Sim Actions
        // Practice Duel / Arrange Sim Duel buttons, or Challenge Nearby.
        ExplicitPlayer,
        // Any AI/ambient/rematch-offer source, including future Nemesis integration.
        Autonomous
    }

    internal struct DuelWillingnessInput
    {
        internal DuelRequestOrigin Origin;
        internal bool IsPartySim;
        internal bool Rival;
        internal bool HasHealth;
        internal int CurrentHealth;
        internal int MaximumHealth;
        internal bool HasLevel;
        internal int PlayerLevel;
        internal int SimLevel;
        internal bool RecentDuel;
        internal string StableKey;
    }

    internal static class DuelChallengePolicy
    {
        // How long a Sim is considered "recently dueled" for a given request origin. An autonomous
        // source keeps the full social cooldown so Nemesis and ambient rematch offers cannot spam
        // combat encounters. An explicit player request only has to clear a short technical debounce
        // - just enough to swallow one duplicated click or command after Cleaning -> Idle - because
        // the real inter-duel safety window is the Cleaning interval, which is enforced separately
        // and independently by the lifecycle state machine.
        internal static float RecentDuelWindowSeconds(DuelRequestOrigin origin,
            float socialCooldownSeconds, float explicitDebounceSeconds)
        {
            return origin == DuelRequestOrigin.ExplicitPlayer
                ? Math.Max(0f, explicitDebounceSeconds)
                : Math.Max(0f, socialCooldownSeconds);
        }

        internal static DuelSocialDecision Evaluate(DuelWillingnessInput input)
        {
            // Cooldown is a hard willingness gate for both party and nearby non-party Sims. The
            // caller decides what "recent" means for this origin via RecentDuelWindowSeconds, so an
            // explicit player request is only blocked by the short debounce, never by the long
            // social cooldown. Every other gate below is unchanged and applies identically to both
            // origins - this separation relaxes nothing about eligibility or safety.
            if (input.RecentDuel) return DuelSocialDecision.DeclineRecentDuel;

            // Preserve the established party-Sim compatibility behavior once mechanical safety
            // has already been proven by DuelEligibilityPolicy.
            if (input.IsPartySim) return DuelSocialDecision.Accept;

            if (input.HasHealth && input.MaximumHealth > 0)
            {
                int healthPercent = Math.Max(0, Math.Min(100,
                    (int)Math.Round(input.CurrentHealth * 100.0 / input.MaximumHealth)));
                if (healthPercent < 35) return DuelSocialDecision.DeclineLowHealth;
            }

            // A direct command is an explicit practice-duel invitation. Once the hard mechanical,
            // health, and cooldown gates pass, nearby non-party Sims accept too. Level gaps and a
            // deterministic personality roll must not make the feature appear broken.
            return DuelSocialDecision.Accept;
        }

        internal static string Token(DuelSocialDecision decision)
        {
            switch (decision)
            {
                case DuelSocialDecision.Accept: return "accept";
                case DuelSocialDecision.DeclineLowHealth: return "decline_low_health";
                case DuelSocialDecision.DeclineRecentDuel: return "decline_recent_duel";
                case DuelSocialDecision.DeclineLevelMismatch: return "decline_level_mismatch";
                default: return "decline";
            }
        }

        internal static string RunSelfTests()
        {
            if (Evaluate(new DuelWillingnessInput { IsPartySim = true }) != DuelSocialDecision.Accept)
                return "FAIL willingness: healthy party Sim compatibility";
            if (Evaluate(new DuelWillingnessInput { IsPartySim = true, RecentDuel = true }) != DuelSocialDecision.DeclineRecentDuel)
                return "FAIL willingness: party Sim cooldown";

            DuelWillingnessInput comparable = new DuelWillingnessInput
            {
                HasHealth = true,
                CurrentHealth = 90,
                MaximumHealth = 100,
                HasLevel = true,
                PlayerLevel = 20,
                SimLevel = 21,
                StableKey = "simindex:101"
            };
            DuelSocialDecision comparableFirst = Evaluate(comparable);
            if (comparableFirst != DuelSocialDecision.Accept)
                return "FAIL willingness: healthy comparable non-party Sim";
            for (int i = 0; i < 8; i++)
                if (Evaluate(comparable) != comparableFirst)
                    return "FAIL willingness: comparable non-party result is not deterministic";

            DuelWillingnessInput low = new DuelWillingnessInput
            {
                HasHealth = true,
                CurrentHealth = 20,
                MaximumHealth = 100,
                Rival = true,
                StableKey = "simindex:102"
            };
            if (Evaluate(low) != DuelSocialDecision.DeclineLowHealth)
                return "FAIL willingness: low health must hard-decline";

            DuelWillingnessInput recent = new DuelWillingnessInput
            {
                RecentDuel = true,
                Rival = true,
                StableKey = "simindex:103"
            };
            if (Evaluate(recent) != DuelSocialDecision.DeclineRecentDuel)
                return "FAIL willingness: recent duel must hard-decline";

            // Rival remains a bounded preference modifier and cannot override the hard gates.
            DuelWillingnessInput rival = new DuelWillingnessInput
            {
                HasHealth = true,
                CurrentHealth = 55,
                MaximumHealth = 100,
                HasLevel = true,
                PlayerLevel = 20,
                SimLevel = 27,
                StableKey = "rival-a"
            };
            DuelSocialDecision withoutRival = Evaluate(rival);
            rival.Rival = true;
            DuelSocialDecision withRival = Evaluate(rival);
            if (withoutRival != DuelSocialDecision.Accept || withRival != DuelSocialDecision.Accept)
                return "FAIL willingness: Rival bounded modifier";

            DuelWillingnessInput mismatch = new DuelWillingnessInput
            {
                HasHealth = true,
                CurrentHealth = 70,
                MaximumHealth = 100,
                HasLevel = true,
                PlayerLevel = 10,
                SimLevel = 30,
                StableKey = "mismatch-a"
            };
            if (Evaluate(mismatch) != DuelSocialDecision.Accept)
                return "FAIL willingness: virtual duel level mismatch should accept";

            // --- explicit vs autonomous recent-duel separation -------------------------------------
            // Live defect: after Cleaning -> Idle (admissionBlocked=False, reason=cleanup_complete)
            // a deliberate re-challenge of the same Sim was still refused with decline_recent_duel
            // for the full 120s social cooldown. An explicit request must only clear the short
            // technical debounce; an autonomous one keeps the full cooldown.
            const float social = 120f;
            const float debounce = 1f;

            if (RecentDuelWindowSeconds(DuelRequestOrigin.Autonomous, social, debounce) != social)
                return "FAIL willingness: an autonomous challenge must keep the full social cooldown";
            if (RecentDuelWindowSeconds(DuelRequestOrigin.ExplicitPlayer, social, debounce) != debounce)
                return "FAIL willingness: an explicit player request must only use the short debounce";
            if (RecentDuelWindowSeconds(DuelRequestOrigin.ExplicitPlayer, social, debounce) >=
                RecentDuelWindowSeconds(DuelRequestOrigin.Autonomous, social, debounce))
                return "FAIL willingness: the explicit debounce must be strictly shorter than the social cooldown";
            if (RecentDuelWindowSeconds(DuelRequestOrigin.ExplicitPlayer, social, -5f) != 0f ||
                RecentDuelWindowSeconds(DuelRequestOrigin.Autonomous, -5f, debounce) != 0f)
                return "FAIL willingness: a negative window must clamp to zero rather than invert";

            // A 5-second-old duel: explicit request is past the 1s debounce, autonomous is not past
            // the 120s cooldown. This is the exact live scenario.
            const float sinceLastDuel = 5f;
            bool explicitBlocked = sinceLastDuel < RecentDuelWindowSeconds(DuelRequestOrigin.ExplicitPlayer, social, debounce);
            bool autonomousBlocked = sinceLastDuel < RecentDuelWindowSeconds(DuelRequestOrigin.Autonomous, social, debounce);
            if (explicitBlocked) return "FAIL willingness: an explicit rematch shortly after Idle must be admitted";
            if (!autonomousBlocked) return "FAIL willingness: an autonomous rematch shortly after Idle must still be declined";

            // Origin must not become a general bypass: every other hard gate still applies to an
            // explicit request exactly as before.
            DuelWillingnessInput explicitLowHealth = new DuelWillingnessInput
            {
                Origin = DuelRequestOrigin.ExplicitPlayer,
                HasHealth = true,
                CurrentHealth = 10,
                MaximumHealth = 100,
                StableKey = "explicit-low"
            };
            if (Evaluate(explicitLowHealth) != DuelSocialDecision.DeclineLowHealth)
                return "FAIL willingness: an explicit request must still be refused for low health";

            // And a caller that DOES report RecentDuel (i.e. inside whatever window applies to it)
            // is still declined regardless of origin - the origin only chooses the window length.
            DuelWillingnessInput explicitInsideDebounce = new DuelWillingnessInput
            {
                Origin = DuelRequestOrigin.ExplicitPlayer,
                IsPartySim = true,
                RecentDuel = true,
                StableKey = "explicit-debounce"
            };
            if (Evaluate(explicitInsideDebounce) != DuelSocialDecision.DeclineRecentDuel)
                return "FAIL willingness: a duplicated click inside the debounce is still declined";

            return "PASS willingness";
        }

    }
}
