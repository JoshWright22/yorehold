namespace Yorehold.Rules;

/// <summary>
/// ui/sounds.json: which sound file plays at which moment, read at start. The moments are named
/// ("dice.throw", "dice.land", "fight.start", "fight.won", "fight.lost", "turn", and for each
/// animation set "&lt;set&gt;.hit", "&lt;set&gt;.miss" and "&lt;set&gt;.critical", falling back to plain
/// "hit", "miss" and "critical"); each names an .ogg, .wav or .mp3 from the content, a skin or an
/// art pack. The game ships no sounds of its own, so a moment with no file, or a file that isn't
/// there, is silent. A skin's file changes only what it names.
/// </summary>
public sealed class UiSounds
{
    public const string File = "ui/sounds.json";
    private static readonly string[] Kinds = { ".ogg", ".wav", ".mp3" };

    public Dictionary<string, string> Moments { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Loudness of every sound, in decibels added (0 as recorded, -6 half as loud).</summary>
    public double Volume { get; init; }

    /// <summary>The file for a moment, or for the plainer moment after the last dot ("slash.hit", then "hit"); "" for none.</summary>
    public string For(string moment)
    {
        if (Moments.TryGetValue(moment, out string? file) && file.Length > 0)
        {
            return file;
        }
        int dot = moment.LastIndexOf('.');
        return dot > 0 && Moments.TryGetValue(moment[(dot + 1)..], out string? plain) ? plain : "";
    }

    public static UiSounds Read(ContentNode node)
    {
        node.RequireObject("a sounds file is a JSON object");
        node.Only("format", "version", "about", "volume", "sounds");
        if (node.At("format").AsText() != "yorehold.sounds")
        {
            throw node.Fail("format", "is \"yorehold.sounds\"");
        }
        var moments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> moment in node.Get("sounds")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            if (moment.Key.Length == 0 || moment.Key.Length > 64 || !moment.Key.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
            {
                throw moment.Value.Fail("moments are named like \"dice.throw\" or \"slash.hit\"");
            }
            string file = moment.Value.AsText(200);
            if (file.Length > 0 && (file.Contains("..") || !Kinds.Any(kind => file.EndsWith(kind, StringComparison.OrdinalIgnoreCase))))
            {
                throw moment.Value.Fail("is a sound file in the content, like \"sounds/dice.ogg\" (.ogg, .wav or .mp3), or \"\" for none");
            }
            moments[moment.Key] = file;
        }
        return new UiSounds { Moments = moments, Volume = node.Number("volume", 0, -40, 12) };
    }
}
