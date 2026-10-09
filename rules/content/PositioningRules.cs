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
    /// <summary>A condition the attacker has for the roll when its target can't see it (it stands in the dark).</summary>
    public string UnseenAttackerCondition { get; init; } = "";
    /// <summary>A condition the attacker has for the roll when it can't see its target.</summary>
    public string UnseenTargetCondition { get; init; } = "";
    /// <summary>A condition for a shot past the weapon's range, out to its longRange (5e: disadvantage).</summary>
    public string LongRangeCondition { get; init; } = "";
    /// <summary>A condition for a ranged attack made with a foe beside the attacker (5e: disadvantage).</summary>
    public string RangedNearFoeCondition { get; init; } = "";

    public static PositioningRules Read(ContentNode node)
    {
        node.RequireObject("positioning rules are a JSON object");
        node.Only("enabled", "flankingCondition", "flankingReach", "halfCoverArmorClass", "threeQuartersCoverArmorClass",
            "creaturesProvideCover", "coverAgainstMelee", "unseenAttackerCondition", "unseenTargetCondition", "longRangeCondition", "rangedNearFoeCondition");
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
            UnseenAttackerCondition = node.Text("unseenAttackerCondition", "", 64),
            UnseenTargetCondition = node.Text("unseenTargetCondition", "", 64),
            LongRangeCondition = node.Text("longRangeCondition", "", 64),
            RangedNearFoeCondition = node.Text("rangedNearFoeCondition", "", 64),
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
        foreach ((string field, string id) in new[]
        {
            ("flankingCondition", FlankingCondition), ("unseenAttackerCondition", UnseenAttackerCondition), ("unseenTargetCondition", UnseenTargetCondition),
            ("longRangeCondition", LongRangeCondition), ("rangedNearFoeCondition", RangedNearFoeCondition),
        })
        {
            if (id.Length > 0 && rules.Condition(id) == null)
            {
                throw new ContentException(file, field, $"unknown condition \"{id}\"");
            }
        }
    }
}
