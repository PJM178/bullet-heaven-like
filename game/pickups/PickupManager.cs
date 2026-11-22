using System;
using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The pickups in the scene (coins, hearts, ...; not XP orbs): the "shell"
/// around <see cref="PickupSimulation"/>, which knows where every pickup is
/// and who touched it. This node owns one, steps it every physics tick,
/// draws the pickups and gives what players collect to them. Same split as
/// EnemyManager / EnemySimulation.
///
/// <see cref="Drop"/> is the one way a pickup enters the world: loot
/// (<see cref="DropLoot"/>) and placed pickups (<see cref="PlacedPickup"/>)
/// all go through it.
/// What a pickup looks like and does comes from its
/// <see cref="PickupDefinition"/>; what collecting it does is in
/// <see cref="PlayerStats.Collect"/>.
///
/// Pickups animate (the coin spin), so unlike XP orbs each one is a real
/// AnimatedSprite3D: one per slot, made once in _Ready and reused.
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

    // The most pickups that can exist at once (and the number of sprite
    // nodes). Pickups are far rarer than XP orbs.
    [Export] public int MaxPickups = 200;

    // The most pickups one loot table can roll at once.
    private const int MaxLootPerRoll = 64;

    // The game logic. Null until _Ready.
    private PickupSimulation _sim;

    // One sprite per slot (see PickupSimulation): the pickup in slot s is
    // always drawn by _sprites[s].
    private AnimatedSprite3D[] _sprites;

    // What is handed to the core every step, made once and reused. One
    // entry per player; there is only one player for now.
    private readonly Vector3[] _playerPositions = new Vector3[1];
    private readonly float[] _pickupRadii = new float[1];

    // Where RollLoot writes what it rolled, reused for every roll.
    private readonly PickupSimulation.LootRoll[] _loot = new PickupSimulation.LootRoll[MaxLootPerRoll];

    // Looks only: the random start frame of a coin's spin.
    private readonly RandomNumberGenerator _random = new();

    /// <summary>How many pickups exist (for the debug HUD).</summary>
    public int Count => _sim?.Count ?? 0;

    public override void _EnterTree()
    {
        // _EnterTree runs before any _Ready, so placed pickups always find
        // the manager in the group.
        AddToGroup(Group);
    }

    public override void _Ready()
    {
        _sim = new PickupSimulation(MaxPickups);
        _sprites = new AnimatedSprite3D[_sim.Capacity];
        for (int s = 0; s < _sprites.Length; s++)
        {
            _sprites[s] = new AnimatedSprite3D { Visible = false };
            AddChild(_sprites[s]);
        }
    }

    /// <summary>
    /// Puts a pickup of kind <paramref name="definition"/> on the ground at
    /// <paramref name="groundPosition"/>.
    /// </summary>
    public void Drop(PickupDefinition definition, Vector3 groundPosition)
    {
        if (definition == null || _sim == null)
            return;

        int i = _sim.Drop(definition, groundPosition);
        if (i == PickupSimulation.NoPickup)
        {
            // Full: give it to the player straight away rather than lose it
            // (coins are kept between runs, so losing them would hurt).
            GD.PushWarning($"PickupManager full ({MaxPickups}); collecting {definition.DisplayName} directly.");
            Player?.Stats.Collect(definition);
            return;
        }

        AnimatedSprite3D sprite = _sprites[_sim.GetSlot(i)];
        SpriteFrames frames = definition.Frames;
        // Only reconfigure the sprite if it last showed a different kind.
        if (sprite.SpriteFrames != frames)
        {
            sprite.SpriteFrames = frames;
            if (frames != null)
                SpriteDirections.Setup(sprite);
        }
        sprite.PixelSize = definition.PixelSize;
        sprite.Position = _sim.Positions[i] + Vector3.Up * definition.Height;
        if (frames != null)
        {
            sprite.Play(definition.Animation);
            // Start at a random frame so a pile of coins doesn't spin in step.
            int frameCount = frames.GetFrameCount(definition.Animation);
            sprite.Frame = _random.RandiRange(0, frameCount - 1);
        }
        sprite.Visible = true;
    }

    /// <summary>
    /// Rolls every line of <paramref name="table"/> and drops what comes
    /// up, scattered at random within <paramref name="scatter"/> metres of
    /// <paramref name="centre"/>. Breakables, chests, events and enemies all
    /// hand their table to this. The rolling is done by the core.
    /// </summary>
    public void DropLoot(LootTable table, Vector3 centre, float scatter)
    {
        if (_sim == null)
            return;

        int count = _sim.RollLoot(table, centre, scatter, _loot);
        for (int n = 0; n < count; n++)
        {
            Drop(_loot[n].Definition, _loot[n].Position);
            _loot[n].Definition = null; // don't keep it alive for nothing
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || Player.Stats.IsDead)
            return;

        // Godot -> core: where the player is and how far they reach.
        _playerPositions[0] = Player.GlobalPosition;
        _pickupRadii[0] = Player.Stats.PickupRadius;

        _sim.Step(_playerPositions, _pickupRadii);

        // Core -> Godot: hide the sprites of collected pickups...
        ReadOnlySpan<int> removed = _sim.RemovedSlots;
        for (int n = 0; n < removed.Length; n++)
        {
            AnimatedSprite3D sprite = _sprites[removed[n]];
            sprite.Visible = false;
            sprite.Stop();
        }

        // ...and give the player what they collected. Only one player for
        // now, so every collection is theirs.
        ReadOnlySpan<PickupSimulation.Collection> collected = _sim.Collected;
        for (int n = 0; n < collected.Length; n++)
        {
            Player.Stats.Collect(collected[n].Definition);
        }
    }
}
