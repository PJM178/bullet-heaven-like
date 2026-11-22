# 11: Phone on the LAN

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Real Wi-Fi**: more jitter and bursty loss than wired, power saving that
  delays packets when the phone's radio sleeps.
- **Android specifics**: the INTERNET permission in the export; .NET sockets
  under Godot's experimental C# Android support (first checked in step 01,
  here under real load).
- **Measuring on the device**: CPU, battery and upload for a phone host
  (open questions in the multiplayer research's game-server.md).
- **Degrading on purpose**: Linux `tc netem` on the PC's interface to add
  delay and loss to a real link.

## Build

- The step 10 setup with the phone as host and as client.
- Debug stats on screen: RTT, loss, bytes per second.

## Test

Done when:
- [ ] Phone hosts, PC joins; PC hosts, phone joins.
- [ ] Movement looks smooth at real Wi-Fi conditions and with `tc netem`
      adding 100 ms and 5 % loss.
- [ ] Measured: RTT, loss and bandwidth on the phone, written in Findings.

## Findings

(Filled in while doing the step.)

## Reading

- Linux `tc netem` man page.

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
