using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Everything kept between runs: the banked coins and the level of each
/// upgrade bought with them. Saved to a small text file after every
/// change, so nothing is lost if the app is closed.
///
/// It is an autoload (Project Settings > Globals > Autoload, pointing at
/// meta/profile.tscn): Godot creates it once at startup, above the current
/// scene, and keeps it while scenes change. The title menu, the upgrade
/// menu and the run all see this same node. Other scripts reach it
/// through the static <see cref="Instance"/>.
///
/// The save is a ConfigFile, Godot's INI-style format, at
/// user://profile.cfg. "user://" is the game's own writable folder (on
/// Linux ~/.local/share/godot/app_userdata/Bullet Heaven Like/, on Android
/// the app's private storage). It reads like this:
///
///   [profile]
///   version=1
///   coins=42
///
///   [upgrades]
///   magnet_radius=2
/// </summary>
public partial class Profile : Node
{
    /// <summary>The one Profile (the autoload). Null only if the
    /// autoload is missing.</summary>
    public static Profile Instance { get; private set; }

    // Every upgrade that can be bought, in the order the menu lists them.
    // Set in meta/profile.tscn.
    [Export] public UpgradeDefinition[] Upgrades = System.Array.Empty<UpgradeDefinition>();

    [Export] public string SavePath = "user://profile.cfg";

    // Bumped if the file's layout ever changes, so old saves can be read
    // and converted.
    private const int SaveVersion = 1;

    /// <summary>Coins banked from all runs, minus what was spent.</summary>
    public int Coins { get; private set; }

    // Owned level per upgrade Id. Upgrades not in here are at level 0. A
    // Dictionary is fine here: it is read a few times per run, not per frame.
    private readonly Dictionary<string, int> _levels = new();

    public override void _EnterTree()
    {
        Instance = this;
        Load();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    // ---- Coins -------------------------------------------------------

    /// <summary>Adds a run's coins to the bank and saves.</summary>
    public void BankCoins(int amount)
    {
        if (amount <= 0)
            return;
        Coins += amount;
        Save();
    }

    // ---- Upgrades ----------------------------------------------------

    public int GetLevel(UpgradeDefinition upgrade)
    {
        // TryGetValue sets "level" to 0 when the key isn't there, which is
        // exactly the "not bought yet" level.
        _levels.TryGetValue(upgrade.Id, out int level);
        return level;
    }

    /// <summary>Coins for the next level, or -1 if it is maxed.</summary>
    public int NextCost(UpgradeDefinition upgrade)
    {
        int level = GetLevel(upgrade);
        return level < upgrade.MaxLevel ? upgrade.Costs[level] : -1;
    }

    public bool CanBuy(UpgradeDefinition upgrade)
    {
        int cost = NextCost(upgrade);
        return cost >= 0 && Coins >= cost;
    }

    /// <summary>Buys the next level if there are coins for it, and saves.
    /// Returns true if it was bought.</summary>
    public bool TryBuy(UpgradeDefinition upgrade)
    {
        if (!CanBuy(upgrade))
            return false;
        Coins -= NextCost(upgrade);
        _levels[upgrade.Id] = GetLevel(upgrade) + 1;
        Save();
        return true;
    }

    /// <summary>
    /// Starts the profile over: every upgrade back to level 0 and the
    /// banked coins to 0, then saves. A run already in progress keeps its
    /// bonuses, since they were applied when it started.
    /// </summary>
    public void ResetProgress()
    {
        _levels.Clear();
        Coins = 0;
        Save();
    }

    /// <summary>
    /// Gives a fresh run's stats the bonuses of every owned upgrade.
    /// Called from PlayerStats._Ready, at the start of each run.
    /// </summary>
    public void ApplyUpgrades(PlayerStats stats)
    {
        foreach (UpgradeDefinition upgrade in Upgrades)
        {
            int level = GetLevel(upgrade);
            if (level > 0)
                stats.ApplyUpgrade(upgrade.Stat, upgrade.BonusPerLevel * level);
        }
    }

    // ---- Saving --------------------------------------------------------

    private void Load()
    {
        var config = new ConfigFile();
        // No file yet (first launch) is not an error: start from zero.
        if (config.Load(SavePath) != Error.Ok)
            return;

        Coins = config.GetValue("profile", "coins", 0).AsInt32();

        _levels.Clear();
        if (config.HasSection("upgrades"))
        {
            foreach (string id in config.GetSectionKeys("upgrades"))
                _levels[id] = config.GetValue("upgrades", id, 0).AsInt32();
        }
    }

    private void Save()
    {
        var config = new ConfigFile();
        config.SetValue("profile", "version", SaveVersion);
        config.SetValue("profile", "coins", Coins);
        // Every saved level is kept, even of upgrades no longer in the
        // list, so removing one by mistake doesn't wipe it from saves.
        foreach (KeyValuePair<string, int> entry in _levels)
            config.SetValue("upgrades", entry.Key, entry.Value);

        Error error = config.Save(SavePath);
        if (error != Error.Ok)
            GD.PushError($"Profile: could not save {SavePath} ({error}).");
    }
}
