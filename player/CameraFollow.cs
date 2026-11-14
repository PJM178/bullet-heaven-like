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

    // The death shot: when the target's PlayerStats reports death, the
    // camera swoops in close to the body and slowly circles it. How close
    // it ends up (as a share of the normal distance), how much lower it
    // drops (0 = same angle, 1 = level with the ground), how fast it
    // circles (degrees per second; 0 = holds still) and how long the swoop
    // in takes (seconds).
    [Export(PropertyHint.Range, "0.1,1,0.005")] public float DeathZoom = 0.225f;
    [Export(PropertyHint.Range, "0,1,0.05")] public float DeathLower = 0.4f;
    [Export] public float DeathOrbitSpeed = 22.0f;
    [Export] public float DeathZoomDuration = 3.0f;

    // The death shot looks at a point this high above the body (metres),
    // so the body and the title sign over it both fit in view.
    [Export] public float DeathFocusHeight = 1.0f;

    private bool _deathShot;
    private float _deathTime;
    private float _deathStartAngle;


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
        // Hit-stop (see HammerWeapon) and the death slow-motion slow the
        // game clock. Dividing by the time scale gives real seconds, so the
        // shake and the death shot play at their own pace regardless.
        float dt = (float)delta;
        if (Engine.TimeScale > 0.0)
        {
            dt /= (float)Engine.TimeScale;
        }

        if (_deathShot)
        {
            UpdateDeathShot(dt);
            return;
        }

        if (_shakeLeft <= 0.0f)
        {
            return;
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

        // Listen for the player's death. The stats are a child of the
        // target, which already exists when this _Ready runs.
        PlayerStats stats = Target.GetNodeOrNull<PlayerStats>("Stats");
        if (stats != null)
        {
            stats.Died += StartDeathShot;
        }
    }

    private void StartDeathShot()
    {
        _deathShot = true;
        _deathTime = 0.0f;
        // Start circling from where the camera already is, so there's no
        // jump. Atan2 gives the angle of the offset seen from above.
        _deathStartAngle = Mathf.Atan2(_offset.X, _offset.Z);
        // The game is paused about a second into the death screen. "Always"
        // keeps this camera's _Process running through the pause so the
        // shot keeps moving over the frozen scene.
        ProcessMode = ProcessModeEnum.Always;
        HOffset = 0.0f;
        VOffset = 0.0f;
        _shakeLeft = 0.0f;
    }

    /// <summary>
    /// Moves the camera along the death shot: closer and lower over
    /// DeathZoomDuration while circling the body.
    /// </summary>
    private void UpdateDeathShot(float dt)
    {
        _deathTime += dt;

        // 0 -> 1 over the zoom, eased so it settles in gently at the end.
        float t = Mathf.Clamp(_deathTime / DeathZoomDuration, 0.0f, 1.0f);
        t = 1.0f - (1.0f - t) * (1.0f - t);

        // Split the normal offset into "how far out" and "how high", then
        // shrink both and drop the height a bit extra.
        float flat = new Vector2(_offset.X, _offset.Z).Length() * Mathf.Lerp(1.0f, DeathZoom, t);
        float height = _offset.Y * Mathf.Lerp(1.0f, DeathZoom * (1.0f - DeathLower), t);

        float angle = _deathStartAngle + Mathf.DegToRad(DeathOrbitSpeed) * _deathTime;

        Vector3 focus = Target.GlobalPosition + new Vector3(0.0f, DeathFocusHeight, 0.0f);
        GlobalPosition = Target.GlobalPosition
            + new Vector3(Mathf.Sin(angle) * flat, height, Mathf.Cos(angle) * flat);
        // LookAt turns the camera to face a point; Vector3.Up says which
        // way is up, so it doesn't roll.
        LookAt(focus, Vector3.Up);

        // The camera now sees every character from a new side, so each
        // 8-direction sprite re-picks which drawn direction to show.
        // Normally the characters do this themselves as they move, but
        // once the game is paused their scripts stop running. CallGroup
        // calls the method on every node in the group, paused or not.
        GetTree().CallGroup(DirectionalSprite3D.Group, DirectionalSprite3D.MethodName.RefreshFacing, this);
    }

    // The player moves in _PhysicsProcess, so the camera follows in the same
    // step. If the two updated at different rates, the player would seem to
    // jitter slightly against the background.
    public override void _PhysicsProcess(double delta)
    {
        // During the death shot _Process moves the camera instead.
        if (Target == null || _deathShot)
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
