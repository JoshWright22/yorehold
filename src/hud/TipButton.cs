using System;
using Godot;

namespace Yorehold;

/// <summary>
/// A button with a tooltip the hud draws itself. The pointer over it asks for the tip; so does
/// holding it down, which is how a touch screen gets one, and a hold doesn't count as a click.
/// </summary>
public partial class TipButton : Button
{
    [Export] public double HoldSeconds { get; set; } = 0.45;

    public string TipTitle { get; set; } = "";
    public string TipMeta { get; set; } = "";
    public string TipBody { get; set; } = "";
    public string TipWarning { get; set; } = "";

    /// <summary>Pressed and let go before it turned into a hold.</summary>
    public event Action<TipButton>? Clicked;
    /// <summary>The tip should show. True when it was asked for by holding, so it stays up after the finger lifts.</summary>
    public event Action<TipButton, bool>? TipWanted;
    public event Action<TipButton>? TipDone;

    private bool _down;
    private double _heldFor;
    private bool _heldTip;

    public override void _Ready()
    {
        MouseEntered += () => TipWanted?.Invoke(this, false);
        MouseExited += () =>
        {
            if (!_heldTip)
            {
                TipDone?.Invoke(this);
            }
        };
        ButtonDown += () =>
        {
            _down = true;
            _heldFor = 0;
            _heldTip = false;
        };
        ButtonUp += () => _down = false;
        Pressed += () =>
        {
            if (!_heldTip)
            {
                Clicked?.Invoke(this);
            }
        };
    }

    /// <summary>A drag began from it: the press is not going to turn into a held tip.</summary>
    protected void CancelHold()
    {
        _down = false;
    }

    public override void _Process(double delta)
    {
        if (!_down || _heldTip)
        {
            return;
        }
        _heldFor += delta;
        if (_heldFor >= HoldSeconds)
        {
            _heldTip = true;
            TipWanted?.Invoke(this, true);
        }
    }
}
