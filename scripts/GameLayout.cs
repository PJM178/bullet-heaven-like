using Godot;

namespace BulletHeavenLike;

/// <summary>
/// The root of the screen: the game view on top, the control panel (with
/// the joystick) below.
///
/// The 3D world isn't drawn straight to the screen. It renders into a
/// SubViewport, an off-screen image, which the GameView TextureRect then
/// shows in the top part. That way the GPU only draws the world for the
/// area you can actually see, and we can choose the resolution it renders
/// at (see RenderWidth).
/// </summary>
public partial class GameLayout : Control
{
    // Share of the screen height given to the game view; the control
    // panel gets the rest. 0.5 = half and half (the portrait layout),
    // 1 = the game fills the whole screen and the panel is hidden (the
    // landscape layout).
    [Export(PropertyHint.Range, "0.2,1,0.05")]
    public float GameViewFraction = 0.5f;

    // How many pixels wide the world is rendered, before being scaled up
    // to fill the game view. 0 renders at full resolution. A low value
    // such as 320 gives a chunky, PS1-style picture and is cheaper to
    // draw. The height always follows the game view's shape.
    [Export] public int RenderWidth = 0;

    private TextureRect _gameView;
    private SubViewport _subViewport;

    public override void _Ready()
    {
        _gameView = GetNode<TextureRect>("%GameView");
        _subViewport = GetNode<SubViewport>("%SubViewport");
        Control controlPanel = GetNode<Control>("%ControlPanel");

        // Both halves sit in a VBoxContainer set to "expand". The container
        // shares the height between them in proportion to these ratios.
        _gameView.SizeFlagsStretchRatio = GameViewFraction;
        controlPanel.SizeFlagsStretchRatio = 1.0f - GameViewFraction;

        // A hidden control takes no space in a container, so with the
        // panel hidden the game view gets the whole height.
        controlPanel.Visible = GameViewFraction < 1.0f;

        // Show the SubViewport's picture in the TextureRect. The texture
        // updates by itself every frame the SubViewport renders.
        _gameView.Texture = _subViewport.GetTexture();

        // Resized is an event (a C# signal) that fires whenever the control
        // changes size: at startup once the layout is worked out, and on
        // the desktop when the window is resized.
        _gameView.Resized += UpdateRenderSize;
        UpdateRenderSize();
    }

    /// <summary>
    /// Passes mouse movement on to the 3D world, so the player can face
    /// the cursor.
    ///
    /// A SubViewport shown through a TextureRect gets no input on its own
    /// (only a SubViewportContainer forwards it automatically). So we
    /// convert the mouse position from screen coordinates into the
    /// SubViewport's own pixels and push the event in by hand. Nodes in the
    /// world (Player) then receive it in their _Input.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventMouseMotion)
        {
            return;
        }

        Vector2 viewSize = _gameView.Size;
        if (viewSize.X < 1.0f || viewSize.Y < 1.0f)
        {
            return;
        }

        // Screen -> GameView local coordinates (the inverse of "local ->
        // screen"), then scale up or down to the SubViewport's resolution.
        // The SubViewport has the same shape as the view (UpdateRenderSize),
        // so a plain scale is enough.
        Vector2 scale = (Vector2)_subViewport.Size / viewSize;
        Transform2D screenToWorldView =
            Transform2D.Identity.Scaled(scale) * _gameView.GetGlobalTransformWithCanvas().AffineInverse();

        // XformedBy returns a copy of the event with its position moved,
        // so the original event is left untouched for the rest of the UI.
        // "true" tells PushInput the position is already in its coordinates.
        _subViewport.PushInput(@event.XformedBy(screenToWorldView), true);
    }

    /// <summary>
    /// Makes the SubViewport the same shape as the game view, so the
    /// picture fills it without stretching.
    /// </summary>
    private void UpdateRenderSize()
    {
        Vector2 viewSize = _gameView.Size;
        if (viewSize.X < 1.0f || viewSize.Y < 1.0f)
        {
            // Not laid out yet; Resized will call us again.
            return;
        }

        int width = RenderWidth > 0 ? RenderWidth : Mathf.RoundToInt(viewSize.X);
        int height = Mathf.Max(1, Mathf.RoundToInt(width * viewSize.Y / viewSize.X));
        _subViewport.Size = new Vector2I(width, height);
    }
}
