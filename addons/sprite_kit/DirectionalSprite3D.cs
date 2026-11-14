using Godot;

namespace BulletHeavenLike;

/// <summary>
/// An AnimatedSprite3D for imported 8-direction sprite sheets (.spritesheet). Assign the
/// .spritesheet file to "Sprite Frames" and call <see cref="PlayFacing"/> each
/// frame. It works out which of the 8 directions to show and only touches the
/// animation when the anim or direction actually changes.
/// [GlobalClass] makes it appear in Godot's "Add Node" dialog.
/// </summary>
[GlobalClass]
public partial class DirectionalSprite3D : AnimatedSprite3D
{
    // Remembered in plain C# fields: reading the engine's Animation property
    // from C# creates a new object every call, which adds up with many enemies.
    private string _anim = "";
    private int _direction = -1;
    // The world direction from the last PlayFacing, so RefreshFacing can
    // re-pick the drawn direction when the camera moves.
    private Vector3 _facing;

    /// <summary>Every DirectionalSprite3D joins this group (see _Ready), so a
    /// moving camera can refresh them all with GetTree().CallGroup.</summary>
    public const string Group = "directional_sprites";

    /// <summary>
    /// A .spritesheet to load at startup when "Sprite Frames" is empty, e.g.
    /// "res://local_assets/players/player_m.spritesheet". Stored as plain text instead of a
    /// resource reference on purpose: res://local_assets/ is git-ignored, and a scene
    /// that references a missing resource fails to load in a fresh clone. With
    /// a text path the scene always loads; the sprite is just empty without local_assets/.
    /// </summary>
    [Export(PropertyHint.File, "*.spritesheet")] public string FramesPath = "";

    public override void _Ready()
    {
        if (SpriteFrames == null && FramesPath != "")
        {
            if (ResourceLoader.Exists(FramesPath))
                SpriteFrames = GD.Load<SpriteFrames>(FramesPath);
            else
                GD.PushWarning($"{Name}: {FramesPath} not found (bake it into res://local_assets/).");
        }
        if (SpriteFrames != null)
            SpriteDirections.Setup(this);
        AddToGroup(Group);
    }

    /// <summary>
    /// Re-picks the drawn direction for the current animation and facing as
    /// seen from <paramref name="camera"/>. For when the camera turns while
    /// the character doesn't (e.g. a camera circling a paused scene).
    /// </summary>
    public void RefreshFacing(Camera3D camera)
    {
        // Nothing played yet: there's no animation to turn.
        if (_anim != "")
            PlayFacing(_anim, _facing, camera);
    }

    /// <summary>
    /// Plays <paramref name="anim"/> ("walk", "attack", ...) in the direction that
    /// <paramref name="facing"/> shows from <paramref name="camera"/>. If only the direction
    /// changed, the current frame is kept so turning doesn't restart the cycle.
    /// </summary>
    public void PlayFacing(string anim, Vector3 facing, Camera3D camera)
    {
        // No frames (res://local_assets/ not baked, e.g. a fresh clone): nothing to play.
        if (SpriteFrames == null)
            return;

        _facing = facing;
        int direction = SpriteDirections.DirectionIndex(facing, camera);
        // Reference comparison is enough: callers pass string literals, and C#
        // gives every identical literal the same string object.
        bool sameAnim = ReferenceEquals(anim, _anim) || anim == _anim;
        if (sameAnim && direction == _direction)
            return;

        int frame = Frame;
        float progress = FrameProgress;
        bool wasPlaying = IsPlaying();
        Play(SpriteDirections.AnimationName(anim, direction));
        if (sameAnim)
        {
            SetFrameAndProgress(frame, progress);
            // Turning shouldn't restart a paused or finished animation
            // (a held pose, a corpse's last frame): Play() would.
            if (!wasPlaying)
                Pause();
        }

        _anim = anim;
        _direction = direction;
    }
}
