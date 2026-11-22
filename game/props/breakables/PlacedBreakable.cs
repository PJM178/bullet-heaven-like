using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A breakable prop placed by hand in a level. When the game starts it
/// hands its position, rotation and scale to the
/// <see cref="BreakableManager"/> (which then owns and draws it) and removes
/// itself. Same idea as <see cref="PlacedPickup"/>.
///
/// In the editor it's just a marker: the model appears when the game runs.
/// </summary>
public partial class PlacedBreakable : Node3D
{
    [Export] public BreakableDefinition Definition;

    public override void _Ready()
    {
        // Deferred so the BreakableManager has finished its own _Ready,
        // whichever of the two comes first in the scene.
        CallDeferred(MethodName.HandOver);
    }

    private void HandOver()
    {
        var manager = GetTree().GetFirstNodeInGroup(BreakableManager.Group) as BreakableManager;
        if (manager == null)
        {
            GD.PushWarning($"{Name}: no BreakableManager in the scene.");
        }
        else
        {
            manager.Add(Definition, GlobalTransform);
        }
        QueueFree();
    }
}
