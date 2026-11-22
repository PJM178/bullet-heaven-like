# 10: Into the Game

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **The game-facing interface**: listen / connect, send on a channel,
  poll received messages and connection events, disconnect. Small enough
  that ENet (Godot's `ENetConnection`) could sit behind it as a fallback.
- **Polling from the tick**: the game calls the transport once per physics
  tick; no threads or callbacks into Godot (Godot nodes must only be touched
  from the main thread).
- **No per-frame allocations**: preallocated buffers, pooled message and
  packet objects, `Span<byte>`; checked with a profiler, not assumed.
- **Godot and a plain library**: the game project references the transport
  project; the transport never references Godot.

## Build

- The interface and its implementation over our transport.
- In the game: a debug host/join (host on a port, join by IP), one player
  per connected peer, each client sending its own player's position
  (unreliable sequenced, about 20 per second; relaxed trust from the
  multiplayer research), the host relaying, others interpolating.
- A headless host: the same project with `--headless`, no local player.

## Test

Done when:
- [ ] Two or three desktop instances (editor: Debug > Customize Run
      Instances) see each other move smoothly.
- [ ] A headless host works the same.
- [ ] No allocations per tick in the transport (profiler).

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Snapshot Interpolation*.
- [game-server.md](../../research/multiplayer-architecture/game-server.md)
  (relaxed trust, who owns what).

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
