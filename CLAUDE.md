# Bullet Heaven Like

A 2D bullet heaven game (auto-firing weapons, swarms of enemies, XP pickups,
level-up upgrades). **Android is the first target platform.**

## Stack

- **Godot 4.7.2 .NET** (C#). Editor binary:
  `~/Applications/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`
- **C# / .NET**: .NET SDK 10 is installed. `BulletHeavenLike.csproj` targets
  `net8.0` (and `net9.0` for Android, which Godot requires).
- Assembly and root namespace: `BulletHeavenLike`.
- Renderer: **GL Compatibility** (widest Android device support).
- Orientation: **portrait**. Stretch mode `canvas_items`, aspect `expand`.
- Android export: arm64-v8a only, package `com.pjmproductions.bulletheavenlike`.
  C# on Android is officially *experimental* in Godot 4.7, so test on the
  real phone often.
- Web export is not possible (C# projects can't export to Web).

## Commands

```bash
dotnet build                                            # compile C# (fast check)
<godot> --headless --path . --build-solutions --quit    # build via Godot
```

`--build-solutions` prints a harmless `EditorSettings not instantiated` error
about `shutdown_adb_on_exit`; ignore it.

Running the game and deploying to the phone happen from the Godot editor
(F5 / the Android one-click deploy icon). Close the Godot editor before
renaming or moving project files from outside it.

## Folder layout

Files are grouped by **feature**, not by type: a script lives next to the
scenes, models, shaders and materials it belongs to.

```
game.tscn, main.tscn   entry scenes (UI shell, and the 3D world)
core/                  shared by everything (base PS1 shader, hit flash)
player/                Player, camera, player model
enemies/               EnemyManager, enemy scenes and models
weapons/<name>/        one folder per weapon (sword/, projectiles/, ...)
skills/                SkillDefinition (resource) and SkillManager (learned
                       skills, random level-up choices); each weapon's
                       <name>_skill.tres lives in its weapon folder
pickups/               XP orbs and other collectables
world_markers/         WorldMarkers (points of interest, events) shown on the minimap
ui/                    layout, joystick, HUD
props/                 level decoration; breakables/ (BreakableManager, definitions)
art/                   Blender sources and generator scripts (.gdignore'd)
docs/                  design/ (plans, decisions) and systems/ (as built);
                       .gdignore'd, indexed in docs/README.md
addons/sprite_kit/     .spritesheet importer -> SpriteFrames, DirectionalSprite3D /
                       SpriteDirections, KeyframeEffect (.str effect player)
local_assets/          local sprites/effects/models used in-game (git-ignored, but
                       exported): characters/{players,monsters}/, pickups/,
                       effects/<name>/ (*.bin), props/<category>/ (.glb)
```

A new weapon gets its own folder under `weapons/`; code shared by several
weapons goes in `weapons/` itself. All C# stays in the single
`BulletHeavenLike` namespace regardless of folder.

Move or rename files with the Godot editor's FileSystem dock, which updates
references. From outside the editor (editor closed), `git mv` the file
together with its `.uid` / `.import` sidecars and rewrite its `res://` paths.

## Docs

The index below is imported into every session; the docs it lists are not.
Open only the doc a task needs.

@docs/README.md

## Local assets

`local_assets/` and `research/` are not committed, so committed scenes must
not reference them directly: load by path if present (`FramesPath`,
`core/OptionalScene.cs`). Where the assets come from and how to bake them is in
`CLAUDE.local.md` (git-ignored).

## Code conventions

- C# for game code. GDScript only for small glue/UI scripts, if at all.
- Godot C# rules: classes are `public partial class`, the class name matches the
  file name, put them in the `BulletHeavenLike` namespace.
- Use Godot's C# API naming (PascalCase: `MoveAndSlide()`, `Velocity`).
- Prefer `[Export]` fields for tunable values (speed, damage, spawn rates) so
  they can be adjusted in the editor.
- **The developer is learning C# alongside this project.** Write clear,
  idiomatic code with comments that explain *why* and any non-obvious C# or
  Godot concepts. Avoid clever tricks when a plain version reads better.

## Performance rules (mobile, many entities)

- Pool bullets, enemies and pickups. Don't `QueueFree()`/instantiate them every frame.
- Avoid per-frame allocations in `_Process`/`_PhysicsProcess` (no LINQ, no new
  lists, no string building in hot paths).
- Enemies: simple steering toward the player, no pathfinding. Use `Area2D` or
  distance checks rather than rigid bodies, and set collision layers/masks so
  enemies don't collide with each other.
- If node count becomes the bottleneck, move to `MultiMeshInstance2D` or
  `RenderingServer`/`PhysicsServer2D`. Don't do that before profiling shows a need.
- Judge performance on the real Android phone, not the desktop.

## Repository

- GitHub: https://github.com/PJM178/bullet-heaven-like (public; don't commit secrets).
- `.godot/` (cache, export credentials) and `.vscode/` are git-ignored.
