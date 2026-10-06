namespace Yorehold.Rules;

/// <summary>positioning.json: flanking and cover. A ruleset without the file has neither.</summary>
public class PositioningRules
{
    public bool Enabled { get; init; }
    public string FlankingCondition { get; init; } = "";
    public double FlankingReach { get; init; } = 1;
    public int HalfCoverArmorClass { get; init; }
    public int ThreeQuartersCoverArmorClass { get; init; }
    public bool CreaturesProvideCover { get; init; }
    public bool CoverAgainstMelee { get; init; }

    public static PositioningRules Read(ContentNode node)
    {
        node.RequireObject("positioning rules are a JSON object");
        node.Only("enabled", "flankingCondition", "flankingReach", "halfCoverArmorClass", "threeQuartersCoverArmorClass",
            "creaturesProvideCover", "coverAgainstMelee");
        double reach = node.Number("flankingReach", 1, 0, 100);
        if (reach <= 0)
        {
            throw node.Fail("flankingReach", "is above 0 and at most 100");
        }
        int half = node.Int("halfCoverArmorClass", 0, 0, 100);
        int threeQuarters = node.Int("threeQuartersCoverArmorClass", 0, 0, 100);
        if (threeQuarters < half)
        {
            throw node.Fail("threeQuartersCoverArmorClass", "is at least halfCoverArmorClass");
        }
        return new PositioningRules
        {
            // A file that is there means positioning is on unless it says otherwise.
            Enabled = node.Bool("enabled", true),
            FlankingCondition = node.Text("flankingCondition", "", 64),
            FlankingReach = reach,
            HalfCoverArmorClass = half,
            ThreeQuartersCoverArmorClass = threeQuarters,
            CreaturesProvideCover = node.Bool("creaturesProvideCover", false),
            CoverAgainstMelee = node.Bool("coverAgainstMelee", false),
        };
    }

    /// <summary>What cover adds to armour class against an attack. Melee ignores cover unless the file says otherwise.</summary>
    public int CoverArmorClass(Cover cover, bool ranged)
    {
        if (!Enabled || (!ranged && !CoverAgainstMelee))
        {
            return 0;
        }
        return cover switch
        {
            Cover.Half => HalfCoverArmorClass,
            Cover.ThreeQuarters => ThreeQuartersCoverArmorClass,
            _ => 0,
        };
    }

    public void Check(Ruleset rules, string file)
    {
        if (FlankingCondition.Length > 0 && rules.Condition(FlankingCondition) == null)
        {
            throw new ContentException(file, "flankingCondition", $"unknown condition \"{FlankingCondition}\"");
        }
    }
}
