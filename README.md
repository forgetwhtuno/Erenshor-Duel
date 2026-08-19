# Erenshor Practice Duels 0.4.17

Part of the **Forgotten Roads for Erenshor** mod collection.

Erenshor Practice Duels 0.4.17 provides friendly, non-lethal simulated sparring between the player and a
local Sim, or between two local Sims while the player watches. It uses Erenshor's native combat
calculations where the installed paths have been verified, while virtualizing duel health so a
practice match does not become ordinary gameplay combat.

## Status: playable-state release candidate

The combat boundary remains native-adjacent: Erenshor computes admitted duel damage through its normal `DamageMe` / `MagicDamageMe` / `BleedDamageMe` path; a scoped `Stats.ReduceHP` Prefix captures the final post-mitigation reduction for the exact Duel participant transaction and suppresses only that real/mirrored HP write; Duel then applies the captured amount once to virtual health. Version 0.4.17 preserves the approved 0.4.16 combat/lifecycle/rematch behavior and does **not** use synthetic `int.MaxValue` HP headroom. Lifecycle remains explicit: `Idle -> Preparing -> Countdown -> Active -> Cleaning -> Idle`; combat is frozen for this release candidate.

Terminal restore is ownership-aware. If native gameplay selects a new unrelated player/NPC target during teardown, Duel does not replay an older target over it, and the post-duel autoattack scrub releases ownership immediately. Party-scope changes, participant invalidation, and zoning still cancel deterministically. Verified ordinary hostile-world PvE is intentionally different: it may overlap an active duel, remains fully native/real, and is never translated into virtual Duel HP. Friendly/protected/unknown third-party interference remains contained. Idle shutdown remains quiet.

The deterministic self-test suite is wired for the current source snapshot, but it could not be executed in this sandbox because no PowerShell/C# compiler is installed. A fresh installed-reference build and plugin-identity audit are also pending because the supplied packet does not include the current game `Assembly-CSharp.dll`. Version 0.4.5 leaves the live-proven 0.4.4 `Stats.ReduceHP` capture, nested damage stack, world-real ledger, declared-self spell repair, actor/effect-aware AoE containment, status/effect restoration, lifecycle, and cleanup semantics intact. Its only combat-semantic expansion is a narrow Active-Duel adaptation for ordinary single-target Heal spells such as Minor Healing: when the exact opponent is still selected/passed to `StartSpell`, the Harmony target argument is changed to the caster’s own `Stats` without changing `PlayerControl.CurrentTarget`; native Erenshor still owns cast time, animation, mana, cooldown, and heal amount. Full live verification remains required for repeated melee, Minor Healing with the opponent selected, declared-self spells, lifesteal, Sim healing, AoE with hostile/protected bystanders, legitimate yield, immediate repeat, post-duel vanilla combat, zoning, and unload/reload.

## What it does

- Starts a player-versus-local-Sim practice duel, including nearby non-party local Sims when the
  safety and locality checks pass.
- Starts local Sim-versus-Sim spectator duels when both participants are eligible.
- Clicking an eligible local Sim opens a small **SIM ACTIONS** menu with **Practice Duel** and
  **Arrange Sim Duel**, so neither a command nor a README lookup is required to discover dueling.
  When Erenshor Follow is installed and healthy, Follow's own Sim Actions menu owns this click and
  already exposes Practice Duel through its existing optional integration; Duel's own click handling
  stands down automatically so there is exactly one menu. See "Mouse discoverability" below.
- Contains verified native melee, skill, spell, healing, pet, effect, and damage paths inside the
  virtual-health duel boundary; unsupported third-party actions fail closed.
- Excludes remote COOP humans and network-owned Sims, and cancels safely for zoning, distance,
  camp activation, participant loss, unsafe friendly/unknown interference, manual stops, and internal errors. Verified hostile-world enemies are not treated as prohibited interference merely for being present, taking AoE damage, or attacking a duelist.
- Restores captured temporary combat state on teardown, including health, effects, targets, aggro, pets, and related temporary state where supported by the current source. Restoration is ownership-aware: newer unrelated target/combat state is preserved instead of blindly overwritten by a pre-duel snapshot. A challenge is rejected if native autoattack evidence is unavailable or player autoattack was already active, so Duel never has to reconstruct a native attack loop that predates the practice match.

Practice Duels grants no XP or loot, changes no faction, creates no real PvP, and does not make
participants permanently hostile. Erenshor's existing AI remains responsible for combat behavior;
this mod does not direct movement, targeting, attacks, spells, or healing decisions.

## Duel lifecycle

```text
Idle
  -> Preparing     challenge accepted; snapshots owned state
  -> Countdown     acceptance/countdown presentation
  -> Active        duel combat interception is admitted
  -> Cleaning      real state restored; bounded stale target/aggro scrub
  -> Idle          all duel and post-cleanup ownership released
```

