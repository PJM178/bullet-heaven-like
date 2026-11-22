using System;
using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The breakable props in the scene (barrels, crates, ...): the "shell"
/// around <see cref="BreakableSimulation"/>, which knows each prop's health
/// and which attacks catch it. This node draws the props, and acts on every
/// hit the core reports: a flash and squash, or on a break: hiding the
/// model, throwing its pieces, puffing dust, dropping its loot and removing
/// its collision, compass marker and enemy obstacle. Same split as
/// EnemyManager / EnemySimulation.
///
/// Props enter through <see cref="Add"/> (placed ones via
/// <see cref="PlacedBreakable"/>). What a prop looks like and drops comes
/// from its <see cref="BreakableDefinition"/>.
///
/// Weapons aim at enemies only. A prop is hit when it's caught in an attack
/// anyway: weapons call the Damage... and HitTest methods below next to
/// their enemy hits.
///
/// Props are few and never move, so they keep their slot for the whole run
/// (no swap remove, see BreakableSimulation); every array here is by slot.
///
/// Broken pieces are pooled MeshInstance3D nodes moved by a few lines of
/// code (gravity, a bounce, spin), not physics bodies: cheaper on the phone
/// and fine for something that lives a second.
///
/// Keep this node at the origin with no rotation or scale.
/// </summary>
public partial class BreakableManager : Node3D, IBreakableTargets
{
    /// <summary>The group this manager is in, so placed props can find it
    /// without a NodePath.</summary>
    public const string Group = "breakable_manager";

    // Where the loot goes.
    [Export] public PickupManager PickupManager;

    // Props whose definition has a Marker show on the compass until they
    // break. Optional: without it no prop is marked.
    [Export] public WorldMarkers WorldMarkers;

    // Props block enemies too, until they break. Optional.
    [Export] public WorldObstacles Obstacles;

    // Swapped onto a prop's meshes for a moment when it's hit (the same
    // white flash enemies get).
    [Export] public Material HitFlashMaterial;
    [Export] public float HitFlashDuration = 0.12f;

    // A hit also squashes the prop briefly: how much (0.15 = 15% shorter)
    // and for how long.
    [Export] public float SquashAmount = 0.15f;
    [Export] public float SquashDuration = 0.15f;

    // Collision layer of the props' solid bodies. The player's body looks
    // at layer 1 by default. Enemies don't use physics, so they walk
    // through props.
    [Export(PropertyHint.Layers3DPhysics)] public uint CollisionLayer = 1;

    [ExportGroup("Pieces")]
    // How fast pieces fly out sideways and up, in metres per second. Each
    // piece gets a random amount between half and all of it.
    [Export] public float PieceOutSpeed = 3.0f;
    [Export] public float PieceUpSpeed = 4.0f;
    [Export] public float PieceGravity = 18.0f;
    // Spin in turns per second, at most.
    [Export] public float PieceSpin = 2.0f;
    // Seconds a piece lasts, the last PieceShrinkTime of them shrinking away.
    [Export] public float PieceLife = 1.2f;
    [Export] public float PieceShrinkTime = 0.3f;

    [ExportGroup("Loot")]
    // Loot lands at random within this distance of the prop's centre.
    [Export] public float LootScatter = 0.6f;

    [ExportGroup("Pool sizes")]
    [Export] public int MaxBreakables = 64;
    [Export] public int MaxPieces = 64;
    [Export] public int MaxDust = 8;

    // The game logic. Null until _Ready.
    private BreakableSimulation _sim;

    // ---- Looks and world hookups, by slot (the core's slot numbers). ------
    private float[] _flashTime;   // seconds left of the white flash
    private float[] _squashTime;  // seconds left of the squash
    private Node3D[] _roots;      // holds the model and the solid body
    private Node3D[] _models;
    private CollisionShape3D[] _shapes;
    // Each prop's meshes, for the flash. One small list per prop, made when
    // it's added, never in the frame loop.
    private List<MeshInstance3D>[] _meshes;
    private int[] _markerIds;     // WorldMarkers id, or -1 for none
    private int[] _obstacleIds;   // WorldObstacles id, or -1 for none

    // ---- Flying pieces: the first _pieceCount are in the air. -----------
    private MeshInstance3D[] _pieceNodes;
    private Vector3[] _piecePositions;
    private Vector3[] _pieceVelocities;
    private Basis[] _pieceBases;      // rotation and the prop's scale
    private Vector3[] _pieceSpinAxes;
    private float[] _pieceSpinRates;  // radians per second
    private float[] _pieceLife;       // seconds left
    private int _pieceCount;

