using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Spends banked coins on upgrades: one row per upgrade in
/// <see cref="Profile.Upgrades"/>, showing its level, its current bonus and
/// a Buy button with the next level's price.
///
/// The rows are built in code, not laid out in the .tscn, because their
/// number comes from the Profile's list: adding an upgrade .tres to that
/// list adds a row here with no UI work. Building nodes is fine in a menu;
/// the no-allocation rule is for per-frame code in the run.
/// </summary>
public partial class UpgradeMenu : Control
{
    [Export(PropertyHint.File, "*.tscn")] public string BackScenePath = "res://ui/title_menu.tscn";

    private Label _coins;
    private VBoxContainer _list;
    private Button _reset;

    // Reset wipes the save, so it takes two taps: the first one only arms
    // it (and changes the button text), the second one resets.
    private bool _resetArmed;

    public override void _Ready()
    {
        _coins = GetNode<Label>("%Coins");
        _list = GetNode<VBoxContainer>("%UpgradeList");
        GetNode<Button>("%BackButton").Pressed += () => GetTree().ChangeSceneToFile(BackScenePath);
        _reset = GetNode<Button>("%ResetButton");
        _reset.Pressed += OnResetPressed;
        Rebuild();
    }

    private void OnResetPressed()
    {
        if (!_resetArmed)
        {
            _resetArmed = true;
            _reset.Text = "Tap again: lose all coins and upgrades";
            return;
        }
        Profile.Instance?.ResetProgress();
        Rebuild();
    }

    /// <summary>
    /// Clears the list and builds it again from the Profile. Called after
    /// every purchase: simpler than updating each label in place, and a
    /// handful of rows costs nothing.
    /// </summary>
    private void Rebuild()
    {
        Profile profile = Profile.Instance;
        if (profile == null)
        {
            _coins.Text = "No profile (is the Profile autoload set up?)";
            return;
        }

        _coins.Text = $"{profile.Coins} coins";

        // Any rebuild (a purchase, or the reset itself) disarms the reset.
        _resetArmed = false;
        _reset.Text = "Reset progress";

        foreach (Node child in _list.GetChildren())
        {
            // RemoveChild first, so the new rows don't sit beside the old
            // ones until QueueFree frees them at the end of the frame.
            _list.RemoveChild(child);
            child.QueueFree();
        }

        foreach (UpgradeDefinition upgrade in profile.Upgrades)
            _list.AddChild(MakeRow(profile, upgrade));
    }

    private Control MakeRow(Profile profile, UpgradeDefinition upgrade)
    {
        int level = profile.GetLevel(upgrade);

        // Left: name, then a smaller line with the level and bonus.
        var name = new Label { Text = upgrade.DisplayName };
        name.AddThemeFontSizeOverride("font_size", 38);

        // "0.##" prints at most two decimals and drops trailing zeros:
        // 1.5 -> "1.5", 2.25 -> "2.25", 3 -> "3".
        string bonus = (upgrade.BonusPerLevel * level).ToString("0.##");
        var details = new Label
        {
            Text = $"Level {level}/{upgrade.MaxLevel}   +{bonus}{upgrade.Unit}\n{upgrade.Description}",
        };
        details.AddThemeFontSizeOverride("font_size", 26);
        details.AddThemeColorOverride("font_color", new Color(0.72f, 0.69f, 0.64f));

        var text = new VBoxContainer
        {
            // Expand to fill the row, pushing the button to the right edge.
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        text.AddChild(name);
        text.AddChild(details);

        // Right: the Buy button.
        int cost = profile.NextCost(upgrade);
        var buy = new Button
        {
            Text = cost < 0 ? "Max" : $"Buy  {cost}",
            Disabled = !profile.CanBuy(upgrade),
            CustomMinimumSize = new Vector2(240, 100),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
        };
        buy.AddThemeFontSizeOverride("font_size", 34);
        // A lambda that remembers "upgrade": each row's button buys its own.
        buy.Pressed += () =>
        {
            if (profile.TryBuy(upgrade))
                Rebuild();
        };

        var row = new HBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
        row.AddThemeConstantOverride("separation", 32);
        row.AddChild(text);
        row.AddChild(buy);
        return row;
    }
}
