using Godot;

namespace BulletHeavenLike;

/// <summary>
/// What an attack's simulation code needs from the enemies: find the ones
/// in an area, tell them apart, and hurt them. Implemented by
/// <see cref="EnemyManager"/>, so a hit both changes the enemy's health
/// (in its core) and shows the flash or corpse right away (in its shell).
/// See <see cref="IObstacleQuery"/> for why cores take interfaces.
/// </summary>
public interface IEnemyTargets
{
    /// <summary>Live enemies overlapping the circle, not behind a wall;
    /// writes their indices into results and returns how many.</summary>
    int QueryCircle(Vector3 point, float radius, int[] results);

    /// <summary>The lifelong id of enemy i (indices change between steps).</summary>
    int GetId(int i);

    /// <summary>Damages enemy i (an index from a query this step).</summary>
    void Damage(int i, float amount);
}
