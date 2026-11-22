using System.Text;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Debug: a "Stats" button on the right edge of the screen. Pressing it
/// pauses the game and shows the player's current numbers in a panel in
/// the middle of the screen; Close (or the pause key) hides the panel and
/// carries on.
///
/// Like the pause menu it runs with ProcessMode Always (set in
/// debug_stats_panel.tscn), so its buttons still work while the game is
/// paused. If something else already paused the game (the pause menu, the
/// level-up screen), opening the panel leaves the pause alone, so closing
/// it doesn't unpause the game under that screen.
/// </summary>
public partial class DebugStatsPanel : CanvasLayer
{
    [Export] public Player Player;

    // Optional: lists the learned skills.
    [Export] public SkillManager Skills;

    private Button _statsButton;
    private Control _overlay;
    private Label _stats;

    // True if opening the panel paused the game, so closing must unpause.
    private bool _pausedByPanel;

    public override void _Ready()
    {
        _statsButton = GetNode<Button>("%StatsButton");
        _overlay = GetNode<Control>("%Overlay");
        _stats = GetNode<Label>("%StatsText");

        _statsButton.Pressed += Open;
        GetNode<Button>("%CloseButton").Pressed += Close;
        _overlay.Hide();

        // Once the player dies the game over screen owns the pause (it
        // freezes the game itself), so the button goes away. Player.Stats
        // is set in the Player's _Ready, which runs before this one
        // because the world comes earlier in game.tscn.
        if (Player?.Stats != null)
        {
            Player.Stats.Died += () =>
            {
                Close();
                _statsButton.Hide();
            };
        }
    }

    /// <summary>
    /// The pause key closes the panel. This node comes after the pause
    /// menu in game.tscn, and _Input reaches later nodes first, so it gets
    /// the key before the pause menu does and can mark it as used.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (_overlay.Visible && @event.IsActionPressed("pause"))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Open()
    {
        if (Player?.Stats == null)
        {
            return;
        }

        _pausedByPanel = !GetTree().Paused;
        GetTree().Paused = true;

        // The game is paused while the panel is open, so the numbers can't
        // change: building the text once, here, is enough.
        _stats.Text = BuildText();
        _overlay.Show();
    }

    private void Close()
    {
        if (!_overlay.Visible)
        {
            return;
        }
        _overlay.Hide();
        if (_pausedByPanel)
        {
            GetTree().Paused = false;
            _pausedByPanel = false;
        }
    }

    private string BuildText()
    {
        PlayerStats stats = Player.Stats;

        // StringBuilder collects pieces of text and joins them once at the
        // end, instead of making a new string for every "+". Allocating is
        // fine here: this runs once per button press, not every frame.
        var text = new StringBuilder();
        text.AppendLine($"Hearts        {stats.Health} / {stats.MaxHealth}");

        string level = stats.IsMaxLevel ? $"{stats.Level} (max)" : $"{stats.Level}";
        text.AppendLine($"Level         {level}");
        text.AppendLine($"XP            {stats.ExperienceIntoLevel} / {stats.ExperienceToNextLevel}");
        text.AppendLine($"Total XP      {stats.TotalExperience}");
        text.AppendLine($"Coins         {stats.Coins}");
        text.AppendLine();
        text.AppendLine($"Speed         {Player.Speed:0.##} m/s");
        text.AppendLine($"Pickup radius {stats.PickupRadius:0.##} m");
        text.AppendLine($"Magnet radius {stats.MagnetRadius:0.##} m");
        text.AppendLine($"Hit invuln.   {stats.InvulnerabilityTime:0.##} s");
        text.AppendLine($"Debug invuln. {(stats.DebugInvulnerable ? "ON" : "off")}");

        if (Skills != null)
        {
            text.AppendLine();
            text.AppendLine("Skills");
            bool any = false;
            foreach (SkillDefinition skill in Skills.Pool)
            {
                int skillLevel = Skills.GetLevel(skill);
                if (skillLevel > 0)
                {
                    text.AppendLine($"  {skill.DisplayName}  Lv {skillLevel}");
                    any = true;
                }
            }
            if (!any)
            {
                text.AppendLine("  none");
            }
        }

        // TrimEnd drops the last line break, so the panel has no empty
        // line at the bottom.
        return text.ToString().TrimEnd();
    }
}
