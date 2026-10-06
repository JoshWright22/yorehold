namespace Yorehold.Rules;

public enum Degree
{
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess,
}

/// <summary>A d20 roll against a DC or an armour class.</summary>
public static class Checks
{
    /// <summary>
    /// A natural 1 is a critical failure and a natural 20 a critical success whatever the total;
    /// anything else passes when the total reaches the DC.
    /// </summary>
    public static Degree DegreeOf(RollResult roll, int dc)
    {
        if (roll.Natural1)
        {
            return Degree.CriticalFailure;
        }
        if (roll.Natural20)
        {
            return Degree.CriticalSuccess;
        }
        return roll.Total >= dc ? Degree.Success : Degree.Failure;
    }

    public static bool Passed(Degree degree) => degree >= Degree.Success;

    public static bool Critical(Degree degree) => degree is Degree.CriticalFailure or Degree.CriticalSuccess;
}
