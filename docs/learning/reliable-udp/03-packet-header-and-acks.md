# 03: Packet Header and Acks

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Packet header**: every packet starts with a few fixed fields. A first
  version:

  | Field | Size | Why |
  | --- | --- | --- |
  | Protocol id | 4 B | Ignore stray packets from other programs or old versions |
  | Packet type | 1 B | Data now; connect, challenge, disconnect in step 08 |
  | Sequence number | 2 B | Which packet this is |
  | Ack | 2 B | Newest packet received from the other side |
  | Ack bits | 4 B | Which of the 32 packets before `Ack` also arrived |

  *Web:* TCP's header carries sequence and ack numbers too; HTTP/2 and
  WebSocket frames are small binary headers in front of a payload.
- **Sequence numbers and wrap-around**: a 16-bit number runs out after 65535
  packets (about 18 minutes at 60 per second) and starts again at 0. "Is 2
  newer than 65530?" Yes: compare by whether the difference is under half
  the range. Bugs here only show after minutes of play.
  *Web:* TCP has the same problem with 32-bit numbers (RFC 1982).
- **Acks with a bitfield**: no separate ack packets. Every outgoing packet
  carries `Ack` plus 32 bits, so one packet confirms up to 33 received
  packets, and a lost ack is repeated by the next packets anyway.
  *Web:* TCP selective acknowledgement (SACK).
- **Bytes on the wire**: fields written by hand into a byte buffer in a
  fixed byte order (little endian, `BinaryPrimitives`), no padding. Reading
  checks the length first: any packet may be truncated or hostile.
  *Web:* every binary protocol works this way; parsing bugs are classic
  security holes.

## Build

- Header writer and reader over `Span<byte>`, with a length check.
- Sequence-number comparison that handles wrap-around.
- Per endpoint: next sequence number to send, a record of received
  sequence numbers (to build `Ack` and `Ack bits`), and a record of sent
  packets (to learn which were acked).
- An "acked" event per sent packet: the base later steps build on.

## Test

Done when:
- [ ] Header round-trips (write then read gives the same values); short or
      garbage packets are rejected, never crash.
- [ ] Wrap-around comparison is right around 65535 → 0.
- [ ] Over the simulated link at 20 % loss, the sender learns exactly which
      packets arrived, including across a wrap-around.

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Reliability and Congestion Avoidance over UDP*.
- RFC 1982, *Serial Number Arithmetic*.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
