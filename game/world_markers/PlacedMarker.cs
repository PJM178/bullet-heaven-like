using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A world marker placed by hand in a level (a point of interest). When the
/// game starts it adds itself to <see cref="WorldMarkers"/>.
///
/// Unlike PlacedPickup it stays in the scene, so whatever is built under it
/// (a shrine model, a chest) stays too. For prototyping, ShowDebugPillar
/// stands a tall coloured post there, so there is something to find.
/// </summary>
public partial class PlacedMarker : Node3D
{
    [Export] public WorldMarkerDefinition Definition;
    [Export] public bool ShowDebugPillar = true;

    private WorldMarkers _markers;
    private int _id = -1;

    public override void _Ready()
    {
        if (ShowDebugPillar && Definition != null)
        {
            var pillar = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.4f, Height = 4.0f, RadialSegments = 6 },
                Position = Vector3.Up * 2.0f,
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = Definition.Color,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
            };
            AddChild(pillar);
        }

        // Deferred so WorldMarkers is surely in the tree and set up,
        // whichever of the two comes first in the scene.
        CallDeferred(MethodName.Register);
    }

    private void Register()
    {
        _markers = GetTree().GetFirstNodeInGroup(WorldMarkers.Group) as WorldMarkers;
        if (_markers == null)
        {
            GD.PushWarning($"{Name}: no WorldMarkers in the scene.");
            return;
        }
        _id = _markers.Add(Definition, GlobalPosition);
    }

    public override void _ExitTree()
    {
        // Taken out of the level (looted, destroyed): drop its marker too.
        _markers?.Remove(_id);
        _id = -1;
    }
}
