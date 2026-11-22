# 02: Simulated Network

Status: not started. Part of [Reliable UDP](README.md).

## Learn

- **Why simulate**: on localhost every packet arrives, instantly and in
  order, so reliability code never runs its hard paths. Real networks lose
  packets (often in bursts), delay them unevenly (jitter), reorder and
  occasionally duplicate them.
- **Determinism**: a test that depends on real time and real randomness
  fails one run in a hundred and can't be replayed. A **fake clock** (time
  advanced by the test) and a **seeded random generator** make every run
  identical, so a failure can be reproduced and debugged.
- **Seams**: the transport sends through an interface; production plugs in
  the real socket, tests plug in the simulated link. *Web:* the same idea
  as injecting a fake `HttpClient` handler or an in-memory database in
  tests.

## Build

- A `INetworkLink`-style interface (send a packet to an address, receive
  pending packets) with two implementations: real UDP socket, and simulated.
- The simulated link, configurable per direction: loss (random and
  bursts), delay and jitter, reordering, duplication, bandwidth cap.
- A test harness: two endpoints connected by the simulated link, stepped in
  a loop with a fake clock.
- The test project (framework chosen here: xUnit or NUnit).

## Test

Done when:
- [ ] A test shows the configured loss rate (about 20 % of 10 000 packets
      lost at 20 % loss).
- [ ] Delay and reordering are visible and the same on every run with the
      same seed.
- [ ] The step 01 echo runs over the simulated link too.

## Findings

(Filled in while doing the step.)

## Reading

- Linux `tc netem` man page: the same knobs on a real interface (used again
  in step 11).

## Changelog

| Date | Change |
| --- | --- |
| 2026-11-22 | Step planned. |