    private AnimatedSprite3D[] _dust;
    private int _nextDust;

    private readonly RandomNumberGenerator _random = new();

    public override void _EnterTree()
    {
        // _EnterTree runs before any _Ready, so placed props always find
        // the manager in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        // Obstacles is handed over as IObstacleQuery: the core only asks it
        // whether a wall stands between an attack and a prop.
        _sim = new BreakableSimulation(MaxBreakables, Obstacles);
        _flashTime = new float[MaxBreakables];
        _squashTime = new float[MaxBreakables];
        _roots = new Node3D[MaxBreakables];
        _models = new Node3D[MaxBreakables];
        _shapes = new CollisionShape3D[MaxBreakables];
        _meshes = new List<MeshInstance3D>[MaxBreakables];
        _markerIds = new int[MaxBreakables];
        _obstacleIds = new int[MaxBreakables];

        _pieceNodes = new MeshInstance3D[MaxPieces];
        _piecePositions = new Vector3[MaxPieces];
        _pieceVelocities = new Vector3[MaxPieces];
        _pieceBases = new Basis[MaxPieces];
        _pieceSpinAxes = new Vector3[MaxPieces];
        _pieceSpinRates = new float[MaxPieces];
        _pieceLife = new float[MaxPieces];
        for (int i = 0; i < MaxPieces; i++)
        {
            _pieceNodes[i] = new MeshInstance3D { Visible = false };
            AddChild(_pieceNodes[i]);
        }

        _dust = new AnimatedSprite3D[MaxDust];
        for (int i = 0; i < MaxDust; i++)
        {
            _dust[i] = new AnimatedSprite3D { Visible = false };
            AddChild(_dust[i]);
        }
    }

