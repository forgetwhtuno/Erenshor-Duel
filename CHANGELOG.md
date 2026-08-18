# Changelog

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
