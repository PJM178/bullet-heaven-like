using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns every experience orb in the world.
///
/// An orb's life:
///   1. Pop: a kill throws one orb, worth the kill's XP, up and a little
///      to the side (Drop). It arcs under gravity until it falls back to
///      hover height.
///   2. Hover: it floats in place, bobbing (the bob is done on the GPU,
///      see pickups/xp_orb.gdshader).
///   3. Home: after a short pause, an orb within the player's magnet
///      radius (PlayerStats.MagnetRadius) flies at them, speeding up, and
///      is absorbed: the player gains its XP. Orbs further away keep
///      hovering until the player comes close.
///
/// Same data layout as EnemyManager and ProjectileManager: plain arrays,
/// one loop. Unlike those, orbs have no nodes at all. They are all drawn by
/// a single MultiMesh: one mesh, drawn many times in one go, each copy at
/// its own transform. Orbs are identical and can pile up into the
/// thousands, which is exactly what a MultiMesh is for, and it's simpler
/// than a node pool: "showing" orb i just means writing its transform.
///
/// Keep this node at the origin with no rotation or scale; orb positions
/// are used as-is.
/// </summary>
public partial class XpManager : Node3D
{
    // The player orbs fly to, and who receives the XP.
    [Export] public Player Player;

    // The most orbs that can exist at once. When full, new XP is added to
    // an existing orb instead, so no XP is ever lost.
    [Export] public int MaxOrbs = 1500;

    [ExportGroup("Pop")]
    // Starting height of the pop, roughly the middle of an enemy.
    [Export] public float PopHeight = 0.5f;
    // Sideways and upward launch speeds (m/s), each randomised between
    // half and full so orbs don't all land the same way.
    [Export] public float PopSpread = 1.0f;
    [Export] public float PopUp = 3.0f;
    [Export] public float Gravity = 14.0f;
    // How quickly sideways motion dies down in the air (per second).
    [Export] public float AirDrag = 2.0f;

    [ExportGroup("Hover")]
    // Height orbs float at once they've landed.
    [Export] public float HoverHeight = 0.45f;
    // Seconds an orb hovers before it can start flying to the player, so
    // the pop reads before it flies in.
    [Export] public float HoverDelay = 0.35f;

    [ExportGroup("Homing")]
    // How close orbs must be to start flying in is the player's stat,
    // PlayerStats.MagnetRadius, shared with upgrades.
    // Speed an orb starts homing at, and how fast it speeds up (m/s²).
    [Export] public float HomingStartSpeed = 1.5f;
    [Export] public float HomingAcceleration = 30.0f;
    // Height on the player orbs fly to (about chest height), and how
    // close they must get to be absorbed.
    [Export] public float AbsorbHeight = 0.9f;
    [Export] public float AbsorbRadius = 0.35f;

    [ExportGroup("Looks")]
    [Export] public Material OrbMaterial;
    [Export] public float OrbRadius = 0.09f;

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
    private Vector3[] _positions;
    private Vector3[] _velocities;
    private OrbState[] _states;
    private float[] _timers;     // Hovering: seconds left before it may home
    private float[] _speeds;     // Homing: current speed toward the player
    private int[] _values;       // XP this orb is worth
    private int _count;

    private MultiMesh _multiMesh;

    // When the pool is full, XP goes to the orb at this index next, moving
    // round so one orb doesn't swallow everything.
    private int _mergeCursor;

    // One random number generator, reused for every orb.
    private readonly Random _random = new();

    /// <summary>How many orbs exist (read by the HUD).</summary>
    public int Count => _count;

