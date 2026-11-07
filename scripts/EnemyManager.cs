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
    // The scene to instance for each enemy (enemies/slime.tscn).
    [Export] public PackedScene EnemyScene;

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

    // Stepped, PS1-style animation rate (see Player.AnimationFps).
    [Export] public float AnimationFps = 12.0f;

    private static readonly StringName MoveAnimation = "move";

    // ---- Enemy data. Index i in every array is the same enemy. Only the
    // first _count entries are alive; the rest are spare pool slots. ------
    private Vector3[] _positions;
    private Vector3[] _separation;          // per-frame push from neighbours
    private Node3D[] _nodes;
    private AnimationPlayer[] _animationPlayers;
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

    private double _animationTimeAccumulated;

    // How many enemies are alive (read by the HUD).
    public int Count => _count;

    public override void _Ready()
    {
        if (EnemyScene == null || Target == null)
        {
            GD.PushWarning("EnemyManager needs EnemyScene and Target set.");
            SetPhysicsProcess(false);
            SetProcess(false);
            return;
        }

        // Allocate every array once, at full size. "new" in _Ready is fine;
        // it's doing it every frame that we avoid.
        _positions = new Vector3[MaxEnemies];
        _separation = new Vector3[MaxEnemies];
        _nodes = new Node3D[MaxEnemies];
        _animationPlayers = new AnimationPlayer[MaxEnemies];
        _enemySlot = new int[MaxEnemies];
        _cellEntries = new int[MaxEnemies];
        _cellStart = new int[GridTableSize];
        _cellCount = new int[GridTableSize];

        // Build the whole pool up front, hidden.
        for (int i = 0; i < MaxEnemies; i++)
        {
            // Instantiate<T> creates a copy of the scene and casts its root.
            Node3D node = EnemyScene.Instantiate<Node3D>();
            node.Visible = false;
            AddChild(node);

            AnimationPlayer animationPlayer = node.GetNode<AnimationPlayer>("Model/AnimationPlayer");
            // Manual: the player only moves when we call Advance(), which
            // lets us step all enemies together at AnimationFps.
            animationPlayer.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;

            _nodes[i] = node;
            _animationPlayers[i] = animationPlayer;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        UpdatePopulation();
        if (_count == 0)
        {
            return;
        }

        BuildGrid();
        ComputeSeparation();
        MoveEnemies((float)delta);
    }

    public override void _Process(double delta)
    {
        // Same stepped animation idea as Player._Process, but one clock
        // drives every enemy, so they all update on the same beat.
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
        _nodes[i].Visible = true;

        // Start each enemy at a random point in its cycle so they don't all
        // hop in perfect unison.
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
    /// Removes enemy i. The last alive enemy is moved into its slot, so the
    /// alive enemies always stay packed at the start of the arrays ("swap
    /// remove"). Fast, but it changes the order: never keep an enemy index
    /// around across frames.
    /// </summary>
    public void Despawn(int i)
    {
        int last = _count - 1;

        // The node itself is parked and hidden. We swap the node and its
        // AnimationPlayer along with the data so every array stays lined up.
        Node3D deadNode = _nodes[i];
        AnimationPlayer deadPlayer = _animationPlayers[i];
        deadNode.Visible = false;

        _positions[i] = _positions[last];
        _nodes[i] = _nodes[last];
        _animationPlayers[i] = _animationPlayers[last];

        _nodes[last] = deadNode;
        _animationPlayers[last] = deadPlayer;
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
        float despawnSquared = DespawnDistance * DespawnDistance;

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
        }
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
