# Backend Services

Status: v0.2, 2026-11-22. **Exploration**, part of the [multiplayer
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

## Open questions

- [ ] How does an offline (local) profile relate to an online one: the
      server copy wins, newest wins, or merge?
- [ ] Database: SQLite to start (a file in a Docker volume); Postgres if it
      is ever hosted for real?
- [ ] Does a phone-hosted (local Wi-Fi) run need the backend at all, e.g.
      to show account names? Or are names local-only?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.2 | Decided: own backend (not Nakama), one ASP.NET Core app, starts as accounts only, relaxed trust (clients may write their own profile). Services ordered by when needed. |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
