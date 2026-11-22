using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A round minimap in the corner of the screen showing the world markers
/// (see <see cref="WorldMarkers"/>) around the player.
///
/// The map is turned to match the screen: up on the map is up on the
/// screen, so a marker up-left on the map is up-left in the world as you
/// see it. The player is the centre, with a small wedge for where the
/// character faces.
///
/// What a marker shows depends on how much the player knows (MarkerState):
///   Rumoured   - a "?" on the rim, in its direction (never its real spot);
///   Sensed     - its icon, dimmed;
///   Discovered - its icon, full, with an outline.
/// Sensed or discovered markers further away than RangeMetres sit on the
/// rim, smaller and dimmer, pointing their way.
///
/// Everything is drawn in code in _Draw, redrawn every frame, without
/// allocating.
/// </summary>
public partial class Minimap : Control
{
    // How many metres from the player to the rim.
    [Export] public float RangeMetres = 40.0f;

    // Size of a marker icon in pixels.
    [Export] public float IconSize = 48.0f;

    // Corners of the disc. Few corners give a faceted, low-poly look.
    [Export(PropertyHint.Range, "6,64,1")] public int Sides = 16;

    // A faint ring at this fraction of the range, to help judge distance.
    // 0 = no ring.
    [Export(PropertyHint.Range, "0,1,0.05")] public float InnerRing = 0.5f;

    [Export] public int FontSize = 40;

    [ExportGroup("Colours")]
    [Export] public Color BackgroundColor = new(0.05f, 0.05f, 0.06f, 0.6f);
    [Export] public Color RimColor = new(0.8f, 0.78f, 0.7f, 0.7f);
    [Export] public Color RingColor = new(0.8f, 0.78f, 0.7f, 0.2f);
    [Export] public Color PlayerColor = new(1.0f, 1.0f, 1.0f, 0.95f);
    [Export] public Color RumourColor = new(0.85f, 0.85f, 0.85f, 0.75f);

    private WorldMarkers _markers;
    private Font _font;

    // Point buffers, made once in _Ready and reused every frame.
    private Vector2[] _discFill;            // the disc's corners
    private Vector2[] _rimLine;             // the same, first one repeated to close the outline
    private readonly Vector2[] _wedge = new Vector2[3];

    // The screen's "up" and "right" laid flat on the ground, from the
    // camera every frame (it circles during the death shot).
    private Vector3 _screenUp;
    private Vector3 _screenRight;

    public override void _Ready()
    {
        // The map only shows things; touches must reach the joysticks.
        MouseFilter = MouseFilterEnum.Ignore;
        _font = ThemeDB.FallbackFont;

        // DrawColoredPolygon and DrawPolyline take the whole array, so make
        // arrays of exactly the right length once.
        _discFill = new Vector2[Sides];
        _rimLine = new Vector2[Sides + 1];
    }

    public override void _Process(double delta)
    {
        // The markers live in the world (inside the SubViewport), the HUD
        // outside it. Both are in the same SceneTree, so a group finds it.
        // Looked up lazily: the world may be ready after the HUD.
        _markers ??= GetTree().GetFirstNodeInGroup(WorldMarkers.Group) as WorldMarkers;
        if (_markers != null)
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (_markers?.Player == null || _markers.Camera == null)
            return;

        Basis cameraBasis = _markers.Camera.GlobalBasis;
        // The camera looks along its -Z; laid flat that is "up the screen".
        _screenUp = Flatten(-cameraBasis.Z);
        _screenRight = Flatten(cameraBasis.X);

        Vector2 centre = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f;
        // Markers stay half an icon inside the rim so they aren't cut off.
        float usable = radius - IconSize * 0.6f;
        float pixelsPerMetre = usable / RangeMetres;

        DrawDisc(centre, radius);

        Vector3 playerPosition = _markers.Player.GlobalPosition;

        for (int i = 0; i < _markers.Capacity; i++)
        {
            if (!_markers.IsActive(i))
                continue;
            MarkerState state = _markers.GetState(i);
            if (state == MarkerState.Hidden)
                continue;

            Vector2 offset = ToMap(_markers.GetPosition(i) - playerPosition) * pixelsPerMetre;
            // Beyond the rim, or only rumoured: pin it to the rim.
            bool pinned = state == MarkerState.Rumoured || offset.Length() > usable;
            if (pinned)
                offset = offset.Normalized() * usable;
            Vector2 position = centre + offset;

            if (state == MarkerState.Rumoured)
            {
                DrawCentredText("?", position, RumourColor);
                continue;
            }

            WorldMarkerDefinition definition = _markers.GetDefinition(i);
            Color color = definition.Color;
            float size = IconSize;
            if (state == MarkerState.Sensed)
                color.A *= 0.45f;
            if (pinned)
            {
                color.A *= 0.75f;
                size *= 0.8f;
            }
            MarkerIcon.Draw(this, definition, position, size, color, state == MarkerState.Discovered);
        }

        DrawPlayer(centre);
    }

    // ---- Pieces of the drawing -------------------------------------------

    private void DrawDisc(Vector2 centre, float radius)
    {
        for (int i = 0; i < Sides; i++)
        {
            // Start at the top (-90°, since 2D y points down), so a corner
            // points straight up.
            float angle = Mathf.Tau * i / Sides - Mathf.Pi * 0.5f;
            var corner = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            _discFill[i] = centre + corner * radius;
            _rimLine[i] = _discFill[i];
        }
        _rimLine[Sides] = _rimLine[0];

        DrawColoredPolygon(_discFill, BackgroundColor);
        if (InnerRing > 0.0f)
            DrawArc(centre, radius * InnerRing, 0.0f, Mathf.Tau, Sides + 1, RingColor, 2.0f);
        DrawPolyline(_rimLine, RimColor, 3.0f);
    }

    private void DrawPlayer(Vector2 centre)
    {
        // A wedge pointing where the character faces: the tip ahead of the
        // centre, two back corners behind it.
        Vector2 forward = ToMap(_markers.Player.Forward).Normalized();
        // Orthogonal() turns a 2D vector 90°, giving the wedge's sideways.
        Vector2 side = forward.Orthogonal();
        float length = IconSize * 0.55f;
        _wedge[0] = centre + forward * length;
        _wedge[1] = centre - forward * length * 0.5f + side * length * 0.55f;
        _wedge[2] = centre - forward * length * 0.5f - side * length * 0.55f;
        DrawColoredPolygon(_wedge, PlayerColor);
    }

    private void DrawCentredText(string text, Vector2 centre, Color color)
    {
        // DrawString places text by its left end and baseline; measure it
        // to centre it on the point.
        Vector2 size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);
        var position = new Vector2(centre.X - size.X * 0.5f, centre.Y + FontSize * 0.35f);
        // A dark drop shadow keeps it readable.
        DrawString(_font, position + Vector2.One * 2.0f, text, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, color.A * 0.8f));
        DrawString(_font, position, text, HorizontalAlignment.Left, -1, FontSize, color);
    }

    // ---- Maths -------------------------------------------------------------

    /// <summary>
    /// A world offset or direction as it appears on the map, in metres:
    /// x to the right of the screen, y down the screen (2D y points down).
    /// </summary>
    private Vector2 ToMap(Vector3 world)
    {
        return new Vector2(world.Dot(_screenRight), -world.Dot(_screenUp));
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.Y = 0.0f;
        return v.Normalized();
    }
}
