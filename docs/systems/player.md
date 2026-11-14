# Player Character

Status: v1.3, 2026-11-14. Documents the player **as built** in the prototype.
Nothing here is a proposal unless marked; planned changes belong in Open
questions until they are decided.

## Overview

One character, controlled with two thumbs: the left stick moves, the right
stick aims. Weapons fire on their own in the direction the character faces.
The player has three hearts, gains XP from orbs dropped by kills, and picks a
skill card on each level-up. Losing the last heart plays a slow death shot
and ends the run.

Weapons and skills are covered in [weapon-plan.md](../design/weapon-plan.md); this doc
stops at "the player has a facing direction that weapons read".

## Controls

| Input | Move | Aim |
| --- | --- | --- |
| Phone | Left half of the screen: floating stick | Right half of the screen: floating stick |
| Desktop | WASD or arrow keys | Mouse cursor (the point on the ground under it) |

- **Floating sticks.** A stick appears wherever the finger lands in its half
  of the screen. Buttons win: a touch on a button never starts a stick.
- **Analog movement.** Stick tilt sets the speed, from 0 at the dead zone
  (15% of the stick radius) to full speed at 90%. Keys always give full speed.
- **Screen-relative.** "Up" on the stick is up the screen, not along a world
  axis (the camera is turned 45°).
- **Pause**: Esc or Tab (`pause` action). There is no pause control on the
  phone yet.

### Facing

Facing decides which way the sprite is drawn and where weapons fire. It is
chosen in this order:

1. The aim stick, if tilted.
2. On desktop, the mouse cursor, once the mouse has moved. A cursor within
   0.2 m of the character is ignored so it does not spin.
3. Otherwise the direction of movement. Standing still keeps the last facing.

The character turns toward the chosen direction smoothly (`TurnSpeed`), and
weapons follow where it visibly points, not where the input points.

Weapons describe directions as an angle around the character, clockwise seen
from above: 0° front, 90° right, 180° back, 270° left
(`Player.Forward`, `Right`, `DirectionAt(degrees)`).

## Movement

- Walks on a flat ground plane at 5 m/s. No dash, jump, acceleration or
  knockback: velocity is input direction × speed, every physics step.
- A `CharacterBody3D` with a capsule (radius 0.3 m, height 1.8 m), moved with
  `MoveAndSlide`.
- Enemies do not physically block the player. They stop 0.7 m away and
  crowd around; contact is a distance check (see Health).

## Health

- **Hearts.** 3 at the start, which is also the maximum. Every hit costs one
  whole heart.
- **Contact damage.** Any enemy within 0.9 m costs a heart
  (`EnemyManager.ContactRange`, `ContactDamage`).
- **Invulnerability.** 1.5 s after a hit, further hits are ignored and the
  character blinks (10 times a second), so a crowd cannot take every heart
  at once.
- **Flinch.** A hit shows the hurt pose for 0.3 s; movement is not interrupted.
- **Healing.** `PlayerStats.Heal` exists but nothing calls it yet: hearts
  cannot be regained in the prototype.
- **HUD.** A row of hearts along the top of the screen; lost hearts stay as
  dark, empty outlines. The minimap is in the top-right corner
  ([minimap.md](minimap.md)).

## Experience and levels

- Each kill drops one orb worth 100 XP. Orbs hover where they fall; within
  4 m of the player they fly in and are absorbed.
- Levels come from a table of costs, one entry per level-up:

| Level-up | XP cost | Kills (at 100 XP) |
| --- | --- | --- |
| 1 → 2 | 1000 | 10 |
| 2 → 3 | 1500 | 15 |
| 3 → 4 | 2200 | 22 |
| 4 → 5 | 3000 | 30 |

- **Max level 5**, reached after 7700 XP (77 kills): four skill picks per run.
  Leftover XP carries into the next level; at max level only the total keeps
  counting.
- **Level-up.** The game pauses and offers 3 random skill cards (see the
  weapon plan). Several level-ups at once are queued one after another. If
  nothing is left to learn, the level passes without the screen.
- **No XP bar yet.** Progress is only visible in the debug HUD.

## Death

Reaching 0 hearts ends the run with a sequence instead of a menu:

| Time | What happens |
| --- | --- |
| 0.0 s | Slow motion (20% speed). The character stops taking input, plays the hurt frames, then stays in the fallen pose. The camera swoops in close and starts circling the body. |
| 0.8 s | The picture starts fading out through a chunky dither. |
| 1.0 s | The game freezes; the camera keeps circling. |
| 2.2 s | The title ("OVERRUN") dithers in as a rippling sign standing over the body. |
| 4.0 s | Run summary: level reached and enemies slain. |
| 4.8 s | Restart button. Last on purpose, so a finger still on the stick cannot skip the moment. |

There is no revive or continue: Restart begins a new run from level 1.

## Look and animation

- Drawn as a **billboard sprite** with 8 facing directions
  (`DirectionalSprite3D`, from `addons/sprite_kit/`), not a 3D model.
- Four animations: `idle`, `walk`, `hurt`, `die`. The sheets run at about
  10–13 frames a second, which gives the stepped, choppy playback the art
  style asks for without extra work.
