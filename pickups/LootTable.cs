using Godot;

namespace BulletHeavenLike;

/// <summary>
/// What something drops: a list of <see cref="LootDrop"/> lines, each
/// rolled on its own. Anything that hands out pickups holds one of these
/// (a breakable prop now; chests, events and enemies later) and passes it
/// to <see cref="PickupManager.DropLoot"/>, which does the rolling and the
/// placing. So a new source of loot needs no loot code of its own.
///
/// It can live inline in the definition that owns it (a sub-resource, like
/// the barrel's), or be saved as its own .tres when several sources should
/// drop the same thing: point them all at the same file and tuning it
/// changes every one of them.
/// </summary>
[GlobalClass]
public partial class LootTable : Resource
{
    [Export] public LootDrop[] Drops = System.Array.Empty<LootDrop>();
}
