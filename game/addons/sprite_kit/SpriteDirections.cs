using System.Collections.Generic;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Runtime helpers for sprites imported by the Sprite Kit importer
/// (<c>.spritesheet</c> files). This part is not editor-only: the game uses it.
/// </summary>
public static class SpriteDirections
{
    /// <summary>The 8 facing directions, in the order the sprite sheets store them.
    /// 0 = S means facing the camera; the order goes clockwise seen from above.</summary>
    public static readonly string[] Directions = { "S", "SW", "W", "NW", "N", "NE", "E", "SE" };

    /// <summary>
    /// Prepares an AnimatedSprite3D for an imported sprite sheet: billboard, crisp pixels, and an
    /// offset that puts the sprite's origin (its feet) on the node's position.
    /// Call once after assigning SpriteFrames.
    /// </summary>
    public static void Setup(AnimatedSprite3D sprite)
    {
        SpriteFrames frames = sprite.SpriteFrames;
        if (frames == null || !frames.HasMeta("sheet_bounds"))
        {
            GD.PushWarning($"{sprite.Name}: SpriteFrames isn't an imported .spritesheet.");
            return;
        }

        var bounds = frames.GetMeta("sheet_bounds").AsRect2();
        sprite.Centered = false;
        // A 3D sprite's y axis points up, so the bottom-left corner of the texture
        // sits at the offset. The origin is bounds.Y + bounds.H pixels above the
        // bottom edge (bounds.Y is negative: the frame reaches up from the feet).
        sprite.Offset = new Vector2(bounds.Position.X, -(bounds.Position.Y + bounds.Size.Y));
        sprite.Billboard = BaseMaterial3D.BillboardModeEnum.FixedY;
        sprite.TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest;
        sprite.AlphaCut = SpriteBase3D.AlphaCutMode.Discard; // hard edges, sorts well with many sprites
    }

    /// <summary>
    /// Which of the 8 directions a character facing <paramref name="facing"/>
    /// (a world-space direction, y ignored) shows, as seen from <paramref name="camera"/>.
    /// </summary>
    public static int DirectionIndex(Vector3 facing, Camera3D camera)
    {
        Basis cam = camera.GlobalTransform.Basis;
        // Camera's right and "toward the viewer" axes, flattened onto the ground.
        Vector3 right = new Vector3(cam.X.X, 0, cam.X.Z).Normalized();
        Vector3 toViewer = new Vector3(cam.Z.X, 0, cam.Z.Z).Normalized();

        float x = facing.Dot(right);    // + = facing screen-right
        float z = facing.Dot(toViewer); // + = facing the camera
        // 0 deg = facing the camera (S), 90 deg = screen-left (W), 180 = away (N).
        float angle = Mathf.Atan2(-x, z);
        int index = Mathf.RoundToInt(angle / (Mathf.Pi / 4));
        return (index % 8 + 8) % 8; // wrap negatives into 0..7
    }

    // "walk" -> { "walk_S", "walk_SW", ... } built once, so picking an animation
    // every frame doesn't allocate new strings (matters with hundreds of enemies).
    private static readonly Dictionary<string, StringName[]> NameCache = new();

    /// <summary>Animation name for an anim + direction, e.g. ("walk", 1) -> "walk_SW".</summary>
    public static StringName AnimationName(string anim, int direction)
    {
        if (!NameCache.TryGetValue(anim, out StringName[] names))
        {
            names = new StringName[8];
            for (int i = 0; i < 8; i++)
                names[i] = $"{anim}_{Directions[i]}";
            NameCache[anim] = names;
        }
        return names[direction];
    }
}
