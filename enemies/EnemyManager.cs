using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns and updates every enemy in one place.
///
/// Instead of each enemy having its own script and physics body, this node
/// keeps all enemy data in plain C# arrays and moves everything in a single
/// loop. That is much cheaper with hundreds of enemies, and it means the
/// way enemies are *drawn* (nodes today, maybe a MultiMesh later) can change
/// without touching movement or game logic.
///
/// Enemies are pooled: every enemy node is created once in _Ready and reused.
/// Nothing is created or freed while the game runs.
/// </summary>
public partial class EnemyManager : Node3D
{
    // The scene to instance for each enemy. Two kinds work:
    // - a 3D model with an AnimationPlayer at "Model/AnimationPlayer"
    //   playing a "move" clip (enemies/slime.tscn), or
    // - an 8-direction sprite: a DirectionalSprite3D at "Model/Sprite"
    //   playing "walk" (enemies/basic_enemy.tscn).
    [Export] public PackedScene EnemyScene;

    // Used instead of EnemyScene when EnemyScene is a sprite enemy whose
    // frames aren't there (local_assets/ is git-ignored, so a fresh clone
    // has none). Keeps the game playable without them.
    [Export] public PackedScene FallbackScene;

    // The node enemies chase.
    [Export] public Node3D Target;

    // Size of the pool: the most enemies that can ever be alive at once.
    [Export] public int MaxEnemies = 600;

    // How many enemies we want alive. The manager spawns or removes a few
    // per frame until it gets there. The stress-test HUD changes this.
    [Export] public int TargetCount = 50;

    // Spawning/removing is spread over frames to avoid one big hitch.
    [Export] public int MaxSpawnsPerFrame = 10;

    // Metres per second.
    [Export] public float Speed = 2.0f;

    // Enemies spawn on a ring this far from the target (off screen), and any
    // that fall further behind than DespawnDistance are moved back onto the
    // ring, so the swarm always stays around the player.
    [Export] public float SpawnDistance = 14.0f;
    [Export] public float DespawnDistance = 24.0f;

    // Enemies closer than this push each other apart, so they spread out
    // instead of piling up in the same spot.
    [Export] public float SeparationRadius = 0.6f;
    [Export] public float SeparationStrength = 6.0f;

    // Enemies stop pushing toward the target once they are this close (in
    // metres, measured centre to centre), and ease off over the
    // ArriveSlowdown metres before that. Without it the whole swarm keeps
    // shoving into the player and crushes into one tight ball.
    [Export] public float StopDistance = 0.7f;
    [Export] public float ArriveSlowdown = 1.0f;

    // Hit points each enemy starts with. A shard does 1 damage, so the
    // default slime takes three hits.
    [Export] public float MaxHealth = 2.0f;

    // Where killed enemies drop their experience, and how much each is
    // worth. Without an XpManager, kills just drop nothing.
    [Export] public XpManager XpManager;

    // Hearts an enemy takes when it touches the player, and how close
    // (centre to centre, in metres) counts as touching. Enemies stop at
    // StopDistance, and the crowd jostles them a little further out, so
    // this needs to be somewhat larger than StopDistance.
    [Export] public int ContactDamage = 1;
    [Export] public float ContactRange = 0.9f;

    [Export] public int XpPerKill = 100;

    // How big an enemy counts as for projectile collisions, in metres on
    // the ground (see HitTest). Roughly the slime's footprint.
    [Export] public float EnemyRadius = 0.25f;

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
    // of "corpse" sprites. The killed enemy's own slot is handed to a new
    // enemy right away, so it can't stay behind to play it. If more enemies
    // die at once than there are corpses, the oldest corpse is reused early.
    [Export] public int MaxCorpses = 64;

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

    // Whichever of the two flash materials fits the enemy scene in use.
    private Material _flashMaterial;

    // The camera, which decides which of a sprite's 8 directions shows.
    // Looked up when first needed (see MoveEnemies).
    private Camera3D _camera;

    // ---- Enemy data. Index i in every array is the same enemy. Only the
    // first _count entries are alive; the rest are spare pool slots. ------
    private Vector3[] _positions;
    private Vector3[] _separation;          // per-frame push from neighbours
    private float[] _health;
    private bool[] _dead;                   // killed, removed next step
    private float[] _flashTime;             // seconds of hit flash left
    private Node3D[] _nodes;
    // One of these two is set per enemy, depending on the kind of scene;
    // the other stays null.
    private AnimationPlayer[] _animationPlayers;
    private DirectionalSprite3D[] _sprites;
    // Every mesh node in each enemy, so the flash can be set on all of
    // them. "GeometryInstance3D[][]" is an array of arrays: one small
    // array per enemy, because a model can be made of several meshes
    // (e.g. a modular character's body, clothes and hair).
    private GeometryInstance3D[][] _meshes;
    private int _count;

