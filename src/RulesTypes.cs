using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>The rules use System.Numerics and their own colours; the screen uses Godot's.</summary>
public static class RulesTypes
{
    public static Vector2 ToGodot(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    public static System.Numerics.Vector2 ToRules(this Vector2 v) => new(v.X, v.Y);

    public static Color ToGodot(this ContentColor c) => Color.Color8(c.R, c.G, c.B, c.A);
}
