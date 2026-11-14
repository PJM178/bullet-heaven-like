using Godot;

namespace BulletHeavenLike;

/// <summary>
/// How much the player knows about a marked place. A marker only ever moves
/// up this list: once something is known it stays known.
/// </summary>
public enum MarkerState
{
    Hidden,     // not shown at all
    Rumoured,   // a faint "?": a direction, but not what it is
    Sensed,     // its real icon, dim, no distance
    Discovered, // full icon with its distance (it has been seen on screen)
}

/// <summary>
/// The shape drawn for a marker that has no Icon texture.
/// </summary>
public enum MarkerShape
{
    Diamond,
    Circle,
    Square,
}

/// <summary>
/// Describes one kind of world marker (a shrine, a chest, a boss, ...):
/// how it looks on the compass and when the player learns about it. Saved
/// as its own .tres, like PickupDefinition. A new kind of point of interest
/// is a new .tres, not new code.
///
/// <see cref="WorldMarkers"/> does the work; this is only data.
/// </summary>
[GlobalClass]
public partial class WorldMarkerDefinition : Resource
{
    // Shown on the compass when the facing cursor points at a discovered
    // marker of this kind.
    [Export] public string DisplayName = "";

    [ExportGroup("Looks")]
    [Export] public Color Color = new(1.0f, 0.85f, 0.4f);

    // Optional icon. Without one, Shape is drawn in Color.
    [Export] public Texture2D Icon;
    [Export] public MarkerShape Shape = MarkerShape.Diamond;

    // Draw "23 m" under the icon once it's discovered.
    [Export] public bool ShowDistance = true;

    [ExportGroup("Discovery")]
    // What the player knows from the start. Hidden suits most things;
    // Discovered suits places the game wants to point at right away.
    [Export] public MarkerState StartState = MarkerState.Hidden;

    // Within this many metres (flat distance on the ground) it shows as a
    // "?". 0 = never rumoured, it goes straight from hidden to sensed.
    [Export] public float RumourRange = 0.0f;

    // Within this many metres its real icon shows, dimmed.
    [Export] public float SenseRange = 20.0f;

    // Becomes Discovered the first time it's inside the camera's view.
    // Off = only Reveal() (an event, a map item) can discover it.
    [Export] public bool DiscoverOnScreen = true;
}
