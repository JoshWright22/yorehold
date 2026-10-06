using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// The player's settings file, settings.json, with the C++ client's field names so one file works
/// in both. Reading never fails: a missing, broken or wrong-typed value keeps its default, since
/// settings must not stop the game from starting. Fields this port has no use for yet (the co-op
/// name and address, the server, the skin) are kept as they were read and written back.
/// </summary>
public sealed class GameSettings
{
    public const float PanSpeedMin = 200;
    public const float PanSpeedMax = 3000;
    public const float PanSpeedStep = 100;

    public static readonly string[] LightingNames = { "map", "off", "mood", "rules" };
    public static readonly string[] TimeNames = { "map", "day", "dusk", "night" };

    /// <summary>The wheel zooms toward the pointer; off zooms on the middle of the screen.</summary>
    public bool ZoomToCursor { get; set; } = true;
    /// <summary>The view pans while the pointer rests on a screen edge.</summary>
    public bool EdgeScroll { get; set; } = true;
    /// <summary>The view goes with whoever is walking or acting.</summary>
    public bool CameraFollows { get; set; } = true;
    public float PanSpeed { get; set; } = 900;
    public bool Fullscreen { get; set; }
    /// <summary>0 = as the map says, else 1 + LightingMode. Goes into WorldOptions as it is.</summary>
    public int Lighting { get; set; }
    /// <summary>0 = as the map says, else 1 + MapTime (day, dusk, night).</summary>
    public int TimeOfDay { get; set; }
    public bool SharedFog { get; set; } = true;
    /// <summary>A hero's reaction asks first. On unless turned off, as the fight screen was built (the C++ client shipped it off).</summary>
    public bool ReactionPrompts { get; set; } = true;
    /// <summary>The package folder Create had open last.</summary>
    public string LastCreatePackage { get; set; } = "";
    /// <summary>Key bindings that differ from the shipped ones: action id to key names.</summary>
    public Dictionary<string, List<string>> Keys { get; } = new();

    // what the file held that this port doesn't read
    private JsonObject _others = new();

    /// <summary>Settings from the file's text. Anything wrong in it is left at its default.</summary>
    public static GameSettings Parse(string text)
    {
        var settings = new GameSettings();
        JsonObject? j;
        try
        {
            j = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return settings;
        }
        if (j == null)
        {
            return settings;
        }

        settings.ZoomToCursor = Bool(j, "zoomToCursor", settings.ZoomToCursor);
        settings.EdgeScroll = Bool(j, "edgeScroll", settings.EdgeScroll);
        settings.CameraFollows = Bool(j, "cameraFollows", settings.CameraFollows);
        settings.Fullscreen = Bool(j, "fullscreen", settings.Fullscreen);
        settings.SharedFog = Bool(j, "sharedFog", settings.SharedFog);
        settings.ReactionPrompts = Bool(j, "reactionPrompts", settings.ReactionPrompts);
        if (j["panSpeed"] is JsonValue speed && speed.GetValueKind() == JsonValueKind.Number)
        {
            float value = (float)speed.GetValue<double>();
            settings.PanSpeed = float.IsFinite(value) ? Math.Clamp(value, PanSpeedMin, PanSpeedMax) : settings.PanSpeed;
        }
        settings.Lighting = Math.Max(0, Array.IndexOf(LightingNames, Text(j, "lighting", "map")));
        settings.TimeOfDay = Math.Max(0, Array.IndexOf(TimeNames, Text(j, "timeOfDay", "map")));
        settings.LastCreatePackage = Text(j, "lastCreatePackage", "");
        if (j["keys"] is JsonObject keys)
        {
            foreach (KeyValuePair<string, JsonNode?> binding in keys)
            {
                if (binding.Value is not JsonArray names)
                {
                    continue;
                }
                var list = new List<string>();
                foreach (JsonNode? name in names)
                {
                    if (name is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>().Length is > 0 and <= 40)
                    {
                        list.Add(value.GetValue<string>());
                    }
                }
                settings.Keys[binding.Key] = list;
            }
        }

        foreach (string known in Known)
        {
            j.Remove(known);
        }
        settings._others = j;
        return settings;
    }

    /// <summary>The settings in a file; the defaults when there is none or it can't be read.</summary>
    public static GameSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new GameSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new GameSettings();
        }
    }

    public string ToJson()
    {
        var j = new JsonObject
        {
            ["zoomToCursor"] = ZoomToCursor,
            ["edgeScroll"] = EdgeScroll,
            ["cameraFollows"] = CameraFollows,
            ["panSpeed"] = PanSpeed,
            ["fullscreen"] = Fullscreen,
            ["lighting"] = LightingNames[Math.Clamp(Lighting, 0, LightingNames.Length - 1)],
            ["timeOfDay"] = TimeNames[Math.Clamp(TimeOfDay, 0, TimeNames.Length - 1)],
            ["sharedFog"] = SharedFog,
            ["reactionPrompts"] = ReactionPrompts,
            ["lastCreatePackage"] = LastCreatePackage,
        };
        var keys = new JsonObject();
        foreach (KeyValuePair<string, List<string>> binding in Keys.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            keys[binding.Key] = new JsonArray(binding.Value.Select(k => (JsonNode)JsonValue.Create(k)).ToArray());
        }
        j["keys"] = keys;
        foreach (KeyValuePair<string, JsonNode?> other in _others)
        {
            j[other.Key] = other.Value?.DeepClone();
        }
        return j.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Writes the file through a temporary one, so a crash halfway leaves the old settings.</summary>
    public void Save(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ToJson());
        File.Move(temporary, path, true);
    }

    /// <summary>Pan speed a step up or down, kept inside its range.</summary>
    public void StepPanSpeed(int steps)
    {
        PanSpeed = Math.Clamp(MathF.Round(PanSpeed / PanSpeedStep) * PanSpeedStep + steps * PanSpeedStep, PanSpeedMin, PanSpeedMax);
    }

    private static readonly string[] Known =
    {
        "zoomToCursor", "edgeScroll", "cameraFollows", "panSpeed", "fullscreen", "lighting", "timeOfDay", "sharedFog",
        "reactionPrompts", "lastCreatePackage", "keys",
    };

    private static bool Bool(JsonObject j, string key, bool fallback)
    {
        return j[key] is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? value.GetValue<bool>() : fallback;
    }

    private static string Text(JsonObject j, string key, string fallback)
    {
        return j[key] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : fallback;
    }
}
