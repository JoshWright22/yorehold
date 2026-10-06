using Godot;

namespace Yorehold;

/// <summary>One light in the light map: a map lamp or the light a hero carries. Flames flicker a little.</summary>
public partial class LightView : PointLight2D
{
    private float _baseScale = 1;
    private bool _flicker;
    private float _phase;
    private double _time;

    /// <summary>Radius in world units.</summary>
    public void Setup(Vector2 at, float radius, Color color, bool flicker, int index)
    {
        Position = at;
        Color = color;
        SetRadius(radius);
        _flicker = flicker;
        _phase = index;
    }

    public void SetRadius(float radius)
    {
        float textureSize = Texture?.GetWidth() ?? 256;
        _baseScale = radius * 2 / textureSize;
        TextureScale = _baseScale;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_flicker)
        {
            TextureScale = _baseScale * (1 + 0.04f * Mathf.Sin((float)_time * 9 + _phase * 2.3f));
        }
    }
}
