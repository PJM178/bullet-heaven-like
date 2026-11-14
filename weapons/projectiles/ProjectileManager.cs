using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns, moves and draws every projectile in flight, whatever fired it.
/// This is the only place projectiles move, so everything a projectile has
/// to respect is handled once, here: walls stop it, it hits enemies (each
/// one once, up to its Pierce), props stop it, a returning one flies back.
///
/// Same design as EnemyManager: projectile data lives in plain C# arrays
/// and everything moves in one loop. Weapons only call <see cref="Spawn"/>
/// (usually through a <see cref="ProjectileWeapon"/>); what a projectile
/// does comes from its <see cref="ProjectileDefinition"/>.
///
/// Projectiles are pooled: every node is created once in _Ready and reused.
/// There are two node pools, one for mesh projectiles and one for picture
/// (texture) projectiles; each node is set up from its definition when it
/// is spawned.
///
/// The manager draws its projectiles at their own local positions, so keep
/// this node at the origin with no rotation or scale.
/// </summary>
public partial class ProjectileManager : Node3D
{
    /// <summary>The group this manager is in, so weapons added at runtime
    /// can find it without a NodePath.</summary>
    public const string Group = "projectile_manager";

    // The most projectiles that can be in flight at once. If it is full,
    // Spawn() skips the shot instead of growing. Each of the two node pools
    // is this big too.
    [Export] public int MaxProjectiles = 256;

    // The manager that owns the enemies projectiles can hit. Without it
    // projectiles still fly, they just pass through everything.
    //
    // The field has the same name as its type, which C# allows: in
    // "EnemyManager.QueryCircle(...)" the compiler sees that a field called
    // EnemyManager exists and uses it, so this calls the method on the one
    // manager object in the scene.
    [Export] public EnemyManager EnemyManager;

    // Props (barrels, ...) stop projectiles and take their damage. Optional.
    [Export] public BreakableManager BreakableManager;

    // Walls stop projectiles. Optional: without it they fly through.
    [Export] public WorldObstacles Obstacles;

    // Godot runs _PhysicsProcess on nodes in order of this priority, lowest
    // first (EnemyManager keeps the default 0). Hit checks have to see
    // where enemies are *after* they moved and rebuilt their grid this
    // step, so we run later. Setting it in code makes the order explicit
    // instead of depending on where the nodes sit in the scene tree.
    private const int PhysicsPriorityAfterEnemies = 10;

    // Enemies a piercing projectile remembers having hit, so it hits each
    // one once. One that passes more than this many forgets the oldest,
    // which is fine: by then it is far away from them.
    private const int HitMemory = 16;

    // A returning projectile that can't catch up with whoever threw it
    // gives up after flying this many times its Range.
    private const float MaxTravelInRanges = 3.0f;

    // How close a returning projectile must get to be caught, in metres
    // (on top of one step's movement).
    private const float CatchDistance = 0.3f;

    // ---- Projectile data. Index i in every array is the same projectile.
    // Only the first _count entries are in flight. -------------------------
    private ProjectileDefinition[] _definitions;
    private Vector3[] _positions;    // on the ground; drawn Height above it
    private Vector3[] _directions;   // flat, length 1; where it is heading
    private float[] _travelled;      // metres flown so far
    private float[] _spin;           // radians, for spinning pictures
    private int[] _hitsLeft;         // enemies it may still hit; -1 = no limit
    private bool[] _returning;       // on its way back
    private Node3D[] _returnTo;      // who it flies back to (Returns)
    private int[] _hitIds;           // HitMemory enemy ids per projectile
    private int[] _hitCount;         // how many of its ids are filled
    private int[] _nextHitSlot;      // where its next id goes (wraps)
    private Node3D[] _nodes;         // its node, from one of the pools
    private int _count;

    // ---- Node pools, used like stacks: the first ...FreeCount are free.
    private MeshInstance3D[] _freeMeshes;
    private int _freeMeshCount;
    private Sprite3D[] _freeSprites;
    private int _freeSpriteCount;

