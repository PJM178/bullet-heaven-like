# 04: Round-Trip Time

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **RTT samples**: store the send time per packet; when its ack arrives,
  now minus send time is one round-trip-time sample.
- **Smoothing**: single samples jump around (jitter). Keep a smoothed
  average and a smoothed variation, each moved a little toward every new
  sample.
- **Resend timeout**: wait about `smoothed RTT + 4 × variation` before
  deciding a packet was lost. Too short resends packets that were only
  slow; too long makes every loss a long stall.
  *Web:* this is TCP's retransmission timer (RFC 6298), nearly to the
  formula.
- **Packet loss estimate**: the share of sent packets never acked, useful
  for debugging and for step 09.

## Build

- RTT samples from acks (only for packets acked the first time they could
  be, so old acks don't skew it).
- Smoothed RTT, variation and resend timeout per connection.
- A loss-rate estimate over a recent window.
- A debug readout (console or test output) of RTT, variation and loss.

## Test

Done when:
- [ ] With a fixed 100 ms simulated delay, RTT settles near 200 ms.
- [ ] With jitter, variation grows and the timeout stays above most samples.
- [ ] The loss estimate tracks the simulated loss rate.

## Findings

(Filled in while doing the step.)

## Reading

- RFC 6298, *Computing TCP's Retransmission Timer*.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