    // ---- Spatial grid (see BuildGrid). -----------------------------------
    // The ground is divided into square cells. Each frame we sort enemies by
    // cell, so an enemy only needs to check the enemies in the 3x3 cells
    // around it rather than all of them. That turns an N*N cost into
    // roughly N.
    //
    // Cells are mapped into a fixed-size table with a hash, so the grid
    // needs no fixed world size and never allocates. Two far-apart cells can
    // occasionally share a slot; that only costs a few extra distance checks.
    private const int GridTableSize = 4096;  // a power of two, see CellSlot
    private int[] _cellStart;    // where each slot's enemies begin in _cellEntries
    private int[] _cellCount;    // how many enemies are in each slot
    private int[] _cellEntries;  // enemy indices, grouped by slot
    private int[] _enemySlot;    // which slot each enemy is in this frame

    // A number unique to each enemy for its whole life, unlike its index
    // (which changes when others are removed). Lets code remember "I already
    // hit this one" across physics steps.
    private int[] _ids;
    private int _nextId;

    private double _animationTimeAccumulated;

    // ---- Corpses (see MaxCorpses). Empty arrays when the enemies have no
    // death animation, so nothing below needs a null check. ---------------
    private Node3D[] _corpses = Array.Empty<Node3D>();
    private DirectionalSprite3D[] _corpseSprites = Array.Empty<DirectionalSprite3D>();
    private float[] _corpseTime = Array.Empty<float>(); // seconds left; 0 = free
    private int _nextCorpse;                             // next one to use, wraps around

    // How many enemies are alive (read by the HUD).
    public int Count => _count;

    // How many enemies have been killed since the game started (read by
    // the HUD). "{ get; private set; }" is an auto-property: anyone can
    // read it, but only this class can change it.
    public int Kills { get; private set; }

    // The Target's health and XP, found in _Ready. Null if the target has
    // no PlayerStats child, in which case enemies just don't hurt it.
    private PlayerStats _targetStats;