    // Overlapping enemies found by one step's hit query.
    private readonly int[] _query = new int[32];

    private Camera3D _camera;

    /// <summary>How many projectiles are in flight (read by the HUD).</summary>
    public int Count => _count;

    public override void _EnterTree()
    {
        // Before any _Ready, so weapons always find us in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;

        _definitions = new ProjectileDefinition[MaxProjectiles];
        _positions = new Vector3[MaxProjectiles];
        _directions = new Vector3[MaxProjectiles];
        _travelled = new float[MaxProjectiles];
        _spin = new float[MaxProjectiles];
        _hitsLeft = new int[MaxProjectiles];
        _returning = new bool[MaxProjectiles];
        _returnTo = new Node3D[MaxProjectiles];
        _hitIds = new int[MaxProjectiles * HitMemory];
        _hitCount = new int[MaxProjectiles];
        _nextHitSlot = new int[MaxProjectiles];
        _nodes = new Node3D[MaxProjectiles];

        // Both pools up front, hidden, without a mesh or picture: each node
        // gets those from the definition when it is spawned, so one pool
        // serves every kind of projectile that looks that way.
        _freeMeshes = new MeshInstance3D[MaxProjectiles];
        _freeSprites = new Sprite3D[MaxProjectiles];
        for (int n = 0; n < MaxProjectiles; n++)
        {
            // "new()" without a type name: C# takes the type from the
            // variable on the left ("target-typed new").
            MeshInstance3D mesh = new()
            {
                Visible = false,
                // Small and fast: shadows would cost more than they add.
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mesh);
            _freeMeshes[n] = mesh;

            Sprite3D sprite = new()
            {
                Visible = false,
                // Hard edges and no smoothing, like the character sprites.
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(sprite);
            _freeSprites[n] = sprite;
        }
        _freeMeshCount = MaxProjectiles;
        _freeSpriteCount = MaxProjectiles;
    }

    /// <summary>
    /// Launches one projectile of the given kind from position (on the
    /// ground; it is drawn the definition's Height above it), flying in
    /// direction (flat, length 1). A projectile that Returns flies back to
    /// <paramref name="returnTo"/> (usually the player who threw it).
    /// Returns false if the pool is full and the shot was skipped.
    /// </summary>
    public bool Spawn(ProjectileDefinition definition, Vector3 position, Vector3 direction,
                      Node3D returnTo = null)
    {
        if (_count >= MaxProjectiles || definition == null)
        {
            return false;
        }

        Node3D node = TakeNode(definition);
        if (node == null)
        {
            return false;
        }

        int i = _count;
        _count++;
        _definitions[i] = definition;
        _positions[i] = new Vector3(position.X, 0.0f, position.Z);
        _directions[i] = direction;
        _travelled[i] = 0.0f;
        _spin[i] = 0.0f;
        // Pierce 0 means one hit; -1 means no limit.
        _hitsLeft[i] = definition.Pierce < 0 ? -1 : definition.Pierce + 1;
        _returning[i] = false;
        _returnTo[i] = returnTo;
        _hitCount[i] = 0;
        _nextHitSlot[i] = 0;
        _nodes[i] = node;
        node.Visible = true;
        PlaceNode(i);
        return true;
    }

    // Takes a free node of the right kind and dresses it for the definition.
    // Null when that pool is empty.
    private Node3D TakeNode(ProjectileDefinition definition)
    {
        if (definition.Texture != null)
        {
            if (_freeSpriteCount == 0)
                return null;
            _freeSpriteCount--;
            Sprite3D sprite = _freeSprites[_freeSpriteCount];
            // Only swap the picture when it differs from what this pooled
            // node last showed: comparing references is cheap, setting it is
            // a call into the engine.
            if (sprite.Texture != definition.Texture)
            {
                sprite.Texture = definition.Texture;
            }
            // Metres per pixel, so the picture is Size metres wide.
            sprite.PixelSize = definition.Size / Mathf.Max(definition.Texture.GetWidth(), 1);
            return sprite;
        }

        if (_freeMeshCount == 0)
            return null;
        _freeMeshCount--;
        MeshInstance3D mesh = _freeMeshes[_freeMeshCount];
        if (mesh.Mesh != definition.Mesh)
        {
            mesh.Mesh = definition.Mesh;
        }
        return mesh;
    }

    // Movement in _PhysicsProcess, like the enemies, so that hit checks
    // compare positions from the same fixed time step.
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Loop backwards, because Despawn moves the last projectile into
        // the freed slot. Going from the end, that moved projectile has
        // already been updated this step, so nothing is skipped or updated
        // twice.
        for (int i = _count - 1; i >= 0; i--)
        {
            if (!Fly(i, dt))
            {
                Despawn(i);
                continue;
            }
            if (!Hit(i))
            {
                Despawn(i);
            }
        }
    }

