using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The screens' motion from ui/motion.json (R20): panels slide up into place as they open, HP bars
/// run down to a new value. Nothing fades. With Less motion set, every move is a cut.
/// </summary>
public static class Motion
{
    public static UiMotion Look { get; private set; } = new();

    /// <summary>Reads the motion file (the game's, a skin's laid over it) at start.</summary>
    public static void Load(ContentFiles files)
    {
        Look = new UiMotion();
        if (!files.Exists(UiMotion.File))
        {
            return;
        }
        try
        {
            Look = UiMotion.Read(ContentNode.Read(files, UiMotion.File));
        }
        catch (ContentException error)
        {
            GD.PushWarning($"{UiMotion.File} can't be read, so the screens move as they come: {error.Message}");
        }
    }

    /// <summary>How long a move of this kind takes now: none with Less motion.</summary>
    public static double Seconds(double seconds) => App.Settings.LessMotion ? 0 : seconds;

    /// <summary>
    /// Makes a panel slide up into place each time it is shown. Its offsets are left as its layout
    /// set them; the slide only moves where it is drawn for a moment.
    /// </summary>
    public static void SlideInWhenShown(Control panel)
    {
        panel.VisibilityChanged += () =>
        {
            if (!panel.Visible || Seconds(Look.PanelSeconds) <= 0 || !panel.IsInsideTree())
            {
                return;
            }
            // drawn lower and brought up: the layout keeps its own offsets throughout
            Tween slide = panel.CreateTween();
            slide.TweenMethod(Callable.From<float>(down => SetDrop(panel, down)), (float)Look.PanelDistance, 0f, Seconds(Look.PanelSeconds))
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        };
    }

    // A panel drawn this far below its place, without touching its layout.
    private static void SetDrop(Control panel, float down)
    {
        if (GodotObject.IsInstanceValid(panel))
        {
            RenderingServer.CanvasItemSetTransform(panel.GetCanvasItem(), new Transform2D(0, panel.Position + new Vector2(0, down)));
        }
    }

    /// <summary>A bar's value moved toward where it should be, at a pace that crosses the whole bar in BarSeconds.</summary>
    public static double Toward(double shown, double target, double max, double delta)
    {
        double seconds = Seconds(Look.BarSeconds);
        if (seconds <= 0 || max <= 0)
        {
            return target;
        }
        double step = max / seconds * delta;
        return shown < target ? System.Math.Min(target, shown + step) : System.Math.Max(target, shown - step);
    }
}
