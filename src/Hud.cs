using System.Collections.Generic;
using Godot;

namespace Yorehold;

/// <summary>What sits on top of the map: the chapter title, the last few log lines and a banner.</summary>
public partial class Hud : CanvasLayer
{
    [Export] public int LogLines { get; set; } = 6;

    private readonly List<string> _log = new();
    private Label? _title;
    private Label? _logLabel;
    private Label? _banner;
    private double _bannerLeft;
    private double _bannerSeconds = 1;

    public override void _Ready()
    {
        _title = GetNode<Label>("Title");
        _logLabel = GetNode<Label>("LogPanel/Log");
        _banner = GetNode<Label>("Banner");
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
        _log.Clear();
        ShowLog();
    }

    public void AddLog(string line)
    {
        _log.Add(line);
        if (_log.Count > 200)
        {
            _log.RemoveRange(0, 50);
        }
        ShowLog();
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

    private void ShowLog()
    {
        if (_logLabel != null)
        {
            int from = System.Math.Max(0, _log.Count - LogLines);
            _logLabel.Text = string.Join("\n", _log.GetRange(from, _log.Count - from));
        }
    }
}
