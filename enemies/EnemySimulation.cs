using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The enemy game logic: where every enemy is, how it moves, how it is hit
/// and when it dies. The "core" half of the enemy system; its "shell" is
/// <see cref="EnemyManager"/>, the node that owns one of these and draws
/// what it says.
///
/// The core never touches nodes. Everything it needs comes in as data
/// (player positions, delta, the obstacle query), and everything that
/// happened goes out as data (positions, spawned and removed enemies,
/// contact damage, the result of a hit). That is what lets the same logic
/// run on a server that draws nothing, chase several players, and later
/// tell other players' games what happened.
///
/// It is a plain C# class, not a Node, so it has no _Ready or
/// _PhysicsProcess: whoever owns it calls <see cref="Step"/> once per
/// physics tick. ("sealed" means no class may inherit from it.)
/// It still uses Godot's math types (Vector3, Mathf), by choice.
/// </summary>
public sealed class EnemySimulation
{
    /// <summary>What a call to <see cref="Damage"/> did.</summary>
    public enum DamageResult
    {
        None,   // bad index, or the enemy was already dead
        Hit,    // took damage and lives on
        Killed, // this hit killed it
    }

    // ---- Settings. EnemyManager copies its [Export] values in here every
    // step, so changing them in the editor or from the HUD still works. ----
    public int TargetCount = 50;
    public int MaxSpawnsPerFrame = 10;
    public float SpawnDistance = 14.0f;
    public float DespawnDistance = 24.0f;
    public float SeparationRadius = 0.6f;
    public float SeparationStrength = 6.0f;
    public float StopDistance = 0.7f;
    public float ArriveSlowdown = 1.0f;

    // ---- Kinds. Index k in each array is the same kind. ------------------
    // "readonly" on a field means it can only be set in the constructor.
    // (The array's contents can still change; only the field can't be
    // pointed at another array.)
    private readonly EnemyDefinition[] _definitions; // null = kind never spawns
    private readonly float[] _weights;

    // ---- Slots. Every enemy that can exist has a fixed slot number for its
    // whole life, unlike its index (which changes when others are removed).
    // Slots are numbered kind by kind in the order of the capacities given
    // to the constructor, so the shell can keep one node per slot. Each
    // kind's free slots are kept as a stack: the first _freeCount[k] are free.
    private readonly int[][] _freeSlots;
    private readonly int[] _freeCount;

    private readonly IObstacleQuery _obstacles; // null = no walls
    private readonly Random _random;

    // The largest HitRadius of any kind: how far around a point the hit
    // queries have to look in the grid.
    private readonly float _maxHitRadius;

    // ---- Live enemy data. Index i in every array is the same live enemy.
    // Only the first _count entries are used. ------------------------------
    private readonly Vector3[] _positions;
    private readonly Vector3[] _velocities;  // last step's move, for facing
    private readonly Vector3[] _headings;    // toward the player it chases
    private readonly Vector3[] _separation;  // per-step push from neighbours
    private readonly float[] _health;
    private readonly bool[] _dead;           // killed, removed next step
    private readonly int[] _kind;            // index into the kind arrays
    private readonly int[] _slot;
    // A number unique to each enemy for its whole life. Unlike a slot it is
    // never reused, so code can remember "I already hit this one" across
    // steps without mixing it up with a later enemy in the same slot.
    private readonly int[] _ids;
    private int _nextId;
    private int _count;

    // ---- What happened during the last Step, for the shell. Arrays made
    // once and reused; only the first ...Count entries are valid. ----------
    private readonly int[] _spawned;      // indices of enemies spawned
    private int _spawnedCount;
    private readonly int[] _removedSlots; // slots of enemies removed
    private int _removedCount;

