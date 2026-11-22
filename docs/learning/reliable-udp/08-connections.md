# 08: Connections

Status: not started. Part of [Reliable UDP](README.md).

## Learn

UDP has no connections; we build them:

1. Client sends a **connect request**, padded to full size.
2. Server answers with a **challenge** holding a random token.
3. Client echoes the token; only then does the server create the connection.
4. Both sides send **keep-alives** when idle; no packet for a few seconds
   means **timed out**.
5. **Disconnect** is sent several times (it may be lost), then the slot is
   freed.

- **Spoofing**: a UDP source address can be faked. The challenge step means
  a spoofer never sees the token, so it can't open connections.
- **Amplification**: if a small request triggered a big reply, an attacker
  could aim replies at a victim by faking the victim's address. Padding the
  request to at least the reply's size removes the gain.
- **Server slots**: a maximum client count; full servers say so instead of
  staying silent.
- *Web:* TCP's three-way handshake and SYN cookies; QUIC's address
  validation with retry tokens and its padded first packet.

## Build

- Packet types: connect request, challenge, challenge response, accepted,
  denied (full), keep-alive, disconnect.
- Connection state per endpoint (connecting, connected, disconnected) and
  connection events for the user of the transport.
- Timeouts and keep-alives; packets from unknown addresses ignored.

## Test

Done when:
- [ ] Clients connect, time out when the link is cut, and leave cleanly.
- [ ] A spoofed connect (wrong token) never creates a connection.
- [ ] The server never sends more bytes than it received from an
      unconnected address.
- [ ] A full server denies the extra client.

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Client Server Connection*; netcode.io's standard (signed
  connect tokens, for when the backend hands out tokens later).

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
