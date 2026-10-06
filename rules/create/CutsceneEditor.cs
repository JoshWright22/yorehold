using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Cutscene mode of Create: one cutscene file and the commands that change it, plus its timing and
/// the frame at any moment worked out the way CutsceneRun plays it, so the preview and the tests
/// share it. It draws nothing. Fields it has no tool for are written back as they were. Every
/// command goes on the history it was given, which the whole open package shares.
/// </summary>
public sealed class CutsceneEditor
{
    public const int MaxLine = 2000;
    public const int MaxName = 64;
    public const double MaxSeconds = 600;

    public enum Kind
    {
        Pause,
        Camera,
        Caption,
        Title,
        Fade,
        Bars,
        Event,
    }

    public sealed record Step
    {
        public Kind Kind { get; init; } = Kind.Pause;
        public double Seconds { get; init; } = 1;
        /// <summary>False: the next step starts with this one.</summary>
        public bool Wait { get; init; } = true;
        /// <summary>Camera, in world units.</summary>
        public double X { get; init; }
        public double Y { get; init; }
        /// <summary>Camera; 0 keeps the zoom it had.</summary>
        public double Zoom { get; init; }
        /// <summary>A name from Eases; empty = the game's default.</summary>
        public string Ease { get; init; } = "";
        /// <summary>Caption and title line, event name.</summary>
        public string Text { get; init; } = "";
        /// <summary>Fade target; alpha 0 fades back in.</summary>
        public ContentColor Color { get; init; } = new(0, 0, 0, 255);
        /// <summary>Bars.</summary>
        public bool On { get; init; } = true;
        /// <summary>Fields the editor has no tool for, as a JSON object; empty = none.</summary>
        public string Extra { get; init; } = "";
    }

    /// <summary>When a step runs, in seconds from the start.</summary>
    public readonly record struct Span(double Start, double End);

    /// <summary>What the screen shows at one moment.</summary>
    public sealed class Frame
    {
        public Vector2 Camera;
        public float Zoom = 1;
        public ContentColor Fade = new(0, 0, 0, 0);
        /// <summary>0 to 1, how far in the letterbox bars are.</summary>
        public float Bars;
        public List<CutsceneText> Lines = new();
    }

    /// <summary>Error false: worth a look, but the file still saves and plays.</summary>
    public sealed record Problem(string Text, bool Error = true);

    public static IReadOnlyList<string> Eases { get; } = new[] { "linear", "inQuad", "outQuad", "inOutQuad", "outCubic", "inOutCubic", "outBack", "outElastic", "outBounce" };

    /// <summary>The events the game does something with (the play screen).</summary>
    public static IReadOnlyList<string> Events { get; } = new[] { "finished" };

    // What an undo step puts back.
    private sealed class State
    {
        public List<Step> Steps = new();
        public string Extra = "";

        public State Copy() => new() { Steps = Steps.ToList(), Extra = Extra };
    }

    private readonly History _history;
    private bool _loaded;
    private State _state = new();
    private (double X, double Y, double W, double H)? _bounds;

    public CutsceneEditor(History history)
    {
        _history = history;
    }

    public bool Loaded => _loaded;
    public IReadOnlyList<Step> Steps => _state.Steps;

    /// <summary>The map's size in world units, for the camera warnings and a new camera's place; null = not checked.</summary>
    public void SetBounds((double X, double Y, double W, double H)? bounds) => _bounds = bounds;

