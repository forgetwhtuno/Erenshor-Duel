# Changelog

## 0.4.17 - stable-world release candidate

- Reconciles the approved 0.4.16 combat/lifecycle/rematch behavior, normal-mode
  forensic logging gate, stable-world launcher readiness, and shared collapsed
  fallback UI integration under one release identity.
- Combat semantics remain frozen: virtual HP, native damage capture, spells,
  cleanup, rematch rules, and world-combat authority are unchanged in this
  bookkeeping pass.

## 0.4.16 - diagnostic performance gate

- Freezes the live-good 0.4.15 duel lifecycle/combat behavior and moves forensic combat telemetry behind a new Lunaris `Diagnostics/Verbose` setting that defaults OFF. The supplied live session contained 204 Practice Duel debug lines out of 272 total log lines, including per-hit damage/heal, spell admission/commit, AoE, assist, and attribution records; synchronous debug I/O is therefore a plausible stutter contributor even though the log alone cannot prove frame-time causation.
- Major lifecycle observability remains always-on at low frequency: state transitions, concise duel start/terminal records, and cleanup-gate snapshots still appear so live QA can prove `Preparing -> Countdown -> Active -> Cleaning -> Idle` without enabling per-hit logging. Errors/warnings are unchanged.
- `DiagnosticVirtual`, throttled interference diagnostics, and `DiagnosticRecord` now fail fast before their own formatting/dictionary/logger work when verbose mode is off. Existing `/eduel diag` player-facing diagnostics and all gameplay policies remain unchanged.

## 0.4.15 - recent-duel admission repair (explicit vs autonomous)

Admission policy only. Frozen and untouched: `DuelArmingPolicy`, countdown behaviour, the
`Stats.ReduceHP` transaction, virtual HP, combat math, native abilities, combat-text attribution,
the `NPC.Combat` Cleaning re-entrancy repair, world combat policy, and the launcher/UI workspace.

**Fixed: a deliberate re-challenge was refused for ~2 minutes after a duel.**
- Live: the lifecycle repair works (`Active -> Cleaning`, `Cleaning -> Idle`,
  `admissionBlocked=False`, `reason=cleanup_complete`), but a second explicit challenge of the same
  Sim was then rejected with `decision=decline_recent_duel`, repeatedly, for Cyndara and Phanty.
- Root cause: `RecentDuelCooldownSeconds = 120f` is a **social** cooldown whose purpose is to stop
  AI-driven challenge spam, but it was applied to every request regardless of who asked.
  `DuelChallengePolicy.Evaluate` checks `RecentDuel` as its first hard gate, and
  `EvaluateWillingness` filled that flag from a single 120s window. The real inter-duel safety
  window is the Cleaning interval, which the lifecycle state machine already enforces separately.
