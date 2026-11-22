using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The enemies in the scene: the "shell" around <see cref="EnemySimulation"/>.
///
/// The simulation (the "core") holds every enemy as plain data and does all
/// the game logic: spawning, steering, walls, hits, deaths. This node owns
/// one, calls it every physics tick, and turns what it says into things on
/// screen: one pooled node per enemy, sprite directions, animation, the hit
/// flash and death corpses. It also connects the core to the rest of the
/// game (the player's position and health, XP drops). The core never knows
/// this node exists, so a server that draws nothing can run the same logic.
///
/// Weapons keep using this node's query methods (QueryCircle, HitTest,
/// Damage...), which pass straight through to the core.
///
/// What each kind of enemy is like comes from its
/// <see cref="EnemyDefinition"/> (<see cref="Kinds"/>). Every kind has its
/// own pool of nodes, made once in _Ready and reused: nothing is created or
/// freed while the game runs.
/// </summary>
public partial class EnemyManager : Node3D
{
    /// <summary>The group this manager is in, so weapons added at runtime
    /// can find it without a NodePath.</summary>
    public const string Group = "enemy_manager";

    // The kinds of enemy that can spawn, and how often each one does
    // compared to the others (SpawnWeights[k] for Kinds[k]; a missing
    // weight counts as 1). A wave system can take over choosing later.
    [Export] public EnemyDefinition[] Kinds = new EnemyDefinition[0];
    [Export] public float[] SpawnWeights = new float[0];

    // The node enemies chase.
    [Export] public Node3D Target;

    // ---- Simulation settings: copied into the core every step (see
    // PushSettings), so they can still be changed while the game runs. ------

    // How many enemies we want alive, of all kinds together. The core
    // spawns or removes a few per step until it gets there. The
    // stress-test HUD changes this.
    [Export] public int TargetCount = 50;

    // Spawning/removing is spread over steps to avoid one big hitch.
    [Export] public int MaxSpawnsPerFrame = 10;

    // Enemies spawn on a ring this far from a player (off screen), and any
    // that fall further behind than DespawnDistance are moved back onto the
    // ring, so the swarm always stays around the players.
    [Export] public float SpawnDistance = 14.0f;
    [Export] public float DespawnDistance = 24.0f;

    // Enemies closer than this push each other apart, so they spread out
    // instead of piling up in the same spot.
    [Export] public float SeparationRadius = 0.6f;
    [Export] public float SeparationStrength = 6.0f;

    // Enemies stop pushing toward the player once they are this close (in
    // metres, measured centre to centre), and ease off over the
    // ArriveSlowdown metres before that. Without it the whole swarm keeps
    // shoving into the player and crushes into one tight ball.
    [Export] public float StopDistance = 0.7f;
    [Export] public float ArriveSlowdown = 1.0f;

    // ---- Connections to the rest of the game. --------------------------------

    // Where killed enemies drop their experience. Without an XpManager,
    // kills just drop nothing.
    [Export] public XpManager XpManager;

    // Walls and props enemies can't walk through. Optional: without it
    // enemies walk through everything. Each enemy's size against them is
    // its definition's BodyRadius.
    [Export] public WorldObstacles Obstacles;

    // ---- Looks. ---------------------------------------------------------------

    // Stepped, PS1-style animation rate (see Player.AnimationFps).
    [Export] public float AnimationFps = 12.0f;

    // How long an enemy flashes white after being hit, in seconds.
    [Export] public float HitFlashDuration = 0.12f;

    // The material a hit enemy is drawn with while it flashes
    // (core/hit_flash_material.tres). One material shared by every
    // enemy; leave it empty to turn the flash off.
    [Export] public Material HitFlashMaterial;

    // The flash for sprite enemies (core/sprite_hit_flash.gdshader). The
    // mesh flash above would draw a sprite as a solid white rectangle, so
    // sprites get their own material, built in _Ready.
    [Export] public Shader SpriteHitFlashShader;

    // Sprite enemies play their death animation on a separate, small pool
    // of "corpse" sprites (EnemyDefinition.CorpsePoolSize). The killed
    // enemy's node goes back to its pool right away, so it can't stay
    // behind to play it.

