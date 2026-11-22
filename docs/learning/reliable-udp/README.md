# Reliable UDP (learning project)

Status: v0.1, 2026-11-22. **Not started.** A learning project: build a
reliable-UDP network transport from scratch, one step at a time, and end up
with the transport the game uses. Each step has its own doc with what it
teaches, what gets built, how it's tested and what we found.

## Why

- **Learning the underlying architecture.** A game transport is a small TCP
  built by hand: sequence numbers, acks, resends, ordering, connections. The
  same ideas run under every web request (TCP, HTTP/2, HTTP/3 and QUIC), so
  each step points at its web counterpart.
- **A real outcome.** The finished transport carries the game's multiplayer
  traffic (phone host and dedicated server). How the game uses it is in the
  [multiplayer research](../../research/multiplayer-architecture/README.md),
  especially [networking.md](../../research/multiplayer-architecture/networking.md).
- **ENet as reference and fallback.** Godot's built-in ENet does the same
  job. The game talks to a small interface, so ENet could stand in if ours
  ever disappoints, and ENet's source is a reference to compare against.

Decided 2026-11-22.

## What a transport does

UDP alone sends single packets: no connection, no guarantee they arrive,
arrive once, or arrive in order. TCP guarantees all of that, but one lost
packet stalls everything behind it, which games can't afford (an old
position is worthless once a newer one exists). A game transport sits in
between: UDP underneath, and each message chooses what it needs.

```
 game                    snapshots, inputs, events   (multiplayer research)
 message layer           our binary formats
 transport  <- this      connections, reliability, channels, fragments
 UDP socket              System.Net.Sockets
 IP / network
```

## Ground rules

- **Plain C#, no Godot** (`System.Net.Sockets`): runs in console apps and
  unit tests; the game references it like any library.
- **Tested on a bad network on purpose.** On localhost nothing is lost, so
  every bug hides. Every step from 02 on is tested through a simulated link
  with loss, delay, reordering and duplicates, driven by a fake clock.
- **No per-frame allocations** once it runs in the game (the mobile rule in
  `CLAUDE.md`): preallocated buffers, pooled objects, `Span<byte>`.
- **Any packet may be hostile.** Reading checks every length and field.

## Steps

| # | Doc | Learn | Status |
| --- | --- | --- | --- |
| 01 | [01-udp-sockets.md](01-udp-sockets.md) | UDP, sockets, ports, MTU; first phone check | Not started |
| 02 | [02-simulated-network.md](02-simulated-network.md) | Testing networks deterministically | Not started |
| 03 | [03-packet-header-and-acks.md](03-packet-header-and-acks.md) | Header, sequence numbers, wrap-around, ack bitfield, bytes on the wire | Not started |
| 04 | [04-round-trip-time.md](04-round-trip-time.md) | RTT, smoothing, resend timeout | Not started |
| 05 | [05-reliable-messages.md](05-reliable-messages.md) | Delivery modes, resends, ordering buffers | Not started |
| 06 | [06-channels.md](06-channels.md) | Head-of-line blocking | Not started |
| 07 | [07-fragmentation.md](07-fragmentation.md) | Big messages, all-or-nothing loss | Not started |
| 08 | [08-connections.md](08-connections.md) | Handshake, timeouts, spoofing, amplification | Not started |
| 09 | [09-send-rate.md](09-send-rate.md) | Bandwidth, congestion, bufferbloat | Not started |
| 10 | [10-into-the-game.md](10-into-the-game.md) | The game-facing interface; two game instances | Not started |
| 11 | [11-phone-on-lan.md](11-phone-on-lan.md) | .NET sockets on Android, real Wi-Fi | Not started |

After step 11 the multiplayer work continues on top (snapshots, interest
management, deltas: see networking.md).

## Step doc format

Each step doc has: **Status**, **Learn** (concepts, each with its web
counterpart), **Build** (what gets written), **Test** (how, and done when),
**Findings** (filled in while doing it: surprises, measurements, mistakes),
**Reading**, and a changelog.

## Reading (whole project)

- Glenn Fiedler, *Networking for Game Programmers* and *Building a Game
  Network Protocol* (gafferongames.com): the classic step-by-step series
  this project roughly follows.
- ENet's source, `protocol.c`: a compact, real implementation.
- RFC 9293 (TCP), RFC 6298 (TCP retransmission timer), RFC 1982 (sequence
  number arithmetic), RFC 9000 (QUIC): the web-side versions of the same
  ideas.

## Open questions

- [ ] Where the code lives: the experiments and console apps (steps 01-09)
      apart from the game's code, with the finished transport moved to a
      library the game references? Folder names to be decided at step 01.
- [ ] Test framework: xUnit (the .NET default) or NUnit.
- [ ] Encryption: none at first (co-op, relaxed trust); something once
      servers are public?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.1 | Project started on paper: why, ground rules, eleven steps with one doc each. |
