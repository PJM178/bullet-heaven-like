using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A hammer smash on a long cooldown: a faint phantom hammer swings down
/// onto a target spot (the closest enemy, or in front of the player; see
/// Targeting) and hits every enemy in a circle there. A tribute to the
/// ground-smash skills of classic action RPGs, made of flat, pixel-art
/// sprite effects (see art/tools/make_hammer_fall_sprites.py).
///
/// One cast, layer by layer (the sheets in sprites/, all played as stepped
/// flipbooks at SpriteFps by core/sprite_flipbook.gdshader):
///   1. hammer    - the phantom hammer swings over and down. On its
///                  ImpactFrame it hits the ground and the damage happens.
///   2. hit       - a bright star burst where it landed, at the impact.
///   3. ripple    - tight rings flat on the ground, at the impact.
///   4. shockwave - a moment later, a big soft-rimmed ring bursting out and
///                  fading. Its edge damages enemies as it sweeps past them
///                  (outside the smash circle, so nobody is hit by both).
/// Plus, at the impact: pale chunks thrown up, a split-second hit-stop and
/// a camera shake.
///
/// The target spot is locked when the cast starts, and all the effects are
/// placed in the world there, so they stay put if the player moves on.
///
/// Casts are pooled (MaxCasts). With a short cooldown several can be on
/// screen at once, overlapping: rings still fading as the next hammer lands.
///
/// Put this node under the Player.
/// </summary>
public partial class HammerWeapon : Node3D
{
    [Export] public Player Player;
    [Export] public EnemyManager EnemyManager;

    // Seconds between smashes.
    [Export] public float Cooldown = 2.0f;

    // Damage to every enemy in the smash. Slimes have 2 health, so one
    // smash kills them.
    [Export] public float Damage = 3.0f;

    // The centre of the smash circle, in metres in front of the player,
    // when there's no target (see Targeting).
    [Export] public float SmashDistance = 2.0f;

    // Where the hammer lands (see WeaponTargeting). With a target in
    // TargetRange it lands right on that enemy; otherwise SmashDistance
    // in front of the player.
    [Export] public TargetMode Targeting = TargetMode.ClosestEnemy;
    [Export] public float TargetRange = 4.0f;

    // Width of the smash circle (its diameter), in metres.
    [Export] public float SmashDiameter = 2.0f;

    // Damage the shockwave's edge deals to each enemy it sweeps over,
    // beyond the smash circle. 0 turns it off.
    [Export] public float ShockwaveDamage = 1.0f;

    [ExportGroup("Timing")]
    // Frames per second for every sprite animation. Low, so they step like
    // hand-drawn effects instead of moving smoothly.
    [Export] public float SpriteFps = 20.0f;
    // The hammer frame on which it hits the ground. Damage and the impact
    // effects happen when it is shown: ImpactFrame / SpriteFps seconds in.
    [Export] public int ImpactFrame = 3;
    // Seconds between the hit and the shockwave setting off.
    [Export] public float ShockwaveDelay = 0.05f;
    // How many casts can play at once.
    [Export] public int MaxCasts = 4;

    [ExportGroup("Sprites")]
    [Export] public Material HammerMaterial;
    [Export] public Material HitMaterial;
    [Export] public Material RippleMaterial;
    [Export] public Material ShockwaveMaterial;
    // Size of each effect in the world, in metres.
    [Export] public float HammerSize = 3.6f;
    [Export] public float HitSize = 1.1f;
    [Export] public float RippleSize = 1.4f;
    // The shockwave sprite's size relative to the smash circle. The ring
    // ends at about half this width, so at 2.5 with a 2 m smash it reaches
    // about 2.4 m out, sweeping 1.4 m past the smash circle.
    [Export] public float ShockwaveScale = 2.5f;

