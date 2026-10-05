namespace Yorehold.Rules;

/// <summary>An area in world units.</summary>
public readonly record struct Rect(float X, float Y, float W, float H)
{
    public bool Contains(System.Numerics.Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;

    /// <summary>True when the two share some area, not only an edge.</summary>
    public bool Overlaps(Rect o)
    {
        float left = MathF.Max(X, o.X), top = MathF.Max(Y, o.Y);
        float right = MathF.Min(X + W, o.X + o.W), bottom = MathF.Min(Y + H, o.Y + o.H);
        return right - left > 0 && bottom - top > 0;
    }
}

public enum Interaction
{
    Missing,
    Unavailable,
    Locked,
    Opened,
    Closed,
    Activated,
}

/// <summary>
/// A door, lever, chest or trap as it is now in a world: the map file's object plus what has
/// happened to it (opened, unlocked, found, sprung, emptied). Ids count from 1 in map order.
/// </summary>
public sealed class WorldObject
{
    public WorldObject(int id, MapObject source)
    {
        Id = id;
        Source = source;
        Area = new Rect((float)source.X, (float)source.Y, (float)source.Width, (float)source.Height);
        HasDoor = source.Door != null;
        Open = source.Door?.Open ?? false;
        Locked = source.Door?.Locked ?? false;
        Destroyed = source.Destroyed;
        TrapArmed = source.Trap?.Armed ?? false;
        TrapFound = source.Trap?.Found ?? false;
        Contents = new SortedDictionary<string, int>(source.Contents, StringComparer.Ordinal);
    }

    public int Id { get; }
    /// <summary>The object as the map file has it.</summary>
    public MapObject Source { get; }
    public string Name => Source.Name;
    public Rect Area { get; }
    public int Floor => Source.Floor;
    public SortedSet<string> Tags => Source.Tags;
    public ObjectLock? Lock => Source.Lock;
    public ObjectTrap? Trap => Source.Trap;

    public bool HasDoor { get; }
    public bool Open { get; set; }
    /// <summary>A door or lid that is locked shut.</summary>
    public bool Locked { get; set; }
    public bool Destroyed { get; set; }
    public bool TrapArmed { get; set; }
    public bool TrapFound { get; set; }
    public SortedDictionary<string, int> Contents { get; }

    public bool Has(string tag) => Tags.Contains(tag);
    public bool BlocksMovement => !Destroyed && (!HasDoor || !Open) && Has("blocksMovement");
    public bool BlocksSight => !Destroyed && (!HasDoor || !Open) && Has("blocksSight");
    public bool IsLocked => HasDoor && Locked;
    public bool ArmedTrap => !Destroyed && Trap != null && TrapArmed;
}
