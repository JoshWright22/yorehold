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
        _atlas = TileArt.Atlas(map.Types);
        int cell = GameMap.CellSize;
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
                                new Rect2((id - 1) * TileArt.Size, 0, TileArt.Size, TileArt.Size));
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
