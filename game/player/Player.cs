using Godot;

// File-scoped namespace (C# 10+): everything in this file belongs to
// BulletHeavenLike without needing an extra pair of braces and indentation.
namespace BulletHeavenLike;

/// <summary>
/// The player character. Moves along the ground in the screen direction of
/// the move_left / move_right / move_up / move_down actions (WASD, the arrow
/// keys or the left-hand stick). It is drawn as a baked sprite sheet (a
/// DirectionalSprite3D, see addons/sprite_kit/) that shows one of 8 facing
/// directions and plays idle, walk, hurt and die (see UpdateAnimation).
///
/// Facing (which is also where weapons fire, see Forward) is chosen in
/// this order:
///   1. the aim actions (aim_left, ...), pressed by the right-hand stick;
///   2. on desktop, the mouse cursor: the character faces the point on
///      the ground under it;
///   3. otherwise the direction it is moving.
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

    // How fast the walk cycle plays (1 = the speed baked into the sprite).
    // Raise it if the feet look like they slide at full speed, lower it if
    // the legs look too busy.
    [Export] public float WalkAnimationSpeed = 1.0f;

    // When moving slower than full speed (a light push on the joystick),
    // the legs slow down to match, so the feet don't slide. They never go
    // slower than this fraction of normal pace, so the steps still read.
    [Export(PropertyHint.Range, "0.1,1,0.05")] public float MinLegPace = 0.35f;

    // How long (seconds) the flinch pose shows after taking a hit. 0 turns
    // it off. The character keeps moving during it; only the pose changes.
    [Export] public float HurtPoseTime = 0.3f;

    // Animation names in the .spritesheet. DirectionalSprite3D adds the
    // direction itself ("walk" -> "walk_SW"). These are const strings:
    // compile-time constants, so passing them every frame allocates nothing.
    private const string IdleAnimation = "idle";
    private const string WalkAnimation = "walk";
    private const string HurtAnimation = "hurt";
    private const string DieAnimation = "die";

    // Input action names. A StringName is Godot's fast, interned string
    // type. Creating it once here ("static readonly") avoids converting a
    // C# string to a StringName every frame.
    private static readonly StringName MoveLeft = "move_left";
    private static readonly StringName MoveRight = "move_right";
    private static readonly StringName MoveUp = "move_up";
    private static readonly StringName MoveDown = "move_down";
    private static readonly StringName AimLeft = "aim_left";
    private static readonly StringName AimRight = "aim_right";
    private static readonly StringName AimUp = "aim_up";
    private static readonly StringName AimDown = "aim_down";

    // An invisible pivot (a plain Node3D child) that turns to face where
    // the character faces. Forward and Right read their directions from it.
    // The sprite inside it is a billboard, which always turns to the camera
    // whatever its parent's rotation, so the pivot's turning only decides
    // *which* of the 8 drawn directions the sprite shows.
    private Node3D _model;

    // The sprite, a child of Model named "Sprite".
    private DirectionalSprite3D _sprite;

    // Seconds of flinch pose left (see HurtPoseTime and OnHealthChanged).
    private float _hurtPoseLeft;

    // Health at the last HealthChanged, to tell a hit from a heal.
    private int _lastHealth;


    // The camera viewing the scene. We need it so "left" means left on the
    // *screen*, not along the world X axis. The camera is rotated 45°,
    // so those two directions are different.
    private Camera3D _camera;

    // How many times a second the model blinks on and off while the
    // player is invulnerable after a hit.
    [Export] public float HurtBlinkRate = 10.0f;
    private double _hurtBlinkTime;

    // Mouse aiming (desktop only). The latest mouse position, in the 3D
    // view's own pixels (GameLayout passes the mouse events in), and
    // whether the mouse has moved at all yet. Until it has, the character
    // faces its movement direction as on the phone.
    private bool _mouseAimEnabled;
    private bool _hasMousePosition;
    private Vector2 _mousePosition;

    /// <summary>
    /// Experience and level (see PlayerStats), a child node named "Stats".
    /// "{ get; private set; }" is an auto-property: anyone can read it, but
    /// only Player can assign it (in _Ready).
    /// </summary>
    public PlayerStats Stats { get; private set; }

    // ---- Facing: the character's own frame of reference ------------------
    // Weapons (and anything else that cares about "in front of" or "behind"
    // the player) describe directions as an angle around the character,
    // seen from above:
    //
    //                 0° (front)
    //                    |
    //     270° (left) ---●--- 90° (right)
    //                    |
    //                180° (back)
    //
    // These are read-only properties. "=>" here is an "expression-bodied"
    // property: a short way to write a getter that computes its value every
    // time it is read, instead of storing it. They return plain Vector3
    // structs, so reading them every frame costs no allocations.

    /// <summary>
    /// The direction the character is facing, flat on the ground, length 1.
    /// This follows the model's actual rotation, so while the character is
    /// still turning (see TurnSpeed) it is where the model visibly points,
    /// not where the input points. When standing still it keeps the last
    /// facing.
    /// </summary>
    public Vector3 Forward => FlattenToGround(_model.GlobalBasis.Z);

    /// <summary>
    /// The character's right-hand side, flat on the ground, length 1.
    /// </summary>
    // The model's front is +Z and up is +Y. In Godot's right-handed axes
    // that makes the character's right -X, not +X (a camera, which looks
    // along -Z, has its right at +X instead).
    public Vector3 Right => FlattenToGround(-_model.GlobalBasis.X);

    /// <summary>
    /// The direction at the given angle around the character, in degrees,
    /// measured clockwise from the front when seen from above: 0 = front,
    /// 90 = right, 180 = back, 270 (or -90) = left.
    /// </summary>
    public Vector3 DirectionAt(float degrees)
    {
        // Mixing the front and right directions by cos/sin of the angle
        // sweeps a full circle: at 0° it is all Forward, at 90° all Right,
        // at 180° it is -Forward (back), and so on. Both inputs are length 1
        // and at right angles, so the result is length 1 too.
        float radians = Mathf.DegToRad(degrees);
        return Forward * Mathf.Cos(radians) + Right * Mathf.Sin(radians);
    }

    // _Ready runs once, when the node and its children have entered the scene.
    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();
        Stats = GetNode<PlayerStats>("Stats");
        Stats.LeveledUp += OnLeveledUp;
        Stats.Died += OnDied;
        Stats.HealthChanged += OnHealthChanged;
        _lastHealth = Stats.Health;

        // On Android, Godot turns every touch into a fake mouse event too
        // ("Emulate Mouse From Touch"), so the cursor would jump to the
        // joystick fingers. Only aim with the mouse on desktop.
        _mouseAimEnabled = !OS.HasFeature("mobile");

        // GetNode<T> finds a node by its path relative to this one and casts
        // it to the given type.
        _model = GetNode<Node3D>("Model");
        _sprite = GetNode<DirectionalSprite3D>("Model/Sprite");
    }

    /// <summary>
    /// Plays the death: the flinch (hurt) frames, then the fallen (die)
    /// pose, which stays.
    ///
    /// The game slows down and then pauses during the death screen, and
    /// the sprite would slow and stop with it. So the sprite is paused here
    /// and a Tween, which can count real time through both, picks the
    /// frames instead (see ShowDeathFrame).
    /// </summary>
    private void OnDied()
    {
        // No sprite frames (res://local_assets/ missing, e.g. a fresh clone): skip the
        // death animation; the rest of the death sequence still runs.
        if (_sprite.SpriteFrames == null)
        {
            return;
        }

        _sprite.PlayFacing(HurtAnimation, Forward, _camera);
        _sprite.Pause();

        // How long the hurt frames last, from the speed baked into the
        // sprite (frames per second). The die pose then holds as long as
        // the hurt part again, after which the Tween is simply done.
        SpriteFrames frames = _sprite.SpriteFrames;
        StringName hurt = _sprite.Animation;
        _deathHurtFrames = frames.GetFrameCount(hurt);
        _deathFrameTime = 1.0f / (float)Mathf.Max(frames.GetAnimationSpeed(hurt), 1.0);
        float length = _deathHurtFrames * _deathFrameTime;

        Tween tween = CreateTween();
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        tween.SetIgnoreTimeScale(true);
        tween.TweenMethod(Callable.From<float>(ShowDeathFrame), 0.0f, length * 2.0f, length * 2.0f);
    }

    // Set in OnDied: how many hurt frames there are and how long each shows.
    private int _deathHurtFrames;
    private float _deathFrameTime;

    private void ShowDeathFrame(float time)
    {
        int frame = (int)(time / _deathFrameTime);
        if (frame < _deathHurtFrames)
        {
            _sprite.Frame = frame;
        }
        else
        {
            // PlayFacing does nothing if "die" is already showing, so
            // calling it on every step of the Tween is cheap.
            _sprite.PlayFacing(DieAnimation, Forward, _camera);
        }
    }

    /// <summary>
    /// Shows the flinch pose for a moment when health goes down (but not
    /// on the killing blow; OnDied handles that).
    /// </summary>
    private void OnHealthChanged(int health, int maxHealth)
    {
        if (health < _lastHealth && health > 0)
        {
            _hurtPoseLeft = HurtPoseTime;
        }
        _lastHealth = health;
    }

    private void OnLeveledUp(int newLevel)
    {
        GD.Print($"Level up! Now level {newLevel}");
    }

    public override void _Input(InputEvent @event)
    {
        // Pattern matching again: only mouse movement is interesting here.
        if (_mouseAimEnabled && @event is InputEventMouseMotion motion)
        {
            _mousePosition = motion.Position;
            _hasMousePosition = true;
        }
    }

    // _PhysicsProcess runs at a fixed rate (60 times per second by default),
    // which is the right place for movement and collisions.
    // _Process runs once per rendered frame, which is the right place for
    // purely visual updates like the hurt blink.
    public override void _Process(double delta)
    {
        UpdateHurtBlink(delta);
    }

    /// <summary>
    /// Blinks the model while the player can't be hurt, so it's clear that
    /// a hit landed and that touching enemies is safe for a moment.
    /// </summary>
    private void UpdateHurtBlink(double delta)
    {
        // No blinking on the killing blow: the death should read clearly.
        if (!Stats.IsInvulnerable || Stats.IsDead)
        {
            _hurtBlinkTime = 0.0;
            _model.Visible = true;
            return;
        }

        // The whole part of (time * rate) counts up 0, 1, 2, ... rate times
        // a second; "% 2 == 0" makes it alternate visible / hidden.
        _hurtBlinkTime += delta;
        _model.Visible = (int)(_hurtBlinkTime * HurtBlinkRate * 2.0) % 2 == 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        // GetVector combines four actions into one 2D vector:
        //   x: -1 (left) .. +1 (right),  y: -1 (up) .. +1 (down)
        // It also caps the length at 1, so holding Up + Right isn't ~41%
        // faster than holding just one key. Its length is the speed:
        // keys give 1, the joystick anything from 0 to 1 (analog).
        //
        // The last argument is the dead zone. We pass 0 because the
        // joystick already has its own: Godot's default would squash small
        // stick values to zero and rescale the rest, so a light push
        // wouldn't come out as the slow speed the joystick meant.
        // (Vector2 and Vector3 are structs, value types, so creating them
        // every frame doesn't allocate memory.)
        // Once dead the character stops listening to the controls and
        // just stands while the game-over screen plays out.
        Vector2 input = Stats.IsDead
            ? Vector2.Zero
            : Input.GetVector(MoveLeft, MoveRight, MoveUp, MoveDown, 0.0f);
        Vector3 direction = ScreenToGround(input);

        // Velocity is a property of CharacterBody3D. MoveAndSlide() moves the
        // body by Velocity * delta itself, so we don't multiply by delta here.
        Velocity = direction * Speed;
        MoveAndSlide();

        bool isMoving = direction.LengthSquared() > 0.0001f;

        // "out" lets a method hand back an extra value besides its return
        // value: here, whether something is aiming (the bool) and where.
        if (Stats.IsDead)
        {
            // Don't turn while falling over.
        }
        else if (TryGetAim(out Vector3 aim))
        {
            // An aim of zero means "keep facing as you are" (the cursor is
            // right on top of the character), so don't turn.
            if (aim.LengthSquared() > 0.0001f)
            {
                FaceDirection(aim, (float)delta);
            }
        }
        else if (isMoving)
        {
            FaceDirection(direction, (float)delta);
        }
        UpdateAnimation(direction, (float)delta);
    }

    /// <summary>
    /// Works out where the player wants to face, if anything is aiming.
    /// Returns false when nothing is, so the caller falls back to the
    /// movement direction.
    /// </summary>
    private bool TryGetAim(out Vector3 aim)
    {
        // 1. The aim stick has priority. On desktop, dragging it with the
        // mouse also moves the cursor, and the stick should win then.
        Vector2 stick = Input.GetVector(AimLeft, AimRight, AimUp, AimDown);
        if (stick.LengthSquared() > 0.0f)
        {
            aim = ScreenToGround(stick);
            return true;
        }

        // 2. The mouse: shoot a ray from the camera through the cursor and
        // see where it hits the ground plane the character stands on.
        if (_mouseAimEnabled && _hasMousePosition)
        {
            Vector3 rayStart = _camera.ProjectRayOrigin(_mousePosition);
            Vector3 rayDirection = _camera.ProjectRayNormal(_mousePosition);
            Plane ground = new(Vector3.Up, GlobalPosition.Y);

            // "Vector3?" is a nullable Vector3: either a value or null.
            // IntersectsRay returns null when the ray misses the plane
            // (e.g. pointing at the sky), which can't happen with our
            // downward camera, but we still check.
            Vector3? hit = ground.IntersectsRay(rayStart, rayDirection);
            aim = Vector3.Zero;
            if (hit.HasValue)
            {
                Vector3 toCursor = hit.Value - GlobalPosition;
                toCursor.Y = 0.0f;
                // Ignore a cursor almost on top of the character, where a
                // tiny mouse movement would spin it around.
                if (toCursor.LengthSquared() > 0.04f)
                {
                    aim = toCursor.Normalized();
                }
            }
            return true;
        }

        // 3. Nothing is aiming. "out" parameters must always be set.
        aim = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Turns a 2D screen direction (x right, y down, like the input
    /// vectors) into a 3D direction on the ground.
    /// </summary>
    private Vector3 ScreenToGround(Vector2 screen)
    {
        // Screen "down" (+y) is the opposite of screen "up", hence the minus.
        return ScreenRightOnGround() * screen.X - ScreenUpOnGround() * screen.Y;
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
    /// Picks the sprite animation: hurt right after a hit, walk while
    /// moving, idle otherwise, always drawn in the direction the character
    /// faces. The sprite's frames are already stepped (the sheets run at
    /// about 10-13 frames a second), which gives the choppy PS1-era look
    /// without extra work.
    /// </summary>
    private void UpdateAnimation(Vector3 moveDirection, float delta)
    {
        // Dead: OnDied drives the sprite.
        if (Stats.IsDead)
        {
            return;
        }

        if (_hurtPoseLeft > 0.0f)
        {
            _hurtPoseLeft -= delta;
            _sprite.SpeedScale = 1.0f;
            _sprite.PlayFacing(HurtAnimation, Forward, _camera);
            return;
        }

        // The input's length is the speed (1 = full speed). Half speed plays
        // the legs at half pace, but never below MinLegPace.
        float speed = moveDirection.Length();
        if (speed > 0.01f)
        {
            _sprite.SpeedScale = WalkAnimationSpeed * Mathf.Max(speed, MinLegPace);
            _sprite.PlayFacing(WalkAnimation, Forward, _camera);
        }
        else
        {
            // The idle isn't a loop: its 3 frames are the head looking
            // straight, left and right, at speed 0. A fresh Play starts on
            // frame 0, looking straight ahead, and stays there.
            _sprite.SpeedScale = 1.0f;
            _sprite.PlayFacing(IdleAnimation, Forward, _camera);
        }
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
