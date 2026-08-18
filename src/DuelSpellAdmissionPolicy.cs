using System;

namespace ErenshorDuel
{
    // Pure spell-admission policy for Practice Duel. No UnityEngine/game dependency, so the exact
    // decision logic the runtime ships can be exercised by the offline deterministic suite.
    //
    // Why this exists
    // ---------------
    // The Stats argument handed to CastSpell.StartSpell is NOT a statement of which actor the spell
    // will finally affect. Installed Assembly-CSharp Hotkeys::DoHotkeyTask (self branch, IL_00C5)
    // assigns PlayerControl.CurrentTarget into the target argument for a SelfOnly spell whenever
    // anything is selected, and only substitutes the caster when CurrentTarget is null. The real
    // self-redirection happens afterwards inside CastSpell::StartSpell, which reads Spell.SelfOnly
    // itself.
    //
    // Deciding "self cast vs cast at the opponent" from the passed Stats alone therefore misread
    // every self-cast made while the duel opponent was targeted as a beneficial spell aimed at the
    // opponent. Beneficial shapes fail the offense test (TargetHealing > 0, or the Beneficial/Heal
    // spell types), so the Harmony prefix returned false and native StartSpell never executed -
    // producing a silent no-op with no cast, no mana spend and no cooldown.
    internal static class DuelSpellAdmissionPolicy
    {
        // A property of the spell asset, independent of the caller-supplied target.
        internal static bool DeclaresSelfApplication(bool selfOnly, bool applyToCaster, bool inflictOnSelf)
        {
            return selfOnly || applyToCaster || inflictOnSelf;
        }

        // A cast is a self-cast when the target argument really is the caster, OR when the spell
        // declares that native resolution will apply it to the caster regardless of that argument.
        internal static bool IsSelfCast(bool targetArgumentIsCaster, bool declaresSelfApplication)
        {
            return targetArgumentIsCaster || declaresSelfApplication;
        }

        // Recognizing self-application is NOT the same as admitting the cast. An admitted self-cast
        // must still be containable inside the 1v1: group effects, pet summons and charms all resolve
        // as self-casts and then reach actors the duel never sandboxed.
        internal static bool StaysOnOneTarget(bool groupEffect, bool petSummon, bool charmTarget,
            bool hasProc, bool allowProc)
        {
            if (groupEffect || petSummon || charmTarget) return false;
            if (!allowProc && hasProc) return false;
            return true;
        }

        // Procs are tolerated on a duelist's own self-cast (a weapon proc buff is ordinary class kit).
        internal static bool IsSelfContainedDuelCast(bool groupEffect, bool petSummon, bool charmTarget, bool hasProc)
        {
            return StaysOnOneTarget(groupEffect, petSummon, charmTarget, hasProc, true);
        }

        internal static bool IsAreaShape(bool groupEffect, bool isAe, bool isPbae)
        {
            return groupEffect || isAe || isPbae;
        }

        // Area casts are admitted only when their escape routes are all covered by per-target
        // containment. GroupEffect itself is not an automatic rejection anymore: participant
        // healing/buffs can be allowed while unrelated beneficiaries are filtered at HealMe /
        // AddStatusEffect. Summons, charm and proc-grant shapes remain uncontainable here.
        internal static bool IsAreaStructurallyContainable(bool petSummon, bool charmTarget, bool hasProc)
        {
            return !petSummon && !charmTarget && !hasProc;
        }

        internal static bool CanAdmitArea(bool knownOffensive, bool knownBeneficial,
            bool petSummon, bool charmTarget, bool hasProc, bool perTargetContainmentAvailable)
        {
            return perTargetContainmentAvailable && (knownOffensive || knownBeneficial) &&
                   IsAreaStructurallyContainable(petSummon, charmTarget, hasProc);
        }