    // ---- Spatial grid (see BuildGrid). -----------------------------------
    // The ground is divided into square cells. Each step we sort enemies by
    // cell, so an enemy only needs to check the enemies in the 3x3 cells
    // around it rather than all of them. That turns an N*N cost into
    // roughly N.
    //
    // Cells are mapped into a fixed-size table with a hash, so the grid
    // needs no fixed world size and never allocates. Two far-apart cells can
    // occasionally share a slot; that only costs a few extra distance checks.
    private const int GridTableSize = 4096;  // a power of two, see CellIndex
    private readonly int[] _cellStart;    // where each table slot's enemies begin in _cellEntries
    private readonly int[] _cellCount;    // how many enemies are in each table slot
    private readonly int[] _cellEntries;  // enemy indices, grouped by table slot
    private readonly int[] _enemyCell;    // which table slot each enemy is in this step

    /// <summary>
    /// Sets up an empty simulation. All arrays are made here, once.
    /// </summary>
    /// <param name="definitions">One per kind of enemy (a null entry never spawns).</param>
    /// <param name="weights">How often each kind spawns compared to the others.</param>
    /// <param name="capacities">How many of each kind can be alive at once.</param>
    /// <param name="obstacles">Walls and props, or null for none.</param>
    /// <param name="random">Random numbers to use; null for .NET's shared one.
    /// A seeded one (new Random(seed)) makes a run's spawns repeatable.</param>
    public EnemySimulation(EnemyDefinition[] definitions, float[] weights, int[] capacities,
        IObstacleQuery obstacles, Random random = null)
    {
        _definitions = definitions;
        _weights = weights;
        _obstacles = obstacles;
        // "??" picks the right side when the left one is null.
        _random = random ?? Random.Shared;

        // Hand out slot numbers kind by kind.
        int kinds = definitions.Length;
        _freeSlots = new int[kinds][];
        _freeCount = new int[kinds];
        int capacity = 0;
        for (int k = 0; k < kinds; k++)
        {
            int size = definitions[k] != null ? Math.Max(capacities[k], 0) : 0;
            _freeSlots[k] = new int[size];
            // Filled backwards, so the stack pops the kind's first slot first.
            for (int n = 0; n < size; n++)
            {
                _freeSlots[k][n] = capacity + size - 1 - n;
            }
            _freeCount[k] = size;
            capacity += size;

            if (definitions[k] != null)
                _maxHitRadius = Mathf.Max(_maxHitRadius, definitions[k].HitRadius);
        }
        Capacity = capacity;

        _positions = new Vector3[capacity];
        _velocities = new Vector3[capacity];
        _headings = new Vector3[capacity];
        _separation = new Vector3[capacity];
        _health = new float[capacity];
        _dead = new bool[capacity];
        _kind = new int[capacity];
        _slot = new int[capacity];
        _ids = new int[capacity];
        _spawned = new int[capacity];
        _removedSlots = new int[capacity];
        _enemyCell = new int[capacity];
        _cellEntries = new int[capacity];
        _cellStart = new int[GridTableSize];
        _cellCount = new int[GridTableSize];
    }

    /// <summary>How many enemies are alive.</summary>
    public int Count => _count;

    /// <summary>The most enemies that can be alive at once: every kind's
    /// capacity together. Also the number of slots.</summary>
    public int Capacity { get; }

    /// <summary>How many enemies have been killed in total.</summary>
    public int Kills { get; private set; }

    // ---- Read-only views for the shell. A ReadOnlySpan is a window onto
    // part of an array: reading through it costs nothing and allocates
    // nothing, and the caller can't write through it. ----------------------

    /// <summary>Ground positions of the live enemies, by index.</summary>
    public ReadOnlySpan<Vector3> Positions => _positions.AsSpan(0, _count);

    /// <summary>Each live enemy's movement in the last step (metres per second).</summary>
    public ReadOnlySpan<Vector3> Velocities => _velocities.AsSpan(0, _count);

    /// <summary>Each live enemy's direction toward the player it chases (not normalized).</summary>
    public ReadOnlySpan<Vector3> Headings => _headings.AsSpan(0, _count);

    /// <summary>Indices of the enemies spawned in the last Step. Valid until
    /// the next Step (enemies are only removed at the start of a step).</summary>
    public ReadOnlySpan<int> Spawned => _spawned.AsSpan(0, _spawnedCount);

