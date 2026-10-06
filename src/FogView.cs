using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The fog over the map: one pixel per cell, stretched to the map. Ink where the party has never
/// looked, a dark veil where it has been but can't see now.
/// </summary>
public partial class FogView : Sprite2D
{
    // palette ink, never pure black
    [Export] public Color Unexplored { get; set; } = Palette.Ink;
    [Export] public Color Explored { get; set; } = Palette.Faded(Palette.Ink, 0.69f);

    private World? _world;
    private Image? _image;
    private ImageTexture? _texture;

    public void Bind(World world)
    {
        _world = world;
        _image = Image.CreateEmpty(world.Map.Width, world.Map.Height, false, Image.Format.Rgba8);
        _image.Fill(Unexplored);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
        Centered = false;
        Scale = new Vector2(GameMap.CellSize, GameMap.CellSize);
        QueueRedraw();
    }

    // Off the map the light map is black, so the same ink as the fog goes round it, in cells.
    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }
        const int Far = 200;
        int w = _world.Map.Width, h = _world.Map.Height;
        DrawRect(new Rect2(-Far, -Far, w + 2 * Far, Far), Unexplored);
        DrawRect(new Rect2(-Far, h, w + 2 * Far, Far), Unexplored);
        DrawRect(new Rect2(-Far, 0, Far, h), Unexplored);
        DrawRect(new Rect2(w, 0, Far, h), Unexplored);
    }

    public override void _Process(double delta)
    {
        if (_world == null || _image == null || _texture == null)
        {
            return;
        }
        int team = _world.ViewTeam();
        var clear = new Color(0, 0, 0, 0);
        for (int y = 0; y < _world.Map.Height; y++)
        {
            for (int x = 0; x < _world.Map.Width; x++)
            {
                FogState state = _world.Fog.State(team, 0, new Cell(x, y));
                _image.SetPixel(x, y, state == FogState.Visible ? clear : state == FogState.Explored ? Explored : Unexplored);
            }
        }
        _texture.Update(_image);
    }
}