    public override void _Ready()
    {
        if (EnemyScene == null || Target == null)
        {
            GD.PushWarning("EnemyManager needs EnemyScene and Target set.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        // GetNodeOrNull returns null instead of erroring when there's no
        // such child. Children already exist when _Ready runs, so this is
        // safe even if the Player's own _Ready hasn't run yet.
        _targetStats = Target.GetNodeOrNull<PlayerStats>("Stats");

        // Allocate every array once, at full size. "new" in _Ready is fine;
        // it's doing it every frame that we avoid.
        _positions = new Vector3[MaxEnemies];
        _separation = new Vector3[MaxEnemies];
        _health = new float[MaxEnemies];
        _dead = new bool[MaxEnemies];
        _flashTime = new float[MaxEnemies];
        _nodes = new Node3D[MaxEnemies];
        _animationPlayers = new AnimationPlayer[MaxEnemies];
        _sprites = new DirectionalSprite3D[MaxEnemies];
        _meshes = new GeometryInstance3D[MaxEnemies][];
        _enemySlot = new int[MaxEnemies];
        _ids = new int[MaxEnemies];
        _cellEntries = new int[MaxEnemies];
        _cellStart = new int[GridTableSize];
        _cellCount = new int[GridTableSize];

        PackedScene scene = ChooseEnemyScene();
        _flashMaterial = HitFlashMaterial;

        // Build the whole pool up front, hidden.
        for (int i = 0; i < MaxEnemies; i++)
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

            DirectionalSprite3D sprite = node.GetNodeOrNull<DirectionalSprite3D>("Model/Sprite");
            if (sprite != null && i == 0)
            {
                // Every enemy is the same scene, so one flash material made
                // from the first one serves them all.
                _flashMaterial = MakeSpriteMaterial(SpriteHitFlashShader, sprite);
            }

            _nodes[i] = node;
            _animationPlayers[i] = animationPlayer;
            _sprites[i] = sprite;
            _meshes[i] = FindMeshes(node);
        }

        BuildCorpses(scene);
    }

    /// <summary>
    /// Makes the corpse pool, if the enemies are sprites with a death
    /// animation. Corpses are ordinary copies of the enemy scene that only
    /// ever play "die".
    /// </summary>
    private void BuildCorpses(PackedScene scene)
    {
        DirectionalSprite3D sprite = _sprites[0];
        if (MaxCorpses <= 0 || sprite == null || sprite.SpriteFrames == null)
        {
            return;
        }
        // The sprite's animations are named per direction, e.g. "die_S".
        if (!sprite.SpriteFrames.HasAnimation(SpriteDirections.AnimationName(DieAnimation, 0)))
        {
            return;
        }

        _corpses = new Node3D[MaxCorpses];
        _corpseSprites = new DirectionalSprite3D[MaxCorpses];
        _corpseTime = new float[MaxCorpses];
        // One material for every corpse; each one's fade amount travels in
        // its own Modulate alpha (see corpse_fade.gdshader).
        Material fadeMaterial = MakeSpriteMaterial(CorpseFadeShader, sprite);
        for (int c = 0; c < MaxCorpses; c++)
        {
            Node3D node = scene.Instantiate<Node3D>();
            node.Visible = false;
            AddChild(node);
            _corpses[c] = node;
            _corpseSprites[c] = node.GetNode<DirectionalSprite3D>("Model/Sprite");
            _corpseSprites[c].MaterialOverride = fadeMaterial;
        }
    }

    /// <summary>
    /// Puts a corpse where enemy i is standing and starts its death
    /// animation, facing the same way the enemy did.
    /// </summary>
    private void StartCorpse(int i)
    {
        Camera3D camera = GetCamera();
        if (_corpses.Length == 0 || camera == null)
        {
            return;
        }

        int c = _nextCorpse;
        // "%" (remainder) wraps the index back to 0 after the last corpse.
        _nextCorpse = (_nextCorpse + 1) % _corpses.Length;

        Node3D node = _corpses[c];
        DirectionalSprite3D sprite = _corpseSprites[c];
        node.Transform = _nodes[i].Transform;
        node.Visible = true;
        // Fully there again (a reused corpse may have been mid-fade).
        sprite.Modulate = Colors.White;

        // PlayFacing skips the call when this corpse last played "die" in
        // the same direction, and that run has already finished. So rewind
        // to the first frame and start it explicitly. Play() with no name
        // plays the current animation.
        sprite.PlayFacing(DieAnimation, Target.GlobalPosition - _positions[i], camera);
        sprite.Frame = 0;
        sprite.Play();

        // How long the animation runs: frames / (frames per second). The
        // importer gives every frame of an animation the same length.
        SpriteFrames frames = sprite.SpriteFrames;
        StringName animation = sprite.Animation;
        double fps = Math.Max(frames.GetAnimationSpeed(animation), 1.0);
        float length = (float)(frames.GetFrameCount(animation) / fps);
        _corpseTime[c] = length + CorpseLinger + CorpseFadeTime;
    }

    /// <summary>
    /// Counts down every corpse, dithers it away during its last
    /// CorpseFadeTime seconds, and hides it when its time is up.
    /// </summary>
    private void UpdateCorpses(float delta)
    {
        for (int c = 0; c < _corpseTime.Length; c++)
        {
            if (_corpseTime[c] <= 0.0f)
            {
                continue;
            }

            _corpseTime[c] -= delta;
            if (_corpseTime[c] <= 0.0f)
            {
                _corpseTime[c] = 0.0f;
                _corpses[c].Visible = false;
            }
            else if (_corpseTime[c] < CorpseFadeTime)
            {
                // Time left as a fraction of the fade: 1 at its start,
                // falling to 0. Color is a struct (a plain value, like
                // Vector3), so "new Color" here allocates nothing.
                float visible = _corpseTime[c] / CorpseFadeTime;
                _corpseSprites[c].Modulate = new Color(1.0f, 1.0f, 1.0f, visible);
            }
        }
    }

    /// <summary>
    /// EnemyScene, unless it's a sprite enemy whose frames are missing; then
    /// FallbackScene. Instances one test enemy to find out, which is the
    /// simplest way to see exactly what the scene's sprite would load.
    /// </summary>
    private PackedScene ChooseEnemyScene()
    {
        if (FallbackScene == null)
        {
            return EnemyScene;
        }

        Node3D test = EnemyScene.Instantiate<Node3D>();
        AddChild(test); // runs DirectionalSprite3D._Ready, which loads the frames
        DirectionalSprite3D sprite = test.GetNodeOrNull<DirectionalSprite3D>("Model/Sprite");
        bool missingFrames = sprite != null && sprite.SpriteFrames == null;
        RemoveChild(test);
        // Free() deletes the node right away. (QueueFree() would wait until
        // the end of the frame; either is fine for a one-off in _Ready.)
        test.Free();

        return missingFrames ? FallbackScene : EnemyScene;
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

    public override void _PhysicsProcess(double delta)
    {
        // Enemies killed during the previous step are removed first, while
        // nothing else is looking at the arrays (see Damage).
        RemoveDead();
        UpdatePopulation();
        if (_count == 0)
        {
            return;
        }

        BuildGrid();
        ComputeSeparation();
        MoveEnemies((float)delta);

        // Rebuild the grid from the positions enemies have just moved to,
        // so HitTest (called by ProjectileManager later in this same step)
        // looks in the right cells. Building it is cheap: two passes over
        // the enemies plus one over the table.
        BuildGrid();
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
    /// Counts down the hit flash on enemies that were hit recently and
    /// ends it when the time runs out. Enemies that aren't flashing are
    /// skipped after one comparison, so a swarm that isn't being hit costs
    /// almost nothing here.
    /// </summary>
    private void UpdateHitFlash(float delta)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_flashTime[i] <= 0.0f)
            {
                continue;
            }

            _flashTime[i] -= delta;
            if (_flashTime[i] <= 0.0f)
            {
                _flashTime[i] = 0.0f;
                SetHitFlash(i, false);
            }
        }
    }

