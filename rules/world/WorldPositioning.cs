namespace Yorehold.Rules;

// Flanking and cover in a fight, from where everyone stands now.
public sealed partial class World
{
    /// <summary>Two standing foes that can act are on opposite sides of it, with nothing between them and it.</summary>
    public bool IsFlanked(int creature)
    {
        PositioningRules positioning = Chapter.Rules.Positioning;
        if (!positioning.Enabled || positioning.FlankingCondition.Length == 0 || creature < 0 || creature >= Creatures.Count
            || !Fighting || Creatures[creature].Sheet.Down)
        {
            return false;
        }
        var foes = new List<Cell>();
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (OrderIndex(i) is int index && Encounter!.Order[index].Standing && Creatures[i].Team != Creatures[creature].Team
                && !Creatures[i].Sheet.HasFlag(Rules, "cantAct"))
            {
                foes.Add(CellOf(i));
            }
        }
        return Positioning.IsFlanked(Grid, CellOf(creature), foes, (float)positioning.FlankingReach,
            (a, b) => !Sight.LineOfSight(a, b, Map.Walls));
    }

    /// <summary>Cover the target has from an attacker: walls first, else the creatures standing in between.</summary>
    public Cover CoverFrom(int from, int target)
    {
        if (from < 0 || from >= Creatures.Count || target < 0 || target >= Creatures.Count)
        {
            return Cover.None;
        }
        var bodies = new List<Cell>();
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (i != from && i != target && !Creatures[i].Sheet.Down && Tokens.Tokens[i].Floor == 0 && !Creatures[i].Fled)
            {
                bodies.Add(CellOf(i));
            }
        }
        return Positioning.CoverFrom(Grid, CellOf(from), CellOf(target), Map.Walls, bodies, Chapter.Rules.Positioning);
    }

    /// <summary>
    /// The conditions the place gives an attacker for one roll at target, as positioning.json
    /// names them: unseen by its target (it stands in the dark beyond the target's darkvision),
    /// or unable to see the target.
    /// </summary>
    public List<string> PlaceConditions(int attacker, int target)
    {
        var place = new List<string>();
        PositioningRules rules = Chapter.Rules.Positioning;
        if (attacker < 0 || target < 0 || attacker >= Creatures.Count || target >= Creatures.Count || !rules.Enabled)
        {
            return place;
        }
        if (rules.UnseenAttackerCondition.Length > 0 && !SeesInTheLight(target, attacker))
        {
            place.Add(rules.UnseenAttackerCondition);
        }
        if (rules.UnseenTargetCondition.Length > 0 && !SeesInTheLight(attacker, target))
        {
            place.Add(rules.UnseenTargetCondition);
        }
        return place;
    }

    // Whether viewer can make seen out: it stands in light, or within viewer's darkvision.
    private bool SeesInTheLight(int viewer, int seen)
    {
        if (LightAt(Tokens.Tokens[seen].Position) != LightLevel.Dark)
        {
            return true;
        }
        float perSquare = Math.Max(1, Rules.FeetPerSquare);
        float darkvision = Creatures[viewer].Sheet.Stats.Value("darkvision") / perSquare;
        return Grid.Distance(CellOf(viewer), CellOf(seen)) <= darkvision + 0.01f;
    }

    /// <summary>Its armour class with flanking, which counts as the flanking condition without staying on the sheet.</summary>
    public int PositionalArmorClass(int target, string defence = "")
    {
        if (target < 0 || target >= Creatures.Count)
        {
            return 0;
        }
        // the defence the attack names, else the one the system's attacks are rolled against
        string id = defence.Length > 0 ? defence : Rules.Checks.Kind(CheckRules.Attack).DefenceId;
        CharacterSheet sheet = Creatures[target].Sheet;
        string flanking = Chapter.Rules.Positioning.FlankingCondition;
        if (!IsFlanked(target) || sheet.HasCondition(flanking))
        {
            return sheet.Defence(Rules, id);
        }
        CharacterSheet shown = sheet.Copy();
        shown.AddCondition(Rules, flanking);
        return shown.Defence(Rules, id);
    }

    /// <summary>What an attack from one creature on another has to reach: flanking, and cover for ranged attacks.</summary>
    public int AttackArmorClass(int from, int target, bool ranged, string defence = "")
    {
        return PositionalArmorClass(target, defence) +Chapter.Rules.Positioning.CoverArmorClass(CoverFrom(from, target), ranged);
    }
}