    // How long the last frame of the death animation (the body on the
    // ground) stays before the corpse disappears, in seconds.
    [Export] public float CorpseLinger = 0.4f;

    // After lingering, the corpse dithers away over this many seconds
    // (enemies/corpse_fade.gdshader). Without the shader it just vanishes.
    [Export] public float CorpseFadeTime = 0.4f;
    [Export] public Shader CorpseFadeShader;

    private static readonly StringName MoveAnimation = "move";

    // A sprite enemy's walk animation ("walk" -> "walk_SW" etc., see
    // DirectionalSprite3D). A const string allocates nothing per call.
    private const string WalkAnimation = "walk";
    private const string DieAnimation = "die";

    /// <summary>
    /// One enemy's node and the parts of it we use, found once when the pool
    /// is built.
    /// </summary>
    private sealed class EnemyBody
    {
        public Node3D Node;
        // One of these two is set, depending on the kind of scene; the
        // other stays null.
        public AnimationPlayer AnimationPlayer;
        public DirectionalSprite3D Sprite;
        // Every mesh node in the enemy, so the flash can be set on all of
        // them (a model can be made of several meshes).
        public GeometryInstance3D[] Meshes;
        // Seconds of hit flash left; 0 = not flashing.
        public float FlashTime;
    }

    /// <summary>
    /// The looks of one kind of enemy: its scene, flash material and corpses.
    /// </summary>
    private sealed class Kind
    {
        public EnemyDefinition Definition;
        public PackedScene Scene;      // Scene or FallbackScene, see ChooseScene
        public Material FlashMaterial; // whichever flash fits the scene

        // Corpses. Empty arrays when the kind has no death animation, so
        // nothing below needs a null check.
        public Node3D[] Corpses = Array.Empty<Node3D>();
        public DirectionalSprite3D[] CorpseSprites = Array.Empty<DirectionalSprite3D>();
        public float[] CorpseTime = Array.Empty<float>(); // seconds left; 0 = free
        public int NextCorpse;                             // next one to use, wraps around
    }

    // The game logic. Null until _Ready (and if the manager is misconfigured).
    private EnemySimulation _sim;

    private Kind[] _kinds = Array.Empty<Kind>();

    // One node per simulation slot (see EnemySimulation): the enemy in slot
    // s is always drawn by _bodies[s]. Slots are numbered kind by kind, in
    // the same order the pools are built here.
    private EnemyBody[] _bodies = Array.Empty<EnemyBody>();

    // What is handed to the core every step, made once and reused. One
    // entry per player; there is only one player (Target) for now.
    private readonly Vector3[] _playerPositions = new Vector3[1];
    private readonly int[] _contactDamage = new int[1];

    // The camera, which decides which of a sprite's 8 directions shows.
    // Looked up when first needed (see GetCamera).
    private Camera3D _camera;

    private double _animationTimeAccumulated;

    // The Target's health and XP, found in _Ready. Null if the target has
    // no PlayerStats child, in which case enemies just don't hurt it.
    private PlayerStats _targetStats;

    // How many enemies are alive (read by the HUD). "?." and "??": if _sim
    // is null, use 0 instead of crashing.
    public int Count => _sim?.Count ?? 0;

    // The most enemies that can be alive at once: all pools together.
    public int MaxEnemies => _sim?.Capacity ?? 0;

    // How many enemies have been killed since the game started (read by
    // the HUD).
    public int Kills => _sim?.Kills ?? 0;

