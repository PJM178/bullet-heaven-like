# 01: UDP Sockets

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **UDP**: sends single datagrams to an address and port. No connection, no
  delivery or order guarantee, no duplicate protection, but no waiting
  either. Each `SendTo` is one packet; each `ReceiveFrom` gets one whole
  packet (never half, never two glued together, unlike a TCP stream).
  *Web:* TCP (under HTTP) is a byte stream with a connection; UDP is what
  DNS lookups and QUIC (HTTP/3) use.
- **Sockets and ports**: a socket is bound to a local port; the receiver
  learns the sender's address and port from each packet, which is how a
  server tells clients apart without connections.
  *Web:* same ports and addresses as HTTP; a server listening on 8080 is a
  bound TCP socket.
- **Blocking vs non-blocking**: a blocking receive waits for a packet; a
  game can't wait, so it polls (`Socket.Available`, non-blocking mode) once
  per tick. *Web:* async/await in ASP.NET hides the same problem behind
  threads.
- **MTU**: the largest packet that crosses the network unsplit, about 1500
  bytes on Ethernet, less over VPNs and tunnels. IPv4 and UDP headers take
  28 bytes. A safe payload is about **1200 bytes** (QUIC's number). Bigger
  UDP packets get split by IP, and losing any piece loses the packet.
  *Web:* why VPN or Docker networks sometimes break large requests.
- **Firewalls and UDP**: UDP ports are opened and published separately from
  TCP (Docker `"7777:7777/udp"`).

## Build

- Two console apps (or one with a `server` / `client` mode): the server
  echoes whatever it receives, the client sends numbered messages and prints
  the echoes with the round-trip time.
- A minimal check from the phone: a tiny Godot scene sending one UDP packet
  to the PC's echo server and showing the answer. Done this early to find out
  whether .NET sockets work on Android under Godot before building on them.

## Test

Done when:
- [ ] Echo works PC to PC (localhost and LAN address).
- [ ] The phone gets an answer from the PC's echo server over Wi-Fi.
- [ ] Sending bigger than the MTU: see what happens (does it arrive on
      localhost? on Wi-Fi?).

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *UDP vs. TCP* and *Sending and Receiving Packets*.
- .NET docs: `System.Net.Sockets.Socket` (`SendTo`, `ReceiveFrom`,
  `Blocking`, `Available`).

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
