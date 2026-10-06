using System.Numerics;

namespace Yorehold.Rules;

/// <summary>Someone looking. DarkRadius is how far they see cells that aren't lit (darkvision); below 0 = as far as Radius.</summary>
public readonly record struct Vision(Vector2 Position, float Radius, float DarkRadius = -1);

public enum FogState
{
    Unexplored,
    Explored,
    Visible,
}

/// <summary>
/// What each view has seen and sees now, per cell. Team 0 is the whole party; a hero alone is 1 +
/// their index. Floors are kept apart though the game plays on floor 0 for now.
/// </summary>
public sealed class FogOfWar
{
    private sealed class View
    {
        public bool[] Explored = Array.Empty<bool>();
        public bool[] Visible = Array.Empty<bool>();
    }

    private readonly Dictionary<(int Team, int Floor), View> _views = new();

    public FogOfWar(int width, int height, float cellSize)
    {
        if (width <= 0 || height <= 0 || !(cellSize > 0) || !float.IsFinite(cellSize))
        {
            throw new ArgumentException("fog sizes and cell size are above 0");
        }
        Width = width;
        Height = height;
        CellSize = cellSize;
    }

    public int Width { get; }
    public int Height { get; }
    public float CellSize { get; }

    public FogState State(int team, int floor, Cell cell)
    {
        if (!Inside(cell) || !_views.TryGetValue((team, floor), out View? view))
        {
            return FogState.Unexplored;
        }
        int i = cell.Y * Width + cell.X;
        return view.Visible[i] ? FogState.Visible : view.Explored[i] ? FogState.Explored : FogState.Unexplored;
    }

    public void Reveal(int team, int floor, Cell cell)
    {
        if (!Inside(cell))
        {
            return;
        }
        View view = ViewOf(team, floor);
        int i = cell.Y * Width + cell.X;
        view.Visible[i] = true;
        view.Explored[i] = true;
    }

    /// <summary>
    /// What a view sees now. With lit, a cell in line of sight is only seen if it is lit or within
    /// the observer's DarkRadius (lighting that follows the rules); without it, everything in range is.
    /// </summary>
    public void Update(int team, int floor, IReadOnlyList<Vision> observers, IReadOnlyList<Wall> walls, Func<Cell, bool>? lit = null)
    {
        View view = ViewOf(team, floor);
        Array.Clear(view.Visible);
        foreach (Vision v in observers)
        {
            if (!(v.Radius > 0) || !float.IsFinite(v.Radius) || !float.IsFinite(v.Position.X) || !float.IsFinite(v.Position.Y))
            {
                continue;
            }
            int x0 = CellX(v.Position.X - v.Radius), x1 = CellX(v.Position.X + v.Radius);
            int y0 = CellY(v.Position.Y - v.Radius), y1 = CellY(v.Position.Y + v.Radius);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var center = new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);
                    Vector2 delta = center - v.Position;
                    float distance2 = delta.X * delta.X + delta.Y * delta.Y;
                    if (distance2 > v.Radius * v.Radius)
                    {
                        continue;
                    }
                    bool inDarkRange = lit == null || v.DarkRadius < 0 || distance2 <= v.DarkRadius * v.DarkRadius;
                    if ((inDarkRange || lit!(new Cell(x, y))) && Sight.LineOfSight(v.Position, center, walls))
                    {
                        Reveal(team, floor, new Cell(x, y));
                    }
                }
            }
        }
    }

    /// <summary>What a view has explored, a '1' or '0' per cell row by row, for saves.</summary>
    public string ExploredText(int team, int floor)
    {
        if (!_views.TryGetValue((team, floor), out View? view))
        {
            return new string('0', Width * Height);
        }
        return new string(view.Explored.Select(seen => seen ? '1' : '0').ToArray());
    }

    /// <summary>Puts back what ExploredText wrote. Nothing is visible until the next Update.</summary>
    public void SetExplored(int team, int floor, string text)
    {
        if (text.Length != Width * Height || text.Any(c => c != '0' && c != '1'))
        {
            throw new ArgumentException("explored cells don't match the map");
        }
        View view = ViewOf(team, floor);
        Array.Clear(view.Visible);
        for (int i = 0; i < text.Length; i++)
        {
            view.Explored[i] = text[i] == '1';
        }
    }

    /// <summary>Forgets everything a team has seen.</summary>
    public void Reset(int team)
    {
        foreach ((int Team, int Floor) key in _views.Keys.Where(key => key.Team == team).ToList())
        {
            _views.Remove(key);
        }
    }

    private bool Inside(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    private int CellX(float x) => (int)Math.Clamp(MathF.Floor(x / CellSize), 0.0f, Width - 1);

    private int CellY(float y) => (int)Math.Clamp(MathF.Floor(y / CellSize), 0.0f, Height - 1);

    private View ViewOf(int team, int floor)
    {
        if (!_views.TryGetValue((team, floor), out View? view))
        {
            view = new View { Explored = new bool[Width * Height], Visible = new bool[Width * Height] };
            _views[(team, floor)] = view;
        }
        return view;
    }
}