- Repair: request origin is now explicit. `DuelRequestOrigin.ExplicitPlayer` (a deliberate
  `/eduel <Sim>`, `/eduel <A> vs <B>`, Sim Actions **Practice Duel**/**Arrange Sim Duel**, or
  Challenge Nearby) clears only a 1-second technical debounce; `DuelRequestOrigin.Autonomous`
  (reserved for future Nemesis/ambient/rematch-offer integration) keeps the full 120s social
  cooldown unchanged. One shared ledger, two windows, selected by the pure and unit-tested
  `DuelChallengePolicy.RecentDuelWindowSeconds`.
- `Start()` and `StartSpectator()` now **require** an origin argument with no default, so a future
  autonomous caller cannot silently inherit explicit-player treatment by omitting it.
- Nothing else was relaxed. Mechanical eligibility, remote-COOP exclusion, protected actors, unsafe
  combat state, same-Sim rejection, low health, the lifecycle/Cleaning gate, and every other
  existing safety failure reject exactly as before, identically for both origins.
- `/eduel nearby` and the eligible-name listings now evaluate as an explicit request, so they no
  longer advertise a `decline_recent_duel` the player would not actually receive.

Note: in practice the 1s debounce is effectively inert, because a duel cannot complete faster than
its own acceptance delay plus countdown (~4s) and the ledger timestamp is written at acceptance. It
is retained as a cheap, explicit double-input guard rather than as a gate.

## 0.4.14 - UI workspace normalization

UI/layout only. Combat transaction, lifecycle, the 60-second timer, combat-text attribution, and
the Cleaning gate (0.4.4-0.4.13) are all unchanged and frozen.

- The standalone PRACTICE DUEL fallback panel's status box is now 68px instead of a fixed 88px
  shared by every module regardless of content. Duel's guide text alone is two lines and status can
  grow to list eligible Sim names, so it keeps more headroom than Follow's 56px. Opt-in via the new
  `StandaloneFallbackUi.ConfigureWorkspaceDefaults(...)`, so unrelated fallback-panel users
  (Campmaster, Deep Sims, Nemesis) are byte-for-behavior unchanged.
- New default panel position: opens into a shared right-side workspace below the launcher column,
  above the combat/chat log - not overlapping it - instead of the previous default that could sit
  lower-center/right over important native UI. An existing saved position is preserved exactly.
- Duel's launcher column slot changed from 2 to 1 (order is now Journal=0, Duel=1, Follow=2).
- Fixed the shared launcher-column right margin, which previously resolved flush to the screen edge
  with zero margin at any realistic resolution (see Journal 0.1.11 for the root cause).
- The standalone launcher now shows a small structural open/active indicator (a top-edge accent bar
  shared with Journal/Follow) instead of relying on panel visibility alone.

## 0.4.13 - duel arming / Cleaning-NRE repair

Lifecycle only. The combat transaction is unchanged and frozen: `Stats.ReduceHP` capture, virtual
HP, damage values, mitigation, native spell use, procs, lifesteal/healing, AoE containment,
spectator combat, yield thresholds, the 60-second timer, combat-text attribution, the repeated-duel
Cleaning timer, Sim Actions, and the launcher are all untouched.

**Fixed: participants attacked before the countdown finished.**
- Root cause: the 0.4.11 combat-text attribution repair pinned `NPC.CurrentAggroTarget` onto the
  duel opponent from `NPC.Combat`/`NPC.CheckAssist`, but gated that repin on the session-wide
  `Active` property, which is true for `Preparing` **and** `Countdown` as well as `Active`. So the
  pair was pinned onto each other the moment the challenge was accepted, and native AI began
  attacking during the 3/2/1 countdown. The live log showed exactly this:
  `combat_text_attribution ... stage=NPC.CheckAssist sourceRole=DuelParticipant` between
  `Idle -> Preparing` and `Preparing -> Countdown`.
- Second gap: `NPC.Combat` calls `PerformMeleeHit` directly (verified in the installed
  `Assembly-CSharp.dll` at `IL_0314` and `IL_048F`). `AllowCombatAction` only covered
  `DoAttackSpell`/`DoAttackSkill`, so plain melee had no pre-GO gate at all.
- Repair: a new pure `DuelArmingPolicy` defines "armed" as `Active` only. The attribution repin now
  *disarms* the participant<->participant edge before GO instead of pinning it, and a narrow
  admission gate in front of `NPC.Combat` refuses that one edge while unarmed. Nothing else is
  touched: a non-participant NPC, a bystander Sim, and a participant genuinely fighting a hostile
  world actor all run completely vanilla. No arena bubble, nothing globally frozen, no timescale
  change.
- Real health was already safe before GO and stays that way: pre-`Active` duel-pair damage returns
  zero and skips native `DamageMe`/`MagicDamageMe`/`BleedDamageMe`/`SelfDamageMe`/
  `SelfDamageMeFlat`/`DamageShieldTaken` entirely, offensive spells are refused with
  `lifecycle_not_active`, and status/heal ingress is blocked. Because native damage never runs,
  `Stats.ReduceHP` is never reached and no native death path can be entered through the duel pair.

**Fixed: `NullReferenceException` in `NPC.Combat` during Cleaning after a spectator duel.**
- Root cause (verified against the installed assembly): `NPC.Combat` executes
  `IL_0314 call PerformMeleeHit` then, with **no null guard at all**,
  `IL_031A ldfld CurrentAggroTarget` / `IL_0324 stfld Character::RecentDirectHit`. When a duel
  reaches its yield threshold inside that melee hit, `ApplyVirtualDamage` calls `Stop()`
  synchronously from the damage prefix, and terminal cleanup nulled `CurrentAggroTarget` while that
  native frame was still on the stack - so the store at `IL_0324` dereferenced null. Spectator duels
  hit this far more often because both participants drive `Combat()` themselves and so are much more
  likely to deliver the finishing blow from inside that frame.
- Repair: the native `Combat` frame is now scoped (prefix marks entry, a `[HarmonyFinalizer]`
  releases it even if native code throws, and the exception is returned unchanged - never
  swallowed). Every duel-owned `CurrentAggroTarget` write during teardown goes through one
  re-entrancy-safe writer that defers the write until the frame has returned, then applies the real
  disarm. No `NullReferenceException` is caught, no dummy target is fabricated, and `NPC.Combat` is
  never suppressed globally. Post-duel target restoration is unchanged and still proof-based: only a
  target that is alive with live `Stats` is ever restored, otherwise the NPC is left in a native
  non-combat state for `DoNonRaidBehavior` to reacquire normally.

**Diagnostics**
- New bounded, throttled `preactive_duel_combat` record (`state`, `sourceRole`, `targetRole`,
  `entry`, `currentAggroTarget`, `playerCurrentTarget`, `action`) identifying the exact native entry
  point of any pre-GO duel-pair attempt.
- The Cleaning-time participant record now also reports `npcMyStatsExists`, `actorMyStatsExists`,
  `npcThisSimLinkageValid`, `insideNativeCombat`, `deferredDisarmPending`, and
  `safeRestoreEligible` - exactly the preconditions native `NPC.Combat` dereferences unguarded.
- The `duel_start` build label was stale (`0.4.11-rc-...` while the plugin reported 0.4.12); it now
  tracks the current source.

## 0.4.12 - shared standalone-launcher visual/placement pass

- The standalone PRACTICE DUEL launcher (`Erenshor-Mod-Suite/shared/ErenshorSuite.UI/StandaloneFallbackUi.cs`,
  used whenever Suite Hub is absent/unhealthy) now matches Journal's canonical launcher chrome exactly:
  154x32 launcher, 20px grip, 1px outline frame, and a centered three-dot grip accent - previously a
  plain colored rect with a slightly different ad-hoc dot layout.
- New default standalone position: a vertical right-side column beneath the native minimap area
  (Journal/Follow/Duel occupy fixed, non-overlapping slots), replacing the old lower-left default. No
  stable minimap RectTransform exists in the installed assembly to derive an exact lower edge from, so
  the column uses a resolution-independent top-right anchor with a conservative fixed inset.
- Fixed a pre-existing defect: dragging the launcher never actually persisted its position (the shared
  save path only recognized the larger fallback panel). The launcher now saves/restores its own
  position the same way the panel already does, so an existing install with no real saved launcher
  position adopts the new right-side default automatically; any future saved position is preserved.
- Added `src/StandaloneLauncherColumnPolicy.cs`, a small per-module copied policy (same convention as
  `StandaloneLauncherVisual.cs`) giving Duel its column slot (2 of 3, after Journal and Follow).

## 0.4.11 - combat-text attribution repair

- Targeting/attribution only. The combat transaction is unchanged and frozen: scoped
  `Stats.ReduceHP` capture, virtual HP, damage values, mitigation, native spell use, proc handling,
  lifesteal/healing, AoE containment, spectator combat, yield thresholds, the 60-second timer, the
  Cleaning lifecycle, post-duel NPC target restoration, and the Sim Actions UI are all untouched.
- Fixed player-facing combat text showing impossible lines during a duel, e.g.
  `Dancer attacks Dancer for 38 damage.` and `Dancer BACKSTABS Dancer for 427 damage!`, while the
  damage ledger itself remained correct.
- Root cause (verified against the currently installed `Assembly-CSharp.dll`): native Erenshor
  builds its combat-log line entirely from the acting NPC's own GameObject name and its
  `CurrentAggroTarget`'s GameObject name - `NPC.PerformMeleeHit` emits
  `base.transform.name + " attacks " + CurrentAggroTarget.transform.name`, and the skill variant
  emits the same two sources around `_skill.NPCUses`. Native code never consults Duel. Several
  native routines inside a single `NPC.DoNonRaidBehavior` frame assign `CurrentAggroTarget` with a
  direct field store - notably `NPC.CheckAssist`, whose group-assist branch copies
  `GameData.PlayerControl.CurrentTarget` verbatim onto every grouped Sim. Because Duel deliberately
  pins the player's target to the duel opponent, the opponent's own `CheckAssist` can park it on
  **itself**, and `Combat()` then runs in that same frame - rendering `<Sim> attacks <Sim>` a full
  frame before the controller's per-frame pin could correct it.
- Fix: correct the Duel-owned targeting state at the moment native code reads it, rather than
  patching, suppressing, or reconstructing any combat text. A prefix on `NPC.Combat` (inert unless a
  duel is active **and** that exact NPC is one of its two participants) re-pins a participant onto
  its duel opponent, and the existing `CheckAssist` hook now corrects duelists forward instead of
  skipping them. Genuine hostile-world PvE aggro still outranks the duel pin, exactly as the
  per-frame pin already allowed, so world PvE combat text remains completely vanilla.
- The decision itself is a new pure, Unity-free `DuelCombatAttributionPolicy` used by production
  and covered by deterministic self-tests for all four attribution directions (player->Sim,
  Sim->player, spectator first->second, second->first), the non-aliasing invariant, hostile-world
  PvE precedence, and non-participant/world combat being untouched.
- Added a bounded, throttled `combat_text_attribution` diagnostic (mode / source and target role /
  native display names / current aggro and player target roles / damage entry) that fires only when
  a correction is actually applied, to confirm the source in live retest.

## 0.4.10 - Cleaning-gate live repair (repeated duels)

- Lifecycle/admission only; virtual HP, ReduceHP capture, damage calculation, spell admission,
  native Sim AI, self-heal handling, spectator combat, AoE, world damage, yield thresholds, the
  60-second max fight duration, and NPC target restoration are all unchanged and frozen.
- Fixed a live defect where, after a duel completed normally, a second duel could not be started:
  the game correctly reported `Active -> Cleaning`, but `Cleaning -> Idle` never appeared during
  play - only at application shutdown. Root cause: `RunPostDuelAttackCleanup`'s top fast-exit
  guard and its bottom "finalize now" decision used the exact same condition
  (`frames <= 0 && time >= deadline`). The 6-frame settle budget is always spent within a handful
  of ordinary frames, long before the 0.75s time deadline, so by the frame the deadline was
  finally reached, the frame budget had already gone to zero on an earlier frame - meaning the TOP
  guard fired and returned before the bottom code could ever call `EndPostDuelAttackCleanup()`.
  Nothing else called it again except `Shutdown()`'s direct, unconditional call at application
  exit, exactly matching the reported symptom.
- Fix: an explicit `_postDuelCleanupPending` flag now gates the top of the function, set only by
  `BeginPostDuelAttackCleanup` and cleared only by `EndPostDuelAttackCleanup`. The actual
  finalize decision is now a single pure, independently-tested function
  (`DuelSafetyPolicy.ShouldFinalizeCleanupPass`), consulted once per tick instead of twice with
  overlapping semantics. The 6-frame/0.75s safety window and every native-state restoration step
  are byte-for-byte unchanged.
- Added a bounded `cleanup_tick` diagnostic (state/now/cleanupUntil/cleanupPassComplete/
  admissionBlocked/reason) logged at cleanup start, cleanup completion, and at most once per
  rejected challenge during Cleaning - never per frame.
- Added a pure-logic regression proof: the deterministic self-tests now simulate both the OLD
  (buggy) combined-guard shape and the NEW pending-flag shape across two full simulated seconds at
  60fps, using the exact same production decision function, proving the old shape never finalizes
  and the new shape reliably does.

## 0.4.9 - Sim Actions fallback UI geometry repair

- UI geometry only; combat transaction, lifecycle, target restoration, the 60-second timer, and
  Follow compatibility are unchanged and frozen.
- Fixed the standalone **SIM ACTIONS** fallback panel rendering with a large empty cyan block above
  a header that appeared far below the panel's actual top edge, with content pushed down and
  overlapping the header/body boundary. Root cause: the panel's body background (`inner`) was built
  with a fixed size that never tracked the panel's own height, which grows every time content
  changes; the header was anchored to that stale fixed-size body's bottom instead of its top, so
  both drifted away from the panel's real, dynamically-grown bounds. `inner` now stretch-anchors to
  the live panel bounds (with a deliberate 1px accent border) and the header top-anchors within it,
  so header/body/panel always agree on one coordinate system with no separate resize bookkeeping.
- Panel width increased from 220px to 260px (within the intended 240-300px compact range). Header
  height is a single named 30px constant. Selected-Sim menu, Choosing-Opponent, and Sim-vs-Sim
  Confirm all share the same corrected row/button geometry, since they were already built from the
  same shared hierarchy - fixing it once fixes all three states and the inline rejection/status text.
- Added a 23-assertion source geometry matrix asserting header height/panel width bounds, top
  anchoring, body-starts-below-header, bounded button heights, content-driven panel height, and that
  Follow suppression behavior is unchanged.

## 0.4.8 - mouse-click Sim Actions discoverability

- Combat transaction and lifecycle (0.4.4-0.4.7) are unchanged and frozen; this is a presentation
  workstream only.
- Fixed the core standalone UX gap: with Duel installed alone, clicking a Sim opened no menu at all,
  and Sim-vs-Sim spectator duels were reachable only through `/eduel <Sim A> vs <Sim B>` from the
  README. Clicking an eligible local Sim now opens a small retained **SIM ACTIONS** menu exposing
  **Practice Duel** (player-vs-Sim) and **Arrange Sim Duel** (spectator), styled to match the same
  dark/translucent/cyan presentation the rest of the collection uses.
- Ownership rule: **Erenshor Follow owns the full Sim Actions system whenever Follow is installed and
  healthy.** Added `DuelFollowCompatibility`, a reflection-only (no compile-time reference) probe of
  Follow's public `FollowControlApi.GetStatus()`, classified by the new pure
  `DuelFollowCompatibilityPolicy.ClassifyStatus`. Duel's own native-click observation
  (`PlayerControl.LeftClick` / `Character.TargetMe`, the same hook points Follow's own Sim Actions
  menu uses) checks this live at every entry point and stands down immediately whenever Follow is
  healthy, so there is exactly one Sim Actions interaction regardless of load order, and Follow
  becoming healthy while the fallback is open disposes it cleanly on the very next tick.
- Arrange Sim Duel is a small click-to-click state machine (`DuelSimActionsFallbackPolicy`, pure and
  unit-tested): click a Sim, choose Arrange Sim Duel, click a second eligible local Sim, confirm with
  Start/Cancel. The same Sim cannot be picked for both sides; an ineligible second Sim is rejected
  with the real reason and the picker stays open; a first or second Sim that becomes hard-invalid
  (gone, dead, wrong zone) before confirmation cancels cleanly with an explanation rather than
  failing silently or leaving stale state behind.
- Both the click path and `/eduel` now provably share one eligibility decision and one start path:
  `DuelController.EvaluateEligibility` was widened from `private` to `internal` so the fallback UI
  calls it directly instead of re-implementing it, and both **Practice Duel**/**Start** call the
  existing `DuelController.Start`/`StartSpectator` entry points unchanged. `DuelController.
  ReportEligibilityFailure`'s chat wording and the new inline rejection text now both come from one
  new shared function, `DuelEligibilityPolicy.DescribeForUi`, instead of two copies of the same
  strings.
- No combat, eligibility, or lifecycle logic was duplicated, and no second spectator-combat system
  was introduced; `StartSpectator` remains the only place a spectator duel actually starts.
- `/eduel <SimName>`, `/eduel <Sim A> vs <Sim B>`, `/eduel nearby`, `/eduel status`, `/eduel diag`,
  `/eduel selftest`, and `/eduel stop` are all unchanged and still work with no Sim Actions menu open.
- Added a 21-case source-contract matrix pinning the Follow stand-down behavior, the shared
  eligibility/start paths, the no-hard-Follow-reference rule, and the unchanged command path, plus
  full pure self-test coverage for the classification and state-machine policies. All pre-existing
  0.4.4-0.4.7 combat/lifecycle/Cleaning-gate matrices (94 cases) pass unchanged.

## 0.4.7 - release candidate: faster Cleaning gate, 60s fights, standalone discoverability

- Semi-tested release candidate; combat transaction (0.4.4-0.4.6) is unchanged and frozen.
- Investigated the live report of a stuck Cleaning gate ("Finishing cleanup from the previous
  duel. Try again in a moment." on repeated retry). Root cause: `Tick()` already called the
  post-duel maintenance pass (`RunPostDuelAttackCleanup`) ahead of its own `!Active` early return,
  so Cleaning was never actually starved of ticks - but `BeginPostDuelAttackCleanup` required both
  a 6-frame scrub **and** a full ~2 real seconds to elapse before the gate could open, so a player
  retrying within that window was correctly refused every time, and at ~2 seconds that reads as
  stuck. `Tick()` is now split into `TickPostDuelMaintenance()` (always runs, every lifecycle
  state, no Active/`_state` gate of its own) and `TickCombatSession()` (owns the Preparing/
  Countdown/Active state machine and is the only half gated on `Active`), so the "maintenance
  keeps running through Cleaning" property is now structural rather than an emergent side effect
  of call order.
- Added a named `PostDuelCleanupSeconds = 0.75f` constant (replacing a scattered `2f` literal) so
  the visible Cleaning gate targets roughly 0.75 seconds under normal operation instead of ~2. The
  existing `PostDuelCleanupFrames = 6` multi-frame native target/attack scrub is preserved
  unchanged - the gate is faster, not skipped.
- Raised the Practice Duel active-fight timeout from 30 to 60 seconds via a single named
  `MaximumFightSeconds` constant; the timeout chat message now derives from the same constant
  instead of a hardcoded "30 seconds" string. Countdown (3s), acceptance delay (1s), and the
  recent-duel cooldown (120s) are unchanged.
- Standalone discoverability: with no Suite Hub and no Follow installed, the existing retained
  fallback panel's status line now names the actual eligible local Sims (bounded, with a "+N more"
  overflow) instead of only a count, and its guide text now spells out
  `Sim vs Sim: /eduel <Sim A> vs <Sim B>` so spectator mode is discoverable without a README
  lookup. The existing Challenge Nearby / Stop Duel buttons and status text are unchanged; no
  per-Sim dynamic buttons were added, since that would require expanding the shared
  `StandaloneFallbackUi` framework, which this workstream does not modify.
- Added deterministic coverage: an explicit "Cleaning admits no virtual combat, and a challenge is
  accepted immediately once CleanupComplete fires" assertion in the pure lifecycle self-tests, plus
  a 14-case source-contract matrix (Tick/TickCombatSession/TickPostDuelMaintenance structure, the
  60s/0.75s named constants, shared StopInternal path for manual stop and timeout, and the
  "new unrelated target during Cleaning is not stomped" guard) and a 3-case standalone
  discoverability guard. All pre-existing 0.4.4-0.4.6 combat forensic/recovery matrices (63 cases)
  pass unchanged.

## 0.4.6 - NPC self-cast misclassification and diagnostic truncation

- Fixed the opposing duelist's offensive spells being resolved onto the caster. A live duel recorded
  `Burning Chains` (StatusEffect, `TargetDamage=50`) cast by the Sim with the caster as the
  `StartSpell` Stats argument, classified `self_contained_self_cast`, and applied to the caster
  instead of the player. Root cause: the 0.4.2 repair inferred self-application from "target argument
  equals caster", which is sound for the player (Hotkeys hands `StartSpell` the selected target) but
  not for an NPC. Verified in the installed `Assembly-CSharp`, all three `NPC.DoAttackSpell` call
  sites pass `CurrentAggroTarget.MyStats` (IL_0a22/IL_0bd7/IL_0d9f), while other native NPC cast
  paths pass the caster's own Stats for spells that still resolve onto the opponent.
- A spell that damages its target (`TargetDamage`, `BleedDamagePercent`, or `Lifetap`) can now only
  become a self-cast when the spell asset declares it via `SelfOnly` / `ApplyToCaster` /
  `InflictOnSelf`. Declared self-application still outranks the qualifier, so genuine self-damage
  effects are unchanged, and non-damaging self-buffs/self-heals keep the existing behavior. The
  0.4.5 targeted heal adaptation is untouched.
- Fixed damage/healing telemetry being cut mid-field. `DiagnosticVirtual` routed through the 120-char
  `SafeLabel` cap, so every live `native_damage` / `virtual_damage` / `virtual_heal` line ended at
  `virtualAfte` and lost `virtualAfter`, `realBefore`, `realAfter`, `yieldThreshold`, `yield` and
  `reason` - exactly the fields that distinguish a virtualized hit from a preserved world hit. These
  records now use the record path, as do `world_damage`, `duel_start`, `duel_terminal`, `cleanup`,
  `diag=summary` and both `third_party_heal_blocked` lines.
- Raised the record ceiling from 400 to 900 characters. A live `spell_commit` measured 424 and lost
  `startSpellEntered`, `nativeResult`, `manaBefore`/`manaAfter` and `resourceCommitted` - the fields
  that say whether native `StartSpell` actually ran and what it returned.
- Added deterministic coverage for the damaging-spell qualifier and tightened the self-cast source
  guard to require it.


## 0.4.5 - targeted ordinary self-heal admission

- Preserves the live-proven 0.4.4 scoped `Stats.ReduceHP` damage transaction unchanged.
- Fixes ordinary single-target `Heal` spells such as Minor Healing when the opposing duelist remains selected. During Active only, if the exact duelist caster receives the exact opposing duelist as the `StartSpell` Stats argument, and the spell is a contained healing-only single-target shape, the Harmony prefix rewrites only that method argument to the caster's own Stats. `PlayerControl.CurrentTarget` is never changed.
- The adaptation is deliberately not a generic beneficial retarget: area/group, summon, charm, proc, mixed damage/heal, and unknown utility shapes remain under the existing containment rules.
- Native `CastSpell.StartSpell` still owns mana, cooldown, cast time, animation, and heal amount; existing `HealMe` capture translates the native self-heal into virtual Duel HP exactly once.
- Declared `SelfOnly` / `ApplyToCaster` / `InflictOnSelf`, AoE/world-combat, lifesteal, protected-NPC, COOP, and cleanup behavior are preserved.


## 0.4.4 - scoped ReduceHP damage transaction restoration

- Repairs the 0.4.3 instant-yield regression. The temporary synthetic HP-headroom transaction is removed from participant damage: Duel no longer assigns a massive temporary `CurrentHP` value and no longer derives virtual damage from a synthetic before/after HP delta.
- Restores the live-proven 0.4.1 semantic boundary without rolling back newer work: native `DamageMe` / `MagicDamageMe` / `BleedDamageMe` still perform Erenshor mitigation/resistance/crit/class math; the exact in-flight participant `Stats.ReduceHP` call captures the final effective amount, returns `false` to suppress that one real/mirrored HP write, and `FinishNativeDamage` applies the captured amount to virtual HP exactly once.
- Modernizes the old capture with the current 0.4.3 `NativeDamageState.Previous` transaction stack. Nested procs/retaliation can push/pop scoped states without overwriting an outer transaction, and `WorldReal` hostile-world damage explicitly bypasses Duel capture so native world HP/death remains authoritative.
- Keeps the current source/target authority matrix: duel participant edges virtualize; duelist <-> verified hostile-world actors remain real native combat; protected/friendly/unknown unsafe edges remain contained. The actor/effect-aware AoE policy, protected-NPC preflight/per-target containment, and hostile-world coexistence are unchanged.
- Preserves the opponent-selected self-cast repair (`SelfOnly || ApplyToCaster || InflictOnSelf`) and native mana/cooldown/cast ownership. Self-heal/HoT/lifesteal/resource effects remain native-adjacent and update virtual health through the existing bounded heal path.
- Strengthens `/eduel diag` damage evidence with native entry, source/target role, raw amount, whether `ReduceHP` was captured, captured effective amount, virtual scale/delta/before/after, real-ledger before/after, virtualization flag, and whether world damage was preserved.
- Reverses the 0.4.3 source-contract guard that required observation-only `ReduceHP`; deterministic/source guards now require scoped capture, nested-stack ownership, no synthetic headroom path, current self-cast admission, current AoE/world authority, and cleanup ownership.

## 0.4.3 - combat semantics, hostile-world authority, and AoE containment

- Fixes the 0.4.2-era zero-damage regression by removing duel ownership of `Stats.ReduceHP`: the Prefix now observes only. Admitted participant hits once again let native `DamageMe` / `MagicDamageMe` / `BleedDamageMe` run against temporary non-lethal HP headroom, measure the completed native HP delta, restore the duel mirror, and apply that effective amount exactly once to virtual HP.
- Preserves the 0.4.2 self-cast admission repair. `SelfOnly`, `ApplyToCaster`, and `InflictOnSelf` remain authoritative declarations of caster application even when `StartSpell` receives the selected opponent's `Stats`. Native mana/cooldown/cast behavior is still owned by Erenshor.
- Adds explicit source/target damage authority: exact duel participant edges are virtual; duelist -> verified hostile-world NPC and hostile-world NPC -> duelist are real native Erenshor combat; friendly/protected/unknown nonparticipant edges remain blocked. Ordinary hostile aggro no longer cancels a duel solely for existing.
- Adds a separate real-world HP ledger while virtual HP is mirrored. Hostile-world damage is applied against the real ledger and survives duel cleanup; a real native death is never overwritten by duel restoration. Damage-shield retaliation receives the same treatment, including nested native-damage detection.
- Tightens hostile classification. Vendors, villagers/friendly factions, Sims, player/group actors, summons/pets, mining nodes, treasure chests, `NeverAggro` NPCs, and unresolved actors cannot be promoted to hostile-world authority merely because an `NPC` component exists.
- Adds AoE-aware StartSpell admission for `AE`, `PBAE`, and `GroupEffect` shapes. The bounded preflight uses current native `NearbyEnemies` / `NearbyFriends` candidate collections and exact actor roles; hostile-world candidates are explicitly allowed. The supplied source/refs expose no authoritative spell radius/shape-distance member, so the runtime records `radius=unavailable_in_supplied_api` rather than inventing one. Per-target damage/heal/status hooks remain the final containment boundary.
- Beneficial AoE/self-resource effects stay native for the caster/participant while unrelated beneficiaries are suppressed per target. Unsafe summon/charm/proc area shapes remain blocked. Offensive effects reaching protected/unknown nonparticipants are refused and surface the existing-chat message `Can't use that here — someone else is in the blast.`
- Hostile-world status effects are adopted slot-by-slot into the real cleanup baseline rather than snapshotting all current duel effects. Tracked world-owned effect durations can advance/expire without causing duel-only buffs/debuffs to persist after cleanup. Mixed unattributed periodic bleed sources fail safely instead of guessing whether a world tick is virtual.
- Expands `/eduel diag` with the last damage authority decision, last AoE preflight, real-ledger values, and native StartSpell mana before/after. Cooldown commitment remains explicitly `unavailable_in_supplied_api` until the current installed assembly can be inspected live.

## 0.4.2 - self-cast spell admission repair

- Fixes legitimate self-heals, HoTs, self-buffs and other caster-applied spells being silently refused during an Active duel whenever the opponent was selected: the cast did not begin, consumed no mana, and started no cooldown.
- Root cause: duel spell admission decided "self-cast vs cast at the opponent" from the `Stats` argument handed to `CastSpell.StartSpell`, and only consulted the spell's own `SelfOnly`/`ApplyToCaster`/`InflictOnSelf` flags when that argument was null. Installed `Assembly-CSharp` `Hotkeys::DoHotkeyTask` (self branch, IL_00C5) assigns `PlayerControl.CurrentTarget` into that argument for a `SelfOnly` spell whenever anything is targeted, and only substitutes the caster when nothing is targeted; the real self-redirection happens afterwards inside `CastSpell::StartSpell`, which reads `Spell.SelfOnly` itself. A self-cast made with the opponent selected was therefore misread as a beneficial spell aimed at the opponent, which fails the offense test (`TargetHealing > 0`, or the `Beneficial`/`Heal` spell types), so the Harmony prefix returned false and native `StartSpell` never ran.
- Repair: self-application is now recognised from the spell asset itself, independent of the caller-supplied target. The fix only stops the incorrect block - it does not synthesise mana, cooldown, healing, or animation; native `StartSpell` performs the cast, resource cost, cooldown and self-redirection exactly as vanilla does.
- Containment is unchanged: an admitted self-cast must still pass the self-contained test, so group effects, pet summons and charms remain blocked, offensive casts still route through duel virtualisation, and third-party/outside-hostile handling is untouched.
- Adds `DuelSpellAdmissionPolicy`, a pure no-Unity policy that owns the self-application/self-cast/containment decisions so the shipped logic is the logic the offline deterministic suite exercises, including an explicit assertion that the pre-repair calculation would have missed the regression.
- Adds bounded per-decision spell-admission telemetry (spell name, native type, caster/target roles, the three self flags, computed self-cast, `TargetHealing`/`TargetDamage`, group/pet/charm shape, admission, reject stage, and whether native `StartSpell` was allowed to run), surfaced through `/eduel diag`. One line per admission decision, no per-frame logging, no private state.

## 0.4.1 - RC terminal cleanup proof

- Restores party Guard/follow movement ownership from both normal and emergency terminal paths before participant references are cleared.
- Adds deterministic source-contract guards for health/effect, pet, enemy-list, movement, attack-loop, session, zone, invalid-participant, external-combat, timeout, and repeat-duel cleanup boundaries.
- Adds an exact version to startup output and a release-identifiable build id.

## Unreleased - deep state ownership / repeated-duel hardening

- Replaced the implicit active/terminal split with an explicit `Idle -> Preparing -> Countdown -> Active -> Cleaning -> Idle` lifecycle. The two-second terminal scrub is now a real `Cleaning` state that blocks a new challenge until cleanup finishes instead of mutating combat after the session appeared idle.
- Made terminal restoration ownership-aware: a new unrelated player/NPC target selected after the duel is preserved; post-duel autoattack cleanup stops policing the player as soon as native gameplay selects an unrelated target; nearby-enemy restoration is additive-only and never removes relationships that appeared during the duel.
- Cancel active duels when a participant changes party scope, when a direct outside/unknown actor damages or acquires aggro on a duelist, or when participant identity/locality becomes invalid. Known friendly-party interference remains blocked rather than dogpiling.
- Fail duel start closed when player-health or native-autoattack evidence cannot be read safely. Clear duel-scoped thread-static damage/effect ownership on every terminal path and guard native damage postfixes against stale re-entrant completion after cleanup.
- Added an emergency best-effort restoration path for unexpected cleanup exceptions and clear queued name-based Hub challenge requests on scene load/unload so delayed control callbacks cannot cross zone boundaries.
- Extended deterministic policy coverage for lifecycle transitions, ten repeated duel cycles, party-scope changes, target/autoattack ownership, direct hostile ingress, additive enemy-list restoration, and exactly-once terminal behavior.
- Combat formulas, reward behavior, native death suppression, eligibility scope, spectator feature scope, and optional social integration were not expanded by this pass.

## Unreleased - playable-state cleanup polish

- Made idle/repeated `Stop()` and normal plugin shutdown silent when no duel or residual participant state exists; cleanup still runs if any duel participant reference survives unexpectedly.
- Routed ordinary duel lifecycle diagnostics to debug severity instead of warning severity. Real exceptions/errors keep their existing error paths.
- Fail practice-duel start closed whenever native player autoattack is already active, so Duel never takes ownership of a pre-existing attack loop it would later need to reconstruct.
- No virtual-health, native effective-damage, reward, third-party isolation, faction-restoration, aggro cleanup, or duel-completion logic was weakened.

## Unreleased - Suite Hub control-surface refinement

- Kept Practice Duel combat/virtual-health containment unchanged; documented the existing shared Hub-aware retained fallback entry point rather than adding another gameplay UI surface.
- Bounded Hub-facing status to a concise active/idle line plus eligible-local-candidate count while preserving the authoritative `challenge(name)` and `stop` ControlApi/Aura actions.
- The current Hub renderer does not render arbitrary argument-entry actions, so `challenge(name)` remains transport/API-ready rather than inventing a fake selector or modifying Hub in this workstream.

## 0.4.0 - Native Lunaris migration

- Migrated off BepInEx 5 onto native Lunaris: `BaseUnityPlugin`/`[BepInPlugin]`/`[BepInProcess]`
  replaced by `LunarisPlugin`/`[LunarisPlugin]`/`[LunarisPermission(Reflection | Harmony)]`;
  `Logger.Log*` replaced by native `Logging.Log*`. This mod has no BepInEx config entries to
  migrate (`/eduel` has no persisted settings).
- This is a loader/logging/lifecycle migration only: no duel eligibility, safety-gate, virtual
  health, native-damage-routing, third-party-isolation, or cleanup logic changed. Every Harmony
  patch target was re-verified against the currently installed `Assembly-CSharp.dll`.
- `BUILD_AND_INSTALL.ps1` rewritten for Lunaris: install target is now
  `<Erenshor>\plugins\ErenshorDuel.dll`; reference resolution now looks for a Lunaris developer
  folder (`Lunaris.dll`/`0Harmony.dll`) instead of a BepInEx profile root; all
  r2modman/Thunderstore BepInEx-profile auto-detection removed.
- Added `tests/RUN_TESTS.ps1`: a new standalone deterministic test runner for the existing
  `DuelSelfTests.RunAll()` suite (challenge policy, eligibility policy, locality policy, identity,
  event contract, safety policy, Deep Sims compatibility) so it can be verified outside a running
  game. No test logic changed; this only makes the existing `/eduel selftest` suite runnable from
  the command line for migration verification.
- Verified: real compile against the installed Erenshor + Lunaris assemblies, zero `BepInEx`
  references in the compiled output, the full existing deterministic self-test suite passes (7/7
  policy groups via `tests/RUN_TESTS.ps1`), and a static hot-unload audit (the
  `SceneManager.sceneLoaded`/`sceneUnloaded` subscriptions installed in `Awake()` are unsubscribed
  in `OnDestroy()`; `Harmony.UnpatchSelf()` is called; `DuelController.Shutdown()` and both
  optional-integration `Reset()` calls run before the plugin instance reference is cleared; the
  only `AppDomain.CurrentDomain.GetAssemblies()` usages are throttled by assembly-count check, not
  cached via an `AssemblyLoad` subscription).
- Not yet done: live in-game verification under Lunaris, including hot unload during an active
  duel. `OnApplicationQuit()`/`OnDestroy()` both still call `DuelController.Stop()`/`Shutdown()` to
  restore real HP and participant state before teardown, unchanged from the prior BepInEx build,
  but this has not been re-confirmed live under the new loader.

## Unreleased development notes (not part of the public 0.3.1 release)

The entries in this section describe source work after 0.3.1. They are retained as development
history and must not be read as a claim that a separately released public build includes them.

- Nearby non-party Sims now accept direct practice challenges whenever the hard health, cooldown,
  locality, camp, COOP, and real-combat safety gates pass; level mismatch no longer looks like a
  broken or unavailable duel feature.
- Added explicit spectator duels with `/eduel watch <Sim A> vs <Sim B>`. Both local Sims fight
  through the existing virtual-health, spell, healing, pet, and third-party isolation boundary;
  both NPC combat states are restored afterward without redirecting the observing player's target.
- The natural shorthand `/eduel <Sim A> vs <Sim B>` now starts the same spectator match; `watch`
  remains a supported alias.
- Extended player-duel terminal attack cleanup to a two-second window and repeatedly clears both
  the native autoattack toggle and a stale duel-opponent target.
- Spectator-duel teardown now clears the accepted pets' target, combatant, and aggro-table links
  to both duelists throughout the terminal window, preventing a contributing pet from remaining
  in combat after a yield or timeout.

- Replaced the temporary `int.MaxValue` native-damage HP headroom with a duel-scoped
  `Stats.ReduceHP` capture. Erenshor still calculates mitigation, resistance, shields, and crits,
  but the exact admitted duel hit can no longer write real HP or enter its native death branch.
- Routed stance/self spell damage, flat self-damage, and damage-shield retaliation into virtual
  duel health. Environmental damage now cancels the friendly duel, restores real state, and then
  proceeds through Erenshor normally.
- Restored periodic bleed handling: Erenshor emits `BleedDamageMe` ticks without an attacker, so
  verified active effect ticks on a duelist are now contained as virtual damage instead of being
  silently discarded.
- Cleanup now restores full pre-duel status-effect slot state and spell-shield charges, rather
  than merely removing effects added during the duel. Native combat cannot consume a real
  breakable buff, duration, or shield charge through practice damage.
- Fixed nearby-duel locality to use the candidate Sim's loaded active Erenshor zone; the persistent player Character scene is never used as zone identity.
- Added `/eduel diag` scene/COOP/Campmaster diagnostics.
- Terminal cleanup now clears the player target when its saved pre-duel value was the duel opponent, repeatedly forces the native autoattack toggle off during the post-duel cleanup window, and logs terminal/cleanup state.
- Added per-duel virtual-health start, damage, heal, threshold, and blocked-third-party-heal diagnostics; `/eduel diag` now logs final eligibility evidence for every actual `SimPlayer`.
- Prefer `ErenshorCampmaster.CampmasterApi.IsHuntCampActive`; Relax no longer counts as Hunt Camp.
- Restricted pet participation to pets present at duel start and strengthened native-damage headroom.
- Hardened optional integration cache refresh, quit cleanup, cooldown pruning, and deterministic safety tests.

## 0.3.1 Deep Sims structured social bridge

- Bumped the optional `PracticeDuelEvents` contract to v2 and added a stable cancellation `ReasonToken` alongside diagnostic reason text.
- Prefer the optional static `ErenshorDeepSims.DuelEventBridge.NotifyDuelEvent(...)` structured bridge when present; fall back to the existing generic `NotifyObservedGameEvent(...)` key/value transport only when the structured bridge is unavailable/fails.
- Added authoritative cancellation tokens for hostile interruption, camp, distance, zone change, participant loss, internal safety cancellation, manual stop, and other cancellation.
- Kept completed-duel legacy fallback memory non-important so Deep Sims can apply its own restrained completion-only memory policy.
- No combat, eligibility, willingness, virtual-health, pet, spell, healing, or third-party isolation rules were changed by this bridge pass.

## 0.3.1 nearby completion — eligibility, identity, events, diagnostics, teardown

- Hardened nearby-local-Sim challenges without replacing the proven 0.3.x virtual-health combat boundary.
- Made target lookup explicitly same-scene and 25m-bounded, retained exact-name preference, and made duplicate exact names as well as multiple partial matches fail safely as ambiguous.
- Added `/eduel nearby` as a command-time-only diagnostic showing nearby `SimPlayer` distance, party/nearby scope, mechanical eligibility, and deterministic willingness token.
- Split mechanical target eligibility into a pure deterministic policy with self-tests for remote COOP, ordinary non-Sim NPC, same-scene, required combat components, distance, and real-combat rejection.
- Added a verified player-autoattack check to the real-combat start gate when autoattack is aimed at another living target.
- Changed cooldown and stable willingness variation to prefer the runtime-verified `SimPlayer.MySimTracking -> SimPlayerTracking.simIndex` identity shape; normalized display name remains a conservative fallback when that capability is unavailable.
- Moved recent-duel timestamping to the authoritative accepted transition instead of command issue time.
- Expanded willingness self-tests for party compatibility/cooldown, comparable non-party determinism, hard low-health/cooldown gates, bounded Rival influence, and large level mismatch.
- Added structured `PracticeDuelEvents` contract v1 for challenge/accept/decline/start/completion/cancellation with party-vs-nearby scope and verified outcome facts.
- Kept compatibility with the existing generic Deep Sims `NotifyObservedGameEvent` fallback; no LLM or Deep Sims dependency was introduced.
- Cached Deep Sims reflection discovery and throttled camp integration polling in the active duel loop instead of scanning assemblies every frame.
- Added plugin teardown for scene-event handlers, optional-integration caches, and the static plugin instance.
- Preserved non-party state isolation: no grouping, spawning, teleporting, `FreeFollow`, or party guard restoration is applied to nearby non-party opponents.
- Preserved native damage accounting, temporary exact-hit faction/layer restoration, spells/skills/debuffs, self-heals/HoTs/lifesteal/consumables, admitted existing pets, healer/buffer masking, assist suppression, fail-closed unknown third-party actions, hostile cancellation, and effect/aggro/attack cleanup.

Live validation remains required against the installed Erenshor build for the full party/non-party, caster/healer/pet, hostile-interruption, zoning, camp, and cleanup matrix.

## 0.3.1 follow-up 3 — Pet damage routing, debuffs, buff selection

- **Pet damage is routed into the match.** Damage, DoTs, and debuffs from a pet owned by either duelist now land on the opposing duelist's virtual health instead of being silently discarded. Ownership resolves through `Character.Master` (up to four links), so the pet counts as its owner for damage, aggro acquisition, attack spells/skills, and status effects — and only ever against the opposing duelist. Pets remain immune to damage: they are a conduit for their owner, not a target. Admitted pets are tracked and have their aggro released on every duel exit path. Summoning a *new* pet mid-duel is still blocked.
- **Debuffs work.** `IsSafeDuelOffense` was a whitelist of damage and crowd-control fields, so every pure debuff failed it — resist debuffs (`NPC.CheckResistDebuffs`), snares (`NPC.CheckSnareSpell`), and stat/attack-speed debuffs carry no `TargetDamage` and none of the CC booleans, and were refused at both `StartSpell` and `AddStatusEffect`. Classification now keys off the game's own `Spell.Type`: `Damage` and `StatusEffect` are admitted between duelists, `Beneficial`/`Heal`/`Pet`/`AE`/`PBAE` are not, and `Misc` keeps the old proven whitelist. Structural denies (group effect, pet summon, charm, proc grant) are checked along the whole `StatusEffectToApply` chain.
- **Buff selection.** `NPC.CheckBuffs` picks targets by walking `Character.NearbyFriends`, and both duelists sit in every bystander's list, so buffers re-selected them every AI tick even though the cast was refused downstream. Duelists are now removed from the candidate list for the duration of the pass and restored afterward, so the AI moves on to real targets.
- A duelist's self-applied status effects are now checked for group/summon/charm shapes too, closing the same hole on the `AddStatusEffect` path that was closed on `StartSpell`.

## 0.3.1 follow-up 2 — Damage gates and bystander isolation

- Fixed the temporary duel faction. `Character.Faction.Player` is `0`, which is the exact value `DamageMe`/`BleedDamageMe` reject non-physical damage on (`-3`) and `MagicDamageMe` rejects any NPC victim on (`-1`). Swapping duelists to it silenced all spell damage onto the Sim, every DoT tick, and every bleed, leaving only plain physical melee working. The duel now picks a temporary faction that clears all three installed gates: not `Player`, not `PC`, and not the attacker's own faction.
- Native duel hits now run against full health headroom and are measured by delta, so an overkill hit can no longer reach `ReduceHP`'s zero-HP branch and move a duelist to the corpse layer. The original layer is captured and restored regardless.
- Logged `nativeResult` per hit so a gate rejection (`-1`/`-2`/`-3`) is distinguishable from a real full resist (`0`).
- Suppressed real damage from unresolved actors against a duelist instead of letting it through. A nearby non-party Sim and its independent group classify as `Unknown`, and their damage was landing on real health that is currently standing in for virtual duel health.
- Closed the assist leak. `NPC.CheckAssist`, `CheckAssistRaid`, `ForceGroupOntoTarget`, and `ForceNewAggroTarget` assign `CurrentAggroTarget` by direct field store, so no `AggroOn`-based patch could see them; `Character.DamageMe` also calls `SimPlayerGrouping.GroupAttack` and `SimPlayerIndependentGroup.CallForAssist` against the attacker, meaning an ordinary duel swing was recruiting bystanders. All six are now filtered when they name a duelist.
- Duelists are removed from every group member's `Character.NearbyEnemies` during and after a duel; that list was previously left polluted with the player permanently.
- Non-duelist NPCs now see both duelists at full health during heal/buff selection (`CheckHeals`, `CheckHealsRaid`), so bystanding healers stop re-selecting duelists every tick.
- Blocked pet summons, charms, and group-wide effects cast by a duelist on itself. "Self-targeted" was previously treated as "inside the 1v1", which let a Druid or Necromancer summon mid-duel.
- Mirrored health is now clamped to the live `CurrentMaxHP` as well as the saved duel maximum.

## 0.3.1 follow-up — Native damage accounting

- Practice duel physical, magic, and bleed hits now use Erenshor's native damage result before applying virtual-health loss.
- Added raw/effective damage diagnostics so armor, resistances, buffs, and crit modifiers can be compared in the BepInEx log.
- Temporarily bypassed the installed faction protection only for the two active duelists, restoring the original faction after native processing (including exception cleanup).
- Unknown third-party actions are now blocked without falsely cancelling the duel.

## 0.3.1 — Nearby Local Sim Challenges (Development)

- Added bounded willingness checks and `/eduel selftest` for nearby local Sim challenges.
- Preserved party-Sim support and excluded remote COOP humans.
- Added optional Deep Sims lifecycle events with safe decline/completion diagnostics.
- Fixed a duel-end effect-cleanup no-op that let a whitelisted duel-safe DoT/root/stun applied late in a duel survive and apply for real against real HP after the duel ended.
- Patched the 3-arg `Stats.AddStatusEffect` overload, closing a bypass around every duel filter/cancellation check.
- Applied the recent-duel cooldown to party Sims as well, and gated `Start()` on the player's own health, not just the target's.
- Stopped an active duel immediately on a scene load/unload instead of waiting for the next polled tick, so real HP does not stay live through a zone transition.
- Excluded Coop `NetworkedSim`-owned Sims (not just `NetworkedPlayer`) from duel eligibility.
- Narrowed the party `GroupEffect` spell block to only spells actually targeting a duelist.
- Wrapped `Stop()`/`Clear()` cleanup paths reached from core-combat Harmony prefixes in try/catch.

## 0.3.0 — Isolated Full-Class Practice Combat (Development)

- Allowed native melee attacks, attack skills, and safely classified single-target offensive spells between duelists.
- Mirrored authoritative virtual health into native `CurrentHP` so Erenshor's healer AI can recognize duel injuries.
- Folded native self-heals, HoTs, lifesteal, and simple consumable healing back into bounded virtual health.
- Blocked grouped Sims, friendly pets, and either duelist from affecting actors outside the strict 1v1 boundary.
- Covered installed spell-start, healing, status-effect, and ticking-effect entry points.
- Restored pre-duel real health and removed duel-added effects during every normal cleanup path.
- Preserved outside-hostile cancellation and allowed real combat to proceed after restoration.
- Kept arbitrary NPC on-hit procs disabled because their effect and summon behavior cannot yet be proven safe.
- Updated the plugin version to 0.3.0.

Validation remaining: complete the documented in-game melee, caster, skill, healing, potion, party-interference, hostile-interruption, resource, and cleanup matrix.


## Unreleased - Suite UI/API coherence handoff

- Added optional, versioned `DuelControlApi` discovery/control surface for Suite Hub without a hard Hub dependency.
- Kept standalone commands and core gameplay authority intact.
- Documented the retained panel/launcher policy and Lunaris live-test requirement.
- Added a primitive-only Hub surface over the existing challenge/stop/eligibility paths; duel containment and the party-locality eligibility fix are preserved.