    /// <summary>Slots of the enemies removed in the last Step: killed ones,
    /// and extra ones when TargetCount went down. A slot can be in both
    /// lists in the same step (freed, then reused at once).</summary>
    public ReadOnlySpan<int> RemovedSlots => _removedSlots.AsSpan(0, _removedCount);

    /// <summary>The slot of live enemy i.</summary>
    public int GetSlot(int i) => _slot[i];

    /// <summary>The kind (index into the definitions) of live enemy i.</summary>
    public int GetKind(int i) => _kind[i];

    /// <summary>Whether live enemy i was killed this step (it is removed at
    /// the start of the next one).</summary>
    public bool IsDead(int i) => _dead[i];

    /// <summary>
    /// Advances the simulation by one physics tick.
    /// </summary>
    /// <param name="delta">Seconds since the last step.</param>
    /// <param name="players">Where every player is. Enemies chase the
    /// nearest one and spawn around a random one.</param>
    /// <param name="contactDamage">Filled in: for each player (same order),
    /// the most hearts any enemy touching them would take this step; 0 if
    /// none. Must be at least as long as <paramref name="players"/>.</param>
    public void Step(float delta, ReadOnlySpan<Vector3> players, Span<int> contactDamage)
    {
        _spawnedCount = 0;
        _removedCount = 0;
        contactDamage.Clear();

        // Enemies killed during the previous step are removed first, while
        // nothing else is looking at the arrays (see Damage).
        RemoveDead();

        if (players.Length > 0)
        {
            UpdatePopulation(players);
            BuildGrid();
            ComputeSeparation();
            MoveEnemies(delta, players, contactDamage);
        }

        // Rebuild the grid from the positions enemies have just moved to, so
        // the hit queries (called by weapons later in this same physics
        // tick) look in the right cells. Also when there are no enemies, so
        // queries never see last step's grid. Cheap: two passes over the
        // enemies plus one over the table.
        BuildGrid();
    }

    // ---- Spawning and removing --------------------------------------------

    /// <summary>
    /// Spawns or removes a few enemies to approach TargetCount.
    /// </summary>
    private void UpdatePopulation(ReadOnlySpan<Vector3> players)
    {
        // Math.Clamp keeps the value inside [min, max].
        int wanted = Math.Clamp(TargetCount, 0, Capacity);

        for (int n = 0; n < MaxSpawnsPerFrame && _count < wanted; n++)
        {
            if (!Spawn(players))
            {
                break; // every kind is full
            }
        }
        for (int n = 0; n < MaxSpawnsPerFrame && _count > wanted; n++)
        {
            Despawn(_count - 1);
        }
    }

    /// <summary>
    /// Picks a kind by its weight among those with free slots left.
    /// Returns its index, or -1 when every kind is full.
    /// </summary>
    private int PickKind()
    {
        float total = 0.0f;
        for (int k = 0; k < _definitions.Length; k++)
        {
            if (_freeCount[k] > 0)
                total += Mathf.Max(_weights[k], 0.0f);
        }
        if (total <= 0.0f)
            return -1;

        // A random point along the weights laid end to end; the kind whose
        // stretch it lands in wins.
        float roll = _random.NextSingle() * total;
        int last = -1;
        for (int k = 0; k < _definitions.Length; k++)
        {
            if (_freeCount[k] == 0 || _weights[k] <= 0.0f)
                continue;
            last = k;
            roll -= _weights[k];
            if (roll < 0.0f)
                return k;
        }
        return last; // rounding left a sliver at the end
    }

    /// <summary>
    /// Takes a free slot of a picked kind and places a new enemy on the
    /// spawn ring. Returns false when every kind is full.
    /// </summary>
    private bool Spawn(ReadOnlySpan<Vector3> players)
    {
        int k = PickKind();
        if (k < 0)
        {
            return false;
        }

        // Pop the top of the kind's stack of free slots.
        _freeCount[k]--;
        int slot = _freeSlots[k][_freeCount[k]];

        int i = _count;
        _count++;
        _slot[i] = slot;
        _kind[i] = k;
        _positions[i] = RandomPointOnSpawnRing(players);
        _velocities[i] = Vector3.Zero;
        _headings[i] = Vector3.Zero;
        _health[i] = _definitions[k].MaxHealth;
        _dead[i] = false;
        _ids[i] = _nextId;
        _nextId++;

        _spawned[_spawnedCount] = i;
        _spawnedCount++;
        return true;
    }

