# Docs

Two kinds of doc, kept apart:

- **`design/`**: plans and decisions. What we intend to build and why:
  proposals, choices made, open questions. Like ADRs, they keep the
  reasoning even after the thing is built.
- **`systems/`**: how the game works *as built*. Facts only, kept in sync
  with the code; anything not yet decided goes under Open questions.

One doc per self-contained piece of the game (weapons, enemies, progression,
...), never one big doc. Each has a status line, open questions and a
changelog. Read only the doc a task needs; update it when decisions change
or the code does. Add one line here per doc and keep each line short: this
index is loaded into every session.

## Design

- [weapon-plan.md](design/weapon-plan.md): shared, class-specific and fusion skills; build steps for the skill system
- [meta-progression.md](design/meta-progression.md): coins kept between runs, base area after death; shape of upgrades undecided
- [pickups.md](design/pickups.md): pickup architecture: definitions, one touch-pickup manager, XP orbs separate
- [breakables.md](design/breakables.md): breakable props: definitions, one manager, hit only when caught in attacks
- [minimap.md](design/minimap.md): minimap (over a compass bar), screen-aligned, reveal states for world markers

## Systems

- [player.md](systems/player.md): controls, movement, hearts, XP and levels, death, animation, camera
- [pickups.md](systems/pickups.md): coins and touch pickups, PickupManager, pickup radii
- [breakables.md](systems/breakables.md): barrel and other breakable props, how weapons hit them, loot
- [minimap.md](systems/minimap.md): minimap, WorldMarkers, reveal states, prototype markers
