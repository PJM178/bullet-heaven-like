# Enemy definitions

Status: decided and built, 2026-11-14. How it is built:
[systems/enemies.md](../systems/enemies.md).

## Problem

Everything about an enemy used to be one setting for all enemies, so the
game could only have one kind of enemy. A second kind (a big brute, a small
rat) needs its own size, speed, health and looks.

## Decisions

- **Each kind of enemy is data:** looks, speed, health, contact damage, XP,
  size, and how many of it can be alive at once. Like pickups and
  breakables, a new kind is a new file, not new code.
- **Two sizes per kind:** how big it is for weapons (what a swing catches)
  and how big its body is for walls, since the two need not match.
- **Each kind has its own fixed pool of enemies**, made when the run
  starts, rather than one shared pool whose enemies change looks. Kinds
  look too different to swap cheaply mid-run, and making enemies during
  play causes hitches on the phone. The pool size also caps how many of
  a kind can be on screen.
- **Which kind spawns is chosen by weight** for now. A wave or spawn
  system can take this over later.
- **Crowd behaviour is shared** by all kinds for now (how enemies keep
  apart and where they stop near the player).

## Open questions

- Crowd behaviour per kind: a big enemy should keep further from small
  ones and stop further from the player.
- A wave system: which kinds spawn when, and how many.
- Pool sizes per map, since each pool costs memory and start-up time.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-14 | EnemyDefinition, per-definition pools, per-kind hit/body radii; MaxAgentRadius removed. |
| 2026-11-15 | Technical detail moved to systems/enemies.md. |
