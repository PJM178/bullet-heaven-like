using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The always-visible game HUD. For now it's the row of hearts along the
/// top of the screen, one per point of health.
///
/// It doesn't read the health every frame. Instead it listens to
/// PlayerStats.HealthChanged and only updates the hearts when health
/// actually changes.
/// </summary>
public partial class PlayerHud : CanvasLayer
{
    [Export] public PlayerStats Stats;

    // On-screen size of one heart, in pixels.
    [Export] public Vector2 HeartSize = new(64, 58);

    private Container _heartRow;

    public override void _Ready()
    {
        _heartRow = GetNode<Container>("%Hearts");

        if (Stats == null)
        {
            GD.PushWarning("PlayerHud needs Stats set.");
            return;
        }

        Stats.HealthChanged += OnHealthChanged;
        // Show the starting hearts. PlayerStats sits deeper in the tree, so
        // its _Ready (which sets Health) has already run by now: Godot
        // readies children before their parents, and the HUD comes after
        // the world in game.tscn.
        OnHealthChanged(Stats.Health, Stats.MaxHealth);
    }

    private void OnHealthChanged(int health, int maxHealth)
    {
        // Make sure there is one heart icon per point of max health. This
        // only creates nodes when max health grows, which is rare, so it's
        // fine outside the pooling rule.
        while (_heartRow.GetChildCount() < maxHealth)
        {
            var heart = new HeartIcon { CustomMinimumSize = HeartSize };
            _heartRow.AddChild(heart);
        }

        int count = _heartRow.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            // GetChild<T> casts the child to the type we know it is.
            var heart = _heartRow.GetChild<HeartIcon>(i);
            heart.Visible = i < maxHealth;
            // Hearts fill from the left: with 2 of 3 health, hearts 0 and
            // 1 are full and heart 2 is empty.
            heart.Full = i < health;
        }
    }
}
