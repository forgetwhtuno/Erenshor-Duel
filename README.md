# Erenshor Practice Duels 0.4.0

Erenshor Practice Duels provides friendly, non-lethal simulated sparring between the player and a
local Sim, or between two local Sims while the player watches. It uses Erenshor's native combat
calculations where the installed paths have been verified, while virtualizing duel health so a
practice match does not become ordinary gameplay combat.

## Status: native Lunaris migration candidate

This version has been migrated off BepInEx 5 onto native Lunaris. This is a
loader/logging/lifecycle migration only — no duel eligibility, safety-gate, virtual-health,
native-damage-routing, third-party-isolation, or cleanup behavior changed; every Harmony patch
target has been re-verified against the currently installed `Assembly-CSharp.dll`, and the full
deterministic self-test suite (`tests/RUN_TESTS.ps1`) still passes. **Live in-game verification
under Lunaris — including hot unload/reload during an active duel — has not yet been done.** A
legacy BepInEx release remains available in this repository's Git history for anyone still on
BepInEx.

## What it does

- Starts a player-versus-local-Sim practice duel, including nearby non-party local Sims when the
  safety and locality checks pass.
- Starts local Sim-versus-Sim spectator duels when both participants are eligible.
- Contains verified native melee, skill, spell, healing, pet, effect, and damage paths inside the
  virtual-health duel boundary; unsupported third-party actions fail closed.
- Excludes remote COOP humans and network-owned Sims, and cancels safely for zoning, distance,
  camp activation, hostile interference, participant loss, manual stops, and internal errors.
- Restores captured temporary combat state on teardown, including health, effects, targets, aggro,
  pets, autoattack, and related temporary state where supported by the current source.

Practice Duels grants no XP or loot, changes no faction, creates no real PvP, and does not make
participants permanently hostile. Erenshor's existing AI remains responsible for combat behavior;
this mod does not direct movement, targeting, attacks, spells, or healing decisions.

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

This project was developed with substantial AI-assisted coding support, guided by design, testing,
playtesting, audits, and iteration. It is an unofficial community mod and is not affiliated with
or endorsed by Erenshor's developer.


## Optional Suite Hub integration

Erenshor Suite Hub is **optional**. The mod exposes a versioned `DuelControlApi`/Aura surface without a Hub assembly reference or load-order dependency. The descriptor reports concise duel status plus an eligible-local-candidate count. Its action transport remains exactly the authoritative operations already supported by the ControlApi: `challenge` with an explicit Sim-name argument and `stop`.

Practice Duels intentionally has no dedicated module panel or standalone launcher; `/eduel` and existing contextual integrations remain standalone. Remote COOP humans and unrelated actors remain excluded by the same eligibility path used outside Hub.

The current Suite Hub renderer can transport two-argument actions but does not yet render arbitrary argument-entry/action controls on a module page. Therefore the Duel provider advertises `challenge(name)`/`stop` correctly, but this workstream does not invent a fake target selector or modify Hub to surface them as buttons.
