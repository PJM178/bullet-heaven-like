using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Describes one *kind* of projectile (a shard, a shuriken, a returning
/// blade...): how it flies, what it hits and what it looks like. It is data
/// only; the <see cref="ProjectileManager"/> moves every projectile, and a
/// <see cref="ProjectileWeapon"/> (or any other code) fires them.
///
/// Many weapons can share one definition. The manager copies the numbers it
/// needs into each projectile when it is spawned.
/// </summary>
// A Resource is Godot's type for reusable data. Each projectile kind is
// saved as its own .tres file (e.g. weapons/projectiles/shard.tres) and
// edited in the Inspector, so new kinds need no new code. A weapon can also
// build one in code (see the research throw weapons).
//
// [GlobalClass] registers this class with the editor by name, so it shows
// up in the "New Resource" list and as a type for [Export] fields.
[GlobalClass]
public partial class ProjectileDefinition : Resource
{
    // PropertyHint.Range turns the field into a slider in the Inspector.
    // The hint string is "min,max,step", plus optional extras such as
    // "or_greater" (allow typing past max) and "suffix:" (unit label).

    [ExportGroup("Flight")]
    /// <summary>Flight speed in metres per second.</summary>
    [Export(PropertyHint.Range, "0.5,50,0.5,or_greater,suffix:m/s")]
    public float Speed = 14.0f;

    /// <summary>How far it flies before it disappears (or, with Returns,
    /// turns back), in metres.</summary>
    [Export(PropertyHint.Range, "0.5,50,0.5,or_greater,suffix:m")]
    public float Range = 10.0f;

    /// <summary>Height above the ground it is drawn at. Hits ignore height.</summary>
    [Export(PropertyHint.Range, "0,3,0.05,suffix:m")]
    public float Height = 0.6f;

    /// <summary>After Range metres, fly back to whoever fired it (a
    /// boomerang), able to hit everything once more on the way.</summary>
    [Export] public bool Returns = false;

    [ExportGroup("Hitting")]
    /// <summary>Damage dealt to an enemy on hit, before any upgrades.</summary>
    [Export(PropertyHint.Range, "0,100,0.5,or_greater")]
    public float Damage = 1.0f;

    /// <summary>
    /// Hit radius in metres, measured on the ground. A hit happens when the
    /// distance to an enemy is less than this plus the enemy's own radius.
    /// A slightly generous radius usually feels fairer than the exact look.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater,suffix:m")]
    public float Radius = 0.15f;

    /// <summary>How many enemies it passes through: 0 = stops at the first
    /// one, -1 = never stops (it still hits each enemy only once).</summary>
    [Export(PropertyHint.Range, "-1,20,1,or_greater")]
    public int Pierce = 0;

    [ExportGroup("Looks")]
    // Set one of the two. A Mesh is drawn as a 3D model pointing along its
    // flight; a Texture as a flat picture facing the camera.
    [Export] public Mesh Mesh;
    [Export] public Texture2D Texture;

    /// <summary>Texture only: width of the picture in the world, in metres.</summary>
    [Export(PropertyHint.Range, "0.05,4,0.05,suffix:m")]
    public float Size = 0.5f;

    /// <summary>Texture only: spin in the screen plane, in turns per second.
    /// 0 = no spin; the picture points along its flight instead.</summary>
    [Export] public float SpinTurnsPerSecond = 0.0f;

    /// <summary>Texture only: which way the tip points in the picture, in
    /// degrees counter-clockwise from "right" (e.g. -135 for bottom-left).</summary>
    [Export] public float TextureAngleDegrees = 0.0f;
}
