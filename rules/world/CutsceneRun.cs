using System.Numerics;

namespace Yorehold.Rules;

/// <summary>The curves a cutscene step can move along, by the names the files use.</summary>
public static class Ease
{
    public static float Apply(string curve, float t)
    {
        t = Math.Clamp(t, 0, 1);
        switch (curve)
        {
            case "inQuad":
                return t * t;
            case "outQuad":
                return 1 - (1 - t) * (1 - t);
            case "inOutQuad":
                return t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
            case "outCubic":
                return 1 - MathF.Pow(1 - t, 3);
            case "inOutCubic":
                return t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
            case "outBack":
            {
                const float c1 = 1.70158f, c3 = c1 + 1;
                return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
            }
            case "outElastic":
                return t is 0 or 1 ? t : MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * 2.0943951f) + 1;
            case "outBounce":
                return BounceOut(t);
            default:
                return t;
        }
    }

    private static float BounceOut(float t)
    {
        const float n = 7.5625f, d = 2.75f;
        if (t < 1 / d)
        {
            return n * t * t;
        }
        if (t < 2 / d)
        {
            t -= 1.5f / d;
            return n * t * t + 0.75f;
        }
        if (t < 2.5f / d)
        {
            t -= 2.25f / d;
            return n * t * t + 0.9375f;
        }
        t -= 2.625f / d;
        return n * t * t + 0.984375f;
    }
}

/// <summary>A caption or title on screen now, and how strongly (fading in and out over its time).</summary>
public readonly record struct CutsceneText(string Text, bool Title, float Alpha);

/// <summary>
/// A cutscene playing, as the C++ client plays it: steps in order, "wait": false starts the next
/// step together with it, steps of no length (bars, events) finish at once. It steers a camera
/// given as a position and zoom, and says what to draw: bars, captions, titles and the fade. It
/// draws nothing itself.
/// </summary>
public sealed class CutsceneRun
{
    public const float BarsPerSecond = 2.5f; // the letterbox slides in or out in 0.4 s

    private sealed class Active
    {
        public int Step;
        public double Elapsed;
        public Vector2 FromPosition;
        public float FromZoom;
        public ContentColor FromColor;
    }

    private readonly Cutscene _cutscene;
    private readonly List<Active> _active = new();
    private int _next;
    private bool _barsOn;

    public CutsceneRun(Cutscene cutscene, Vector2 cameraPosition, float cameraZoom)
    {
        _cutscene = cutscene;
        CameraPosition = cameraPosition;
        CameraZoom = cameraZoom;
        Running = cutscene.Steps.Count > 0;
    }

    public bool Running { get; private set; }
    public Vector2 CameraPosition { get; private set; }
    public float CameraZoom { get; private set; }
    /// <summary>True once a camera step has moved it; the screen leaves its camera alone until then.</summary>
    public bool CameraSteered { get; private set; }
    /// <summary>The colour laid over everything, alpha 0 for none.</summary>
    public ContentColor Fade { get; private set; } = new(0, 0, 0, 0);
    /// <summary>0 to 1: how far the letterbox bars are in.</summary>
    public float Bars { get; private set; }
    /// <summary>"event" steps reached since the last call to TakeEvents.</summary>
    private readonly List<string> _events = new();

    public List<string> TakeEvents()
    {
        List<string> taken = new(_events);
        _events.Clear();
        return taken;
    }

    /// <summary>The captions and titles showing now.</summary>
    public List<CutsceneText> Texts()
    {
        var texts = new List<CutsceneText>();
        foreach (Active a in _active)
        {
            CutsceneStep step = _cutscene.Steps[a.Step];
            if (step.Kind is not (CutsceneStepKind.Caption or CutsceneStepKind.Title))
            {
                continue;
            }
            double edge = Math.Min(0.6, step.Seconds / 3);
            double alpha = edge <= 0 ? 1 : Math.Clamp(Math.Min(a.Elapsed, step.Seconds - a.Elapsed) / edge, 0, 1);
            texts.Add(new CutsceneText(step.Text, step.Kind == CutsceneStepKind.Title, (float)alpha));
        }
        return texts;
    }

    public void Update(double deltaSeconds)
    {
        float target = _barsOn ? 1 : 0;
        float most = BarsPerSecond * (float)deltaSeconds;
        Bars += Math.Clamp(target - Bars, -most, most);
        if (!Running)
        {
            return;
        }
        // Zero-length steps finish at once, so steps keep starting until one takes time.
        double dt = Math.Max(0, deltaSeconds);
        for (int guard = 0; guard <= _cutscene.Steps.Count; guard++)
        {
            while (_next < _cutscene.Steps.Count && (_active.Count == 0 || !_cutscene.Steps[_active[^1].Step].Wait))
            {
                Begin(_next++);
            }
            int before = _active.Count;
            double step = dt;
            _active.RemoveAll(a => Advance(a, step));
            dt = 0;
            if (_active.Count == before)
            {
                break;
            }
        }
        if (_active.Count == 0 && _next >= _cutscene.Steps.Count)
        {
            Running = false;
        }
    }

    /// <summary>Jumps to the end: the camera lands on its last target and every event still comes out.</summary>
    public void Skip()
    {
        if (!Running)
        {
            return;
        }
        foreach (Active a in _active)
        {
            Advance(a, 1e9);
        }
        _active.Clear();
        for (; _next < _cutscene.Steps.Count; _next++)
        {
            Begin(_next);
            Advance(_active[^1], 1e9);
            _active.Clear();
        }
        Bars = _barsOn ? 1 : 0;
        Running = false;
    }

    private void Begin(int index)
    {
        CutsceneStep step = _cutscene.Steps[index];
        if (step.Kind == CutsceneStepKind.Bars)
        {
            _barsOn = step.On;
        }
        if (step.Kind == CutsceneStepKind.Event)
        {
            _events.Add(step.Text);
        }
        _active.Add(new Active { Step = index, FromPosition = CameraPosition, FromZoom = CameraZoom, FromColor = Fade });
    }

    private bool Advance(Active active, double deltaSeconds)
    {
        CutsceneStep step = _cutscene.Steps[active.Step];
        active.Elapsed += deltaSeconds;
        float t = step.Seconds > 0 ? (float)Math.Min(1.0, active.Elapsed / step.Seconds) : 1;
        float e = Ease.Apply(step.Ease, t);
        if (step.Kind == CutsceneStepKind.Camera)
        {
            var to = new Vector2((float)step.X, (float)step.Y);
            CameraPosition = active.FromPosition + (to - active.FromPosition) * e;
            CameraZoom = step.Zoom > 0 ? active.FromZoom + ((float)step.Zoom - active.FromZoom) * e : active.FromZoom;
            CameraSteered = true;
        }
        else if (step.Kind == CutsceneStepKind.Fade)
        {
            ContentColor a = active.FromColor, b = step.Color;
            byte Mix(byte x, byte y) => (byte)Math.Clamp(MathF.Round(x + (y - x) * t), 0, 255);
            Fade = new ContentColor(Mix(a.R, b.R), Mix(a.G, b.G), Mix(a.B, b.B), Mix(a.A, b.A));
        }
        return t >= 1;
    }
}
