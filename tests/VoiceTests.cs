namespace Yorehold.Rules.Tests;

/// <summary>
/// Voice lines: the voice file, joining the model's tokens into words, matching them to the
/// written line, decoding a recording, the importer's commands and undo, and an import saved from
/// Create. No speech model runs here; a stand-in hears fixed words.
/// </summary>
public class VoiceTests
{
    private static VoiceWord Heard(string text, double start, double end, float confidence = 0.9f) => new() { Text = text, Start = start, End = end, Confidence = confidence };

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.0005;

    // A WAV file: 16-bit samples of a 440 Hz tone.
    private static byte[] Wav(int rate, int channels, double seconds)
    {
        int frames = (int)(rate * seconds);
        int data = frames * channels * 2;
        var bytes = new List<byte>();
        void Text(string s) => bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(s));
        void U32(uint v) => bytes.AddRange(BitConverter.GetBytes(v));
        void U16(ushort v) => bytes.AddRange(BitConverter.GetBytes(v));
        Text("RIFF");
        U32((uint)(36 + data));
        Text("WAVE");
        Text("fmt ");
        U32(16);
        U16(1);
        U16((ushort)channels);
        U32((uint)rate);
        U32((uint)(rate * channels * 2));
        U16((ushort)(channels * 2));
        U16(16);
        Text("data");
        U32((uint)data);
        for (int f = 0; f < frames; f++)
        {
            short sample = (short)(Math.Sin(f * 2 * Math.PI * 440 / rate) * 8000);
            for (int c = 0; c < channels; c++)
            {
                U16((ushort)sample);
            }
        }
        return bytes.ToArray();
    }

    // Hears the same words in anything, and says how long what it was given is.
    private sealed class FixedEars : IVoiceTranscriber
    {
        private readonly List<VoiceWord> _words;

        public FixedEars(params VoiceWord[] words) => _words = words.ToList();

        public string Model => "fixed";
        public int Given { get; private set; }
        public string Prompted { get; private set; } = "";

        public List<VoiceWord>? Transcribe(float[] samples, string prompt, out string error)
        {
            error = "";
            Given = samples.Length;
            Prompted = prompt;
            return _words.ToList();
        }
    }

    [Fact]
    public void Words()
    {
        Assert.True(VoiceWords.Normalize("Kharos,") == "kharos" && VoiceWords.Normalize("Don't!") == "don't" && VoiceWords.Normalize("'Tis") == "tis" && VoiceWords.Normalize("--").Length == 0,
            "Words are compared without case or punctuation");
        List<string> split = VoiceWords.Split("  Halt!  Who goes\nthere? ");
        Assert.True(split.Count == 4 && split[0] == "Halt!" && split[3] == "there?", "A line splits on spaces and keeps its punctuation");

        List<VoiceWord> joined = VoiceWords.JoinTokens(new HeardToken[]
        {
            new(" Halt", 0.9f, 0.0, 0.3, 0.12), new("!", 0.4f, 0.3, 0.35, 0.3), new(" Who", 0.95f, 0.5, 0.7, 0.62),
            new(" go", 0.8f, 0.7, 0.8), new("es", 0.7f, 0.8, 0.9), new(" -", 0.9f, 0.9, 0.95), new(" there", 0.9f, 1.0, 1.3, 1.05),
        }, 1.6);
        Assert.True(joined.Count == 4 && joined[0].Text == "Halt!" && joined[2].Text == "goes-" && joined[3].Text == "there", "Tokens join into words; punctuation goes with the word before it");
        Assert.True(Near(joined[0].Start, 0) && Near(joined[1].Start, 0.3) && Near(joined[2].Start, 0.62) && Near(joined[3].Start, 1.0),
            "A word starts at the DTW time of the token before it, else at its own first token's start");
        Assert.True(Near(joined[0].End, 0.3) && Near(joined[3].End, 1.6), "and ends where the next one starts");
        Assert.True(Near(joined[0].Confidence, 0.4) && Near(joined[2].Confidence, 0.7), "A word is as sure as its least sure token");
    }

    [Fact]
    public void Matching()
    {
        List<VoiceWord> same = VoiceWords.Match(new[] { Heard("halt", 0.1, 0.4), Heard("who", 0.5, 0.7), Heard("goes", 0.7, 0.9), Heard("there", 0.9, 1.2) }, "Halt! Who goes there?", 1.5);
        Assert.True(same.Count == 4 && same[0].Text == "Halt!" && same[3].Text == "there?" && Near(same[1].Start, 0.5) && same[3].Matched, "The written words take the heard timings");

        List<VoiceWord> named = VoiceWords.Match(new[] { Heard("Carlos", 0.2, 0.6, 0.3f), Heard("waits", 0.6, 0.9) }, "Kharos waits.", 1);
        Assert.True(named.Count == 2 && named[0].Text == "Kharos" && named[0].Matched && Near(named[0].Start, 0.2) && Near(named[0].Confidence, 0.3),
            "A misheard name keeps the written spelling and the heard time");

        List<VoiceWord> missing = VoiceWords.Match(new[] { Heard("the", 0.0, 0.2), Heard("gate", 0.6, 0.9), Heard("is", 0.9, 1.0), Heard("open", 1.0, 1.4) }, "The north gate is open", 1.5);
        Assert.True(missing.Count == 5 && !missing[1].Matched && missing[1].Text == "north" && Near(missing[1].Start, 0.2) && Near(missing[1].End, 0.6),
            "A word not heard gets the time between its neighbours, flagged");
        Assert.True(missing[0].Matched && missing[2].Matched && Near(missing[2].Start, 0.6), "and the rest still match");

        List<VoiceWord> extra = VoiceWords.Match(new[] { Heard("uh", 0.0, 0.2), Heard("the", 0.2, 0.4), Heard("gate", 0.4, 0.8) }, "The gate.", 1);
        Assert.True(extra.Count == 2 && extra[0].Matched && Near(extra[0].Start, 0.2) && extra[1].Text == "gate.", "A heard word not written is left out");

        List<VoiceWord> pieces = VoiceWords.Match(new[] { Heard("car", 0.2, 0.4), Heard("los", 0.4, 0.7), Heard("waits", 0.7, 1.0) }, "Kharos waits", 1);
        Assert.True(pieces.Count == 2 && pieces[0].Matched && Near(pieces[0].Start, 0.2) && Near(pieces[0].End, 0.7), "A name heard in two pieces is one word");

        List<VoiceWord> other = VoiceWords.Match(new[] { Heard("banana", 0.2, 0.6) }, "Wren", 1);
        Assert.True(other.Count == 1 && !other[0].Matched && Near(other[0].Start, 0.2), "A heard word nothing like the written one keeps its time but is flagged");

        List<VoiceWord> cut = VoiceWords.Match(new[] { Heard("my", 0.0, 0.2), Heard("keep", 0.2, 0.5) }, "My keep. Well, the lord's keep.", 1.5);
        Assert.True(cut.Count == 6 && cut[1].Matched && Near(cut[1].Start, 0.2) && !cut[5].Matched, "A recording that stops early matches the first of two same words");
        Assert.True(Near(cut[2].Start, 0.5) && Near(cut[5].End, 1.5), "and the rest share what is left of it");

        List<VoiceWord> suggestion = VoiceWords.Match(new[] { Heard("hello", 0.1, 0.5) }, "  ", 1);
        Assert.True(suggestion.Count == 1 && suggestion[0].Text == "hello" && suggestion[0].Matched, "With no written line the heard words are kept");

        List<VoiceWord> silent = VoiceWords.Match(Array.Empty<VoiceWord>(), "Two words", 2);
        Assert.True(silent.Count == 2 && !silent[0].Matched && Near(silent[0].Start, 0) && Near(silent[1].End, 2), "Nothing heard spreads the line over the recording");
    }

    [Fact]
    public void File_()
    {
        const string text = "Halt! Who goes there?";
        var line = new VoiceLine
        {
            Audio = "wren.hello.wav",
            Model = "base.en",
            Text = text,
            Words = VoiceWords.Match(new[] { Heard("halt", 0.1004, 0.4), Heard("who", 0.5, 0.7, 0.42f), Heard("goes", 0.7, 0.9), Heard("there", 0.9, 1.25) }, text, 1.5),
        };
        string json = line.ToJson();
        VoiceLine? back = VoiceLine.Read(json, out string error);
        Assert.True(back != null && back.Audio == "wren.hello.wav" && back.Model == "base.en" && back.Words.Count == 4 && Near(back.Words[0].Start, 0.1)
            && Near(back.Words[1].Confidence, 0.42) && back.ToJson() == json, "A voice file reads back the same, times in milliseconds: " + error);
        Assert.Contains("\"format\": 1", json);
        Assert.True(VoiceLine.Read("""{"format": 2, "words": []}""", out error) == null && error.Contains("newer"), "A newer format is refused, saying so");
        Assert.True(VoiceLine.Read("""{"audio": "a.wav", "words": []}""", out error) == null && error.Contains("format"), "One with no format is refused");
        Assert.True(VoiceLine.Read("""{"format": 1, "words": [{"text": "a", "start": 2, "end": 1}]}""", out error) == null && error.Contains("word 1"),
            "A word that ends before it starts is refused, naming it");
        Assert.NotNull(VoiceLine.Read("""{"format": 1, "words": [{"text": "a", "start": 0, "end": 1}]}""", out _));

        Assert.Equal(text, line.ToText());
        Assert.StartsWith("1\n00:00:00,100 --> 00:00:01,250\nHalt! Who goes there?\n", line.ToSrt());
        Assert.StartsWith("WEBVTT\n\n00:00:00.100 --> 00:00:01.250\n", line.ToVtt());
        Assert.Equal(1, line.LowConfidence(0.6f));
    }

    [Fact]
    public void Decoding()
    {
        float[]? samples = VoiceAudio.Decode(Wav(44100, 2, 0.5), out string error);
        Assert.True(samples != null && samples.Length > 7900 && samples.Length < 8100, "A 44.1 kHz stereo WAV becomes 16 kHz mono: " + error);
        float loudest = samples!.Max(Math.Abs);
        Assert.True(loudest > 0.2f && loudest < 0.3f, "at the same loudness");
        Assert.True(VoiceAudio.Decode("nope"u8.ToArray(), out error) == null && error.Length > 0, "Something that isn't a recording is refused");

        var ears = new FixedEars(Heard("Carlos", 0.1, 0.4), Heard("waits", 0.4, 0.8));
        VoiceLine? line = VoiceTranscription.Transcribe(Wav(16000, 1, 1.0), "wren.hello.wav", "Kharos waits.", new[] { "Kharos", "Wren" }, ears, out error);
        Assert.True(line != null && line.Text == "Kharos waits." && line.Model == "fixed" && line.Audio == "wren.hello.wav" && line.Words[0].Text == "Kharos",
            "An import keeps the written line: " + error);
        Assert.True(ears.Given == 16000 && ears.Prompted == "Kharos, Wren.", "The listener gets the samples and the vocabulary as its prompt");
        VoiceLine? suggested = VoiceTranscription.Transcribe(Wav(16000, 1, 1.0), "a.wav", "", Array.Empty<string>(), ears, out _);
        Assert.Equal("Carlos waits", suggested?.Text);
        Assert.True(VoiceTranscription.Load(out error) == null && error.Length > 0 && !VoiceTranscription.BuiltIn, "Without a speech model the game says why");
    }

    [Fact]
    public void Importer()
    {
        using var scratch = new Scratch();
        string folder = scratch.Folder;
        Directory.CreateDirectory(Path.Combine(folder, "voice"));
        File.WriteAllBytes(Path.Combine(folder, "voice", "wren.hello.wav"), Wav(16000, 1, 1.0));
        File.WriteAllText(Path.Combine(folder, "voice", "vocabulary.txt"), "# names\nKharos\n  Tobb  \r\n\n");
        var old = new VoiceLine { Audio = "wren.bye.ogg", Model = "base.en", Text = "Go.", Words = new() { Heard("Go.", 0.1, 0.3) } };
        File.WriteAllText(Path.Combine(folder, "voice", "wren.bye.voice.json"), old.ToJson());
        File.WriteAllText(Path.Combine(folder, "voice", "wren.broken.voice.json"), "{");
        var files = new ContentFiles(folder);

        Assert.Equal(new[] { "Kharos", "Tobb" }, VoiceImporter.Vocabulary(files));
        VoiceImporter.Settings? settings = VoiceImporter.Settings.Read("""{"flagBelow": 0.5, "extensions": ["ogg"]}""", out string error);
        Assert.True(settings != null && Near(settings.FlagBelow, 0.5) && settings.Extensions.SequenceEqual(new[] { "ogg" }), "Settings read from data");
        Assert.True(VoiceImporter.Settings.Read("""{"flagBelow": 3}""", out error) == null && error.Length > 0, "A cutoff over 1 is refused");

        var history = new History();
        var voices = new VoiceImporter(history);
        Assert.True(VoiceImporter.Stem("wren", "hello") == "wren.hello" && VoiceImporter.VoicePath("wren.hello") == "voice/wren.hello.voice.json",
            "A node's voice is named after its conversation and node");
        VoiceImporter.Line hello = voices.LineOf(files, "wren.hello");
        Assert.True(hello.Audio == "voice/wren.hello.wav" && hello.Voice == null && hello.Error.Length == 0, "A recording with no voice file yet is found");
        Assert.Single(voices.Problems("wren.hello", "Hello."));
        VoiceImporter.Line bye = voices.LineOf(files, "wren.bye");
        Assert.True(bye.Audio.Length == 0 && bye.Voice?.Text == "Go.", "A voice file is read");
        Assert.Single(voices.Problems("wren.bye", "Go."));
        Assert.True(voices.LineOf(files, "wren.broken").Error.Length > 0 && voices.Problems("wren.broken", "")[0].Error, "A broken voice file is an error");
        Assert.Empty(voices.Changed());

        string written = "Well met, Kharos.";
        var imported = new VoiceLine
        {
            Audio = "wren.hello.wav",
            Model = "base.en",
            Text = written,
            Words = VoiceWords.Match(new[] { Heard("well", 0.1, 0.3), Heard("met", 0.3, 0.5), Heard("Carlos", 0.5, 0.9, 0.3f) }, written, 1),
        };
        Assert.True(voices.SetVoice("wren.hello", imported) && voices.Find("wren.hello")!.Voice!.Text == written, "An import is set");
        Assert.True(voices.Changed().Count == 1 && voices.Changed()[0].Path == "voice/wren.hello.voice.json", "and is a change to save");
        List<VoiceImporter.Problem> flagged = voices.Problems("wren.hello", written);
        Assert.True(flagged.Count == 1 && flagged[0].Text.Contains("unsurely"), "A word below the cutoff is flagged");
        history.Undo();
        Assert.True(voices.Find("wren.hello")!.Voice == null && voices.Changed().Count == 0, "Undo takes the import back");
        history.Redo();
        Assert.NotNull(voices.Find("wren.hello")!.Voice);

        // the writer changes the line; the words follow without listening again
        string longer = "Well met, Kharos of the keep.";
        Assert.True(VoiceImporter.Differs(voices.Find("wren.hello")!.Voice!, longer), "A changed line is noticed");
        Assert.True(voices.Match("wren.hello", longer), "Match lines up the words again");
        VoiceLine rematched = voices.Find("wren.hello")!.Voice!;
        Assert.True(rematched.Words.Count == 6 && rematched.Words[2].Text == "Kharos" && Near(rematched.Words[2].Start, 0.5) && !rematched.Words[4].Matched,
            "Kept words keep their times; new ones are guessed");
        Assert.False(voices.Match("wren.hello", longer), "Matching again changes nothing");
        history.Undo();
        Assert.Equal(written, voices.Find("wren.hello")!.Voice!.Text);

        // looking again doesn't drop unsaved work
        Assert.NotNull(voices.LineOf(files, "wren.hello", true).Voice);
        voices.MarkSaved("voice/wren.hello.voice.json");
        Assert.Empty(voices.Changed());

        // imports run off the main thread, here with a stand-in for the speech model
        var job = new VoiceImport();
        job.SetLoader((out string why) =>
        {
            why = "";
            return new FixedEars(Heard("hello", 0.2, 0.6));
        });
        Assert.True(!job.Start(voices, files, "wren.nobody", "Hi.") && job.Status.Length > 0, "No recording, no import, and it says why");
        Assert.True(job.Start(voices, files, "wren.hello", "Hello there.") && job.Busy, "An import starts");
        Assert.True(job.Finish(voices, true) && !job.Busy, "and finishes");
        VoiceLine done = voices.Find("wren.hello")!.Voice!;
        Assert.True(done.Model == "fixed" && done.Text == "Hello there." && done.Words.Count == 2 && done.Words[0].Matched && !done.Words[1].Matched,
            "Its result is the voice line, matched to the written one");
        history.Undo();
        Assert.Equal(written, voices.Find("wren.hello")!.Voice!.Text);

        var deaf = new VoiceImport();
        deaf.SetLoader((out string why) =>
        {
            why = "no model";
            return null;
        });
        Assert.True(deaf.Start(voices, files, "wren.hello", "x") && deaf.Finish(voices, true) && deaf.Status.Contains("no model"), "A missing model is reported, not a crash");
    }

    [Fact]
    public void InCreate()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        package.NewDialogue();
        DialogueEditor conversation = package.DialogueEditor()!;
        conversation.SetText(0, "Who's there?");
        string stem = VoiceImporter.Stem(conversation.Id, conversation.Nodes[0].Id);
        Directory.CreateDirectory(Path.Combine(package.PackagePath, "voice"));
        File.WriteAllBytes(Path.Combine(package.PackagePath, "voice", stem + ".wav"), Wav(22050, 1, 0.8));

        VoiceImporter? voices = package.VoiceImporter();
        Assert.True(voices != null && Near(voices.Options.FlagBelow, 0.6), "Create has voice lines, with the game's settings");
        package.VoiceJob.SetLoader((out string why) =>
        {
            why = "";
            return new FixedEars(Heard("who's", 0.1, 0.3), Heard("there", 0.3, 0.7));
        });
        Assert.True(package.VoiceJob.Start(voices!, package.PackageFiles(), stem, conversation.Nodes[0].Text) && package.VoiceJob.Finish(voices!, true),
            "A recording in the package is imported");
        Assert.True(package.Save(), package.Status);
        VoiceLine? read = VoiceLine.Read(File.ReadAllText(Path.Combine(package.PackagePath, VoiceImporter.VoicePath(stem))), out _);
        Assert.True(read != null && read.Text == "Who's there?" && read.Audio == stem + ".wav" && read.Words.Count == 2, "writes its voice file beside the recording");
        package.Undo();
        Assert.Null(voices!.Find(stem)!.Voice);
    }
}
