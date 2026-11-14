using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The damage side of a wave that spreads out from a point: a full ring
/// (like the hammer's shockwave) or an arc, a slice of the ring facing one
/// way (a frontal wave or a cone blast).
///
/// It remembers how far out it has already reached. Each physics step the
/// weapon tells it where the edge is now (SweepTo), and it damages the
/// enemies in the newly covered band, plus a thin strip just behind the
/// edge (CatchUp). The strip catches enemies walking towards the centre:
/// in one step they can cross from just outside the edge to inside where it
/// was, and would otherwise never be in a checked band. The wave remembers
/// the IDs of enemies it has hit, so the strip never hits anyone twice.
///
/// Mechanics only: how big the wave is at each moment, when it ends and
/// what it looks like are the weapon's decisions.
///
/// Make one per wave a weapon can have going at once, when the weapon is
/// set up (e.g. one per pooled cast), and reuse it with StartRing/StartArc.
/// Nothing is allocated after that.
/// </summary>
public sealed class ExpandingWave
{
    // The most enemies one step can hit. The ring query only returns the
    // newly covered band, so this is plenty.
    private const int MaxTargets = 64;

    // How far behind the edge to look again each step, in metres. More than
    // enemies move in a step (Speed 2 m/s is about 0.03 m per step), with
    // room for being shoved by the crowd.
    private const float CatchUp = 0.2f;

    // The most enemies one wave remembers hitting. A wave in a huge crowd
    // that goes past this could hit a few twice, which is harmless.
    private const int MaxRemembered = 256;

    private readonly EnemyManager _enemies;
    private readonly BreakableManager _breakables; // may be null
    private readonly int[] _targets = new int[MaxTargets];
    // IDs (EnemyManager.GetId) of the enemies this wave has hit.
    private readonly int[] _hitIds = new int[MaxRemembered];
    private int _hitCount;

    private Vector3 _centre;
    private float _damage;
    // How far out it has already damaged, in metres.
    private float _sweptRadius;
    // Where it started: the catch-up strip never reaches inside this, so
    // enemies a weapon's own blast already hit there are left alone.
    private float _startRadius;
    // For an arc: the way it faces (flat on the ground, length 1) and half
    // its width in radians.
    private bool _isArc;
    private Vector3 _direction;
    private float _halfAngle;

    /// <summary>True from Start until Stop.</summary>
    // "{ get; private set; }": anyone can read it, only this class changes it.
    public bool Active { get; private set; }

    // "= null" makes the second argument optional: a weapon without props
    // to break can leave it out.
    public ExpandingWave(EnemyManager enemies, BreakableManager breakables = null)
    {
        _enemies = enemies;
        _breakables = breakables;
    }

    /// <summary>
    /// Starts a full ring at centre. Enemies within startRadius aren't hit,
    /// e.g. because the blast in the middle already hit them. Each enemy
    /// the edge passes takes damage.
    /// </summary>
    public void StartRing(Vector3 centre, float startRadius, float damage)
    {
        _centre = centre;
        _sweptRadius = startRadius;
        _startRadius = startRadius;
        _damage = damage;
        _hitCount = 0;
        _isArc = false;
        Active = damage > 0.0f;
    }

    /// <summary>
    /// Starts an arc facing direction, angleDegrees wide in total (90 is a
    /// quarter of the ring; 360 or more is a full ring). Otherwise like
    /// StartRing.
    /// </summary>
    public void StartArc(Vector3 centre, float startRadius, float damage, Vector3 direction, float angleDegrees)
    {
        StartRing(centre, startRadius, damage);
        // Flatten onto the ground, so only the left-right angle counts.
        direction.Y = 0.0f;
        _direction = direction.Normalized();
        _halfAngle = Mathf.DegToRad(angleDegrees * 0.5f);
        _isArc = angleDegrees < 360.0f;
    }

    /// <summary>Ends the wave: SweepTo does nothing until the next Start.</summary>
    public void Stop()
    {
        Active = false;
    }

    /// <summary>
    /// Moves the edge out to radius and damages the enemies it passed on
    /// the way. A radius not beyond the edge does nothing (the wave hasn't
    /// moved on yet). Returns how many enemies were hit.
    /// </summary>
    public int SweepTo(float radius)
    {
        if (!Active || radius <= _sweptRadius || _enemies == null)
        {
            return 0;
        }

        float inner = Mathf.Max(_startRadius, _sweptRadius - CatchUp);
        int count = _enemies.QueryRing(_centre, inner, radius, _targets);
        int hits = 0;
        for (int n = 0; n < count; n++)
        {
            int i = _targets[n];
            int id = _enemies.GetId(i);
            // Array.IndexOf searches the first _hitCount entries; -1 means
            // not found. The list is short, so a plain search is fine.
            if (Array.IndexOf(_hitIds, id, 0, _hitCount) >= 0)
            {
                continue; // hit on an earlier step
            }
            if (_isArc && !InArc(_enemies.GetPosition(i), _enemies.EnemyRadius))
            {
                continue;
            }
            _enemies.Damage(i, _damage);
            hits++;
            if (_hitCount < MaxRemembered)
            {
                _hitIds[_hitCount] = id;
                _hitCount++;
            }
        }

        // Props don't walk into the wave, so they need no catch-up strip:
        // only the newly covered band, which passes each prop exactly once.
        _breakables?.DamageRing(_centre, _sweptRadius, radius, _damage, _isArc, _direction, _halfAngle);

        _sweptRadius = radius;
        return hits;
    }

    /// <summary>
    /// Whether an enemy at position, with a body of bodyRadius, overlaps the
    /// arc's slice. The ring query has already checked the distance; this
    /// checks the angle.
    /// </summary>
    private bool InArc(Vector3 position, float bodyRadius)
    {
        Vector3 offset = position - _centre;
        offset.Y = 0.0f;
        float distance = offset.Length();
        if (distance <= bodyRadius)
        {
            return true; // its body covers the centre, so every direction
        }

        // The angle between the arc's facing and the enemy. The dot product
        // of two length-1 directions is the cosine of the angle between
        // them; Acos turns it back into an angle. Clamp guards against
        // rounding nudging it just past 1.
        float cosine = Mathf.Clamp(offset.Dot(_direction) / distance, -1.0f, 1.0f);
        float angle = Mathf.Acos(cosine);

        // The enemy's body widens it by the angle its radius covers at this
        // distance, so a slime touching the arc's side edge still counts.
        float bodyAngle = Mathf.Asin(bodyRadius / distance);
        return angle <= _halfAngle + bodyAngle;
    }
}
