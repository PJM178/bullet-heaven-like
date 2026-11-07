using Godot;

namespace BulletHeavenLike;

/// <summary>
/// TEMPORARY: fires one projectile straight ahead of the player on a fixed
/// timer, to test the ProjectileManager before real weapons exist. It will
/// be replaced by WeaponDefinition / WeaponMount.
/// </summary>
public partial class ProjectileTestShooter : Node
{
    [Export] public Player Player;
    [Export] public ProjectileManager ProjectileManager;
    [Export] public ProjectileDefinition Projectile;

    // Seconds between shots.
    [Export] public float FireInterval = 0.5f;

    // Where the shot starts, relative to the player's feet: this far in
    // front of them and this high off the ground. Low, so shots pass
    // through the short slimes instead of over them.
    [Export] public float MuzzleForward = 0.3f;
    [Export] public float MuzzleHeight = 0.3f;

    private float _cooldown;

    public override void _PhysicsProcess(double delta)
    {
        if (Player == null || ProjectileManager == null || Projectile == null)
        {
            return;
        }

        _cooldown -= (float)delta;
        if (_cooldown > 0.0f)
        {
            return;
        }
        // "+=" rather than "= FireInterval" keeps the rhythm exact: if this
        // frame came a little late, the next shot comes a little sooner.
        _cooldown += FireInterval;

        Vector3 forward = Player.Forward;
        Vector3 muzzle = Player.GlobalPosition + forward * MuzzleForward + Vector3.Up * MuzzleHeight;
        ProjectileManager.Spawn(Projectile, muzzle, forward);
    }
}
