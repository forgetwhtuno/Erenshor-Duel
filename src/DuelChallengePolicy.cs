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

    internal struct DuelWillingnessInput
    {
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
        internal static DuelSocialDecision Evaluate(DuelWillingnessInput input)
        {
            // Cooldown is a hard willingness gate for both party and nearby non-party Sims.
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

            return "PASS willingness";
        }

    }
}
