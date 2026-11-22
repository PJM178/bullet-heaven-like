using Godot;

namespace BulletHeavenLike;

/// <summary>
/// What simulation code needs to know about walls and props, without
/// knowing which node answers it.
///
/// An interface is a list of methods a class promises to have, with no code
/// of its own. <see cref="WorldObstacles"/> (a node) implements it, and a
/// simulation core such as <see cref="EnemySimulation"/> only ever sees this
/// interface. That keeps the core free of nodes: it is handed "something
/// that can answer these questions" from outside ("dependency inversion").
/// </summary>
public interface IObstacleQuery
{
    /// <summary>Moves a circle of the given radius out of any obstacle it
    /// overlaps (on the ground) and returns the corrected position.</summary>
    Vector3 PushOut(Vector3 position, float radius);

    /// <summary>Whether an obstacle that blocks attacks lies on the straight
    /// line between the two points.</summary>
    bool IsLineBlocked(Vector3 from, Vector3 to);
}
