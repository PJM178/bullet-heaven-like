using Godot;

namespace BulletHeavenLike;

/// <summary>
/// A floating on-screen joystick. Put a finger down anywhere inside this
/// control (in game.tscn it covers the whole screen) and the joystick
/// appears under it; drag to steer, lift to stop. Buttons win: a touch
/// that lands on a button presses the button and doesn't start the
/// joystick.
///
/// It doesn't move the player itself. It presses the same input actions
/// as the arrow keys (move_left, move_right, ...), with a strength between
/// 0 and 1, so Player reads it through Input.GetVector exactly like the
/// keyboard and needs no changes. Keyboard input keeps working alongside.
///
/// On the desktop, project setting "Emulate Touch From Mouse" turns mouse
/// clicks into touches, so the joystick can be tested with the mouse.
/// </summary>
public partial class VirtualJoystick : Control
{
    // How far (in UI pixels) the knob can move from the centre. Dragging
    // further than this still counts as full tilt.
    [Export] public float Radius = 150.0f;

    // Size of the knob drawn under the finger.
    [Export] public float KnobRadius = 60.0f;

    // Tilt below this fraction of Radius is ignored, so a resting thumb
    // that wobbles slightly doesn't make the player creep around.
    [Export(PropertyHint.Range, "0,0.9,0.05")] public float DeadZone = 0.15f;

    [Export] public Color BaseColor = new(1.0f, 1.0f, 1.0f, 0.12f);
    [Export] public Color RingColor = new(1.0f, 1.0f, 1.0f, 0.35f);
    [Export] public Color KnobColor = new(1.0f, 1.0f, 1.0f, 0.55f);

    // The action names, created once so pressing them every drag event
    // doesn't build new StringNames.
    private static readonly StringName MoveLeft = "move_left";
    private static readonly StringName MoveRight = "move_right";
    private static readonly StringName MoveUp = "move_up";
    private static readonly StringName MoveDown = "move_down";

    // Several fingers can touch the screen at once, and Godot numbers them
    // (Index). We follow only the finger that started the joystick, so a
    // second finger elsewhere can't take over. -1 means "no finger".
    private int _fingerIndex = -1;

    // Where the joystick appeared and where the knob is now, both in this
    // control's own (local) coordinates, which is what _Draw uses.
    private Vector2 _center;
    private Vector2 _knob;

    /// <summary>
    /// Starting the joystick happens here, in _UnhandledInput, which Godot
    /// calls only for events the UI didn't use. So a touch on a button
    /// (the HUD's +50, later ability buttons) presses the button and never
    /// reaches us: buttons automatically win, without the joystick having
    /// to know about them.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        // "@event" is just a parameter called "event"; the @ lets us use a
        // C# keyword as a name.
        //
        // "is InputEventScreenTouch touch" is pattern matching: it checks
        // the event's type and, if it matches, gives us a variable of that
        // type in one step.
        if (@event is InputEventScreenTouch touch
            && touch.Pressed
            && _fingerIndex == -1
            && GetGlobalRect().HasPoint(touch.Position))
        {
            // A new finger inside our area: the joystick appears here.
            _fingerIndex = touch.Index;
            _center = ToLocalPoint(touch.Position);
            _knob = _center;
            UpdateOutput();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// Once the joystick is held, its finger is followed in _Input, which
    /// sees every event before the UI does. That way the finger keeps
    /// steering even if it slides over a button while held down.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventScreenTouch touch)
        {
            if (!touch.Pressed && touch.Index == _fingerIndex)
            {
                Release();
                GetViewport().SetInputAsHandled();
            }
        }
        else if (@event is InputEventScreenDrag drag && drag.Index == _fingerIndex)
        {
            // Keep the knob inside the ring, but let the finger go further.
            // LimitLength shortens the vector only if it's longer than Radius.
            Vector2 offset = (ToLocalPoint(drag.Position) - _center).LimitLength(Radius);
            _knob = _center + offset;
            UpdateOutput();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // If the app loses focus mid-drag (a call, the home button), the
        // finger-up event never arrives. Let go, or the player would keep
        // running on their own when the game comes back.
        if (what == NotificationApplicationFocusOut || what == NotificationVisibilityChanged)
        {
            Release();
        }
    }

    public override void _Draw()
    {
        // Nothing to draw while no finger is down.
        if (_fingerIndex == -1)
        {
            return;
        }

        DrawCircle(_center, Radius, BaseColor);
        // DrawArc from 0 to Tau (a full turn) draws the ring's outline.
        DrawArc(_center, Radius, 0.0f, Mathf.Tau, 48, RingColor, 4.0f, true);
        DrawCircle(_knob, KnobRadius, KnobColor);
    }

    /// <summary>
    /// Turns the knob position into a direction and presses the move
    /// actions to match.
    /// </summary>
    private void UpdateOutput()
    {
        // Redraw on the next frame, since the knob moved.
        QueueRedraw();

        Vector2 offset = _knob - _center;
        Vector2 direction = Vector2.Zero;

        // Outside the dead zone the player runs at full speed in the
        // stick's direction: the stick only steers. ("Normalized" makes
        // the vector length 1.) Most bullet heavens play like this, since
        // dodging needs full speed, not careful creeping.
        if (offset.Length() > Radius * DeadZone)
        {
            direction = offset.Normalized();
        }

        // Screen y grows downwards, so a negative y means "up".
        SetAction(MoveRight, Mathf.Max(direction.X, 0.0f));
        SetAction(MoveLeft, Mathf.Max(-direction.X, 0.0f));
        SetAction(MoveDown, Mathf.Max(direction.Y, 0.0f));
        SetAction(MoveUp, Mathf.Max(-direction.Y, 0.0f));
    }

    /// <summary>
    /// Presses an action with a strength from 0 to 1, or releases it at 0.
    /// </summary>
    private static void SetAction(StringName action, float strength)
    {
        if (strength > 0.0f)
        {
            Input.ActionPress(action, strength);
        }
        else
        {
            Input.ActionRelease(action);
        }
    }

    private void Release()
    {
        if (_fingerIndex == -1)
        {
            return;
        }

        _fingerIndex = -1;
        Input.ActionRelease(MoveLeft);
        Input.ActionRelease(MoveRight);
        Input.ActionRelease(MoveUp);
        Input.ActionRelease(MoveDown);
        QueueRedraw();
    }

    /// <summary>
    /// Converts a screen position from an input event into this control's
    /// local coordinates (where (0, 0) is its top-left corner).
    /// </summary>
    private Vector2 ToLocalPoint(Vector2 screenPosition)
    {
        // GetGlobalTransformWithCanvas maps local -> screen; its inverse
        // maps screen -> local.
        return GetGlobalTransformWithCanvas().AffineInverse() * screenPosition;
    }
}
