using System;

namespace ErenshorDuel
{
    internal enum DuelDamageAuthority
    {
        Vanilla,
        VirtualDuel,
        RealWorld,
        Block
    }

    // Pure authority policy for the combat edges Practice Duel can observe. The runtime maps exact
    // actor identities into these booleans; this file deliberately contains no game/Unity types so
    // the core source/target rules can be exercised outside Erenshor.
    internal static class DuelCombatSemanticsPolicy
    {
        internal static DuelDamageAuthority ResolveDamageAuthority(bool sourceIsDuelSide, bool targetIsDuelist,
            bool sourceIsWorldHostile, bool targetIsWorldHostile, bool sourceIsFriendlyOrProtected,
            bool sourceIsUnknown)
        {
            if (sourceIsDuelSide)
            {
                if (targetIsDuelist) return DuelDamageAuthority.VirtualDuel;
                if (targetIsWorldHostile) return DuelDamageAuthority.RealWorld;
                return DuelDamageAuthority.Block;
            }

            if (targetIsDuelist)
            {
                if (sourceIsWorldHostile) return DuelDamageAuthority.RealWorld;
                if (sourceIsFriendlyOrProtected || sourceIsUnknown) return DuelDamageAuthority.Block;
            }

            return DuelDamageAuthority.Vanilla;
        }

        // Native Erenshor owns mitigation/resistance/crit/class math. Practice Duel's transaction
        // captures the final Stats.ReduceHP argument produced by that calculation and suppresses
        // only the exact real-HP write. The outer native DamageMe/MagicDamageMe/BleedDamageMe call
        // still executes; no synthetic HP headroom participates in the calculation.
        internal static int EffectiveCapturedDamage(bool capturedReduceHp, int capturedReduceHpDamage, int nativeResult)
        {
            if (capturedReduceHp) return Math.Max(0, capturedReduceHpDamage);
            return Math.Max(0, nativeResult);
        }

        internal static bool ShouldCaptureReduceHp(bool transactionPresent, bool worldReal, bool exactTargetStats,
            bool combatActive, bool exactVirtualDuelEdge)
        {
            return transactionPresent && !worldReal && exactTargetStats && combatActive && exactVirtualDuelEdge;
        }

        internal static bool ShouldAdoptWorldDamageIntoRealLedger(bool targetIsDuelist, bool sourceIsWorldHostile)
        {
            return targetIsDuelist && sourceIsWorldHostile;
        }

        internal static bool ShouldBlockBeneficialEdge(bool sourceIsDuelSide, bool targetIsDuelist,
            bool sourceEqualsTarget, bool sourceIsFriendlyOrProtected, bool sourceIsUnknown)
        {
            if (sourceIsDuelSide) return !targetIsDuelist || !sourceEqualsTarget;
            if (targetIsDuelist && (sourceIsFriendlyOrProtected || sourceIsUnknown)) return true;
            return false;
        }

        internal static string RunSelfTests()
        {
            if (ResolveDamageAuthority(true, true, false, false, false, false) != DuelDamageAuthority.VirtualDuel)
                return "FAIL combat semantics: participant damage must be virtual";
            if (ResolveDamageAuthority(true, false, false, true, false, false) != DuelDamageAuthority.RealWorld)
                return "FAIL combat semantics: duelist -> hostile world must stay real";
            if (ResolveDamageAuthority(false, true, true, false, false, false) != DuelDamageAuthority.RealWorld)
                return "FAIL combat semantics: hostile world -> duelist must stay real";
            if (ResolveDamageAuthority(true, false, false, false, false, false) != DuelDamageAuthority.Block)
                return "FAIL combat semantics: duelist -> protected/unknown third actor must block";
            if (ResolveDamageAuthority(false, true, false, false, true, false) != DuelDamageAuthority.Block)
                return "FAIL combat semantics: friendly third-party damage must block";
            if (ResolveDamageAuthority(false, true, false, false, false, true) != DuelDamageAuthority.Block)
                return "FAIL combat semantics: unknown direct ingress must block";
            if (ResolveDamageAuthority(false, false, true, false, false, false) != DuelDamageAuthority.Vanilla)
                return "FAIL combat semantics: unrelated world combat must remain vanilla";

            // Regression gate: a small captured effective hit remains small even if a broken outer
            // transaction/result contains the historical multi-billion sentinel-sized value.
            if (EffectiveCapturedDamage(true, 12, 2147483062) != 12)
                return "FAIL combat semantics: captured 12 damage became synthetic-headroom sized";
            if (EffectiveCapturedDamage(true, 58, 218) != 58 ||
                EffectiveCapturedDamage(true, 4, 13) != 4 ||
                EffectiveCapturedDamage(true, 97, 140) != 97)
                return "FAIL combat semantics: captured native mitigation result must be authoritative";
            if (EffectiveCapturedDamage(true, 0, 87) != 0)
                return "FAIL combat semantics: captured zero damage must remain zero";
            if (EffectiveCapturedDamage(false, 0, 87) != 87 || EffectiveCapturedDamage(false, 0, -3) != 0)
                return "FAIL combat semantics: native result fallback when ReduceHP is not reached";

            if (!ShouldCaptureReduceHp(true, false, true, true, true))
                return "FAIL combat semantics: exact active Duel ReduceHP must be captured";
            if (ShouldCaptureReduceHp(false, false, true, true, true) ||
                ShouldCaptureReduceHp(true, true, true, true, true) ||
                ShouldCaptureReduceHp(true, false, false, true, true) ||
                ShouldCaptureReduceHp(true, false, true, false, true) ||
                ShouldCaptureReduceHp(true, false, true, true, false))
                return "FAIL combat semantics: ordinary/world/wrong-target ReduceHP must remain native";

            if (!ShouldAdoptWorldDamageIntoRealLedger(true, true) || ShouldAdoptWorldDamageIntoRealLedger(true, false))
                return "FAIL combat semantics: real-world ledger adoption";
            if (ShouldBlockBeneficialEdge(true, true, true, false, false))
                return "FAIL combat semantics: self benefit must be allowed";
            if (!ShouldBlockBeneficialEdge(true, true, false, false, false) ||
                !ShouldBlockBeneficialEdge(true, false, false, false, false) ||
                !ShouldBlockBeneficialEdge(false, true, false, true, false))
                return "FAIL combat semantics: cross/third-party benefit isolation";

            return "PASS combat semantics";
        }
    }
}
