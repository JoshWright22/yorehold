using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>One word of a recorded line.</summary>
public sealed record VoiceWord
{
    /// <summary>As written, punctuation kept.</summary>
    public string Text { get; init; } = "";
    /// <summary>Seconds into the recording.</summary>
    public double Start { get; init; }
    public double End { get; init; }
    /// <summary>The lowest token probability the model gave it; 1 for a word it didn't hear.</summary>
    public float Confidence { get; init; } = 1;
    /// <summary>Its timing was heard; false = spread between its neighbours.</summary>
    public bool Matched { get; init; } = true;
}

/// <summary>
/// A recorded line as words with timings: voice/name.voice.json beside the recording. Made once in
/// Create when the recording is imported; the game only reads it.
/// </summary>
public sealed record VoiceLine
{
    public const int Format = 1;

    /// <summary>The recording's file name, beside this file.</summary>
    public string Audio { get; init; } = "";
    /// <summary>"base.en", "tiny.en".</summary>
    public string Model { get; init; } = "";
    /// <summary>The line the words make up.</summary>
    public string Text { get; init; } = "";
    public List<VoiceWord> Words { get; init; } = new();

    /// <summary>A newer format, or a missing or broken field, is refused with the reason.</summary>
    public static VoiceLine? Read(string json, out string error)
    {
        error = "";
        VoiceLine? Fail(string why, out string message)
        {
            message = why;
            return null;
        }
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            parsed = null;
        }
        if (parsed is not JsonObject j)
        {
            return Fail("not a JSON object", out error);
        }
        if (!FormJson.IsInteger(j["format"]))
        {
            return Fail("no \"format\" number", out error);
        }
        int version = (int)FormJson.Number(j["format"]);
        if (version > Format)
        {
            return Fail($"made by a newer Yorehold (format {version}, this one reads {Format})", out error);
        }
        if (version < 1)
        {
            return Fail($"format {version} is not one Yorehold made", out error);
        }
        string audio = "", model = "", text = "";
        bool Text(string key, ref string into)
        {
            if (!j.ContainsKey(key))
            {
                return true;
            }
            if (!FormJson.IsString(j[key], out string? value))
            {
                return false;
            }
            into = value;
            return true;
        }
        if (!Text("audio", ref audio) || !Text("model", ref model) || !Text("text", ref text))
        {
            return Fail("\"audio\", \"model\" and \"text\" are strings", out error);
        }
        if (j["words"] is not JsonArray list)
        {
            return Fail("no \"words\" list", out error);
        }
        var words = new List<VoiceWord>();
        foreach (JsonNode? w in list)
        {
            string at = $"word {words.Count + 1}";
            if (w is not JsonObject o || !FormJson.IsString(o["text"], out string? wordText))
            {
                return Fail(at + " has no text", out error);
            }
            if (!FormJson.IsNumber(o["start"]) || !FormJson.IsNumber(o["end"]))
            {
                return Fail(at + " needs a start and an end in seconds", out error);
            }
            double start = FormJson.Number(o["start"]), end = FormJson.Number(o["end"]);
            if (start < 0 || end < start)
            {
                return Fail(at + " ends before it starts", out error);
            }
            float confidence = 1;
            if (o.ContainsKey("confidence"))
            {
                if (!FormJson.IsNumber(o["confidence"]))
                {
                    return Fail(at + ": confidence is a number from 0 to 1", out error);
                }
                confidence = (float)FormJson.Number(o["confidence"]);
            }
            bool matched = true;
            if (o.ContainsKey("matched"))
            {
                if (o["matched"]?.GetValueKind() is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False))
                {
                    return Fail(at + ": matched is true or false", out error);
                }
                matched = o["matched"]!.GetValue<bool>();
            }
            words.Add(new VoiceWord { Text = wordText, Start = start, End = end, Confidence = confidence, Matched = matched });
        }
        return new VoiceLine { Audio = audio, Model = model, Text = text, Words = words };
    }

    public string ToJson()
    {
        var words = new JsonArray();
        foreach (VoiceWord word in Words)
        {
            words.Add(new JsonObject
            {
                ["text"] = word.Text,
                ["start"] = Round(word.Start, 1000),
                ["end"] = Round(word.End, 1000),
                ["confidence"] = Round(word.Confidence, 100),
                ["matched"] = word.Matched,
            });
        }
        var j = new JsonObject { ["format"] = Format, ["audio"] = Audio, ["model"] = Model, ["text"] = Text, ["words"] = words };
        return CreateJson.Write(j) + "\n";
    }

    /// <summary>Made on demand, never stored.</summary>
    public string ToText() => CueText(0, Words.Count);

    public string ToSrt()
    {
        var text = new StringBuilder();
        int number = 1;
        foreach ((int from, int to) in Cues())
        {
            text.Append(number++).Append('\n').Append(Clock(Words[from].Start, ',')).Append(" --> ").Append(Clock(Words[to - 1].End, ',')).Append('\n')
                .Append(CueText(from, to)).Append("\n\n");
        }
        return text.ToString();
    }

    public string ToVtt()
    {
        var text = new StringBuilder("WEBVTT\n\n");
        foreach ((int from, int to) in Cues())
        {
            text.Append(Clock(Words[from].Start, '.')).Append(" --> ").Append(Clock(Words[to - 1].End, '.')).Append('\n').Append(CueText(from, to)).Append("\n\n");
        }
        return text.ToString();
    }

    /// <summary>Words heard less surely than the cutoff, for the editor to flag.</summary>
    public int LowConfidence(float cutoff) => Words.Count(w => w.Matched && w.Confidence < cutoff);

    // Seconds kept to milliseconds, a confidence to hundredths, whole numbers without ".0".
    private static JsonNode Round(double value, double by)
    {
        double rounded = Math.Round(value * by, MidpointRounding.AwayFromZero) / by;
        return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e9 ? JsonValue.Create((long)rounded) : JsonValue.Create(rounded);
    }

    private static string Clock(double seconds, char comma)
    {
        long ms = (long)Math.Round(Math.Max(0, seconds) * 1000, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{ms / 3600000:00}:{ms / 60000 % 60:00}:{ms / 1000 % 60:00}{comma}{ms % 1000:000}");
    }

    // Subtitle cues: at most 8 words or about 42 letters, broken after a sentence when one ends.
    private List<(int, int)> Cues()
    {
        var cues = new List<(int, int)>();
        int first = 0, letters = 0;
        for (int i = 0; i < Words.Count; i++)
        {
            letters += Words[i].Text.Length + 1;
            char last = Words[i].Text.Length == 0 ? ' ' : Words[i].Text[^1];
            bool sentence = last is '.' or '?' or '!' && i + 1 - first >= 3;
            if (i + 1 == Words.Count || i + 1 - first >= 8 || letters >= 42 || sentence)
            {
                cues.Add((first, i + 1));
                first = i + 1;
                letters = 0;
            }
        }
        return cues;
    }

    private string CueText(int from, int to) => string.Join(" ", Words.Skip(from).Take(to - from).Select(w => w.Text));
}
