# Running and Hosting

Status: v0.2, 2026-11-22. **Exploration**, part of the [multiplayer
architecture research](README.md). Where the pieces run: everything on one
machine first, hosted later.

## Docker locally (decided 2026-11-22)

The local setup runs in **Docker Compose**, so it runs the same way it will
later be deployed: moving to a VPS means copying the compose setup and
changing addresses, not rewriting how things start.

Proposed services in `deploy/docker-compose.yml`:

| Service | Image | Notes |
| --- | --- | --- |
| `backend` | ASP.NET Core app (official .NET runtime image, or trimmed / Native AOT on a small base) | HTTP port, e.g. 8080. SQLite file in a named volume so data survives restarts. |
| `game-server` | Headless Godot export of the game ("dedicated server" export) on a small Linux base | UDP port(s) published, e.g. `7000-7005/udp`. |

Phones connect to the development machine's LAN address; Docker publishes
the ports, the firewall must allow them (UDP for the game server).

Local Wi-Fi play (a phone hosts) needs none of this.

### Starting game servers

The backend needs a game server per online run. Options, simplest first:

1. **Fixed set** (proposed for the start): compose starts one (or a few)
   long-running game-server containers that wait for a party; the backend
   hands out a free one. No orchestration code at all. A run that ends
   resets the server for the next one.
2. **Backend starts containers** through the Docker API (mounting the Docker
   socket into the backend container). One fresh container per run; more
   moving parts, and the socket gives the backend full control of Docker.
3. **A fleet manager** (Agones on Kubernetes, or a managed game-server host).
   Only when hosting for many players.

### Day-to-day development

Compose runs the whole stack. While working on one piece, it is usually
faster to run that piece directly: the backend with `dotnet watch`, the game
server from the Godot editor or `godot --headless`, and the rest in compose.

## Open questions

- [ ] Where to host later: VPS + Docker, or a managed game-server service?
- [ ] Game-server image size and start-up time (Godot export + .NET runtime):
      measure.
- [ ] Fixed set of game servers: how does a server reset between runs
      (reload the scene, or exit and let Docker restart it)?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.2 | Decided: Docker Compose locally. Proposed: backend + game-server services, fixed set of game servers first; day-to-day dev outside compose. |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
