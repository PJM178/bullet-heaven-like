# Skills

Status: v1, 2026-11-15. Documents the in-run skill system **as built**,
plus the code changes planned for the weapon plan. The game design is in
[design/weapon-plan.md](../design/weapon-plan.md).

## Overview

Every weapon is a skill. Each weapon is a node under the Player; its
`SkillDefinition` names that node. At the start all weapon nodes are
switched off and the starting skills are learned, which switches theirs
on. On each level-up `SkillManager.RollChoices` offers random skills from
the pool, weighted, and `Learn` applies the picked one.

| Skill | Weapon node | Folder | Start |
| --- | --- | --- | --- |
| Sword | `SwordWeapon` | `weapons/sword/` | yes |
| Hammer Smash | `HammerWeapon` | `weapons/hammer/` | no |

## Implementation notes

| Piece | Where | Role |
| --- | --- | --- |
| `SkillDefinition` | `skills/SkillDefinition.cs`; `<name>_skill.tres` in each weapon folder | `DisplayName`, `Description`, `Icon`, `WeaponNodeName`, `Weight`, `MaxLevel`. |
| `SkillManager` | `skills/SkillManager.cs`, node `Skills` under the Player | `Pool`, `StartingSkills`, `FillWithRepeats`; `GetLevel`, `IsLearned`, `Learn`, `RollChoices`; signal `SkillLearned`. |

`RollChoices` picks from skills below their max level with `Weight` > 0,
no repeats while others are left; with `FillWithRepeats` (on while the pool
is small) the remaining cards repeat.

## Planned changes

Decided in [design/weapon-plan.md](../design/weapon-plan.md), not built:
fusions and class skills are mostly new data on `SkillDefinition` plus one
filter in `RollChoices`.

| Piece | Today | Change |
| --- | --- | --- |
| `SkillDefinition` | Name, description, icon, weapon node, weight, max level | Add `Requires` (skills that must be learned), `Replaces` (the base skill it upgrades), later `Classes` (who may be offered it). |
| `SkillManager.RollChoices` | Offers any skill below max level | Also skip skills whose requirements are not met, or that belong to another class. |
| `SkillManager.Learn` | Switches the skill's weapon node on | For a fusion, also switch the replaced base weapon off. |
| Weapon folders | `weapons/sword/`, `weapons/hammer/`, `weapons/projectiles/` | New `weapons/kunai/`, `weapons/fireball/`, `weapons/exploding_kunai/`, each with its `_skill.tres`. |

Each fusion stays its own weapon node and scene, so Exploding Kunai can
reuse Kunai and Fireball code without either of them knowing about fusions.

## Changelog

| Date | Version | Change |
| --- | --- | --- |
| 2026-11-15 | v1 | First systems doc; implementation notes moved here from design/weapon-plan.md. |
