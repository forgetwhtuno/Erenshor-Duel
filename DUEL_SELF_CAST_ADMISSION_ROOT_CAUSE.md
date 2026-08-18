# Practice Duel — self-heal / self-buff cast admission root cause (proven)

> **0.4.3 status:** this is the bundled pre-repair diagnosis. The current source now uses the declared self-application model described in section 6 (`SelfOnly || ApplyToCaster || InflictOnSelf`) before interpreting the passed `Stats` as a final target. The document remains as evidence for why the fix is necessary; it is no longer a statement that the defect is still present.

Evidence: current installed `Assembly-CSharp.dll` (IL via Mono.Cecil) + current local
`mods/Erenshor-Duel` source. No source changes made yet; this is the diagnosis.

## 1. Native player cast pipeline (current assembly, verified)

Player hotbar/click path is `Hotkeys::DoHotkeyTask` (the only player-facing `StartSpell(Spell,Stats)`
caller; every other caller is `NPC::*`, `SpellVessel`, `ItemIcon::UseConsumable`, or a proc site).

```
Hotkeys::DoHotkeyTask
  IL_0001  guard AssignedSpell != null
  IL_0012  guard thisHK type
  IL_001D  guard Hotkeys::Cooldown <= 0            <-- native per-hotkey cooldown
  IL_0036  guard CastSpell::KnownSpells.Contains(AssignedSpell)
  IL_004D  read PlayerControl::CurrentTarget
  IL_0081  read Spell::SelfOnly  -> if true, branch to the SELF path at IL_00C5
  IL_0096  (non-self path) target = CurrentTarget?.MyStats
  IL_00BA  PlayerSpells.StartSpell(AssignedSpell, target)
```

### The self path (IL_00C5) — the critical detail

```
IL_00CB  if (!AssignedSpell.SelfOnly)      -> IL_01DB
IL_00DB  if (AssignedSpell.Type == 5)      -> IL_01DB
IL_00E6  if (PlayerControl::CurrentTarget == null)
IL_00F8       loc0 = PlayerControl::Myself          <-- SELF   (only when nothing is targeted)
IL_0105  else loc0 = PlayerControl::CurrentTarget   <-- THE SELECTED TARGET
```

**For a `SelfOnly` spell, vanilla Erenshor still puts `CurrentTarget` into the working target variable
whenever anything is targeted.** It only substitutes the player when `CurrentTarget == null`. The
self-redirection happens later — `CastSpell::StartSpell` itself reads `Spell::SelfOnly` (verified by
whole-assembly field-read scan), so native `StartSpell` is what actually resolves a `SelfOnly` spell
onto the caster. The `Stats` handed to `StartSpell` is therefore **not** a reliable statement of who
the spell will affect.

## 2. Duel's admission chokepoint

All six overloads (`StartSpell/2,3,4,5`, `StartSpellFromProc`, `StartSpellNoAnim`) are Harmony
**Prefix**es that funnel into `DuelController.AllowSpellStart` (`DuelController.cs:1662`) and can
return `false`, which skips native `StartSpell` entirely — before any native cooldown, mana, or cast
commitment. That matches the reported signature (no cast, no cooldown, no resource).

`DuelController.cs:1674-1675`:

```csharp
bool selfCast = targetCharacter == casterCharacter ||
                (target == null && spell != null && (spell.SelfOnly || spell.ApplyToCaster || spell.InflictOnSelf));
```

The spell's own self-application flags are consulted **only when `target == null`**.

## 3. Root cause

In an Active duel the player normally has the opponent selected (Duel itself owns
`CurrentTarget = opponent` for offensive combat). So when the player clicks a `SelfOnly` heal:

1. Native `DoHotkeyTask` takes the self branch, but because `CurrentTarget != null` it sets
   loc0 = **the duel opponent** and calls `StartSpell(spell, opponentStats)`.
2. Duel's prefix computes `targetCharacter` = opponent, `casterCharacter` = player.
   `targetCharacter == casterCharacter` is **false**, and `target != null` so the
   `SelfOnly || ApplyToCaster || InflictOnSelf` clause is **never evaluated**.
   → `selfCast` = **false**, despite the spell being `SelfOnly`.
3. Control falls to the participant-vs-participant branch (`DuelController.cs:1685-1690`):
   `return IsSafeDuelOffense(spell) || BlockSpell(ref result);`
4. `IsSafeDuelOffense` (`DuelController.cs:1776`) rejects it:
   - `spell.TargetHealing > 0` → `return false` (line 1782) for heals/HoTs/lifesteal returns, **and**
   - `Spell.SpellType.Beneficial` / `Heal` / `Pet` / AE types fall to `default: return false` (line 1797)
     for self-buffs and beneficial utility.
5. → `BlockSpell` sets `__result = false` and the prefix returns `false` →
   **native `StartSpell` never executes** → no cast, no cooldown, no mana.

**The defect is in Duel's `selfCast` detection, not in native Erenshor.** Native behaves as designed;
Duel misreads a self-cast as "participant casting a beneficial spell at the opponent" and refuses it
as unsafe offense.

## 4. Why "a couple of other spells" also die

Any `SelfOnly` spell whose category is heal / HoT / beneficial / self-buff / beneficial-utility fails
**identically** whenever the player has a target selected. Offensive spells are unaffected because
they legitimately satisfy `IsSafeDuelOffense`. That exactly matches the report: melee and offensive
casts work; self-heal and several other buttons are silent no-ops.

Predicted corollary (must be confirmed live): pressing the same self-heal with **no target selected**
should work today, because then `target == null` and the existing second clause fires. That is a
clean live A/B test of this diagnosis.

## 5. Sim opponent — separate path, likely NOT the same cause

The Sim's heal path is `NPC::CheckHeals` → `StartSpell(Spell,Stats)`, and `NPC::CheckHeals` reads
`Spell::SelfOnly` itself. Duel also patches `NPC.CheckHeals` (`DuelController.cs:3452`) and
`NPC.CheckBuffs` (`3361`). A Sim healing itself passes its **own** Stats, so
`targetCharacter == casterCharacter` is true and `selfCast` is correctly detected → it should reach
`IsSelfContainedDuelCast`, which is permissive (`StaysOnOneTarget(spell, allowProc: true)`, rejecting
only GroupEffect / PetToSummon / CharmTarget). So the opponent self-heal is **probably fine** and must
be verified independently rather than assumed broken.

## 6. Fix direction (underlying model, not a special case)

Consult the spell's declared self-application intent **independently of the passed `Stats`**, because
native `StartSpell` re-resolves `SelfOnly` onto the caster regardless of what target was handed in:

```csharp
bool declaresSelfApplication = spell != null &&
    (spell.SelfOnly || spell.ApplyToCaster || spell.InflictOnSelf);
bool selfCast = targetCharacter == casterCharacter || declaresSelfApplication;
```

This keeps every other guarantee intact: the admitted cast still has to pass
`IsSelfContainedDuelCast` (so group effects, pet summons and charms remain blocked), third-party and
outside-hostile handling is untouched, and native mana/cooldown/cast-time stay authoritative because
the prefix now returns `true` and lets native `StartSpell` run normally.

Still to be added in the repair pass: bounded per-click admission diagnostics
(`spell_click_observed` / `admission` / `reject_stage` / `start_spell_observed` /
`resource_before-after` / `cooldown_before-after` / `effect_callback`) so the Part 7 spell table can be
filled from a live run, plus the deterministic tests in Part 17.
