using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Keeps track of every marked place in the world (points of interest,
/// events, bosses, ...) and how much the player knows about each one (see
/// <see cref="MarkerState"/>). It knows nothing about how markers are shown:
/// the <see cref="CompassBar"/> reads from it, and later a minimap or
/// edge-of-screen arrows could read the same data.
///
/// Game systems only talk to this manager:
///   int id = markers.Add(definition, position);   // or Add(definition, node)
///   markers.Reveal(id, MarkerState.Discovered);  // an event tells the player
///   markers.Remove(id);                          // it's gone (looted, killed)
///
/// Markers are few, so each one keeps its slot until removed and the id is
/// simply the slot number. Don't hold on to an id after removing it: the
/// slot gets reused by the next Add.
/// </summary>
public partial class WorldMarkers : Node
{
    /// <summary>The group this manager is in, so other systems (and the
    /// HUD, which sits outside the world's SubViewport) can find it without
    /// a NodePath.</summary>
    public const string Group = "world_markers";

    // Distances are measured from the player, and "seen on screen" uses
    // the camera.
    [Export] public Player Player;
    [Export] public Camera3D Camera;

    [Export] public int MaxMarkers = 64;

    // Markers count as "on screen" only this far inside the view's edges
    // (a fraction of its size), so something at the very rim isn't
    // discovered by a single stray pixel.
    [Export(PropertyHint.Range, "0,0.3,0.01")] public float ScreenMargin = 0.05f;

    // ---- Marker data. Index i in every array is the same marker. --------
    private WorldMarkerDefinition[] _definitions; // null = free slot
    private Vector3[] _positions;
    private Node3D[] _targets;   // optional node the marker follows
    private MarkerState[] _states;

    public override void _EnterTree()
    {
        // _EnterTree runs before any _Ready, so placed markers always find
        // the manager in the group.
        AddToGroup(Group);

        // _EnterTree runs again if the node is ever moved in the tree;
        // only make the arrays the first time.
        if (_definitions != null)
            return;
        _definitions = new WorldMarkerDefinition[MaxMarkers];
        _positions = new Vector3[MaxMarkers];
        _targets = new Node3D[MaxMarkers];
        _states = new MarkerState[MaxMarkers];
    }

    /// <summary>Number of slots. Loop 0..Capacity-1 and skip the ones
    /// where <see cref="IsActive"/> is false.</summary>
    public int Capacity => MaxMarkers;

    public bool IsActive(int id) => _definitions[id] != null;
    public WorldMarkerDefinition GetDefinition(int id) => _definitions[id];
    public Vector3 GetPosition(int id) => _positions[id];
    public MarkerState GetState(int id) => _states[id];

    /// <summary>Marks a fixed place. Returns its id, or -1 if it couldn't
    /// be added.</summary>
    public int Add(WorldMarkerDefinition definition, Vector3 position)
    {
        if (definition == null)
            return -1;

        for (int i = 0; i < MaxMarkers; i++)
        {
            if (_definitions[i] != null)
                continue;

            _definitions[i] = definition;
            _positions[i] = position;
            _targets[i] = null;
            _states[i] = definition.StartState;
            return i;
        }

        GD.PushWarning($"WorldMarkers full ({MaxMarkers}); {definition.DisplayName} not added.");
        return -1;
    }

    /// <summary>Marks a node that may move (a boss, a cart). The marker
    /// follows it until it's removed or the node is freed.</summary>
    public int Add(WorldMarkerDefinition definition, Node3D target)
    {
        int id = Add(definition, target.GlobalPosition);
        if (id >= 0)
            _targets[id] = target;
        return id;
    }

    /// <summary>Takes a marker off the compass. Ids of -1 are ignored, so
    /// callers don't have to check whether Add worked.</summary>
    public void Remove(int id)
    {
        if (id < 0 || id >= MaxMarkers)
            return;
        _definitions[id] = null;
        _targets[id] = null;
    }

    /// <summary>Tells the player about a marker (an event, a map item). It
    /// never makes a marker less known than it already is.</summary>
    public void Reveal(int id, MarkerState state)
    {
        if (id < 0 || id >= MaxMarkers || _definitions[id] == null)
            return;
        if (state > _states[id])
            _states[id] = state;
    }

    public override void _Process(double delta)
    {
        if (Player == null)
            return;

        Vector3 playerPosition = Player.GlobalPosition;
        Rect2 screen = Camera?.GetViewport().GetVisibleRect() ?? new Rect2();
        // Shrink the view by the margin on every side.
        screen = screen.Grow(-ScreenMargin * Mathf.Min(screen.Size.X, screen.Size.Y));

        for (int i = 0; i < MaxMarkers; i++)
        {
            WorldMarkerDefinition definition = _definitions[i];
            if (definition == null)
                continue;

            // Follow the target node, if it has one. IsInstanceValid is
            // false once the node has been freed: then the marker stays
            // where the node was last seen.
            Node3D target = _targets[i];
            if (target != null)
            {
                if (GodotObject.IsInstanceValid(target))
                    _positions[i] = target.GlobalPosition;
                else
                    _targets[i] = null;
            }

            if (_states[i] == MarkerState.Discovered)
                continue;

            Vector3 offset = _positions[i] - playerPosition;
            offset.Y = 0.0f;
            float distance = offset.Length();

            MarkerState state = _states[i];
            if (definition.DiscoverOnScreen && IsOnScreen(_positions[i], screen))
                state = MarkerState.Discovered;
            else if (distance <= definition.SenseRange)
                state = MarkerState.Sensed;
            else if (distance <= definition.RumourRange)
                state = MarkerState.Rumoured;

            // Only ever move up (see MarkerState).
            if (state > _states[i])
                _states[i] = state;
        }
    }

    private bool IsOnScreen(Vector3 position, Rect2 screen)
    {
        if (Camera == null)
            return false;
        // UnprojectPosition gives a pixel even for points behind the
        // camera (mirrored), so check that first.
        if (Camera.IsPositionBehind(position))
            return false;
        return screen.HasPoint(Camera.UnprojectPosition(position));
    }
}
