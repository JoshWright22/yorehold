using System.Numerics;

namespace Yorehold.Rules;

/// <summary>Where someone stands on the map and where they are walking to, in world units.</summary>
public sealed class Token
{
    public string Name { get; set; } = "";
    /// <summary>The centre.</summary>
    public Vector2 Position { get; set; }
    public float Radius { get; set; } = 40;
    public ContentColor Color { get; set; } = new(200, 200, 200);
    public string Image { get; set; } = "";
    /// <summary>The player who plays it.</summary>
    public int Owner { get; set; }
    public bool Selected { get; set; }
    /// <summary>Waypoints left while walking.</summary>
    public List<Vector2> Path { get; } = new();
    /// <summary>Its share of the walking speed (0.5 = half speed).</summary>
    public float Pace { get; set; } = 1.0f;
    public int Floor { get; set; }
}

public sealed class TokenSettings
{
    public float WalkCellsPerSecond { get; set; } = 5.0f;
    /// <summary>Walk around party members instead of through them. Others always block.</summary>
    public bool AvoidAllies { get; set; } = true;
    public float FollowDistanceCells { get; set; } = 1.5f;
    /// <summary>Followers standing idle in a party member's way step aside instead of blocking them.</summary>
    public bool AlliesMakeWay { get; set; } = true;
    public bool InCombat { get; set; }
    /// <summary>In a fight only this token may move.</summary>
    public int? ActiveTurn { get; set; }
}

/// <summary>
/// Moves tokens along their paths, keeps followers behind whoever they follow and has idle
/// followers step out of a walker's way. The C++ client's TokenController without the mouse:
/// clicks and drags are the screen's (P4).
/// </summary>
public sealed class TokenMover
{
    private readonly SortedDictionary<int, int> _links = new();
    private readonly Dictionary<int, Cell> _followerTargets = new();
    private readonly SortedSet<int> _steppingAside = new();

    public TokenSettings Settings { get; } = new();
    public List<Token> Tokens { get; } = new();
    public int LocalPlayer { get; set; }
    public int ViewedFloor { get; set; }

    /// <summary>Links can't make a loop or cross owners. Indexes stay valid while tokens keep their order.</summary>
    public bool Link(int follower, int leader)
    {
        if (follower < 0 || leader < 0 || follower >= Tokens.Count || leader >= Tokens.Count || follower == leader
            || Tokens[follower].Owner != Tokens[leader].Owner)
        {
            return false;
        }
        int parent = leader;
        for (int visits = 0; visits <= _links.Count; visits++)
        {
            if (parent == follower)
            {
                return false;
            }
            if (!_links.TryGetValue(parent, out int next))
            {
                break;
            }
            parent = next;
        }
        _links[follower] = leader;
        _followerTargets.Remove(follower);
        return true;
    }

    public void Unlink(int follower)
    {
        _links.Remove(follower);
        _followerTargets.Remove(follower);
        _steppingAside.Remove(follower);
        if (follower >= 0 && follower < Tokens.Count)
        {
            Tokens[follower].Path.Clear();
        }
    }

    public void ClearLinks()
    {
        foreach (int follower in _links.Keys)
        {
            if (follower < Tokens.Count)
            {
                Tokens[follower].Path.Clear();
            }
        }
        _links.Clear();
        _followerTargets.Clear();
        _steppingAside.Clear();
    }

    public int? Follows(int follower) => _links.TryGetValue(follower, out int leader) ? leader : null;

    public void SetFloor(int token, int floor, bool includeFollowers = true)
    {
        if (token < 0 || token >= Tokens.Count)
        {
            return;
        }
        Tokens[token].Floor = floor;
        Tokens[token].Path.Clear();
        if (includeFollowers)
        {
            foreach (KeyValuePair<int, int> link in _links.ToList())
            {
                if (link.Value == token)
                {
                    SetFloor(link.Key, floor, true);
                }
            }
        }
        _followerTargets.Clear();
    }

    /// <summary>One step of time: followers pick their routes, idle ones make way, everyone walks.</summary>
    public void Advance(Grid grid, Func<Cell, bool> passable, double deltaSeconds)
    {
        FollowParty(grid, passable);
        MakeWay(grid, passable);
        Walk(deltaSeconds, grid);
    }

    private bool Controllable(Token token) => token.Owner == LocalPlayer;

