using Godot;

// File-scoped namespace (C# 10+): everything in this file belongs to
// BulletHeavenLike without needing an extra pair of braces and indentation.
namespace BulletHeavenLike;

/// <summary>
/// The player character. Moves along the ground in the screen direction of
/// the move_left / move_right / move_up / move_down actions (the arrow keys),
/// turns to face where it's going, and plays the run or idle animation.
/// </summary>
// "partial" is required by Godot: its source generator writes the other half
// of this class (the glue that connects C# to the engine).
public partial class Player : CharacterBody3D
{
    // [Export] makes this field show up in the Inspector so it can be tuned
    // in the editor without touching code. Units are metres per second.
    [Export] public float Speed = 5.0f;

    // How quickly the model turns to face the movement direction. Higher is
    // snappier; it's a rate, not degrees, see _PhysicsProcess.
    [Export] public float TurnSpeed = 14.0f;

    // Seconds to cross-fade between run and idle, so switching isn't a pop.
    [Export] public float AnimationBlendTime = 0.12f;

    // How many times per second the character's pose updates. A low value
    // (around 10-15) gives the choppy, stepped look of PS1-era games, while
    // movement and the rest of the game stay smooth. 0 = update every frame.
    [Export] public float AnimationFps = 12.0f;

    // Animation names, as exported from Blender. A StringName is Godot's
    // fast, interned string type. Creating it once here ("static readonly")
    // avoids converting a C# string to a StringName every frame.
    private static readonly StringName RunAnimation = "run";
    private static readonly StringName IdleAnimation = "idle";

    // The imported model (a child node) and the AnimationPlayer that came
    // with it. We rotate only the model, not the whole body, so the
    // collision shape and camera maths stay simple.
    private Node3D _model;
    private AnimationPlayer _animationPlayer;

    // Remembers which animation we asked for, so we only call Play() when
    // it actually changes.
    private bool _isRunning;

    // Game time collected since the pose was last updated (see _Process).
    private double _animationTimeAccumulated;

    // The camera viewing the scene. We need it so "left" means left on the
    // *screen*, not along the world X axis. The camera is rotated 45°,
    // so those two directions are different.
    private Camera3D _camera;

    // _Ready runs once, when the node and its children have entered the scene.
    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();

        // GetNode<T> finds a node by its path relative to this one and casts
        // it to the given type. The AnimationPlayer lives inside the
        // imported player.glb scene, which is instanced as "Model".
        _model = GetNode<Node3D>("Model");
        _animationPlayer = GetNode<AnimationPlayer>("Model/AnimationPlayer");
        _animationPlayer.Play(IdleAnimation);
    }

    // _PhysicsProcess runs at a fixed rate (60 times per second by default),
    // which is the right place for movement and collisions.
    // _Process runs once per rendered frame, which is the right place for
    // purely visual updates like the animation pose.
    public override void _Process(double delta)
    {
        if (AnimationFps <= 0.0f)
        {
            // Smooth mode: let the AnimationPlayer update itself every frame.
            _animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Idle;
            return;
        }

        // Choppy mode: "Manual" stops the AnimationPlayer from advancing on
        // its own; it only moves when we call Advance().
        _animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

        // Collect time, and once a whole step (e.g. 1/12 s) has passed, jump
        // the animation forward by that many steps in one go. In between,
        // the pose holds still, which is what makes it look stepped.
        _animationTimeAccumulated += delta;
        double step = 1.0 / AnimationFps;
        if (_animationTimeAccumulated >= step)
        {
            // (int) cuts off the fraction, e.g. 2.7 steps -> 2 whole steps.
            int steps = (int)(_animationTimeAccumulated / step);
            _animationPlayer.Advance(steps * step);
            // Keep the leftover fraction so the rhythm stays even.
            _animationTimeAccumulated -= steps * step;
        }
    }

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

        bool isMoving = direction.LengthSquared() > 0.0001f;
        if (isMoving)
        {
            FaceDirection(direction, (float)delta);
        }
        UpdateAnimation(isMoving);
    }

    /// <summary>
    /// Smoothly turns the model toward a direction on the ground.
    /// </summary>
    private void FaceDirection(Vector3 direction, float delta)
    {
        // The model's front is +Z (Blender's -Y after the glTF export).
        // Atan2(x, z) is the angle around the vertical (Y) axis that turns
        // +Z to point along "direction".
        float targetAngle = Mathf.Atan2(direction.X, direction.Z);

        // LerpAngle moves part of the way toward the target and takes the
        // short way round (it won't spin 350 degrees instead of -10).
        // 1 - Exp(-speed * delta) gives the same turning feel at any frame
        // rate, unlike a fixed fraction per frame.
        float weight = 1.0f - Mathf.Exp(-TurnSpeed * delta);

        // Rotation is a struct property: copy it, change Y, assign it back.
        Vector3 rotation = _model.Rotation;
        rotation.Y = Mathf.LerpAngle(rotation.Y, targetAngle, weight);
        _model.Rotation = rotation;
    }

    /// <summary>
    /// Plays the run animation while moving and idle while standing still.
    /// </summary>
    private void UpdateAnimation(bool isMoving)
    {
        if (isMoving == _isRunning)
        {
            return; // already playing the right one
        }
        _isRunning = isMoving;
        _animationPlayer.Play(isMoving ? RunAnimation : IdleAnimation, AnimationBlendTime);
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