    /// <summary>
    /// Removes every enemy marked dead by Damage. UpdatePopulation then
    /// spawns replacements on the ring.
    /// </summary>
    private void RemoveDead()
    {
        // Backwards: Despawn moves the last enemy into the freed index, and
        // going from the end that enemy has already been checked.
        for (int i = _count - 1; i >= 0; i--)
        {
            if (_dead[i])
            {
                Despawn(i);
            }
        }
    }

    /// <summary>
    /// Removes enemy i: its slot goes back to its kind's free stack, and the
    /// last live enemy is moved into index i, so the live enemies always
    /// stay packed at the start of the arrays ("swap remove"). Fast, but it
    /// changes the order: never keep an enemy index around across steps.
    ///
    /// Private, because removing an enemy in the middle of a physics step
    /// would break the grid. Other code kills enemies through Damage.
    /// </summary>
    private void Despawn(int i)
    {
        int k = _kind[i];
        _freeSlots[k][_freeCount[k]] = _slot[i];
        _freeCount[k]++;
        _removedSlots[_removedCount] = _slot[i];
        _removedCount++;

        int last = _count - 1;
        _positions[i] = _positions[last];
        _velocities[i] = _velocities[last];
        _headings[i] = _headings[last];
        _health[i] = _health[last];
        _dead[i] = _dead[last];
        _kind[i] = _kind[last];
        _slot[i] = _slot[last];
        _ids[i] = _ids[last];
        _count--;
    }

    private Vector3 RandomPointOnSpawnRing(ReadOnlySpan<Vector3> players)
    {
        Vector3 center = players[_random.Next(players.Length)];
        float angle = _random.NextSingle() * Mathf.Tau;
        return new Vector3(
            center.X + Mathf.Cos(angle) * SpawnDistance,
            0.0f,
            center.Z + Mathf.Sin(angle) * SpawnDistance);
    }

    // ---- Moving -------------------------------------------------------------

