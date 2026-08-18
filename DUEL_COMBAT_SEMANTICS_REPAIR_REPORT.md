# Practice Duel 0.4.4 — forensic damage-transaction repair report

**Scope:** `mods/Erenshor-Duel` only.  
**Baseline:** current supplied 2026-08-17 project packet, Practice Duel 0.4.3.  
**Historical comparison:** `forgetwhtuno/ForgottenRoads-Duel` commit `037d28ba0ff9c282dd9553912f1ecd3c4b532097` (0.4.1).  
**Git:** no Git writes.

## Environment boundary

The supplied current packet contains `Lunaris.dll` and Harmony references but does not contain the current installed `Assembly-CSharp.dll` or Unity managed assemblies, and this sandbox has no C#/.NET/PowerShell build toolchain or Erenshor runtime. The current-assembly/build/install/live gates therefore remain unclaimed. Historical installed-IL notes bundled with the project are used only where explicitly identified.

## Three-way forensic result

### Current 0.4.3 transaction — broken

For an admitted Duel participant hit, current 0.4.3 did:

```text
PrepareNativeDamage
  -> NativeBefore = int.MaxValue
  -> target.MyStats.CurrentHP = int.MaxValue
  -> native DamageMe/MagicDamageMe/BleedDamageMe
  -> Stats.ReduceHP executes normally
  -> FinishNativeDamage
  -> effective = NativeBefore - nativeAfter (when positive)
  -> virtual HP -= effective
```

That design assumes every native damage path preserves the synthetic headroom except for the final reduction. The live regression disproves that assumption.

The reported virtual delta was `2,147,483,062`. Since 0.4.3 sets `NativeBefore = 2,147,483,647`, the exact `nativeAfter` observed by the current formula was:

```text
2,147,483,647 - 2,147,483,062 = 585
```

Therefore the synthetic headroom itself entered the computed delta. The visible/native hit being about 12 is consistent with native code normalizing/clamping the actor back into its ordinary HP domain before/while applying the hit, but the exact pre-hit ordinary HP value cannot be proven without the missing current assembly/live diagnostic. The arithmetic above is source + live-value proof; no guess about the missing intermediate native instructions is required to establish the defect.

### Historical 0.4.1 transaction — live-proven semantic baseline

Commit `037d28ba0ff9c282dd9553912f1ecd3c4b532097` used:

```text
PrepareNativeDamage
  -> push scoped NativeDamageState
  -> native DamageMe/MagicDamageMe/BleedDamageMe calculates hit
  -> exact target Stats.ReduceHP Prefix
       CapturedReduceHpDamage = final damage argument
       CapturedReduceHp = true
       __result = false
       return false          // suppress only this HP mutation
  -> native outer method returns
  -> FinishNativeDamage uses captured amount
  -> virtual HP decreases exactly once
```

That architecture let Erenshor own mitigation/resistance/crit/class math while preventing real participant HP/death mutation. Historical live evidence showed repeated sensible hits and normal duel duration/yield.

### Repaired 0.4.4 transaction

0.4.4 restores the historical capture semantics but keeps the current safer nesting/world model:

```text
PrepareNativeDamage
  -> prove exact Duel participant edge
  -> push NativeDamageState { Previous = current }
  -> retain current scoped faction/layer workaround
  -> DO NOT alter participant HP
  -> native damage method calculates
  -> CaptureNativeReduceHp
       require current top-of-stack state
       require !WorldReal
       require exact target Stats
       require current active Duel + exact Duel edge
       capture final reduction
       suppress only this ReduceHP write
  -> FinishNativeDamage
       effective = captured ReduceHP amount
       ApplyVirtualDamageOnce
       pop/restore scoped state
       mirror current virtual health
       evaluate yield threshold
```

If a current native path does not reach `ReduceHP`, the historical `nativeResult` fallback is retained, clamped to non-negative. No synthetic HP/headroom fallback remains.

## Reentrancy

0.4.1 used a single thread-static in-flight state. Current 0.4.3 had already modernized that into a linked `NativeDamageState.Previous` stack. 0.4.4 preserves the stack. A nested virtual hit pushes its own capture scope and pops back to the outer state; a `WorldReal` nested transaction bypasses `ReduceHP` suppression and remains native. Terminal cleanup still clears/restores outstanding scoped state.

## Preserved current work

0.4.4 does not roll back the current lifecycle, retained UI/standalone controls, Suite compatibility, COOP remote-human exclusions, cleanup/emergency cleanup, self-cast admission repair, actor/effect-aware AoE behavior, hostile-world real-combat ledger, protected NPC classification, or status/effect restoration.

The self-cast decision remains:

```text
declaresSelfApplication = SelfOnly || ApplyToCaster || InflictOnSelf
selfCast = passedTargetIsCaster || declaresSelfApplication
```

Allowed casts still return to native Erenshor for mana, cooldown, cast time, animation and effect resolution.

## World/AoE authority

- Duel player -> Duel Sim: virtual, native calculation.
- Duel Sim -> Duel player: virtual, native calculation.
- Duel participant -> verified hostile-world actor: real native world damage/effect.
- Verified hostile-world actor -> Duel participant: real native world damage/effect, adopted into real ledger.
- Protected/friendly/unknown unsafe nonparticipant effect edges: contained according to current policy.
- Offensive AoE with legitimate hostile-world actors: remains admissible; hostile actors take real native damage.
- Protected neutral/noncombat bystanders: current preflight/per-target protection remains.
- Beneficial area effects: current per-target containment remains; unrelated assistance is not promoted into Duel virtual health.

World-real `ReduceHP` calls explicitly bypass `CaptureNativeReduceHp`, so outside hostile damage can never be converted into Duel virtual damage merely because a Duel is active.

## Diagnostics

The bounded last-damage diagnostic now records: native entry/source, source role, target role, raw damage, `reduceHpCaptured`, captured effective damage, native result, virtual scale (`1.000` for a captured Duel hit), virtual before/delta/after, real-ledger before/after, mirrored HP after, virtualization authority, and `worldDamagePreserved`.

## Verification status

- Source scope/static validation: required and performed in the handoff generation pass.
- Pure C# deterministic executable: not run here (compiler/PowerShell unavailable).
- Current installed-reference build: not run (`Assembly-CSharp.dll`/Unity refs unavailable).
- Install/hash: not run.
- Live melee/self-heal/AoE/yield/repeat: not run.
- Restart/persistence: not run.

A live PASS must begin with repeated player->Sim and Sim->player melee. A healthy participant must not yield from a small hit; `/eduel diag` should show a small captured effective amount and unchanged real ledger rather than a multi-billion virtual delta.
