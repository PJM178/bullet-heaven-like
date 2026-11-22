using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The breakable prop game logic: which props exist, how much health they
/// have, which attacks catch them, and when they break. The "core" half;
/// its "shell" is <see cref="BreakableManager"/>, which draws the props and
/// does everything a hit or a break sets off (flash, pieces, dust, loot,
/// removing the prop's collision and obstacle). Same split as
/// <see cref="EnemySimulation"/>.
///
/// Props are few and never move, so each keeps its slot for the whole run
/// (no swap remove): a broken one is just marked broken. Here the slot *is*
/// the index, and it stays valid across frames, unlike an enemy index.
///
/// Nothing happens between hits, so there is no Step. Each damage call
/// (<see cref="DamageCircle"/>, <see cref="DamageArc"/>,
/// <see cref="DamageRing"/>, <see cref="HitTest"/>) records what it hit in
/// <see cref="Hits"/>, for the shell to act on straight after the call.
/// </summary>
public sealed class BreakableSimulation
{
    /// <summary>Returned by <see cref="Add"/> when there's no room.</summary>
    public const int NoProp = -1;

    /// <summary>One prop a damage call hit.</summary>
    public struct Hit
    {
        public int Slot;
        public bool Broke; // this hit broke it (otherwise it took damage and stands)
    }

    private readonly IObstacleQuery _obstacles; // null = no walls

    // ---- Prop data. Slot i in every array is the same prop. Only the
    // first _count slots are used. ----------------------------------------
    private readonly BreakableDefinition[] _definitions;
    private readonly Transform3D[] _transforms;
    private readonly float[] _health;
    private readonly bool[] _broken;
    private int _count;

    // What the last damage call hit.
    private readonly Hit[] _hits;
    private int _hitCount;

    /// <param name="capacity">The most props there can be.</param>
    /// <param name="obstacles">Walls that stop attacks reaching props, or null.</param>
    public BreakableSimulation(int capacity, IObstacleQuery obstacles)
    {
        Capacity = Math.Max(capacity, 0);
        _obstacles = obstacles;
        _definitions = new BreakableDefinition[Capacity];
        _transforms = new Transform3D[Capacity];
        _health = new float[Capacity];
        _broken = new bool[Capacity];
        _hits = new Hit[Capacity];
    }

    /// <summary>How many props have been added (broken ones too).</summary>
    public int Count => _count;

    /// <summary>The most props there can be.</summary>
    public int Capacity { get; }

    /// <summary>What the last damage call hit, in order. Valid until the next
    /// damage call.</summary>
    public ReadOnlySpan<Hit> Hits => _hits.AsSpan(0, _hitCount);

    public BreakableDefinition GetDefinition(int slot) => _definitions[slot];
    public Transform3D GetTransform(int slot) => _transforms[slot];
    public bool IsBroken(int slot) => _broken[slot];

    /// <summary>
    /// Adds a prop of kind <paramref name="definition"/>, placed, turned and
    /// scaled by <paramref name="transform"/> (its origin is the bottom
    /// centre of the prop). Returns its slot, or <see cref="NoProp"/> when
    /// full or definition is null.
    /// </summary>
    public int Add(BreakableDefinition definition, Transform3D transform)
    {
        if (definition == null || _count >= Capacity)
        {
            return NoProp;
        }

        int i = _count;
        _count++;
        _definitions[i] = definition;
        _transforms[i] = transform;
        _health[i] = definition.Health;
        _broken[i] = false;
        return i;
    }

    // ---- Hits. Weapons reach these through BreakableManager. --------------

    /// <summary>Damages every prop that overlaps a circle on the ground
    /// (a smash, an explosion).</summary>
    public void DamageCircle(Vector3 centre, float radius, float damage)
    {
        _hitCount = 0;
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
        _hitCount = 0;
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
        _hitCount = 0;
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
        _hitCount = 0;
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
        return _obstacles == null || !_obstacles.IsLineBlocked(from, _transforms[i].Origin);
    }

    private void Damage(int i, float amount)
    {
        _health[i] -= amount;
        bool broke = _health[i] <= 0.0f;
        if (broke)
        {
            _broken[i] = true;
        }
        _hits[_hitCount] = new Hit { Slot = i, Broke = broke };
        _hitCount++;
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
}
