using Godot;

namespace BulletHeavenLike;

/// <summary>
/// What an attack's simulation code needs from breakable props. Implemented
/// by <see cref="BreakableManager"/>. See <see cref="IObstacleQuery"/> for
/// why cores take interfaces.
/// </summary>
public interface IBreakableTargets
{
    /// <summary>Damages a prop overlapping the circle, if there is one.
    /// Returns true if something was hit.</summary>
    bool HitTest(Vector3 point, float radius, float damage);
}