    [ExportGroup("Impact feel")]
    // Hit-stop: for this many real seconds the game runs at this speed.
    // A tiny freeze on impact is a classic way to make a hit feel heavy.
    [Export] public float HitStopDuration = 0.0f;
    [Export(PropertyHint.Range, "0,1,0.01")] public float HitStopTimeScale = 0.05f;
    // Camera shake on impact: largest offset (metres) and length (seconds).
    [Export] public float ShakeStrength = 0.15f;
    [Export] public float ShakeDuration = 0.25f;
    // Pale chunks of ground thrown up by each impact.
    [Export] public int DebrisCount = 10;
    [Export] public Material DebrisMaterial;

    // The most enemies one smash can hit. Made once and reused, so finding
    // enemies never allocates.
    private const int MaxTargets = 64;
    private readonly int[] _targets = new int[MaxTargets];

    // Run after EnemyManager has moved enemies this step (as the sword does).
    private const int PhysicsPriorityAfterEnemies = 10;

    // Lifts ground effects a little, so they don't flicker as they fight
    // the ground for the same pixels ("z-fighting").
    private const float GroundLift = 0.03f;

    // The hammer sprite's landing point sits this far above the bottom of
    // its frame (6 of 96 pixels; see the generator script).
    private const float HammerLandingFromBottom = 6.0f / 96.0f;

    // Height of the hit burst's centre above the ground.
    private const float HitHeight = 0.3f;

    // The shockwave ring's radius on each frame, copied from
    // make_shockwave() in art/tools/make_hammer_fall_sprites.py so the
    // damage follows the drawn ring. In pixels of its 96-pixel frame:
    //   radius = 10 + 36 * grow, grow = 1 - (1 - t)^3, t = frame / (frames - 1)
    private const float ShockFramePixels = 96.0f;
    private const float ShockStartPixels = 10.0f;
    private const float ShockGrowPixels = 36.0f;

    private static readonly StringName FrameParameter = "frame";
    private static readonly StringName FramesParameter = "frames";
    private static readonly StringName FlipParameter = "flip_h";

    /// <summary>
    /// One flipbook effect within a cast: its node, its own material (so it
    /// can show a different frame from the same layer in other casts), and
    /// when in the cast it starts.
    ///
    /// A plain C# class (not a Godot node): it only groups data together.
    /// "sealed" means nothing can inherit from it, which suits a small
    /// private helper.
    /// </summary>
    private sealed class SpriteLayer
    {
        public MeshInstance3D Node;
        public ShaderMaterial Material;
        public int Frames;
        public float Start;
        // The frame currently set on the material, so we only send a new
        // value to the GPU when it changes.
        public int ShownFrame = -1;
    }

    /// <summary>One smash: its layers, debris, target and clocks.</summary>
    private sealed class Cast
    {
        public bool Active;
        public bool Impacted;
        // The shockwave's damage, sweeping outward after the impact.
        public ExpandingWave Wave;
        public Vector3 Target;
        // Two clocks, as with the other weapons: gameplay (the impact) runs
        // in _PhysicsProcess, the visuals in _Process.
        public float PhysicsTime;
        public float VisualTime;
        public SpriteLayer Hammer;
        public SpriteLayer Hit;
        public SpriteLayer Ripple;
        public SpriteLayer Shockwave;
        public CpuParticles3D Debris;
    }

    private Cast[] _casts;
    private int _nextCast;
    private float _cooldownLeft;
    private float _impactTime;
    private float _castDuration;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;

        _impactTime = ImpactFrame / SpriteFps;

        // Build every cast up front; casting only reuses them.
        _casts = new Cast[Mathf.Max(1, MaxCasts)];
        for (int i = 0; i < _casts.Length; i++)
        {
            _casts[i] = BuildCast();
        }

