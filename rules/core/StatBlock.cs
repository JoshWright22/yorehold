namespace Yorehold.Rules;

/// <summary>A modifier on a sheet and what put it there; it comes off by that source.</summary>
public readonly record struct AppliedModifier(Modifier Modifier, string Source);

/// <summary>
/// Named numbers ("str", "ac", "speed"...) with a base value and modifiers on top. The value is
/// the highest override if there is one, else the base, plus every add, times every multiply.
/// The sums are done in float like the C++ client so both round the same way.
/// </summary>
public sealed class StatBlock
{
    private readonly SortedDictionary<string, float> _bases = new(StringComparer.Ordinal);
    private readonly List<AppliedModifier> _modifiers = new();

    public IReadOnlyDictionary<string, float> Bases => _bases;
    public IReadOnlyList<AppliedModifier> Modifiers => _modifiers;

    public void SetBase(string stat, float value)
    {
        _bases[stat] = value;
    }

    public float Base(string stat)
    {
        return _bases.TryGetValue(stat, out float value) ? value : 0;
    }

    public float Value(string stat)
    {
        float add = 0;
        float multiply = 1;
        float? replaced = null;
        // typed adds: the best bonus and the worst penalty of each type
        Dictionary<string, (float Best, float Worst)>? typed = null;
        foreach (AppliedModifier applied in _modifiers)
        {
            Modifier modifier = applied.Modifier;
            if (modifier.Stat != stat)
            {
                continue;
            }
            float value = (float)modifier.Value;
            switch (modifier.Op)
            {
            case ModifierOp.Add when modifier.Type.Length > 0:
                typed ??= new Dictionary<string, (float, float)>(StringComparer.Ordinal);
                (float best, float worst) = typed.GetValueOrDefault(modifier.Type);
                typed[modifier.Type] = (MathF.Max(best, value), MathF.Min(worst, value));
                break;
            case ModifierOp.Add:
                add += value;
                break;
            case ModifierOp.Multiply:
                multiply *= value;
                break;
            default:
                replaced = MathF.Max(replaced ?? value, value);
                break;
            }
        }
        foreach ((float best, float worst) in typed?.Values ?? Enumerable.Empty<(float, float)>())
        {
            add += best + worst;
        }
        return ((replaced ?? Base(stat)) + add) * multiply;
    }

    /// <summary>Rounded down, with a hair of slack so 14.9999 from a multiply still reads 15.</summary>
    public int Integer(string stat)
    {
        return (int)MathF.Floor(Value(stat) + 0.0001f);
    }

    public void AddModifier(Modifier modifier, string source)
    {
        _modifiers.Add(new AppliedModifier(modifier, source));
    }

    public void RemoveSource(string source)
    {
        _modifiers.RemoveAll(applied => applied.Source == source);
    }
}
