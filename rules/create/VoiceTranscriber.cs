namespace Yorehold.Rules;

/// <summary>
/// Hears the words in a recording. Tests use their own; the game has none built in yet (see
/// ROADMAP.md, P13), so Import says so. One call at a time per object, from any thread.
/// </summary>
public interface IVoiceTranscriber
{
    /// <summary>"base.en", written into the voice file.</summary>
    string Model { get; }

    /// <summary>samples are 16 kHz mono. prompt steers it toward spellings (the package's names).</summary>
    List<VoiceWord>? Transcribe(float[] samples, string prompt, out string error);
}

public static class VoiceTranscription
{
    /// <summary>False while the game ships no speech model.</summary>
    public static bool BuiltIn => false;

    /// <summary>The game's own listener; null and why while there is none.</summary>
    public static IVoiceTranscriber? Load(out string error)
    {
        error = "this build has no speech model to listen with yet";
        return null;
    }

    /// <summary>
    /// What an import does: decode, listen, then match to the written line (empty = keep what was
    /// heard, as a suggestion). Touches no editor, so it can run off the main thread.
    /// </summary>
    public static VoiceLine? Transcribe(byte[] recording, string audioName, string written, IReadOnlyList<string> vocabulary, IVoiceTranscriber transcriber, out string error)
    {
        if (VoiceAudio.Decode(recording, out error) is not float[] samples)
        {
            return null;
        }
        string prompt = vocabulary.Count == 0 ? "" : string.Join(", ", vocabulary) + ".";
        if (transcriber.Transcribe(samples, prompt, out error) is not List<VoiceWord> heard)
        {
            return null;
        }
        double length = (double)samples.Length / VoiceAudio.SampleRate;
        var line = new VoiceLine { Audio = audioName, Model = transcriber.Model, Words = VoiceWords.Match(heard, written, length) };
        return line with { Text = VoiceWords.Split(written).Count == 0 ? line.ToText() : written };
    }
}
