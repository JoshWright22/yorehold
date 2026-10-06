using Godot;

namespace Yorehold;

/// <summary>
/// What sits on top of the map: the chapter title, a banner across the middle and the panels
/// (PlayHud: party cards, the log and everything a fight adds).
/// </summary>
public partial class Hud : CanvasLayer
{
    private Label? _title;
    private Label? _banner;
    private PlayHud? _panels;
    private double _bannerLeft;
    private double _bannerSeconds = 1;

    /// <summary>The panels; null only if the scene lost them.</summary>
    public PlayHud? Panels => _panels;

    public override void _Ready()
    {
        _title = GetNode<Label>("Title");
        _banner = GetNode<Label>("Banner");
        _panels = GetNode<PlayHud>("Panels");
        _banner.Visible = false;
    }

    public void SetTitle(string text)
    {
        if (_title != null)
        {
            _title.Text = text;
        }
    }

    public void ClearLog()
    {
        _panels?.Log.Clear();
    }

    public void AddLog(string line)
    {
        _panels?.Log.Add(line);
    }

    public void Banner(string text, double seconds)
    {
        if (_banner == null)
        {
            return;
        }
        _banner.Text = text;
        _banner.Visible = true;
        _bannerSeconds = seconds > 0 ? seconds : 2;
        _bannerLeft = _bannerSeconds;
    }

    public override void _Process(double delta)
    {
        if (_banner == null || !_banner.Visible)
        {
            return;
        }
        _bannerLeft -= delta;
        // fades over its last half second
        _banner.Modulate = new Color(1, 1, 1, (float)Mathf.Clamp(_bannerLeft / 0.5, 0, 1));
        _banner.Visible = _bannerLeft > 0;
    }
}
