using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Makes the camera follow a target (the player) while keeping the same
/// angle and distance it had when the scene started. Place the camera in the
/// editor where it looks right relative to the player; this script keeps
/// that framing as the player moves.
/// </summary>
public partial class CameraFollow : Camera3D
{
    // The node to follow. Exporting a node type (Node3D) lets you pick it
    // in the Inspector, and Godot fills in the reference for us.
    [Export] public Node3D Target;

    // How quickly the camera catches up. Higher is tighter; 0 snaps
    // straight to the target with no smoothing at all.
    [Export] public float FollowSpeed = 0.0f;

    // Camera position relative to the target, measured once at the start.
    private Vector3 _offset;

    public override void _Ready()
    {
        if (Target == null)
        {
            // GD.PushWarning shows up in the editor's Debugger panel.
            GD.PushWarning("CameraFollow has no Target set; the camera won't move.");
            return;
        }

        _offset = GlobalPosition - Target.GlobalPosition;
    }

    // The player moves in _PhysicsProcess, so the camera follows in the same
    // step. If the two updated at different rates, the player would seem to
    // jitter slightly against the background.
    public override void _PhysicsProcess(double delta)
    {
        if (Target == null)
        {
            return;
        }

        Vector3 desired = Target.GlobalPosition + _offset;

        if (FollowSpeed <= 0.0f)
        {
            GlobalPosition = desired;
            return;
        }

        // Move part of the way toward the desired spot each step. Using
        // 1 - Exp(-speed * delta) makes the smoothing feel the same at any
        // frame rate. Only the position changes, never the rotation, so the
        // view angle stays fixed.
        float weight = 1.0f - Mathf.Exp(-FollowSpeed * (float)delta);
        GlobalPosition = GlobalPosition.Lerp(desired, weight);
    }
}