    public static string KindName(Kind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>Only a file the game would load: what it refuses is refused here too, with its reason.</summary>
    public bool Load(string text, out string error)
    {
        error = "";
        try
        {
            // the game's own reader first, so nothing it refuses opens here
            ContentNode j = ContentNode.Parse("cutscene", text);
            Rules.Cutscene.Read(j);
            var state = new State { Extra = CreateJson.ExtraOf(j, new[] { "steps" }) };
            foreach (ContentNode s in j.At("steps").Items())
            {
                bool wait = s.Bool("wait", true);
                string ease = s.Text("ease", "");
                double seconds = s.Number("seconds", 0);
                Step step;
                string[] known;
                if (s.Get("camera") is ContentNode camera)
                {
                    double[] at = camera.Items().Select(p => p.AsNumber()).ToArray();
                    step = new Step { Kind = Kind.Camera, X = at[0], Y = at[1], Zoom = s.Number("zoom", 0) };
                    known = new[] { "camera", "zoom", "seconds", "ease", "wait" };
                }
                else if (s.Has("title") || s.Has("caption"))
                {
                    bool title = s.Has("title");
                    step = new Step { Kind = title ? Kind.Title : Kind.Caption, Text = s.At(title ? "title" : "caption").AsText() };
                    seconds = s.Number("seconds", 3);
                    known = new[] { title ? "title" : "caption", "seconds", "ease", "wait" };
                }
                else if (s.Get("fade") is ContentNode fade)
                {
                    step = new Step { Kind = Kind.Fade, Color = ContentParts.ColorFrom(fade) };
                    known = new[] { "fade", "seconds", "ease", "wait" };
                }
                else if (s.Get("bars") is ContentNode bars)
                {
                    step = new Step { Kind = Kind.Bars, On = bars.AsBool() };
                    known = new[] { "bars", "seconds", "ease", "wait" };
                }
                else if (s.Get("event") is ContentNode name)
                {
                    step = new Step { Kind = Kind.Event, Text = name.AsText() };
                    known = new[] { "event", "seconds", "ease", "wait" };
                }
                else
                {
                    // the game reads a pause's time from "pause" only
                    step = new Step { Kind = Kind.Pause };
                    seconds = s.At("pause").AsNumber();
                    known = new[] { "pause", "seconds", "ease", "wait" };
                }
                state.Steps.Add(step with { Seconds = seconds, Wait = wait, Ease = ease, Extra = CreateJson.ExtraOf(s, known) });
            }
            _state = state;
            _loaded = true;
            return true;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return false;
        }
    }

    /// <summary>A new cutscene: bars in, a caption, bars out.</summary>
    public void Create()
    {
        _state = new State();
        _state.Steps.Add(new Step { Kind = Kind.Bars, Seconds = 0, On = true });
        _state.Steps.Add(new Step { Kind = Kind.Caption, Seconds = 3 });
        _state.Steps.Add(new Step { Kind = Kind.Bars, Seconds = 0, On = false });
        _loaded = true;
    }

    public string ToJson()
    {
        if (!_loaded)
        {
            return "{}";
        }
        var j = new JsonObject();
        CreateJson.AddExtra(j, _state.Extra);
        var steps = new JsonArray();
        foreach (Step step in _state.Steps)
        {
            steps.Add(StepJson(step));
        }
        j["steps"] = steps;
        return CreateJson.Write(j);
    }

    /// <summary>As the game reads it; null (and why) while something in it would be refused.</summary>
    public Cutscene? Cutscene(out string error)
    {
        error = "";
        if (!_loaded)
        {
            error = "nothing is open";
            return null;
        }
        try
        {
            return Rules.Cutscene.Read(ContentNode.Parse("cutscene", ToJson()));
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return null;
        }
    }

    // ---------------------------------------------------------------- timing

    /// <summary>When each step starts and ends, the way CutsceneRun runs them.</summary>
    public List<Span> Spans()
    {
        // the next step starts once nothing is running, or straight away while the last one started doesn't make it wait
        List<Step> steps = _state.Steps;
        var spans = new Span[steps.Count];
        var running = new List<int>();
        int next = 0;
        double now = 0;
        while (true)
        {
            while (next < steps.Count && (running.Count == 0 || !steps[running[^1]].Wait))
            {
                spans[next] = new Span(now, now + Math.Max(0, steps[next].Seconds));
                running.Add(next++);
            }
            if (running.Count == 0)
            {
                break;
            }
            now = running.Min(r => spans[r].End);
            double at = now;
            running.RemoveAll(r => spans[r].End <= at);
        }
        return spans.ToList();
    }

    public double Length() => Spans().Select(s => s.End).DefaultIfEmpty(0).Max();

    /// <summary>camera and zoom are where the view is when it starts (the game starts from the party).</summary>
    public Frame FrameAt(double seconds, Vector2 camera, float zoom)
    {
        List<Step> steps = _state.Steps;
        List<Span> times = Spans();
        float Progress(int i, double at) => times[i].End > times[i].Start ? (float)Math.Clamp((at - times[i].Start) / (times[i].End - times[i].Start), 0, 1) : 1;

        // each camera move and fade starts from wherever the earlier ones had got to when it began
        var fromCamera = new Vector2[steps.Count];
        var fromZoom = new float[steps.Count];
        var fromFade = new ContentColor[steps.Count];
        (Vector2, float) ViewAt(double at, int before)
        {
            Vector2 position = camera;
            float z = zoom;
            for (int i = 0; i < before; i++)
            {
                if (steps[i].Kind == Kind.Camera && times[i].Start <= at)
                {
                    float e = Rules.Ease.Apply(steps[i].Ease.Length == 0 ? "inOutCubic" : steps[i].Ease, Progress(i, at));
                    position = fromCamera[i] + (new Vector2((float)steps[i].X, (float)steps[i].Y) - fromCamera[i]) * e;
                    z = steps[i].Zoom > 0 ? fromZoom[i] + ((float)steps[i].Zoom - fromZoom[i]) * e : fromZoom[i];
                }
            }
            return (position, z);
        }
        ContentColor FadeAt(double at, int before)
        {
            var color = new ContentColor(0, 0, 0, 0);
            for (int i = 0; i < before; i++)
            {
                if (steps[i].Kind == Kind.Fade && times[i].Start <= at)
                {
                    color = Mix(fromFade[i], steps[i].Color, Progress(i, at));
                }
            }
            return color;
        }
        for (int i = 0; i < steps.Count; i++)
        {
            (fromCamera[i], fromZoom[i]) = ViewAt(times[i].Start, i);
            fromFade[i] = FadeAt(times[i].Start, i);
        }

        var frame = new Frame();
        (frame.Camera, frame.Zoom) = ViewAt(seconds, steps.Count);
        frame.Fade = FadeAt(seconds, steps.Count);

        // the bars slide toward the last setting at a fixed speed
        double last = 0;
        bool on = false;
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].Kind == Kind.Bars && times[i].Start <= seconds)
            {
                float move = CutsceneRun.BarsPerSecond * (float)(times[i].Start - last);
                frame.Bars = Math.Clamp(frame.Bars + (on ? move : -move), 0, 1);
                last = times[i].Start;
                on = steps[i].On;
            }
        }
        float rest = CutsceneRun.BarsPerSecond * (float)(seconds - last);
        frame.Bars = Math.Clamp(frame.Bars + (on ? rest : -rest), 0, 1);

        // captions and titles fade in and out over a third of their time, 0.6 s at most each way
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].Kind is not (Kind.Caption or Kind.Title) || seconds < times[i].Start || seconds >= times[i].End)
            {
                continue;
            }
            double elapsed = seconds - times[i].Start, total = steps[i].Seconds;
            double edge = Math.Min(0.6, total / 3);
            double alpha = edge <= 0 ? 1 : Math.Clamp(Math.Min(elapsed, total - elapsed) / edge, 0, 1);
            frame.Lines.Add(new CutsceneText(steps[i].Text, steps[i].Kind == Kind.Title, (float)alpha));
        }
        return frame;
    }

    // ---------------------------------------------------------------- commands

    /// <summary>A new step after after (or at the end) with its kind's defaults.</summary>
    public int? AddStep(Kind kind, int? after = null)
    {
        if (!_loaded || _state.Steps.Count >= 1000 || (after is int a && (a < 0 || a >= _state.Steps.Count)))
        {
            return null;
        }
        int at = after is int b ? b + 1 : _state.Steps.Count;
        Step step = new() { Kind = kind };
        switch (kind)
        {
            case Kind.Pause:
            case Kind.Fade:
                step = step with { Seconds = 1 };
                break;
            case Kind.Camera:
            {
                // from where the camera was last sent, or the middle of the map
                step = step with { Seconds = 2, X = _bounds is { } m ? m.X + m.W / 2 : 0, Y = _bounds is { } n ? n.Y + n.H / 2 : 0 };
                for (int i = at - 1; i >= 0; i--)
                {
                    if (_state.Steps[i].Kind == Kind.Camera)
                    {
                        step = step with { X = _state.Steps[i].X, Y = _state.Steps[i].Y };
                        break;
                    }
                }
                break;
            }
            case Kind.Caption:
            case Kind.Title:
                step = step with { Seconds = 3 };
                break;
            case Kind.Bars:
            {
                // the other way from how the bars stand by then
                step = step with { Seconds = 0 };
                for (int i = at - 1; i >= 0; i--)
                {
                    if (_state.Steps[i].Kind == Kind.Bars)
                    {
                        step = step with { On = !_state.Steps[i].On };
                        break;
                    }
                }
                break;
            }
            case Kind.Event:
                step = step with { Seconds = 0, Text = Events[0] };
                break;
        }
        Edit($"Add {KindName(kind)} step", () => _state.Steps.Insert(at, step));
        return at;
    }

    public bool RemoveStep(int step)
    {
        if (!Has(step))
        {
            return false;
        }
        Edit($"Remove {KindName(_state.Steps[step].Kind)} step", () => _state.Steps.RemoveAt(step));
        return true;
    }

    /// <summary>One place up (-1) or down (1).</summary>
    public bool MoveStep(int step, int by)
    {
        int to = step + by;
        if (!Has(step) || (by != -1 && by != 1) || !Has(to))
        {
            return false;
        }
        Edit("Move step", () => (_state.Steps[step], _state.Steps[to]) = (_state.Steps[to], _state.Steps[step]));
        return true;
    }

    public int? CopyStep(int step)
    {
        if (!Has(step) || _state.Steps.Count >= 1000)
        {
            return null;
        }
        Edit("Copy step", () => _state.Steps.Insert(step + 1, _state.Steps[step]));
        return step + 1;
    }

    /// <summary>
    /// Any change to one step: refused (false) if the game wouldn't read it. Typing in a box is one
    /// undo step until EndTyping; field keeps typing in two boxes of one step apart on the history.
    /// </summary>
    public bool SetStep(int step, Step changed, string field = "")
    {
        if (!Has(step))
        {
            return false;
        }
        if (changed.Ease.Length > 0 && changed.Ease != _state.Steps[step].Ease && !Eases.Contains(changed.Ease))
        {
            return false;
        }
        if (changed.Kind is Kind.Caption or Kind.Title ? changed.Text.Length > MaxLine : changed.Text.Length > MaxName)
        {
            return false;
        }
        if (!double.IsFinite(changed.X) || !double.IsFinite(changed.Y))
        {
            return false;
        }
        // the game's reader decides the rest (times, zoom, a blank event)
        string one = CreateJson.Compact(new JsonObject { ["steps"] = new JsonArray(StepJson(changed)) });
        try
        {
            Rules.Cutscene.Read(ContentNode.Parse("step", one));
        }
        catch (ContentException)
        {
            return false;
        }
        if (CreateJson.Compact(StepJson(changed)) == CreateJson.Compact(StepJson(_state.Steps[step])))
        {
            return false;
        }
        Edit($"Change {KindName(changed.Kind)} step", () => _state.Steps[step] = changed, $"cutscene-step-{step}-{field}");
        return true;
    }

    public void EndTyping() => _history.BreakMerge();

    public List<Problem> Problems()
    {
        var found = new List<Problem>();
        if (!_loaded)
        {
            return found;
        }
        if (Cutscene(out string why) == null)
        {
            found.Add(new Problem("the game would refuse it: " + why));
        }
        if (_state.Steps.Count == 0)
        {
            found.Add(new Problem("it has no steps, so it ends as soon as it starts", false));
        }
        for (int i = 0; i < _state.Steps.Count; i++)
        {
            Step step = _state.Steps[i];
            string where = $"step {i + 1}";
            bool line = step.Kind is Kind.Caption or Kind.Title;
            if (line && step.Text.Length == 0)
            {
                found.Add(new Problem($"{where}: the {KindName(step.Kind)} has no line", false));
            }
            if (line && step.Seconds <= 0)
            {
                found.Add(new Problem($"{where}: the {KindName(step.Kind)} shows for no time", false));
            }
            if (step.Ease.Length > 0 && !Eases.Contains(step.Ease))
            {
                found.Add(new Problem($"{where}: the game doesn't know the ease {step.Ease} and moves at an even speed", false));
            }
            if (step.Kind == Kind.Camera && _bounds is { } b && (step.X < b.X || step.Y < b.Y || step.X > b.X + b.W || step.Y > b.Y + b.H))
            {
                found.Add(new Problem($"{where}: the camera aims outside the map, the game stops it at the edge", false));
            }
            if (step.Kind == Kind.Event && !Events.Contains(step.Text))
            {
                found.Add(new Problem($"{where}: the game does nothing with the event {step.Text}", false));
            }
        }
        return found;
    }

    /// <summary>Three decimals at most, and whole numbers without ".0".</summary>
    public static JsonNode Number(double value)
    {
        double rounded = Math.Round(value * 1000) / 1000;
        return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e9 ? JsonValue.Create((long)rounded) : JsonValue.Create(rounded);
    }

    /// <summary>1.5 not 1.500000, and 2 not 2.0.</summary>
    public static string Shown(double value) => (Math.Round(value * 1000) / 1000).ToString(CultureInfo.InvariantCulture);

    private static JsonObject StepJson(Step step)
    {
        var s = new JsonObject();
        switch (step.Kind)
        {
            case Kind.Pause:
                s["pause"] = Number(step.Seconds);
                break;
            case Kind.Camera:
                s["camera"] = new JsonArray(Number(step.X), Number(step.Y));
                if (step.Zoom > 0)
                {
                    s["zoom"] = Number(step.Zoom);
                }
                break;
            case Kind.Caption:
                s["caption"] = step.Text;
                break;
            case Kind.Title:
                s["title"] = step.Text;
                break;
            case Kind.Fade:
                s["fade"] = new JsonArray(step.Color.R, step.Color.G, step.Color.B, step.Color.A);
                break;
            case Kind.Bars:
                s["bars"] = step.On;
                break;
            case Kind.Event:
                s["event"] = step.Text;
                break;
        }
        // a caption without seconds shows for 3, so its time is always written
        if (step.Kind is Kind.Caption or Kind.Title || (step.Kind != Kind.Pause && step.Seconds > 0))
        {
            s["seconds"] = Number(step.Seconds);
        }
        if (step.Ease.Length > 0)
        {
            s["ease"] = step.Ease;
        }
        if (!step.Wait)
        {
            s["wait"] = false;
        }
        CreateJson.AddExtra(s, step.Extra);
        return s;
    }

    private static ContentColor Mix(ContentColor a, ContentColor b, float t)
    {
        byte Channel(byte x, byte y) => (byte)Math.Round(x + (y - x) * t, MidpointRounding.AwayFromZero);
        return new ContentColor(Channel(a.R, b.R), Channel(a.G, b.G), Channel(a.B, b.B), Channel(a.A, b.A));
    }

    private bool Has(int step) => _loaded && step >= 0 && step < _state.Steps.Count;

    private void Edit(string label, Action change, string mergeKey = "")
    {
        State before = _state.Copy();
        change();
        State after = _state.Copy();
        _history.Record(label, () => _state = after.Copy(), () => _state = before.Copy(), mergeKey);
    }
}