Any terminal reason from Preparing, Countdown, or Active enters Cleaning. New challenges are rejected during Cleaning. Zone transitions, participant loss, party-scope changes, manual stop, unload, and shutdown all converge on the same terminal restoration boundary. Verified hostile-world combat does not cancel solely for overlapping the duel; its HP/effect consequences remain native and are carried in a separate real-world ledger.

## Commands

```text
/eduel <SimName>                 challenge a nearby local Sim
/eduel <Sim A> vs <Sim B>        start a nearby Sim-versus-Sim spectator match
/eduel watch <Sim A> vs <Sim B>  spectator alias
/eduel nearby                    inspect nearby candidate eligibility
/eduel status                    show current duel status
/eduel diag                      log eligibility and cleanup diagnostics
/eduel selftest                  run deterministic policy tests
/eduel stop                      stop the current duel
```

There is no `/eduel pvp` command, F9 panel, incoming-offer system, PvP matchmaking, protected-zone
policy, or temporary-party spawning system in this public build.

## Optional compatibility

Erenshor COOP is not required. When it is present, its remote-human and networked-Sim signals are
used only to exclude unsafe participants. Deep Sims is also optional: when installed, Practice
Duels can emit fact-only lifecycle events for short social reactions. Neither integration gives
another mod control over duel gameplay.

## Installation

This is a **native Lunaris plugin** — BepInEx is no longer required for this version. Requires
Lunaris installed in your Erenshor install. The compiled DLL is placed directly in
`<Erenshor>\plugins\ErenshorDuel.dll`; Lunaris manages enable/disable.

## Build and validation

`BUILD_AND_INSTALL.ps1` builds against an installed Erenshor copy and installs directly into
`<Erenshor>\plugins\`. `tests\RUN_TESTS.ps1` runs the deterministic policy self-test suite
standalone, outside the game. `/eduel selftest` runs the same suite live, in-game. Full live
validation is still required for the installed game version, especially across class, spell, pet,
interruption, zoning, and teardown combinations, and now also across a Lunaris load/unload/reload
cycle.

## Credits and inspiration

- **[Erenshor COOP](https://github.com/MizukiBelhi/ErenshorCoop) by MizukiBelhi** is a technical
  reference and compatibility target for remote-human and networked-Sim detection.

This project is guided by design, testing, playtesting, audits, and iteration. It is an unofficial
community mod and is not affiliated with
or endorsed by Erenshor's developer.


## Mouse discoverability (standalone Sim Actions)

Duel installed alone has no dedicated Hub panel with per-Sim buttons, so before 0.4.8 spectator
duels were only reachable via `/eduel <Sim A> vs <Sim B>`. Clicking an eligible local Sim now opens
a small retained **SIM ACTIONS** menu for that Sim:

- **Practice Duel** challenges the selected Sim directly (the same path as `/eduel <SimName>`); the
  button is disabled with the real eligibility reason shown inline when the Sim is not currently
  challengeable (too far, in combat, camp active, and so on).
- **Arrange Sim Duel** enters "choose opponent" — click a second eligible local Sim to see
  `Sim A vs Sim B` with **Start**/**Cancel**. **Start** calls the exact same `StartSpectator` entry
  point `/eduel <Sim A> vs <Sim B>` uses; **Cancel** returns to the menu for the first Sim. If either
  Sim becomes unavailable before you confirm, the arrangement is cancelled with the real reason
  rather than failing silently.

**Erenshor Follow owns Sim Actions when Follow is installed and healthy.** Duel detects this by
reflection only (no compile-time reference to Follow) and never opens competing UI in that case —
Follow's own Sim Actions menu already exposes Practice Duel through the existing `DuelControlApi`
integration. This is checked live, not just once at load, so it is safe regardless of whether Follow
loads before or after Duel, or is added/removed mid-session. `/eduel` continues to work either way.

## Optional Suite Hub integration

Forgotten Roads Hub is **optional**. The mod exposes a versioned `DuelControlApi`/Aura surface without a Hub assembly reference or load-order dependency. The descriptor reports concise duel status plus an eligible-local-candidate count. Its action transport remains exactly the authoritative operations already supported by the ControlApi: `challenge` with an explicit Sim-name argument and `stop`.

Practice Duels keeps combat UI small, but the shared retained fallback entry point provides mouse discoverability when Forgotten Roads Hub is absent/unavailable and hides while a healthy Hub owns primary access. With no Hub and no Follow installed, Lunaris + this DLL alone are enough: the fallback panel's status line names the actual eligible local Sims (not just a count), a **Challenge Nearby** button starts a duel with the first eligible one, **Stop Duel** ends an active one, and the panel's guide text spells out `Sim vs Sim: /eduel <Sim A> vs <Sim B>` so spectator mode does not require a README lookup. `/eduel` remains a compatibility control. Remote COOP humans and unrelated actors remain excluded by the same eligibility path used outside Hub.

The current Suite Hub renderer can transport two-argument actions but does not yet render arbitrary argument-entry/action controls on a module page. Therefore the Duel provider advertises `challenge(name)`/`stop` correctly, but this workstream does not invent a fake target selector or modify Hub to surface them as buttons.
