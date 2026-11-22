using Godot;

namespace BulletHeavenLike;

/// <summary>
/// INERT FOR NOW: the minimap (Minimap.cs) replaced it on the HUD; the node
/// in player_hud.tscn is hidden and disabled. Kept to compare on the phone.
///
/// A compass strip along the top of the screen that shows the world
/// markers (see <see cref="WorldMarkers"/>) around the player.
///
/// The strip is fixed to the screen, not to the character: its centre is
/// "up the screen", its left half is left on screen, and its edges are
/// behind. The camera never turns, so markers only slide along the bar as
/// the player walks, never as they aim. Where the character faces is shown
/// by a small cursor sliding along the bar instead; pointing it at a marker
/// highlights the marker and shows its name.
///
/// Everything is drawn in code in _Draw. Unlike the hearts it changes every
/// frame, so _Process asks for a redraw each frame. Drawing allocates
/// nothing: the distance labels are only rebuilt when the whole number of
/// metres changes.
/// </summary>
public partial class CompassBar : Control
{
    // How far round the bar reaches on each side of its centre, in
    // degrees. 180 = the whole circle (both edges are straight behind).
    // Smaller values spread the front out and pin anything outside the
    // range to the nearest edge, dimmed.
    [Export(PropertyHint.Range, "45,180,5")] public float HalfSpanDegrees = 180.0f;

    // Height of the strip in pixels. Text (distances, names) goes below it.
    [Export] public float BarHeight = 96.0f;

    // Size of a marker icon in pixels.
    [Export] public float IconSize = 52.0f;

    // The facing cursor counts as pointing at a marker within this many
    // degrees of it.
    [Export] public float AlignDegrees = 8.0f;

    // Letters for the world directions (north is the world's -Z axis).
    [Export] public bool ShowCardinals = true;

    [Export] public int FontSize = 44;

    [ExportGroup("Colours")]
    [Export] public Color BackgroundColor = new(0.05f, 0.05f, 0.06f, 0.65f);
    [Export] public Color TickColor = new(0.8f, 0.78f, 0.7f, 0.5f);
    [Export] public Color CardinalColor = new(0.9f, 0.88f, 0.8f, 0.9f);
    [Export] public Color CursorColor = new(1.0f, 1.0f, 1.0f, 0.95f);
    [Export] public Color RumourColor = new(0.85f, 0.85f, 0.85f, 0.6f);
    [Export] public Color TextColor = new(0.95f, 0.93f, 0.88f, 1.0f);

    // Plain C# arrays of strings: made once, so drawing them allocates
    // nothing.
    private static readonly string[] CardinalLetters = { "N", "E", "S", "W" };

    private WorldMarkers _markers;
    private Font _font;

    // "23 m" per marker slot, rebuilt only when the number changes.
    private int[] _shownMetres;
    private string[] _distanceText;

    // Corner buffer for the facing cursor, reused.
    private readonly Vector2[] _cursor = new Vector2[3];

    // The screen's "up" and "right" laid flat on the ground, worked out
    // from the camera every frame (it circles during the death shot).
    private Vector3 _screenUp;
    private Vector3 _screenRight;

    public override void _Ready()
    {
        // The bar only shows things; touches must reach the joysticks.
        MouseFilter = MouseFilterEnum.Ignore;
        // ThemeDB.FallbackFont is the font Godot uses when no theme sets
        // one, so we don't need a font file for the prototype.
        _font = ThemeDB.FallbackFont;
    }

    public override void _Process(double delta)
    {
        // The markers live in the world (inside the SubViewport), the HUD
        // outside it. Both are in the same SceneTree, so a group finds it.
        // Looked up lazily: the world may be ready after the HUD.
        if (_markers == null)
        {
            _markers = GetTree().GetFirstNodeInGroup(WorldMarkers.Group) as WorldMarkers;
            if (_markers == null)
                return;
            _shownMetres = new int[_markers.Capacity];
            _distanceText = new string[_markers.Capacity];
            for (int i = 0; i < _shownMetres.Length; i++)
                _shownMetres[i] = -1;
        }
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

        float width = Size.X;
        float centreY = BarHeight * 0.5f;

        // Background strip.
        DrawRect(new Rect2(0, 0, width, BarHeight), BackgroundColor);

        DrawTicks(width);

        Vector3 playerPosition = _markers.Player.GlobalPosition;
        float facingAngle = BarAngle(_markers.Player.Forward);

        // Remember the marker the cursor points at, to name it after the loop.
        int alignedId = -1;
        float alignedX = 0.0f;
        float bestAlignment = AlignDegrees;

        for (int i = 0; i < _markers.Capacity; i++)
        {
            if (!_markers.IsActive(i))
                continue;
            MarkerState state = _markers.GetState(i);
            if (state == MarkerState.Hidden)
                continue;

            WorldMarkerDefinition definition = _markers.GetDefinition(i);
            Vector3 offset = _markers.GetPosition(i) - playerPosition;
            offset.Y = 0.0f;
            float distance = offset.Length();
            float angle = BarAngle(offset);

            // Outside the bar's span: pin to the nearer edge.
            bool pinned = Mathf.Abs(angle) > HalfSpanDegrees;
            float x = AngleToX(Mathf.Clamp(angle, -HalfSpanDegrees, HalfSpanDegrees), width);

            // Does the facing cursor point at it? Rumours can't be pointed
            // at (you don't know what they are yet).
            float alignment = Mathf.Abs(Mathf.Wrap(angle - facingAngle, -180.0f, 180.0f));
            bool aligned = state != MarkerState.Rumoured && alignment < bestAlignment;
            if (aligned)
            {
                bestAlignment = alignment;
                alignedId = i;
                alignedX = x;
            }

            DrawMarker(definition, state, new Vector2(x, centreY), pinned, alignment < AlignDegrees);

            if (state == MarkerState.Discovered && definition.ShowDistance)
                DrawCentredText(DistanceText(i, distance), x, BarHeight + FontSize, TextColor);
        }

        if (alignedId >= 0)
        {
            WorldMarkerDefinition definition = _markers.GetDefinition(alignedId);
            if (definition.DisplayName != "")
                DrawCentredText(definition.DisplayName, alignedX, BarHeight + FontSize * 2.1f, definition.Color);
        }

        DrawFacingCursor(AngleToX(Mathf.Clamp(facingAngle, -HalfSpanDegrees, HalfSpanDegrees), width));
    }

