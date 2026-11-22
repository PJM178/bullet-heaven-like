# 07: Fragmentation

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Why**: a message bigger than one packet's payload (about 1200 bytes, see
  step 01) must be cut into pieces and rebuilt on arrival. Joining a run
  (a full snapshot) needs this.
- **Reliable fragments**: each piece is resent on its own until acked; the
  message is delivered when all pieces are in.
- **Unreliable fragments are all-or-nothing**: lose one piece, lose the
  message. At 5 % loss a 10-piece message is lost about 40 % of the time.
  So big unreliable data (enemy snapshots) should fit one packet or be split
  into self-contained chunks: a design rule for the game, not only the
  transport.
- **Limits**: a maximum message size and a cap on half-built messages, or a
  hostile sender fills the receiver's memory.
- *Web:* IP fragmentation, and why QUIC and modern TCP avoid it (path MTU
  discovery).

## Build

- Fragment header (message id, fragment index, fragment count).
- Splitting on send; reassembly buffers with timeouts and size limits on
  receive.

## Test

Done when:
- [ ] A 10 KB reliable message arrives intact at 20 % loss.
- [ ] Measured: how often a 2-, 5- and 10-fragment unreliable message
      survives at 5 % loss (compare with the arithmetic above).
- [ ] Oversized or malformed fragment headers are rejected without
      allocating.

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Packet Fragmentation and Reassembly* and *Sending Large
  Blocks of Data*.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
