using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The experience orbs in the scene: the "shell" around
/// <see cref="XpSimulation"/>, which pops, hovers and homes the orbs. This
/// node owns one, steps it every physics tick, draws it, and gives the XP
/// that orbs bring to the player. Same split as EnemyManager /
/// EnemySimulation.
///
/// Orbs have no nodes at all. They are all drawn by a single MultiMesh: one
/// mesh, drawn many times in one go, each copy at its own transform. Orbs
/// are identical and can pile up into the thousands, which is exactly what
/// a MultiMesh is for: "showing" orb i just means writing copy i's
/// transform. The bob is done on the GPU (pickups/xp_orb.gdshader).
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

    // The game logic. Null until _Ready.
    private XpSimulation _sim;

    private MultiMesh _multiMesh;

    // What is handed to the core every step, made once and reused. One
    // entry per player; there is only one player for now.
    private readonly Vector3[] _playerPositions = new Vector3[1];
    private readonly float[] _magnetRadii = new float[1];
    private readonly int[] _xpGained = new int[1];

    /// <summary>How many orbs exist (read by the HUD).</summary>
    public int Count => _sim?.Count ?? 0;

    public override void _Ready()
    {
        _sim = new XpSimulation(MaxOrbs);
        // Now too, not only each step: Drop can come before the first step.
        PushSettings();

        _multiMesh = new MultiMesh
        {
            // Each copy gets a full 3D transform (we only use its position).
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            // Reserve room for every orb up front; VisibleInstanceCount
            // says how many of them to actually draw (the first Count).
            InstanceCount = _sim.Capacity,
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
    /// physics step.
    /// </summary>
    public void Drop(Vector3 groundPosition, int xp)
    {
        if (_sim == null)
        {
            return;
        }

        int i = _sim.Drop(groundPosition, xp);
        if (i == XpSimulation.Merged)
        {
            return; // no new orb to draw
        }

        // Draw it right away, already where it starts.
        DrawOrb(i);
        _multiMesh.VisibleInstanceCount = _sim.Count;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null)
        {
            return;
        }

        PushSettings();

        // Godot -> core: where the player is and how far their magnet reaches.
        _playerPositions[0] = Player.GlobalPosition;
        _magnetRadii[0] = Player.Stats.MagnetRadius;

        _sim.Step((float)delta, _playerPositions, _magnetRadii, _xpGained);

        // Core -> Godot: redraw only the orbs that moved or changed index.
        ReadOnlySpan<int> changed = _sim.Changed;
        int count = _sim.Count;
        for (int n = 0; n < changed.Length; n++)
        {
            // An index at or past Count is no longer drawn; skip it.
            if (changed[n] < count)
            {
                DrawOrb(changed[n]);
            }
        }
        _multiMesh.VisibleInstanceCount = count;

        // All the XP absorbed this step in one call. AddExperience handles
        // several level-ups at once, so this is the same as one per orb.
        if (_xpGained[0] > 0)
        {
            Player.Stats.AddExperience(_xpGained[0]);
        }
    }

    /// <summary>Copies the [Export] settings into the core (see EnemyManager).</summary>
    private void PushSettings()
    {
        _sim.PopHeight = PopHeight;
        _sim.PopSpread = PopSpread;
        _sim.PopUp = PopUp;
        _sim.Gravity = Gravity;
        _sim.AirDrag = AirDrag;
        _sim.HoverHeight = HoverHeight;
        _sim.HoverDelay = HoverDelay;
        _sim.HomingStartSpeed = HomingStartSpeed;
        _sim.HomingAcceleration = HomingAcceleration;
        _sim.AbsorbHeight = AbsorbHeight;
        _sim.AbsorbRadius = AbsorbRadius;
    }

    private void DrawOrb(int i)
    {
        // Transform3D is a struct, so building one here doesn't allocate.
        _multiMesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, _sim.Positions[i]));
    }
}
