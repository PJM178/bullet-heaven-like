using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The death sequence. Instead of a menu popping up, it plays out slowly:
///
///   0.0 s  the world drops into slow motion; the character falls
///          (Player) and the camera swoops in close and starts slowly
///          circling the body (CameraFollow)
///   1.0 s  the game freezes; the camera keeps circling
///   0.8 s+ the picture fades out through a chunky PS1 dither
///   2.2 s  the title dithers in: a PS1-style sign standing still in
///          the world over the body. It turns to keep facing the camera
///          as it circles; tilting or turning the phone angles it away,
///          and its letters warp across the sign's triangles
///   4.0 s  the run summary dithers in
///   4.8 s  the Restart button appears
///
/// The button comes last on purpose, so a finger still on the joystick
/// can't skip past the moment by accident.
///
/// The sign's angle follows the phone's gyroscope (on desktop, the mouse
/// position stands in for it) and settles back to facing the camera when
/// the phone is held still.
///
/// Like the pause menu it runs with ProcessMode Always (set in
/// game_over_screen.tscn) so it keeps working while paused, and sits on a
/// higher CanvasLayer than the other screens so nothing covers it.
/// </summary>
public partial class GameOverScreen : CanvasLayer
{
    [Export] public PlayerStats Stats;

    // Optional: only used for the kill count in the summary.
    [Export] public EnemyManager Enemies;

    // The game camera: the sign faces it and is drawn through a copy of it.
    [Export] public CameraFollow Camera;

    // The words shown on death.
    [Export] public string TitleText = "OVERRUN";

    // Game speed during the slow-motion part (1 = normal speed), and how
    // long (real seconds) until the game freezes completely.
    [Export(PropertyHint.Range, "0.05,1,0.05")] public float SlowMotionScale = 0.2f;
    [Export] public float SlowMotionDuration = 1.0f;

    // When the world starts fading (seconds after death) and how long
    // the fade takes. The delay leaves a moment to watch the fall.
    [Export] public float FadeDelay = 0.8f;
    [Export] public float FadeDuration = 3.0f;

    // When the title, the summary and the Restart button appear, in
    // seconds after death, and how long the title takes to dither in.
    [Export] public float TitleDelay = 2.2f;
    [Export] public float TitleRevealDuration = 1.8f;
    [Export] public float SummaryDelay = 4.0f;
    [Export] public float ButtonDelay = 4.8f;

    private ColorRect _fade;
    private ShaderMaterial _fadeMaterial;
    private Label _titleText;
    private SubViewport _titleTextViewport;
    private ShaderMaterial _titleMaterial;
    private bool _isMobile;
    private SubViewport _signViewport;
    private Camera3D _signCamera;
    private Label _summary;
    private ShaderMaterial _summaryMaterial;
    private Button _restartButton;

    public override void _Ready()
    {
        _fade = GetNode<ColorRect>("%Fade");
        // The material is a resource; casting it lets us set its uniforms.
        _fadeMaterial = (ShaderMaterial)_fade.Material;
        _titleText = GetNode<Label>("%TitleText");
        _titleTextViewport = GetNode<SubViewport>("%TitleTextViewport");
        // The sign's material gets the word's picture. GetTexture() returns
        // a ViewportTexture that shows whatever the viewport last rendered.
        _titleMaterial = TitleMaterial;
        _titleMaterial.SetShaderParameter("albedo_texture", _titleTextViewport.GetTexture());
        // The phone angles the sign with its gyroscope, the desktop with
        // the mouse (see UpdateSign).
        _isMobile = OS.HasFeature("mobile");

        _signViewport = GetNode<SubViewport>("%SignViewport");
        _signCamera = GetNode<Camera3D>("%SignCamera");
        GetNode<TextureRect>("%SignView").Texture = _signViewport.GetTexture();
        _summary = GetNode<Label>("%Summary");
        _summaryMaterial = (ShaderMaterial)_summary.Material;
        _restartButton = GetNode<Button>("%RestartButton");
        _restartButton.Pressed += Restart;
        Hide();

        if (Stats != null)
        {
            Stats.Died += OnDied;
        }
    }

    /// <summary>
    /// While the death plays out the game isn't paused yet (it's in slow
    /// motion), so the pause menu would happily open on top. Swallow the
    /// pause key while this screen is showing. This node comes last in
    /// game.tscn, and Godot sends _Input to the last nodes first, so it
    /// sees the key before the pause menu does.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!Visible)
        {
            return;
        }

