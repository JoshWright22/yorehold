namespace Yorehold.Rules;

/// <summary>stealth.json: the numbers sneaking runs on. Every field is optional, and so is the file.</summary>
public class StealthRules
{
    /// <summary>Metres a sneaking hero moves inside a vision cone between checks.</summary>
    public double CheckEvery { get; init; } = 5;
    public double SneakSpeed { get; init; } = 0.5;
    public int DarkBonus { get; init; } = 5;
    public int DimBonus { get; init; } = 2;
    public int BrightBonus { get; init; }
    public bool Critical { get; init; } = true;

    public int LightBonus(LightLevel level) => level switch
    {
        LightLevel.Dark => DarkBonus,
        LightLevel.Dim => DimBonus,
        _ => BrightBonus,
    };

    public static StealthRules Read(ContentNode node)
    {
        node.RequireObject("stealth rules are a JSON object");
        double checkEvery = node.Number("checkEvery", 5, 0, 10000);
        double sneakSpeed = node.Number("sneakSpeed", 0.5, 0, 1);
        if (checkEvery <= 0)
        {
            throw node.Fail("checkEvery", "is above 0 and at most 10000");
        }
        if (sneakSpeed <= 0)
        {
            throw node.Fail("sneakSpeed", "is above 0 and at most 1");
        }
        return new StealthRules
        {
            CheckEvery = checkEvery,
            SneakSpeed = sneakSpeed,
            DarkBonus = node.Int("darkBonus", 5, -20, 20),
            DimBonus = node.Int("dimBonus", 2, -20, 20),
            BrightBonus = node.Int("brightBonus", 0, -20, 20),
            Critical = node.Bool("critical", true),
        };
    }
}