    /// <summary>
    /// Moves projectile i one step. Returns false when it is done: flown its
    /// range, caught by whoever threw it, or stopped by a wall.
    /// </summary>
    private bool Fly(int i, float dt)
    {
        ProjectileDefinition definition = _definitions[i];
        float move = definition.Speed * dt;
        _spin[i] += definition.SpinTurnsPerSecond * Mathf.Tau * dt;
        Vector3 previous = _positions[i];

        if (_returning[i])
        {
            Node3D thrower = _returnTo[i];
            // IsInstanceValid: the thrower may have been freed meanwhile.
            if (thrower == null || !IsInstanceValid(thrower))
            {
                return false;
            }
            Vector3 toThrower = thrower.GlobalPosition - _positions[i];
            toThrower.Y = 0.0f;
            float distance = toThrower.Length();
            if (distance <= move + CatchDistance)
            {
                return false; // caught
            }
            _directions[i] = toThrower / distance;
        }

        _positions[i] += _directions[i] * move;
        _travelled[i] += move;

        // A wall anywhere on this step's path stops it. The whole path is
        // tested, not just the new position: a wall (0.24 m) is thinner
        // than some projectiles move per step.
        if (Obstacles != null && Obstacles.IsLineBlocked(previous, _positions[i]))
        {
            return false;
        }

        if (!_returning[i] && _travelled[i] >= definition.Range)
        {
            if (!definition.Returns || _returnTo[i] == null)
            {
                return false;
            }
            // Turn back: everything can be hit once more on the way.
            _returning[i] = true;
            _hitCount[i] = 0;
            _nextHitSlot[i] = 0;
        }
        else if (_travelled[i] > definition.Range * MaxTravelInRanges)
        {
            return false; // couldn't catch up with the thrower
        }

        return true;
    }

    /// <summary>
    /// Damages what projectile i touches at its new position. Returns false
    /// when it is used up (out of hits, or stopped by a prop).
    /// </summary>
    private bool Hit(int i)
    {
        // Hits are checked at the new position, not along the path since
        // the last step. Fine while a projectile moves less per step than
        // its reach (a 14 m/s shard moves 0.23 m per step against a 0.4 m
        // reach). Walls are tested along the path in Fly.
        if (EnemyManager != null)
        {
            ProjectileDefinition definition = _definitions[i];
            // QueryCircle leaves out enemies behind a wall from here.
            int count = EnemyManager.QueryCircle(_positions[i], definition.Radius, _query);
            for (int n = 0; n < count; n++)
            {
                int enemy = _query[n];
                int id = EnemyManager.GetId(enemy);
                if (HasHit(i, id))
                {
                    continue; // already hit by this projectile
                }

                EnemyManager.Damage(enemy, definition.Damage);
                RememberHit(i, id);

                // "--x" lowers x first, then compares the new value.
                if (_hitsLeft[i] > 0 && --_hitsLeft[i] == 0)
                {
                    return false;
                }
            }
        }

        // A prop is solid: it takes the hit and stops the projectile, even
        // a piercing one.
        if (BreakableManager != null
            && BreakableManager.HitTest(_positions[i], _definitions[i].Radius, _definitions[i].Damage))
        {
            return false;
        }
        return true;
    }

