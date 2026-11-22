# Multiplayer Architecture (research)

Status: v0.6, 2026-11-22. **Exploration**, nothing built. These docs are a
learning and research notebook for the technical side of online co-op: how
the servers, networking and replication could work. Choices made are marked
**Decided** (with a date); everything else is a proposal, an option or an
open question. This page is the overview; each topic has its own doc below.

## Decided so far

| Date | Decision | Doc |
| --- | --- | --- |
| 2026-11-22 | C# for everything (game, game server, backend) | [stack.md](stack.md) |
| 2026-11-22 | Monorepo (`game/`, `shared/`, `backend/`, `deploy/`), for now | [stack.md](stack.md) |
| 2026-11-22 | Backend and shared library are plain C# (no Godot) | [stack.md](stack.md) |
| 2026-11-22 | Game code stays on Godot types; simulation managers split into core + shell for multiplayer | [code-impact.md](code-impact.md) |
| 2026-11-22 | One host is authoritative for a run; the host is a dedicated server **or** a player's phone (same code). Trust is relaxed: no anti-cheat for now | [game-server.md](game-server.md) |
| 2026-11-22 | Own backend, one ASP.NET Core app; starts as little more than accounts | [backend.md](backend.md) |
| 2026-11-22 | Local setup runs in Docker (Compose), the same way it will be deployed | [deployment.md](deployment.md) |

