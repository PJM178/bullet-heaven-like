using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Draws one world marker's icon (its texture, or its shape in its colour).
/// Shared by everything that shows <see cref="WorldMarkers"/>: the minimap
/// and the compass bar.
///
/// A "static class" has no instances: it's just a home for helper methods,
/// called as MarkerIcon.Draw(...). The drawing goes onto whichever
/// CanvasItem (Control, Node2D) is passed in, from inside its _Draw.
/// </summary>
public static class MarkerIcon
{
    // Corner buffers, reused so drawing allocates nothing. Static is fine:
    // all drawing happens on the main thread, one call at a time.
    private static readonly Vector2[] Diamond = new Vector2[4];
    private static readonly Vector2[] DiamondOutline = new Vector2[5];

    /// <summary>
    /// Draws the icon of <paramref name="definition"/> centred on
    /// <paramref name="centre"/>, <paramref name="size"/> pixels across,
    /// tinted <paramref name="color"/>. <paramref name="outlined"/> adds a
    /// dark outline so it reads on any background.
    /// </summary>
    public static void Draw(CanvasItem canvas, WorldMarkerDefinition definition, Vector2 centre, float size, Color color, bool outlined)
    {
        if (definition.Icon != null)
        {
            var rect = new Rect2(centre - Vector2.One * size * 0.5f, Vector2.One * size);
            canvas.DrawTextureRect(definition.Icon, rect, false, color);
            return;
        }

        float half = size * 0.5f;
        var outline = new Color(0, 0, 0, color.A);
        switch (definition.Shape)
        {
            case MarkerShape.Circle:
                canvas.DrawCircle(centre, half, color);
                if (outlined)
                    canvas.DrawArc(centre, half, 0.0f, Mathf.Tau, 12, outline, 2.0f);
                break;

            case MarkerShape.Square:
                var square = new Rect2(centre - Vector2.One * half * 0.8f, Vector2.One * half * 1.6f);
                canvas.DrawRect(square, color);
                if (outlined)
                    canvas.DrawRect(square, outline, false, 2.0f);
                break;

            default: // Diamond
                Diamond[0] = centre + new Vector2(0, -half);
                Diamond[1] = centre + new Vector2(half * 0.75f, 0);
                Diamond[2] = centre + new Vector2(0, half);
                Diamond[3] = centre + new Vector2(-half * 0.75f, 0);
                canvas.DrawColoredPolygon(Diamond, color);
                if (outlined)
                {
                    for (int i = 0; i < 4; i++)
                        DiamondOutline[i] = Diamond[i];
                    DiamondOutline[4] = Diamond[0];
                    canvas.DrawPolyline(DiamondOutline, outline, 2.0f);
                }
                break;
        }
    }
}
