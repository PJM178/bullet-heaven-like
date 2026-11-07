using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Describes one *kind* of projectile (a shard, a fireball, a pellet...):
/// how fast it flies, how long it lasts, how hard it hits and what it
/// looks like. It is data only; it does not move anything itself.
///
/// Weapons point at a definition, and the ProjectileManager copies these
/// values into each projectile when it is spawned. Many weapons can share
/// one definition, and a weapon can adjust the copied values as it fires
/// (for example a damage upgrade) without changing the definition.
/// </summary>
// A Resource is Godot's type for reusable data. Each projectile kind is
// saved as its own .tres file (e.g. projectiles/shard.tres) and edited in
// the Inspector, so new kinds need no new code.
//
// [GlobalClass] registers this class with the editor by name, so it shows
// up in the "New Resource" list and as a type for [Export] fields.
[GlobalClass]
public partial class ProjectileDefinition : Resource
{
    // PropertyHint.Range turns the field into a slider in the Inspector.
    // The hint string is "min,max,step", plus optional extras such as
    // "or_greater" (allow typing past max) and "suffix:" (unit label).

    /// <summary>Flight speed in metres per second.</summary>
    [Export(PropertyHint.Range, "0.5,50,0.5,or_greater,suffix:m/s")]
    public float Speed = 14.0f;

    /// <summary>
    /// Seconds before the projectile disappears if it hasn't hit anything.
    /// Together with Speed this sets the range: Speed * Lifetime metres.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,10,0.05,or_greater,suffix:s")]
    public float Lifetime = 0.8f;

    /// <summary>Damage dealt to an enemy on hit, before any upgrades.</summary>
    [Export(PropertyHint.Range, "0,100,0.5,or_greater")]
    public float Damage = 1.0f;

    /// <summary>
    /// Hit radius in metres, measured on the ground. A hit happens when the
    /// distance to an enemy is less than this plus the enemy's own radius.
    /// It doesn't have to match the mesh exactly; a slightly generous
    /// radius usually feels fairer.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,2,0.01,or_greater,suffix:m")]
    public float Radius = 0.15f;

    /// <summary>
    /// What the projectile looks like. The mesh is drawn centred on the
    /// projectile's position.
    /// </summary>
    [Export]
    public Mesh Mesh;
}
