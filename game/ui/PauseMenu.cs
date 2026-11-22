using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The pause screen: dims the game and shows a title and a Resume button.
/// It opens when the "pause" action is pressed (Esc or Tab). (Level-ups
/// have their own screen, LevelUpScreen.)
///
/// Pausing uses Godot's built-in pause: setting GetTree().Paused = true
/// stops _Process / _PhysicsProcess (and input) for every node whose
/// ProcessMode lets it pause, which by default is all of them. This menu
/// sets its own ProcessMode to Always (in pause_menu.tscn), so it keeps
/// running and can unpause the game. Its children inherit that mode.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    [Export] public string PausedTitle = "Paused";

    private Label _title;

    public override void _Ready()
    {
        _title = GetNode<Label>("%Title");
        GetNode<Button>("%ResumeButton").Pressed += Close;

        // Hidden and unpaused at the start. A CanvasLayer can be hidden
        // like a Control; everything on it disappears with it.
        Hide();
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
            // Something else paused the game (the level-up screen): leave
            // it alone, or closing this menu would unpause under it.
            if (!Visible && GetTree().Paused)
            {
                return;
            }
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
