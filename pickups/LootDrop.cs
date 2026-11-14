using Godot;

namespace BulletHeavenLike;

/// <summary>
/// One line of a loot table: "a Chance of dropping between MinCount and
/// MaxCount of this pickup". Anything that drops loot (breakable props now,
/// enemies or chests later) holds a list of these and rolls each line on
/// its own, so one barrel can drop both bronze and silver coins.
///
/// Usually made inline inside the definition that owns it (a sub-resource
/// in the same .tres), not as a file of its own.
/// </summary>
[GlobalClass]
public partial class LootDrop : Resource
{
    [Export] public PickupDefinition Pickup;

    // 0 = never, 1 = always.
    [Export(PropertyHint.Range, "0,1,0.01")] public float Chance = 1.0f;

    [Export(PropertyHint.Range, "1,50,1")] public int MinCount = 1;
    [Export(PropertyHint.Range, "1,50,1")] public int MaxCount = 1;

    /// <summary>
    /// Rolls this line: how many pickups to drop this time (0 if the chance
    /// failed).
    /// </summary>
    public int Roll(RandomNumberGenerator random)
    {
        if (Pickup == null || random.Randf() >= Chance)
            return 0;
        // Mathf.Max guards against MaxCount having been set below MinCount.
        return random.RandiRange(MinCount, Mathf.Max(MinCount, MaxCount));
    }
}
