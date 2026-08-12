# Changelog

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
