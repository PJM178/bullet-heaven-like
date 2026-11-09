using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Shown when the player dies: pauses the game, shows how the run went and
/// offers a Restart button.
///
/// Like the pause menu it runs with ProcessMode Always (set in
/// game_over_screen.tscn) so its button still works while paused. It sits
/// on a higher CanvasLayer than the other screens so nothing covers it.
/// </summary>
public partial class GameOverScreen : CanvasLayer
{
    [Export] public PlayerStats Stats;

    // Optional: only used for the kill count in the summary.
    [Export] public EnemyManager Enemies;

    private Label _summary;

    public override void _Ready()
    {
        _summary = GetNode<Label>("%Summary");
        GetNode<Button>("%RestartButton").Pressed += Restart;
        Hide();

        if (Stats != null)
        {
            Stats.Died += OnDied;
        }
    }

    private void OnDied()
    {
        // String building is fine here: it happens once per run, not
        // every frame.
        int kills = Enemies?.Kills ?? 0;
        _summary.Text = $"Level {Stats.Level}   Kills {kills}";

        Show();
        GetTree().Paused = true;
    }

    private void Restart()
    {
        // Unpause first: the pause flag belongs to the SceneTree, not the
        // scene, so it would survive the reload and freeze the new run.
        GetTree().Paused = false;
        // Throws the whole running scene (game.tscn) away and loads it
        // fresh, which resets every enemy, orb, skill and stat at once.
        GetTree().ReloadCurrentScene();
    }
}
