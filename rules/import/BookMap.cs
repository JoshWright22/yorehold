using StbImageSharp;

namespace Yorehold.Rules;

/// <summary>
/// A book's map picture read into squares: which are floor, which are doors, and which numbered
/// place each floor square belongs to. The floor is whatever looks like the ground under the
/// places' numbers and can be walked to from one of them; the squares are the picture's own grid
/// when it draws one. Nothing here knows a book: a picture this can't make sense of gives null
/// and the rooms are laid out as boxes instead.
/// </summary>
public sealed class BookMap
{
    /// <summary>The longest side a picture is read at; a larger one is shrunk first.</summary>
    public const int ReadSize = 700;
    /// <summary>How much of a square has to look like floor for it to be floor.</summary>
    public const double FloorShare = 0.3;
    /// <summary>How much of a square has to be plain white or grey for it to be a door.</summary>
    public const double DoorShare = 0.15;
    /// <summary>The most squares one white patch can cover and still be a door, not paper.</summary>
    public const int DoorSquares = 6;
    /// <summary>What walking through a door costs when squares are shared out between places: a door ends a room.</summary>
    public const int DoorCost = 40;
    /// <summary>How many squares across the picture is taken to be when it draws no grid.</summary>
    public const int SquaresWithoutGrid = 40;

    private BookMap(int width, int height)
    {
        Width = width;
        Height = height;
        Floor = new bool[height, width];
        Door = new bool[height, width];
        Place = new string?[height, width];
    }

    public int Width { get; }
    public int Height { get; }
    public bool[,] Floor { get; }
    public bool[,] Door { get; }
    /// <summary>The place each floor square belongs to.</summary>
    public string?[,] Place { get; }
    /// <summary>The square each place's number sits on.</summary>
    public Dictionary<string, Cell> Labels { get; } = new(StringComparer.Ordinal);
    /// <summary>Did the picture draw a grid; without one the squares are a guess at its scale.</summary>
    public bool GridFound { get; private set; }
    /// <summary>Where the whole picture lies in these squares: left, top, width, height.</summary>
    public (double X, double Y, double Width, double Height) Picture { get; private set; }

