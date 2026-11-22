using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A pickup placed by hand in a level. When the game starts it hands itself
/// to the <see cref="PickupManager"/> (which then owns and draws it) and
/// removes itself, so placed and dropped pickups work exactly the same way.
///
/// In the editor it's just a marker: the sprite appears when the game runs.
/// </summary>
public partial class PlacedPickup : Node3D
{
    [Export] public PickupDefinition Definition;

    public override void _Ready()
    {
        // Deferred (run at the end of this frame) so the PickupManager has
        // finished its own _Ready and built its sprite pool, whichever of
        // the two comes first in the scene.
        CallDeferred(MethodName.HandOver);
    }

    private void HandOver()
    {
        var manager = GetTree().GetFirstNodeInGroup(PickupManager.Group) as PickupManager;
        if (manager == null)
        {
            GD.PushWarning($"{Name}: no PickupManager in the scene.");
        }
        else
        {
            manager.Drop(Definition, GlobalPosition);
        }
        QueueFree();
    }
}
