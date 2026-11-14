using Godot;

namespace BulletHeavenLike;

/// <summary>What happens when the player collects a pickup. Handled in
/// <see cref="PlayerStats.Collect"/>.</summary>
public enum PickupEffect
{
    Coins,
    Heal,
}

/// <summary>
/// Describes one kind of pickup: how it looks and what it does when the
/// player touches it. Saved as its own .tres file (e.g.
/// pickups/coins/coin_gold.tres), like SkillDefinition. A new kind of pickup
/// is a new .tres, not new code, unless it needs a new PickupEffect.
///
/// XP orbs are not pickups in this sense: XpManager handles them on its own
/// path (thousands of them, pulled in by the magnet).
/// </summary>
[GlobalClass]
public partial class PickupDefinition : Resource
{
    [Export] public string DisplayName = "";

    [Export] public PickupEffect Effect = PickupEffect.Coins;

    // How much of the effect: coins gained, hearts healed, ...
    [Export] public int Amount = 1;

    [ExportGroup("Looks")]
    // The sprite sheet, stored as a text path because local_assets/ is
    // git-ignored (see DirectionalSprite3D.FramesPath for why). Without the
    // file the pickup is invisible but still works.
    [Export(PropertyHint.File, "*.spritesheet")] public string FramesPath = "";
    // World units per sprite pixel. Characters use 0.02.
    [Export] public float PixelSize = 0.04f;
    // Height of the sprite's origin above the ground (for the coins, their
    // centre), so it floats instead of sinking into the floor.
    [Export] public float Height = 0.3f;

    // Loaded on first use and then reused by every pickup of this kind.
    // Not exported, so it isn't saved into the .tres.
    private SpriteFrames _frames;
    private StringName _animation;
    private bool _loaded;

    /// <summary>The sprite frames, or null if the file is missing.</summary>
    public SpriteFrames Frames
    {
        get
        {
            Load();
            return _frames;
        }
    }

    /// <summary>The animation to play: the sheet's first (and usually only)
    /// one, e.g. the coin spin.</summary>
    public StringName Animation
    {
        get
        {
            Load();
            return _animation;
        }
    }

    private void Load()
    {
        if (_loaded)
            return;
        _loaded = true;
        if (FramesPath == "" || !ResourceLoader.Exists(FramesPath))
        {
            GD.PushWarning($"Pickup '{DisplayName}': {FramesPath} not found (bake it into res://local_assets/).");
            return;
        }
        _frames = GD.Load<SpriteFrames>(FramesPath);
        _animation = _frames.GetAnimationNames()[0];
    }
}