    /// <summary>
    /// Sorts the live enemies into grid cells (a "counting sort"): count
    /// how many land in each table slot, work out where each one's run
    /// starts, then drop every enemy into place.
    /// </summary>
    private void BuildGrid()
    {
        // Array.Clear resets every entry to 0 without allocating.
        Array.Clear(_cellCount);

        for (int i = 0; i < _count; i++)
        {
            int cell = CellIndex(CellCoord(_positions[i].X), CellCoord(_positions[i].Z));
            _enemyCell[i] = cell;
            _cellCount[cell]++;
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
            int cell = _enemyCell[i];
            _cellEntries[_cellStart[cell] + _cellCount[cell]] = i;
            _cellCount[cell]++;
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
                    int cell = CellIndex(cellX + dx, cellZ + dz);
                    int start = _cellStart[cell];
                    int end = start + _cellCount[cell];

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
    /// Steers every enemy toward the nearest player, applies separation and
    /// walls, and works out which players are being touched.
    /// </summary>
    private void MoveEnemies(float delta, ReadOnlySpan<Vector3> players, Span<int> contactDamage)
    {
        float despawnSquared = DespawnDistance * DespawnDistance;

        for (int i = 0; i < _count; i++)
        {
            EnemyDefinition definition = _definitions[_kind[i]];
            Vector3 position = _positions[i];
            float contactSquared = definition.ContactRange * definition.ContactRange;

            // Find the nearest player, and on the way note every player this
            // enemy is touching. With at most a handful of players this
            // small loop costs next to nothing.
            int nearest = 0;
            float nearestSquared = float.MaxValue;
            for (int p = 0; p < players.Length; p++)
            {
                Vector3 toPlayer = players[p] - position;
                toPlayer.Y = 0.0f;
                float distanceSquared = toPlayer.LengthSquared();
                if (distanceSquared < nearestSquared)
                {
                    nearestSquared = distanceSquared;
                    nearest = p;
                }

                // Touching only counts with no wall in between. The wall
                // check runs last, only for the few enemies already this
                // close that would raise the damage.
                if (distanceSquared < contactSquared
                    && definition.ContactDamage > contactDamage[p]
                    && (_obstacles == null || !_obstacles.IsLineBlocked(position, players[p])))
                {
                    contactDamage[p] = definition.ContactDamage;
                }
            }

            // Left far behind by every player: put it back on a ring around
            // one instead of letting it trudge all the way back.
            if (nearestSquared > despawnSquared)
            {
                _positions[i] = RandomPointOnSpawnRing(players);
                continue;
            }

            Vector3 toTarget = players[nearest] - position;
            toTarget.Y = 0.0f;

            // 0 at StopDistance, rising to 1 (full speed) ArriveSlowdown
            // metres further out.
            float distance = Mathf.Sqrt(nearestSquared);
            float chase = Mathf.Clamp((distance - StopDistance) / ArriveSlowdown, 0.0f, 1.0f);

            Vector3 velocity = toTarget.Normalized() * (definition.Speed * chase) + _separation[i] * SeparationStrength;
            position += velocity * delta;
            position.Y = 0.0f;
            // Step back out of any wall or prop it just walked into. Doing
            // it after the move (not by changing the velocity) also frees
            // enemies that spawned inside something.
            if (_obstacles != null)
            {
                position = _obstacles.PushOut(position, definition.BodyRadius);
            }

            _positions[i] = position;
            _velocities[i] = velocity;
            _headings[i] = toTarget;
        }
    }

    // ---- Hits -----------------------------------------------------------------

    /// <summary>
    /// Deals damage to enemy i (an index from a query, used in the same
    /// physics step). An enemy whose health drops to 0 is marked dead but
    /// stays in the arrays until the start of the next step. Removing it
    /// right away would swap another enemy into its index and leave the grid
    /// pointing at the wrong enemies while other weapons are still using it.
    /// </summary>
    public DamageResult Damage(int i, float amount)
    {
        // Ignore bad indices and enemies that are already dead, so a
        // second hit in the same step can't count a kill twice.
        if (i < 0 || i >= _count || _dead[i])
        {
            return DamageResult.None;
        }

        _health[i] -= amount;
        if (_health[i] > 0.0f)
        {
            return DamageResult.Hit;
        }

        _dead[i] = true;
        Kills++;
        return DamageResult.Killed;
    }

    // ---- Queries for weapons ------------------------------------------------

    /// <summary>
    /// How big live enemy i is for hits, in metres on the ground (its
    /// definition's HitRadius). Only valid during the current physics step.
    /// </summary>
    public float GetHitRadius(int i) => _definitions[_kind[i]].HitRadius;

    /// <summary>The definition of live enemy i's kind.</summary>
    public EnemyDefinition GetDefinition(int i) => _definitions[_kind[i]];

    /// <summary>
    /// Whether an attack from <paramref name="from"/> can reach live enemy i:
    /// no wall (an obstacle that blocks attacks) on the straight line between
    /// them. The hit queries (QueryCircle, QueryRing, HitTest) and FindClosest
    /// already apply it; call it directly only for a line they don't cover.
    /// Always true without obstacles.
    /// </summary>
    public bool CanReach(Vector3 from, int i)
    {
        return _obstacles == null || !_obstacles.IsLineBlocked(from, _positions[i]);
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
    /// Enemies behind a wall as seen from point are left out (see CanReach),
    /// so every area attack built on these queries is stopped by walls
    /// without having to check itself.
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
        // Look as far out as the biggest enemy could reach in from.
        int cellRange = Mathf.CeilToInt((outerRadius + _maxHitRadius) / SeparationRadius);
        int cellX = CellCoord(point.X);
        int cellZ = CellCoord(point.Z);

        for (int dz = -cellRange; dz <= cellRange; dz++)
        {
            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                int cell = CellIndex(cellX + dx, cellZ + dz);
                int start = _cellStart[cell];
                int end = start + _cellCount[cell];

                for (int e = start; e < end; e++)
                {
                    int j = _cellEntries[e];
                    if (_dead[j])
                    {
                        continue;
                    }

                    // Each kind has its own size, so the reach is worked
                    // out per enemy.
                    float hitRadius = _definitions[_kind[j]].HitRadius;
                    float reach = outerRadius + hitRadius;
                    // Enemies closer than this were already touching the
                    // inner circle. With no hole (innerRadius 0) nothing is
                    // excluded.
                    float innerReach = innerRadius > 0.0f ? innerRadius + hitRadius : 0.0f;
                    float offsetX = _positions[j].X - point.X;
                    float offsetZ = _positions[j].Z - point.Z;
                    float distanceSquared = offsetX * offsetX + offsetZ * offsetZ;
                    if (distanceSquared >= reach * reach || distanceSquared < innerReach * innerReach)
                    {
                        continue;
                    }

                    // Behind a wall from the query's centre. Tested after the
                    // distance, so only enemies actually caught pay for it.
                    if (!CanReach(point, j))
                    {
                        continue;
                    }

                    // Two far-apart cells can share a table slot (see
                    // CellIndex), so a wide search can visit the same one
                    // twice and meet the same enemy again. Skip repeats.
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
    /// height ignored) that is no further than maxRange and can be seen from
    /// point (no wall in between, see CanReach), or -1 if none is. Like
    /// every enemy index, only valid during the current physics step.
    ///
    /// A plain loop over every enemy: a few hundred distance checks are
    /// cheap, and unlike the grid it works at any range. The wall check is
    /// dearer, so it only runs for an enemy that would beat the best so far.
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
            if (distanceSquared < bestSquared && CanReach(point, i))
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
            // Behind a wall: can't be aimed at.
            if (!CanReach(point, i))
            {
                continue;
            }

            // Shift the further ones down one place (dropping the last when
            // full) until the new enemy's place is reached.
            int place = found < capacity ? found++ : found - 1;
            while (place > 0 && distances[place - 1] > distanceSquared)
            {
                distances[place] = distances[place - 1];
                results[place] = results[place - 1];
                place--;
            }
            distances[place] = distanceSquared;
            results[place] = i;
        }

        return found;
    }

    /// <summary>
    /// Returns the index of a live enemy overlapping the circle at point with
    /// the given radius (measured on the ground, height is ignored), or -1
    /// if there is none. Enemies behind a wall from point don't count.
    /// Only valid during the current physics step: enemy indices change
    /// when enemies are removed.
    /// </summary>
    public int HitTest(Vector3 point, float radius)
    {
        if (_count == 0)
        {
            return -1;
        }

        // How many cells out from the point's cell we have to look. Cells
        // are SeparationRadius wide, so with the default sizes this is 1
        // (the 3x3 block), but a big projectile or enemy would need more.
        int cellRange = Mathf.CeilToInt((radius + _maxHitRadius) / SeparationRadius);
        int cellX = CellCoord(point.X);
        int cellZ = CellCoord(point.Z);

        for (int dz = -cellRange; dz <= cellRange; dz++)
        {
            for (int dx = -cellRange; dx <= cellRange; dx++)
            {
                int cell = CellIndex(cellX + dx, cellZ + dz);
                int start = _cellStart[cell];
                int end = start + _cellCount[cell];

                for (int e = start; e < end; e++)
                {
                    int j = _cellEntries[e];
                    // Killed earlier this step but not removed yet: it is
                    // already invisible, so don't let it soak up shots.
                    if (_dead[j])
                    {
                        continue;
                    }

                    // The two circles overlap when their centres are closer
                    // than the two radii added together.
                    float reach = radius + _definitions[_kind[j]].HitRadius;
                    float offsetX = _positions[j].X - point.X;
                    float offsetZ = _positions[j].Z - point.Z;
                    if (offsetX * offsetX + offsetZ * offsetZ < reach * reach && CanReach(point, j))
                    {
                        return j;
                    }
                }
            }
        }

        return -1;
    }

    // ---- Grid helpers ---------------------------------------------------------

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
    private static int CellIndex(int x, int z)
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
