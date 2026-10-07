using StbImageSharp;
using StbImageWriteSharp;

namespace Yorehold.Rules.Tests;

public class PaperGroundTests
{
    // a 20 by 20 picture: ground all round, a dark figure in the middle, and a hole of ground colour
    // inside the figure that the edge can't reach
    public static byte[] Figure(byte ground)
    {
        var px = new byte[20 * 20 * 4];
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                bool figure = x >= 5 && x < 15 && y >= 4 && y < 18;
                bool hole = x >= 9 && x < 11 && y >= 9 && y < 11;
                byte v = figure && !hole ? (byte)40 : ground;
                int i = (y * 20 + x) * 4;
                px[i] = v;
                px[i + 1] = v;
                px[i + 2] = (byte)(figure && !hole ? 60 : Math.Max(0, ground - 10));
                px[i + 3] = 255;
            }
        }
        using var output = new MemoryStream();
        new ImageWriter().WritePng(px, 20, 20, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        return output.ToArray();
    }

    private static byte Alpha(ImageResult image, int x, int y) => image.Data[(y * image.Width + x) * 4 + 3];

    [Fact]
    public void PaperRoundAFigureIsMadeSeeThrough()
    {
        byte[]? cleared = PaperGround.Clear(Figure(235));
        Assert.NotNull(cleared);
        ImageResult image = ImageResult.FromMemory(cleared, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Assert.Equal(0, Alpha(image, 0, 0));
        Assert.Equal(0, Alpha(image, 19, 2));
        Assert.True(Alpha(image, 6, 6) == 255, "The figure stays");
        Assert.True(Alpha(image, 9, 9) == 255, "Paper-coloured parts inside the figure stay: only what the edge reaches goes");
    }

    [Fact]
    public void ADarkSceneOrSomethingElseIsLeftAlone()
    {
        Assert.Null(PaperGround.Clear(Figure(30)));
        Assert.Null(PaperGround.Clear(new byte[] { 1, 2, 3, 4 }));
    }
}
