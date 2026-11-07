using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns and updates every projectile in flight, whatever fired it.
///
/// Same design as EnemyManager: projectile data lives in plain C# arrays
/// and everything moves in one loop. Weapons only call Spawn(); after that
/// the manager moves the projectile, checks it against the enemies, and
/// returns it to the pool when it hits one or its lifetime runs out.
///
/// Projectiles are pooled: every node is created once in _Ready and reused.
///
/// The manager draws its projectiles at their own local positions, so keep
/// this node at the origin with no rotation or scale.
/// </summary>
public partial class ProjectileManager : Node3D
{
    // Size of the pool: the most projectiles that can be in flight at
    // once. If it is full, Spawn() skips the shot instead of growing.
    [Export] public int MaxProjectiles = 512;

    // The manager that owns the enemies projectiles can hit. It is set in
    // main.tscn. Without it projectiles still fly, they just pass through
    // everything.
    //
    // The field has the same name as its type, which C# allows: in
    // "EnemyManager.HitTest(...)" the compiler sees that a field called
    // EnemyManager exists and uses it, so this calls the method on the one
    // manager object in the scene.
    [Export] public EnemyManager EnemyManager;

    // Godot runs _PhysicsProcess on nodes in order of this priority, lowest
    // first (EnemyManager keeps the default 0). Hit checks have to see
    // where enemies are *after* they moved and rebuilt their grid this
    // step, so we run later. Setting it in code makes the order explicit
    // instead of depending on where the nodes sit in the scene tree.
    private const int PhysicsPriorityAfterEnemies = 10;

    // ---- Projectile data. Index i in every array is the same projectile.
    // Only the first _count entries are in flight; the rest are spare
    // pool slots. ---------------------------------------------------------
    private Vector3[] _positions;
    private Vector3[] _velocities;   // metres per second, direction * speed
    private float[] _timeLeft;       // seconds until it disappears
    private float[] _damage;         // copied now, used once damage exists
    private float[] _radius;         // hit radius on the ground
    private MeshInstance3D[] _nodes;
    private int _count;

    /// <summary>How many projectiles are in flight (read by the HUD).</summary>
    public int Count => _count;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;

        _positions = new Vector3[MaxProjectiles];
        _velocities = new Vector3[MaxProjectiles];
        _timeLeft = new float[MaxProjectiles];
        _damage = new float[MaxProjectiles];
        _radius = new float[MaxProjectiles];
        _nodes = new MeshInstance3D[MaxProjectiles];

        // Build the whole pool up front, hidden and without a mesh. Each
        // node gets its mesh from the definition when it is spawned, so one
        // pool serves every kind of projectile.
        for (int i = 0; i < MaxProjectiles; i++)
        {
            // "new()" without a type name: C# takes the type from the
            // variable on the left ("target-typed new").
            MeshInstance3D node = new()
            {
                Visible = false,
                // Tiny and fast: shadows would cost more than they add.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(node);
            _nodes[i] = node;
        }
    }

    /// <summary>
    /// Launches one projectile of the given kind from position, flying in
    /// direction (which should be length 1). Returns false if the pool is
    /// full and the shot was skipped.
    /// </summary>
    public bool Spawn(ProjectileDefinition definition, Vector3 position, Vector3 direction)
    {
        if (_count >= MaxProjectiles || definition == null)
        {
            return false;
        }

        int i = _count;
        _count++;

        // Copy what the projectile needs out of the definition. From here
        // on the projectile only uses these numbers, so weapons could tweak
        // them per shot (upgrades) without changing the shared definition.
        _positions[i] = position;
        _velocities[i] = direction * definition.Speed;
        _timeLeft[i] = definition.Lifetime;
        _damage[i] = definition.Damage;
        _radius[i] = definition.Radius;

        MeshInstance3D node = _nodes[i];
        // Only swap the mesh when it differs from what this pooled node
        // last showed. Comparing references is cheap; setting the mesh is a
        // call into the engine.
        if (node.Mesh != definition.Mesh)
        {
            node.Mesh = definition.Mesh;
        }
        node.Position = position;
        node.Visible = true;
        return true;
    }

    // Movement in _PhysicsProcess, like the enemies, so that hit checks
    // compare positions from the same fixed time step.
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Loop backwards, because Despawn moves the last projectile into
        // the freed slot. Going from the end, that moved projectile has
        // already been updated this frame, so nothing is skipped or
        // updated twice.
        for (int i = _count - 1; i >= 0; i--)
        {
            _timeLeft[i] -= dt;
            if (_timeLeft[i] <= 0.0f)
            {
                Despawn(i);
                continue;
            }

            _positions[i] += _velocities[i] * dt;

            // Hits are only checked at the new position, not along the path
            // since the last step. That is fine while a projectile moves less
            // per step than its hit size: a shard moves about 0.23 m per step
            // (14 m/s at 60 steps/s) against a 0.4 m reach. Much faster
            // projectiles would need a swept test.
            if (EnemyManager != null)
            {
                int hit = EnemyManager.HitTest(_positions[i], _radius[i]);
                if (hit >= 0)
                {
                    // For now a hit just stops the projectile; the enemy is
                    // untouched. Damage comes next, and pierce would mean
                    // counting down here instead of despawning.
                    Despawn(i);
                    continue;
                }
            }

            _nodes[i].Position = _positions[i];
        }
    }

    /// <summary>
    /// Removes projectile i with a "swap remove": the last projectile in
    /// flight moves into slot i, so the live ones stay packed at the start
    /// of the arrays. Never keep a projectile index across frames.
    /// </summary>
    private void Despawn(int i)
    {
        int last = _count - 1;

        MeshInstance3D deadNode = _nodes[i];
        deadNode.Visible = false;

        _positions[i] = _positions[last];
        _velocities[i] = _velocities[last];
        _timeLeft[i] = _timeLeft[last];
        _damage[i] = _damage[last];
        _radius[i] = _radius[last];
        _nodes[i] = _nodes[last];

        // Park the hidden node in the freed slot at the end, so the pool
        // never loses a node.
        _nodes[last] = deadNode;
        _count--;
    }
}
