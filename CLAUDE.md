# Bullet Heaven Like

A 2D bullet heaven game (auto-firing weapons, swarms of enemies, XP pickups,
level-up upgrades). **Android is the first target platform.**

## Stack

- **Godot 4.7.2 .NET** (C#). Editor binary:
  `~/Applications/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`
- **Repo layout**: a monorepo. The Godot project is `game/` (its root is
  `game/project.godot`; all `res://` paths are relative to it). `docs/` and
  `art/` sit beside it at the repo root, with the server side: `backend/`
  (ASP.NET Core, net10.0), `shared/` (plain C# library, net8.0, never
  references Godot) and `deploy/` (Docker). See `docs/systems/backend.md`.
- **C# / .NET**: .NET SDK 10 is installed. `game/BulletHeavenLike.csproj` targets
  `net8.0` (and `net9.0` for Android, which Godot requires).
- Assembly and root namespace: `BulletHeavenLike`.
- Renderer: **GL Compatibility** (widest Android device support).
- Orientation: **portrait**. Stretch mode `canvas_items`, aspect `expand`.
- Android export: arm64-v8a only, package `com.pjmproductions.bulletheavenlike`.
  C# on Android is officially *experimental* in Godot 4.7, so test on the
  real phone often.
- Web export is not possible (C# projects can't export to Web).

## Commands

Run from `game/` (the Godot project):

```bash
dotnet build                                            # compile C# (fast check)
<godot> --headless --path . --build-solutions --quit    # build via Godot
```

`--build-solutions` prints a harmless `EditorSettings not instantiated` error
about `shutdown_adb_on_exit`; ignore it.

Server side, from the repo root:

```bash
dotnet build      # BulletHeavenLike.slnx: backend, shared and game
make run          # backend in Docker on :8080, logs on screen (make up/down/logs/health)
```

Godot keeps its own `game/BulletHeavenLike.sln`; the root `.slnx` is for
`dotnet` only.

Running the game and deploying to the phone happen from the Godot editor
(F5 / the Android one-click deploy icon). Close the Godot editor before
renaming or moving project files from outside it.

## Folder layout

Repo root: `game/` (the Godot project), `backend/`, `shared/`, `deploy/`,
`docs/`, `art/`, `.claude/`, plus `BulletHeavenLike.slnx` and `Makefile`.
Inside `game/`, files are grouped by **feature**, not by type: a script
lives next to the scenes, models, shaders and materials it belongs to.

```
game/
game.tscn, main.tscn   a run (UI shell, and the 3D world); ui/title_menu.tscn starts the game
core/                  shared by everything (base PS1 shader, hit flash,
                       WorldObstacles: walls/props that block movement and attacks)
player/                Player, camera, player model
enemies/               EnemyManager, enemy scenes and models;
                       kinds/ (EnemyDefinition .tres, one per kind of enemy)
weapons/<name>/        one folder per weapon (sword/, hammer/, ...); projectiles/ is the
                       shared projectile system (ProjectileManager, ProjectileWeapon)
skills/                SkillDefinition (resource) and SkillManager (learned
                       skills, random level-up choices); each weapon's
                       <name>_skill.tres lives in its weapon folder
pickups/               XP orbs and other collectables; LootTable (what anything drops)
meta/                  between runs: Profile (save, autoload), UpgradeDefinition,
                       upgrades/ (.tres), upgrade menu
world_markers/         WorldMarkers (points of interest, events) shown on the minimap
ui/                    title menu (main scene), layout, joystick, HUD
props/                 level decoration; breakables/ (BreakableManager, definitions),
                       walk_in/ (WalkInSpace: cutaway with wall stubs for enterable spaces)
addons/sprite_kit/     .spritesheet importer -> SpriteFrames, DirectionalSprite3D /
                       SpriteDirections, KeyframeEffect (.str effect player)
local_assets/          local sprites/effects/models used in-game (git-ignored, but
                       exported): characters/{players,monsters}/, pickups/,
                       effects/<name>/ (*.bin), props/<category>/ (.glb),
                       exteriors/, interiors/ (building blocks),
                       kit/<style>/ (modular walk-in building pieces),
                       buildings/<name>.tscn (assembled from the blocks)
research/              local experiments and tests (git-excluded)

(repo root)
backend/BulletHeavenLike.Backend/   ASP.NET Core backend (namespace BulletHeavenLike.Backend)
shared/BulletHeavenLike.Shared/     plain C# shared by game and servers (BulletHeavenLike.Shared)
deploy/                Dockerfile, docker-compose.yml (build context = repo root)
art/                   Blender sources and generator scripts
docs/                  design/ (plans, decisions), systems/ (as built),
                       research/ (exploration); indexed in docs/README.md.
                       Code paths in docs are relative to game/.
```

A new weapon gets its own folder under `weapons/`; code shared by several
weapons goes in `weapons/` itself. All game C# stays in the single
`BulletHeavenLike` namespace regardless of folder (the server projects use
their own, above).

Move or rename files with the Godot editor's FileSystem dock, which updates
references. From outside the editor (editor closed), `git mv` the file
together with its `.uid` / `.import` sidecars and rewrite its `res://` paths.

## Docs

The index below is imported into every session; the docs it lists are not.
Open only the doc a task needs.

@docs/README.md

## Local assets

`game/local_assets/` and `game/research/` are not committed, so committed scenes must
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