- The walk cycle slows with the stick (never below 35% pace) so the feet do
  not slide at low speed. Idle holds a single frame.
- The sprite sheet is a local asset
  (`local_assets/characters/players/player_m.spritesheet`), loaded by path. On a fresh
  clone without it the character is invisible but the game still runs.

## Camera

- Fixed high angle following the player: 30° down, turned 45°, 32 m away
  with a narrow 25° field of view (a flat, almost isometric picture).
- Snaps to the player with no smoothing (`FollowSpeed = 0`); the framing is
  whatever the camera has in the editor when the scene starts.
- **Shake.** Heavy hits (the Hammer) jolt the view in choppy steps, 30 a
  second, mostly up and down.
- **Death shot.** Zooms to 22.5% of the normal distance (7.2 m) over 3 s, drops lower
  and circles the body at 22° per second, running on real time through the
  slow motion and the pause. As the camera circles, every 8-direction
  sprite (player, enemies, corpses) turns to show the side the camera now
  sees, also while the game is paused (`DirectionalSprite3D.RefreshFacing`).

## Open questions

- [ ] Sprite or 3D model? The art style describes low-poly 3D characters on
      a shared rig, but the player is a sprite today. `player/player.glb`
      and `player_material.tres` are left over from the earlier model and
      unused.
- [ ] Can hearts be regained (pickups, level-up reward), and can the maximum
      grow?
- [ ] Should the player have stats that skills change (speed, pickup
      radius, damage)? `PlayerStats` is the intended home.
- [ ] Where does the XP bar go on the HUD?
- [ ] How does the player pause on the phone (HUD button, back button)?
- [ ] Is level 5 the real cap, or only a prototype number? What happens to
      XP after the cap?
- [ ] Any active move (dash, dodge), or is movement the only defence?
- [ ] Do classes (see the weapon plan) change the character's look, stats
      or starting skill?
- [ ] Aim on the phone: keep the manual right stick, or auto-aim with the
      stick as an override? (Weapons already have a closest-enemy target mode.)

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `Player` | `player/Player.cs`, node `Player` in `main.tscn` | Movement, facing, animation choice, hurt blink, death frames. |
| `PlayerStats` | `player/PlayerStats.cs`, child node `Stats` | Hearts, invulnerability, XP and level, coins, pickup radii; `Collect` applies pickups. Signals: `HealthChanged`, `Died`, `LeveledUp`, `CoinsChanged`. |
| `SkillManager` | `skills/`, child node `Skills` | Learned skills and level-up choices; starts with the Sword. |
| `CameraFollow` | `player/CameraFollow.cs` | Follow, shake, death shot. |
| `VirtualJoystick` | `ui/`, `MoveStick` and `AimStick` in `game.tscn` | Presses the `move_*` / `aim_*` actions with analog strength. |
| `PlayerHud`, `HeartIcon` | `ui/` | Heart row, updated only on `HealthChanged`. |
| `LevelUpScreen`, `GameOverScreen` | `ui/` | Listen to `LeveledUp` and `Died`. |
| `XpManager` | `pickups/` | Orbs; reads `Stats.MagnetRadius`, calls `Stats.AddExperience`. |
| `PickupManager` | `pickups/` | Touch pickups (coins); see [pickups.md](pickups.md). |
| `FacingDebug` | `player/FacingDebug.cs` | Debug overlay drawing the facing frame on the ground. |
| `LocalExtras` | child node of `Player` | Loads optional local-only extras if present. |

Everything else reacts to the player through the three `PlayerStats` signals,
so new systems (healing pickups, an XP bar) can hook in without touching
`Player`.

Tunable values and their defaults:

| Value | Default | On |
| --- | --- | --- |
| `Speed` | 5 m/s | `Player` |
| `TurnSpeed` | 14 | `Player` |
| `HurtPoseTime` | 0.3 s | `Player` |
| `HurtBlinkRate` | 10 /s | `Player` |
| `MinLegPace` | 0.35 | `Player` |
| `MaxHealth` | 3 | `PlayerStats` |
| `InvulnerabilityTime` | 1.5 s | `PlayerStats` |
| `XpPerLevel` | 1000, 1500, 2200, 3000 | `PlayerStats` |
| `ContactRange` / `ContactDamage` | 0.9 m / 1 | `EnemyManager` |
| `XpPerKill` | 100 | `EnemyManager` |
| `MagnetRadius` | 4 m | `PlayerStats` |
| `PickupRadius` | 0.6 m | `PlayerStats` |

Debug: the stress-test HUD has **Die** and **Level up** buttons.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | First player doc: controls, movement, hearts, XP and levels, death, look, camera as built. |
| 2026-11-14 | v1.1 | Camera 20% closer: 40 m to 32 m. Death shot keeps its 7.2 m end distance (DeathZoom 0.18 to 0.225). |
| 2026-11-14 | v1.2 | Death shot: sprites show the side the circling camera sees, also while paused. |
| 2026-11-14 | v1.3 | `MagnetRadius` moved from `XpManager` to `PlayerStats`; added `PickupRadius`, `Coins`, `Collect`. |
