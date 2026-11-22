using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A weapon that throws or shoots projectiles: every Cooldown seconds it
/// fires Count projectiles of kind <see cref="Projectile"/>, in a fan
/// SpreadDegrees wide, aimed by Targeting (see WeaponTargeting). With
/// ClosestEnemies each projectile instead flies at its own enemy: the first
/// at the closest, the second at the next closest, and so on. ShotDelay
/// spaces a volley's projectiles out in time instead of sending them all at
/// once.
///
/// It only decides when and where to fire. Flying, hitting, walls and
/// drawing all happen in the <see cref="ProjectileManager"/>, the same for
/// every projectile weapon.
///
/// Put it directly under the Player. Like the other weapons it is a skill:
/// the SkillManager switches it on when its skill is learned.
/// </summary>
public partial class ProjectileWeapon : Node3D
{
    // What it fires. A subclass can build one in code instead (see
    // BuildProjectile).
    [Export] public ProjectileDefinition Projectile;

    // Optional: found by group (or the parent, for Player) when not set, so
    // a weapon added at runtime needs no NodePaths.
    [Export] public Player Player;
    [Export] public EnemyManager EnemyManager;
    [Export] public ProjectileManager ProjectileManager;

    [ExportGroup("Firing")]
    // Seconds between volleys.
    [Export] public float Cooldown = 0.8f;
    // Projectiles per volley, spread evenly over SpreadDegrees.
    [Export(PropertyHint.Range, "1,16,1")] public int Count = 1;
    [Export(PropertyHint.Range, "0,360,1")] public float SpreadDegrees = 0.0f;
    // Seconds between the projectiles of one volley: 0 = all at once. Each
    // one aims when it leaves, from where the player is then.
    [Export(PropertyHint.Range, "0,1,0.01")] public float ShotDelay = 0.0f;

    [Export] public TargetMode Targeting = TargetMode.ClosestEnemy;
    [Export] public float TargetRange = 9.0f;

    [ExportGroup("Sound")]
    // Played once per volley (or per delayed shot). Optional.
    [Export] public AudioStream Sound;
    [Export(PropertyHint.Range, "-40,6,0.5")] public float VolumeDb = -8.0f;

    // Run after EnemyManager has moved enemies this step (like the weapons).
    private const int PhysicsPriorityAfterEnemies = 10;

    // Enemies picked by a ClosestEnemies volley, nearest first: one per
    // projectile, so as many as Count allows.
    private readonly int[] _targets = new int[16];

    private ProjectileDefinition _projectile;
    private AudioStreamPlayer _soundPlayer;
    private float _cooldownLeft;

    // The volley in progress: which projectile (0 .. Count-1) goes next,
    // and the time until it does. _nextShot == Count means none is pending.
    private int _nextShot;
    private float _shotDelayLeft;

    public override void _Ready()
    {
        ProcessPhysicsPriority = PhysicsPriorityAfterEnemies;
        _nextShot = Count;

        // "??=" assigns only if the left side is still null.
        Player ??= GetParent() as Player;
        EnemyManager ??= GetTree().GetFirstNodeInGroup(EnemyManager.Group) as EnemyManager;
        ProjectileManager ??= GetTree().GetFirstNodeInGroup(ProjectileManager.Group) as ProjectileManager;

        _projectile = BuildProjectile();
        if (Player == null || ProjectileManager == null || _projectile == null)
        {
            GD.PushWarning($"{Name}: needs a Player parent, a ProjectileManager and a projectile; switched off.");
            ProcessMode = ProcessModeEnum.Disabled;
            return;
        }

        AudioStream sound = BuildSound();
        if (sound != null)
        {
            _soundPlayer = new AudioStreamPlayer
            {
                Stream = sound,
                VolumeDb = VolumeDb,
                // Lets a quick weapon overlap a few volleys.
                MaxPolyphony = 4,
            };
            AddChild(_soundPlayer);
        }
    }

    /// <summary>
    /// The projectile this weapon fires: <see cref="Projectile"/> by default.
    /// "virtual" lets a subclass replace it ("override"), e.g. to build the
    /// definition in code from files that only exist locally.
    /// </summary>
    protected virtual ProjectileDefinition BuildProjectile() => Projectile;

    /// <summary>The volley sound: <see cref="Sound"/> by default.</summary>
    protected virtual AudioStream BuildSound() => Sound;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        _cooldownLeft -= dt;
        if (_cooldownLeft <= 0.0f)
        {
            _cooldownLeft += Cooldown;
            // Start a new volley: the first projectile leaves right away.
            _nextShot = 0;
            _shotDelayLeft = 0.0f;
        }

        if (_nextShot < Count)
        {
            _shotDelayLeft -= dt;
            FireDue();
        }
    }

    /// <summary>
    /// Fires every projectile of the current volley whose turn has come:
    /// with ShotDelay 0 that's all of them at once, otherwise one at a time.
    /// </summary>
    private void FireDue()
    {
        bool fired = false;
        while (_nextShot < Count && _shotDelayLeft <= 0.0f)
        {
            // "|=" keeps fired true once any shot went out.
            fired |= FireOne(_nextShot);
            _nextShot++;
            _shotDelayLeft += ShotDelay;
        }

        // One sound per batch, so an all-at-once volley doesn't stack them.
        if (fired && _soundPlayer != null)
        {
            _soundPlayer.Play();
        }
    }

    /// <summary>
    /// Fires projectile number n (0 = first) of a volley. Returns false if
    /// the ProjectileManager was full.
    /// </summary>
    private bool FireOne(int n)
    {
        Vector3 aim = WeaponTargeting.AimDirection(Targeting, Player, EnemyManager, TargetRange);
        Vector3 origin = Player.GlobalPosition;
        origin.Y = 0.0f;

        // ClosestEnemies: projectile n goes at the (n+1)-th closest enemy.
        // When there are fewer enemies than that, it takes its fan slot.
        int targetCount = 0;
        if (Targeting == TargetMode.ClosestEnemies && EnemyManager != null)
        {
            targetCount = EnemyManager.FindClosest(origin, TargetRange, _targets);
        }

        Vector3 direction;
        if (n < targetCount)
        {
            direction = DirectionTo(origin, EnemyManager.GetPosition(_targets[n]), aim);
        }
        else
        {
            // Even fan: with 3 and 30 degrees the angles are -15, 0 and +15.
            float spread = Mathf.DegToRad(SpreadDegrees);
            float step = Count > 1 ? spread / (Count - 1) : 0.0f;
            direction = aim.Rotated(Vector3.Up, -spread * 0.5f + step * n);
        }

        // The player is passed along so a returning projectile comes back.
        return ProjectileManager.Spawn(_projectile, origin, direction, Player);
    }

    // Flat direction from one point to another, length 1, or fallback when
    // they are on top of each other.
    private static Vector3 DirectionTo(Vector3 from, Vector3 to, Vector3 fallback)
    {
        Vector3 offset = to - from;
        offset.Y = 0.0f;
        return offset.LengthSquared() > 0.0001f ? offset.Normalized() : fallback;
    }
}
