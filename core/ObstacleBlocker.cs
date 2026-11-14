using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Makes a solid body block enemies too. Add it as a child of a
/// StaticBody3D (or any CollisionObject3D): when the game starts it hands
/// that body's collision shapes to <see cref="WorldObstacles"/>, and takes
/// them back when it leaves the scene.
///
/// Box shapes become boxes; cylinder, sphere and capsule shapes become
/// circles. Other shapes are skipped with a warning. Shapes are read once,
/// so the body should not move afterwards.
/// </summary>
public partial class ObstacleBlocker : Node
{
    // Whether the shapes also stop attacks, projectiles and enemy contact
    // through them. True for walls; false for something low you can hit
    // or shoot over.
    [Export] public bool BlocksAttacks = true;

    private WorldObstacles _obstacles;
    private readonly List<int> _ids = new();

    public override void _Ready()
    {
        // Deferred so the body and its shapes have their final global
        // positions, and WorldObstacles is surely set up.
        CallDeferred(MethodName.Register);
    }

    private void Register()
    {
        _obstacles = GetTree().GetFirstNodeInGroup(WorldObstacles.Group) as WorldObstacles;
        if (_obstacles == null)
            return; // a scene without enemies, e.g. a test

        foreach (Node child in GetParent().GetChildren())
        {
            if (child is not CollisionShape3D shape || shape.Disabled || shape.Shape == null)
                continue;

            Transform3D transform = shape.GlobalTransform;
            // Scale of the shape along X and Z, from its basis columns.
            float scale = Mathf.Max(transform.Basis.X.Length(), transform.Basis.Z.Length());

            // "switch" on the shape's type: each "case Type name" both
            // checks the type and gives a typed variable.
            switch (shape.Shape)
            {
                case BoxShape3D box:
                    _ids.Add(_obstacles.AddBox(transform, box.Size, BlocksAttacks));
                    break;
                case CylinderShape3D cylinder:
                    _ids.Add(_obstacles.AddCircle(transform.Origin, cylinder.Radius * scale, BlocksAttacks));
                    break;
                case SphereShape3D sphere:
                    _ids.Add(_obstacles.AddCircle(transform.Origin, sphere.Radius * scale, BlocksAttacks));
                    break;
                case CapsuleShape3D capsule:
                    _ids.Add(_obstacles.AddCircle(transform.Origin, capsule.Radius * scale, BlocksAttacks));
                    break;
                default:
                    GD.PushWarning($"{GetParent().Name}/{shape.Name}: enemies can't use a {shape.Shape.GetClass()}, skipped.");
                    break;
            }
        }
    }

    public override void _ExitTree()
    {
        // IsInstanceValid: when the whole scene closes, WorldObstacles may
        // already be gone.
        if (_obstacles == null || !IsInstanceValid(_obstacles))
            return;
        foreach (int id in _ids)
            _obstacles.Remove(id);
        _ids.Clear();
    }
}