    /// <summary>
    /// Turns the white flash on or off for all of enemy i's meshes.
    ///
    /// MaterialOverride replaces the mesh's own material(s) while it is set;
    /// setting it back to null brings the normal look back. Swapping in one
    /// shared material works for any number of enemies. (We first tried a
    /// per-mesh "instance uniform", but phones like the Adreno 642L only
    /// have room for about 256 of those in the whole game.)
    /// </summary>
    private void SetHitFlash(int i, bool on)
    {
        Material material = on ? _flashMaterial : null;
        GeometryInstance3D[] meshes = _meshes[i];
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
        // drawn at a low rate, so they look stepped without our help.
        if (_count == 0 || _animationPlayers[0] == null)
        {
            return;
        }

        if (AnimationFps <= 0.0f)
        {
            for (int i = 0; i < _count; i++)
            {
                _animationPlayers[i].Advance(delta);
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
        for (int i = 0; i < _count; i++)
        {
            _animationPlayers[i].Advance(advance);
        }
    }

    /// <summary>
    /// Spawns or removes a few enemies to approach TargetCount.
    /// </summary>
    private void UpdatePopulation()
    {
        // Math.Clamp keeps the value inside [min, max].
        int wanted = Math.Clamp(TargetCount, 0, MaxEnemies);

        for (int n = 0; n < MaxSpawnsPerFrame && _count < wanted; n++)
        {
            Spawn();
        }
        for (int n = 0; n < MaxSpawnsPerFrame && _count > wanted; n++)
        {
            Despawn(_count - 1);
        }
    }

    /// <summary>
    /// Takes the next free pool slot and places an enemy on the spawn ring.
    /// </summary>
    private void Spawn()
    {
        int i = _count;
        _count++;

        _positions[i] = RandomPointOnSpawnRing();
        // Pool slots are reused, so reset everything a previous enemy in
        // this slot may have left behind.
        _health[i] = MaxHealth;
        _dead[i] = false;
        _ids[i] = _nextId;
        _nextId++;
        // A previous enemy in this slot may have died mid-flash.
        if (_flashTime[i] > 0.0f)
        {
            _flashTime[i] = 0.0f;
            SetHitFlash(i, false);
        }
        _nodes[i].Visible = true;

        // Start each enemy at a random point in its cycle so they don't all
        // hop in perfect unison.
        DirectionalSprite3D sprite = _sprites[i];
        if (sprite != null)
        {
            Camera3D camera = GetCamera();
            if (camera != null && sprite.SpriteFrames != null)
            {
                sprite.PlayFacing(WalkAnimation, Target.GlobalPosition - _positions[i], camera);
                int frameCount = sprite.SpriteFrames.GetFrameCount(sprite.Animation);
                sprite.Frame = Random.Shared.Next(frameCount);
            }
            return;
        }

        AnimationPlayer animationPlayer = _animationPlayers[i];
        animationPlayer.Play(MoveAnimation);
        // Random.Shared is .NET's ready-made random generator, so we don't
        // need to create (allocate) our own. (We read the length from the
        // clip itself: in Manual mode, Play() hasn't fully started the
        // animation yet, so CurrentAnimationLength would still be 0.)
        double length = animationPlayer.GetAnimation(MoveAnimation).Length;
        animationPlayer.Seek(Random.Shared.NextDouble() * length, true);
    }

    /// <summary>
    /// Ground position of live enemy i. Like every enemy index, only valid
    /// during the current physics step.
    /// </summary>
    public Vector3 GetPosition(int i) => _positions[i];

    /// <summary>
    /// The lifelong ID of live enemy i. Unlike the index it stays the same
    /// across physics steps, so it can be stored. No two enemies ever share
    /// one (it would take billions of spawns to wrap around).
    /// </summary>
    public int GetId(int i) => _ids[i];

    /// <summary>
    /// Finds every live enemy overlapping the circle at point with the given
    /// radius (on the ground, height ignored) and writes their indices into
    /// results. Returns how many it found, at most results.Length.
    ///
    /// The caller owns the results array and reuses it every call, so a
    /// query allocates nothing. Indices are only valid during the current
    /// physics step, so use them right away.
    /// </summary>
    public int QueryCircle(Vector3 point, float radius, int[] results)
    {
        // A circle is a ring with no hole in the middle.
        return QueryRing(point, 0.0f, radius, results);
    }

    /// <summary>
    /// Like QueryCircle, but leaves out enemies that already overlapped the
    /// smaller circle of innerRadius. Used by an expanding wave: each step
    /// it asks only for the enemies its edge has newly reached, so it hits
    /// each one once and a crowd inside it can't fill up the results.
    /// </summary>
    public int QueryRing(Vector3 point, float innerRadius, float outerRadius, int[] results)
    {
        int found = 0;
        float reach = outerRadius + EnemyRadius;
        float reachSquared = reach * reach;
        // Enemies closer than this were already touching the inner circle.
        // With no hole (innerRadius 0) nothing is excluded.
        float innerReach = innerRadius > 0.0f ? innerRadius + EnemyRadius : 0.0f;
        float innerReachSquared = innerReach * innerReach;
        int cellRange = Mathf.CeilToInt(reach / SeparationRadius);
        int cellX = CellCoord(point.X);
        int cellZ = CellCoord(point.Z);

        for (int dz = -cellRange; dz <= cellRange; dz++)
        {
            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                int slot = CellSlot(cellX + dx, cellZ + dz);
                int start = _cellStart[slot];
                int end = start + _cellCount[slot];

                for (int e = start; e < end; e++)
                {
                    int j = _cellEntries[e];
                    if (_dead[j])
                    {
                        continue;
                    }

                    float offsetX = _positions[j].X - point.X;
                    float offsetZ = _positions[j].Z - point.Z;
                    float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                    if (distanceSquared >= reachSquared || distanceSquared < innerReachSquared)
                    {
                        continue;
                    }

                    // Two far-apart cells can share a slot in the hash table
                    // (see CellSlot), so a wide search can visit the same
                    // slot twice and meet the same enemy again. Skip repeats.
                    // The list is short, so a plain search is fine.
                    if (Array.IndexOf(results, j, 0, found) >= 0)
                    {
                        continue;
                    }

                    results[found] = j;
                    found++;
                    if (found == results.Length)
                    {
                        return found; // buffer full
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Returns the index of the live enemy closest to point (on the ground,
    /// height ignored) that is no further than maxRange, or -1 if none is.
    /// Like every enemy index, only valid during the current physics step.
    ///
    /// A plain loop over every enemy: a few hundred distance checks are
    /// cheap, and unlike the grid it works at any range.
    /// </summary>
    public int FindClosest(Vector3 point, float maxRange)
    {
        int closest = -1;
        // Compare squared distances: same order as real distances, without
        // a square root per enemy.
        float bestSquared = maxRange * maxRange;

        for (int i = 0; i < _count; i++)
        {
            if (_dead[i])
            {
                continue;
            }

            float offsetX = _positions[i].X - point.X;
            float offsetZ = _positions[i].Z - point.Z;
            float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
            if (distanceSquared < bestSquared)
            {
                bestSquared = distanceSquared;
                closest = i;
            }
        }

        return closest;
    }

    /// <summary>
    /// Finds the live enemies closest to point (on the ground) within
    /// maxRange, up to results.Length of them, and writes their indices into
    /// results nearest first. Returns how many it found. Like the other
    /// queries, the caller reuses its own results array, so nothing is
    /// allocated.
    ///
    /// It keeps a small sorted list while looping over every enemy: a new
    /// enemy that beats the furthest one kept is slid into its place. Cheap
    /// for a handful of results (a weapon's projectile count).
    /// </summary>
    public int FindClosest(Vector3 point, float maxRange, int[] results)
    {
        int found = 0;
        float maxSquared = maxRange * maxRange;
        // Squared distance of each enemy in results, in the same order.
        // stackalloc puts a small array on the stack instead of the heap,
        // so it's free to make and needs no garbage collection. Span<T> is
        // the array-like view of it.
        Span<float> distances = stackalloc float[Mathf.Min(results.Length, 64)];
        int capacity = distances.Length;
        if (capacity == 0)
        {
            return 0;
        }

        for (int i = 0; i < _count; i++)
        {
            if (_dead[i])
            {
                continue;
            }

            float offsetX = _positions[i].X - point.X;
            float offsetZ = _positions[i].Z - point.Z;
            float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
            if (distanceSquared >= maxSquared)
            {
                continue;
            }
            // List full and this one is no closer than the furthest kept.
            if (found == capacity && distanceSquared >= distances[found - 1])
            {
                continue;
            }

            // Shift the further ones down one slot (dropping the last when
            // full) until the new enemy's place is reached.
            int slot = found < capacity ? found++ : found - 1;
            while (slot > 0 && distances[slot - 1] > distanceSquared)
            {
                distances[slot] = distances[slot - 1];
                results[slot] = results[slot - 1];
                slot--;
            }
            distances[slot] = distanceSquared;
            results[slot] = i;
        }

        return found;
    }

    /// <summary>
    /// Returns the index of a live enemy overlapping the circle at point with
    /// the given radius (measured on the ground, height is ignored), or -1
    /// if there is none. Only valid during the current physics step:
    /// enemy indices change when enemies are removed.
    /// </summary>
    public int HitTest(Vector3 point, float radius)
    {
        if (_count == 0)
        {
            return -1;
        }

        // The two circles overlap when their centres are closer than the
        // two radii added together.
        float reach = radius + EnemyRadius;
        float reachSquared = reach * reach;

        // How many cells out from the point's cell we have to look. Cells
        // are SeparationRadius wide, so with the default sizes this is 1
        // (the 3x3 block), but a big projectile would need more.
        int cellRange = Mathf.CeilToInt(reach / SeparationRadius);
        int cellX = CellCoord(point.X);
        int cellZ = CellCoord(point.Z);

        for (int dz = -cellRange; dz <= cellRange; dz++)
        {
            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                int slot = CellSlot(cellX + dx, cellZ + dz);
                int start = _cellStart[slot];
                int end = start + _cellCount[slot];

                for (int e = start; e < end; e++)
                {
                    int j = _cellEntries[e];
                    // Killed earlier this step but not removed yet: it is
                    // already invisible, so don't let it soak up shots.
                    if (_dead[j])
                    {
                        continue;
                    }

                    float offsetX = _positions[j].X - point.X;
                    float offsetZ = _positions[j].Z - point.Z;
                    if (offsetX * offsetX + offsetZ * offsetZ < reachSquared)
                    {
                        return j;
                    }
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Deals damage to enemy i (an index from HitTest, used in the same
    /// physics step). An enemy whose health drops to 0 is marked dead but
    /// stays in the arrays until the start of the next step. Removing it
    /// right away would swap another enemy into its slot and leave the grid
    /// pointing at the wrong enemies while projectiles are still being
    /// checked against it.
    /// </summary>
    public void Damage(int i, float amount)
    {
        // Ignore bad indices and enemies that are already dead, so a
        // second hit in the same step can't count a kill twice.
        if (i < 0 || i >= _count || _dead[i])
        {
            return;
        }

        _health[i] -= amount;

        // Start (or restart) the white flash; UpdateHitFlash ends it. Only
        // swap the material if it isn't already flashing.
        if (HitFlashMaterial != null && HitFlashDuration > 0.0f)
        {
            if (_flashTime[i] <= 0.0f)
            {
                SetHitFlash(i, true);
            }
            _flashTime[i] = HitFlashDuration;
        }

        if (_health[i] <= 0.0f)
        {
            _dead[i] = true;
            // Hide it now so it vanishes on the frame it was killed, not
            // one step later. A corpse takes its place for the death
            // animation (sprite enemies only; others just vanish).
            _nodes[i].Visible = false;
            StartCorpse(i);
            Kills++;

            // "?." calls Drop only if XpManager is set (not null).
            XpManager?.Drop(_positions[i], XpPerKill);
        }
    }

    /// <summary>
    /// Removes every enemy marked dead by Damage. With the stress-test
    /// TargetCount, UpdatePopulation then spawns replacements on the ring.
    /// </summary>
    private void RemoveDead()
    {
        // Backwards, for the same reason as the projectile loop: Despawn
        // moves the last enemy into the freed slot, and going from the end
        // that enemy has already been checked.
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_dead[i])
            {
                Despawn(i);
            }
        }
    }

    /// <summary>
    /// Removes enemy i. The last alive enemy is moved into its slot, so the
    /// alive enemies always stay packed at the start of the arrays ("swap
    /// remove"). Fast, but it changes the order: never keep an enemy index
    /// around across frames.
    ///
    /// Private, because removing an enemy in the middle of a physics step
    /// would break the grid. Other code kills enemies through Damage.
    /// </summary>
    private void Despawn(int i)
    {
        int last = _count - 1;

        // The node itself is parked and hidden. We swap the node and its
        // AnimationPlayer along with the data so every array stays lined up.
        Node3D deadNode = _nodes[i];
        AnimationPlayer deadPlayer = _animationPlayers[i];
        DirectionalSprite3D deadSprite = _sprites[i];
        GeometryInstance3D[] deadMeshes = _meshes[i];
        float deadFlash = _flashTime[i];
        deadNode.Visible = false;

        _positions[i] = _positions[last];
        _ids[i] = _ids[last];
        _health[i] = _health[last];
        _dead[i] = _dead[last];
        _flashTime[i] = _flashTime[last];
        _nodes[i] = _nodes[last];
        _animationPlayers[i] = _animationPlayers[last];
        _sprites[i] = _sprites[last];
        _meshes[i] = _meshes[last];

        // The flash belongs to the node, so it travels with it: the dead
        // node keeps its flash time in the parked slot and Spawn clears it.
        _flashTime[last] = deadFlash;
        _nodes[last] = deadNode;
        _animationPlayers[last] = deadPlayer;
        _sprites[last] = deadSprite;
        _meshes[last] = deadMeshes;
        _count--;
    }

    /// <summary>
    /// Sorts the alive enemies into grid cells (a "counting sort"): count
    /// how many land in each slot, work out where each slot's run starts,
    /// then drop every enemy into place.
    /// </summary>
    private void BuildGrid()
    {
        // Array.Clear resets every entry to 0 without allocating.
        Array.Clear(_cellCount);

        for (int i = 0; i < _count; i++)
        {
            int slot = CellSlot(CellCoord(_positions[i].X), CellCoord(_positions[i].Z));
            _enemySlot[i] = slot;
            _cellCount[slot]++;
        }

        int running = 0;
        for (int s = 0; s < GridTableSize; s++)
        {
            _cellStart[s] = running;
            running += _cellCount[s];
        }

        // Reuse _cellCount as a "how many placed so far" counter.
        Array.Clear(_cellCount);
        for (int i = 0; i < _count; i++)
        {
            int slot = _enemySlot[i];
            _cellEntries[_cellStart[slot] + _cellCount[slot]] = i;
            _cellCount[slot]++;
        }
    }

    /// <summary>
    /// For every enemy, adds up a push away from each neighbour that is too
    /// close. The closer the neighbour, the stronger the push.
    /// </summary>
    private void ComputeSeparation()
    {
        float radiusSquared = SeparationRadius * SeparationRadius;

        for (int i = 0; i < _count; i++)
        {
            Vector3 position = _positions[i];
            Vector3 push = Vector3.Zero;
            int cellX = CellCoord(position.X);
            int cellZ = CellCoord(position.Z);

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int slot = CellSlot(cellX + dx, cellZ + dz);
                    int start = _cellStart[slot];
                    int end = start + _cellCount[slot];

                    for (int e = start; e < end; e++)
                    {
                        int j = _cellEntries[e];
                        if (j == i)
                        {
                            continue;
                        }

                        Vector3 away = position - _positions[j];
                        away.Y = 0.0f;
                        float distanceSquared = away.LengthSquared();
                        if (distanceSquared >= radiusSquared)
                        {
                            continue;
                        }

                        if (distanceSquared < 0.000001f)
                        {
                            // Exactly on top of each other: there is no
                            // "away", so pick a direction from the indices.
                            away = new Vector3(i < j ? 1.0f : -1.0f, 0.0f, 0.0f);
                            distanceSquared = 1.0f;
                        }

                        float distance = Mathf.Sqrt(distanceSquared);
                        // 1 when touching, 0 at the edge of the radius.
                        float strength = 1.0f - distance / SeparationRadius;
                        push += away / distance * strength;
                    }
                }
            }

            _separation[i] = push;
        }
    }

    /// <summary>
    /// Steers every enemy toward the target, applies separation, and copies
    /// the result to the enemy's node.
    /// </summary>
    private void MoveEnemies(float delta)
    {
        Vector3 targetPosition = Target.GlobalPosition;
        Camera3D camera = GetCamera();
        float despawnSquared = DespawnDistance * DespawnDistance;
        bool touching = false;

        for (int i = 0; i < _count; i++)
        {
            Vector3 position = _positions[i];
            Vector3 toTarget = targetPosition - position;
            toTarget.Y = 0.0f;

            // Left far behind: put it back on the ring around the player
            // instead of letting it trudge all the way back.
            if (toTarget.LengthSquared() > despawnSquared)
            {
                _positions[i] = RandomPointOnSpawnRing();
                continue;
            }

            // 0 at StopDistance, rising to 1 (full speed) ArriveSlowdown
            // metres further out.
            float distance = toTarget.Length();
            if (distance < ContactRange)
            {
                touching = true;
            }
            float chase = Mathf.Clamp((distance - StopDistance) / ArriveSlowdown, 0.0f, 1.0f);

            Vector3 velocity = toTarget.Normalized() * (Speed * chase) + _separation[i] * SeparationStrength;
            position += velocity * delta;
            position.Y = 0.0f;
            _positions[i] = position;

            // Face the direction of travel (the model's front is +Z, see
            // Player.FaceDirection). Building the whole transform and
            // setting it once is one call into the engine instead of two.
            float yaw = Mathf.Atan2(velocity.X, velocity.Z);
            _nodes[i].Transform = new Transform3D(new Basis(Vector3.Up, yaw), position);

            // A sprite is a billboard, so turning the node doesn't show; it
            // picks which drawn direction to show instead. It faces the
            // player rather than its exact velocity, because the crowd's
            // pushing makes velocity jitter, and the sprite would flicker
            // between directions. PlayFacing does nothing if neither the
            // animation nor the direction changed, so this is cheap.
            DirectionalSprite3D sprite = _sprites[i];
            if (sprite != null && camera != null)
            {
                sprite.PlayFacing(WalkAnimation, toTarget, camera);
            }
        }

        // One touch is enough to cost a heart. TakeDamage ignores it while
        // the player is still invulnerable from the previous hit, so it is
        // fine to call it every step something is touching.
        if (touching && _targetStats != null)
        {
            _targetStats.TakeDamage(ContactDamage);
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

    private Vector3 RandomPointOnSpawnRing()
    {
        float angle = Random.Shared.NextSingle() * Mathf.Tau;
        Vector3 center = Target.GlobalPosition;
        return new Vector3(
            center.X + Mathf.Cos(angle) * SpawnDistance,
            0.0f,
            center.Z + Mathf.Sin(angle) * SpawnDistance);
    }

    /// <summary>
    /// World coordinate -> grid cell number along one axis. Cells are as wide
    /// as the separation radius, so any neighbour within that radius is in
    /// the 3x3 cells around an enemy.
    /// </summary>
    private int CellCoord(float worldCoord)
    {
        return Mathf.FloorToInt(worldCoord / SeparationRadius);
    }

    /// <summary>
    /// Maps a cell (x, z) to a slot in the fixed-size grid table.
    /// </summary>
    private static int CellSlot(int x, int z)
    {
        // Multiply by two large primes and mix them, which scatters nearby
        // cells across the table. "unchecked" lets the multiplication wrap
        // around on overflow instead of throwing an error. The "& (size - 1)"
        // keeps the result in range; it works like "% size" but is only
        // correct because GridTableSize is a power of two.
        unchecked
        {
            int hash = (x * 73856093) ^ (z * 19349663);
            return hash & (GridTableSize - 1);
        }
    }
}
