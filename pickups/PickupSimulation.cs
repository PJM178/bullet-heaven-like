using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The pickup game logic (coins, hearts, ...; not XP orbs): where every
/// pickup lies, which players touch which, and rolling loot. The "core"
/// half; its "shell" is <see cref="PickupManager"/>, which draws the
/// pickups and applies what players collect. Same split as
/// <see cref="EnemySimulation"/>, with slots for the shell's sprite nodes.
/// </summary>
public sealed class PickupSimulation
{
    /// <summary>Returned by <see cref="Drop"/> when there's no room.</summary>
    public const int NoPickup = -1;

    /// <summary>One pickup a player collected in the last Step.</summary>
    public struct Collection
    {
        public PickupDefinition Definition;
        public int Player; // index into the players passed to Step
    }

    /// <summary>One pickup rolled by <see cref="RollLoot"/>: what, and where.</summary>
    public struct LootRoll
    {
        public PickupDefinition Definition;
        public Vector3 Position;
    }

    // ---- Pickup data. Index i in every array is the same pickup. Only the
    // first _count entries are alive. ------------------------------------
    private readonly Vector3[] _positions;           // on the ground
    private readonly PickupDefinition[] _definitions;
    private readonly int[] _slot;                    // fixed slot, for the shell's sprite
    private int _count;

    // Free slots, used like a stack: the first _freeCount are free.
    private readonly int[] _freeSlots;
    private int _freeCount;

    // What happened in the last Step.
    private readonly Collection[] _collected;
    private int _collectedCount;
    private readonly int[] _removedSlots;
    private int _removedCount;

    // Godot's random number generator, because LootDrop.Roll takes one.
    private readonly RandomNumberGenerator _random = new();

    /// <param name="capacity">The most pickups that can exist at once.</param>
    public PickupSimulation(int capacity)
    {
        Capacity = Math.Max(capacity, 0);
        _positions = new Vector3[Capacity];
        _definitions = new PickupDefinition[Capacity];
        _slot = new int[Capacity];
        _collected = new Collection[Capacity];
        _removedSlots = new int[Capacity];
        _freeSlots = new int[Capacity];
        for (int n = 0; n < Capacity; n++)
        {
            _freeSlots[n] = Capacity - 1 - n; // so slot 0 is handed out first
        }
        _freeCount = Capacity;
    }

    /// <summary>How many pickups exist.</summary>
    public int Count => _count;

    /// <summary>The most pickups that can exist at once; also the number of slots.</summary>
    public int Capacity { get; }

    /// <summary>Pickups collected in the last Step, in the order they were
    /// touched.</summary>
    public ReadOnlySpan<Collection> Collected => _collected.AsSpan(0, _collectedCount);

    /// <summary>Slots of the pickups removed (collected) in the last Step.</summary>
    public ReadOnlySpan<int> RemovedSlots => _removedSlots.AsSpan(0, _removedCount);

    /// <summary>Ground positions of the pickups, by index.</summary>
    public ReadOnlySpan<Vector3> Positions => _positions.AsSpan(0, _count);

    /// <summary>The slot of pickup i.</summary>
    public int GetSlot(int i) => _slot[i];

    /// <summary>
    /// Puts a pickup of kind <paramref name="definition"/> on the ground at
    /// <paramref name="groundPosition"/>. Returns its index (valid until the
    /// next Step), or <see cref="NoPickup"/> when full or definition is null.
    /// </summary>
    public int Drop(PickupDefinition definition, Vector3 groundPosition)
    {
        if (definition == null || _freeCount == 0)
        {
            return NoPickup;
        }

        _freeCount--;
        int i = _count;
        _count++;
        _slot[i] = _freeSlots[_freeCount];
        _positions[i] = new Vector3(groundPosition.X, 0.0f, groundPosition.Z);
        _definitions[i] = definition;
        return i;
    }

    /// <summary>
    /// Rolls every line of <paramref name="table"/> and writes what comes
    /// up into results, each scattered at random within
    /// <paramref name="scatter"/> metres of <paramref name="centre"/>.
    /// Returns how many were rolled (at most results.Length). The one place
    /// loot is rolled; dropping the results is up to the caller.
    /// </summary>
    public int RollLoot(LootTable table, Vector3 centre, float scatter, LootRoll[] results)
    {
        if (table == null)
            return 0;

        int found = 0;
        foreach (LootDrop line in table.Drops)
        {
            // "?." gives null if line is null, and "?? 0" turns that into
            // 0, so an empty slot in the list drops nothing.
            int count = line?.Roll(_random) ?? 0;
            for (int n = 0; n < count && found < results.Length; n++)
            {
                // A random point in a circle. The square root spreads the
                // points evenly; without it they would bunch in the middle.
                float angle = _random.Randf() * Mathf.Tau;
                float distance = Mathf.Sqrt(_random.Randf()) * scatter;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle)) * distance;
                results[found] = new LootRoll { Definition = line.Pickup, Position = centre + offset };
                found++;
            }
        }
        return found;
    }

    /// <summary>
    /// Collects every pickup a player is touching.
    /// </summary>
    /// <param name="players">Where each player is. A player that can't
    /// collect (dead) should be left out or given a radius of 0.</param>
    /// <param name="pickupRadii">Each player's pickup radius, same order.</param>
    public void Step(ReadOnlySpan<Vector3> players, ReadOnlySpan<float> pickupRadii)
    {
        _collectedCount = 0;
        _removedCount = 0;

        // Backwards, because collecting moves the last pickup into the
        // collected one's index (swap remove); that one was already checked.
        for (int i = _count - 1; i >= 0; i--)
        {
            for (int p = 0; p < players.Length; p++)
            {
                // Distance on the ground only: height doesn't matter for touch.
                float dx = _positions[i].X - players[p].X;
                float dz = _positions[i].Z - players[p].Z;
                if (dx * dx + dz * dz <= pickupRadii[p] * pickupRadii[p])
                {
                    _collected[_collectedCount] = new Collection { Definition = _definitions[i], Player = p };
                    _collectedCount++;
                    Remove(i);
                    break; // gone: no other player can take it too
                }
            }
        }
    }

    /// <summary>
    /// Removes pickup i by moving the last pickup into its index; its slot
    /// goes back on the free stack.
    /// </summary>
    private void Remove(int i)
    {
        _freeSlots[_freeCount] = _slot[i];
        _freeCount++;
        _removedSlots[_removedCount] = _slot[i];
        _removedCount++;

        int last = _count - 1;
        _positions[i] = _positions[last];
        _definitions[i] = _definitions[last];
        _slot[i] = _slot[last];
        _definitions[last] = null; // don't keep the definition alive for nothing
        _count--;
    }
}
