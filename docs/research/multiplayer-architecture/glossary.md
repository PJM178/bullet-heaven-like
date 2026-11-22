# Multiplayer Glossary

Status: v0.1, 2026-11-22. **Exploration only**, part of the [multiplayer
architecture research](README.md). The core ideas of networked action games,
for learning; the other docs use these terms.

| Term | Meaning |
| --- | --- |
| **Authority** | Who decides the truth about something. "Server-authoritative" = the server's simulation is the real game; clients only ask and display. |
| **Tick** | One fixed step of the simulation (e.g. 1/30 s). Server and clients number ticks so messages can say *when* something happened. |
| **Input command** | What a client sends: "at tick 1042 my stick was (0.3, -0.9)". Not "I am at position X". |
| **Snapshot** | What the server sends: the state of (some of) the world at a tick. |
| **Delta compression** | Sending only what changed since a snapshot the client has confirmed (acknowledged) receiving. |
| **Interest management** | Sending each client only what is relevant to them (near them), not the whole world. |
| **Interpolation** | Clients draw other entities slightly in the past (e.g. 100 ms), smoothly blending between two received snapshots. Hides jitter and packet loss. |
| **Extrapolation / dead reckoning** | Guessing where something is now from its last known position and velocity. Works well for things that move predictably. |
| **Client-side prediction** | The client moves its own player immediately from its own input instead of waiting for the server, so controls feel instant. |
| **Reconciliation** | When the server's answer for the player's position arrives, the client rewinds to it and replays the inputs the server hasn't processed yet. |
| **Reliable / unreliable channel** | Reliable = resent until it arrives, in order (events: "enemy died", "level up"). Unreliable = fire and forget (snapshots: a newer one is coming anyway). |
| **RPC** | Remote procedure call: calling a method on another machine. Godot has `[Rpc]` methods. Good for rare events, bad for hundreds of entities per tick. |
| **Lockstep** | All machines run the full simulation and only exchange inputs. Requires bit-identical results everywhere (determinism). |

## Open questions

- [ ] None yet.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
