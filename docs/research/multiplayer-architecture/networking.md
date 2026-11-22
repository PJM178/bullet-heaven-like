# Networking and Replication

Status: v0.1, 2026-11-22. **Exploration only**, part of the [multiplayer
architecture research](README.md). How state gets from the game server to
the phones: transport, the enemy horde, projectiles, joining and leaving.

## Transport

- **ENet over UDP** (`ENetMultiplayerPeer`), built into Godot. Has reliable
  and unreliable channels. Proposed.
- WebSocket / WebRTC peers exist in Godot too; not needed (no web export for C#
  anyway).
- Steam / platform relays: not relevant for Android-first.

## Replication: the horde problem

Bullet heaven's special difficulty is **hundreds of enemies**, many more than
in a typical shooter. Rough budget, to be checked by measurement:

- One enemy, full update: id 2 B + position 2×2 B (16-bit, quantised relative
  to an area) + state/kind 1 B ≈ **7 B**.
- 200 enemies in one player's interest area → 1.4 KB per snapshot.
- At 20 Hz: **~28 KB/s down per client** before delta compression.
- Server upload with 6 players: ~170 KB/s (~1.4 Mbit/s). Fine on a LAN or a
  VPS; worth reducing for mobile data.
- Client upload (inputs): tiny, well under 1 KB/s.

Ways to cut it, roughly in order of payoff:

1. **Interest management**: each client gets enemies within screen + margin.
   Players far apart see different enemies.
2. **Delta snapshots** against the last acknowledged one; unchanged enemies
   cost nothing, new ones are sent in full.
3. **Don't send what the client can compute.** Enemies walk straight at the
   nearest player; the client can extrapolate and the server only sends
   corrections when the error grows. (Separation pushes make this imperfect;
   to be tested.)
4. Lower send rate for far or slow things.

Godot's `MultiplayerSynchronizer` (one node per synced object) is convenient
for players but doesn't fit 600 pooled enemies. Proposal: **custom binary
snapshots** for enemies (one packet built from `EnemyManager`'s packed arrays,
sent with `MultiplayerPeer.PutPacket` or a byte-array RPC), and RPCs for rare
events. The packed-array layout `EnemyManager` already has is the right
shape for this.

### Projectiles

Sent as events: "kind K, from P, direction D, at tick T, id N". Clients
simulate the flight themselves (straight lines are predictable); only the
server decides hits and sends "projectile N hit / expired". Fits the
data-driven `ProjectileDefinition` system.

## Players joining and leaving

- Join: the server sends a full snapshot (all relevant state), then deltas.
- Disconnect: keep the player's character for a grace period (reconnect with
  the same join token), then remove it. What happens to their gear and XP is
  a design question.
- Mid-run join (a friend arrives late): technically the same as join;
  whether it's allowed is design.

## Open questions

- [ ] Is extrapolating enemies on the client good enough, given separation
      pushes and walls?
- [ ] Byte-array RPC or raw `PutPacket` for snapshots?
- [ ] Does C# on Android (experimental in Godot 4.7) behave with ENet? Test
      early on the phone.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
