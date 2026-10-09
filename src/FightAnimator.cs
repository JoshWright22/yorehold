using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Plays an action's animation set (ui/animations) over the map once its dice have landed (R19):
/// the doer lunging, a projectile, an arc or a ring, a flash on whoever it reached, drawn as plain
/// shapes in the screens' colours; lunges, recoils and dodges nudge the tokens themselves. The
/// play screen lets the held numbers out at the set's impact moment.
/// </summary>
public partial class FightAnimator : Node2D
{
    private static readonly Dictionary<string, UiAnimation> Sets = new(StringComparer.Ordinal);

    /// <summary>Reads the animation sets (the game's, a skin's laid over them) at start.</summary>
    public static void Load(ContentFiles files)
    {
        Sets.Clear();
        foreach (string path in files.List(UiAnimation.Folder))
        {
            try
            {
                UiAnimation set = UiAnimation.Read(ContentNode.Read(files, path));
                Sets[set.Id] = set;
            }
            catch (ContentException error)
            {
                GD.PushWarning($"An animation set can't be read and is left out: {error.Message}");
            }
        }
    }

    /// <summary>The set by id; null when there is none of that name.</summary>
    public static UiAnimation? Set(string id) => Sets.GetValueOrDefault(id);

    private UiAnimation? _set;
    private List<UiAnimation.Step> _steps = new();
    private Vector2 _from;
    private List<Vector2> _to = new();
    private int _doer;
    private List<int> _targets = new();
    private TokensView? _tokens;
    private PlayCamera? _camera;
    private double _time;

    /// <summary>A set is playing.</summary>
    public bool Playing => _set != null;
    /// <summary>The blow has landed: the numbers may come out.</summary>
    public bool Struck => _set == null || _time >= _set.Impact;

    /// <summary>Plays a set from the doer's token to the targets', the hit, miss or critical version.</summary>
    public void Play(UiAnimation set, World world, int doer, List<int> targets, bool hit, bool critical, TokensView tokens, PlayCamera camera)
    {
        _set = set;
        _steps = critical ? set.Critical : hit ? set.Hit : set.Miss;
        _doer = doer;
        _targets = targets.Where(t => t >= 0 && t < world.Tokens.Tokens.Count).Distinct().ToList();
        _from = world.Tokens.Tokens[doer].Position.ToGodot();
        _to = _targets.Select(t => world.Tokens.Tokens[t].Position.ToGodot()).ToList();
        if (_to.Count == 0)
        {
            _to.Add(_from + new Vector2(GameMap.CellSize, 0));
        }
        _tokens = tokens;
        _camera = camera;
        _time = 0;
    }

    public override void _Process(double delta)
    {
        if (_set == null)
        {
            return;
        }
        _time += delta;
        Nudge();
        QueueRedraw();
        if (_time >= _set.Seconds)
        {
            _tokens?.ClearNudges();
            if (_camera != null)
            {
                _camera.Offset = Vector2.Zero;
            }
            _set = null;
            QueueRedraw();
        }
    }

    // How far into a step (0 to 1), or null outside it.
    private float? Into(UiAnimation.Step step)
    {
        double u = (_time - step.At) / step.Seconds;
        return u is < 0 or > 1 ? null : (float)u;
    }

    // The tokens' own moves: the doer's lunge, the targets' recoil or sidestep, the screen's shake.
    private void Nudge()
    {
        if (_tokens == null)
        {
            return;
        }
        var offsets = new Dictionary<int, Vector2>();
        Vector2 aim = (_to[0] - _from).Normalized();
        foreach (UiAnimation.Step step in _steps)
        {
            if (Into(step) is not float u)
            {
                continue;
            }
            // out and back: a hump that peaks halfway
            float hump = Mathf.Sin(u * Mathf.Pi);
            float cells = (float)step.Distance * GameMap.CellSize * hump;
            switch (step.Do)
            {
                case "lunge":
                    offsets[_doer] = offsets.GetValueOrDefault(_doer) + aim * cells;
                    break;
                case "recoil":
                    foreach (int t in _targets)
                    {
                        Vector2 away = (_tokens.PositionOf(t) - _from).Normalized();
                        offsets[t] = offsets.GetValueOrDefault(t) + away * cells;
                    }
                    break;
                case "dodge":
                    foreach (int t in _targets)
                    {
                        Vector2 side = (_tokens.PositionOf(t) - _from).Normalized().Orthogonal();
                        offsets[t] = offsets.GetValueOrDefault(t) + side * cells;
                    }
                    break;
                case "shake" when _camera != null:
                    float strength = (float)step.Size * GameMap.CellSize * (1 - u);
                    _camera.Offset = new Vector2(Mathf.Sin((float)_time * 90) * strength, Mathf.Cos((float)_time * 77) * strength);
                    break;
            }
        }
        _tokens.Nudge(offsets);
    }

    public override void _Draw()
    {
        if (_set == null)
        {
            return;
        }
        float cell = GameMap.CellSize;
        foreach (UiAnimation.Step step in _steps)
        {
            if (Into(step) is not float u)
            {
                continue;
            }
            Color color = Palette.Named(step.Color);
            Color fading = new(color, 1 - u);
            foreach (Vector2 to in step.Do is "projectile" or "line" ? _to : _to.Take(_to.Count))
            {
                Vector2 at = step.On == "doer" ? _from : to;
                switch (step.Do)
                {
                    case "projectile":
                    {
                        // along the line to the target (past it, when Distance > 1, for a miss), lifted into an arc
                        float reach = (float)Math.Max(step.Distance, 1);
                        Vector2 point = _from.Lerp(_from + (to - _from) * reach, u);
                        point += Vector2.Up * (float)step.Height * cell * Mathf.Sin(u * Mathf.Pi);
                        DrawCircle(point, Math.Max(2, (float)step.Size * cell), color);
                        Vector2 tail = _from.Lerp(_from + (to - _from) * reach, Math.Max(0, u - 0.08f));
                        DrawLine(tail, point, color, Math.Max(1, (float)step.Size * cell * 0.6f), true);
                        break;
                    }
                    case "line":
                        DrawLine(_from, _from.Lerp(to, Math.Min(1, u * 1.5f)), fading, Math.Max(1, (float)step.Size * 6), true);
                        break;
                    case "arc":
                    {
                        float radius = (float)step.Size * cell * 0.5f;
                        float start = (float)(step.Height != 0 ? 0.6 : -0.6) + Mathf.Pi * 1.2f * u;
                        DrawArc(at, radius, start - 1.2f, start, 16, color, 3, true);
                        break;
                    }
                    case "ring":
                        DrawArc(at, Math.Max(2, (float)step.Size * cell * 0.5f * u), 0, Mathf.Tau, 48, fading, 3, true);
                        break;
                    case "flash":
                        DrawCircle(at, cell * 0.45f, new Color(color, 0.55f * (1 - u)));
                        break;
                    case "glow":
                        DrawCircle(at, (float)step.Size * cell * 0.5f, new Color(color, 0.35f * Mathf.Sin(u * Mathf.Pi)));
                        break;
                }
                if (step.On == "doer" && step.Do is not "projectile" and not "line")
                {
                    break; // drawn once, on the doer
                }
            }
        }
    }
}
