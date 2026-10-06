namespace Yorehold.Rules;

/// <summary>
/// One import at a time, off the main thread: the recording is read here, listened to on a worker,
/// and its result put on the importer (one undo step) by Finish on the main thread. The listener
/// is made the first time one is needed and kept, so a model loads once.
/// </summary>
public sealed class VoiceImport
{
    /// <summary>Makes the listener; null and why when there is none.</summary>
    public delegate IVoiceTranscriber? Loader(out string error);

    private sealed record Result(VoiceLine? Line, string Error);

    private readonly object _lock = new();
    private Loader _loader = VoiceTranscription.Load;
    private IVoiceTranscriber? _transcriber;
    private Task<Result>? _job;
    private string _stem = "";

    public bool Busy => _job != null;
    public string Status { get; private set; } = "";

    public void SetLoader(Loader loader)
    {
        lock (_lock)
        {
            _loader = loader;
            _transcriber = null;
        }
    }

    /// <summary>Starts listening to a node's recording; false (and why in Status) if it can't.</summary>
    public bool Start(VoiceImporter voices, ContentFiles files, string stem, string written)
    {
        if (Busy)
        {
            Status = "Still listening to " + _stem;
            return false;
        }
        // a recording dropped in since the line was first read is looked for again
        VoiceImporter.Line line = voices.LineOf(files, stem);
        if (line.Audio.Length == 0)
        {
            line = voices.LineOf(files, stem, true);
        }
        if (line.Audio.Length == 0)
        {
            Status = $"There is no recording for {stem} yet";
            return false;
        }
        byte[] recording;
        try
        {
            recording = files.ReadBytes(line.Audio);
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ContentException)
        {
            Status = line.Audio + " can't be read";
            return false;
        }
        string audioName = line.Audio[(line.Audio.LastIndexOf('/') + 1)..];
        List<string> vocabulary = VoiceImporter.Vocabulary(files);
        _job = Task.Run(() =>
        {
            lock (_lock)
            {
                string error = "";
                _transcriber ??= _loader(out error);
                if (_transcriber == null)
                {
                    return new Result(null, error.Length > 0 ? error : "there is nothing to listen with");
                }
                VoiceLine? heard = VoiceTranscription.Transcribe(recording, audioName, written, vocabulary, _transcriber, out error);
                return new Result(heard, error);
            }
        });
        _stem = stem;
        Status = $"Listening to {line.Audio}...";
        return true;
    }

    /// <summary>Takes a finished import in; true once one was. wait blocks until it is done.</summary>
    public bool Finish(VoiceImporter voices, bool wait = false)
    {
        if (_job == null || (!wait && !_job.IsCompleted))
        {
            return false;
        }
        Result result = _job.GetAwaiter().GetResult();
        _job = null;
        if (result.Line != null)
        {
            voices.SetVoice(_stem, result.Line);
            Status = $"Imported {_stem}: {result.Line.Words.Count} words";
        }
        else
        {
            Status = $"{_stem} not imported: {result.Error}";
        }
        return true;
    }
}