    /// <summary>
    /// Puts a prop of kind <paramref name="definition"/> into the world.
    /// <paramref name="transform"/> places, turns and scales it; its origin
    /// is the bottom centre of the prop.
    /// </summary>
    public void Add(BreakableDefinition definition, Transform3D transform)
    {
        if (definition == null || _sim == null)
            return;
        int i = _sim.Add(definition, transform);
        if (i == BreakableSimulation.NoProp)
        {
            GD.PushWarning($"BreakableManager full ({MaxBreakables}); {definition.DisplayName} not added.");
            return;
        }

        _markerIds[i] = definition.Marker != null && WorldMarkers != null
            ? WorldMarkers.Add(definition.Marker, transform.Origin)
            : -1;

        // Props are added when a level starts, so making nodes here is fine
        // (the pooling rule is about things made every frame).
        var root = new Node3D { Transform = transform };
        AddChild(root);
        _roots[i] = root;

        Node3D model = definition.Model?.Instantiate<Node3D>() ?? MakeStandIn(definition);
        root.AddChild(model);
        _models[i] = model;
        _meshes[i] = new List<MeshInstance3D>();
        foreach (Node node in model.FindChildren("*", "MeshInstance3D", true, false))
            _meshes[i].Add((MeshInstance3D)node);

        // A plain StaticBody3D: the player's MoveAndSlide stops at it. The
        // cylinder stands on the ground, so its centre is half way up.
        var body = new StaticBody3D { CollisionLayer = CollisionLayer, CollisionMask = 0 };
        var shape = new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = definition.Radius, Height = definition.Height },
            Position = Vector3.Up * definition.Height * 0.5f,
        };
        body.AddChild(shape);
        root.AddChild(body);
        _shapes[i] = shape;

        // The same cylinder, for enemies. Scale it like the body.
        Vector3 scale = transform.Basis.Scale;
        _obstacleIds[i] = Obstacles != null
            ? Obstacles.AddCircle(transform.Origin, definition.Radius * Mathf.Max(scale.X, scale.Z),
                blocksAttacks: false) // a named argument: says which parameter the value is for
            : -1;
    }

    // ---- Hits. Weapons call these next to their enemy hits. The core
    // decides what is hit; ApplyHits shows it straight away. See
    // BreakableSimulation for what each one catches. ----------------------

    public void DamageCircle(Vector3 centre, float radius, float damage)
    {
        _sim.DamageCircle(centre, radius, damage);
        ApplyHits();
    }

    public void DamageArc(Vector3 origin, Vector3 forward, float range, float halfArc, float damage)
    {
        _sim.DamageArc(origin, forward, range, halfArc, damage);
        ApplyHits();
    }

    public void DamageRing(Vector3 centre, float innerRadius, float outerRadius, float damage,
        bool isArc, Vector3 direction, float halfAngle)
    {
        _sim.DamageRing(centre, innerRadius, outerRadius, damage, isArc, direction, halfAngle);
        ApplyHits();
    }

    /// <summary>For projectiles (IBreakableTargets): damages the first prop
    /// within radius of point; true if one was hit.</summary>
    public bool HitTest(Vector3 point, float radius, float damage)
    {
        bool hit = _sim.HitTest(point, radius, damage);
        ApplyHits();
        return hit;
    }

    /// <summary>Acts on what the core's last damage call hit.</summary>
    private void ApplyHits()
    {
        ReadOnlySpan<BreakableSimulation.Hit> hits = _sim.Hits;
        for (int n = 0; n < hits.Length; n++)
        {
            int i = hits[n].Slot;
            if (hits[n].Broke)
            {
                Break(i);
                continue;
            }

            if (HitFlashMaterial != null)
            {
                foreach (MeshInstance3D mesh in _meshes[i])
                    mesh.MaterialOverride = HitFlashMaterial;
                _flashTime[i] = HitFlashDuration;
            }
            _squashTime[i] = SquashDuration;
        }
    }

    // Everything a break sets off. The core has already marked it broken.
    private void Break(int i)
    {
        // Remove ignores -1, so no check needed for unmarked props.
        WorldMarkers?.Remove(_markerIds[i]);
        _markerIds[i] = -1;
        _roots[i].Visible = false;
        // Hits happen during the physics step, when Godot doesn't allow
        // changing collision shapes; SetDeferred does it at the end of the
        // frame.
        _shapes[i].SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        Obstacles?.Remove(_obstacleIds[i]);
        _obstacleIds[i] = -1;

        BreakableDefinition definition = _sim.GetDefinition(i);
        Transform3D transform = _sim.GetTransform(i);
        Vector3 centre = transform.Origin;

        // End a hit flash still going, or the pieces would be cut with the
        // white flash material instead of the model's own.
        foreach (MeshInstance3D mesh in _meshes[i])
            mesh.MaterialOverride = null;

        // Cut the model into pieces the first time a prop of this kind
        // breaks, then reuse them.
        definition.Pieces ??= MeshShatter.Split(_models[i], definition.Slices);
        foreach (MeshShatter.Piece piece in definition.Pieces)
            ThrowPiece(piece, transform);

        PuffDust(definition, centre);

        PickupManager?.DropLoot(definition.Loot, centre, LootScatter);
    }

    private void ThrowPiece(MeshShatter.Piece piece, Transform3D propTransform)
    {
        if (_pieceCount >= MaxPieces)
            return; // pool full: this piece just doesn't appear

        int p = _pieceCount;
        _pieceCount++;

        Vector3 start = propTransform * piece.Centre;
        // Outwards from the prop's middle; a piece right in the middle (a
        // lid) gets a random direction.
        Vector3 outward = start - propTransform.Origin;
        outward.Y = 0.0f;
        if (outward.LengthSquared() < 0.0001f)
            outward = new Vector3(_random.RandfRange(-1, 1), 0.0f, _random.RandfRange(-1, 1));
        outward = outward.Normalized();

        _piecePositions[p] = start;
        _pieceVelocities[p] = outward * PieceOutSpeed * _random.RandfRange(0.5f, 1.0f)
            + Vector3.Up * PieceUpSpeed * _random.RandfRange(0.5f, 1.0f);
        _pieceBases[p] = propTransform.Basis;
        _pieceSpinAxes[p] = new Vector3(_random.RandfRange(-1, 1), _random.RandfRange(-1, 1), _random.RandfRange(-1, 1)).Normalized();
        _pieceSpinRates[p] = _random.RandfRange(0.3f, 1.0f) * PieceSpin * Mathf.Tau;
        _pieceLife[p] = PieceLife * _random.RandfRange(0.8f, 1.0f);

        MeshInstance3D node = _pieceNodes[p];
        node.Mesh = piece.Mesh;
        node.Transform = new Transform3D(_pieceBases[p], start);
        node.Visible = true;
    }

    private void PuffDust(BreakableDefinition definition, Vector3 position)
    {
        SpriteFrames frames = definition.DustFrames;
        if (frames == null)
            return;

        // Reuse the puffs in turn; with a short animation the oldest one
        // has long finished by the time it comes round again.
        AnimatedSprite3D dust = _dust[_nextDust];
        _nextDust = (_nextDust + 1) % MaxDust;
        if (dust.SpriteFrames != frames)
        {
            dust.SpriteFrames = frames;
            SpriteDirections.Setup(dust);
        }
        dust.PixelSize = definition.DustPixelSize;
        dust.Position = position;
        dust.Visible = true;
        dust.Play(frames.GetAnimationNames()[0]);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        UpdateProps(dt);
        UpdatePieces(dt);

        for (int d = 0; d < MaxDust; d++)
        {
            if (_dust[d].Visible && !_dust[d].IsPlaying())
                _dust[d].Visible = false;
        }
    }

    // The hit flash and squash of props that were hit.
    private void UpdateProps(float dt)
    {
        int count = _sim.Count;
        for (int i = 0; i < count; i++)
        {
            if (_sim.IsBroken(i))
                continue;

            if (_flashTime[i] > 0.0f)
            {
                _flashTime[i] -= dt;
                if (_flashTime[i] <= 0.0f)
                {
                    foreach (MeshInstance3D mesh in _meshes[i])
                        mesh.MaterialOverride = null;
                }
            }

            if (_squashTime[i] > 0.0f)
            {
                _squashTime[i] = Mathf.Max(0.0f, _squashTime[i] - dt);
                // 1 right after the hit, easing back to 0.
                float k = _squashTime[i] / SquashDuration;
                float down = SquashAmount * k;
                // Shorter and a little wider, like something being thumped.
                _models[i].Scale = new Vector3(1.0f + down * 0.5f, 1.0f - down, 1.0f + down * 0.5f);
            }
        }
    }

    private void UpdatePieces(float dt)
    {
        // Backwards, because removing moves the last piece into the freed
        // slot (swap remove); that one was already updated.
        for (int p = _pieceCount - 1; p >= 0; p--)
        {
            _pieceLife[p] -= dt;
            if (_pieceLife[p] <= 0.0f)
            {
                RemovePiece(p);
                continue;
            }

            _pieceVelocities[p].Y -= PieceGravity * dt;
            _piecePositions[p] += _pieceVelocities[p] * dt;

            // Bounce once off the ground, losing most of the speed, then
            // lie there. The centre stops a little above the ground so the
            // piece doesn't sink in.
            const float restHeight = 0.08f;
            if (_piecePositions[p].Y < restHeight)
            {
                _piecePositions[p].Y = restHeight;
                _pieceVelocities[p] *= new Vector3(0.5f, -0.3f, 0.5f);
                _pieceSpinRates[p] *= 0.5f;
            }

            // Turn a little more around the piece's spin axis. Rotating
            // the basis on the left turns it in world space.
            _pieceBases[p] = new Basis(_pieceSpinAxes[p], _pieceSpinRates[p] * dt) * _pieceBases[p];

            // Shrink away over the last moments.
            float size = Mathf.Clamp(_pieceLife[p] / PieceShrinkTime, 0.0f, 1.0f);
            _pieceNodes[p].Transform = new Transform3D(_pieceBases[p].Scaled(Vector3.One * size), _piecePositions[p]);
        }
    }

    private void RemovePiece(int p)
    {
        int last = _pieceCount - 1;
        MeshInstance3D removed = _pieceNodes[p];
        removed.Visible = false;
        removed.Mesh = null;

        if (p != last)
        {
            _pieceNodes[p] = _pieceNodes[last];
            _piecePositions[p] = _piecePositions[last];
            _pieceVelocities[p] = _pieceVelocities[last];
            _pieceBases[p] = _pieceBases[last];
            _pieceSpinAxes[p] = _pieceSpinAxes[last];
            _pieceSpinRates[p] = _pieceSpinRates[last];
            _pieceLife[p] = _pieceLife[last];
            _pieceNodes[last] = removed;
        }
        _pieceCount--;
    }

    // ---- Helpers ---------------------------------------------------------

    /// <summary>A plain brown cylinder, used when the model file is
    /// missing.</summary>
    private static Node3D MakeStandIn(BreakableDefinition definition)
    {
        var mesh = new CylinderMesh
        {
            TopRadius = definition.Radius,
            BottomRadius = definition.Radius,
            Height = definition.Height,
            RadialSegments = 8,
            Rings = 0,
            Material = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.18f), Roughness = 1.0f },
        };
        // A cylinder mesh is centred on its middle; lift it to stand on
        // the ground like a model would. The holder node makes it look like
        // an imported model: a root with the mesh below it.
        var holder = new Node3D();
        holder.AddChild(new MeshInstance3D { Mesh = mesh, Position = Vector3.Up * definition.Height * 0.5f });
        return holder;
    }
}