    public override void _EnterTree()
    {
        // Before any _Ready, so weapons always find us in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        if (Kinds.Length == 0 || Target == null)
        {
            GD.PushWarning("EnemyManager needs Kinds and Target set.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        // GetNodeOrNull returns null instead of erroring when there's no
        // such child. Children already exist when _Ready runs, so this is
        // safe even if the Player's own _Ready hasn't run yet.
        _targetStats = Target.GetNodeOrNull<PlayerStats>("Stats");

        // Build every kind's pool, hidden, and note how big each one is: the
        // pool size is how many of that kind the core may have alive.
        // "new" in _Ready is fine; it's doing it every frame that we avoid.
        _kinds = new Kind[Kinds.Length];
        int[] capacities = new int[Kinds.Length];
        float[] weights = new float[Kinds.Length];
        int total = 0;
        for (int k = 0; k < Kinds.Length; k++)
        {
            _kinds[k] = BuildKind(Kinds[k]);
            capacities[k] = _kinds[k].Scene != null ? Math.Max(Kinds[k].PoolSize, 0) : 0;
            weights[k] = k < SpawnWeights.Length ? SpawnWeights[k] : 1.0f;
            total += capacities[k];
        }

        // The nodes, one per slot, kind by kind in the same order as the
        // capacities (which is how the core numbers its slots).
        _bodies = new EnemyBody[total];
        int slot = 0;
        for (int k = 0; k < _kinds.Length; k++)
        {
            for (int n = 0; n < capacities[k]; n++)
            {
                EnemyBody body = MakeBody(_kinds[k].Scene);
                if (body.Sprite != null && n == 0)
                {
                    // Every enemy of a kind is the same scene, so one flash
                    // material made from the first one serves them all.
                    _kinds[k].FlashMaterial = MakeSpriteMaterial(SpriteHitFlashShader, body.Sprite);
                }
                if (n == 0)
                    BuildCorpses(_kinds[k], body.Sprite);
                _bodies[slot] = body;
                slot++;
            }
        }

        // Kinds without a scene get capacity 0, so the core never spawns
        // them. Obstacles is handed over as the IObstacleQuery interface:
        // the core can ask it questions but never sees the node.
        _sim = new EnemySimulation(Kinds, weights, capacities, Obstacles);
    }

    /// <summary>
    /// Sets up one kind's looks. A kind with no definition or scene gets no
    /// scene, so it has no pool and never spawns.
    /// </summary>
    private Kind BuildKind(EnemyDefinition definition)
    {
        var kind = new Kind { Definition = definition };
        if (definition == null || definition.Scene == null)
        {
            GD.PushWarning($"EnemyManager: a kind has no definition or scene; it won't spawn.");
            return kind;
        }

        kind.Scene = ChooseScene(definition);
        kind.FlashMaterial = HitFlashMaterial;
        return kind;
    }

    private EnemyBody MakeBody(PackedScene scene)
    {
        // Instantiate<T> creates a copy of the scene and casts its root.
        Node3D node = scene.Instantiate<Node3D>();
        node.Visible = false;
        // Adding it runs its _Ready, which is when a DirectionalSprite3D
        // loads its frames, so look at the children after this.
        AddChild(node);

        AnimationPlayer animationPlayer = node.GetNodeOrNull<AnimationPlayer>("Model/AnimationPlayer");
        if (animationPlayer != null)
        {
            // Manual: the player only moves when we call Advance(), which
            // lets us step all enemies together at AnimationFps.
            animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
        }

        return new EnemyBody
        {
            Node = node,
            AnimationPlayer = animationPlayer,
            Sprite = node.GetNodeOrNull<DirectionalSprite3D>("Model/Sprite"),
            Meshes = FindMeshes(node),
        };
    }

    /// <summary>
    /// Makes a kind's corpse pool, if its enemies are sprites with a death
    /// animation. Corpses are ordinary copies of the enemy scene that only
    /// ever play "die".
    /// </summary>
    private void BuildCorpses(Kind kind, DirectionalSprite3D sprite)
    {
        int size = kind.Definition.CorpsePoolSize;
        if (size <= 0 || sprite == null || sprite.SpriteFrames == null)
        {
            return;
        }
        // The sprite's animations are named per direction, e.g. "die_S".
        if (!sprite.SpriteFrames.HasAnimation(SpriteDirections.AnimationName(DieAnimation, 0)))
        {
            return;
        }

        kind.Corpses = new Node3D[size];
        kind.CorpseSprites = new DirectionalSprite3D[size];
        kind.CorpseTime = new float[size];
        // One material for every corpse of the kind; each one's fade amount
        // travels in its own Modulate alpha (see corpse_fade.gdshader).
        Material fadeMaterial = MakeSpriteMaterial(CorpseFadeShader, sprite);
        for (int c = 0; c < size; c++)
        {
            Node3D node = kind.Scene.Instantiate<Node3D>();
            node.Visible = false;
            AddChild(node);
            kind.Corpses[c] = node;
            kind.CorpseSprites[c] = node.GetNode<DirectionalSprite3D>("Model/Sprite");
            kind.CorpseSprites[c].MaterialOverride = fadeMaterial;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        PushSettings();

        // Godot -> core: where the players are. Only one for now.
        _playerPositions[0] = Target.GlobalPosition;

        _sim.Step((float)delta, _playerPositions, _contactDamage);

        // Core -> Godot. Removed before spawned: a slot freed this step can
        // be reused at once, and its node must end up shown, not hidden.
        HideRemoved();
        ShowSpawned();
        PlaceBodies();

        // One touch is enough to cost a heart. TakeDamage ignores it while
        // the player is still invulnerable from the previous hit, so it is
        // fine to call it every step something is touching.
        if (_contactDamage[0] > 0 && _targetStats != null)
        {
            _targetStats.TakeDamage(_contactDamage[0]);
        }
    }

    public override void _Process(double delta)
    {
        // Visual-only updates live in _Process (every rendered frame)
        // rather than _PhysicsProcess, so they look smooth at any frame rate.
        UpdateHitFlash((float)delta);
        UpdateCorpses((float)delta);
        AdvanceAnimations(delta);
    }

    /// <summary>
    /// Copies the [Export] settings into the core. A handful of
    /// assignments per step, and it means editing them while the game runs
    /// (the editor's remote inspector, the stress-test HUD) still works.
    /// </summary>
    private void PushSettings()
    {
        _sim.TargetCount = TargetCount;
        _sim.MaxSpawnsPerFrame = MaxSpawnsPerFrame;
        _sim.SpawnDistance = SpawnDistance;
        _sim.DespawnDistance = DespawnDistance;
        _sim.SeparationRadius = SeparationRadius;
        _sim.SeparationStrength = SeparationStrength;
        _sim.StopDistance = StopDistance;
        _sim.ArriveSlowdown = ArriveSlowdown;
    }

    // ---- Core -> nodes ----------------------------------------------------------

    /// <summary>
    /// Hides the nodes of enemies the core removed this step, ending any
    /// flash still going so the node is back to its normal look when reused.
    /// </summary>
    private void HideRemoved()
    {
        // ReadOnlySpan works like a read-only array here; see EnemySimulation.
        ReadOnlySpan<int> removed = _sim.RemovedSlots;
        for (int n = 0; n < removed.Length; n++)
        {
            EnemyBody body = _bodies[removed[n]];
            body.Node.Visible = false;
            if (body.FlashTime > 0.0f)
            {
                body.FlashTime = 0.0f;
                SetHitFlash(body, null);
            }
        }
    }

    /// <summary>
    /// Shows the nodes of enemies the core spawned this step and starts
    /// their animation at a random point in its cycle, so they don't all
    /// hop in perfect unison.
    /// </summary>
    private void ShowSpawned()
    {
        ReadOnlySpan<int> spawned = _sim.Spawned;
        for (int n = 0; n < spawned.Length; n++)
        {
            int i = spawned[n];
            EnemyBody body = _bodies[_sim.GetSlot(i)];
            body.Node.Visible = true;

            DirectionalSprite3D sprite = body.Sprite;
            if (sprite != null)
            {
                Camera3D camera = GetCamera();
                if (camera != null && sprite.SpriteFrames != null)
                {
                    sprite.PlayFacing(WalkAnimation, _sim.Headings[i], camera);
                    int frameCount = sprite.SpriteFrames.GetFrameCount(sprite.Animation);
                    sprite.Frame = Random.Shared.Next(frameCount);
                }
                continue;
            }

            AnimationPlayer animationPlayer = body.AnimationPlayer;
            if (animationPlayer != null)
            {
                animationPlayer.Play(MoveAnimation);
                // Random.Shared is .NET's ready-made random generator, so we
                // don't need to create (allocate) our own. (We read the
                // length from the clip itself: in Manual mode, Play() hasn't
                // fully started the animation yet, so CurrentAnimationLength
                // would still be 0.) This randomness is only for looks, so
                // it doesn't use the core's random generator.
                double length = animationPlayer.GetAnimation(MoveAnimation).Length;
                animationPlayer.Seek(Random.Shared.NextDouble() * length, true);
            }
        }
    }

    /// <summary>
    /// Moves every live enemy's node to where the core says it is, facing
    /// the way it moves.
    /// </summary>
    private void PlaceBodies()
    {
        Camera3D camera = GetCamera();
        ReadOnlySpan<Vector3> positions = _sim.Positions;
        ReadOnlySpan<Vector3> velocities = _sim.Velocities;
        ReadOnlySpan<Vector3> headings = _sim.Headings;

        for (int i = 0; i < positions.Length; i++)
        {
            EnemyBody body = _bodies[_sim.GetSlot(i)];

            // Face the direction of travel (the model's front is +Z, see
            // Player.FaceDirection). Building the whole transform and
            // setting it once is one call into the engine instead of two.
            float yaw = Mathf.Atan2(velocities[i].X, velocities[i].Z);
            body.Node.Transform = new Transform3D(new Basis(Vector3.Up, yaw), positions[i]);

            // A sprite is a billboard, so turning the node doesn't show; it
            // picks which drawn direction to show instead. It faces the
            // player rather than its exact velocity, because the crowd's
            // pushing makes velocity jitter, and the sprite would flicker
            // between directions. PlayFacing does nothing if neither the
            // animation nor the direction changed, so this is cheap.
            DirectionalSprite3D sprite = body.Sprite;
            if (sprite != null && camera != null)
            {
                sprite.PlayFacing(WalkAnimation, headings[i], camera);
            }
        }
    }

    // ---- Hits -------------------------------------------------------------------

    /// <summary>
    /// Deals damage to enemy i (an index from a query, used in the same
    /// physics step). The core decides what happens; this adds the looks
    /// (flash, corpse) and the XP drop.
    /// </summary>
    public void Damage(int i, float amount)
    {
        EnemySimulation.DamageResult result = _sim.Damage(i, amount);
        if (result == EnemySimulation.DamageResult.None)
        {
            return;
        }

        EnemyBody body = _bodies[_sim.GetSlot(i)];

        // Start (or restart) the white flash; UpdateHitFlash ends it. Only
        // swap the material if it isn't already flashing.
        if (HitFlashMaterial != null && HitFlashDuration > 0.0f)
        {
            if (body.FlashTime <= 0.0f)
            {
                SetHitFlash(body, _kinds[_sim.GetKind(i)].FlashMaterial);
            }
            body.FlashTime = HitFlashDuration;
        }

        if (result == EnemySimulation.DamageResult.Killed)
        {
            // Hide it now so it vanishes on the frame it was killed; the core
            // removes it at the start of the next step. A corpse takes its
            // place for the death animation (sprite enemies only; others
            // just vanish).
            body.Node.Visible = false;
            StartCorpse(i, body);

            // "?." calls Drop only if XpManager is set (not null).
            XpManager?.Drop(_sim.GetPosition(i), _sim.GetDefinition(i).XpPerKill);
        }
    }

    // ---- Queries for weapons: straight through to the core. "=>" makes a
    // method whose whole body is that one expression. See EnemySimulation
    // for what each one does. ------------------------------------------------------

    public float GetHitRadius(int i) => _sim.GetHitRadius(i);
    public bool CanReach(Vector3 from, int i) => _sim.CanReach(from, i);
    public Vector3 GetPosition(int i) => _sim.GetPosition(i);
    public int GetId(int i) => _sim.GetId(i);
    public int QueryCircle(Vector3 point, float radius, int[] results) => _sim.QueryCircle(point, radius, results);
    public int QueryRing(Vector3 point, float innerRadius, float outerRadius, int[] results)
        => _sim.QueryRing(point, innerRadius, outerRadius, results);
    public int FindClosest(Vector3 point, float maxRange) => _sim.FindClosest(point, maxRange);
    public int FindClosest(Vector3 point, float maxRange, int[] results) => _sim.FindClosest(point, maxRange, results);
    public int HitTest(Vector3 point, float radius) => _sim.HitTest(point, radius);

    // ---- Looks ------------------------------------------------------------------

    /// <summary>
    /// Counts down the hit flash on enemies that were hit recently and
    /// ends it when the time runs out. Enemies that aren't flashing are
    /// skipped after one comparison, so a swarm that isn't being hit costs
    /// almost nothing here.
    /// </summary>
    private void UpdateHitFlash(float delta)
    {
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            EnemyBody body = _bodies[_sim.GetSlot(i)];
            if (body.FlashTime <= 0.0f)
            {
                continue;
            }

            body.FlashTime -= delta;
            if (body.FlashTime <= 0.0f)
            {
                body.FlashTime = 0.0f;
                SetHitFlash(body, null);
            }
        }
    }

    /// <summary>
    /// Sets the flash material on all of an enemy's meshes (null = off).
    ///
    /// MaterialOverride replaces the mesh's own material(s) while it is set;
    /// setting it back to null brings the normal look back. Swapping in one
    /// shared material works for any number of enemies. (We first tried a
    /// per-mesh "instance uniform", but phones like the Adreno 642L only
    /// have room for about 256 of those in the whole game.)
    /// </summary>
    private static void SetHitFlash(EnemyBody body, Material material)
    {
        GeometryInstance3D[] meshes = body.Meshes;
        for (int m = 0; m < meshes.Length; m++)
        {
            meshes[m].MaterialOverride = material;
        }
    }

    private void AdvanceAnimations(double delta)
    {
        // Same stepped animation idea as Player._Process, but one clock
        // drives every enemy, so they all update on the same beat.
        // Sprite enemies animate by themselves: their frames are already
        // drawn at a low rate, so they look stepped without our help, and
        // have no AnimationPlayer (null) to advance.
        int count = Count;
        if (count == 0)
        {
            return;
        }

        if (AnimationFps <= 0.0f)
        {
            for (int i = 0; i < count; i++)
            {
                _bodies[_sim.GetSlot(i)].AnimationPlayer?.Advance(delta);
            }
            return;
        }

        _animationTimeAccumulated += delta;
        double step = 1.0 / AnimationFps;
        if (_animationTimeAccumulated < step)
        {
            return;
        }

        int steps = (int)(_animationTimeAccumulated / step);
        double advance = steps * step;
        _animationTimeAccumulated -= advance;
        for (int i = 0; i < count; i++)
        {
            _bodies[_sim.GetSlot(i)].AnimationPlayer?.Advance(advance);
        }
    }

    /// <summary>
    /// Puts a corpse where enemy i is standing and starts its death
    /// animation, facing the same way the enemy did.
    /// </summary>
    private void StartCorpse(int i, EnemyBody body)
    {
        Kind kind = _kinds[_sim.GetKind(i)];
        Camera3D camera = GetCamera();
        if (kind.Corpses.Length == 0 || camera == null)
        {
            return;
        }

        int c = kind.NextCorpse;
        // "%" (remainder) wraps the index back to 0 after the last corpse.
        kind.NextCorpse = (kind.NextCorpse + 1) % kind.Corpses.Length;

        Node3D node = kind.Corpses[c];
        DirectionalSprite3D sprite = kind.CorpseSprites[c];
        node.Transform = body.Node.Transform;
        node.Visible = true;
        // Fully there again (a reused corpse may have been mid-fade).
        sprite.Modulate = Colors.White;

        // PlayFacing skips the call when this corpse last played "die" in
        // the same direction, and that run has already finished. So rewind
        // to the first frame and start it explicitly. Play() with no name
        // plays the current animation.
        sprite.PlayFacing(DieAnimation, _sim.Headings[i], camera);
        sprite.Frame = 0;
        sprite.Play();

        // How long the animation runs: frames / (frames per second). The
        // importer gives every frame of an animation the same length.
        SpriteFrames frames = sprite.SpriteFrames;
        StringName animation = sprite.Animation;
        double fps = Math.Max(frames.GetAnimationSpeed(animation), 1.0);
        float length = (float)(frames.GetFrameCount(animation) / fps);
        kind.CorpseTime[c] = length + CorpseLinger + CorpseFadeTime;
    }

    /// <summary>
    /// Counts down every corpse, dithers it away during its last
    /// CorpseFadeTime seconds, and hides it when its time is up.
    /// </summary>
    private void UpdateCorpses(float delta)
    {
        for (int k = 0; k < _kinds.Length; k++)
        {
            Kind kind = _kinds[k];
            for (int c = 0; c < kind.CorpseTime.Length; c++)
            {
                if (kind.CorpseTime[c] <= 0.0f)
                {
                    continue;
                }

                kind.CorpseTime[c] -= delta;
                if (kind.CorpseTime[c] <= 0.0f)
                {
                    kind.CorpseTime[c] = 0.0f;
                    kind.Corpses[c].Visible = false;
                }
                else if (kind.CorpseTime[c] < CorpseFadeTime)
                {
                    // Time left as a fraction of the fade: 1 at its start,
                    // falling to 0. Color is a struct (a plain value, like
                    // Vector3), so "new Color" here allocates nothing.
                    float visible = kind.CorpseTime[c] / CorpseFadeTime;
                    kind.CorpseSprites[c].Modulate = new Color(1.0f, 1.0f, 1.0f, visible);
                }
            }
        }
    }

    /// <summary>
    /// The active 3D camera, looked up once. Not in _Ready, because the
    /// camera may not have become the current one yet when this node is
    /// ready. Returns null if there's no camera.
    /// </summary>
    private Camera3D GetCamera()
    {
        // "??=" assigns only if the left side is still null.
        _camera ??= GetViewport().GetCamera3D();
        return _camera;
    }

    /// <summary>
    /// The definition's Scene, unless it's a sprite enemy whose frames are
    /// missing; then its FallbackScene. Instances one test enemy to find out,
    /// which is the simplest way to see exactly what the scene's sprite
    /// would load.
    /// </summary>
    private PackedScene ChooseScene(EnemyDefinition definition)
    {
        if (definition.FallbackScene == null)
        {
            return definition.Scene;
        }

        Node3D test = definition.Scene.Instantiate<Node3D>();
        AddChild(test); // runs DirectionalSprite3D._Ready, which loads the frames
        DirectionalSprite3D sprite = test.GetNodeOrNull<DirectionalSprite3D>("Model/Sprite");
        bool missingFrames = sprite != null && sprite.SpriteFrames == null;
        RemoveChild(test);
        // Free() deletes the node right away. (QueueFree() would wait until
        // the end of the frame; either is fine for a one-off in _Ready.)
        test.Free();

        return missingFrames ? definition.FallbackScene : definition.Scene;
    }

    /// <summary>
    /// Builds a material from one of the sprite shaders (hit flash, corpse
    /// fade) for a sprite enemy. Sprite frames are AtlasTextures: small
    /// windows into one big texture (the atlas). The shaders sample that
    /// atlas, set here as "sheet", with the sprite's own UVs.
    /// Returns null when there's no shader, so the effect is just off.
    /// </summary>
    private static Material MakeSpriteMaterial(Shader shader, DirectionalSprite3D sprite)
    {
        SpriteFrames frames = sprite.SpriteFrames;
        if (shader == null || frames == null)
        {
            return null;
        }

        // "as" casts if it can and gives null if not (instead of throwing).
        string firstAnimation = frames.GetAnimationNames()[0];
        AtlasTexture frame = frames.GetFrameTexture(firstAnimation, 0) as AtlasTexture;
        if (frame == null)
        {
            return null;
        }

        ShaderMaterial material = new ShaderMaterial();
        material.Shader = shader;
        material.SetShaderParameter("sheet", frame.Atlas);
        return material;
    }

    /// <summary>
    /// Collects every mesh node under an enemy, at any depth. Only called
    /// in _Ready: it allocates, which is fine once but not every frame.
    /// </summary>
    private static GeometryInstance3D[] FindMeshes(Node root)
    {
        // FindChildren(pattern, type, recursive, owned). "*" matches any
        // name, and owned: false is needed because the nodes inside an
        // imported .glb model belong to that model's scene, not ours.
        Godot.Collections.Array<Node> found = root.FindChildren("*", "GeometryInstance3D", true, false);

        GeometryInstance3D[] meshes = new GeometryInstance3D[found.Count];
        for (int m = 0; m < found.Count; m++)
        {
            // "(Type)value" is a cast: we know these nodes are
            // GeometryInstance3D because that's what we searched for.
            meshes[m] = (GeometryInstance3D)found[m];
        }
        return meshes;
    }
}
