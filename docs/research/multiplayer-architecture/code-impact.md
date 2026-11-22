# Impact on the Current Code

Status: v0.6, 2026-11-22. **Exploration**, part of the [multiplayer
architecture research](README.md). What in today's single-player code would
have to change for multiplayer. Progress is noted per item.

## Planned refactor: core and shell (decided 2026-11-22)

Each simulation manager is split into two classes, **staying on Godot
types** (`Godot.Vector3`, `Mathf`; no `System.Numerics` in game code):

- **Core**: the game logic and its state (positions, health, kinds, the
  spatial grid). Never touches nodes. Data comes in as parameters (player
  positions, `delta`); results go out as data (new positions, events such as
  "enemy died", "contact damage").
- **Shell**: the Godot node. Holds the core as a field, calls it every
  physics tick, converts what it returns into node transforms, sprites,
  animation and effects, and applies events (damage to players, hit flash).
  The core never knows the shell.

Worked example, `EnemyManager.MoveEnemies` today: reads one `Target` node,
moves enemies, judges contact damage, then sets node transforms and picks
sprite directions in the same loop. After the split: `Step(delta,
playerPositions, events)` in the core, the transform and sprite loop in the
shell.

Why, all needed for multiplayer and useful in single player too:

| Benefit | |
| --- | --- |
| Several players | Positions passed in, instead of one `Target` node. |
| Server skips visuals | The shell decides whether to draw; a headless server runs only the core. |
| Network-ready events | Events as data can be applied locally *and* sent to clients; today they are direct calls clients never hear about. |
| Snapshots | The core's arrays are what a snapshot serialises, nothing visual mixed in. |
| Clearer code | `EnemyManager` (~1300 lines) does simulation, pooling and visuals; each part gets smaller. |

Progress:

- [x] **Enemies** (2026-11-22): `EnemySimulation` (core) + `EnemyManager`
      (shell), `IObstacleQuery` for walls. Slots keep one node per enemy
      despite swap remove. Core takes several players and gives contact
      damage per player. Damage returns a result (None/Hit/Killed) instead
      of an event list. Weapons unchanged. As built:
      [systems/enemies.md](../../systems/enemies.md). Checked with the local
      headless tests before/after (walls, kinds, sword, wall blocking):
      same results.
- [x] **Projectiles** (2026-11-22): `ProjectileSimulation` (core) +
      `ProjectileManager` (shell). Hits on other managers go through
      interfaces they implement (`IEnemyTargets`, `IBreakableTargets`), so
      damage still shows its flash at once. Returning projectiles target a
      player index instead of a node. As built:
      [systems/projectiles.md](../../systems/projectiles.md). Local tests
      before/after match (flight, return, walls, sealed room).
- [x] **Pickups and XP orbs** (2026-11-22): `PickupSimulation` +
      `PickupManager`, `XpSimulation` + `XpManager`. Loot rolling moved
      into the core. Orbs have no nodes (MultiMesh copy i = orb i), so no
      slots: the core lists which orbs changed instead. XP comes back as a
      per-player total per step. As built:
      [systems/pickups.md](../../systems/pickups.md). Checked: placed-coin
      pickup test identical before/after; XP invariant (gained + in orbs =
      kills x XP per kill) holds; screenshots.
- [x] **Breakables** (2026-11-22): `BreakableSimulation` + `BreakableManager`.
      Props never move or swap, so slot = index and there is no Step;
      each damage call lists its hits (slot, broke) and the shell acts on
      them at once. As built: [systems/breakables.md](../../systems/breakables.md).
      Checked: every damage path (circle, arc, ring, projectile hit test,
      hit vs break, broken takes no hits) gives identical results
      before/after.
- [x] **`WorldObstacles`: no split needed** (2026-11-22). It is already
      shape data plus math with no visuals; the node only holds the
      registry so blockers and props can find it. Cores use it through
      `IObstacleQuery`. Adding and removing shapes stays on the node,
      called by shells (blockers, `BreakableManager`).
- The core/shell refactor is **done** for every simulation manager.
- [ ] Next, outside this refactor: several players actually passed in.
      Every core already takes a list of player positions; the shells
      still pass one player. Needs a player list (comes with the
      multiplayer spikes).

Weapons' calls into the queries keep working (same types).

Things to keep right:

- Order within a physics step: `ProjectileManager` calls `HitTest` after the
  enemies move and the grid is rebuilt; the split keeps that order.
- `EnemyDefinition` is a resource that also holds scenes; the core only
  needs its numbers (speed, radii, health).
- No per-frame allocations: event lists and position arrays are allocated
  once and reused.

### Not doing: plain .NET types in the game (decided 2026-11-22)

Swapping the core to `System.Numerics` (engine-free core) is **not** done:
game code stays on Godot types. It would only buy engine-free unit tests, a
Godot-free server and engine independence, none needed now, at the cost of
conversions at every boundary and behaviour traps (e.g. .NET's
`Vector3.Normalize` of a zero vector gives NaN, Godot's `Normalized()` gives
zero). See [stack.md](stack.md).

## Other changes multiplayer needs

- **Input as data.** `Player` reads `Input.GetVector` directly. It would read
  an input command struct, filled from the joystick locally or from the
  network on the server.
- **Several players.** Besides the enemy core taking positions: spawning
  around "the player" becomes around players. Camera, HUD, minimap stay per
  client.
- **Stable network ids.** Swap-remove arrays reorder enemies; snapshots need
  an id that survives (check what `GetId` gives).
- **Seeded randomness.** `Random.Shared` → a per-run seeded generator on the
  host. Not for determinism; for reproducible bugs.
- **Profile.** `Profile.BankCoins` writes locally; online, the profile may be
  synced with the backend (see [backend.md](backend.md)).
- **Player movement.** `Player` uses `MoveAndSlide()` (engine physics). With
  client-authoritative movement it can stay as it is; the host only receives
  positions.

## Open questions

- [ ] Naming: `EnemySimulation` + `EnemyManager` used for enemies; keep
      `<Thing>Simulation` + `<Thing>Manager` for the other managers?
- [ ] Events: enemies needed none beyond return values and the step's
      Spawned/RemovedSlots lists. For network sync, is that enough, or does
      the host need a per-step event list (hits, deaths) to send?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-22 | v0.6 | Breakables split done; WorldObstacles needs no split; refactor complete. |
| 2026-11-22 | v0.5 | Pickups and XP orbs split done. |
| 2026-11-22 | v0.4 | Projectiles split done; cross-manager hits through interfaces. |
| 2026-11-22 | v0.3 | Enemies split done (EnemySimulation + EnemyManager); progress list; open questions updated. |
| 2026-11-22 | v0.2 | Decided: core/shell split of the simulation managers on Godot types (step 1); no .NET math types in game code (step 2 rejected). Worked example, benefits, order and allocation notes. |
| 2026-11-22 | v0.1 | Split out of the single multiplayer-architecture.md (v0.3); content unchanged. |
