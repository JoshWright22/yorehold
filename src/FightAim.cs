using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// What the player is pointing at in a fight, worked out once a frame by FightControl and read by
/// everything that draws it: the hotbar, the marks on the ground, the rings on tokens.
/// </summary>
public sealed class FightAim
{
    /// <summary>It is a hero's turn and the player can act: no reaction waiting, no cutscene.</summary>
    public bool HeroTurn { get; set; }
    /// <summary>The action picked on the hotbar and waiting for a target; empty for none.</summary>
    public string Action { get; set; } = "";
    /// <summary>The map cell under the pointer; null over the hud or off the map.</summary>
    public Cell? Hover { get; set; }
    /// <summary>The creature a click would act on.</summary>
    public int? Target { get; set; }
    /// <summary>The creature under the pointer, whatever a click would do.</summary>
    public int? Hovered { get; set; }
    /// <summary>An action aimed at a square: who its area would land on there.</summary>
    public List<int> Hit { get; } = new();
    /// <summary>An action aimed at a square can't go where the pointer is.</summary>
    public bool AimBad { get; set; }
    /// <summary>Where a click on Hover would walk, from the square stood on.</summary>
    public List<Cell> Path { get; } = new();
    /// <summary>Squares of movement that walk would use.</summary>
    public float PathCost { get; set; }
    /// <summary>Words at the pointer: the chance to hit, or what a move costs.</summary>
    public string Label { get; set; } = "";
    public Vector2 LabelAt { get; set; }
    /// <summary>The label says why not, and is drawn in red.</summary>
    public bool LabelBad { get; set; }

    public void ClearHover()
    {
        Hover = null;
        Target = null;
        Hovered = null;
        Hit.Clear();
        AimBad = false;
        Path.Clear();
        PathCost = 0;
        Label = "";
        LabelBad = false;
    }
}
