using Godot;

namespace BulletHeavenLike;

/// <summary>
/// How a weapon picks where to attack. Each weapon has its own setting
/// (an [Export] TargetMode field), chosen in the Inspector.
/// </summary>
public enum TargetMode
{
    // Wherever the player faces (Player.Forward).
    Facing,
    // The closest living enemy within the weapon's target range. With
    // nobody in range the weapon falls back to Facing.
    ClosestEnemy,
}

/// <summary>
/// Shared targeting for every weapon: a weapon asks it where to aim, and
/// new target modes are added here once instead of in each weapon.
///
/// A "static class" can't be instanced and holds only static methods,
/// like Godot's Mathf. Nothing here keeps state or allocates, so it's free
/// to call whenever a weapon fires.
///
/// Call it from _PhysicsProcess after EnemyManager has moved its enemies
/// (weapons use a higher ProcessPhysicsPriority for that), so the enemy
/// positions are this step's.
/// </summary>
public static class WeaponTargeting
{
    /// <summary>
    /// Finds the enemy the mode picks within range of the player. Returns
    /// false (and the player's own position) when the mode doesn't target
    /// enemies or none is in range.
    /// </summary>
    public static bool TryFindTarget(TargetMode mode, Player player, EnemyManager enemies,
                                     float range, out Vector3 position)
    {
        Vector3 origin = player.GlobalPosition;
        position = origin;

        // "switch" picks a branch by value; each case ends with "break"
        // (or "return"). Facing has no enemy to find, so it falls to the
        // default.
        switch (mode)
        {
            case TargetMode.ClosestEnemy:
                int enemy = enemies.FindClosest(origin, range);
                if (enemy < 0)
                {
                    return false;
                }
                position = enemies.GetPosition(enemy);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// The direction a weapon should attack in, flat on the ground, length
    /// 1: toward the target the mode picks, or the player's facing when
    /// there is none.
    /// </summary>
    public static Vector3 AimDirection(TargetMode mode, Player player, EnemyManager enemies, float range)
    {
        if (TryFindTarget(mode, player, enemies, range, out Vector3 target))
        {
            Vector3 toTarget = target - player.GlobalPosition;
            toTarget.Y = 0.0f;
            // An enemy right on top of the player gives no direction.
            if (toTarget.LengthSquared() > 0.0001f)
            {
                return toTarget.Normalized();
            }
        }
        return player.Forward;
    }
}
