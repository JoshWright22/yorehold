using System.Globalization;
using System.Text.RegularExpressions;

namespace Yorehold.Rules.Tests;

/// <summary>
/// Everything is drawn in CC-29 and nothing else. These read the scenes, the theme, the shaders and
/// the C# drawing code as text and fail on a colour written into them that is not one of the 29.
/// </summary>
public class PaletteTests
{
    private static readonly string[] Cc29 =
    {
        "f2f0e5", "b8b5b9", "868188", "646365", "45444f", "3a3858", "212123", "352b42", "43436a", "4b80ca",
        "68c2d3", "a2dcc7", "ede19e", "d3a068", "b45252", "6a536e", "4b4158", "80493a", "a77b5b", "e5ceb4",
        "c2d368", "8ab060", "567b79", "4e584a", "7b7243", "b2b47e", "edc8c4", "cf8acb", "5f556a",
    };

    private static readonly (int R, int G, int B)[] Colours = Cc29
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
                        problems.Add($"{name}:{n + 1}: {m.Value} is not a CC-29 colour");
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
                        problems.Add($"{name}:{n + 1}: {m.Value} is not a CC-29 colour");
                    }
                }
                foreach (Match m in HexColour.Matches(line))
                {
                    string h = m.Groups[1].Value;
                    if (!InPalette(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..], 16)))
                    {
                        problems.Add($"{name}:{n + 1}: #{h} is not a CC-29 colour");
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
            Assert.True(found.SequenceEqual(Colours), $"{Path.GetFileName(file)}: its palette is not CC-29 in the order of Palette.cs");
        }
    }

    [Fact]
    public void PaletteFileIsCc29()
    {
        string palette = File.ReadAllText(Path.Combine(ProjectFolder(), "src", "hud", "Palette.cs"));
        var found = ByteColour.Matches(palette)
            .Select(m => (Convert.ToInt32(m.Groups[1].Value, 16), Convert.ToInt32(m.Groups[2].Value, 16), Convert.ToInt32(m.Groups[3].Value, 16)))
            .ToHashSet();
        Assert.True(found.SetEquals(Colours), "src/hud/Palette.cs does not hold exactly the 29 CC-29 colours");
    }
}
