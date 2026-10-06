namespace Yorehold.Rules;

public enum CutsceneStepKind
{
    Camera,
    Caption,
    Title,
    Fade,
    Bars,
    Event,
    Wait,
}

public class CutsceneStep
{
    public CutsceneStepKind Kind { get; init; }
    public double Seconds { get; init; }
    /// <summary>False: the next step starts together with this one.</summary>
    public bool Wait { get; init; } = true;
    public string Ease { get; init; } = "inOutCubic";
    /// <summary>Camera: where it looks, in world units, and the zoom (0 keeps it).</summary>
    public double X { get; init; }
    public double Y { get; init; }
    public double Zoom { get; init; }
    /// <summary>A caption's or title's line, or an event's name.</summary>
    public string Text { get; init; } = "";
    public ContentColor Color { get; init; }
    public bool On { get; init; }
}

/// <summary>A cutscene file: camera moves, captions, fades and bars played in order.</summary>
public class Cutscene
{
    private const double LongestStep = 600;

    public List<CutsceneStep> Steps { get; init; } = new();

    public static Cutscene Read(ContentNode node)
    {
        node.RequireObject("a cutscene is a JSON object");
        var steps = new List<CutsceneStep>();
        foreach (ContentNode s in node.At("steps").Items())
        {
            s.RequireObject("each step is an object");
            CutsceneStepKind kind;
            double seconds = s.Number("seconds", 0, 0, LongestStep);
            double x = 0, y = 0, zoom = 0;
            string text = "";
            var color = new ContentColor(0, 0, 0);
            bool on = false;
            if (s.Get("camera") is ContentNode camera)
            {
                kind = CutsceneStepKind.Camera;
                if (!camera.IsArray || camera.Count != 2)
                {
                    throw camera.Fail("needs [x, y]");
                }
                double[] at = camera.Items().Select(part => part.AsNumber()).ToArray();
                (x, y) = (at[0], at[1]);
                zoom = s.Number("zoom", 0, 0, 64);
            }
            else if (s.Has("caption") || s.Has("title"))
            {
                kind = s.Has("title") ? CutsceneStepKind.Title : CutsceneStepKind.Caption;
                text = s.At(s.Has("title") ? "title" : "caption").AsText(2000);
                if (!s.Has("seconds"))
                {
                    seconds = 3;
                }
            }
            else if (s.Get("fade") is ContentNode fade)
            {
                kind = CutsceneStepKind.Fade;
                color = ContentParts.ColorFrom(fade);
            }
            else if (s.Get("bars") is ContentNode bars)
            {
                kind = CutsceneStepKind.Bars;
                on = bars.AsBool();
            }
            else if (s.Get("event") is ContentNode name)
            {
                kind = CutsceneStepKind.Event;
                text = name.AsText();
                if (text.Length == 0)
                {
                    throw name.Fail("needs a name");
                }
            }
            else if (s.Get("pause") is ContentNode pause)
            {
                kind = CutsceneStepKind.Wait;
                seconds = pause.AsNumber(0, LongestStep);
            }
            else
            {
                throw s.Fail("unknown cutscene step");
            }
            steps.Add(new CutsceneStep
            {
                Kind = kind,
                Seconds = seconds,
                Wait = s.Bool("wait", true),
                Ease = s.Text("ease", "inOutCubic"), // the C++ client's default curve
                X = x,
                Y = y,
                Zoom = zoom,
                Text = text,
                Color = color,
                On = on,
            });
        }
        return new Cutscene { Steps = steps };
    }
}
