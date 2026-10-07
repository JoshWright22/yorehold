using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The map's tiles and grid lines. The tiles go into blocks of a few cells each, so a torch only
/// lights the blocks it reaches and no block has more lights on it than the renderer allows.
/// </summary>
public partial class MapView : Node2D
{
    private const int Block = 8;

    [Export] public Color GridColor { get; set; } = Palette.Faded(Palette.Ink, 0.2f);

    private readonly List<Rid> _items = new();
    private ImageTexture? _atlas;

    public void Build(World world)
    {
        Clear();
        GameMap map = world.Chapter.Map;
        (_atlas, int size) = TileArt.Atlas(map.Types, world.Files);
        int cell = GameMap.CellSize;

        // a layer's painted picture lies under every tile
        Rid painted = NewItem();
        RenderingServer.CanvasItemSetDefaultTextureFilter(painted, RenderingServer.CanvasItemTextureFilter.Linear);
        foreach (MapLayer layer in map.Layers)
        {
            if (layer.Floor == 0 && layer.Visible && layer.ImageArea.Length == 4 && PlayerArt.Texture(world.Files, layer.Image) is Texture2D picture)
            {
                double[] a = layer.ImageArea;
                RenderingServer.CanvasItemAddTextureRect(painted, new Rect2((float)a[0], (float)a[1], (float)a[2], (float)a[3]), picture.GetRid());
            }
        }

        for (int by = 0; by < map.Height; by += Block)
        {
            for (int bx = 0; bx < map.Width; bx += Block)
            {
                Rid item = NewItem();
                for (int layer = 0; layer < map.Layers.Count; layer++)
                {
                    MapLayer tiles = map.Layers[layer];
                    if (tiles.Floor != 0 || !tiles.Visible)
                    {
                        continue;
                    }
                    for (int y = by; y < by + Block && y < map.Height; y++)
                    {
                        for (int x = bx; x < bx + Block && x < map.Width; x++)
                        {
                            int id = tiles.Tiles[y * map.Width + x];
                            if (id == 0)
                            {
                                continue;
                            }
                            RenderingServer.CanvasItemAddTextureRectRegion(item, new Rect2(x * cell, y * cell, cell, cell), _atlas.GetRid(),
                                new Rect2((id - 1) * size, 0, size, size));
                        }
                    }
                }
            }
        }

        var lines = new List<Vector2>();
        for (int x = 0; x <= map.Width; x++)
        {
            lines.Add(new Vector2(x * cell, 0));
            lines.Add(new Vector2(x * cell, map.Height * cell));
        }
        for (int y = 0; y <= map.Height; y++)
        {
            lines.Add(new Vector2(0, y * cell));
            lines.Add(new Vector2(map.Width * cell, y * cell));
        }
        RenderingServer.CanvasItemAddMultiline(NewItem(), lines.ToArray(), new[] { GridColor });
    }

    public override void _ExitTree()
    {
        Clear();
    }

    private Rid NewItem()
    {
        Rid item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(item, GetCanvasItem());
        RenderingServer.CanvasItemSetDefaultTextureFilter(item, RenderingServer.CanvasItemTextureFilter.Nearest);
        _items.Add(item);
        return item;
    }

    private void Clear()
    {
        foreach (Rid item in _items)
        {
            RenderingServer.FreeRid(item);
        }
        _items.Clear();
    }
}
