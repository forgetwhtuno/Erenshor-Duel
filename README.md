# Erenshor Practice Duels 0.3.1

Erenshor Practice Duels provides friendly, non-lethal simulated sparring between the player and a
local Sim, or between two local Sims while the player watches. It uses Erenshor's native combat
calculations where the installed paths have been verified, while virtualizing duel health so a
practice match does not become ordinary gameplay combat.

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

## Build and validation

`BUILD_AND_INSTALL.ps1` builds against an installed Erenshor copy and installs into the selected
BepInEx profile. `/eduel selftest` runs deterministic policy tests. Full live validation is still
required for the installed game version, especially across class, spell, pet, interruption,
zoning, and teardown combinations.

## Credits and inspiration

- **[Erenshor COOP](https://github.com/MizukiBelhi/ErenshorCoop) by MizukiBelhi** is a technical
  reference and compatibility target for remote-human and networked-Sim detection.

This project was developed with substantial AI-assisted coding support, guided by design, testing,
playtesting, audits, and iteration. It is an unofficial community mod and is not affiliated with
or endorsed by Erenshor's developer.
