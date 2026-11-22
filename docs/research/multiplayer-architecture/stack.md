# Language and Repo Layout

Status: v0.5, 2026-11-22. **Exploration**, part of the [multiplayer
architecture research](README.md). Which language each server is written in,
and where the projects live in the repo.

## Language: C# everywhere (decided 2026-11-22)

Go was considered for the servers (small static binaries, tiny containers,
strong networking, the language of Agones/Nakama/Docker). It fits backend
services well, but a Go *game server* would mean writing the simulation
twice (C# for the client, Go for the server) and keeping them in sync, with
every difference showing up as a desync. A Go backend beside a C# game server
works technically (the boundary is HTTP/JSON), but then the project has two
languages for little gain.

Decided: **C# for all of it.**

- Game server: the Godot C# project, headless.
- Backend services and orchestrator: ASP.NET Core.
- Shared: one plain C# class library (no Godot types) for message contracts
  (join tokens, run results, snapshot layouts) and rules both sides check
  (upgrade costs, profile validation). Written once, used by game, game
  server and backend.

Why: one language and toolchain to learn and maintain, shared code instead
of duplicated code. Container size is not a concern at friends-only scale;
ASP.NET with trimming or Native AOT on a small base image is tens of MB.

Moving the game simulation itself into the plain C# library (a Godot-free
game server) was considered and set aside: see below.

## Repo layout (decided for now, 2026-11-22)

The game server is the Godot project itself (headless), so only two new
projects appear: the backend (ASP.NET: services + orchestrator) and the
shared library. Today the Godot project *is* the repo root.

Options considered:

- **A. Keep the game at the root**, add `server/backend` and `server/shared`
  inside it. Nothing moves, but every non-game folder needs both
  `<Compile Remove="..." />` in `BulletHeavenLike.csproj` (Godot's csproj
  compiles every `.cs` below it) and a `.gdignore`; forgetting one gives
  confusing errors. Game and servers stay mixed at the top level.
- **B. Monorepo, game in its own folder** (decided for now):

  ```
  /
    BulletHeavenLike.sln               game + shared + backend (+ tests)
    game/                              Godot project: client AND headless game server
    shared/BulletHeavenLike.Shared/    plain C# library, no Godot types
    backend/BulletHeavenLike.Backend/  ASP.NET Core: services + orchestrator
    deploy/                            Dockerfiles, docker-compose for local runs
    docs/  art/                        moved out of the Godot project
  ```

  Each project owns its folder; the game uses a plain `ProjectReference` to
  `../shared/`. `res://` paths don't change (they are relative to
  `project.godot`). One-time cost: `.gitignore` paths (`/local_assets/`,
  `/android/`), the git-excluded `research/`, paths in `CLAUDE.md` /
  `CLAUDE.local.md` and the bake commands writing into `local_assets/`, the
  editor's project list entry, a fresh `.godot/` cache. Editor closed while
  moving.
- **C. Separate server repo.** Rejected: sharing the library would need
  submodules or a NuGet package.

Details that come with B:

- Shared library targets `net8.0` so the game (net8.0, net9.0 on Android)
  can reference it; the backend can target .NET 10 (LTS).
- Namespaces: the game keeps its single `BulletHeavenLike` namespace; new
  projects use `BulletHeavenLike.Shared` and `BulletHeavenLike.Backend`.
- The shared library never references Godot (`System.Numerics` or own
  structs; the game converts at the boundary), or the backend would need
  Godot to build.

Done 2026-11-22: the Godot project moved into `game/` (`docs/` and `art/`
stay at the root). Then the backend shell added `shared/`, `backend/`,
`deploy/` and a root `BulletHeavenLike.slnx` (not `.sln`) for all projects;
Godot keeps its own `game/BulletHeavenLike.sln`. The game doesn't reference
`shared/` until the first shared type exists. As built:
[systems/backend.md](../../systems/backend.md).

## Backend and shared code: plain C# (decided 2026-11-22)

The ASP.NET Core backend is plain .NET: it never references Godot. The only
way Godot could reach it is through code it shares with the game, so
everything in the shared library is plain C# too (no `Node`, no Godot
`Vector3`, no Godot resources). The game converts at the boundary.

Game rules the backend may need are therefore written once, in plain C#, in
the shared library. With relaxed trust (clients may write their own profile,
see [backend.md](backend.md)) the backend needs few rules at first.

## Game simulation off Godot types: not doing it (decided 2026-11-22)

The game logic is already C#; the question was whether to make it run
*without the Godot engine* (own math types instead of Godot's `Vector3`, no
`Node`s). Checked against the code: the simulation barely uses the engine.
`EnemyManager`, `WorldObstacles`, projectiles and pickups do their own
distance and push-out math on packed arrays; only `Player` uses an engine
feature (`MoveAndSlide()`). So a headless Godot server runs it as it is.

Moving it out would only buy a Godot-free game server (smaller, faster to
start: irrelevant for 2-6 friends) and unit tests without the engine. The
cost is a refactor of working code and type conversions everywhere.
Decided: **game code stays on Godot types**, no `System.Numerics` in the
game. The simulation is instead split into core and shell on Godot types
(see [code-impact.md](code-impact.md)), which gives multiplayer what it
needs. Revisit only if server size or engine-free testing ever becomes a
real need.

When an engine-agnostic simulation *would* pay off: switching engines or
adding a second client (web, another engine); scale (many runs per machine,
thousands of players); a simulation that must match bit for bit (lockstep,
replays); heavy rule testing in CI. None apply now.

The cheap hedge instead: keep the **network protocol** engine-agnostic.
Messages (inputs, snapshots, events) are plain C# types in the shared
library with our own binary format, not Godot's RPC wire format, for core
game state. Any future server or client then only has to speak the protocol;
the simulation can stay in Godot. See [networking.md](networking.md).

## Open questions

- [ ] Vector values in shared message types: since game code uses no .NET
      math types, proposal is plain `float` fields (or tiny own structs),
      converted to `Godot.Vector3` where the game reads a message.
- [ ] Data the backend might check (upgrade costs, unlocks) lives in Godot
      `.tres` resources today; the backend can't read those. If it ever
      needs them: export to JSON, or define them in the shared library.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.5 | Layout in use: `shared/`, `backend/`, `deploy/`, root `.slnx`. |
| 2026-11-22 | v0.4 | Repo move done: Godot project in `game/`. |
| 2026-11-22 | v0.3 | Decided: game code stays on Godot types; core/shell split instead. Shared message vectors: plain floats proposed. |
| 2026-11-22 | v0.2 | Decided: C# everywhere, monorepo (for now), backend and shared library plain C# (no Godot). Game simulation off Godot types: not worth it (checked against the code). |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
