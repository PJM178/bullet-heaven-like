# Docs

Four kinds of doc, kept apart:

- **`design/`**: plans and decisions. What we intend to build and why:
  proposals, choices made, open questions. Like ADRs, they keep the
  reasoning even after the thing is built.
- **`systems/`**: how the game works *as built*. Facts only, kept in sync
  with the code; anything not yet decided goes under Open questions.
  Code-level detail (classes, methods, files, values) lives only here;
  design docs talk about gameplay and link to their systems doc. Code
  changes planned for a decided design go under Planned changes.
- **`research/`**: technical exploration and learning before anything is
  designed or decided (options, trade-offs, spikes). May name code; turns
  into design/systems docs once decisions are made. A big topic gets a
  folder: a README.md overview plus one doc per subtopic.
- **`learning/`**: learning projects: building something step by step to
  understand how it works underneath. One folder per project: a README.md
  (why, steps with status) plus one doc per step (learn, build, test,
  findings). The outcome may become part of the game.

Code paths in docs (`enemies/EnemyManager.cs`, ...) are relative to `game/`,
the Godot project; server-side paths (`backend/...`, `deploy/...`) are
relative to the repo root.

One doc per self-contained piece of the game (weapons, enemies, progression,
...), never one big doc. Each has a status line, open questions and a
changelog. Read only the doc a task needs; update it when decisions change
or the code does. Add one line here per doc and keep each line short: this
index is loaded into every session.

## Design

- [weapon-plan.md](design/weapon-plan.md): shared, class-specific and fusion skills; build steps for the skill system
- [meta-progression.md](design/meta-progression.md): coins kept on death, tiered stat upgrades, menu as base (walkable later)
- [maps-and-events.md](design/maps-and-events.md): several maps, events around them; maps unlocked by completing map-specific events
- [smithing.md](design/smithing.md): equipment smithing (idea only); crafting materials proposed beside coins
- [pickups.md](design/pickups.md): pickups as data, one touch-pickup system, loot tables, XP orbs separate
- [breakables.md](design/breakables.md): breakable props: definitions, one manager, hit only when caught in attacks
- [minimap.md](design/minimap.md): minimap (over a compass bar), screen-aligned, reveal states for world markers
- [walk-in-spaces.md](design/walk-in-spaces.md): entering houses/caves on foot; cutaway of roof and near walls chosen
- [projectiles.md](design/projectiles.md): one projectile system for every weapon; projectiles and firing as data
- [enemy-definitions.md](design/enemy-definitions.md): enemy kinds as data, a fixed pool per kind, weighted spawning
- [enemy-navigation.md](design/enemy-navigation.md): enemy collision (obstacle push-out, built) and flow-field pathfinding (planned); costs

## Systems

- [player.md](systems/player.md): controls, movement, hearts, XP and levels, death, animation, camera
- [pickups.md](systems/pickups.md): coins and touch pickups, PickupManager, LootTable/DropLoot, pickup radii
- [breakables.md](systems/breakables.md): barrel and other breakable props, how weapons hit them, loot
- [minimap.md](systems/minimap.md): minimap, WorldMarkers, reveal states, prototype markers
- [walk-in-spaces.md](systems/walk-in-spaces.md): WalkInSpace cutaway component, the placed house
- [world-obstacles.md](systems/world-obstacles.md): WorldObstacles, ObstacleBlocker; walls block enemies, attacks, projectiles, contact
- [projectiles.md](systems/projectiles.md): ProjectileManager, ProjectileDefinition, ProjectileWeapon
- [enemies.md](systems/enemies.md): EnemyDefinition, kinds, per-kind pools, spawning, weapon queries
- [skills.md](systems/skills.md): SkillDefinition, SkillManager; planned code changes for the weapon plan
- [meta-progression.md](systems/meta-progression.md): title/upgrade menus, Profile save, upgrades, coins banked on death
- [backend.md](systems/backend.md): ASP.NET backend shell, shared library, root solution, Docker, Makefile

## Learning

- [reliable-udp/](learning/reliable-udp/README.md): own network transport over UDP from scratch, 11 steps, each tied to web protocols

## Research

- [multiplayer-architecture/](research/multiplayer-architecture/README.md): online co-op tech, one doc per topic (game server, networking, backend, stack...)
