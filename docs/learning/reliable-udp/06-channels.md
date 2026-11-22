# 06: Channels

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Head-of-line blocking**: with one ordered stream, a single lost message
  holds back every message after it until the resend arrives, even ones
  that have nothing to do with it.
- **Channels**: independent ordered streams over one connection. Order is
  kept within a channel only, so a stall on one channel doesn't delay
  another. Each channel has its own delivery mode.
- *Web:* the reason HTTP/2 over TCP still stalls everything on one lost
  packet (its streams share TCP's single order), and why HTTP/3 moved to
  QUIC, whose independent streams are channels.

## Build

- A channel id in each message; per-channel message ids, send queues and
  ordering buffers (step 05's logic, one instance per channel).
- Channels declared at setup (e.g. 0 reliable events, 1 unreliable
  positions).

## Test

Done when:
- [ ] A message lost on channel 0 doesn't delay delivery on channel 1
      (measured in the harness).
- [ ] Every channel still meets step 05's guarantees at 20 % loss.

## Findings

(Filled in while doing the step.)

## Reading

- RFC 9000 (QUIC), the sections on streams, for comparison.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
