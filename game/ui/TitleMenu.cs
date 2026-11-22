using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The first screen: the game's name, the banked coins, and buttons to
/// start a run or open the upgrades. It is the project's main scene
/// (Project Settings > Application > Run > Main Scene).
///
/// For now the menu stands in for the base area of the meta progression
/// (docs/design/meta-progression.md); a walkable base may replace it later.
/// </summary>
public partial class TitleMenu : Control
{
    // Scene paths rather than PackedScene exports: game.tscn links back to
    // this menu (the game-over Menu button), and scenes that reference each
    // other in a loop can't be loaded.
    [Export(PropertyHint.File, "*.tscn")] public string GameScenePath = "res://game.tscn";
    [Export(PropertyHint.File, "*.tscn")] public string UpgradeMenuPath = "res://meta/upgrade_menu.tscn";

    public override void _Ready()
    {
        // The game's name from Project Settings, so it is written in one place.
        GetNode<Label>("%Title").Text = (string)ProjectSettings.GetSetting("application/config/name");
        GetNode<Label>("%Coins").Text = $"{Profile.Instance?.Coins ?? 0} coins";

        // ChangeSceneToFile frees this menu and loads the new scene in its
        // place (at the end of the frame, so finishing this call is safe).
        GetNode<Button>("%StartButton").Pressed += () => GetTree().ChangeSceneToFile(GameScenePath);
        GetNode<Button>("%UpgradesButton").Pressed += () => GetTree().ChangeSceneToFile(UpgradeMenuPath);
    }
}
