using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Adds a scene next to itself (as a sibling, under this node's parent) if
/// the scene file exists, and does nothing if it doesn't.
///
/// For local-only extras that aren't in git, like the research hookups in
/// research/ (git-excluded): a committed scene can't reference those files
/// directly, or it would fail to load in a fresh clone. A path stored as
/// plain text is not a dependency, so the committed scene always loads.
///
/// The added scene's root becomes a sibling, so NodePaths inside it such as
/// "../HammerWeapon" resolve exactly as if it had been placed there by hand.
/// </summary>
public partial class OptionalScene : Node
{
    [Export(PropertyHint.File, "*.tscn")] public string ScenePath = "";

    public override void _Ready()
    {
        if (ScenePath == "" || !ResourceLoader.Exists(ScenePath))
            return;

        Node instance = GD.Load<PackedScene>(ScenePath).Instantiate();
        // The parent is still adding its own children while _Ready runs here,
        // and Godot refuses new children then. CallDeferred waits until the
        // end of the current frame, when it's safe.
        GetParent().CallDeferred(Node.MethodName.AddChild, instance);
    }
}
