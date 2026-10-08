namespace Yorehold.Rules;

/// <summary>
/// Where a picture's subject is, so a round token or a card can be cut around the creature
/// instead of the middle of the painting. X and Y run 0 to 1 across the picture; Zoom 1 shows
/// as much of the picture as the frame's shape allows, 2 half as much.
/// </summary>
public sealed record PictureFocus(double X = 0.5, double Y = 0.5, double Zoom = 1)
{
    public static readonly PictureFocus Middle = new();

    public const double MaxZoom = 8;

    /// <summary>
    /// The part of a width × height picture shown in a frame of the given shape (its width over
    /// its height), centred on the focus and pushed back inside the picture at the edges, so a
    /// face near a border is still shown whole rather than with blank space beside it.
    /// </summary>
    public (double X, double Y, double Width, double Height) Cut(double width, double height, double aspect)
    {
        if (width <= 0 || height <= 0 || aspect <= 0)
        {
            return (0, 0, Math.Max(0, width), Math.Max(0, height));
        }
        double w = width / height > aspect ? height * aspect : width;
        double h = w / aspect;
        double zoom = Math.Clamp(Zoom, 1, MaxZoom);
        w /= zoom;
        h /= zoom;
        double x = Math.Clamp(Math.Clamp(X, 0, 1) * width - w / 2, 0, width - w);
        double y = Math.Clamp(Math.Clamp(Y, 0, 1) * height - h / 2, 0, height - h);
        return (x, y, w, h);
    }

    /// <summary>A token's "focus": [x, y] and "zoom", both optional; null when it says neither.</summary>
    public static PictureFocus? Read(ContentNode token)
    {
        if (token.Get("focus") == null && token.Get("zoom") == null)
        {
            return null;
        }
        double x = 0.5, y = 0.5;
        if (token.Get("focus") is ContentNode focus)
        {
            if (!focus.IsArray || focus.Count != 2)
            {
                throw focus.Fail("is [x, y], each from 0 to 1 across the picture");
            }
            ContentNode[] at = focus.Items().ToArray();
            x = at[0].AsNumber(0, 1);
            y = at[1].AsNumber(0, 1);
        }
        return new PictureFocus(x, y, token.Number("zoom", 1, 1, MaxZoom));
    }
}
