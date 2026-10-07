using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>Everyone on the map, one Token scene each, and the lines to where the walkers are headed.</summary>
public partial class TokensView : Node2D
{
    [Export] public PackedScene? TokenScene { get; set; }

    private readonly List<TokenView> _views = new();
    private World? _world;
    private TokenSkin _skin = new();

    public void Build(World world)
    {
        _world = world;
        try
        {
            _skin = TokenSkin.Load(world.Files);
        }
        catch (ContentException error)
        {
            GD.PushWarning($"Token frames: {error.Message}");
            _skin = new TokenSkin();
        }
        foreach (TokenView view in _views)
        {
            view.QueueFree();
        }
        _views.Clear();
        if (TokenScene == null)
        {
            GD.PushError("TokensView needs its Token scene");
            return;
        }
        for (int i = 0; i < world.Tokens.Tokens.Count; i++)
        {
            var view = TokenScene.Instantiate<TokenView>();
            view.Name = $"Token{i}";
            AddChild(view);
            _views.Add(view);
        }
    }

    public override void _Process(double delta)
    {
        if (_world == null)
        {
            return;
        }
        World w = _world;
        if (_views.Count != w.Tokens.Tokens.Count)
        {
            Build(w); // a companion came along or left with a save
        }
        int team = w.ViewTeam();
        for (int i = 0; i < _views.Count && i < w.Tokens.Tokens.Count; i++)
        {
            Token token = w.Tokens.Tokens[i];
            bool hero = i < w.HeroCount;
            bool dead = token.Floor == World.DeadFloor;
            // heroes always show; the rest only where the party sees them, or where they fell once that is explored
            bool seen = hero || (dead ? w.Fog.State(team, 0, w.CellOf(i)) != FogState.Unexplored : token.Floor == 0);
            TokenSide side = w.SideOf(i);
            _views[i].Show(token, hero, dead, seen, i < w.Creatures.Count ? Portraits.Of(w, i) : null,
                side, PlayerArt.Texture(w.Files, TokenSkin.FramePath(side)), _skin);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }
        for (int i = 0; i < _world.HeroCount; i++)
        {
            Token token = _world.Tokens.Tokens[i];
            if (token.Path.Count == 0)
            {
                continue;
            }
            Color color = Palette.Nearest(token.Color.ToGodot());
            Vector2 from = token.Position.ToGodot();
            foreach (System.Numerics.Vector2 to in token.Path)
            {
                DrawLine(from, to.ToGodot(), color, 3);
                from = to.ToGodot();
            }
        }
    }
}
