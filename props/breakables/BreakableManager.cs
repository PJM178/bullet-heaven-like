using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns every breakable prop in the world (barrels, crates, ...): takes the
/// hits weapons deal to them, and breaks them apart when their health runs
/// out, throwing their pieces, puffing dust and dropping their loot.
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
/// (no swap remove): a broken one is just marked broken. That also means a
/// slot number stays valid across frames, unlike an enemy index.
///
/// Broken pieces are pooled MeshInstance3D nodes moved by a few lines of
/// code (gravity, a bounce, spin), not physics bodies: cheaper on the phone
/// and fine for something that lives a second.
///
/// Keep this node at the origin with no rotation or scale.
/// </summary>
public partial class BreakableManager : Node3D
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

    // ---- Prop data. Index i in every array is the same prop. Only the
    // first _count slots are used. ----------------------------------------
    private BreakableDefinition[] _definitions;
    private Transform3D[] _transforms;
    private float[] _health;
    private bool[] _broken;
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
    private int _count;

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
        _definitions = new BreakableDefinition[MaxBreakables];
        _transforms = new Transform3D[MaxBreakables];
        _health = new float[MaxBreakables];
        _broken = new bool[MaxBreakables];
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
        if (definition == null)
            return;
        if (_count >= MaxBreakables)
        {
            GD.PushWarning($"BreakableManager full ({MaxBreakables}); {definition.DisplayName} not added.");
            return;
        }

        int i = _count;
        _count++;
        _definitions[i] = definition;
        _markerIds[i] = definition.Marker != null && WorldMarkers != null
            ? WorldMarkers.Add(definition.Marker, transform.Origin)
            : -1;
        _transforms[i] = transform;
        _health[i] = definition.Health;

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

    // ---- Hits. Weapons call these next to their enemy hits. --------------

    /// <summary>Damages every prop that overlaps a circle on the ground
    /// (a smash, an explosion).</summary>
    public void DamageCircle(Vector3 centre, float radius, float damage)
    {
        for (int i = 0; i < _count; i++)
        {
            if (!_broken[i] && GroundDistance(i, centre) <= radius + _definitions[i].Radius && CanReach(centre, i))
                Damage(i, damage);
        }
    }

    /// <summary>Damages every prop that overlaps a wedge (a sword swing):
    /// within range of origin and within halfArc radians either side of
    /// forward.</summary>
    public void DamageArc(Vector3 origin, Vector3 forward, float range, float halfArc, float damage)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_broken[i])
                continue;
            float radius = _definitions[i].Radius;
            if (GroundDistance(i, origin) > range + radius)
                continue;
            if (!InArc(i, origin, forward, halfArc))
                continue;
            if (!CanReach(origin, i))
                continue;
            Damage(i, damage);
        }
    }

    /// <summary>
    /// Damages the props an expanding wave's edge passed since its last
    /// step: those whose nearest edge is further than innerRadius from
    /// centre, but not further than outerRadius. Each prop is passed only
    /// once, so it's hit once per wave. An arc wave also checks the angle
    /// (isArc, direction, halfAngle; see ExpandingWave).
    /// </summary>
    public void DamageRing(Vector3 centre, float innerRadius, float outerRadius, float damage,
        bool isArc, Vector3 direction, float halfAngle)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_broken[i])
                continue;
            float edge = Mathf.Max(0.0f, GroundDistance(i, centre) - _definitions[i].Radius);
            if (edge <= innerRadius || edge > outerRadius)
                continue;
            if (isArc && !InArc(i, centre, direction, halfAngle))
                continue;
            if (!CanReach(centre, i))
                continue;
            Damage(i, damage);
        }
    }

    /// <summary>
    /// For projectiles: damages the first prop within radius of point and
    /// returns true, so the projectile can stop there. False if it hit
    /// nothing.
    /// </summary>
    public bool HitTest(Vector3 point, float radius, float damage)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_broken[i] || point.Y > _definitions[i].Height * _transforms[i].Basis.Scale.Y)
                continue;
            if (GroundDistance(i, point) <= radius + _definitions[i].Radius)
            {
                Damage(i, damage);
                return true;
            }
        }
        return false;
    }

    // No wall between an attack's centre and prop i (projectiles don't
    // need this: walls stop them before they get here).
    private bool CanReach(Vector3 from, int i)
    {
        return Obstacles == null || !Obstacles.IsLineBlocked(from, _transforms[i].Origin);
    }

    private void Damage(int i, float amount)
    {
        _health[i] -= amount;

        if (_health[i] <= 0.0f)
        {
            Break(i);
            return;
        }

        if (HitFlashMaterial != null)
        {
            foreach (MeshInstance3D mesh in _meshes[i])
                mesh.MaterialOverride = HitFlashMaterial;
            _flashTime[i] = HitFlashDuration;
        }
        _squashTime[i] = SquashDuration;
    }

    private void Break(int i)
    {
        _broken[i] = true;
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

        BreakableDefinition definition = _definitions[i];
        Transform3D transform = _transforms[i];
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

        if (PickupManager != null)
        {
            foreach (LootDrop line in definition.Loot)
            {
                int count = line?.Roll(_random) ?? 0;
                for (int n = 0; n < count; n++)
                {
                    // A random point in a circle around the prop.
                    float angle = _random.Randf() * Mathf.Tau;
                    float distance = Mathf.Sqrt(_random.Randf()) * LootScatter;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * distance;
                    PickupManager.Drop(line.Pickup, centre + offset);
                }
            }
        }
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
        for (int i = 0; i < _count; i++)
        {
            if (_broken[i])
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

    private float GroundDistance(int i, Vector3 point)
    {
        Vector3 origin = _transforms[i].Origin;
        float dx = origin.X - point.X;
        float dz = origin.Z - point.Z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Whether prop i overlaps the slice that spreads halfAngle
    /// radians either side of direction from origin. Its body widens the
    /// slice by the angle its radius covers, like enemies in the sword
    /// swing.</summary>
    private bool InArc(int i, Vector3 origin, Vector3 direction, float halfAngle)
    {
        Vector3 offset = _transforms[i].Origin - origin;
        offset.Y = 0.0f;
        float distance = offset.Length();
        float radius = _definitions[i].Radius;
        if (distance <= radius)
            return true; // it covers the origin, so every direction
        direction.Y = 0.0f;
        float angle = direction.AngleTo(offset);
        float bodyAngle = Mathf.Asin(radius / distance);
        return angle <= halfAngle + bodyAngle;
    }

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
