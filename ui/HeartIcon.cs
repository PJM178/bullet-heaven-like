using Godot;

namespace BulletHeavenLike;

/// <summary>
/// One heart in the health display, drawn in code as a low-poly shape (no
/// texture needed). Full hearts are red; lost hearts stay as dark, empty
/// outlines so you can see how many you had.
///
/// Drawing happens in _Draw, which Godot calls once and then caches. It only
/// runs again after QueueRedraw(), so a heart costs nothing per frame.
/// </summary>
[Tool] // Runs in the editor too, so the heart is visible in the 2D view.
public partial class HeartIcon : Control
{
    // How many corners the heart outline has. Few corners give the
    // faceted, low-poly look.
    private const int Corners = 14;

    [Export] public Color FullColor = new(0.72f, 0.1f, 0.1f);
    [Export] public Color EmptyColor = new(0.1f, 0.07f, 0.07f, 0.8f);
    [Export] public Color OutlineColor = Colors.Black;
    [Export] public float OutlineWidth = 4.0f;

    private bool _full = true;
    private readonly Vector2[] _points = new Vector2[Corners];
    // DrawPolyline needs the first point repeated at the end to close it.
    private readonly Vector2[] _outline = new Vector2[Corners + 1];

    /// <summary>Whether this heart is still yours (red) or lost (empty).</summary>
    public bool Full
    {
        get => _full;
        set
        {
            // A property with a body: setting it also asks for a redraw,
            // but only if the value actually changed.
            if (_full == value)
            {
                return;
            }
            _full = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // The classic heart curve, sampled at a few points:
        //   x = 16 sin³t,  y = 13 cos t - 5 cos 2t - 2 cos 3t - cos 4t
        // It spans about 32 x 29 units with y pointing up, so we scale it
        // to fit the control and flip y (in Godot's 2D, y points down).
        Vector2 size = Size;
        float scale = Mathf.Min(size.X / 34.0f, size.Y / 31.0f);
        Vector2 center = size / 2.0f;

        for (int i = 0; i < Corners; i++)
        {
            float t = Mathf.Tau * i / Corners;
            float sin = Mathf.Sin(t);
            float x = 16.0f * sin * sin * sin;
            float y = 13.0f * Mathf.Cos(t) - 5.0f * Mathf.Cos(2.0f * t)
                - 2.0f * Mathf.Cos(3.0f * t) - Mathf.Cos(4.0f * t);
            // + 2.5: the curve sits a little high, this centres it.
            _points[i] = center + new Vector2(x, -(y + 2.5f)) * scale;
            _outline[i] = _points[i];
        }
        _outline[Corners] = _points[0];

        DrawColoredPolygon(_points, _full ? FullColor : EmptyColor);
        DrawPolyline(_outline, OutlineColor, OutlineWidth);
    }
}