    public override void _Ready()
    {
        _positions = new Vector3[MaxOrbs];
        _velocities = new Vector3[MaxOrbs];
        _states = new OrbState[MaxOrbs];
        _timers = new float[MaxOrbs];
        _speeds = new float[MaxOrbs];
        _values = new int[MaxOrbs];

        _multiMesh = new MultiMesh
        {
            // Each copy gets a full 3D transform (we only use its position).
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            // Reserve room for every orb up front; VisibleInstanceCount
            // says how many of them to actually draw (the first _count).
            InstanceCount = MaxOrbs,
            VisibleInstanceCount = 0,
            Mesh = BuildOrbMesh(),
            // Normally the MultiMesh re-measures the box around all its
            // copies whenever one moves, which costs a pass over every orb.
            // A fixed, huge box skips that. The catch is that orbs are
            // never culled as off-screen, but all of them together are only
            // one draw call anyway.
            CustomAabb = new Aabb(new Vector3(-5000, -50, -5000), new Vector3(10000, 100, 10000)),
        };

        MultiMeshInstance3D drawer = new()
        {
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(drawer);
    }

    /// <summary>
    /// Builds the orb mesh: a low-poly ball. 8 sides and 4 rings keep it
    /// visibly faceted at the edge, while its smooth normals let the
    /// shading and sheen roll across it.
    /// </summary>
    private SphereMesh BuildOrbMesh()
    {
        return new SphereMesh
        {
            Radius = OrbRadius,
            Height = OrbRadius * 2.0f,
            RadialSegments = 8,
            Rings = 4,
            Material = OrbMaterial,
        };
    }

    /// <summary>
    /// Pops one orb worth xp from a point on the ground (where an enemy
    /// died). Safe to call at any time, including from another manager's
    /// physics step: it only adds an orb at the end of the arrays.
    /// </summary>
    public void Drop(Vector3 groundPosition, int xp)
    {
        if (xp <= 0)
        {
            return;
        }

        if (_count >= MaxOrbs)
        {
            // Full: fold the XP into an existing orb instead of losing it.
            _values[_mergeCursor % _count] += xp;
            _mergeCursor++;
            return;
        }

        int i = _count;
        _count++;

        // A random direction around the circle, and random strengths
        // between half and full, so each pop looks a little different.
        float angle = (float)_random.NextDouble() * Mathf.Tau;
        float spread = PopSpread * (0.5f + 0.5f * (float)_random.NextDouble());
        float up = PopUp * (0.5f + 0.5f * (float)_random.NextDouble());

        _positions[i] = new Vector3(groundPosition.X, PopHeight, groundPosition.Z);
        _velocities[i] = new Vector3(Mathf.Cos(angle) * spread, up, Mathf.Sin(angle) * spread);
        _states[i] = OrbState.Popping;
        _values[i] = xp;

        _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, _positions[i]));
        _multiMesh.VisibleInstanceCount = _count;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null)
        {
            return;
        }

        float dt = (float)delta;
        Vector3 playerPosition = Player.GlobalPosition;
        Vector3 absorbPoint = playerPosition + Vector3.Up * AbsorbHeight;
        float magnetSquared = Player.Stats.MagnetRadius * Player.Stats.MagnetRadius;
        float absorbSquared = AbsorbRadius * AbsorbRadius;

        // Backwards, because absorbing an orb moves the last orb into its
        // slot (swap remove); going from the end, that one was already done.
        for (int i = _count - 1; i >= 0; i--)
        {
            switch (_states[i])
            {
                case OrbState.Popping:
                    // Simple projectile motion: gravity pulls velocity down,
                    // drag slows the sideways part, velocity moves the orb.
                    Vector3 velocity = _velocities[i];
                    velocity.Y -= Gravity * dt;
                    float drag = Mathf.Exp(-AirDrag * dt);
                    velocity.X *= drag;
                    velocity.Z *= drag;
                    _velocities[i] = velocity;
                    _positions[i] += velocity * dt;

                    // Coming down through hover height: settle there.
                    if (velocity.Y < 0.0f && _positions[i].Y <= HoverHeight)
                    {
                        _positions[i].Y = HoverHeight;
                        _states[i] = OrbState.Hovering;
                        _timers[i] = HoverDelay;
                    }
                    UpdateTransform(i);
                    break;

                case OrbState.Hovering:
                    // Hovering orbs don't move (the bob is in the shader), so
                    // they cost only this check, and no transform update.
                    _timers[i] -= dt;
                    if (_timers[i] <= 0.0f)
                    {
                        float dx = _positions[i].X - playerPosition.X;
                        float dz = _positions[i].Z - playerPosition.Z;
                        if (dx * dx + dz * dz <= magnetSquared)
                        {
                            _states[i] = OrbState.Homing;
                            _speeds[i] = HomingStartSpeed;
                        }
                    }
                    break;

                case OrbState.Homing:
                    // Accelerate toward the player's chest. Speeding up
                    // (rather than a fixed speed) makes it look like a pull,
                    // and guarantees orbs catch a player who runs away.
                    _speeds[i] += HomingAcceleration * dt;
                    Vector3 toPlayer = absorbPoint - _positions[i];
                    float distanceSquared = toPlayer.LengthSquared();
                    float step = _speeds[i] * dt;

                    // Absorbed if it's close, or would overshoot this step.
                    if (distanceSquared <= absorbSquared || step * step >= distanceSquared)
                    {
                        Player.Stats.AddExperience(_values[i]);
                        Remove(i);
                        continue; // "continue" skips to the next loop round
                    }

                    _positions[i] += toPlayer / Mathf.Sqrt(distanceSquared) * step;
                    UpdateTransform(i);
                    break;
            }
        }
    }

    private void UpdateTransform(int i)
    {
        // Transform3D is a struct, so building one here doesn't allocate.
        _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, _positions[i]));
    }

    /// <summary>
    /// Removes orb i by moving the last orb into its slot ("swap remove"),
    /// so alive orbs stay packed at the start and the MultiMesh can draw
    /// just the first _count copies.
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
            _values[i] = _values[last];
            UpdateTransform(i);
        }
        _count--;
        _multiMesh.VisibleInstanceCount = _count;
    }
}
