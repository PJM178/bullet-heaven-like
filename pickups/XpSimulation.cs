using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The experience orb game logic: every orb, how it pops, hovers and flies
/// to a player, and how much XP each player gained. The "core" half; its
/// "shell" is <see cref="XpManager"/>, which draws the orbs and hands the
/// XP to the players. Same split as <see cref="EnemySimulation"/>.
///
/// An orb's life:
///   1. Pop: a kill throws one orb, worth the kill's XP, up and a little
///      to the side (<see cref="Drop"/>). It arcs under gravity until it
///      falls back to hover height.
///   2. Hover: it floats in place (the bob is only drawn, on the GPU).
///   3. Home: after a short pause, an orb within a player's magnet radius
///      flies at that player, speeding up, and is absorbed: the player
///      gains its XP. Orbs further away keep hovering.
///
/// Orbs have no nodes: the shell draws orb i as copy i of one MultiMesh.
/// So there are no slots here; instead the core lists which orbs moved (or
/// changed index) each step, so the shell only rewrites those.
/// </summary>
public sealed class XpSimulation
{
    /// <summary>Returned by <see cref="Drop"/> when the XP was folded into
    /// an existing orb instead of making a new one.</summary>
    public const int Merged = -1;

    // ---- Settings. XpManager copies its [Export] values in here every
    // step, so they can be changed while the game runs. ----------------------
    public float PopHeight = 0.5f;
    public float PopSpread = 1.0f;
    public float PopUp = 3.0f;
    public float Gravity = 14.0f;
    public float AirDrag = 2.0f;
    public float HoverHeight = 0.45f;
    public float HoverDelay = 0.35f;
    public float HomingStartSpeed = 1.5f;
    public float HomingAcceleration = 30.0f;
    public float AbsorbHeight = 0.9f;
    public float AbsorbRadius = 0.35f;

    // What an orb is doing. ": byte" stores it in one byte instead of the
    // default four; there are only three values.
    private enum OrbState : byte
    {
        Popping,
        Hovering,
        Homing,
    }

    // ---- Orb data. Index i in every array is the same orb. Only the
    // first _count entries are alive. ------------------------------------
    private readonly Vector3[] _positions;
    private readonly Vector3[] _velocities;
    private readonly OrbState[] _states;
    private readonly float[] _timers;  // Hovering: seconds left before it may home
    private readonly float[] _speeds;  // Homing: current speed toward its player
    private readonly int[] _targets;   // Homing: which player it flies to
    private readonly int[] _values;    // XP this orb is worth
    private int _count;

    // Orbs whose position (or index) changed in the last Step.
    private readonly int[] _changed;
    private int _changedCount;

    // When full, XP goes to the orb at this index next, moving round so one
    // orb doesn't swallow everything.
    private int _mergeCursor;

    private readonly Random _random;

    /// <param name="capacity">The most orbs that can exist at once.</param>
    /// <param name="random">Random numbers to use; null for .NET's shared one.</param>
    public XpSimulation(int capacity, Random random = null)
    {
        Capacity = Math.Max(capacity, 0);
        _random = random ?? Random.Shared;
        _positions = new Vector3[Capacity];
        _velocities = new Vector3[Capacity];
        _states = new OrbState[Capacity];
        _timers = new float[Capacity];
        _speeds = new float[Capacity];
        _targets = new int[Capacity];
        _values = new int[Capacity];
        _changed = new int[Capacity];
    }

    /// <summary>How many orbs exist.</summary>
    public int Count => _count;

    /// <summary>The most orbs that can exist at once.</summary>
    public int Capacity { get; }

    /// <summary>Positions of the orbs, by index (in the air, not on the ground).</summary>
    public ReadOnlySpan<Vector3> Positions => _positions.AsSpan(0, _count);

    /// <summary>Indices of the orbs that moved or changed index in the last
    /// Step. May include indices at or past <see cref="Count"/> (an orb that
    /// moved and was then moved away); skip those.</summary>
    public ReadOnlySpan<int> Changed => _changed.AsSpan(0, _changedCount);

    /// <summary>
    /// Pops one orb worth xp from a point on the ground (where an enemy
    /// died). Safe to call at any time, including from another manager's
    /// physics step: it only adds an orb at the end of the arrays. Returns
    /// the new orb's index, or <see cref="Merged"/> when full and the XP was
    /// added to an existing orb (no XP is ever lost) or xp was 0.
    /// </summary>
    public int Drop(Vector3 groundPosition, int xp)
    {
        if (xp <= 0)
        {
            return Merged;
        }

        if (_count >= Capacity)
        {
            if (_count > 0)
            {
                _values[_mergeCursor % _count] += xp;
                _mergeCursor++;
            }
            return Merged;
        }

        int i = _count;
        _count++;

        // A random direction around the circle, and random strengths
        // between half and full, so each pop looks a little different.
        float angle = _random.NextSingle() * Mathf.Tau;
        float spread = PopSpread * (0.5f + 0.5f * _random.NextSingle());
        float up = PopUp * (0.5f + 0.5f * _random.NextSingle());

        _positions[i] = new Vector3(groundPosition.X, PopHeight, groundPosition.Z);
        _velocities[i] = new Vector3(Mathf.Cos(angle) * spread, up, Mathf.Sin(angle) * spread);
        _states[i] = OrbState.Popping;
        _values[i] = xp;
        return i;
    }

