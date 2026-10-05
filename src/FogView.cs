using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The fog over the map: one pixel per cell, stretched to the map. Black where the party has never
/// looked, a dark veil where it has been but can't see now.
/// </summary>
public partial class FogView : Sprite2D
{
    [Export] public Color Unexplored { get; set; } = new(0, 0, 0, 1);
    [Export] public Color Explored { get; set; } = Color.Color8(4, 6, 14, 175);

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
