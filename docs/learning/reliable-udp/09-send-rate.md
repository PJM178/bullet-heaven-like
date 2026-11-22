# 09: Send Rate

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Bandwidth**: a link carries so many bytes per second. Sending faster
  fills router buffers: latency climbs first, then packets drop.
- **Bufferbloat**: big router buffers turn overload into seconds of delay
  instead of loss. Why a full upload makes a video call lag.
- **Congestion control**: back off when loss or RTT rises, speed up slowly
  when the link is clear. *Web:* TCP slow start and congestion avoidance
  (why downloads ramp up).
- **For this game**: 2-6 players with a known budget (multiplayer research,
  networking.md), so a fixed send-rate cap may be all that's needed; real
  congestion control only if measurements ask for it.

## Build

- A per-connection byte budget per second (token bucket), with reliable
  resends counted in it.
- Optional: a simple "bad conditions" mode (lower rate when RTT or loss
  rises), like Gaffer's good/bad mode.
- Bandwidth readout in the debug stats.

## Test

Done when:
- [ ] Sending is capped at the configured rate.
- [ ] Over a simulated bandwidth-capped link, latency stays low with the cap
      and climbs without it (bufferbloat shown).

## Findings

(Filled in while doing the step.)

## Reading

- Gaffer on Games, *Reliability and Congestion Avoidance over UDP* (the
  congestion part).

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
