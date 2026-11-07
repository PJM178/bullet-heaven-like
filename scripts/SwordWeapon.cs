using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A sword that swings automatically on a cooldown, hitting every enemy in
/// a narrow wedge in front of the player (where Player.Forward points).
///
/// The hit happens all at once, on the frame the swing starts. The slash
/// you see sweeping across the wedge is only a visual that plays out over
/// SwingDuration. (Hitting enemies one by one as the blade passed would
/// mean remembering who was already hit across frames, and enemy indices
/// change between frames; see EnemyManager.Despawn.)
///
/// Put this node under the Player so the slash moves along with them.
/// Keep its own position at zero.
/// </summary>
public partial class SwordWeapon : Node3D
{
    [Export] public Player Player;
    [Export] public EnemyManager EnemyManager;

    // Seconds between swings.
    [Export] public float Cooldown = 1.0f;

    // Damage to every enemy in the swing. Slimes have 2 health, so 2 kills
    // one in a single swing.
    [Export] public float Damage = 2.0f;

    // How far the blade reaches from the player's centre, in metres.
    [Export] public float Range = 2.2f;

    // Total width of the wedge, in degrees, centred on where the player
    // faces: 30 means 15 degrees to either side.
    [Export(PropertyHint.Range, "5,360,1")] public float ArcDegrees = 30.0f;

    [ExportGroup("Slash visual")]
    // A ShaderMaterial using shaders/sword_slash.gdshader.
    [Export] public Material SlashMaterial;
    // Seconds the slash takes to sweep across and fade.
    [Export] public float SwingDuration = 0.18f;
    // Height of the slash above the ground (about the hands).
    [Export] public float SlashHeight = 0.8f;
    // Where the drawn strip starts, from the player's centre. The blade's
    // path near the hilt isn't interesting to see.
    [Export] public float SlashInnerRadius = 0.6f;

    // The most enemies one swing can hit. The array is made once and
    // reused, so finding enemies never allocates.
    private const int MaxTargets = 64;
    private readonly int[] _targets = new int[MaxTargets];

    // Run after EnemyManager has moved enemies and rebuilt its grid this
    // step, like ProjectileManager (see the note there).
    private const int PhysicsPriorityAfterEnemies = 10;

    private static readonly StringName ProgressParameter = "progress";

    private float _cooldownLeft;
    private MeshInstance3D _slash;
    private ShaderMaterial _slashMaterial;
    private float _swingTime;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;

        // Our own copy of the material: we change "progress" on it every
        // frame, and a shared material would change for everyone using it.
        // "as" converts to ShaderMaterial, or gives null if it isn't one.
        _slashMaterial = SlashMaterial?.Duplicate() as ShaderMaterial;