    private bool HasHit(int i, int id)
    {
        // Array.IndexOf over this projectile's part of _hitIds; -1 = not found.
        return Array.IndexOf(_hitIds, id, i * HitMemory, _hitCount[i]) >= 0;
    }

    private void RememberHit(int i, int id)
    {
        _hitIds[i * HitMemory + _nextHitSlot[i]] = id;
        _nextHitSlot[i] = (_nextHitSlot[i] + 1) % HitMemory;
        _hitCount[i] = Mathf.Min(_hitCount[i] + 1, HitMemory);
    }

    // Drawing in _Process, which runs every rendered frame and after the
    // camera has moved, so pictures turn smoothly towards it.
    public override void _Process(double delta)
    {
        for (int i = 0; i < _count; i++)
        {
            PlaceNode(i);
        }
    }

    // Puts projectile i's node where it is, turned the way it should look.
    private void PlaceNode(int i)
    {
        ProjectileDefinition definition = _definitions[i];
        Vector3 drawAt = _positions[i] + Vector3.Up * definition.Height;
        Vector3 direction = _directions[i];

        if (_nodes[i] is MeshInstance3D)
        {
            // A model points along its flight (its front is +Z).
            float yaw = Mathf.Atan2(direction.X, direction.Z);
            _nodes[i].Transform = new Transform3D(new Basis(Vector3.Up, yaw), drawAt);
            return;
        }

        _camera ??= GetViewport().GetCamera3D();
        if (_camera == null)
        {
            return;
        }
        Basis cameraBasis = _camera.GlobalBasis;

        float angle;
        if (definition.SpinTurnsPerSecond != 0.0f)
        {
            angle = _spin[i];
        }
        else
        {
            // How the flight direction looks on screen: its parts along the
            // camera's right (X) and up (Y) axes.
            float screenX = direction.Dot(cameraBasis.X);
            float screenY = direction.Dot(cameraBasis.Y);
            angle = Mathf.Atan2(screenY, screenX) - Mathf.DegToRad(definition.TextureAngleDegrees);
        }

        // Face the camera (its basis), then turn around the axis that
        // points at the camera (local Z) by the angle.
        _nodes[i].Transform = new Transform3D(cameraBasis * new Basis(Vector3.Back, angle), drawAt);
    }

    /// <summary>
    /// Removes projectile i with a "swap remove": the last projectile in
    /// flight moves into slot i, so the live ones stay packed at the start
    /// of the arrays. Its node goes back to its pool. Never keep a
    /// projectile index across frames.
    /// </summary>
    private void Despawn(int i)
    {
        // Hide the node and push it back onto its pool's stack.
        Node3D node = _nodes[i];
        node.Visible = false;
        if (node is Sprite3D sprite)
        {
            _freeSprites[_freeSpriteCount] = sprite;
            _freeSpriteCount++;
        }
        else
        {
            _freeMeshes[_freeMeshCount] = (MeshInstance3D)node;
            _freeMeshCount++;
        }

        int last = _count - 1;
        _definitions[i] = _definitions[last];
        _positions[i] = _positions[last];
        _directions[i] = _directions[last];
        _travelled[i] = _travelled[last];
        _spin[i] = _spin[last];
        _hitsLeft[i] = _hitsLeft[last];
        _returning[i] = _returning[last];
        _returnTo[i] = _returnTo[last];
        _hitCount[i] = _hitCount[last];
        _nextHitSlot[i] = _nextHitSlot[last];
        // The hit memory is a block of HitMemory ids per projectile: copy
        // the last one's block into slot i's.
        Array.Copy(_hitIds, last * HitMemory, _hitIds, i * HitMemory, HitMemory);
        _nodes[i] = _nodes[last];
        _definitions[last] = null;
        _returnTo[last] = null;
        _nodes[last] = null;
        _count--;
    }
}
