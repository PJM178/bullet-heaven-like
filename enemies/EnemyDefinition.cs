using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Describes one kind of enemy: what it looks like, how tough and fast it
/// is, and how big it is. Saved as its own .tres under enemies/kinds/, like
/// BreakableDefinition and PickupDefinition. A new kind of enemy is a new
/// .tres, not new code.
///
/// The <see cref="EnemyManager"/> does all the work; this is only data. Each
/// kind gets its own pool of nodes, made once when the game starts.
/// </summary>
[GlobalClass]
public partial class EnemyDefinition : Resource
{
    [Export] public string DisplayName = "";

    [ExportGroup("Looks")]
    // The enemy's scene. Two kinds work:
    // - a 3D model with an AnimationPlayer at "Model/AnimationPlayer"
    //   playing a "move" clip (enemies/slime.tscn), or
    // - an 8-direction sprite: a DirectionalSprite3D at "Model/Sprite"
    //   playing "walk", and optionally "die" (enemies/basic_enemy.tscn).
    [Export] public PackedScene Scene;

    // Used instead of Scene when Scene is a sprite enemy whose frames aren't
    // there (local_assets/ is git-ignored, so a fresh clone has none).
    // Keeps the game playable without them.
    [Export] public PackedScene FallbackScene;

    [ExportGroup("Stats")]
    // Metres per second.
    [Export] public float Speed = 2.0f;

    // Hit points it starts with. The sword does 1 per swing.
    [Export] public float MaxHealth = 2.0f;

    // Hearts it takes when it touches the player, and how close (centre to
    // centre, in metres) counts as touching. Enemies stop at
    // EnemyManager.StopDistance and the crowd jostles them a little further
    // out, so this needs to be somewhat larger than StopDistance.
    [Export] public int ContactDamage = 1;
    [Export] public float ContactRange = 0.9f;

    // Experience dropped when it dies.
    [Export] public int XpPerKill = 100;

    [ExportGroup("Size")]
    // How big it is for weapons and projectiles, in metres on the ground
    // (see EnemyManager.HitTest).
    [Export] public float HitRadius = 0.25f;

    // How big its body is against walls and props (see WorldObstacles).
    [Export] public float BodyRadius = 0.3f;

    [ExportGroup("Pools")]
    // The most of this kind that can be alive at once. Every one is made
    // when the game starts, so don't set it far above what a run needs.
    [Export] public int PoolSize = 300;

    // Sprite enemies with a "die" animation play it on a small pool of
    // corpses (see EnemyManager). If more die at once than there are
    // corpses, the oldest is reused early.
    [Export] public int CorpsePoolSize = 64;
}
