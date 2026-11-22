using Godot;

namespace BulletHeavenLike;

/// <summary>
/// One choice on the level-up screen: a big button showing a skill's icon,
/// name, description and whether it is new or an upgrade.
///
/// The card is a Button so it handles taps, hover and focus itself. Its
/// contents are ordinary child Controls with mouse_filter "Ignore", so taps
/// go through them to the button (see skill_card.tscn).
/// </summary>
public partial class SkillCard : Button
{
    // Emitted when the card is chosen. LevelUpScreen listens to it.
    [Signal] public delegate void ChosenEventHandler(SkillDefinition skill);

    public SkillDefinition Skill { get; private set; }

    private TextureRect _icon;
    private Label _name;
    private Label _tag;
    private Label _description;

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("%Icon");
        _name = GetNode<Label>("%Name");
        _tag = GetNode<Label>("%Tag");
        _description = GetNode<Label>("%Description");

        // Pressed is the Button's own signal. A lambda ("() => ...") is a
        // small unnamed function, handy for one-line handlers.
        Pressed += () => EmitSignal(SignalName.Chosen, Skill);
    }

    /// <summary>
    /// Fills the card. currentLevel is the skill's level now (0 = not
    /// learned yet).
    /// </summary>
    public void SetSkill(SkillDefinition skill, int currentLevel)
    {
        Skill = skill;
        _icon.Texture = skill.Icon;
        _name.Text = skill.DisplayName;
        _tag.Text = currentLevel == 0 ? "New!" : $"Level {currentLevel + 1}";
        _description.Text = skill.Description;
    }
}