        // A cast is over when its last layer has played its last frame.
        Cast first = _casts[0];
        _castDuration = 0.0f;
        foreach (SpriteLayer layer in new[] { first.Hammer, first.Hit, first.Ripple, first.Shockwave })
        {
            _castDuration = Mathf.Max(_castDuration, layer.Start + layer.Frames / SpriteFps);
        }
    }

    private Cast BuildCast()
    {
        // The hammer stands on its landing point: shift the quad up so its
        // landing point, not its centre, sits at the node's position.
        QuadMesh hammerMesh = new()
        {
            Size = new Vector2(HammerSize, HammerSize),
            CenterOffset = new Vector3(0.0f, HammerSize * (0.5f - HammerLandingFromBottom), 0.0f),
        };
        float shockwaveSize = SmashDiameter * ShockwaveScale;

        return new Cast
        {
            Hammer = BuildLayer(HammerMaterial, hammerMesh, 0.0f),
            Hit = BuildLayer(HitMaterial, new QuadMesh { Size = new Vector2(HitSize, HitSize) }, _impactTime),
            // PlaneMesh lies flat on the ground (facing up) by default.
            Ripple = BuildLayer(RippleMaterial, new PlaneMesh { Size = new Vector2(RippleSize, RippleSize) }, _impactTime),
            Shockwave = BuildLayer(ShockwaveMaterial,
                new PlaneMesh { Size = new Vector2(shockwaveSize, shockwaveSize) }, _impactTime + ShockwaveDelay),
            Debris = BuildDebris(),
            Wave = new ExpandingWave(EnemyManager),
        };
    }

    private SpriteLayer BuildLayer(Material template, Mesh mesh, float start)
    {
        // Each layer gets its own copy of the material: casts can be at
        // different frames at the same time.
        ShaderMaterial material = template?.Duplicate() as ShaderMaterial;
        MeshInstance3D node = new()
        {
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // TopLevel: ignore the parent's (the player's) position. Effects
            // are placed in the world and stay there.
            TopLevel = true,
            Visible = false,
        };
        AddChild(node);

        return new SpriteLayer
        {
            Node = node,
            Material = material,
            // GetShaderParameter returns a Variant (Godot's "any value"
            // type); AsInt32 reads it as an int.
            Frames = material != null ? material.GetShaderParameter(FramesParameter).AsInt32() : 0,
            Start = start,
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || EnemyManager == null)
        {
            return;
        }
        float dt = (float)delta;

        foreach (Cast cast in _casts)
        {
            if (!cast.Active || (cast.Impacted && !cast.Wave.Active))
            {
                continue;
            }
            cast.PhysicsTime += dt;
            if (!cast.Impacted && cast.PhysicsTime >= _impactTime)
            {
                Impact(cast);
            }
            if (cast.Wave.Active)
            {
                UpdateWave(cast);
            }
        }

        _cooldownLeft -= dt;
        if (_cooldownLeft > 0.0f)
        {
            return;
        }
        _cooldownLeft += Cooldown;

        StartCast();
    }

    private void StartCast()
    {
        // Take the next cast in the pool, round and round. If it's still
        // busy (more casts than the pool holds), land its hit now so no
        // damage is lost, then reuse it.
        Cast cast = _casts[_nextCast];
        _nextCast = (_nextCast + 1) % _casts.Length;
        if (cast.Active && !cast.Impacted)
        {
            Impact(cast);
        }

        // Land on the target if there is one, else in front of the player.
        // "forward" is still needed below (which side the hammer swings
        // in from), so work it out from wherever the target ended up.
        Vector3 forward;
        if (WeaponTargeting.TryFindTarget(Targeting, Player, EnemyManager, TargetRange, out Vector3 target))
        {
            cast.Target = new Vector3(target.X, Player.GlobalPosition.Y, target.Z);
            Vector3 toTarget = cast.Target - Player.GlobalPosition;
            // An enemy right on top of the player gives no direction.
            forward = toTarget.LengthSquared() > 0.0001f ? toTarget.Normalized() : Player.Forward;
        }
        else
        {
            forward = Player.Forward;
            cast.Target = Player.GlobalPosition + forward * SmashDistance;
        }
        cast.Active = true;
        cast.Impacted = false;
        cast.Wave.Stop();
        cast.PhysicsTime = 0.0f;
        cast.VisualTime = 0.0f;

        // Place the effects. The hammer stands on the target; the hit burst
        // floats just above it; the rings lie on the ground.
        cast.Hammer.Node.GlobalPosition = cast.Target;
        cast.Hit.Node.GlobalPosition = cast.Target + Vector3.Up * HitHeight;
        cast.Ripple.Node.GlobalPosition = cast.Target + Vector3.Up * GroundLift;
        cast.Shockwave.Node.GlobalPosition = cast.Target + Vector3.Up * GroundLift;

        // The hammer sheet is drawn with its grip on the right, swinging
        // down to the left. If the target is to the right of the player on
        // screen, mirror it so the grip is on the player's side. The
        // camera's basis X is "screen right" in the world.
        Camera3D camera = GetViewport().GetCamera3D();
        bool targetOnRight = camera != null && forward.Dot(camera.GlobalBasis.X) > 0.0f;
        cast.Hammer.Material?.SetShaderParameter(FlipParameter, targetOnRight);

        UpdateCastVisual(cast);
    }

    private void Impact(Cast cast)
    {
        cast.Impacted = true;

        // QueryCircle already counts enemies whose body only overlaps the
        // edge of the circle, so the radius is just the smash's own.
        int count = EnemyManager.QueryCircle(cast.Target, SmashDiameter * 0.5f, _targets);
        for (int n = 0; n < count; n++)
        {
            EnemyManager.Damage(_targets[n], Damage);
        }

        // The shockwave starts at the smash circle's edge: everything inside
        // has just been hit by the hammer itself.
        cast.Wave.StartRing(cast.Target, SmashDiameter * 0.5f, ShockwaveDamage);

        cast.Debris.GlobalPosition = cast.Target + Vector3.Up * GroundLift;
        cast.Debris.Restart();

        // "as" gives null if the camera isn't a CameraFollow, and "?."
        // then skips the call instead of crashing.
        (GetViewport().GetCamera3D() as CameraFollow)?.Shake(ShakeStrength, ShakeDuration);

        StartHitStop();
    }

    /// <summary>
    /// Moves the shockwave's damaging edge out to where the ring is drawn
    /// now, damaging the enemies it passes, and ends it once the ring has
    /// faded.
    /// </summary>
    private void UpdateWave(Cast cast)
    {
        float radius = ShockwaveRadiusAt(cast.PhysicsTime - _impactTime);
        if (radius < 0.0f)
        {
            cast.Wave.Stop();
            return;
        }
        cast.Wave.SweepTo(radius);
    }

    /// <summary>
    /// The shockwave ring's radius in metres, timeSinceImpact seconds after
    /// the hammer hit, or -1 once it has faded out. Before it sets off
    /// (ShockwaveDelay) it is 0.
    ///
    /// It follows the sprite's growth curve, but smoothly rather than in the
    /// sprite's frame steps. Stepped, the edge jumped up to half a metre at
    /// a time, so enemies were hit in clumps every 1/SpriteFps seconds
    /// instead of one after another as the ring passed them.
    /// </summary>
    public float ShockwaveRadiusAt(float timeSinceImpact)
    {
        int frames = _casts[0].Shockwave.Frames;
        // Which frame the sprite is on, with the fraction of the way to the
        // next one kept (a float, not rounded down).
        float frame = (timeSinceImpact - ShockwaveDelay) * SpriteFps;
        if (frame < 0.0f)
        {
            return 0.0f;
        }
        if (frame >= frames)
        {
            return -1.0f;
        }
        // The last frame is fully grown (t = 1); it then stays at that size
        // until the sprite's last frame ends.
        float t = frames > 1 ? Mathf.Min(frame / (frames - 1), 1.0f) : 1.0f;
        float grow = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
        float pixels = ShockStartPixels + ShockGrowPixels * grow;
        return pixels / ShockFramePixels * SmashDiameter * ShockwaveScale;
    }

    /// <summary>
    /// Slows the whole game almost to a stop for a split second.
    /// Engine.TimeScale scales every delta (and physics), so everything
    /// slows together. The timer that ends it must ignore the time scale,
    /// or it would itself run 20 times slower.
    /// </summary>
    private void StartHitStop()
    {
        if (HitStopDuration <= 0.0f)
        {
            return;
        }
        Engine.TimeScale = HitStopTimeScale;

        // CreateTimer's extra arguments: processAlways false (it waits
        // while the game is paused), processInPhysics false, and
        // ignoreTimeScale true (it counts real seconds).
        SceneTreeTimer timer = GetTree().CreateTimer(HitStopDuration, false, false, true);
        timer.Timeout += EndHitStop;
    }

    private void EndHitStop()
    {
        Engine.TimeScale = 1.0;
    }

    // Visuals only, so they run per rendered frame.
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        foreach (Cast cast in _casts)
        {
            if (!cast.Active)
            {
                continue;
            }
            cast.VisualTime += dt;
            // Keep a cast alive until its hit and shockwave have also
            // finished in physics.
            if (cast.VisualTime >= _castDuration && cast.Impacted && !cast.Wave.Active)
            {
                cast.Active = false;
            }
            UpdateCastVisual(cast);
        }
    }

    private void UpdateCastVisual(Cast cast)
    {
        UpdateLayer(cast.Hammer, cast);
        UpdateLayer(cast.Hit, cast);
        UpdateLayer(cast.Ripple, cast);
        UpdateLayer(cast.Shockwave, cast);
    }

    /// <summary>
    /// Shows the layer's frame for this moment of the cast, or hides it
    /// before it starts and after its last frame.
    /// </summary>
    private void UpdateLayer(SpriteLayer layer, Cast cast)
    {
        if (layer.Material == null)
        {
            return;
        }

        // FloorToInt rounds down, so the frame only changes every
        // 1 / SpriteFps seconds: the stepped look.
        int frame = Mathf.FloorToInt((cast.VisualTime - layer.Start) * SpriteFps);
        bool visible = cast.Active && frame >= 0 && frame < layer.Frames;
        layer.Node.Visible = visible;

        if (visible && frame != layer.ShownFrame)
        {
            layer.Material.SetShaderParameter(FrameParameter, frame);
            layer.ShownFrame = frame;
        }
    }

    /// <summary>
    /// Pale chunks of ground thrown up on impact: a particle emitter that
    /// fires all its particles at once (Explosiveness 1) and only when told
    /// to (OneShot, started with Restart in Impact). CpuParticles3D runs on
    /// the processor, the dependable choice on phones with the
    /// Compatibility renderer.
    /// </summary>
    private CpuParticles3D BuildDebris()
    {
        // Fade each chunk out over the end of its life. The gradient's
        // colour multiplies the particle (the material uses vertex colour).
        Gradient fade = new();
        fade.SetColor(0, Colors.White);
        fade.SetColor(1, new Color(1.0f, 1.0f, 1.0f, 0.0f));
        fade.SetOffset(0, 0.6f);

        CpuParticles3D debris = new()
        {
            Emitting = false,
            OneShot = true,
            Amount = Mathf.Max(1, DebrisCount),
            Lifetime = 0.6,
            Explosiveness = 1.0f,
            // A small square sprite; the material turns it to face the
            // camera and picks one of the three chunk drawings for each
            // particle (AnimOffset is a random start frame).
            Mesh = new QuadMesh { Size = new Vector2(0.14f, 0.14f), Material = DebrisMaterial },
            AnimOffsetMin = 0.0f,
            AnimOffsetMax = 1.0f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.3f,
            Direction = Vector3.Up,
            Spread = 50.0f,
            InitialVelocityMin = 2.5f,
            InitialVelocityMax = 4.5f,
            Gravity = new Vector3(0.0f, -12.0f, 0.0f),
            ScaleAmountMin = 0.7f,
            ScaleAmountMax = 1.4f,
            ColorRamp = fade,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            // Placed in the world at each impact, not carried by the player.
            TopLevel = true,
        };
        AddChild(debris);
        return debris;
    }
}
