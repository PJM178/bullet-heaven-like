using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The player's progression numbers: experience and level. It lives as a
/// child node of the Player ("Stats") so the level table can be edited in
/// the Inspector, and so later stats (health, damage bonuses, ...) have an
/// obvious home.
///
/// Levelling works off a table, XpPerLevel, where each entry is the XP
/// needed to go from one level to the next:
///
///   XpPerLevel = [1000, 1500, 2200]
///     level 1 -> 2 costs 1000 XP
///     level 2 -> 3 costs 1500 XP
///     level 3 -> 4 costs 2200 XP
///     level 4 is the max level (the table has run out)
///
/// Storing the cost of each level, rather than a running total, keeps the
/// table easy to tune: changing one level's cost doesn't shift the others,
/// and "XP into this level / XP needed" is exactly what an XP bar shows.
/// </summary>
public partial class PlayerStats : Node
{
    // XP needed for each level-up, in order (see the class comment). Add
    // entries here or in the Inspector to add levels. An exported int[]
    // shows up in the Inspector as an editable list.
    [Export] public int[] XpPerLevel = { 1000 };

    // A Godot signal, emitted once for each level gained. The
    // "[Signal] delegate ...EventHandler" pattern is how C# declares one;
    // Godot's source generator then creates a C# event called LeveledUp, so
    // other scripts subscribe with "stats.LeveledUp += OnLeveledUp;". It
    // also appears in the editor's Node > Signals tab. The level-up screen
    // (choosing an upgrade) will hook in here.
    [Signal] public delegate void LeveledUpEventHandler(int newLevel);

    /// <summary>The current level. Everyone starts at level 1.</summary>
    public int Level { get; private set; } = 1;

    /// <summary>All XP ever collected, for score/statistics.</summary>
    public int TotalExperience { get; private set; }

    /// <summary>
    /// XP gathered toward the next level. Resets (keeping any overflow) on
    /// each level-up.
    /// </summary>
    public int ExperienceIntoLevel { get; private set; }

    /// <summary>True once every level in XpPerLevel has been gained.</summary>
    public bool IsMaxLevel => Level - 1 >= XpPerLevel.Length;

    /// <summary>
    /// XP needed to finish the current level, or 0 at max level.
    /// Level 1 uses XpPerLevel[0], level 2 uses XpPerLevel[1], and so on,
    /// because C# arrays count from 0.
    /// </summary>
    public int ExperienceToNextLevel => IsMaxLevel ? 0 : XpPerLevel[Level - 1];

    /// <summary>
    /// Progress through the current level, 0 to 1 (1 at max level). Handy
    /// for an XP bar.
    /// </summary>
    public float LevelProgress =>
        IsMaxLevel ? 1.0f : (float)ExperienceIntoLevel / ExperienceToNextLevel;

    public void AddExperience(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        TotalExperience += amount;
        if (IsMaxLevel)
        {
            // Nothing left to level toward; only the total keeps counting.
            return;
        }

        ExperienceIntoLevel += amount;

        // "while", not "if": one big pickup can be worth several levels.
        // Leftover XP carries over into the next level instead of being lost.
        while (!IsMaxLevel && ExperienceIntoLevel >= ExperienceToNextLevel)
        {
            ExperienceIntoLevel -= ExperienceToNextLevel;
            Level++;
            // EmitSignal needs the signal's name; SignalName.LeveledUp is a
            // generated constant, so a typo is a compile error, not a
            // silent bug.
            EmitSignal(SignalName.LeveledUp, Level);
        }

        if (IsMaxLevel)
        {
            ExperienceIntoLevel = 0;
        }
    }
}
