using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Describes one skill the player can learn (for now each skill is a
/// weapon): what the level-up card shows, and which weapon node it turns on.
///
/// This is a Resource, not a Node: a piece of data saved as its own .tres
/// file (e.g. weapons/hammer/hammer_skill.tres) and shared by reference. The
/// SkillManager's pool is a list of these. [GlobalClass] registers the type
/// with the editor, so "New Resource..." offers it and exported fields of
/// this type only accept it.
/// </summary>
[GlobalClass]
public partial class SkillDefinition : Resource
{
    // Shown as the card's title.
    [Export] public string DisplayName = "";

    // Shown on the card under the title. MultilineText gives a bigger text
    // box in the Inspector.
    [Export(PropertyHint.MultilineText)] public string Description = "";

    // Picture on the card. Optional.
    [Export] public Texture2D Icon;

    // Name of the weapon node under the Player that this skill switches on
    // (e.g. "HammerWeapon"). Weapons sit in main.tscn already wired up;
    // until learned they are switched off (see SkillManager).
    [Export] public string WeaponNodeName = "";

    // How likely the skill is to be offered compared with the others: a
    // skill with weight 2 turns up twice as often as one with weight 1.
    [Export(PropertyHint.Range, "0,10,0.1")] public float Weight = 1.0f;

    // How many times it can be learned. Each skill is 1 for now; later,
    // picking a skill again can upgrade it.
    [Export(PropertyHint.Range, "1,10,1")] public int MaxLevel = 1;
}
