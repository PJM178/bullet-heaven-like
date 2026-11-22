# 05: Reliable Messages

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Messages inside packets**: the game sends *messages*; the transport
  packs several small ones into each packet (fewer packets, fewer headers).
- **Delivery modes**:

  | Mode | Behaviour | Game use |
  | --- | --- | --- |
  | Reliable ordered | Resent until acked, delivered in order | Upgrade picked, player joined, enemy died |
  | Unreliable sequenced | Maybe lost; anything older than the newest received is dropped | Positions, snapshots |
  | Unreliable | Maybe lost, any order | One-off effects |

  *Web:* TCP is "reliable ordered" for everything; WebRTC data channels and
  QUIC datagrams offer the unreliable modes.
- **Message ids vs packet sequence numbers**: a resent message travels in a
  new packet, so reliable messages need their own ids.
- **Resending**: a reliable message stays in a send queue until a packet
  carrying it is acked; if the resend timeout (step 04) passes, it rides
  again in the next packet.
- **Ordering buffer**: the receiver holds reliable messages that arrive
  ahead of a gap until the gap is filled, then delivers them in order and
  drops duplicates.

## Build

- Message format inside a packet (type/mode, id, length, payload).
- Packing messages into packets up to the payload size.
- Reliable send queue with resends; receive buffer with ordering and
  duplicate removal.
- Unreliable-sequenced "newest wins" filter.

## Test

Done when:
- [ ] At 20 % loss with reordering and duplicates, 10 000 reliable messages
      all arrive, once each, in order.
- [ ] Unreliable-sequenced never delivers an older message after a newer
      one.
- [ ] The send queue empties when the network recovers (nothing stuck).

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Reliable Ordered Messages*.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