        _slash = new MeshInstance3D
        {
            Mesh = BuildSlashMesh(),
            MaterialOverride = _slashMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(0.0f, SlashHeight, 0.0f),
            Visible = false,
        };
        AddChild(_slash);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || EnemyManager == null)
        {
            return;
        }

        _cooldownLeft -= (float)delta;
        if (_cooldownLeft > 0.0f)
        {
            return;
        }
        // "+=" keeps the rhythm exact, as in ProjectileTestShooter.
        _cooldownLeft += Cooldown;

        Swing();
    }

    private void Swing()
    {
        Vector3 origin = Player.GlobalPosition;
        Vector3 forward = Player.Forward;
        float halfArc = Mathf.DegToRad(ArcDegrees * 0.5f);

        // First the cheap, broad search: everyone within reach in any
        // direction. Then keep only those inside the wedge.
        int count = EnemyManager.QueryCircle(origin, Range, _targets);
        for (int n = 0; n < count; n++)
        {
            int enemy = _targets[n];
            Vector3 toEnemy = EnemyManager.GetPosition(enemy) - origin;
            toEnemy.Y = 0.0f;
            float distance = toEnemy.Length();

            if (distance > 0.001f)
            {
                // The angle between where we face and where the enemy is.
                // AngleTo gives 0 straight ahead, up to 180° (Pi) behind.
                float angle = forward.AngleTo(toEnemy);

                // Enemies have a body, not just a centre point: one whose
                // centre is just outside the wedge can still have its edge
                // inside. Its body covers about Asin(radius / distance) of
                // angle either side of its centre, so widen by that much.
                // (Math.Min keeps the value valid for Asin when an enemy is
                // closer than its own radius.)
                float bodyAngle = Mathf.Asin(Math.Min(1.0f, EnemyManager.EnemyRadius / distance));
                if (angle > halfArc + bodyAngle)
                {
                    continue; // outside the wedge
                }
            }

            EnemyManager.Damage(enemy, Damage);
        }

        StartSlashVisual(forward);
    }

    private void StartSlashVisual(Vector3 forward)
    {
        if (_slashMaterial == null)
        {
            return;
        }

        // Point the slash where the player faces. The mesh is built
        // around +Z, and Atan2(x, z) is the turn that brings +Z round to
        // "forward" (the same maths Player uses to turn its model).
        Vector3 rotation = _slash.Rotation;
        rotation.Y = Mathf.Atan2(forward.X, forward.Z);
        _slash.Rotation = rotation;

        _swingTime = 0.0f;
        _slashMaterial.SetShaderParameter(ProgressParameter, 0.0f);
        _slash.Visible = true;
    }

    // Visual only, so it runs per rendered frame for the smoothest sweep.
    public override void _Process(double delta)
    {
        if (!_slash.Visible)
        {
            return;
        }

        _swingTime += (float)delta;
        float progress = _swingTime / SwingDuration;
        if (progress >= 1.0f)
        {
            _slash.Visible = false;
            return;
        }
        _slashMaterial.SetShaderParameter(ProgressParameter, progress);
    }

    /// <summary>
    /// Builds the slash strip: a flat, curved band covering the wedge,
    /// lying on the ground plane (the node lifts it to SlashHeight).
    ///
    ///        outer edge (Range)
    ///      ___________________
    ///      \       |       /      the band, seen from above, with
    ///       \______|______/       the player at the point below
    ///        inner edge
    ///             (+Z = forward)
    ///
    /// It runs from the character's right to their left, so a swing
    /// sweeps the way a right-handed slash would.
    /// </summary>
    private ArrayMesh BuildSlashMesh()
    {
        // Few segments: the faceted curve suits the low-poly look.
        const int segments = 8;
        float halfArc = Mathf.DegToRad(ArcDegrees * 0.5f);

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] indices = new int[segments * 6];

        for (int k = 0; k <= segments; k++)
        {
            float t = (float)k / segments;   // 0..1 along the arc
            // The character's front is +Z and their right is -X (see
            // Player.Right), so start at -halfArc (the right) and turn to
            // +halfArc (the left).
            float angle = Mathf.Lerp(-halfArc, halfArc, t);
            Vector3 direction = new(Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));

            vertices[k * 2] = direction * SlashInnerRadius;
            vertices[k * 2 + 1] = direction * Range;
            uvs[k * 2] = new Vector2(t, 0.0f);
            uvs[k * 2 + 1] = new Vector2(t, 1.0f);
        }

        // Two triangles per segment, joining this pair of points (inner
        // and outer) to the next pair.
        for (int k = 0; k < segments; k++)
        {
            int inner = k * 2;
            int outer = k * 2 + 1;
            int nextInner = inner + 2;
            int nextOuter = outer + 2;
            int at = k * 6;
            indices[at] = inner;
            indices[at + 1] = outer;
            indices[at + 2] = nextOuter;
            indices[at + 3] = inner;
            indices[at + 4] = nextOuter;
            indices[at + 5] = nextInner;
        }

        // A mesh's data goes in as a Godot Array with one slot per kind of
        // data (positions, UVs, ...), at the slots Mesh.ArrayType names.
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
