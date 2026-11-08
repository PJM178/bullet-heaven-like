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

    // How many times per second a shake picks a new offset. Low and
    // choppy reads as a jolt (and suits the PS1 look) rather than a blur.
    [Export] public float ShakeRate = 30.0f;

    // Camera position relative to the target, measured once at the start.
    private Vector3 _offset;

    // The current shake: how strong it started, how long it lasts, how
    // much is left, and time until the next new offset.
    private float _shakeStrength;
    private float _shakeDuration;
    private float _shakeLeft;
    private float _shakeStepLeft;

    /// <summary>
    /// Shakes the view for a moment, e.g. on a heavy hit. Strength is the
    /// largest offset in metres; it dies away over duration seconds. A
    /// weaker shake doesn't cut short a stronger one already playing.
    /// </summary>
    public void Shake(float strength, float duration)
    {
        float current = _shakeLeft > 0.0f ? _shakeStrength * (_shakeLeft / _shakeDuration) : 0.0f;
        if (strength < current || duration <= 0.0f)
        {
            return;
        }
        _shakeStrength = strength;
        _shakeDuration = duration;
        _shakeLeft = duration;
        _shakeStepLeft = 0.0f;
    }

    // The shake moves the view with HOffset / VOffset, which slide the
    // picture sideways and up/down without moving the camera node. So it
    // never fights with the following in _PhysicsProcess.
    public override void _Process(double delta)
    {
        if (_shakeLeft <= 0.0f)
        {
            return;
        }

        // Hit-stop (see HammerWeapon) slows the game clock right as a shake
        // starts. Dividing by the time scale gives real seconds, so the
        // shake plays during the freeze, which is what sells the hit.
        float dt = (float)delta;
        if (Engine.TimeScale > 0.0)
        {
            dt /= (float)Engine.TimeScale;
        }

        _shakeLeft -= dt;
        if (_shakeLeft <= 0.0f)
        {
            HOffset = 0.0f;
            VOffset = 0.0f;
            return;
        }

        _shakeStepLeft -= dt;
        if (_shakeStepLeft > 0.0f)
        {
            return;
        }
        _shakeStepLeft = 1.0f / ShakeRate;

        // Fade out quickly: squaring the remaining share makes the first
        // jolts strong and the tail of the shake gentle.
        float remaining = _shakeLeft / _shakeDuration;
        float amount = _shakeStrength * remaining * remaining;
        // Mostly up and down, the direction a downward smash pushes.
        HOffset = (float)GD.RandRange(-amount, amount) * 0.5f;
        VOffset = (float)GD.RandRange(-amount, amount);
    }

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
