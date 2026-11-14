# Breakable props

Status: v1, 2026-11-14. Documents breakable props **as built**. Why it is
shaped this way: [design/breakables.md](../design/breakables.md).

## Overview

A breakable prop stands in the world, blocks the player, and breaks when
its health runs out. On every hit that doesn't break it, it flashes white
and squashes briefly. When it breaks, it is cut into pieces that fly out,
bounce once and shrink away (about 1.2 s), a dust puff plays, and its loot
drops around it.

Weapons hit props only when the attack catches them; auto-aim ignores them.

| Weapon | How it hits props |
| --- | --- |
| Sword | Props in the swing's wedge |
| Hammer | Props in the smash circle, then once more when the shockwave passes |
| Projectiles | First prop touched; the projectile stops there |

Weapons in `research/` don't hit props.

## Props

| Prop | Health | Size | Pieces | Loot |
| --- | --- | --- | --- | --- |
| Barrel | 1 | r 0.33 m, 0.8 m tall | 8 staves + lid + bottom | 2-4 bronze; 30% a silver; 5% a gold |

The barrel model and the dust sprite come from the git-ignored
`local_assets/` (models in `local_assets/props/<category>/`, e.g.
`props/barrels/barrel.glb`); without the model a brown 8-sided cylinder stands in.
One barrel is placed in `main.tscn` for testing.

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `BreakableDefinition` | `props/breakables/BreakableDefinition.cs` | Resource: health, radius, height, `ModelPath`, `Slices`, `DustFramesPath`, `Loot`. |
| Barrel | `props/breakables/barrel.tres` | The barrel's definition. |
| `BreakableManager` | `props/breakables/BreakableManager.cs`, node in `main.tscn` | `Add`; hits (`DamageCircle`, `DamageArc`, `DamageRing`, `HitTest`); flash, squash, break, pooled pieces and dust. |
| `PlacedBreakable` | `props/breakables/PlacedBreakable.cs` | Editor marker; hands its transform to the manager on start. |
| `MeshShatter` | `props/breakables/MeshShatter.cs` | Cuts a model into slices + top + bottom; run once per kind, on its first break. |
| `LootDrop` | `pickups/LootDrop.cs` | One loot line: pickup, chance, count range. |

| Value | Default | On |
| --- | --- | --- |
| `MaxBreakables` / `MaxPieces` / `MaxDust` | 64 / 64 / 8 | `BreakableManager` |
| Piece speed out / up, gravity | 3 / 4 m/s, 18 m/s² | `BreakableManager` |
| `PieceLife` / `PieceShrinkTime` | 1.2 / 0.3 s | `BreakableManager` |
| `LootScatter` | 0.6 m | `BreakableManager` |

## Open questions

- [ ] Enemies walk through props.
- [ ] No break sound yet.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-14 | v1 | Breakable props: definitions, manager, mesh shatter, loot lines; the barrel. |