        if (@event.IsActionPressed("pause"))
        {
            GetViewport().SetInputAsHandled();
        }
    }


    private void OnDied()
    {
        // String building is fine here: it happens once per run.
        int kills = Enemies?.Kills ?? 0;
        _titleText.Text = TitleText;
        _summary.Text = $"Level {Stats.Level}     {kills} slain";

        // Start everything invisible. The labels fade with their dither
        // shaders; the button uses Modulate's alpha (A), which fades a
        // control and its children together.
        _fadeMaterial.SetShaderParameter("amount", 0.0f);
        _titleMaterial.SetShaderParameter("reveal", 0.0f);
        _summaryMaterial.SetShaderParameter("reveal", 0.0f);
        _restartButton.Modulate = Colors.Transparent;
        _restartButton.Disabled = true;
        PlaceTitleSign();
        Show();

        PlaySequence();
    }

    /// <summary>
    /// Builds the whole sequence as one Tween. A Tween animates properties
    /// over time; Parallel() makes a step run alongside the previous one
    /// instead of after it, and SetDelay() holds a step back.
    /// </summary>
    private void PlaySequence()
    {
        Tween tween = CreateTween();
        // Keep running while the game is paused...
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        // ...and count real seconds, not slowed-down game seconds.
        tween.SetIgnoreTimeScale(true);

        // Slow motion: Engine.TimeScale slows the whole game (movement,
        // animation, timers). It's a static property, not a node property,
        // so TweenProperty can't reach it; TweenMethod calls our lambda with
        // each in-between value instead.
        tween.TweenMethod(
            Callable.From((float scale) => Engine.TimeScale = scale),
            1.0f, SlowMotionScale, SlowMotionDuration * 0.5f)
            .SetEase(Tween.EaseType.Out);

        // The world fades through the dither, after a moment to watch.
        tween.Parallel().TweenMethod(
            Callable.From((float amount) => _fadeMaterial.SetShaderParameter("amount", amount)),
            0.0f, 1.0f, FadeDuration)
            .SetDelay(FadeDelay)
            .SetTrans(Tween.TransitionType.Sine);

        // Freeze the game once the slow motion has run its course.
        tween.Parallel().TweenCallback(Callable.From(Freeze)).SetDelay(SlowMotionDuration);

        // The title dithers in.
        tween.Parallel().TweenMethod(
            Callable.From((float reveal) => _titleMaterial.SetShaderParameter("reveal", reveal)),
            0.0f, 1.0f, TitleRevealDuration)
            .SetDelay(TitleDelay);

        tween.Parallel().TweenMethod(
            Callable.From((float reveal) => _summaryMaterial.SetShaderParameter("reveal", reveal)),
            0.0f, 1.0f, 0.8f)
            .SetDelay(SummaryDelay);

        tween.Parallel().TweenProperty(_restartButton, "modulate:a", 1.0f, 0.6f)
            .SetDelay(ButtonDelay);
        tween.Parallel().TweenCallback(Callable.From(() => _restartButton.Disabled = false))
            .SetDelay(ButtonDelay);
    }

    // ---- The title sign --------------------------------------------------
    //
    // The PS1 texture warp only shows on 3D triangles seen at an angle, so
    // the title is a sign standing in the game world:
    //
    //   TitleTextViewport  draws the word (a Label) into a texture, once
    //   the sign           a flat mesh of a few big triangles, textured
    //                      with the word through ui/ps1_title.gdshader
    //
    // The sign stays still; the camera circling the body is what changes
    // the angle and makes the letters bend.

    // The sign is drawn on its own render layer, which the game camera
    // stops drawing on death. SignCamera, a copy of the game camera that
    // draws only that layer, renders it into SignViewport, shown above the
    // fade. So the sign sits in the world (same place, same angle, same
    // warp) but the darkening fade doesn't dim it. Layer 20 is the last
    // one; each layer is one bit, so layer 20 is bit 19.
    private const uint SignLayer = 1u << 19;

    // The sign's shape and material (set in game_over_screen.tscn).
    [Export] public Mesh TitleMesh;
    [Export] public ShaderMaterial TitleMaterial;

    // How high above the ground the middle of the sign floats, in metres.
    [Export] public float TitleHeight = 1.5f;

    // How the sign follows the phone. Strength is how many degrees the
    // sign turns per degree the phone turns (negative reverses it), it
    // never turns further than SignMaxAngle away from facing the camera,
    // and SignReturnSpeed is how quickly it drifts back to facing the
    // camera (which also cancels the gyroscope's slow drift).
    [Export] public float SignGyroStrength = 1.5f;
    [Export] public float SignMaxAngle = 40.0f;
    [Export] public float SignReturnSpeed = 1.0f;

    private MeshInstance3D _sign;
    // The phone's turn of the sign away from facing the camera, radians:
    // yaw around the vertical, pitch tipping it forward or back.
    private float _signYaw;
    private float _signPitch;

    private void PlaceTitleSign()
    {
        // Draw the word into its texture once; it doesn't change after.
        _titleTextViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

        // The Stats node sits under the Player, and the Player in the
        // world, so the sign goes into the world next to the Player. It
        // is created once per run (the restart reloads the whole scene,
        // which frees it), so creating a node here is fine.
        Node3D player = Stats.GetParent<Node3D>();
        _sign = new MeshInstance3D
        {
            Mesh = TitleMesh,
            MaterialOverride = TitleMaterial,
            // An unshaded sign casts no believable shadow; skip it.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Layers = SignLayer,
        };
        player.GetParent().AddChild(_sign);

        // Its angle is set every frame in UpdateSign.
        _signYaw = 0.0f;
        _signPitch = 0.0f;
        _sign.GlobalPosition = player.GlobalPosition + new Vector3(0.0f, TitleHeight, 0.0f);

        if (Camera != null)
        {
            // "&= ~" clears one bit: the game camera stops drawing the sign.
            Camera.CullMask &= ~SignLayer;
            // Render the sign layer into the same 3D world the game uses.
            _signViewport.World3D = Camera.GetWorld3D();
            _signViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible || Camera == null)
        {
            return;
        }

        UpdateSign((float)delta);

        // Keep the sign camera an exact copy of the game camera. The game
        // camera moves earlier in the frame (it comes first in the tree),
        // so this copies its latest position.
        _signCamera.GlobalTransform = Camera.GlobalTransform;
        _signCamera.Fov = Camera.Fov;
        _signCamera.Near = Camera.Near;
        _signCamera.Far = Camera.Far;
        // Same picture size as the game view, so the two line up exactly.
        // The game camera lives in the game's SubViewport (see GameLayout).
        if (Camera.GetViewport() is SubViewport gameView)
        {
            _signViewport.Size = gameView.Size;
        }
    }

    /// <summary>
    /// Turns the sign to face the camera, then angles it with the phone.
    /// On the phone the gyroscope reports how fast the phone is rotating
    /// (radians per second) around the screen's own axes: Y runs up the
    /// screen (turning the phone left or right), X across it (tipping the
    /// top toward or away from you). Adding up rate * time gives how far
    /// it has turned. On desktop the mouse's position on screen sets the
    /// angle directly instead.
    /// </summary>
    private void UpdateSign(float delta)
    {
        // Real seconds, even during the death slow-motion.
        if (Engine.TimeScale > 0.0)
        {
            delta /= (float)Engine.TimeScale;
        }
        float maxAngle = Mathf.DegToRad(SignMaxAngle);

        if (_isMobile)
        {
            Vector3 rate = Input.GetGyroscope();
            _signYaw += rate.Y * SignGyroStrength * delta;
            _signPitch += rate.X * SignGyroStrength * delta;

            // Drift back toward facing the camera. Same 1 - Exp smoothing
            // as the follow camera, so it behaves alike at any frame rate.
            float settle = 1.0f - Mathf.Exp(-SignReturnSpeed * delta);
            _signYaw = Mathf.Lerp(_signYaw, 0.0f, settle);
            _signPitch = Mathf.Lerp(_signPitch, 0.0f, settle);
        }
        else
        {
            // Mouse position as 0..1 across the window, then -1..1 from
            // the centre: the sign turns toward wherever the mouse is.
            Vector2 mouse = GetViewport().GetMousePosition() / GetViewport().GetVisibleRect().Size;
            float targetYaw = (mouse.X - 0.5f) * 2.0f * maxAngle;
            float targetPitch = (mouse.Y - 0.5f) * 2.0f * maxAngle;
            float follow = 1.0f - Mathf.Exp(-8.0f * delta);
            _signYaw = Mathf.Lerp(_signYaw, targetYaw, follow);
            _signPitch = Mathf.Lerp(_signPitch, targetPitch, follow);
        }

        _signYaw = Mathf.Clamp(_signYaw, -maxAngle, maxAngle);
        _signPitch = Mathf.Clamp(_signPitch, -maxAngle, maxAngle);

        // Face the circling camera (seen from above). A QuadMesh faces +Z,
        // and Atan2(x, z) is the angle that turns +Z toward (x, z).
        Vector3 toCamera = Camera.GlobalPosition - _sign.GlobalPosition;
        float facing = Mathf.Atan2(toCamera.X, toCamera.Z);

        // Basis * Basis combines rotations: first face the camera and turn
        // by the phone's yaw, then tip around the sign's own sideways axis.
        _sign.Basis = new Basis(Vector3.Up, facing + _signYaw)
            * new Basis(Vector3.Right, _signPitch);
    }

    private void Freeze()
    {
        GetTree().Paused = true;
        // Paused already stops the game; put the speed back to normal so
        // nothing else ends up running slowly.
        Engine.TimeScale = 1.0;
    }

    private void Restart()
    {
        // The pause flag and Engine.TimeScale belong to the whole engine,
        // not the scene, so they would survive the reload and freeze or
        // slow down the new run.
        GetTree().Paused = false;
        Engine.TimeScale = 1.0;
        // Throws the whole running scene (game.tscn) away and loads it
        // fresh, which resets every enemy, orb, skill and stat at once.
        GetTree().ReloadCurrentScene();
    }
}
