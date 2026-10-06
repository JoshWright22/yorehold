using System.Numerics;

namespace Yorehold.Rules;

/// <summary>
/// Seeded random numbers (PCG32), the same generator as the C++ client: one seed gives the same
/// rolls on every machine. State and Increment are all of it, so a save keeps those two numbers
/// and Restore carries on from the same roll.
/// </summary>
public sealed class Rng
{
    public const ulong DefaultSeed = 0x853c49e6748fea9bUL;
    public const ulong DefaultStream = 0xda3e39cb94b95bdbUL;

    private ulong _state;
    private readonly ulong _increment;

    public Rng(ulong seed = DefaultSeed, ulong stream = DefaultStream)
    {
        _increment = (stream << 1) | 1;
        Next();
        unchecked
        {
            _state += seed;
        }
        Next();
    }

    private Rng(ulong state, ulong increment, bool restored)
    {
        _ = restored; // only tells this constructor from the seeding one
        _state = state;
        _increment = increment;
    }

    public ulong State => _state;
    public ulong Increment => _increment;

    /// <summary>Picks up where a saved generator stopped.</summary>
    public static Rng Restore(ulong state, ulong increment)
    {
        if ((increment & 1) == 0)
        {
            throw new ArgumentException("a saved increment is always odd", nameof(increment));
        }
        return new Rng(state, increment, true);
    }

    public uint Next()
    {
        ulong old = _state;
        unchecked
        {
            _state = old * 6364136223846793005UL + _increment;
        }
        uint shifted = (uint)(((old >> 18) ^ old) >> 27);
        return BitOperations.RotateRight(shifted, (int)(old >> 59));
    }

    /// <summary>A whole number from low to high, both included, with no modulo bias.</summary>
    public int Range(int low, int high)
    {
        if (low > high)
        {
            throw new ArgumentException("range is reversed");
        }
        ulong wide = (ulong)((long)high - low) + 1;
        if (wide == 1UL << 32)
        {
            return (int)(low + (long)Next());
        }
        uint span = (uint)wide;
        uint threshold = unchecked(0u - span) % span;
        uint value;
        do
        {
            value = Next();
        }
        while (value < threshold);
        return (int)(low + (long)(value % span));
    }
}