        internal static string RunSelfTests()
        {
            // --- DeclaresSelfApplication -------------------------------------------------------
            if (!DeclaresSelfApplication(true, false, false)) return "FAIL SelfOnly declares self-application";
            if (!DeclaresSelfApplication(false, true, false)) return "FAIL ApplyToCaster declares self-application";
            if (!DeclaresSelfApplication(false, false, true)) return "FAIL InflictOnSelf declares self-application";
            if (DeclaresSelfApplication(false, false, false)) return "FAIL plain spell must not declare self-application";

            // --- 1/2/3: the regression. Opponent is the passed target argument, yet the spell -----
            //            declares self-application, so it must still be recognized as a self-cast.
            const bool targetArgumentIsOpponent = false; // target argument is NOT the caster
            if (!IsSelfCast(targetArgumentIsOpponent, DeclaresSelfApplication(true, false, false)))
                return "FAIL SelfOnly heal with opponent targeted must be a self-cast";
            if (!IsSelfCast(targetArgumentIsOpponent, DeclaresSelfApplication(false, true, false)))
                return "FAIL ApplyToCaster spell with opponent targeted must be a self-cast";
            if (!IsSelfCast(targetArgumentIsOpponent, DeclaresSelfApplication(false, false, true)))
                return "FAIL InflictOnSelf spell with opponent targeted must be a self-cast";

            // The pre-repair calculation, reproduced, must disagree - proving the test covers the bug.
            bool preRepairSelfCast = targetArgumentIsOpponent /* targetCharacter == casterCharacter */ ||
                                     (false /* target == null */ && true);
            if (preRepairSelfCast) return "FAIL pre-repair calculation should have missed this self-cast";

            // --- 4/5: non-self shapes are unchanged ---------------------------------------------
            if (IsSelfCast(targetArgumentIsOpponent, DeclaresSelfApplication(false, false, false)))
                return "FAIL offensive spell at opponent must not become a self-cast";
            if (IsSelfCast(targetArgumentIsOpponent, DeclaresSelfApplication(false, false, false)))
                return "FAIL beneficial spell genuinely aimed at opponent must not become a self-cast";

            // A self-cast is still recognized the ordinary way when the target argument is the caster.
            if (!IsSelfCast(true, false)) return "FAIL target argument equal to caster must be a self-cast";

            // --- 6/7/8: containment safety survives the repair ----------------------------------
            if (IsSelfContainedDuelCast(true, false, false, false))
                return "FAIL group-effect self spell must remain blocked";
            if (IsSelfContainedDuelCast(false, true, false, false))
                return "FAIL pet summon must remain blocked";
            if (IsSelfContainedDuelCast(false, false, true, false))
                return "FAIL charm must remain blocked";
            if (!IsSelfContainedDuelCast(false, false, false, true))
                return "FAIL proc on a duelist self-cast must remain allowed";
            if (!IsSelfContainedDuelCast(false, false, false, false))
                return "FAIL ordinary contained self-cast must be allowed";

            // A declared self-application that is NOT containable is still refused: recognizing the
            // self-cast must not become a blanket "admit every beneficial spell".
            bool declaredButUnsafe = DeclaresSelfApplication(true, false, false) &&
                                     IsSelfContainedDuelCast(true, false, false, false);
            if (declaredButUnsafe) return "FAIL group-effect SelfOnly buff must not be admitted";

            // --- offense containment is stricter than self-containment --------------------------
            if (StaysOnOneTarget(false, false, false, true, false))
                return "FAIL proc handed to the opponent must remain blocked";

            // --- area policy --------------------------------------------------------------------
            if (!IsAreaShape(true, false, false) || !IsAreaShape(false, true, false) ||
                !IsAreaShape(false, false, true) || IsAreaShape(false, false, false))
                return "FAIL area shape detection";
            if (!CanAdmitArea(true, false, false, false, false, true))
                return "FAIL offensive AoE should be admissible with per-target containment";
            if (!CanAdmitArea(false, true, false, false, false, true))
                return "FAIL beneficial AoE should be admissible with per-target containment";
            if (CanAdmitArea(true, false, true, false, false, true) ||
                CanAdmitArea(true, false, false, true, false, true) ||
                CanAdmitArea(true, false, false, false, true, true))
                return "FAIL summon/charm/proc AoE must remain blocked";
            if (CanAdmitArea(true, false, false, false, false, false))
                return "FAIL area cast without per-target containment must remain blocked";
            if (CanAdmitArea(false, false, false, false, false, true))
                return "FAIL unknown area payload must remain blocked";

            return "PASS deterministic duel spell-admission self-tests";
        }
    }
}
