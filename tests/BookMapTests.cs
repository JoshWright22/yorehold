using System.Text.Json.Nodes;
using StbImageWriteSharp;
using Xunit;
using Yorehold.Rules;

namespace Yorehold.Rules.Tests;

/// <summary>A book's map picture read into squares: its grid, its floor, its doors and whose floor is whose.</summary>
public class BookMapTests
{
    // Rock with two rooms on a 10 pixel grid: one 4 by 3 squares at 2,2 and one 3 by 4 at 9,2,
    // a passage one square high between them along row 3, and a white door across it at square 7,3.
    public static byte[] TwoRooms(bool grid = true)
    {
        const int w = 160, h = 90;
        var px = new byte[w * h * 4];
        void Fill(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int at = (y * w + x) * 4;
                    (px[at], px[at + 1], px[at + 2], px[at + 3]) = (r, g, b, 255);
                }
            }
        }
        Fill(0, 0, w, h, 120, 100, 85);
        void Ground(int cx, int cy, int cw, int ch)
        {
            Fill(cx * 10, cy * 10, (cx + cw) * 10, (cy + ch) * 10, 225, 205, 170);
            for (int x = cx; grid && x <= cx + cw; x++)
            {
                Fill(x * 10, cy * 10, x * 10 + 1, (cy + ch) * 10, 185, 165, 135);
            }
            for (int y = cy; grid && y <= cy + ch; y++)
            {
                Fill(cx * 10, y * 10, (cx + cw) * 10, y * 10 + 1, 185, 165, 135);
            }
        }
        Ground(2, 2, 4, 3);
        Ground(9, 2, 3, 4);
        Ground(6, 3, 3, 1);
        Fill(72, 30, 78, 40, 255, 255, 255);
        using var output = new MemoryStream();
        new ImageWriter().WritePng(px, w, h, ColorComponents.RedGreenBlueAlpha, output);
        return output.ToArray();
    }

    public static Dictionary<string, (double X, double Y)> TwoLabels() => new()
    {
        ["hall"] = (0.25, 0.39),
        ["cell"] = (0.66, 0.44),
    };

    [Fact]
    public void AMapsGridFloorAndDoorAreRead()
    {
        BookMap map = BookMap.Read(TwoRooms(), TwoLabels())!;
        Assert.True(map.GridFound);
        Assert.Equal((16, 9), (map.Width, map.Height));
        Assert.Equal(new[]
        {
            "                ",
            "                ",
            "  bbbb   aaa    ",
            "  bbbbb+aaaa    ",
            "  bbbb   aaa    ",
            "         aaa    ",
            "                ",
            "                ",
            "                ",
        }, map.Rows());
        Assert.Equal(new Cell(4, 3), map.Labels["hall"]);
        // the door ends the hall: the passage past it is the cell's
        Assert.Equal("hall", map.Place[3, 6]);
        Assert.Equal("cell", map.Place[3, 8]);
        Assert.Equal(12 + 1, map.CellsOf("hall").Count());
        // the picture is 16 squares across, give or take the pixel the lines were found to
        Assert.InRange(map.Picture.Width, 15.7, 16.3);
        Assert.InRange(map.Picture.X, -0.2, 0.2);
    }

    [Fact]
    public void AMapWithNoGridOrNoFloorUnderANumberIsNotRead()
    {
        // no grid: squares are a guess, but the floor is still found
        BookMap plain = BookMap.Read(TwoRooms(grid: false), TwoLabels())!;
        Assert.False(plain.GridFound);
        Assert.Contains(plain.Rows(), row => row.Contains('+'));
        // a number out on the rock
        Assert.Null(BookMap.Read(TwoRooms(), new Dictionary<string, (double X, double Y)> { ["hall"] = (0.25, 0.39), ["lost"] = (0.9, 0.9) }));
        Assert.Null(BookMap.Read(new byte[] { 1, 2, 3 }, TwoLabels()));
        Assert.Null(BookMap.Read(TwoRooms(), new Dictionary<string, (double X, double Y)>()));
    }

    [Fact]
    public void ABuiltChapterHasTheFloorWallsAndDoorsItsBooksMapDraws()
    {
        using var scratch = new Scratch();
        string import = Path.Combine(scratch.Folder, "import");
        Directory.CreateDirectory(Path.Combine(import, "pictures"));
        File.WriteAllBytes(Path.Combine(import, "pictures", "map.png"), TwoRooms());
        Outline outline = Outline.Parse("outline.json", """
            {"format": "yorehold.outline", "version": 1, "title": "Two rooms", "entries": [
              {"id": "ch", "kind": "chapter", "data": {"title": "Two rooms", "mapPicture": "pictures/map.png"}},
              {"id": "hall", "kind": "place", "chapter": "ch", "data": {"name": "Hall", "label": "1", "size": [8, 8], "mapAt": [0.25, 0.39]}},
              {"id": "cell", "kind": "place", "chapter": "ch", "data": {"name": "Cell", "label": "2", "size": [8, 8], "mapAt": [0.66, 0.44], "readAloud": ["Straw and a bucket."]}},
              {"id": "way", "kind": "link", "data": {"from": "hall", "to": "cell", "way": "locked", "check": {"skill": "dex", "difficulty": 15}}}]}
            """);
        string package = Path.Combine(scratch.Folder, "package");
        var builder = new OutlineBuilder(outline, import, TestContent.Shipped());
        List<string> problems = builder.Build(package);
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        ContentFiles files = TestContent.Shipped();
        files.Add(package);
        Chapter chapter = Chapter.Load(files, "chapters/ch");
        // the map's squares with a square of rock all round: walls where the rock touches floor
        Assert.Equal(18, chapter.Map.Width);
        Assert.Equal(11, chapter.Map.Height);
        Assert.True(chapter.Map.Walkable(new Cell(3, 3)) && chapter.Map.Walkable(new Cell(7, 4)) && chapter.Map.Walkable(new Cell(12, 6)));
        Assert.False(chapter.Map.Walkable(new Cell(2, 3)) || chapter.Map.Walkable(new Cell(7, 3)) || chapter.Map.Walkable(new Cell(8, 6)));
        // the door the map draws is the link's locked one
        MapObject door = Assert.Single(chapter.Map.Objects);
        Assert.True(door.Door is { Locked: true } && door.Lock is { Dc: 15, Skill: "dex" });
        // the cell is no box: the room above its passage, the row with the passage and the room below each set the flag that reads it out
        Assert.Equal(new[] { "room-cell", "room-cell-2", "room-cell-3" }, chapter.Map.Areas.Select(a => a.Id));
        Assert.All(chapter.Map.Areas, a => Assert.Equal(new[] { "entered_cell" }, a.Set));
        Assert.DoesNotContain(builder.Report, line => line.Text.Contains("no walk from the start") || line.Text.Contains("corridor"));
        Assert.Contains("\"drawn\": true", File.ReadAllText(Path.Combine(import, "report.json")));

        // with the walls switched off the same outline is two boxes, as before
        string boxes = Path.Combine(scratch.Folder, "boxes");
        Assert.Empty(new OutlineBuilder(outline, import, TestContent.Shipped()) { ReadWalls = false }.Build(boxes));
        Assert.Contains("\"drawn\": false", File.ReadAllText(Path.Combine(import, "report.json")));
    }

    // Reads the map of an import made before for a look by hand: YOREHOLD_MAP_IMPORT is its import
    // folder; the squares go to map-squares.txt there. Without the variable nothing runs.
    [Fact]
    public void AnImportsMapIsReadForALook()
    {
        string? folder = Environment.GetEnvironmentVariable("YOREHOLD_MAP_IMPORT");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }
        Outline outline = Outline.Load(folder);
        var lines = new List<string>();
        foreach (OutlineEntry chapter in outline.OfKind(OutlineKind.Chapter).Where(c => c.Text("mapPicture").Length > 0))
        {
            var labels = new Dictionary<string, (double X, double Y)>();
            foreach (OutlineEntry place in outline.OfKind(OutlineKind.Place))
            {
                if (place.Data["mapAt"] is JsonArray { Count: 2 } at)
                {
                    labels[place.Id] = (at[0]!.GetValue<double>(), at[1]!.GetValue<double>());
                }
            }
            BookMap? map = BookMap.Read(File.ReadAllBytes(Path.Combine(folder, chapter.Text("mapPicture"))), labels);
            lines.Add($"{chapter.Id}: {(map == null ? "not read" : $"{map.Width} by {map.Height}, grid {(map.GridFound ? "found" : "guessed")}, picture at {map.Picture}")}");
            lines.AddRange(labels.Keys.Select(id => $"  {id} at {(map?.Labels.TryGetValue(id, out Cell c) == true ? c.ToString() : "?")}"));
            lines.AddRange(map?.Rows() ?? new List<string>());
        }
        File.WriteAllLines(Path.Combine(folder, "map-squares.txt"), lines);
    }
}