    public IEnumerable<Cell> CellsOf(string place)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Place[y, x] == place)
                {
                    yield return new Cell(x, y);
                }
            }
        }
    }

    /// <summary>The squares as text, one row a line, for a look by hand: a for the first place's floor (by id), b the next, + a door.</summary>
    public List<string> Rows()
    {
        var rows = new List<string>();
        List<string> names = Labels.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        for (int y = 0; y < Height; y++)
        {
            var row = new char[Width];
            for (int x = 0; x < Width; x++)
            {
                row[x] = Door[y, x] ? '+' : Place[y, x] is string place ? (char)('a' + names.IndexOf(place) % 26) : ' ';
            }
            rows.Add(new string(row));
        }
        return rows;
    }

    /// <summary>
    /// labels: each place's id and where its number is on the picture, 0-1 across and down.
    /// Null when the picture can't be read, the ground under the numbers doesn't stand out from
    /// the rest, or a number isn't on any floor.
    /// </summary>
    public static BookMap? Read(byte[] picture, IReadOnlyDictionary<string, (double X, double Y)> labels)
    {
        if (labels.Count == 0)
        {
            return null;
        }
        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(picture, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception)
        {
            return null;
        }
        int shrink = Math.Max(1, (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)ReadSize));
        int w = image.Width / shrink, h = image.Height / shrink;
        if (w < 24 || h < 24)
        {
            return null;
        }
        var light = new float[h, w];
        var white = new bool[h, w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int r = 0, g = 0, b = 0;
                for (int sy = 0; sy < shrink; sy++)
                {
                    for (int sx = 0; sx < shrink; sx++)
                    {
                        int at = ((y * shrink + sy) * image.Width + x * shrink + sx) * 4;
                        r += image.Data[at];
                        g += image.Data[at + 1];
                        b += image.Data[at + 2];
                    }
                }
                int n = shrink * shrink;
                r /= n;
                g /= n;
                b /= n;
                light[y, x] = (r * 299 + g * 587 + b * 114) / 1000f;
                // a door is drawn as a small plain box: white or pale grey, with none of the ground's colour in it
                white[y, x] = light[y, x] >= 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 20;
            }
        }

        (double pitch, double left, double top, bool grid) = FindGrid(light, w, h);
        // the strip before the first line is part of a square too
        left = left > 0.01 ? left - pitch : 0;
        top = top > 0.01 ? top - pitch : 0;
        // a sliver past the last line is not another square
        int columns = (int)Math.Ceiling((w - left) / pitch - 0.1), rows = (int)Math.Ceiling((h - top) / pitch - 0.1);
        if (columns < 3 || rows < 3 || columns > 400 || rows > 400)
        {
            return null;
        }

        // what the ground under the numbers looks like, against the rest of the picture
        var under = new List<float>();
        foreach ((double lx, double ly) in labels.Values)
        {
            under.Add(MedianRound(light, w, h, lx * w, ly * h, pitch * 0.75));
        }
        under.Sort();
        float floorLight = under[under.Count / 2];
        var all = new List<float>(w * h);
        foreach (float v in light)
        {
            all.Add(v);
        }
        all.Sort();
        float restLight = all[all.Count / 2];
        // two heaps of lightness, started at the floor's and the picture's middle
        for (int pass = 0; pass < 8; pass++)
        {
            double floorSum = 0, restSum = 0;
            int floorCount = 0, restCount = 0;
            foreach (float v in all)
            {
                if (Math.Abs(v - floorLight) < Math.Abs(v - restLight))
                {
                    floorSum += v;
                    floorCount++;
                }
                else
                {
                    restSum += v;
                    restCount++;
                }
            }
            if (floorCount == 0 || restCount == 0)
            {
                return null;
            }
            floorLight = (float)(floorSum / floorCount);
            restLight = (float)(restSum / restCount);
        }
        if (Math.Abs(floorLight - restLight) < 16)
        {
            return null;
        }
        // nearer the rest than halfway: a floor in shadow is still floor, and loose specks are dropped later for touching no place
        float edge = restLight + 0.4f * (floorLight - restLight);
        bool lighter = floorLight > restLight;

        var map = new BookMap(columns, rows) { GridFound = grid, Picture = (-left / pitch, -top / pitch, w / pitch, h / pitch) };
        var floorShare = new double[rows, columns];
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < columns; cx++)
            {
                int x0 = Math.Max(0, (int)Math.Round(left + cx * pitch)), x1 = Math.Min(w, (int)Math.Round(left + (cx + 1) * pitch));
                int y0 = Math.Max(0, (int)Math.Round(top + cy * pitch)), y1 = Math.Min(h, (int)Math.Round(top + (cy + 1) * pitch));
                int floor = 0, door = 0, count = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        count++;
                        floor += lighter ? (light[y, x] > edge ? 1 : 0) : (light[y, x] < edge ? 1 : 0);
                        door += white[y, x] ? 1 : 0;
                    }
                }
                // a square mostly off the picture's edge is nothing
                if (count >= pitch * pitch / 3)
                {
                    floorShare[cy, cx] = floor / (double)count;
                    map.Door[cy, cx] = door >= count * DoorShare;
                }
            }
        }
        // a square half floor is floor; one less than that only beside one that is, so a room's
        // ragged edge is kept and a light patch out in the rock is not
        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < columns; cx++)
            {
                map.Floor[cy, cx] = floorShare[cy, cx] >= 0.5 || floorShare[cy, cx] >= FloorShare
                    && Beside(new Cell(cx, cy)).Any(c => map.Inside(c) && floorShare[c.Y, c.X] >= 0.5);
            }
        }
        map.DropPaper();

        foreach ((string id, (double lx, double ly)) in labels.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            var at = new Cell((int)Math.Floor((lx * w - left) / pitch), (int)Math.Floor((ly * h - top) / pitch));
            if (map.NearestFloor(at, 3) is not Cell on)
            {
                return null;
            }
            map.Labels[id] = on;
        }
        map.ShareOut();
        return map;
    }

    // a white patch too big to be a door is the page, not the map
    private void DropPaper()
    {
        var seen = new bool[Height, Width];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!Door[y, x] || seen[y, x])
                {
                    continue;
                }
                var patch = new List<Cell>();
                var queue = new Queue<Cell>();
                queue.Enqueue(new Cell(x, y));
                seen[y, x] = true;
                while (queue.Count > 0)
                {
                    Cell at = queue.Dequeue();
                    patch.Add(at);
                    foreach (Cell next in Beside(at))
                    {
                        if (Inside(next) && Door[next.Y, next.X] && !seen[next.Y, next.X])
                        {
                            seen[next.Y, next.X] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
                if (patch.Count > DoorSquares)
                {
                    foreach (Cell at in patch)
                    {
                        Door[at.Y, at.X] = false;
                    }
                }
            }
        }
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                // a door is walked through, so it is floor too
                Floor[y, x] |= Door[y, x];
            }
        }
    }

    private bool Inside(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    private static IEnumerable<Cell> Beside(Cell c)
    {
        yield return new Cell(c.X + 1, c.Y);
        yield return new Cell(c.X - 1, c.Y);
        yield return new Cell(c.X, c.Y + 1);
        yield return new Cell(c.X, c.Y - 1);
    }

    private Cell? NearestFloor(Cell at, int reach)
    {
        Cell? best = null;
        int bestFar = int.MaxValue;
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                var c = new Cell(at.X + dx, at.Y + dy);
                int far = Math.Abs(dx) + Math.Abs(dy);
                if (far <= reach && Inside(c) && Floor[c.Y, c.X] && !Door[c.Y, c.X] && far < bestFar)
                {
                    best = c;
                    bestFar = far;
                }
            }
        }
        return best;
    }

    // every floor square goes to the place whose number is the shortest walk away, a door counting
    // as a long one; floor no number can be walked to from is dropped
    private void ShareOut()
    {
        var cost = new int[Height, Width];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                cost[y, x] = int.MaxValue;
            }
        }
        var queue = new PriorityQueue<(Cell At, string Place), (int Cost, int Order)>();
        int order = 0;
        foreach ((string id, Cell at) in Labels.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            if (cost[at.Y, at.X] != 0)
            {
                cost[at.Y, at.X] = 0;
                Place[at.Y, at.X] = id;
                queue.Enqueue((at, id), (0, order++));
            }
        }
        while (queue.TryDequeue(out (Cell At, string Place) now, out (int Cost, int Order) priority))
        {
            if (priority.Cost > cost[now.At.Y, now.At.X])
            {
                continue;
            }
            foreach (Cell next in Beside(now.At))
            {
                if (!Inside(next) || !Floor[next.Y, next.X])
                {
                    continue;
                }
                int to = priority.Cost + (Door[next.Y, next.X] ? DoorCost : 1);
                if (to < cost[next.Y, next.X])
                {
                    cost[next.Y, next.X] = to;
                    Place[next.Y, next.X] = now.Place;
                    queue.Enqueue((next, now.Place), (to, order++));
                }
            }
        }
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Place[y, x] == null)
                {
                    Floor[y, x] = false;
                    Door[y, x] = false;
                }
            }
        }
    }

    private static float MedianRound(float[,] light, int w, int h, double x, double y, double reach)
    {
        var values = new List<float>();
        int r = Math.Max(1, (int)Math.Round(reach));
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int px = (int)x + dx, py = (int)y + dy;
                if (px >= 0 && py >= 0 && px < w && py < h)
                {
                    values.Add(light[py, px]);
                }
            }
        }
        if (values.Count == 0)
        {
            return 0;
        }
        values.Sort();
        return values[values.Count / 2];
    }

    // The grid a map draws is thin lines a little darker than the ground either side, the same
    // step apart across and down. How often each row and each column of pixels is such a dip is
    // counted, and the step and start that land on the most dips win.
    private static (double Pitch, double Left, double Top, bool Found) FindGrid(float[,] light, int w, int h)
    {
        var across = new double[w]; // dips in each column: lines running down
        var down = new double[h];
        // rough ground is full of dips too, but only a drawn line is one three pixels running, straight along itself
        bool DipAcross(int x, int y) => light[y, x - 2] - light[y, x] > 8 && light[y, x + 2] - light[y, x] > 8;
        bool DipDown(int x, int y) => light[y - 2, x] - light[y, x] > 8 && light[y + 2, x] - light[y, x] > 8;
        for (int y = 3; y < h - 3; y++)
        {
            for (int x = 3; x < w - 3; x++)
            {
                if (DipAcross(x, y) && DipAcross(x, y - 1) && DipAcross(x, y + 1))
                {
                    across[x]++;
                }
                if (DipDown(x, y) && DipDown(x - 1, y) && DipDown(x + 1, y))
                {
                    down[y]++;
                }
            }
        }
        double mean = (across.Average() + down.Average()) / 2;
        double best = 0, bestPitch = 0, bestLeft = 0, bestTop = 0;
        var scores = new Dictionary<int, (double Score, double Left, double Top)>();
        int most = Math.Min(w, h) / 6 * 10;
        for (int tenths = 60; tenths <= most; tenths++)
        {
            double pitch = tenths / 10.0;
            (double sx, double left) = BestStart(across, pitch);
            (double sy, double top) = BestStart(down, pitch);
            scores[tenths] = ((sx + sy) / 2, left, top);
            if ((sx + sy) / 2 > best)
            {
                (best, bestPitch, bestLeft, bestTop) = ((sx + sy) / 2, pitch, left, top);
            }
        }
        if (best < 2.5 * Math.Max(mean, 0.5))
        {
            return (Math.Max(6, w / (double)SquaresWithoutGrid), 0, 0, false);
        }
        // every second or third line scores as well as every line: the smallest step that still does is the grid's own
        for (int part = 4; part >= 2; part--)
        {
            int around = (int)Math.Round(bestPitch * 10 / part);
            for (int tenths = around - 1; tenths <= around + 1; tenths++)
            {
                if (scores.TryGetValue(tenths, out var s) && s.Score >= 0.6 * best)
                {
                    return (tenths / 10.0, s.Left, s.Top, true);
                }
            }
        }
        return (bestPitch, bestLeft, bestTop, true);
    }

    private static (double Score, double Start) BestStart(double[] dips, double pitch)
    {
        double best = -1, bestStart = 0;
        for (double start = 0; start < pitch; start += 0.5)
        {
            double sum = 0;
            for (double at = start; at < dips.Length - 1; at += pitch)
            {
                sum += dips[(int)Math.Floor(at + 0.5)];
            }
            // over the lines that fit, not the ones this start happens to have: a start at the edge has one more and is no worse for it
            double score = sum / (dips.Length / pitch);
            if (score > best)
            {
                best = score;
                bestStart = start;
            }
        }
        return (Math.Max(best, 0), bestStart);
    }
}
