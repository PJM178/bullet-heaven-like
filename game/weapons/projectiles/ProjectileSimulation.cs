using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The projectile game logic: every projectile in flight, how it moves and
/// what it hits. The "core" half of the projectile system; its "shell" is
/// <see cref="ProjectileManager"/>, the node that owns one of these and
/// draws what it says. Same split as <see cref="EnemySimulation"/>.
///
/// This is the only place projectiles move, so everything a projectile has
/// to respect is handled once, here: walls stop it, it hits enemies (each
/// one once, up to its Pierce), props stop it, a returning one flies back
/// to the player who threw it.
///
/// It never touches nodes. Walls, enemies and props come in as interfaces
/// (<see cref="IObstacleQuery"/>, <see cref="IEnemyTargets"/>,
/// <see cref="IBreakableTargets"/>), players as positions passed to
/// <see cref="Step"/>, and the projectiles that ended go out as a list of
/// slots (see <see cref="RemovedSlots"/>).
/// </summary>
public sealed class ProjectileSimulation
{
    /// <summary>Returned by <see cref="Spawn"/> when the shot was skipped.</summary>
    public const int NoProjectile = -1;

    /// <summary>For <see cref="Spawn"/>: the projectile returns to nobody.</summary>
    public const int NoPlayer = -1;

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

    // What projectiles can run into. Each may be null (nothing of that sort).
    private readonly IObstacleQuery _obstacles;
    private readonly IEnemyTargets _enemies;
    private readonly IBreakableTargets _breakables;

    // ---- Projectile data. Index i in every array is the same projectile.
    // Only the first _count entries are in flight. --------------------------
    private readonly ProjectileDefinition[] _definitions;
    private readonly Vector3[] _positions;  // on the ground; drawn Height above it
    private readonly Vector3[] _directions; // flat, length 1; where it is heading
    private readonly float[] _travelled;    // metres flown so far
    private readonly int[] _hitsLeft;       // enemies it may still hit; -1 = no limit
    private readonly bool[] _returning;     // on its way back
    private readonly int[] _returnTo;       // player index it flies back to, or NoPlayer
    private readonly int[] _hitIds;         // HitMemory enemy ids per projectile
    private readonly int[] _hitCount;       // how many of its ids are filled
    private readonly int[] _nextHitSlot;    // where its next id goes (wraps)
    private readonly int[] _slot;           // fixed slot, for the shell's nodes
    private int _count;

    // Free slots, used like a stack: the first _freeCount are free. Slots
    // work as in EnemySimulation: one per node the shell keeps, so the
    // shell's node for a projectile never has to move when others are
    // removed.
    private readonly int[] _freeSlots;
    private int _freeCount;

    // Slots of projectiles that ended during the last Step.
    private readonly int[] _removedSlots;
    private int _removedCount;

    // Overlapping enemies found by one hit query.
    private readonly int[] _query = new int[32];

    /// <summary>
    /// Sets up an empty simulation with room for capacity projectiles.
    /// Any of the three targets may be null.
    /// </summary>
    public ProjectileSimulation(int capacity, IObstacleQuery obstacles,
        IEnemyTargets enemies, IBreakableTargets breakables)
    {
        Capacity = Math.Max(capacity, 0);
        _obstacles = obstacles;
        _enemies = enemies;
        _breakables = breakables;

        _definitions = new ProjectileDefinition[Capacity];
        _positions = new Vector3[Capacity];
        _directions = new Vector3[Capacity];
        _travelled = new float[Capacity];
        _hitsLeft = new int[Capacity];
        _returning = new bool[Capacity];
        _returnTo = new int[Capacity];
        _hitIds = new int[Capacity * HitMemory];
        _hitCount = new int[Capacity];
        _nextHitSlot = new int[Capacity];
        _slot = new int[Capacity];
        _removedSlots = new int[Capacity];

        _freeSlots = new int[Capacity];
        for (int n = 0; n < Capacity; n++)
        {
            _freeSlots[n] = Capacity - 1 - n; // so slot 0 is handed out first
        }
        _freeCount = Capacity;
    }

    /// <summary>How many projectiles are in flight.</summary>
    public int Count => _count;

    /// <summary>The most projectiles that can be in flight at once; also
    /// the number of slots.</summary>
    public int Capacity { get; }

    /// <summary>Slots of the projectiles that ended in the last Step. Valid
    /// until the next Step.</summary>
    public ReadOnlySpan<int> RemovedSlots => _removedSlots.AsSpan(0, _removedCount);

    /// <summary>Ground positions of the projectiles in flight, by index.</summary>
    public ReadOnlySpan<Vector3> Positions => _positions.AsSpan(0, _count);

    /// <summary>Flight directions (flat, length 1), by index.</summary>
    public ReadOnlySpan<Vector3> Directions => _directions.AsSpan(0, _count);

    /// <summary>The slot of projectile i.</summary>
    public int GetSlot(int i) => _slot[i];

    /// <summary>The definition of projectile i.</summary>
    public ProjectileDefinition GetDefinition(int i) => _definitions[i];

