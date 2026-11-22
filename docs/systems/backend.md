# Backend

Status: v0.1, 2026-11-22. Documents the backend **as built**: an empty
shell with one endpoint. What it will do and why:
[research/multiplayer-architecture/backend.md](../research/multiplayer-architecture/backend.md);
how this step was planned:
[backend-shell-plan.md](../research/multiplayer-architecture/backend-shell-plan.md).

Paths in this doc are relative to the **repo root** (not `game/`): the
backend, shared library and Docker files sit beside the Godot project.

## Projects

| Project | Path | Target | What |
| --- | --- | --- | --- |
| Backend | `backend/BulletHeavenLike.Backend/` | net10.0 | ASP.NET Core minimal API. Namespace `BulletHeavenLike.Backend`. |
| Shared | `shared/BulletHeavenLike.Shared/` | net8.0 | Plain C# library for code the game, game server and backend share. Empty. Never references Godot. Not referenced by anything yet. |

`BulletHeavenLike.slnx` at the root covers backend, shared and
`game/BulletHeavenLike.csproj`, so `dotnet build` at the root builds all
three. Godot keeps using its own `game/BulletHeavenLike.sln`.

## Endpoints

| Method | Path | Answer |
| --- | --- | --- |
| GET | `/health` | `200 {"status":"ok"}` when the service is up |

Anything else answers 404.

## Startup and configuration

`Program.cs` (top-level statements) builds the app with
`WebApplication.CreateBuilder(args)`, maps the endpoints and calls
`app.Run()`, which serves until stopped (Ctrl+C, SIGTERM).

Configuration, later sources overriding earlier ones: `appsettings.json`,
`appsettings.{Environment}.json`, environment variables (`__` for nesting,
e.g. `Logging__LogLevel__Default`), command-line arguments (none passed
today). Logging goes to the console. In Development,
`appsettings.Development.json` sets `Microsoft.AspNetCore.Hosting.Diagnostics`
to Information, so every request logs a "Request starting" and a "Request
finished" line (with status and time); `appsettings.json` keeps ASP.NET at
Warning otherwise.

| Run | Listens on | Environment | Set by |
| --- | --- | --- | --- |
| `dotnet run` | `http://localhost:8080` | Development | `Properties/launchSettings.json` |
| Docker | port 8080 in the container, published as 8080 on the host | Development | `aspnet` image (`ASPNETCORE_HTTP_PORTS=8080`), `deploy/docker-compose.yml` |

No HTTPS (TLS comes from the hosting platform later), no authentication,
no database yet.

## Docker

- `deploy/Dockerfile`: multi-stage. `sdk:10.0` restores and publishes the
  backend (Release), `aspnet:10.0` runs `BulletHeavenLike.Backend.dll` as the
  image's non-root `app` user. Built with the **repo root** as context so it
  can copy `shared/` as well as `backend/`.
- `.dockerignore` (root): keeps `game/`, `art/`, `docs/`, `.git/` and
  `bin/`/`obj/` out of the build context.
- `deploy/docker-compose.yml`: project `bulletheavenlike`, one service,
  `backend` (container `bulletheavenlike-backend-1`), port 8080:8080.

## Commands

From the repo root (`Makefile`):

| Command | Does |
| --- | --- |
| `make run` | Build and start the backend in Docker in the foreground, logs on screen; Ctrl+C stops it |
| `make up` | Build the image and start the backend in Docker (background) |
| `make down` | Stop and remove the container |
| `make logs` | Follow the backend logs |
| `make health` | `curl` `/health` on localhost:8080 |
| `make build` | `dotnet build` of the root solution |

Without Docker: `dotnet run` in `backend/BulletHeavenLike.Backend/`. Run
only one of the two at a time (same port). From the phone (same Wi-Fi),
use the Docker one at `http://<PC's LAN IP>:8080/health`; `dotnet run`
only listens on localhost.

## Open questions

- [ ] Should `/health` report more than "up" (version, uptime)?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.1 | Backend shell: `/health`, shared library, root solution, Dockerfile + compose (project `bulletheavenlike`), Makefile (`run`/`up`/`down`/`logs`/`health`/`build`), request logging in Development. |
