using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A small on-screen overlay for performance testing on the phone: shows
/// FPS, enemy count, draw calls and frame timings, with buttons to add or
/// remove enemies. Remove it (or hide it) once we're done profiling.
/// </summary>
public partial class StressTestHud : CanvasLayer
{
    [Export] public EnemyManager Enemies;
    [Export] public ProjectileManager Projectiles;

    // How many enemies each button press adds or removes.
    [Export] public int Step = 50;

    // Seconds between label refreshes. Updating text builds a new string,
    // so we don't do it every frame.
    [Export] public double RefreshInterval = 0.25;

    private Label _stats;
    private double _sinceRefresh;

    public override void _Ready()
    {
        // "%Name" finds a node marked "Access as Unique Name" in the scene,
        // so the script keeps working if the layout is rearranged.
        _stats = GetNode<Label>("%Stats");

        // Pressed is a C# event; "+=" subscribes a method to it. The
        // "() => ..." lambdas are tiny inline methods.
        GetNode<Button>("%LessButton").Pressed += () => ChangeTarget(-Step);
        GetNode<Button>("%MoreButton").Pressed += () => ChangeTarget(Step);
    }

    private void ChangeTarget(int amount)
    {
        if (Enemies == null)
        {
            return;
        }
        Enemies.TargetCount = Mathf.Clamp(Enemies.TargetCount + amount, 0, Enemies.MaxEnemies);
    }

    public override void _Process(double delta)
    {
        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshInterval || Enemies == null)
        {
            return;
        }
        _sinceRefresh = 0.0;

        // Performance.GetMonitor reads the same numbers as the editor's
        // Debugger > Monitors tab. Times are in seconds; * 1000 = ms.
        double fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        double processMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double physicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        double drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);

        // $"..." is string interpolation; ":0.0" formats to one decimal.
        _stats.Text =
            $"FPS {fps:0}\n" +
            $"Enemies {Enemies.Count} / {Enemies.TargetCount}\n" +
            // "?." returns null instead of crashing if Projectiles isn't
            // set, and "?? 0" then substitutes 0 for that null.
            $"Projectiles {Projectiles?.Count ?? 0}\n" +
            $"Draw calls {drawCalls:0}\n" +
            $"Process {processMs:0.0} ms  Physics {physicsMs:0.0} ms";
    }
}
