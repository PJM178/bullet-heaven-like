using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The player's numbers: health, experience and level. It lives as a
/// child node of the Player ("Stats") so the values can be edited in the
/// Inspector, and so later stats (damage bonuses, ...) have an obvious
/// home.
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

    // ---- Health ----------------------------------------------------------
    // Health is counted in whole hearts. Any hit costs one heart, and after
    // a hit the player is invulnerable for a moment, so a crowd of enemies
    // can't take every heart in the same instant.

    // Hearts at the start, and the most the player can have.
    [Export] public int MaxHealth = 3;

    // Seconds of invulnerability after losing a heart.
    [Export] public float InvulnerabilityTime = 1.5f;

    // Emitted whenever Health changes, so the hearts on screen only update
    // when something actually happened instead of checking every frame.
    [Signal] public delegate void HealthChangedEventHandler(int health, int maxHealth);

    // Emitted once, when Health reaches 0. The game-over screen listens.
    [Signal] public delegate void DiedEventHandler();

    /// <summary>Current hearts, from 0 to MaxHealth.</summary>
    public int Health { get; private set; }

    /// <summary>True once Health has reached 0.</summary>
    public bool IsDead => Health <= 0;

    /// <summary>
    /// True for a short while after a hit, when further hits are ignored.
    /// The Player blinks while this is true.
    /// </summary>
    public bool IsInvulnerable => _invulnerableTimeLeft > 0.0f;

    private float _invulnerableTimeLeft;

    public override void _Ready()
    {
        Health = MaxHealth;
    }

    public override void _PhysicsProcess(double delta)
    {
        // Count the invulnerability down. Physics process, because the
        // damage that it blocks is dealt in the physics step too.
        if (_invulnerableTimeLeft > 0.0f)
        {
            _invulnerableTimeLeft -= (float)delta;
        }
    }

    /// <summary>
    /// Takes away hearts (usually one), unless the player is still
    /// invulnerable from the last hit. Returns true if the hit landed.
    /// </summary>
    public bool TakeDamage(int hearts = 1)
    {
        // "hearts = 1" is a default value: TakeDamage() means one heart.
        if (hearts <= 0 || IsDead || IsInvulnerable)
        {
            return false;
        }

        // Mathf.Max keeps it from going below 0.
        Health = Mathf.Max(Health - hearts, 0);
        _invulnerableTimeLeft = InvulnerabilityTime;
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);

        if (IsDead)
        {
            EmitSignal(SignalName.Died);
        }
        return true;
    }

    /// <summary>
    /// Takes every remaining heart at once, ignoring invulnerability. For
    /// the debug Die button, and anything later that should kill outright.
    /// </summary>
    public void Kill()
    {
        if (IsDead)
        {
            return;
        }
        Health = 0;
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
        EmitSignal(SignalName.Died);
    }

    /// <summary>Restores hearts, up to MaxHealth. For healing pickups later.</summary>
    public void Heal(int hearts = 1)
    {
        if (hearts <= 0 || IsDead)
        {
            return;
        }

        Health = Mathf.Min(Health + hearts, MaxHealth);
        EmitSignal(SignalName.HealthChanged, Health, MaxHealth);
    }

    // ---- Experience ------------------------------------------------------

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
