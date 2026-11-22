# Game Server

Status: v0.3, 2026-11-22. **Exploration**, part of the [multiplayer
architecture research](README.md). The process that simulates one run: how
it is built, how many run, tick rates, and who is authoritative over what.

## One host role, two places to run it (decided 2026-11-22)

Authority is a **role, not a place**. One process is the host of a run: it
simulates enemies, hits, pickups and level-ups, and the other players
connect to it. That host can be:

- **A player's phone** (listen server): the phone runs the run *and* plays
  it. For playing together on the same Wi-Fi, with no backend or server
  needed. Friends find the host by LAN discovery (UDP broadcast on the local
  network) or by typing/scanning its address.
- **A dedicated server**: the same code, headless, in a container. For playing
  over the internet, where phones can't reach each other (mobile data and
  home routers block incoming connections, NAT).

Godot's multiplayer API treats both the same way (the host is peer 1 either
way), so this costs little extra: the difference is whether the host also
has a local player, a camera and a screen.

This is not full peer-to-peer, where each phone owns part of the world:
hundreds of enemies need one owner, or two phones disagree about what was
hit and what died.

Costs of the phone host to measure: CPU and battery for simulating the run
for everyone *and* rendering, upload bandwidth from the phone.

### Relaxed trust (decided 2026-11-22)

Co-op and friends only, so no anti-cheat for now. Where trusting the client
makes things simpler, trust it. Biggest win, **proposed**: each client is
authoritative over **its own player's position**. The client moves its
player and tells the host where it is; the host accepts it (maybe with a
sanity check on speed). That removes the need for client-side prediction
and reconciliation, the hardest netcode technique, at least at first. The
cost: the host sees each player slightly late, so enemy contact hits are
judged a little in the past. Fine for co-op.

## Same project, headless

The server is the **same Godot C# project**, started with `--headless` (or
exported with Godot's "dedicated server" export mode, which strips textures
and meshes). Game logic (`EnemyManager`, weapons, `WorldObstacles`,
`PickupManager`, `SkillManager`) runs on the server; models, sprites, effects,
UI, camera and audio don't. This avoids writing the game twice.

`OS.HasFeature("dedicated_server")` / a command-line flag tells code which role
it has. Visual-only code must not run (or must be cheap no-ops) on the server.

## Process per run vs rooms

- **One process per run** (proposed): simple, a crash affects one party, easy
  to reason about. Cost: each Godot + .NET process takes memory (to be
  measured; guess 150-300 MB).
- **Many rooms per process**: cheaper at scale, much harder (separate worlds
  in one scene tree, separate physics spaces). Not worth it for friends-only.

## Tick rates

- Simulation: keep the existing 60 Hz physics, or drop to 30 Hz on the server
  if CPU-bound (to be measured).
- Network send rate: snapshots at 20-30 Hz, input commands from clients at
  the simulation rate (batched, a few per packet, with redundancy so one lost
  packet loses no input).

## Who is authoritative over what (proposal)

| Thing | Authority | How clients see it |
| --- | --- | --- |
| 2026-11-22 | v0.3 | Proposed: run state vs persistent state; game server reports gains to the backend in checkpoints. |
| Own player movement | **Own client** (relaxed trust, proposed); server + prediction only if needed later | Own player: instant. Host checks speed. |
| Other players | Their own clients, relayed by the host | Interpolated |
| Enemies (position, HP, death) | Server | Snapshots; interpolated or extrapolated |
| Weapon hits, damage | Server only | Events (hit flash, damage numbers) |
| Projectiles | Server for hits | Spawn **event** once; both sides fly the straight line |
| Pickups, XP orbs, coins | Server | Spawn/collect events |
| Breakables | Server | Break event; shatter is client-side visuals |
| Level-up choices | Server validates | Client picks from the server's offered list |
| Run rewards (coins) | Host decides each player's share | Each client banks its own (relaxed trust); a dedicated server may report to the backend instead |
| Visual-only (animation frames, effects, camera, cutaways) | Each client | Not networked |

## Run state and persistence (proposal)

A game server's memory lives only as long as its run. Two kinds of state:

| | Run state | Persistent state |
| --- | --- | --- |
| Examples | Enemy positions, HP, run XP and level, what a player carries right now | Account, coins, upgrades, stored materials, unlocked maps |
| Lives in | Game server memory | Backend database |
| Lifetime | Ends with the run | Kept |

Example: a player mines an ore. The game server changes run state and tells
the player's phone at once (over the transport). The ore only becomes
persistent when the game server reports it to the backend: the game server
is the witness, the backend the record keeper.

When to report:

| When | Pro | Con |
| --- | --- | --- |
| Once, at run end | Simplest | A crash loses the whole run's gains |
| Every event | Nothing lost | Many calls, backend busy during play |
| **Checkpoints** (proposed): a batch every 30-60 s, when a player leaves, and at run end | Little lost on a crash, few calls | Some bookkeeping |

The game server keeps a list of unreported gains per player and sends it in
batches; the backend applies each batch once (idempotent reports, see
[backend.md](backend.md)). How much is at stake depends on the design
question of *when* a gain becomes the player's (see the README's Handed to
design): if a bag is only kept on surviving, the run-end report is enough.

## Open questions

- [ ] Memory and CPU of one headless Godot + .NET game server running a full
      run: measure.
- [ ] Server simulation rate: 60 or 30 Hz?
- [ ] Can a phone host a full run for up to 5 others (CPU, battery, upload)?
      Measure on the test phone. Maybe a lower player cap for phone hosts.
- [ ] Is client-authoritative movement good enough, or do contact hits feel
      unfair? Try it in a spike before adding prediction.
- [ ] LAN discovery: broadcast, or address/QR code only at first?
- [ ] What if the host phone leaves mid-run (run ends, or host migration)?
      Host migration is hard; ending the run is the simple first answer.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.2 | Decided: one host role, run by a phone (local Wi-Fi) or a dedicated server; relaxed trust. Proposed: client-authoritative own movement. Authority table updated. |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
