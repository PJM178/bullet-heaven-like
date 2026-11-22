using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Which player stat an upgrade raises. What each one actually does is in
/// <see cref="PlayerStats.ApplyUpgrade"/>, the one switch over this enum.
/// </summary>
public enum UpgradeStat
{
    MagnetRadius,
}

/// <summary>
/// One permanent upgrade bought with coins between runs (the meta
/// progression, docs/design/meta-progression.md). Saved as its own .tres
/// in meta/upgrades/, like PickupDefinition: a new upgrade of an existing
/// stat is a new file, not new code.
///
/// Upgrades are tiered: each level adds BonusPerLevel to the stat and
/// costs the next entry in Costs, so the length of Costs is the max level.
///
///   BonusPerLevel = 0.75, Costs = [10, 25, 50]
///     level 1 costs 10 coins and gives +0.75
///     level 2 costs 25 more and gives +1.5 in total
///     level 3 (max) costs 50 more and gives +2.25 in total
///
/// Only data lives here. The levels the player owns are in
/// <see cref="Profile"/>, which saves them.
/// </summary>
[GlobalClass]
public partial class UpgradeDefinition : Resource
{
    // The key in the save file. Never change it once players have saves,
    // or their bought levels are lost; the display name can change freely.
    [Export] public string Id = "";

    [Export] public string DisplayName = "";
    [Export(PropertyHint.MultilineText)] public string Description = "";

    [Export] public UpgradeStat Stat;

    // Added to the stat for each level owned.
    [Export] public float BonusPerLevel = 1.0f;

    // Shown after the bonus in the menu ("+1.5 m"). Optional.
    [Export] public string Unit = "";

    // Coins for each level, in order (see the class comment).
    [Export] public int[] Costs = System.Array.Empty<int>();

    /// <summary>The highest level that can be bought.</summary>
    public int MaxLevel => Costs.Length;
}
