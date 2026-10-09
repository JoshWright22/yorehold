using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The picture behind the game-facing screens: players' and creators' art (ui/banners/ in the
/// content and the art packs), covering the window with its middle kept, a new one every few
/// seconds. With none, the window is a plain palette fill. The game ships no banner of its own.
/// </summary>
public partial class BannerView : Control
{
    public const string Folder = "ui/banners";

    [Export] public Color Fill { get; set; } = Palette.Night;

    private readonly List<string> _pictures = new();

    /// <summary>The players' art packs bring pictures to show.</summary>
    public bool HasPictures => _pictures.Count > 0;
    /// <summary>How long each picture stays.</summary>
    public double Seconds => _seconds;
    private ContentFiles? _files;
    private int _shown;
    private double _clock;
    private double _seconds = 8;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        Read();
    }

    /// <summary>Looks for pictures again: the art packs may have changed.</summary>
    public void Read()
    {
        _files = App.Content();
        _pictures.Clear();
        _pictures.AddRange(_files.Pictures(Folder));
        _seconds = ScreenSizes.Load(_files).BannerSeconds;
        _shown = _pictures.Count > 0 ? (int)(Time.GetUnixTimeFromSystem() / _seconds) % _pictures.Count : 0;
        _clock = 0;
        QueueRedraw();
    }

    /// <summary>Shows one picture and stays on it (an adventure's cover); null goes back to the players' art.</summary>
    public void Pin(ContentFiles? files, string picture)
    {
        if (picture.Length == 0 || files == null)
        {
            Read();
            return;
        }
        _files = files;
        _pictures.Clear();
        _pictures.Add(picture);
        _shown = 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_pictures.Count < 2 || !IsVisibleInTree())
        {
            return;
        }
        _clock += delta;
        if (_clock >= _seconds)
        {
            _clock = 0;
            _shown = (_shown + 1) % _pictures.Count;
            QueueRedraw();
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Fill);
        if (_pictures.Count == 0 || PlayerArt.Texture(_files, _pictures[_shown % _pictures.Count]) is not Texture2D picture)
        {
            return;
        }
        // cover the window: the picture's middle is kept and what sticks out is cut off
        Vector2 whole = picture.GetSize();
        float scale = Mathf.Max(Size.X / whole.X, Size.Y / whole.Y);
        Vector2 part = Size / scale;
        DrawTextureRectRegion(picture, new Rect2(Vector2.Zero, Size), new Rect2((whole - part) / 2, part));
    }
}
