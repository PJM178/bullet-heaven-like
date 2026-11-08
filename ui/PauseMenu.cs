using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The pause screen: dims the game and shows a title and a Resume button.
/// It opens when the "pause" action is pressed (Esc or Tab), or when the
/// player gains a level, where it will later offer the upgrade choices.
///
/// Pausing uses Godot's built-in pause: setting GetTree().Paused = true
/// stops _Process / _PhysicsProcess (and input) for every node whose
/// ProcessMode lets it pause, which by default is all of them. This menu
/// sets its own ProcessMode to Always (in pause_menu.tscn), so it keeps
/// running and can unpause the game. Its children inherit that mode.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    // The stats whose LeveledUp signal opens this menu.
    [Export] public PlayerStats Stats;

    [Export] public string PausedTitle = "Paused";
    [Export] public string LevelUpTitle = "Level Up!";

    private Label _title;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        GetNode<Button>("%ResumeButton").Pressed += Close;

        // Hidden and unpaused at the start. A CanvasLayer can be hidden
        // like a Control; everything on it disappears with it.
        Hide();

        if (Stats != null)
        {
            Stats.LeveledUp += OnLeveledUp;
        }
    }

    /// <summary>
    /// _Input sees events before the UI does. That matters for Tab: the UI
    /// uses Tab to move keyboard focus between buttons, and we want it to
    /// toggle the pause instead.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        // IsActionPressed is true only on the press itself, not while the
        // key is held, so one press toggles once.
        if (@event.IsActionPressed("pause"))
        {
            if (Visible)
            {
                Close();
            }
            else
            {
                Open(PausedTitle);
            }
            // Mark the event as used so nothing else reacts to it.
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnLeveledUp(int newLevel)
    {
        // One big XP pickup can emit LeveledUp several times in a row.
        // Opening twice is harmless: the second call just sets the same text.
        Open(LevelUpTitle);
    }

    public void Open(string title)
    {
        _title.Text = title;
        Show();
        GetTree().Paused = true;
    }

    public void Close()
    {
        Hide();
        GetTree().Paused = false;
    }
}
