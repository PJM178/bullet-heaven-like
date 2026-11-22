# Backend Services

Status: v0.3, 2026-11-22. **Exploration**, part of the [multiplayer
architecture research](README.md). The always-on, stateless services around
the game servers: accounts, profiles, parties, sessions, results.

## Own backend, one app (decided 2026-11-22)

- **Our own backend**, not Nakama: keeps the whole project in C#, and
  building it is part of the learning goal.
- **One ASP.NET Core app** holding every service as a separate area of code
  (folders / endpoint groups), not separate deployable services. One thing
  to build, run, debug and deploy. Splitting later is possible because the
  areas don't share state except through the database.
- **Starts small.** Local Wi-Fi play needs no backend at all (a phone hosts,
  see [game-server.md](game-server.md)), so the first version is little more
  than accounts. The rest is added when online play needs it.

## Services, in the order they are needed

| Service | What it does | Needed for | Local version |
| --- | --- | --- | --- |
| 2026-11-22 | v0.3 | Proposed: game servers talk to the backend over HTTP with a server key, at run edges only; checkpoint reports with idempotency keys; models A/B for who writes progress. |
| Accounts | Identify a player. Start with a device id + generated secret (anonymous), add real login later. | First version | Table in SQLite |
| Profiles | Coins, upgrades, unlocks: a cloud copy of today's local profile file. | First version, or soon after | Table in SQLite |
| Parties | Create a party, share an invite code, see who's in, leader starts the run. | Online play | In memory |
| Sessions | Get a dedicated game server for a party, give members its address. | Online play | Starts a game-server container (see [deployment.md](deployment.md)) |
| Results | Game server posts the outcome (coins earned per player, events completed). | Only if the backend should own rewards | HTTP endpoint with a server-only key |

Trust model (relaxed, decided 2026-11-22): no anti-cheat for now. A client
may write its own profile (bank its own coins), so Results is optional. If
trust ever matters, the backend stops accepting profile writes from clients
and only takes them from game servers.

Offline single player: keeps working without the backend. How a local profile
and an online profile relate is an open question (and partly design).

## Talking to game servers (proposal)

The backend and the game servers are two separate kinds of process (see
[game-server.md](game-server.md)). They talk over **HTTP**: a game server
is a client of the backend, like a phone, but uses **internal endpoints**
phones can't call. It authenticates with a **server key** (a shared secret
given to game-server containers as an environment variable). In Docker both
sit on the same internal network, so this traffic never leaves the machine.

The backend **owns the database**. Game servers never touch it directly;
every persistent change goes through the backend's API.

They only talk at the edges of a run, because meta progression doesn't
change during one (upgrades are bought in the menu, rewards banked at the
end):

```
1. Start   phone -> backend: start a run for my party
           backend: picks or starts a game server, creates run 42,
                    makes a join token per player
           backend -> phones: server address + port, your token
2. Setup   game server -> backend: who is in run 42, with which upgrades?
           (or the backend passes it when starting the server)
3. Play    phones <-> game server over our transport (UDP); backend not involved
4. Saves   game server -> backend: checkpoint reports during the run (see
           game-server.md, Run state and persistence) and a final report
           at the end
           phones -> backend: fetch the updated profile
```

### Idempotent reports

A report can be applied, then its answer lost, then sent again. Each
report carries the run id and a batch number (`run 42, batch 7`); the
backend records which batches it applied and ignores repeats, so retries
never double a reward. (The web pattern: idempotency keys.)

### Who writes progress: two models

| | A. Client owns its profile | B. Backend owns profiles, game server reports |
| --- | --- | --- |
| Run start | Phone tells the game server its own upgrades | Game server fetches upgrades from the backend |
| Run end | Phone gets its reward from the game server, banks it, uploads its profile | Game server reports results with the server key; backend updates profiles |
| Cheating | Easy | Hard (only servers write progress) |

The relaxed-trust decision above allows A. Proposed: **B's shape with
relaxed checks**: game servers report results from the start (one place
saw the run, and it's a small amount of work), while e.g. upgrades a phone
reports at join are trusted. Going strict later means closing endpoints,
not redesigning. Phone-hosted runs on local Wi-Fi may have no backend at
all; their rewards are banked locally, as in single player.

## Open questions

- [ ] How does an offline (local) profile relate to an online one: the
      server copy wins, newest wins, or merge?
- [ ] Database: SQLite to start (a file in a Docker volume); Postgres if it
      is ever hosted for real?
- [ ] Model A or B for writing progress (proposal: B's shape, relaxed
      checks)?
- [ ] Join setup: game server fetches the run's players and upgrades, or
      the backend passes them when it starts the server?
- [ ] Does a phone-hosted (local Wi-Fi) run need the backend at all, e.g.
      to show account names? Or are names local-only?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.2 | Decided: own backend (not Nakama), one ASP.NET Core app, starts as accounts only, relaxed trust (clients may write their own profile). Services ordered by when needed. |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