    /// <summary>
    /// Moves every orb one physics step.
    /// </summary>
    /// <param name="delta">Seconds since the last step.</param>
    /// <param name="players">Where each player is (on the ground).</param>
    /// <param name="magnetRadii">Each player's magnet radius, same order.</param>
    /// <param name="xpGained">Filled in: XP each player absorbed this step
    /// (same order). Must be at least as long as players.</param>
    public void Step(float delta, ReadOnlySpan<Vector3> players, ReadOnlySpan<float> magnetRadii,
                     Span<int> xpGained)
    {
        _changedCount = 0;
        xpGained.Clear();
        float absorbSquared = AbsorbRadius * AbsorbRadius;

        // Backwards, because absorbing an orb moves the last orb into its
        // index (swap remove); going from the end, that one was already done.
        for (int i = _count - 1; i >= 0; i--)
        {
            switch (_states[i])
            {
                case OrbState.Popping:
                    // Simple projectile motion: gravity pulls velocity down,
                    // drag slows the sideways part, velocity moves the orb.
                    Vector3 velocity = _velocities[i];
                    velocity.Y -= Gravity * delta;
                    float drag = Mathf.Exp(-AirDrag * delta);
                    velocity.X *= drag;
                    velocity.Z *= drag;
                    _velocities[i] = velocity;
                    _positions[i] += velocity * delta;

                    // Coming down through hover height: settle there.
                    if (velocity.Y < 0.0f && _positions[i].Y <= HoverHeight)
                    {
                        _positions[i].Y = HoverHeight;
                        _states[i] = OrbState.Hovering;
                        _timers[i] = HoverDelay;
                    }
                    MarkChanged(i);
                    break;

                case OrbState.Hovering:
                    // Hovering orbs don't move, so they cost only this
                    // check, and the shell doesn't have to redraw them.
                    _timers[i] -= delta;
                    if (_timers[i] <= 0.0f)
                    {
                        int player = PlayerInMagnet(i, players, magnetRadii);
                        if (player >= 0)
                        {
                            _states[i] = OrbState.Homing;
                            _speeds[i] = HomingStartSpeed;
                            _targets[i] = player;
                        }
                    }
                    break;

                case OrbState.Homing:
                    int target = _targets[i];
                    if (target >= players.Length)
                    {
                        // Its player left: hover again, any player may take it.
                        _states[i] = OrbState.Hovering;
                        _timers[i] = 0.0f;
                        break;
                    }

                    // Accelerate toward the player's chest. Speeding up
                    // (rather than a fixed speed) makes it look like a pull,
                    // and guarantees orbs catch a player who runs away.
                    _speeds[i] += HomingAcceleration * delta;
                    Vector3 absorbPoint = players[target] + Vector3.Up * AbsorbHeight;
                    Vector3 toPlayer = absorbPoint - _positions[i];
                    float distanceSquared = toPlayer.LengthSquared();
                    float step = _speeds[i] * delta;

                    // Absorbed if it's close, or would overshoot this step.
                    if (distanceSquared <= absorbSquared || step * step >= distanceSquared)
                    {
                        xpGained[target] += _values[i];
                        Remove(i);
                        continue; // "continue" skips to the next loop round
                    }

                    _positions[i] += toPlayer / Mathf.Sqrt(distanceSquared) * step;
                    MarkChanged(i);
                    break;
            }
        }
    }

    /// <summary>The nearest player whose magnet radius reaches orb i
    /// (measured on the ground), or -1.</summary>
    private int PlayerInMagnet(int i, ReadOnlySpan<Vector3> players, ReadOnlySpan<float> magnetRadii)
    {
        int best = -1;
        float bestSquared = float.MaxValue;
        for (int p = 0; p < players.Length; p++)
        {
            float dx = _positions[i].X - players[p].X;
            float dz = _positions[i].Z - players[p].Z;
            float distanceSquared = dx * dx + dz * dz;
            if (distanceSquared <= magnetRadii[p] * magnetRadii[p] && distanceSquared < bestSquared)
            {
                best = p;
                bestSquared = distanceSquared;
            }
        }
        return best;
    }

    private void MarkChanged(int i)
    {
        _changed[_changedCount] = i;
        _changedCount++;
    }

    /// <summary>
    /// Removes orb i by moving the last orb into its index ("swap remove"),
    /// so alive orbs stay packed at the start and the shell can draw just
    /// the first Count copies.
    /// </summary>
    private void Remove(int i)
    {
        int last = _count - 1;
        if (i != last)
        {
            _positions[i] = _positions[last];
            _velocities[i] = _velocities[last];
            _states[i] = _states[last];
            _timers[i] = _timers[last];
            _speeds[i] = _speeds[last];
            _targets[i] = _targets[last];
            _values[i] = _values[last];
            // The moved orb is drawn by copy i from now on.
            MarkChanged(i);
        }
        _count--;
    }
}
