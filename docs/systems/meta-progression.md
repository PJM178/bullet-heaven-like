# Meta Progression

Status: v1, 2026-11-15. Documents what is kept between runs **as built**.
Why it is shaped this way: [design/meta-progression.md](../design/meta-progression.md).

## Overview

The game starts on the **title menu**: the game's name, the banked coins,
**Start** and **Upgrades**. The upgrade menu lists every upgrade with its
level, current bonus and a **Buy** button showing the next level's price;
**Back** returns to the title.

During a run coins are collected as usual (see [pickups.md](pickups.md)).
When the player dies, every coin collected that run is banked, and the
death summary shows "+N coins". The game-over screen then offers **Rise
again** (a new run) or **Menu**. Upgrade bonuses are applied at the start
of each run.

Coins collected in a run that ends without dying (the app closed) are lost.

## Upgrades

| Upgrade | Stat | Per level | Levels | Costs |
| --- | --- | --- | --- | --- |
| Magnet | `MagnetRadius` (base 4 m) | +0.75 m | 5 | 10, 25, 50, 100, 200 |

Costs and bonuses are placeholders for testing.

## Save file

`user://profile.cfg` (ConfigFile), written after every purchase and every
banking. On desktop Linux: `~/.local/share/godot/app_userdata/Bullet Heaven Like/`.
Delete it to start over.

```ini
[profile]
version=1
coins=42

[upgrades]
magnet_radius=2
```

Levels are stored by upgrade `Id`; saved levels of upgrades no longer in
the list are kept.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `Profile` | `meta/Profile.cs`, autoload `meta/profile.tscn` | Coins, upgrade levels, save/load; `BankCoins`, `GetLevel`, `NextCost`, `CanBuy`, `TryBuy`, `ApplyUpgrades`. `Profile.Instance` from code. Lists the upgrades (`Upgrades`). |
| `UpgradeDefinition` | `meta/UpgradeDefinition.cs` | Resource: `Id`, `DisplayName`, `Description`, `Stat`, `BonusPerLevel`, `Unit`, `Costs` (length = max level). |
| Upgrades | `meta/upgrades/*.tres` | One per upgrade; add it to `Profile.Upgrades` in `profile.tscn` to show it. |
| `PlayerStats.ApplyUpgrade` | `player/PlayerStats.cs` | The switch over `UpgradeStat`; called by `Profile.ApplyUpgrades` from `PlayerStats._Ready`. |
| `TitleMenu` | `ui/TitleMenu.cs`, `ui/title_menu.tscn` | Main scene. |
| `UpgradeMenu` | `meta/UpgradeMenu.cs`, `meta/upgrade_menu.tscn` | Builds its rows from `Profile.Upgrades`; rebuilds after a purchase. |
| `GameOverScreen` | `ui/GameOverScreen.cs` | Banks `Stats.Coins` on death; Rise again / Menu. |

Scene changes use paths (`ChangeSceneToFile`), not `PackedScene` exports,
because the title menu and `game.tscn` reference each other and scenes that
load each other in a loop can't be loaded.

A new upgrade of an existing stat is a new `.tres`. A new stat is a new
`UpgradeStat` value and a case in `PlayerStats.ApplyUpgrade`.

The upgrade menu's **Reset progress** button (tap twice: the first tap
only arms it) calls `Profile.ResetProgress()`, which sets every upgrade
back to level 0 and the banked coins to 0, and saves.

## Open questions

- [ ] Coin counter in the run's HUD.
- [ ] Bank coins when a run is left without dying?

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-15 | v1 | Title menu, upgrade menu, Profile save, Magnet upgrade, coins banked on death. |
| 2026-11-15 | v1.1 | Reset progress button in the upgrade menu (`Profile.ResetProgress`: upgrades and coins to 0). |
