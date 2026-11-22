using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The projectiles in the scene: the "shell" around
/// <see cref="ProjectileSimulation"/>, which does all the flying and hitting.
/// This node owns one, steps it every physics tick, and draws it: one mesh
/// node and one picture node per slot, dressed from the projectile's
/// <see cref="ProjectileDefinition"/> when it is spawned. Same split as
/// EnemyManager / EnemySimulation.
///
/// Weapons only call <see cref="Spawn"/> (usually through a
/// <see cref="ProjectileWeapon"/>).
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
    // Spawn() skips the shot instead of growing.
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

    // The most different throwers (players) returning projectiles can fly
    // back to. One today; room for co-op.
    private const int MaxPlayers = 8;

    // The game logic. Null until _Ready.
    private ProjectileSimulation _sim;

    // ---- Per slot (see ProjectileSimulation): both kinds of node, set up
    // once. A projectile shows whichever fits its definition. -------------
    private MeshInstance3D[] _meshes;
    private Sprite3D[] _sprites;
    private Node3D[] _shown;   // the node slot s shows now; null = none
    private float[] _spin;     // radians, for spinning pictures (looks only)

    // Players that threw returning projectiles, so the core can refer to
    // them by number: the core gets their positions every step, in this
    // order. Filled as throwers first appear (see PlayerIndex).
    private readonly Node3D[] _players = new Node3D[MaxPlayers];
    private readonly Vector3[] _playerPositions = new Vector3[MaxPlayers];
    private int _playerCount;

    private Camera3D _camera;

    /// <summary>How many projectiles are in flight (read by the HUD).</summary>
    public int Count => _sim?.Count ?? 0;

    public override void _EnterTree()
    {
        // Before any _Ready, so weapons always find us in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;

        // The managers are handed over as interfaces: the core can ask them
        // things (and damage what they own) but never sees the nodes.
        _sim = new ProjectileSimulation(MaxProjectiles, Obstacles, EnemyManager, BreakableManager);

        // Both kinds of node for every slot up front, hidden, without a mesh
        // or picture: each gets those from the definition when it is
        // spawned, so one node serves every kind of projectile that looks
        // that way.
        int capacity = _sim.Capacity;
        _meshes = new MeshInstance3D[capacity];
        _sprites = new Sprite3D[capacity];
        _shown = new Node3D[capacity];
        _spin = new float[capacity];
        for (int n = 0; n < capacity; n++)
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
            _meshes[n] = mesh;

            Sprite3D sprite = new()
            {
                Visible = false,
                // Hard edges and no smoothing, like the character sprites.
                AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(sprite);
            _sprites[n] = sprite;
        }
    }

    /// <summary>
    /// Launches one projectile of the given kind from position (on the
    /// ground; it is drawn the definition's Height above it), flying in
    /// direction (flat, length 1). A projectile that Returns flies back to
    /// <paramref name="returnTo"/> (usually the player who threw it).
    /// Returns false if the manager is full and the shot was skipped.
    /// </summary>
    public bool Spawn(ProjectileDefinition definition, Vector3 position, Vector3 direction,
                      Node3D returnTo = null)
    {
        if (_sim == null)
        {
            return false;
        }

        int returnPlayer = returnTo != null ? PlayerIndex(returnTo) : ProjectileSimulation.NoPlayer;
        int i = _sim.Spawn(definition, position, direction, returnPlayer);
        if (i == ProjectileSimulation.NoProjectile)
        {
            return false;
        }

        // Show it right away, already where it starts.
        int slot = _sim.GetSlot(i);
        _shown[slot] = DressNode(slot, definition);
        _shown[slot].Visible = true;
        _spin[slot] = 0.0f;
        PlaceNode(i);
        return true;
    }

    /// <summary>
    /// The number the core uses for this thrower, adding it the first time.
    /// NoPlayer if there's no room for another.
    /// </summary>
    private int PlayerIndex(Node3D player)
    {
        for (int p = 0; p < _playerCount; p++)
        {
            if (_players[p] == player)
                return p;
        }
        if (_playerCount == MaxPlayers)
            return ProjectileSimulation.NoPlayer;

        _players[_playerCount] = player;
        _playerPositions[_playerCount] = player.GlobalPosition;
        _playerCount++;
        return _playerCount - 1;
    }

    // Gives slot s's node of the right kind the definition's look and
    // returns it.
    private Node3D DressNode(int slot, ProjectileDefinition definition)
    {
        if (definition.Texture != null)
        {
            Sprite3D sprite = _sprites[slot];
            // Only swap the picture when it differs from what this node
            // last showed: comparing references is cheap, setting it is a
            // call into the engine.
            if (sprite.Texture != definition.Texture)
            {
                sprite.Texture = definition.Texture;
            }
            // Metres per pixel, so the picture is Size metres wide.
            sprite.PixelSize = definition.Size / Mathf.Max(definition.Texture.GetWidth(), 1);
            return sprite;
        }

        MeshInstance3D mesh = _meshes[slot];
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
        // Godot -> core: where the throwers are. A thrower that has been
        // freed keeps its last known position, so its projectiles fly back
        // there and end.
        for (int p = 0; p < _playerCount; p++)
        {
            // IsInstanceValid: false once a node has been freed.
            if (IsInstanceValid(_players[p]))
            {
                _playerPositions[p] = _players[p].GlobalPosition;
            }
        }

        // AsSpan(0, n): only the first n entries, the throwers known so far.
        _sim.Step((float)delta, _playerPositions.AsSpan(0, _playerCount));

        // Core -> Godot: hide the nodes of projectiles that ended.
        ReadOnlySpan<int> removed = _sim.RemovedSlots;
        for (int n = 0; n < removed.Length; n++)
        {
            int slot = removed[n];
            _shown[slot].Visible = false;
            _shown[slot] = null;
        }
    }

    // Drawing in _Process, which runs every rendered frame and after the
    // camera has moved, so pictures turn smoothly towards it.
    public override void _Process(double delta)
    {
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            int slot = _sim.GetSlot(i);
            _spin[slot] += _sim.GetDefinition(i).SpinTurnsPerSecond * Mathf.Tau * (float)delta;
            PlaceNode(i);
        }
    }

    // Puts projectile i's node where it is, turned the way it should look.
    private void PlaceNode(int i)
    {
        ProjectileDefinition definition = _sim.GetDefinition(i);
        int slot = _sim.GetSlot(i);
        Node3D node = _shown[slot];
        Vector3 drawAt = _sim.Positions[i] + Vector3.Up * definition.Height;
        Vector3 direction = _sim.Directions[i];

        if (node is MeshInstance3D)
        {
            // A model points along its flight (its front is +Z).
            float yaw = Mathf.Atan2(direction.X, direction.Z);
            node.Transform = new Transform3D(new Basis(Vector3.Up, yaw), drawAt);
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
            angle = _spin[slot];
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
        node.Transform = new Transform3D(cameraBasis * new Basis(Vector3.Back, angle), drawAt);
    }
}
