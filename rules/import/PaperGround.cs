using StbImageSharp;
using StbImageWriteSharp;

namespace Yorehold.Rules;

/// <summary>
/// A figure cut out of a book usually stands on the page's paper. When a picture's edge is
/// mostly one light colour, that colour is flooded in from the edge and made see-through, so the
/// figure stands on its own in a conversation and on a token. Anything else is left as it was.
/// </summary>
public static class PaperGround
{
    /// <summary>How close to the paper's colour a pixel is to count as paper (distance in 0-255 RGB).</summary>
    public const int Tolerance = 40;
    /// <summary>How much of the edge has to be the paper's colour for the picture to have a plain ground.</summary>
    public const double EdgeShare = 0.7;
    /// <summary>How light the ground has to be (0-255) to be paper rather than a dark scene.</summary>
    public const int Light = 170;

    /// <summary>The picture as a PNG with its paper see-through, or null when it has no plain light ground.</summary>
    public static byte[]? Clear(byte[] picture)
    {
        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(picture, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception)
        {
            return null; // not a picture this can read: it goes in as it is
        }
        int w = image.Width, h = image.Height;
        byte[] px = image.Data;
        if (w < 3 || h < 3)
        {
            return null;
        }

        var edge = new List<int>();
        for (int x = 0; x < w; x++)
        {
            edge.Add(x);
            edge.Add((h - 1) * w + x);
        }
        for (int y = 1; y < h - 1; y++)
        {
            edge.Add(y * w);
            edge.Add(y * w + w - 1);
        }
        // the paper is the edge's middle colour, channel by channel
        int Median(int channel) => edge.Select(i => (int)px[i * 4 + channel]).Order().ElementAt(edge.Count / 2);
        (int r, int g, int b) paper = (Median(0), Median(1), Median(2));
        if ((paper.r + paper.g + paper.b) / 3 < Light)
        {
            return null;
        }
        bool IsPaper(int i)
        {
            int dr = px[i * 4] - paper.r, dg = px[i * 4 + 1] - paper.g, db = px[i * 4 + 2] - paper.b;
            return px[i * 4 + 3] > 0 && dr * dr + dg * dg + db * db <= Tolerance * Tolerance;
        }
        if (edge.Count(IsPaper) < EdgeShare * edge.Count)
        {
            return null;
        }

        var seen = new bool[w * h];
        var stack = new Stack<int>(edge.Where(IsPaper));
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (seen[i] || !IsPaper(i))
            {
                continue;
            }
            seen[i] = true;
            px[i * 4 + 3] = 0;
            int x = i % w, y = i / w;
            if (x > 0) stack.Push(i - 1);
            if (x < w - 1) stack.Push(i + 1);
            if (y > 0) stack.Push(i - w);
            if (y < h - 1) stack.Push(i + w);
        }
        using var output = new MemoryStream();
        new ImageWriter().WritePng(px, w, h, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
        return output.ToArray();
    }
}
