using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns the player's skills: which ones they have (and at what level), and
/// rolling the random choices a level-up offers.
///
/// Every skill in Pool has a weapon node under the Player (named by
/// SkillDefinition.WeaponNodeName). At the start those weapons are all
/// switched off, then StartingSkills are learned, which switches theirs on.
///
/// Put this node under the Player (as "Skills").
/// </summary>
public partial class SkillManager : Node
{
    // Every skill that exists in the game: what level-ups pick from.
    [Export] public SkillDefinition[] Pool = { };

    // Learned before the game starts (the sword).
    [Export] public SkillDefinition[] StartingSkills = { };

    // When fewer skills can still be learned than there are cards, fill the
    // remaining cards with repeats rather than leaving them empty. With a
    // small pool (only the hammer to learn, for now) every card shows the
    // hammer. Turn off once the pool is bigger.
    [Export] public bool FillWithRepeats = true;

    // Emitted after a skill is learned or levelled up.
    [Signal] public delegate void SkillLearnedEventHandler(SkillDefinition skill, int level);

    // Each learned skill's level. Dictionary is C#'s key -> value table;
    // a skill not in it is not learned (level 0).
    private readonly Dictionary<SkillDefinition, int> _levels = new();

    // Godot's random number generator. Its own instance, rather than the
    // global GD.Randf(), so it could later be seeded for repeatable runs.
    private readonly RandomNumberGenerator _random = new();

    // Reused by RollChoices so rolling doesn't make new lists each time.
    private readonly List<SkillDefinition> _candidates = new();

    public override void _Ready()
    {
        _random.Randomize();

        foreach (SkillDefinition skill in Pool)
        {
            SetWeaponActive(skill, false);
        }
        foreach (SkillDefinition skill in StartingSkills)
        {
            Learn(skill);
        }
    }

    public int GetLevel(SkillDefinition skill)
    {
        // TryGetValue: look up a key without throwing if it's missing.
        return skill != null && _levels.TryGetValue(skill, out int level) ? level : 0;
    }

    public bool IsLearned(SkillDefinition skill) => GetLevel(skill) > 0;

    /// <summary>
    /// Learns a skill, or raises its level if already learned. Learning it
    /// for the first time switches its weapon on.
    /// </summary>
    public void Learn(SkillDefinition skill)
    {
        if (skill == null)
        {
            return;
        }
        int level = Mathf.Min(GetLevel(skill) + 1, skill.MaxLevel);
        _levels[skill] = level;
        if (level == 1)
        {
            SetWeaponActive(skill, true);
        }
        EmitSignal(SignalName.SkillLearned, skill, level);
    }

    /// <summary>
    /// Picks up to results.Length random skills to offer, weighted by
    /// SkillDefinition.Weight, from those not yet at their max level. No
    /// skill is picked twice while others are left; after that, empty
    /// slots are filled with repeats if FillWithRepeats is on. Returns how
    /// many slots were filled (0 if nothing can be learned).
    /// </summary>
    public int RollChoices(SkillDefinition[] results)
    {
        _candidates.Clear();
        foreach (SkillDefinition skill in Pool)
        {
            if (skill != null && skill.Weight > 0.0f && GetLevel(skill) < skill.MaxLevel)
            {
                _candidates.Add(skill);
            }
        }
        if (_candidates.Count == 0)
        {
            return 0;
        }

        // First pass: different skills, each removed once picked.
        int count = 0;
        while (count < results.Length && _candidates.Count > 0)
        {
            int index = PickWeighted(_candidates);
            results[count++] = _candidates[index];
            _candidates.RemoveAt(index);
        }

        if (!FillWithRepeats)
        {
            return count;
        }

        // Second pass: out of different skills, so fill the rest from the
        // ones already picked (the candidates removed above).
        int unique = count;
        while (count < results.Length)
        {
            results[count++] = results[_random.RandiRange(0, unique - 1)];
        }
        return count;
    }

    /// <summary>
    /// A weighted random pick: imagine each skill as a stretch of a line,
    /// as long as its weight. Throw a dart at a random point along the
    /// whole line and see whose stretch it lands in.
    /// </summary>
    private int PickWeighted(List<SkillDefinition> skills)
    {
        float total = 0.0f;
        foreach (SkillDefinition skill in skills)
        {
            total += skill.Weight;
        }

        float dart = _random.RandfRange(0.0f, total);
        for (int i = 0; i < skills.Count; i++)
        {
            dart -= skills[i].Weight;
            if (dart < 0.0f)
            {
                return i;
            }
        }
        // Rounding can leave the dart a hair past the end.
        return skills.Count - 1;
    }

    /// <summary>
    /// Switches a skill's weapon on or off. A disabled node gets no
    /// _Process / _PhysicsProcess calls, so it never fires; hiding it hides
    /// any effect it was showing.
    /// </summary>
    private void SetWeaponActive(SkillDefinition skill, bool active)
    {
        if (skill == null || string.IsNullOrEmpty(skill.WeaponNodeName))
        {
            return;
        }
        // The weapons are siblings of this node, under the Player.
        Node weapon = GetParent().GetNodeOrNull(skill.WeaponNodeName);
        if (weapon == null)
        {
            GD.PushWarning($"SkillManager: no weapon node named {skill.WeaponNodeName}");
            return;
        }

        weapon.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        // "is Node3D node3D" checks the type and gives a typed variable in
        // one step (pattern matching).
        if (weapon is Node3D node3D)
        {
            node3D.Visible = active;
        }
    }
}
