using System;
using Godot;

namespace Yorehold;

/// <summary>
/// The encounter's difficulty as the design draws it: a bar of four bands, Too easy, Fits, Hard
/// and Too hard, filled up to the band the last forecast put the fight in, with each band's name
/// under it. Band reads the forecast each frame; -1 leaves the bar empty (not played yet).
/// </summary>
public partial class DifficultyBar : Control
{
    public static readonly string[] Bands = { "Too easy", "Fits", "Hard", "Too hard" };

    public Func<int> Band { get; set; } = () => -1;

    private int _shown = -2;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 34);
    }

    public override void _Process(double delta)
    {
        int band = Band();
        if (band != _shown)
        {
            _shown = band;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Font font = GetThemeFont("font", "DimLabel");
        float gap = 2;
        float width = (Size.X - gap * (Bands.Length - 1)) / Bands.Length;
        for (int i = 0; i < Bands.Length; i++)
        {
            var band = new Rect2(i * (width + gap), 0, width, 12);
            // filled up to the fight's band; the last filled one in the colour that says how it went
            Color fill = i > _shown ? Palette.Dusk : _shown == 3 ? Palette.Red : _shown == 0 ? Palette.Ash : Palette.Blue;
            DrawRect(band, fill);
            DrawString(font, new Vector2(band.Position.X, 30), Bands[i], HorizontalAlignment.Left, width, 12, i == _shown ? Palette.Bone : Palette.Ash);
        }
    }
}
