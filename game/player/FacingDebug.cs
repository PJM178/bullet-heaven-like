using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Debug overlay: draws the player's own frame of reference on the ground
/// around them, so we can see that Player.Forward, Player.Right and
/// Player.DirectionAt point where we expect.
///
///   yellow (long)  front, 0°
///   red            right, 90°
///   blue           back, 180°
///   green          left, 270°
///   grey ring      with a tick every 45°
///
/// Put it under the Player node. Untick Visible (or delete the node) to
/// hide it. It is a debug tool only and not meant for the finished game.
/// </summary>
public partial class FacingDebug : MeshInstance3D
{
    [Export] public Player Player;

    // Length of the front spoke in metres. The other spokes are shorter,
    // so the front stands out at a glance.
    [Export] public float FrontLength = 1.6f;
    [Export] public float SpokeLength = 1.0f;
    [Export] public float RingRadius = 1.0f;

    // Height above the ground. Slightly raised so the lines don't flicker
    // against the ground plane ("z-fighting").
    [Export] public float Height = 0.03f;

    // How many straight segments make up the ring.
    private const int RingSegments = 32;

    private static readonly Color FrontColor = new(1.0f, 0.85f, 0.2f);
    private static readonly Color RightColor = new(0.9f, 0.25f, 0.2f);
    private static readonly Color BackColor = new(0.25f, 0.45f, 1.0f);
    private static readonly Color LeftColor = new(0.3f, 0.85f, 0.3f);
    private static readonly Color RingColor = new(0.7f, 0.7f, 0.7f);

    // An ImmediateMesh is a mesh we rebuild from scratch in code whenever we
    // like. Good for simple debug drawing. It is slow compared to a normal
    // mesh, which is fine for a few dozen lines.
    private ImmediateMesh _mesh;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        // TopLevel makes this node ignore its parent's transform, so its
        // local coordinates are world coordinates. That lets us draw using
        // the world-space directions the Player gives us, without
        // converting them.
        TopLevel = true;
        GlobalTransform = Transform3D.Identity;

        _mesh = new ImmediateMesh();
        Mesh = _mesh;
        CastShadow = ShadowCastingSetting.Off;

        _material = new StandardMaterial3D
        {
            // Unshaded: ignore lighting, just show the colour as-is.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            // Take each line's colour from the colour we give its vertices.
            VertexColorUseAsAlbedo = true,
            // Draw on top of everything, so the player model can't hide it.
            NoDepthTest = true,
        };
    }

    // Debug drawing is visual only, so _Process (once per rendered frame)
    // is the right place for it.
    public override void _Process(double delta)
    {
        _mesh.ClearSurfaces();
        if (Player == null || !Visible)
        {
            return;
        }

        Vector3 center = Player.GlobalPosition;
        center.Y = Height;

        // Everything between SurfaceBegin and SurfaceEnd becomes one batch
        // of lines. With PrimitiveType.Lines, every two vertices are one
        // line. SurfaceSetColor sets the colour of the vertices that follow.
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, _material);

        // The spokes go through DirectionAt rather than Forward/Right
        // directly, so this checks the angle maths as well.
        AddSpoke(center, 0.0f, FrontLength, FrontColor);
        AddSpoke(center, 90.0f, SpokeLength, RightColor);
        AddSpoke(center, 180.0f, SpokeLength, BackColor);
        AddSpoke(center, 270.0f, SpokeLength, LeftColor);

        AddRing(center);

        _mesh.SurfaceEnd();
    }

    private void AddSpoke(Vector3 center, float degrees, float length, Color color)
    {
        _mesh.SurfaceSetColor(color);
        _mesh.SurfaceAddVertex(center);
        _mesh.SurfaceAddVertex(center + Player.DirectionAt(degrees) * length);
    }

    private void AddRing(Vector3 center)
    {
        _mesh.SurfaceSetColor(RingColor);

        // The ring also rotates with the player (it is built from
        // DirectionAt), so its ticks stay at 0°, 45°, 90°... relative to
        // the character's front.
        float step = 360.0f / RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            Vector3 a = center + Player.DirectionAt(i * step) * RingRadius;
            Vector3 b = center + Player.DirectionAt((i + 1) * step) * RingRadius;
            _mesh.SurfaceAddVertex(a);
            _mesh.SurfaceAddVertex(b);
        }

        // Short ticks pointing outward every 45°.
        for (int degrees = 0; degrees < 360; degrees += 45)
        {
            Vector3 direction = Player.DirectionAt(degrees);
            _mesh.SurfaceAddVertex(center + direction * RingRadius);
            _mesh.SurfaceAddVertex(center + direction * (RingRadius + 0.15f));
        }
    }
}