Gameplay questions (shared progress, mixed meta levels, world unlocking...)
are only *collected* here, under [Handed to design](#handed-to-design). They
belong in a future `design/multiplayer.md` once we start thinking about
multiplayer as a game designer.

## Docs in this folder

| Doc | What it covers |
| --- | --- |
| [glossary.md](glossary.md) | Networking terms: authority, ticks, snapshots, prediction, interpolation... |
| [game-server.md](game-server.md) | Headless Godot game server, process per run, tick rates, authority table |
| [networking.md](networking.md) | ENet transport, replicating the horde, bandwidth budget, projectiles, joining/leaving |
| [backend-shell-plan.md](backend-shell-plan.md) | First backend step (built): empty ASP.NET shell, shared library, root solution, Docker |
| [backend.md](backend.md) | Accounts, profiles, parties, sessions, results; trust model; build or buy |
| [deployment.md](deployment.md) | Running everything locally first, Docker and hosting later |
| [stack.md](stack.md) | Language (C#), repo layout (monorepo), plain C# backend and shared code |
| [code-impact.md](code-impact.md) | Planned core/shell refactor, other changes multiplayer needs |

## Starting assumptions

These came from the first conversation and are what the research is scoped
to. Any of them can change.

- **Co-op only.** No PvP, no competitive leaderboards. Cheating mostly hurts
  the cheater, so trust can be relaxed where it makes things simpler.
- **Play with friends first.** Party codes / invites, no matchmaking with
  strangers.
- **Dedicated, always-on servers from the start**, but **run locally** on the
  development machine first (localhost / LAN), moved to a hosted machine later.
  Revised 2026-11-22: a player's phone can also host a run (local Wi-Fi play),
  see [game-server.md](game-server.md).
- **2 to 6 players** per run.
- Android first, same as the single-player game.

## Architecture models considered

| Model | Who runs the game | Verdict |
| --- | --- | --- |
| **Listen server** | One player's phone hosts | **Decided, as one of two host modes.** Works on the same Wi-Fi with no backend. Over the internet, phones on mobile data sit behind NAT and can't be reached (needs a relay or the dedicated server). Host's battery/connection limits everyone. |
| **Dedicated authoritative server** | Headless game process on a server | **Decided, as the other host mode.** Same code as the listen server, no NAT problems. Costs hosting and backend work, which is the learning goal anyway. |
| Full peer-to-peer (every peer owns part of the world) | Split between phones | Not chosen: hundreds of enemies need one owner, and splitting them between phones makes hits and deaths disagree. |
| Deterministic lockstep | Every phone runs everything | Rejected for now: C# `float` math across ARM/x86 and Godot physics are not guaranteed deterministic; one desync breaks the run. |

## Overall shape (proposal)

Two ways to play, one host role (see [game-server.md](game-server.md)):

```
 Local (same Wi-Fi, no servers needed)       Online (backend + dedicated server)
 ┌────────────┐  ENet/UDP  ┌──────────┐      phones ═══ ENet/UDP ═══► headless game server
 │ host phone │◄══════════►│  phones  │                               (same game code as host phone)
 │ runs the   │            └──────────┘
 │ run + plays│                               phones ── HTTP ──► backend (accounts, profiles, ...)
 └────────────┘
```

The full online picture, once parties and server launching are needed, has
two very different kinds of server:

```
                 ┌──────────── Backend services (always on, stateless HTTP) ───────────┐
   phone ──────► │ Accounts/auth   Profiles (coins, upgrades)   Parties (invite codes) │
                 └───────────────────────────────┬─────────────────────────────────────┘
                                                 │ "party ABCD starts a run on map X"
                                                 ▼
                 ┌──────────── Orchestrator (session manager) ─────────────┐
                 │ starts/stops game-server processes, hands out            │
                 │ address:port + a one-time join token per player          │
                 └───────────────────────────────┬─────────────────────────┘
                                                 ▼
   phones ◄═══ UDP (ENet) ═══► Game server: headless Godot, one per run, lives ~20 min
                                simulates the run, reports results to the backend, exits
```

- **Game server** is *stateful and disposable*: it holds the whole run in
  memory and dies with it. If it crashes, the run is lost (acceptable at first).
- **Backend services** are *stateless and long-lived*: plain web API + a
  database. They never simulate anything. They can be restarted any time.
- The **orchestrator** is the glue. Locally it can be a small program that
  starts Godot processes on free ports. Hosted, the same job is done by
  systemd, Docker, or a fleet manager (Agones on Kubernetes, or a managed
  game-server host).

Details: [game-server.md](game-server.md), [backend.md](backend.md),
[deployment.md](deployment.md), [stack.md](stack.md).

## Learning path (proposed spikes)

Each step is small, runs locally, and teaches one idea:

1. **Hello ENet**: a headless server and two desktop clients connect; clients
   see each other's player (naive: client sends position). Learn: peers, RPCs.
2. **Server authority**: clients send input commands; server moves players.
   Feel the input lag. Learn: ticks, input commands.
3. **Prediction + reconciliation** for own player; interpolation for others.
4. **Enemies**: server runs `EnemyManager`; one binary snapshot per tick;
   measure bytes. Then interest management, then deltas.
5. **Projectiles and hits** as events.
6. **Backend**: ASP.NET + SQLite with parties and invite codes; orchestrator
   starts the game server process.
7. **Phone on LAN**: real Android client against the local server; measure
   bandwidth, CPU, battery, feel.
8. **Hosted**: same pieces in Docker on a VPS.

## Handed to design

Collected for a future `design/multiplayer.md`; not answered here.

- How is **progress shared**? Shared XP pool and level, or each player
  levels alone? Does a level-up pause everyone, nobody, or only the chooser?
- How do **players at different meta levels** play together? Upgrades apply
  as is, are scaled, or capped? Does difficulty scale with party size and
  power?
- How is the **world unlocked** in co-op? Does completing a map event unlock
  the map for everyone in the party, or only for those who have the
  prerequisite? Can a friend bring you into a map you haven't unlocked?
- **Rewards**: coins per player or shared and split? Pickups first come first
  served, or per-player loot?
- **Death**: spectate, revive by a teammate, respawn? Does the run end when
  everyone is down?
- **Joining late / leaving**: allowed mid-run? What happens to a leaver's
  rewards?
- **Base / menu**: is the (later walkable) base shared with the party?
- **Enemy count**: scale with player count, or with how spread out players
  are?

## Open questions

Topic questions live in each doc's own Open questions section.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.6 | Decided: core/shell split on Godot types; no .NET math types in game code. |
| 2026-11-22 | v0.5 | First decisions: C#, monorepo, plain-C# backend and shared code, two host modes (phone or dedicated) with relaxed trust, own one-app backend, Docker locally. Decided table added. |
| 2026-11-22 | v0.4 | Split into a folder: this overview plus one doc per topic. |
| 2026-11-22 | v0.3 | Repo layout: leaning monorepo with game/, shared/, backend/, deploy/; move just before the first non-Godot code. |
| 2026-11-22 | v0.2 | Language: Go considered, leaning C# everywhere (Godot headless game server, ASP.NET backend, shared plain C# library). Repo layout question added. |
| 2026-11-22 | v0.1 | First research notes: assumptions (co-op, friends, local dedicated servers, 2-6 players), models compared, proposed server shape, replication budget, learning path, design questions collected. |