    private void Walk(double deltaSeconds, Grid grid)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
        {
            return;
        }
        for (int i = 0; i < Tokens.Count; i++)
        {
            Token token = Tokens[i];
            if (Settings.InCombat && Settings.ActiveTurn != i)
            {
                continue;
            }
            float step = Settings.WalkCellsPerSecond * MathF.Max(token.Pace, 0.0f) * grid.Size * (float)deltaSeconds;
            while (step > 0 && token.Path.Count > 0)
            {
                Vector2 toNext = token.Path[0] - token.Position;
                float distance = Stealth.Length(toNext);
                if (distance <= step)
                {
                    token.Position = token.Path[0];
                    token.Path.RemoveAt(0);
                    step -= distance;
                }
                else
                {
                    token.Position = token.Position + toNext * (step / distance);
                    step = 0;
                }
            }
        }
    }

    private void FollowParty(Grid grid, Func<Cell, bool> passable)
    {
        if (Settings.InCombat)
        {
            return;
        }
        float spacing = MathF.Max(1.0f, Settings.FollowDistanceCells);
        foreach ((int follower, int leader) in _links)
        {
            if (follower >= Tokens.Count || leader >= Tokens.Count)
            {
                continue;
            }
            Token child = Tokens[follower];
            Token parent = Tokens[leader];
            if (!Controllable(child) || child.Selected || child.Floor != parent.Floor || child.Floor != ViewedFloor)
            {
                continue;
            }
            Cell from = grid.CellAt(child.Position), to = grid.CellAt(parent.Position);
            if (grid.Distance(from, to) <= spacing)
            {
                if (_steppingAside.Contains(follower) && child.Path.Count > 0)
                {
                    continue;
                }
                child.Path.Clear();
                _followerTargets.Remove(follower);
                continue;
            }
            _steppingAside.Remove(follower);
            if (_followerTargets.TryGetValue(follower, out Cell planned) && planned == to && child.Path.Count > 0)
            {
                continue;
            }
            _followerTargets[follower] = to;
            List<Cell> route = Paths.Find(grid, from, to, passable, 20000);
            child.Path.Clear();
            for (int i = 1; i < route.Count && grid.Distance(route[i], to) >= spacing; i++)
            {
                child.Path.Add(grid.Center(route[i]));
            }
        }
    }

    private void MakeWay(Grid grid, Func<Cell, bool> passable)
    {
        _steppingAside.RemoveWhere(i => i >= Tokens.Count || Tokens[i].Path.Count == 0);
        if (!Settings.AlliesMakeWay || Settings.InCombat)
        {
            return;
        }

        // Cells walking tokens are about to pass through, and cells someone stands in or is headed to.
        var route = new HashSet<Cell>();
        var standing = new HashSet<Cell>();
        for (int i = 0; i < Tokens.Count; i++)
        {
            Token token = Tokens[i];
            if (token.Floor != ViewedFloor)
            {
                continue;
            }
            standing.Add(grid.CellAt(token.Path.Count == 0 ? token.Position : token.Path[^1]));
            if (Controllable(token) && !_steppingAside.Contains(i))
            {
                foreach (Vector2 at in token.Path)
                {
                    route.Add(grid.CellAt(at));
                }
            }
        }
        if (route.Count == 0)
        {
            return;
        }

        foreach (int follower in _links.Keys.ToList())
        {
            if (follower >= Tokens.Count)
            {
                continue;
            }
            Token token = Tokens[follower];
            if (token.Floor != ViewedFloor || !Controllable(token) || token.Selected || token.Path.Count > 0)
            {
                continue;
            }
            Cell here = grid.CellAt(token.Position);
            if (!route.Contains(here))
            {
                continue;
            }

            // The nearest free cell off the route. None nearby (a narrow corridor): let them pass through.
            Cell? aside = null;
            var queue = new Queue<Cell>();
            queue.Enqueue(here);
            var seen = new HashSet<Cell> { here };
            while (queue.Count > 0 && seen.Count < 80)
            {
                Cell cell = queue.Dequeue();
                if (cell != here && passable(cell) && !route.Contains(cell) && !standing.Contains(cell))
                {
                    aside = cell;
                    break;
                }
                foreach (Cell next in grid.Neighbours(cell))
                {
                    if (passable(next) && seen.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
            if (aside is not Cell free)
            {
                continue;
            }
            List<Cell> path = Paths.Find(grid, here, free, passable, 2000);
            if (path.Count < 2)
            {
                continue;
            }
            token.Path.Clear();
            for (int i = 1; i < path.Count; i++)
            {
                token.Path.Add(grid.Center(path[i]));
            }
            _steppingAside.Add(follower);
            _followerTargets.Remove(follower);
            standing.Add(free);
        }
    }
}
