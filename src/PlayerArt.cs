using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Pictures made by the people who write the content: faces, tiles, painted maps. They are read
/// from the same content tree as the chapter, so a package's own pictures win over the game's.
/// The game ships none of its own; with no picture, whoever draws it falls back to a plain shape.
/// </summary>
public static class PlayerArt
{
    // by file on disk and when it was written, so a picture changed while the game runs is read again
    private static readonly Dictionary<(string, DateTime), (Image? Image, Texture2D? Texture)> Loaded = new();
    private static ContentFiles? _game;

    /// <summary>The picture at a content path, or null when there is none or it can't be read.</summary>
    public static Image? Picture(ContentFiles? files, string path) => Load(files, path).Image;

    public static Texture2D? Texture(ContentFiles? files, string path) => Load(files, path).Texture;

    private static (Image? Image, Texture2D? Texture) Load(ContentFiles? files, string path)
    {
        files ??= _game ??= App.Content();
        if (path.Length == 0 || files.FullPath(path) is not string file)
        {
            return (null, null);
        }
        var key = (file, System.IO.File.GetLastWriteTimeUtc(file));
        if (Loaded.TryGetValue(key, out (Image?, Texture2D?) known))
        {
            return known;
        }
        var image = new Image();
        byte[] bytes = System.IO.File.ReadAllBytes(file);
        Error error = System.IO.Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".png" => image.LoadPngFromBuffer(bytes),
            ".jpg" or ".jpeg" => image.LoadJpgFromBuffer(bytes),
            ".webp" => image.LoadWebpFromBuffer(bytes),
            _ => Error.FileUnrecognized,
        };
        (Image?, Texture2D?) loaded = (null, null);
        if (error == Error.Ok)
        {
            loaded = (image, ImageTexture.CreateFromImage(image));
        }
        else
        {
            GD.PushWarning($"{path}: not a picture the game can read (png, jpg or webp)");
        }
        Loaded[key] = loaded;
        return loaded;
    }
}
