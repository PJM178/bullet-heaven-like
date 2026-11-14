using Godot;

namespace BulletHeavenLike;

/// <summary>
/// Owns every pickup in the world except XP orbs (coins, hearts, ...), and
/// collects them when the player touches them.
///
/// <see cref="Drop"/> is the one way a pickup enters the world: enemy loot,
/// chests and placed pickups (<see cref="PlacedPickup"/>) all go through it.
/// What a pickup looks like and does comes from its
/// <see cref="PickupDefinition"/>; what collecting it does is in
/// <see cref="PlayerStats.Collect"/>.
///
/// Same data layout as the other managers: plain arrays, one loop, swap
/// remove. Pickups animate (the coin spin), so unlike XP orbs each one is a
/// real AnimatedSprite3D, from a pool made once in _Ready and reused.
///
/// Keep this node at the origin with no rotation or scale; pickup
/// positions are used as-is.
/// </summary>
public partial class PickupManager : Node3D
{
    /// <summary>The group this manager is in, so pickups placed in a level
    /// can find it without a NodePath.</summary>
    public const string Group = "pickup_manager";

    // Who collects the pickups. The pickup radius is the player's stat
    // (PlayerStats.PickupRadius), so upgrades only need to change it there.
    [Export] public Player Player;

    // The most pickups that can exist at once (and the size of the sprite
    // pool). Pickups are far rarer than XP orbs.
    [Export] public int MaxPickups = 200;

    // ---- Pickup data. Index i in every array is the same pickup. Only the
    // first _count entries are alive. ------------------------------------
    private Vector3[] _positions;           // on the ground
    private PickupDefinition[] _definitions;
    private AnimatedSprite3D[] _sprites;    // the pool; entries move with their pickup
    private int _count;

    private readonly RandomNumberGenerator _random = new();

    /// <summary>How many pickups exist (for the debug HUD).</summary>
    public int Count => _count;

    public override void _EnterTree()
    {
        // _EnterTree runs before any _Ready, so placed pickups always find
        // the manager in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        _positions = new Vector3[MaxPickups];
        _definitions = new PickupDefinition[MaxPickups];
        _sprites = new AnimatedSprite3D[MaxPickups];
        for (int i = 0; i < MaxPickups; i++)
        {
            _sprites[i] = new AnimatedSprite3D { Visible = false };
            AddChild(_sprites[i]);
        }
    }

    /// <summary>
    /// Puts a pickup of kind <paramref name="definition"/> on the ground at
    /// <paramref name="groundPosition"/>.
    /// </summary>
    public void Drop(PickupDefinition definition, Vector3 groundPosition)
    {
        if (definition == null)
            return;

        if (_count >= MaxPickups)
        {
            // Full: give it to the player straight away rather than lose it
            // (coins are kept between runs, so losing them would hurt).
            GD.PushWarning($"PickupManager full ({MaxPickups}); collecting {definition.DisplayName} directly.");
            Player?.Stats.Collect(definition);
            return;
        }

        int i = _count;
        _count++;
        _positions[i] = new Vector3(groundPosition.X, 0.0f, groundPosition.Z);
        _definitions[i] = definition;

        AnimatedSprite3D sprite = _sprites[i];
        SpriteFrames frames = definition.Frames;
        // Only reconfigure the sprite if it last showed a different kind.
        if (sprite.SpriteFrames != frames)
        {
            sprite.SpriteFrames = frames;
            if (frames != null)
                SpriteDirections.Setup(sprite);
        }
        sprite.PixelSize = definition.PixelSize;
        sprite.Position = _positions[i] + Vector3.Up * definition.Height;
        if (frames != null)
        {
            sprite.Play(definition.Animation);
            // Start at a random frame so a pile of coins doesn't spin in step.
            int frameCount = frames.GetFrameCount(definition.Animation);
            sprite.Frame = _random.RandiRange(0, frameCount - 1);
        }
        sprite.Visible = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || Player.Stats.IsDead)
            return;

        Vector3 playerPosition = Player.GlobalPosition;
        float radius = Player.Stats.PickupRadius;
        float radiusSquared = radius * radius;

        // Backwards, because collecting moves the last pickup into the
        // collected one's slot (swap remove); that one was already checked.
        for (int i = _count - 1; i >= 0; i--)
        {
            // Distance on the ground only: height doesn't matter for touch.
            float dx = _positions[i].X - playerPosition.X;
            float dz = _positions[i].Z - playerPosition.Z;
            if (dx * dx + dz * dz <= radiusSquared)
            {
                Player.Stats.Collect(_definitions[i]);
                Remove(i);
            }
        }
    }

    /// <summary>
    /// Removes pickup i by moving the last pickup into its slot. The sprite
    /// nodes are swapped too, so the moved pickup keeps its sprite (and its
    /// animation frame) and the freed sprite goes to the end of the pool.
    /// </summary>
    private void Remove(int i)
    {
        int last = _count - 1;
        AnimatedSprite3D removed = _sprites[i];
        removed.Visible = false;
        removed.Stop();

        if (i != last)
        {
            _positions[i] = _positions[last];
            _definitions[i] = _definitions[last];
            _sprites[i] = _sprites[last];
            _sprites[last] = removed;
        }
        _definitions[last] = null; // don't keep the definition alive for nothing
        _count--;
    }
}
