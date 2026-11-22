# Backend Shell Plan

Status: v0.2, 2026-11-22. **Built** (as built: [systems/backend.md](../../systems/backend.md)).
Part of the [multiplayer
architecture research](README.md). The first step of the backend: an empty
but runnable shell that later features are added to. What the backend will
eventually do is in [backend.md](backend.md); how it runs is in
[deployment.md](deployment.md); the language and repo layout in
[stack.md](stack.md).

## Goal

A backend that builds, runs locally (directly and in Docker) and answers one
request, with the project structure in place for everything that comes
after. No game features yet: no accounts, parties or game servers.

## Scope

| Piece | Where | What |
| --- | --- | --- |
| Backend | `backend/BulletHeavenLike.Backend/` | ASP.NET Core minimal API, .NET 10. One endpoint, `GET /health`, returning that the service is up. Configuration (`appsettings.json`, environment variables) and logging set up the standard ASP.NET way. |
| Shared library | `shared/BulletHeavenLike.Shared/` | Plain C# class library, `net8.0` (so the game, net8.0 / net9.0 on Android, can reference it). Empty for now. Never references Godot. The game doesn't reference it until the first shared type exists. |
| Root solution | `BulletHeavenLike.slnx` | Covers backend, shared and the game, so one `dotnet build` at the root builds everything. Godot keeps its own `game/BulletHeavenLike.sln` for its builds. |
| Docker | `deploy/` | A `Dockerfile` for the backend and a `docker-compose.yml` that runs it locally on a published port. |
| Makefile | repo root | Added while building: `make up` / `down` / `logs` / `health` / `build`. |

Namespaces: `BulletHeavenLike.Backend` and `BulletHeavenLike.Shared` (the
game keeps its single `BulletHeavenLike` namespace).

## Out of scope (later steps)

- Accounts, profiles, parties, sessions, results (see [backend.md](backend.md),
  in the order listed there).
- A database (SQLite comes with accounts).
- The game-server container and starting game servers
  ([deployment.md](deployment.md)).
- Anything in the game itself.

## Steps

1. Create the shared library project (`dotnet new classlib`), target
   `net8.0`, remove the template class.
2. Create the backend project (`dotnet new web`), .NET 10, with the
   `/health` endpoint.
3. Create the root solution and add all three projects (backend, shared,
   `game/BulletHeavenLike.csproj`).
4. Make sure the game's Godot build is unaffected: `game/` builds alone, and
   Godot's `--build-solutions` still uses its own solution.
5. Add `deploy/Dockerfile` (multi-stage: SDK image builds, runtime image
   runs) and `deploy/docker-compose.yml`.
6. Ignore build output (`bin/`, `obj/`) for the new projects in
   `.gitignore`.
7. Update docs: this plan's status, `stack.md` (layout now in use),
   `CLAUDE.md` (folder layout and commands for the server side), and a new
   systems doc for the backend once it exists.

## Done when

- `dotnet build` at the repo root builds backend, shared and game.
- `dotnet run` in `backend/...` starts the service; `GET /health` answers.
- `docker compose up` in `deploy/` starts it in a container; `GET /health`
  answers from the host (and from the phone on the same Wi-Fi, by the
  machine's LAN address).
- The game still builds and runs from the Godot editor as before.

## Open questions

- [x] Port numbers: 8080 inside the container and on the host (and for
      `dotnet run`).
- [ ] Should `/health` report anything beyond "up" (version, uptime)?
      Just "up" for now.
- [x] Root solution format: `.slnx`.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.2 | Built. Port 8080, `.slnx`, `/health` returns only "up"; Makefile and `.dockerignore` added. |
| 2026-11-22 | v0.1 | Plan written: backend shell scope, steps, done criteria. |
