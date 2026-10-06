using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Voice lines in Dialogue mode: the package's voice files and the commands that change them. A
/// node's recording is voice/conversation.node.wav in the package, and its words with timings go
/// in voice/conversation.node.voice.json beside it. Every command goes on the shared history.
/// </summary>
public sealed class VoiceImporter
{
    /// <summary>From create/voice.json in the game's assets.</summary>
    public sealed record Settings
    {
        /// <summary>Words heard less surely than this are flagged.</summary>
        public float FlagBelow { get; init; } = 0.6f;
        /// <summary>Recordings looked for, in this order.</summary>
        public List<string> Extensions { get; init; } = new() { "wav", "ogg" };

        public static Settings? Read(string json, out string error)
        {
            error = "";
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
                error = "not a JSON object";
                return null;
            }
            var settings = new Settings();
            if (j.ContainsKey("flagBelow"))
            {
                if (!FormJson.IsNumber(j["flagBelow"]) || FormJson.Number(j["flagBelow"]) is < 0 or > 1)
                {
                    error = "flagBelow is a number from 0 to 1";
                    return null;
                }
                settings = settings with { FlagBelow = (float)FormJson.Number(j["flagBelow"]) };
            }
            if (j.ContainsKey("extensions"))
            {
                if (j["extensions"] is not JsonArray list || list.Any(e => !FormJson.IsString(e, out string? text) || text.Length == 0))
                {
                    error = "extensions is a list like [\"wav\", \"ogg\"]";
                    return null;
                }
                settings = settings with { Extensions = list.Select(e => e!.GetValue<string>()).ToList() };
            }
            return settings;
        }
    }

    public sealed class Line
    {
        /// <summary>"wren.hello".</summary>
        public string Stem = "";
        /// <summary>"voice/wren.hello.wav"; empty = no recording yet.</summary>
        public string Audio = "";
        public VoiceLine? Voice;
        /// <summary>Its voice file as read or written; empty = not on disk.</summary>
        public string Saved = "";
        /// <summary>Why its voice file can't be read.</summary>
        public string Error = "";
    }

    public sealed record Problem(string Text, bool Error = false);

    private readonly History _history;
    // by stem; never removed, the history points at them
    private readonly SortedDictionary<string, Line> _lines = new(StringComparer.Ordinal);

    public VoiceImporter(History history, Settings? settings = null)
    {
        _history = history;
        Options = settings ?? new Settings();
    }

    public Settings Options { get; }

    public static string Stem(string conversation, string node) => conversation + "." + node;

    public static string VoicePath(string stem) => "voice/" + stem + ".voice.json";

    /// <summary>A node's recording and voice file, read the first time. again reads them again, unless its voice file has changes that aren't saved.</summary>
    public Line LineOf(ContentFiles files, string stem, bool again = false)
    {
        if (_lines.TryGetValue(stem, out Line? known))
        {
            bool unsaved = known.Voice != null && known.Voice.ToJson() != known.Saved;
            if (!again || unsaved)
            {
                return known;
            }
        }
        if (!_lines.TryGetValue(stem, out Line? line))
        {
            _lines[stem] = line = new Line();
        }
        line.Stem = stem;
        line.Audio = Options.Extensions.Select(e => $"voice/{stem}.{e}").FirstOrDefault(files.Exists) ?? "";
        line.Voice = null;
        line.Saved = "";
        line.Error = "";
        if (files.Exists(VoicePath(stem)))
        {
            line.Voice = VoiceLine.Read(files.ReadText(VoicePath(stem)), out string error);
            if (line.Voice != null)
            {
                line.Saved = line.Voice.ToJson();
            }
            else
            {
                line.Error = $"{VoicePath(stem)}: {error}";
            }
        }
        return line;
    }

    public Line? Find(string stem) => _lines.GetValueOrDefault(stem);

    /// <summary>An import's result, as one undo step.</summary>
    public bool SetVoice(string stem, VoiceLine voice)
    {
        if (stem.Length == 0)
        {
            return false;
        }
        Put(stem, "Import voice line", voice);
        return true;
    }

    /// <summary>
    /// Lines the words up with the written line again, without listening again: for a line the
    /// writer changed, or a name fixed ("Carlos" to "Kharos"). False if there is nothing to change.
    /// </summary>
    public bool Match(string stem, string written)
    {
        if (Find(stem) is not { Voice: VoiceLine voice } || VoiceWords.Split(written).Count == 0)
        {
            return false;
        }
        // only the words whose timing was heard: guessed ones are guessed again
        List<VoiceWord> heard = voice.Words.Where(w => w.Matched).ToList();
        VoiceLine matched = voice with { Words = VoiceWords.Match(heard, written, voice.Words.Count == 0 ? 0 : voice.Words[^1].End), Text = written };
        if (matched.ToJson() == voice.ToJson())
        {
            return false;
        }
        Put(stem, "Match voice to the line", matched);
        return true;
    }

    /// <summary>The written line and the voice's words differ.</summary>
    public static bool Differs(VoiceLine voice, string written)
    {
        static List<string> Normalized(string text) => VoiceWords.Split(text).Select(VoiceWords.Normalize).Where(w => w.Length > 0).ToList();
        return !Normalized(written).SequenceEqual(Normalized(string.Join(" ", voice.Words.Select(w => w.Text))));
    }

    /// <summary>What is worth a look in one line against the node's written text.</summary>
    public List<Problem> Problems(string stem, string written)
    {
        var found = new List<Problem>();
        if (Find(stem) is not Line line)
        {
            return found;
        }
        if (line.Error.Length > 0)
        {
            found.Add(new Problem(line.Error, true));
        }
        if (line.Voice is not VoiceLine voice)
        {
            if (line.Audio.Length > 0)
            {
                found.Add(new Problem(line.Audio + " isn't imported yet"));
            }
            return found;
        }
        if (line.Audio.Length == 0)
        {
            found.Add(new Problem(VoicePath(stem) + " has no recording beside it"));
        }
        if (VoiceWords.Split(written).Count > 0 && Differs(voice, written))
        {
            found.Add(new Problem(stem + ": the line changed since its voice was matched"));
        }
        int low = voice.LowConfidence(Options.FlagBelow);
        if (low > 0)
        {
            found.Add(new Problem($"{stem}: {low} {(low == 1 ? "word" : "words")} heard unsurely"));
        }
        int guessed = voice.Words.Count(w => !w.Matched);
        if (guessed > 0)
        {
            found.Add(new Problem($"{stem}: {guessed} {(guessed == 1 ? "word" : "words")} not heard as written, timed by guess"));
        }
        return found;
    }

    /// <summary>Voice files changed since they were read or written: path, then text.</summary>
    public List<(string Path, string Text)> Changed()
    {
        var found = new List<(string, string)>();
        foreach ((string stem, Line line) in _lines)
        {
            if (line.Voice != null && line.Voice.ToJson() is string text && text != line.Saved)
            {
                found.Add((VoicePath(stem), text));
            }
        }
        return found;
    }

    public void MarkSaved(string path)
    {
        foreach ((string stem, Line line) in _lines)
        {
            if (line.Voice != null && VoicePath(stem) == path)
            {
                line.Saved = line.Voice.ToJson();
            }
        }
    }

    /// <summary>The names in the package's voice/vocabulary.txt, one per line, given to the model as a hint.</summary>
    public static List<string> Vocabulary(ContentFiles files)
    {
        if (!files.Exists("voice/vocabulary.txt"))
        {
            return new List<string>();
        }
        // ReadText checks the path; the file isn't JSON, which is fine for reading it as text
        return files.ReadText("voice/vocabulary.txt").Split('\n').Select(l => l.Trim(' ', '\t', '\r')).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
    }

    private void Put(string stem, string label, VoiceLine? voice)
    {
        if (!_lines.TryGetValue(stem, out Line? line))
        {
            _lines[stem] = line = new Line { Stem = stem };
        }
        VoiceLine? before = line.Voice;
        string errorBefore = line.Error;
        _history.Perform(label, () =>
        {
            line.Voice = voice;
            line.Error = "";
        }, () =>
        {
            line.Voice = before;
            line.Error = errorBefore;
        });
    }
}
