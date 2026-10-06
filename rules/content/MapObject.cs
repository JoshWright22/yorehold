namespace Yorehold.Rules;

public record ObjectDoor(bool Open = false, bool Locked = false);

/// <summary>A lock on a door or lid. Dc 0 means only a key opens it.</summary>
public record ObjectLock(int Dc = 0, string Skill = "");

public record ObjectDurability(int Health, int Maximum);

public record ObjectLight(double X, double Y, double Radius, ContentColor Color, bool Shadows = true);

public class ObjectTrap
{
    public int DetectDc { get; init; } = 10;
    public int DisarmDc { get; init; } = 10;
    public string DetectSkill { get; init; } = "";
    public string DisarmSkill { get; init; } = "";
    /// <summary>What it does to whoever sets it off. Null = nothing.</summary>
    public Effect? Effect { get; init; }
    public bool Armed { get; init; } = true;
    public bool Found { get; init; }
    public bool Rearms { get; init; }
}

/// <summary>
/// A door, lever, chest or trap on a map. Areas are in world units, 64 to a cell, like the files
/// write them.
/// </summary>
public class MapObject
{
    public string Name { get; init; } = "";
    public string Texture { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 32;
    public double Height { get; init; } = 32;
    public int Floor { get; init; }
    public SortedSet<string> Tags { get; init; } = new(StringComparer.Ordinal);
    public double Weight { get; init; } = 1;
    public bool Destroyed { get; init; }
    /// <summary>Item ids with counts, and "coins" in copper.</summary>
    public Dictionary<string, int> Contents { get; init; } = new();
    public ObjectDoor? Door { get; init; }
    public ObjectDurability? Durability { get; init; }
    public ObjectLock? Lock { get; init; }
    public ObjectTrap? Trap { get; init; }
    public ObjectLight? Light { get; init; }

    public bool BlocksMovement => !Destroyed && (Door == null || !Door.Open) && Tags.Contains("blocksMovement");
    public bool BlocksSight => !Destroyed && (Door == null || !Door.Open) && Tags.Contains("blocksSight");

    public static MapObject Read(ContentNode node)
    {
        node.RequireObject("an object is a JSON object");
        double x = 0, y = 0, width = 32, height = 32;
        if (node.Get("area") is ContentNode area)
        {
            if (!area.IsArray || area.Count != 4)
            {
                throw area.Fail("needs [x, y, w, h]");
            }
            double[] parts = area.Items().Select(part => part.AsNumber()).ToArray();
            (x, y, width, height) = (parts[0], parts[1], parts[2], parts[3]);
            if (width <= 0 || height <= 0)
            {
                throw area.Fail("needs a width and height above 0");
            }
        }
        var tags = new SortedSet<string>(node.Texts("tags"), StringComparer.Ordinal);

        ObjectDoor? door = null;
        if (node.Get("door") is ContentNode d)
        {
            door = new ObjectDoor(d.Bool("open", false), d.Bool("locked", false));
            tags.Add("door");
        }
        ObjectDurability? durability = null;
        if (node.Get("durability") is ContentNode dur)
        {
            int maximum = dur.At("maximum").AsInt(1);
            durability = new ObjectDurability(dur.At("health").AsInt(0, maximum), maximum);
        }
        ObjectLock? objectLock = null;
        if (node.Get("lock") is ContentNode l)
        {
            objectLock = new ObjectLock(l.Int("dc", 0, 0), l.Text("skill", ""));
        }
        ObjectTrap? trap = null;
        if (node.Get("trap") is ContentNode t)
        {
            Effect? effect = null;
            if (t.Get("effect") is ContentNode e)
            {
                // Saved traps hold the effect as JSON text; authored ones write it in place.
                ContentNode source = e.IsString ? ContentNode.Parse(e.File, e.AsText()).Moved(e.Path) : e;
                effect = e.IsString && e.AsText().Length == 0 ? null : Effect.Read(source);
            }
            trap = new ObjectTrap
            {
                DetectDc = t.Int("detectDc", 10, 0),
                DisarmDc = t.Int("disarmDc", 10, 0),
                DetectSkill = t.Text("detectSkill", ""),
                DisarmSkill = t.Text("disarmSkill", ""),
                Effect = effect,
                Armed = t.Bool("armed", true),
                Found = t.Bool("found", false),
                Rearms = t.Bool("rearms", false),
            };
        }
        ObjectLight? light = null;
        if (node.Get("light") is ContentNode li)
        {
            ContentNode position = li.At("position");
            if (!position.IsArray || position.Count != 2)
            {
                throw position.Fail("is [x, y]");
            }
            double[] p = position.Items().Select(part => part.AsNumber()).ToArray();
            double radius = li.At("radius").AsNumber(0);
            if (radius <= 0)
            {
                throw li.Fail("radius", "is above 0");
            }
            light = new ObjectLight(p[0], p[1], radius, ContentParts.ColorFrom(li.At("color")), li.Bool("shadows", true));
        }
        return new MapObject
        {
            Name = node.Text("name", ""),
            Texture = node.Text("texture", ""),
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Floor = node.Int("floor", 0),
            Tags = tags,
            Weight = node.Number("weight", 1, 0),
            Destroyed = node.Bool("destroyed", false),
            Contents = ContentParts.NumbersFrom(node, "contents", 0, int.MaxValue, idKeys: false),
            Door = door,
            Durability = durability,
            Lock = objectLock,
            Trap = trap,
            Light = light,
        };
    }
}
