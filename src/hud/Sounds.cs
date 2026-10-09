using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Plays the sound ui/sounds.json names for a moment, from the content, a skin or an art pack. The
/// game ships none, so most moments are silent until a pack brings them. Options > Sound sets how
/// loud, or off.
/// </summary>
public static class Sounds
{
    private static UiSounds _look = new();
    private static ContentFiles? _files;
    private static readonly Dictionary<string, AudioStream?> Loaded = new();
    private static readonly List<AudioStreamPlayer> Players = new();

    /// <summary>Reads the sounds file (the game's, a skin's laid over it) at start.</summary>
    public static void Load(ContentFiles files)
    {
        _files = files;
        _look = new UiSounds();
        Loaded.Clear();
        if (!files.Exists(UiSounds.File))
        {
            return;
        }
        try
        {
            _look = UiSounds.Read(ContentNode.Read(files, UiSounds.File));
        }
        catch (ContentException error)
        {
            GD.PushWarning($"{UiSounds.File} can't be read, so the game is silent: {error.Message}");
        }
    }

    /// <summary>Plays a moment's sound, if it has one and sound is on.</summary>
    public static void Play(string moment)
    {
        int loudness = App.Settings.Sound;
        if (loudness <= 0 || _files == null || DisplayServer.GetName() == "headless")
        {
            return;
        }
        string file = _look.For(moment);
        if (file.Length == 0 || Stream(file) is not AudioStream stream)
        {
            return;
        }
        AudioStreamPlayer player = FreePlayer();
        player.Stream = stream;
        // quiet, medium, loud: -12, -6 and 0 dB on top of the file's own volume
        player.VolumeDb = (float)_look.Volume + (loudness - 3) * 6;
        player.Play();
    }

    private static AudioStream? Stream(string file)
    {
        if (Loaded.TryGetValue(file, out AudioStream? known))
        {
            return known;
        }
        AudioStream? stream = null;
        if (_files!.FullPath(file) is string full)
        {
            string kind = System.IO.Path.GetExtension(full).ToLowerInvariant();
            stream = kind switch
            {
                ".ogg" => AudioStreamOggVorbis.LoadFromFile(full),
                ".wav" => AudioStreamWav.LoadFromFile(full),
                ".mp3" => AudioStreamMP3.LoadFromFile(full),
                _ => null,
            };
        }
        Loaded[file] = stream;
        return stream;
    }

    // a player not already sounding, so a dice clatter doesn't cut off a hit
    private static AudioStreamPlayer FreePlayer()
    {
        foreach (AudioStreamPlayer player in Players)
        {
            if (GodotObject.IsInstanceValid(player) && !player.Playing)
            {
                return player;
            }
        }
        Players.RemoveAll(p => !GodotObject.IsInstanceValid(p));
        var made = new AudioStreamPlayer();
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(made);
        Players.Add(made);
        return made;
    }
}
