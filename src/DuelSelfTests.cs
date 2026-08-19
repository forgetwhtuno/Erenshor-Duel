namespace ErenshorDuel
{
    internal static class DuelSelfTests
    {
        internal static string RunAll()
        {
            string result = DuelCombatSemanticsPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelLifecyclePolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelChallengePolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelEligibilityPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelLocalityPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelIdentity.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelEventFactory.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelSafetyPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DeepSimsCompatibility.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelSpellAdmissionPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelFollowCompatibilityPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelSimActionsFallbackPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelCombatAttributionPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = DuelArmingPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            result = StandaloneLauncherColumnPolicy.RunSelfTests();
            if (!result.StartsWith("PASS")) return result;

            return "PASS deterministic duel self-tests";
        }
    }
}
