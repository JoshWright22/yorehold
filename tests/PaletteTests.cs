using System.Globalization;
using System.Text.RegularExpressions;

namespace Yorehold.Rules.Tests;

/// <summary>
/// Everything is drawn in Apollo and nothing else. These read the scenes, the theme, the shaders and
/// the C# drawing code as text and fail on a colour written into them that is not one of its 46.
/// </summary>
public class PaletteTests
{
    // in Palette.cs order: its 29 named roles, then the rest
    private static readonly string[] Apollo =
    {
        "ebede9", "a8b5b2", "819796", "577277", "394a50", "1e1d39", "151d28", "172038", "253a5e", "3c5e8b",
        "4f8fba", "a4dddb", "73bed3", "da863e", "a53030", "7a367b", "402751", "7a4841", "ad7757", "c7cfcc",
        "a8ca58", "75a743", "25562e", "19332d", "468232", "d0da91", "df84a5", "c65197", "411d31",
        "4d2b32", "c09473", "d7b594", "e7d5b3", "341c27", "602c2c", "884b2b", "be772b", "de9e41", "e8c170",
        "241527", "752438", "cf573c", "a23e8c", "090a14", "10141f", "202e37",
    };

    private static readonly (int R, int G, int B)[] Colours = Apollo
        .Select(h => (Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16)))
        .ToArray();

    private static readonly Regex FloatColour = new(@"(?<![A-Za-z])Color\(\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*,\s*([0-9.]+)f?\s*(?:,\s*([0-9.]+)f?\s*)?\)");
    private static readonly Regex ByteColour = new(@"Color8\(\s*(?:\(byte\))?(?:0x)?([0-9a-fA-F]+)\s*,\s*(?:\(byte\))?(?:0x)?([0-9a-fA-F]+)\s*,\s*(?:\(byte\))?(?:0x)?([0-9a-fA-F]+)");
    private static readonly Regex HexColour = new(@"#([0-9a-fA-F]{6})\b");
    private static readonly Regex ShaderColour = new(@"vec3\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");

    private static string ProjectFolder() => Path.GetDirectoryName(TestContent.AssetsFolder())!;

    private static IEnumerable<string> Files(string folder, params string[] patterns)
    {
        string root = Path.Combine(ProjectFolder(), folder);
        return patterns.SelectMany(p => Directory.EnumerateFiles(root, p, SearchOption.AllDirectories));
    }

    // a byte off either way is how a float written to four places comes back
    private static bool InPalette(int r, int g, int b) =>
        Colours.Any(c => Math.Abs(c.R - r) <= 1 && Math.Abs(c.G - g) <= 1 && Math.Abs(c.B - b) <= 1);

    private static int Byte(string value) => (int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture) * 255);

    private static List<string> OffPalette(IEnumerable<string> files, bool csharp)
    {
        var problems = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetRelativePath(ProjectFolder(), file);
            string[] lines = File.ReadAllLines(file);
            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n];
                foreach (Match m in FloatColour.Matches(line))
                {
                    double alpha = m.Groups[4].Success ? double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture) : 1;
                    if (alpha == 0)
                    {
                        continue; // nothing drawn
                    }
                    if (!InPalette(Byte(m.Groups[1].Value), Byte(m.Groups[2].Value), Byte(m.Groups[3].Value)))
                    {
                        problems.Add($"{name}:{n + 1}: {m.Value} is not an Apollo colour");
                    }
                    else if (!csharp && alpha < 1)
                    {
                        problems.Add($"{name}:{n + 1}: {m.Value} is see-through, which blends off the palette");
                    }
                }
                foreach (Match m in ByteColour.Matches(line))
                {
                    bool hex = m.Value.Contains("0x");
                    int Part(int i) => hex ? Convert.ToInt32(m.Groups[i].Value, 16) : int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                    if (!InPalette(Part(1), Part(2), Part(3)))
                    {
                        problems.Add($"{name}:{n + 1}: {m.Value} is not an Apollo colour");
                    }
                }
                foreach (Match m in HexColour.Matches(line))
                {
                    string h = m.Groups[1].Value;
                    if (!InPalette(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16)))
                    {
                        problems.Add($"{name}:{n + 1}: #{h} is not an Apollo colour");
                    }
                }
            }
        }
        return problems;
    }

    [Fact]
    public void ScenesAndThemesUseOnlyThePalette()
    {
        List<string> problems = OffPalette(Files("scenes", "*.tscn", "*.tres"), csharp: false);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void DrawingCodeUsesOnlyThePalette()
    {
        List<string> problems = OffPalette(Files("src", "*.cs"), csharp: true);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void ShadersListTheWholePaletteAndNothingElse()
    {
        foreach (string file in Files("scenes", "*.gdshader"))
        {
            var found = ShaderColour.Matches(File.ReadAllText(file))
                .Select(m => (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)))
                .ToList();
            if (found.Count == 0)
            {
                continue;
            }
            Assert.True(found.SequenceEqual(Colours), $"{Path.GetFileName(file)}: its palette is not Apollo in the order of Palette.cs");
        }
    }

    [Fact]
    public void TheGameShipsNoPicturesOfItsOwn()
    {
        // every picture comes from content packages and the players' art packs; the placeholder
        // app icon (icon.svg, beside the project) is the one stand-in until there is a logo
        string[] pictures = { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.svg", "*.bmp", "*.gif" };
        List<string> found = pictures.SelectMany(p => Directory.EnumerateFiles(TestContent.AssetsFolder(), p, SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(ProjectFolder(), f)).ToList();
        Assert.True(found.Count == 0, "pictures in the game's own content: " + string.Join(", ", found));
    }

    [Fact]
    public void PaletteFileIsApollo()
    {
        string palette = File.ReadAllText(Path.Combine(ProjectFolder(), "src", "hud", "Palette.cs"));
        var found = ByteColour.Matches(palette)
            .Select(m => (Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16), Convert.ToInt32(m.Groups[3].Value, 16)))
            .ToHashSet();
        Assert.True(found.SetEquals(Colours), "src/hud/Palette.cs does not hold exactly the 46 Apollo colours");
    }
}