    /// <summary>
    /// Launches one projectile of the given kind from position (on the
    /// ground), flying in direction (flat, length 1). A projectile that
    /// Returns flies back to player <paramref name="returnTo"/> (an index
    /// into the players passed to Step), or ends at its Range with
    /// <see cref="NoPlayer"/>. Returns the new projectile's index (valid
    /// until the next Step), or <see cref="NoProjectile"/> if it is full.
    /// </summary>
    public int Spawn(ProjectileDefinition definition, Vector3 position, Vector3 direction,
                     int returnTo = NoPlayer)
    {
        if (definition == null || _freeCount == 0)
        {
            return NoProjectile;
        }

        _freeCount--;
        int i = _count;
        _count++;
        _slot[i] = _freeSlots[_freeCount];
        _definitions[i] = definition;
        _positions[i] = new Vector3(position.X, 0.0f, position.Z);
        _directions[i] = direction;
        _travelled[i] = 0.0f;
        // Pierce 0 means one hit; -1 means no limit.
        _hitsLeft[i] = definition.Pierce < 0 ? -1 : definition.Pierce + 1;
        _returning[i] = false;
        _returnTo[i] = returnTo;
        _hitCount[i] = 0;
        _nextHitSlot[i] = 0;
        return i;
    }

    /// <summary>
    /// Moves every projectile one physics step and applies its hits.
    /// Call it after the enemies have moved this step, so hits see where
    /// they are now.
    /// </summary>
    /// <param name="delta">Seconds since the last step.</param>
    /// <param name="players">Where each player is, for returning
    /// projectiles (index = the returnTo given to Spawn).</param>
    public void Step(float delta, ReadOnlySpan<Vector3> players)
    {
        _removedCount = 0;

        // Loop backwards, because Despawn moves the last projectile into
        // the freed index. Going from the end, that moved projectile has
        // already been updated this step, so nothing is skipped or updated
        // twice.
        for (int i = _count - 1; i >= 0; i--)
        {
            if (!Fly(i, delta, players) || !Hit(i))
            {
                Despawn(i);
            }
        }
    }

    /// <summary>
    /// Moves projectile i one step. Returns false when it is done: flown its
    /// range, caught by whoever threw it, or stopped by a wall.
    /// </summary>
    private bool Fly(int i, float dt, ReadOnlySpan<Vector3> players)
    {
        ProjectileDefinition definition = _definitions[i];
        float move = definition.Speed * dt;
        Vector3 previous = _positions[i];

        if (_returning[i])
        {
            int player = _returnTo[i];
            // The player may have left (fewer players than when it was thrown).
            if (player < 0 || player >= players.Length)
            {
                return false;
            }
            Vector3 toThrower = players[player] - _positions[i];
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
        if (_obstacles != null && _obstacles.IsLineBlocked(previous, _positions[i]))
        {
            return false;
        }

        if (!_returning[i] && _travelled[i] >= definition.Range)
        {
            if (!definition.Returns || _returnTo[i] == NoPlayer)
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
        ProjectileDefinition definition = _definitions[i];

        // Hits are checked at the new position, not along the path since
        // the last step. Fine while a projectile moves less per step than
        // its reach (a 14 m/s shard moves 0.23 m per step against a 0.4 m
        // reach). Walls are tested along the path in Fly.
        if (_enemies != null)
        {
            // QueryCircle leaves out enemies behind a wall from here.
            int count = _enemies.QueryCircle(_positions[i], definition.Radius, _query);
            for (int n = 0; n < count; n++)
            {
                int enemy = _query[n];
                int id = _enemies.GetId(enemy);
                if (HasHit(i, id))
                {
                    continue; // already hit by this projectile
                }

                _enemies.Damage(enemy, definition.Damage);
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
        if (_breakables != null
            && _breakables.HitTest(_positions[i], definition.Radius, definition.Damage))
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
        _hitCount[i] = Math.Min(_hitCount[i] + 1, HitMemory);
    }

    /// <summary>
    /// Removes projectile i with a "swap remove": the last projectile in
    /// flight moves into index i, so the live ones stay packed at the start
    /// of the arrays. Its slot goes back on the free stack. Never keep a
    /// projectile index across steps.
    /// </summary>
    private void Despawn(int i)
    {
        _freeSlots[_freeCount] = _slot[i];
        _freeCount++;
        _removedSlots[_removedCount] = _slot[i];
        _removedCount++;

        int last = _count - 1;
        _definitions[i] = _definitions[last];
        _positions[i] = _positions[last];
        _directions[i] = _directions[last];
        _travelled[i] = _travelled[last];
        _hitsLeft[i] = _hitsLeft[last];
        _returning[i] = _returning[last];
        _returnTo[i] = _returnTo[last];
        _hitCount[i] = _hitCount[last];
        _nextHitSlot[i] = _nextHitSlot[last];
        _slot[i] = _slot[last];
        // The hit memory is a block of HitMemory ids per projectile: copy
        // the last one's block into i's.
        Array.Copy(_hitIds, last * HitMemory, _hitIds, i * HitMemory, HitMemory);
        // Let go of the definition, so nothing keeps it alive by accident.
        _definitions[last] = null;
        _count--;
    }
}
