using Godot;

// File-scoped namespace (C# 10+): everything in this file belongs to
// BulletHeavenLike without needing an extra pair of braces and indentation.
namespace BulletHeavenLike;

/// <summary>
/// The player-controlled block. Moves along the ground in the screen
/// direction of the move_left / move_right / move_up / move_down actions
/// (the arrow keys).
/// </summary>
// "partial" is required by Godot: its source generator writes the other half
// of this class (the glue that connects C# to the engine).
public partial class Player : CharacterBody3D
{
    // [Export] makes this field show up in the Inspector so it can be tuned
    // in the editor without touching code. Units are metres per second.
    [Export] public float Speed = 5.0f;

    // The camera viewing the scene. We need it so "left" means left on the
    // *screen*, not along the world X axis. The camera is rotated 45°,
    // so those two directions are different.
    private Camera3D _camera;

    // _Ready runs once, when the node and its children have entered the scene.
    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();
    }

    // _PhysicsProcess runs at a fixed rate (60 times per second by default),
    // which is the right place for movement and collisions.
    public override void _PhysicsProcess(double delta)
    {
        // GetVector combines four actions into one 2D vector:
        //   x: -1 (left) .. +1 (right),  y: -1 (up) .. +1 (down)
        // It also normalizes diagonals, so holding Up + Right isn't ~41%
        // faster than holding just one key. (Vector2 and Vector3 are structs,
        // value types, so creating them every frame doesn't allocate memory.)
        Vector2 input = Input.GetVector("move_left", "move_right", "move_up", "move_down");

        // Turn the 2D screen input into a 3D direction on the ground.
        // Screen "down" (+y) is the opposite of screen "up", hence the minus.
        Vector3 direction = ScreenRightOnGround() * input.X - ScreenUpOnGround() * input.Y;

        // Velocity is a property of CharacterBody3D. MoveAndSlide() moves the
        // body by Velocity * delta itself, so we don't multiply by delta here.
        Velocity = direction * Speed;
        MoveAndSlide();
    }

    /// <summary>
    /// The direction that looks like "right" on screen, flattened onto the
    /// ground so the block slides along the floor instead of into it.
    /// </summary>
    private Vector3 ScreenRightOnGround()
    {
        // A node's Basis holds its local axes. Basis.X is the camera's
        // "right" direction.
        return FlattenToGround(_camera.GlobalBasis.X);
    }

    /// <summary>
    /// The direction that looks like "up" on screen, i.e. away from the
    /// viewer along the ground.
    /// </summary>
    private Vector3 ScreenUpOnGround()
    {
        // In Godot a camera looks along its *negative* Z axis, so -Basis.Z
        // is "forward". It points down into the ground at an angle; once
        // flattened it becomes "up the screen".
        return FlattenToGround(-_camera.GlobalBasis.Z);
    }

    /// <summary>
    /// Drops the vertical part of a direction and rescales it to length 1,
    /// so speed stays the same however the camera is tilted.
    /// </summary>
    private static Vector3 FlattenToGround(Vector3 direction)
    {
        // "direction" is a copy (structs are passed by value), so changing
        // its Y here doesn't affect the caller's vector.
        direction.Y = 0.0f;
        return direction.Normalized();
    }
}