    // ---- Pieces of the drawing -------------------------------------------

    private void DrawTicks(float width)
    {
        // A tick every 15° of world heading, longer every 45°, letters on
        // the four main ones. Heading 0 = north = world -Z, 90 = east = +X.
        for (int step = 0; step < 24; step++)
        {
            float heading = step * 15.0f;
            float radians = Mathf.DegToRad(heading);
            var direction = new Vector3(Mathf.Sin(radians), 0.0f, -Mathf.Cos(radians));
            float angle = BarAngle(direction);
            if (Mathf.Abs(angle) > HalfSpanDegrees)
                continue;
            float x = AngleToX(angle, width);

            bool cardinal = step % 6 == 0;
            if (cardinal && ShowCardinals)
            {
                // Letter at the bottom of the strip, behind the markers.
                DrawCentredText(CardinalLetters[step / 6], x, BarHeight - 12.0f, CardinalColor);
                continue;
            }
            float length = step % 3 == 0 ? BarHeight * 0.35f : BarHeight * 0.18f;
            DrawLine(new Vector2(x, BarHeight - length), new Vector2(x, BarHeight), TickColor, 3.0f);
        }
    }

    private void DrawMarker(WorldMarkerDefinition definition, MarkerState state, Vector2 centre, bool pinned, bool highlighted)
    {
        if (state == MarkerState.Rumoured)
        {
            Color rumour = RumourColor;
            if (pinned)
                rumour.A *= 0.5f;
            DrawCentredText("?", centre.X, centre.Y + FontSize * 0.35f, rumour);
            return;
        }

        Color color = definition.Color;
        float size = IconSize;
        if (state == MarkerState.Sensed)
            color.A *= 0.45f;
        if (pinned)
        {
            color.A *= 0.5f;
            size *= 0.7f;
        }
        if (highlighted)
            size *= 1.35f;

        // Discovered markers get a dark outline so they read on any ground.
        MarkerIcon.Draw(this, definition, centre, size, color, state == MarkerState.Discovered);
    }

    private void DrawFacingCursor(float x)
    {
        // A small downward triangle hanging from the top edge, plus a thin
        // line through the strip.
        _cursor[0] = new Vector2(x - 14.0f, 0.0f);
        _cursor[1] = new Vector2(x + 14.0f, 0.0f);
        _cursor[2] = new Vector2(x, 20.0f);
        DrawColoredPolygon(_cursor, CursorColor);
        // "with" copies a struct and changes one field: the cursor colour,
        // fainter.
        DrawLine(new Vector2(x, 20.0f), new Vector2(x, BarHeight), CursorColor with { A = 0.35f }, 2.0f);
    }

    private void DrawCentredText(string text, float x, float baselineY, Color color)
    {
        // DrawString places text by its left end; measure it to centre it.
        Vector2 size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, FontSize);
        var position = new Vector2(x - size.X * 0.5f, baselineY);
        // A dark drop shadow keeps it readable over the game.
        DrawString(_font, position + Vector2.One * 2.0f, text, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, color.A * 0.8f));
        DrawString(_font, position, text, HorizontalAlignment.Left, -1, FontSize, color);
    }

    // ---- Maths -------------------------------------------------------------

    /// <summary>
    /// Where a flat world direction sits on the bar, in degrees: 0 = up the
    /// screen (the bar's centre), positive = to the right, ±180 = straight
    /// down the screen (behind, at the bar's edges).
    /// </summary>
    private float BarAngle(Vector3 direction)
    {
        // Atan2(sideways, ahead) is the angle from "ahead" towards "right".
        float right = direction.Dot(_screenRight);
        float up = direction.Dot(_screenUp);
        return Mathf.RadToDeg(Mathf.Atan2(right, up));
    }

    private float AngleToX(float angle, float width)
    {
        // Keep half an icon free at each end so pinned markers fit.
        float usable = width * 0.5f - IconSize * 0.6f;
        return width * 0.5f + angle / HalfSpanDegrees * usable;
    }

    private string DistanceText(int id, float distance)
    {
        int metres = Mathf.RoundToInt(distance);
        if (_shownMetres[id] != metres)
        {
            // Only here, when the number changes, is a new string built.
            _shownMetres[id] = metres;
            _distanceText[id] = $"{metres} m";
        }
        return _distanceText[id];
    }

    private static Vector3 Flatten(Vector3 v)
    {
        v.Y = 0.0f;
        return v.Normalized();
    }
}
