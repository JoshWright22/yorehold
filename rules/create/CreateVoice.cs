namespace Yorehold.Rules;

/// <summary>Voice lines' part of the open package: the voice files of the conversations, and the import running.</summary>
public sealed partial class CreatePackage
{
    private VoiceImporter? _voice;
    private string _voiceError = "";

    /// <summary>The import that may be running; kept with the package so it finishes into it.</summary>
    public VoiceImport VoiceJob { get; } = new();

    /// <summary>The voice files, with the settings in the game's create/voice.json (the defaults if it can't be read).</summary>
    public VoiceImporter? VoiceImporter()
    {
        if (Manifest == null)
        {
            return null;
        }
        if (_voice == null)
        {
            VoiceImporter.Settings settings = new();
            var game = new ContentFiles(_gameAssets);
            if (game.Exists("create/voice.json"))
            {
                if (Yorehold.Rules.VoiceImporter.Settings.Read(game.ReadText("create/voice.json"), out string error) is VoiceImporter.Settings read)
                {
                    settings = read;
                }
                else
                {
                    _voiceError = $"create/voice.json: {error} (using the defaults)";
                }
            }
            _voice = new VoiceImporter(_history, settings);
        }
        return _voice;
    }

    /// <summary>The package's own files, where recordings and voice files are.</summary>
    public ContentFiles PackageFiles() => new(PackagePath);

    private void CloseVoice()
    {
        // a running import lands in the importer it was started for before it goes
        if (_voice != null)
        {
            VoiceJob.Finish(_voice, true);
        }
        _voice = null;
        _voiceError = "";
    }

    private void VoicesToSave(List<Changed> changed)
    {
        if (_voice == null)
        {
            return;
        }
        VoiceImporter voices = _voice;
        foreach ((string path, string text) in voices.Changed())
        {
            changed.Add(new Changed(path, text, () => voices.MarkSaved(path)));
        }
    }

    private void VoiceProblems(List<CreateProblem> found)
    {
        if (_voiceError.Length > 0)
        {
            found.Add(new CreateProblem("create/voice.json", _voiceError, false));
        }
        if (_voice == null)
        {
            return;
        }
        // only voice lines already looked at: nothing is read from disk here
        foreach (DialogueTab tab in _dialogues.Values)
        {
            foreach (DialogueEditor.Node node in tab.Editor.Nodes)
            {
                string stem = Yorehold.Rules.VoiceImporter.Stem(tab.Editor.Id, node.Id);
                found.AddRange(_voice.Problems(stem, node.Text).Select(p => new CreateProblem(Yorehold.Rules.VoiceImporter.VoicePath(stem), p.Text, p.Error)));
            }
        }
    }
}
