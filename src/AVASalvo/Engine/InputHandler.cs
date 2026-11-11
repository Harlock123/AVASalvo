using Avalonia.Input;

namespace AVASalvo.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: turn the barrel, more or less power, change weapon, fire.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    /// <summary>
    /// Left and right (A and D) swing the barrel; up and down (W and S) add or take away power, up being more. Tab, Q
    /// and E change weapon; Space or Ctrl fires; Shift held makes the adjustments fine. On a gamepad: the stick or
    /// D-pad, A to fire, the shoulder buttons for the weapon, and X held for fine.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            int turn = (Any(Key.Right, Key.D) || pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0) - (Any(Key.Left, Key.A) || pad.DPadLeft || pad.LeftStickX < -0.4f ? 1 : 0);
            int charge = (Any(Key.Up, Key.W) || PadUp ? 1 : 0) - (Any(Key.Down, Key.S) || PadDown ? 1 : 0);
            int weapon = Any(Key.Tab, Key.E) || pad.RightBumper ? 1 : Any(Key.Q) || pad.LeftBumper ? -1 : 0;
            bool fire = Any(Key.Space, Key.LeftCtrl, Key.RightCtrl) || pad.ButtonA;
            bool fine = Any(Key.LeftShift, Key.RightShift) || pad.ButtonX;
            return new Controls(turn, charge, fire, weapon, fine);
        }
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
