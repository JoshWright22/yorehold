using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Where sync works: the folder of adventure saves (adventure.json, adventure-*.json), the
/// character library (with its graveyard/ folder) and the folder for sync.json and sync-backup/.
/// </summary>
public sealed record SyncFolders(string Saves, string Characters, string State);

/// <summary>
/// Keeps the characters and adventure saves the same as the copies on the player's account. The
/// files stay the truth while offline; a pass runs at sign-in, when the game asks for one after
/// writing, and every Interval seconds. Per record the newest change wins, and the copy that lost
/// is kept: the server keeps the one it replaced, and a local file a pass replaces or removes goes
/// to sync-backup/ first.
///
/// What changed since the last pass is worked out from sync.json (the server's revision and the
/// file's contents as they were then), so edits and deletions made offline are sent later. The C++
/// client's AccountSync, with the saves and characters in folders of their own as this port keeps them.
/// </summary>
public sealed class AccountSync
{
    public const string StateFile = "sync.json";
    public const string BackupFolder = "sync-backup";
    /// <summary>The server's limits; a larger file is left alone and named in Problems.</summary>
    public const int MaxCharacterBytes = 256 * 1024;
    public const int MaxSaveBytes = 1024 * 1024;

    public sealed record Counts
    {
        public int Uploaded { get; set; }     // files sent to the account
        public int Downloaded { get; set; }   // files written from the account
        public int RemovedHere { get; set; }  // files removed because another device deleted them
        public int RemovedThere { get; set; } // records deleted on the account because the file was deleted here
        public int Failed { get; set; }       // calls the server didn't answer
        public bool Any => Uploaded > 0 || Downloaded > 0 || RemovedHere > 0 || RemovedThere > 0 || Failed > 0;
    }

    private sealed record Kind(string Name, string List, string Get, string Put, string Remove, string ListField, string IdField, string TimeField,
        string BodyField, int MaxBytes);

    private static readonly Kind[] Kinds =
    {
        new("characters", "characters_list", "characters_get", "characters_put", "characters_delete", "characters", "id", "updatedAt", "character", MaxCharacterBytes),
        new("saves", "saves_list", "saves_get", "saves_put", "saves_delete", "saves", "adventure", "savedAt", "save", MaxSaveBytes),
    };

    private const int CharactersKind = 0;
    private const int SavesKind = 1;
    private const string GraveyardPrefix = "graveyard.";
    private const int MaxFailures = 3; // a pass gives up after this many unanswered calls

    private sealed record Known(long Revision, string Hash); // Hash "" = no file (deleted)
    private sealed record Remote(long Revision, long Time, bool Deleted);
    private sealed record Local(string Path, JsonObject Body, string Hash, long Time);

    private enum JobType
    {
        Upload,
        Download,
        RemoveThere,
    }

    private sealed record Job(JobType Type, int Kind, string Id);

    private readonly IAccountServer _server;
    private SyncFolders? _folders;
    private string _account = ""; // the one the pass in progress is for
    private readonly SortedDictionary<string, Known>[] _known = { new(StringComparer.Ordinal), new(StringComparer.Ordinal) };
    private readonly Dictionary<string, Remote>[] _remote = { new(), new() };
    private readonly LinkedList<Job> _jobs = new();
    private int _listing;
    private bool _waiting; // a call is out
    private bool _wanted;
    private bool _signedIn;
    private double _timer;
    private int _pass; // answers to an earlier pass are ignored
    private Counts _counts = new();
    private readonly List<string> _problems = new();

    public AccountSync(IAccountServer server)
    {
        _server = server;
    }

    /// <summary>Milliseconds since 1970; the system clock unless a test replaces it.</summary>
    public Func<long> Clock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public double Interval { get; set; } = 30;

    public bool Running { get; private set; }
    /// <summary>Finished passes, whether or not every call was answered.</summary>
    public int Passes { get; private set; }
    public Counts Last { get; private set; } = new();
    /// <summary>Goes up whenever a pass wrote or removed one of the game's files.</summary>
    public int LocalChanges { get; private set; }
    /// <summary>Files the last pass left alone and why.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Null turns sync off.</summary>
    public void SetFolders(SyncFolders? folders)
    {
        if (folders == _folders)
        {
            return;
        }
        // a pass for the old folders must not write into the new ones
        _pass++;
        Running = _waiting = _signedIn = false;
        _jobs.Clear();
        _folders = folders;
    }

    /// <summary>Asks for a pass as soon as one can run (after the game wrote or removed a file).</summary>
    public void Request() => _wanted = true;

    /// <summary>One line about the last pass, "" if it had nothing to do.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        void Add(int count, string what)
        {
            if (count != 0)
            {
                parts.Add($"{count} {what}");
            }
        }
        Add(Last.Uploaded, "sent");
        Add(Last.Downloaded, "received");
        Add(Last.RemovedHere, "removed here");
        Add(Last.RemovedThere, "removed there");
        Add(Last.Failed, "not answered");
        Add(_problems.Count, "left alone");
        return parts.Count == 0 ? "" : "Account sync: " + string.Join(", ", parts);
    }

    /// <summary>Call once per frame.</summary>
    public void Update(double deltaSeconds)
    {
        _timer -= deltaSeconds;
        if (!Running)
        {
            bool signedIn = _folders != null && _server.Account.Length > 0;
            if (signedIn && !_signedIn)
            {
                _wanted = true; // just came online: send what was done offline
            }
            _signedIn = signedIn;
            if (!signedIn)
            {
                return;
            }
            if (_timer <= 0)
            {
                _wanted = true;
            }
            if (!_wanted)
            {
                return;
            }
            Begin();
        }
        // an answer may arrive inside the call, so this carries on until one is still out
        while (Running && !_waiting)
        {
            Step();
        }
    }

    private void Begin()
    {
        Running = true;
        _waiting = false;
        _wanted = false;
        _pass++;
        _counts = new Counts();
        _problems.Clear();
        _jobs.Clear();
        _remote[CharactersKind].Clear();
        _remote[SavesKind].Clear();
        _listing = 0;
        _account = _server.Account;
        LoadState();
    }

    private void Step()
    {
        if (_listing < 2)
        {
            ListRemote(_listing);
        }
        else if (_listing == 2)
        {
            Plan();
            _listing++;
        }
        else if (_jobs.Count == 0 || _counts.Failed >= MaxFailures || _server.Account != _account)
        {
            Finish();
        }
        else
        {
            Job job = _jobs.First!.Value;
            _jobs.RemoveFirst();
            switch (job.Type)
            {
                case JobType.Upload: Upload(job.Kind, job.Id); break;
                case JobType.Download: Download(job.Kind, job.Id); break;
                case JobType.RemoveThere: RemoveThere(job.Kind, job.Id); break;
            }
        }
    }

    private void Finish()
    {
        SaveState();
        Last = _counts;
        Passes++;
        _pass++;
        Running = _waiting = false;
        _jobs.Clear();
        _timer = Interval;
    }

    // Calls the server; then runs with the answer (null = none) unless the pass was dropped meanwhile.
    private void CallServer(string rpc, JsonObject payload, Action<JsonObject?> then)
    {
        _waiting = true;
        int pass = _pass;
        _server.Call(rpc, payload, answer =>
        {
            if (pass != _pass)
            {
                return;
            }
            _waiting = false;
            var answered = answer as JsonObject;
            if (answered == null)
            {
                _counts.Failed++;
            }
            then(answered);
        });
    }

    private void ListRemote(int kind)
    {
        CallServer(Kinds[kind].List, new JsonObject { ["headers"] = true, ["deleted"] = true }, answer =>
        {
            Kind k = Kinds[kind];
            if (answer?[k.ListField] is not JsonArray list)
            {
                // without the whole list nothing can be decided: try again next time
                if (answer != null)
                {
                    _counts.Failed++;
                }
                Finish();
                return;
            }
            foreach (JsonNode? header in list)
            {
                if (header is not JsonObject h || Text(h, k.IdField) is not string id)
                {
                    continue;
                }
                if (Usable(kind, id))
                {
                    _remote[kind][id] = new Remote(Number(h, "revision"), Number(h, k.TimeField), Flag(h, "deleted"));
                }
            }
            _listing++;
        });
    }

    private void Plan()
    {
        for (int kind = 0; kind < 2; kind++)
        {
            SortedDictionary<string, string> files = LocalFiles(kind);
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            ids.UnionWith(files.Keys);
            ids.UnionWith(_remote[kind].Keys);
            ids.UnionWith(_known[kind].Keys);

            foreach (string id in ids)
            {
                Local? local = null;
                if (files.TryGetValue(id, out string? path))
                {
                    local = ReadLocal(kind, id, path);
                    if (local == null)
                    {
                        continue; // unreadable or too large: left alone, already noted
                    }
                }
                Known? known = _known[kind].GetValueOrDefault(id);
                Remote? remote = _remote[kind].GetValueOrDefault(id);

                // A file that is gone counts as a change only if the last pass saw it.
                bool localChanged = local != null ? known == null || known.Hash != local.Hash : known != null && known.Hash.Length > 0;
                bool remoteChanged = remote != null && (known == null || known.Revision != remote.Revision);
                // Both changed: the newer one wins, and the account's on a tie.
                bool localWins = local != null && localChanged && (!remoteChanged || local.Time > remote!.Time);

                if (remote == null)
                {
                    if (local != null)
                    {
                        _jobs.AddLast(new Job(JobType.Upload, kind, id));
                    }
                    else
                    {
                        _known[kind].Remove(id);
                    }
                }
                else if (localWins)
                {
                    _jobs.AddLast(new Job(JobType.Upload, kind, id));
                }
                else if (remote.Deleted)
                {
                    if (local != null)
                    {
                        RemoveHere(kind, id, remote.Revision);
                    }
                    else
                    {
                        _known[kind][id] = new Known(remote.Revision, "");
                    }
                }
                else if (local != null)
                {
                    if (remoteChanged)
                    {
                        _jobs.AddLast(new Job(JobType.Download, kind, id));
                    }
                }
                else if (localChanged && !remoteChanged)
                {
                    _jobs.AddLast(new Job(JobType.RemoveThere, kind, id));
                }
                else
                {
                    // deleted here but changed there since: the newer work is kept
                    _jobs.AddLast(new Job(JobType.Download, kind, id));
                }
            }
        }
    }

    private void Upload(int kind, string id)
    {
        Kind k = Kinds[kind];
        if (ReadLocal(kind, id, PathFor(kind, id)) is not Local local)
        {
            return;
        }
        var payload = new JsonObject { [k.IdField] = id, [k.TimeField] = local.Time, [k.BodyField] = local.Body.DeepClone() };
        CallServer(k.Put, payload, answer =>
        {
            if (answer == null)
            {
                return;
            }
            long revision = Number(answer, "revision");
            if (Flag(answer, "stored"))
            {
                _known[kind][id] = new Known(revision, local.Hash);
                _counts.Uploaded++;
            }
            // the account holds something newer after all (another device got there first)
            else if (Flag(answer, "deleted"))
            {
                RemoveHere(kind, id, revision);
            }
            else
            {
                _jobs.AddFirst(new Job(JobType.Download, kind, id));
            }
        });
    }

    private void Download(int kind, string id)
    {
        Kind k = Kinds[kind];
        CallServer(k.Get, new JsonObject { [k.IdField] = id }, answer =>
        {
            if (answer == null)
            {
                return;
            }
            if (answer[k.BodyField] is not JsonObject body)
            {
                _problems.Add($"{k.Name}/{id}: the account's copy isn't a file the game can use");
                return;
            }
            string path = PathFor(kind, id);
            if (File.Exists(path) && !BackUp(kind, id, path))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                WriteAtomically(path, body.ToJsonString(Indented));
                // The file is as old as the change it holds, so a later pass doesn't take it for newer.
                long time = Number(answer, k.TimeField);
                if (time > 0)
                {
                    File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _problems.Add($"{k.Name}/{id}: {error.Message}");
                return;
            }
            _known[kind][id] = new Known(Number(answer, "revision"), HashOf(body.ToJsonString()));
            _counts.Downloaded++;
            LocalChanges++;
        });
    }

    private void RemoveThere(int kind, string id)
    {
        Kind k = Kinds[kind];
        // when the file was deleted isn't known, only that it was before now
        CallServer(k.Remove, new JsonObject { [k.IdField] = id, [k.TimeField] = Clock() }, answer =>
        {
            if (answer == null)
            {
                return;
            }
            long revision = Number(answer, "revision");
            if (Flag(answer, "deleted"))
            {
                _known[kind][id] = new Known(revision, "");
                if (Flag(answer, "stored"))
                {
                    _counts.RemovedThere++;
                }
            }
            else if (revision > 0)
            {
                _jobs.AddFirst(new Job(JobType.Download, kind, id)); // a newer copy stands
            }
            else
            {
                _known[kind].Remove(id);
            }
        });
    }

    private void RemoveHere(int kind, string id, long revision)
    {
        string path = PathFor(kind, id);
        if (File.Exists(path))
        {
            if (!BackUp(kind, id, path))
            {
                return;
            }
            try
            {
                File.Delete(path);
                File.Delete(path + ".bak"); // the game reads a .bak when the file itself is missing
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _problems.Add($"{Kinds[kind].Name}/{id}: couldn't remove the file: {error.Message}");
                return;
            }
            _counts.RemovedHere++;
            LocalChanges++;
        }
        _known[kind][id] = new Known(revision, "");
    }

    private string PathFor(int kind, string id)
    {
        SyncFolders folders = _folders!; // only asked for during a pass, which needs folders
        if (kind == SavesKind)
        {
            return System.IO.Path.Combine(folders.Saves, id + ".json");
        }
        if (id.StartsWith(GraveyardPrefix, StringComparison.Ordinal))
        {
            return System.IO.Path.Combine(folders.Characters, CharacterLibrary.GraveyardFolder, id[GraveyardPrefix.Length..] + ".json");
        }
        return System.IO.Path.Combine(folders.Characters, id + ".json");
    }

    private SortedDictionary<string, string> LocalFiles(int kind)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        SyncFolders folders = _folders!;
        void Scan(string dir, string prefix)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(file);
                if (kind == SavesKind && stem != "adventure" && !stem.StartsWith("adventure-", StringComparison.Ordinal))
                {
                    continue; // the game's other files
                }
                string id = prefix + stem;
                // a plain character file named like a graveyard id would be read back into the graveyard
                if (!Usable(kind, id) || (kind == CharactersKind && prefix.Length == 0 && stem.StartsWith(GraveyardPrefix, StringComparison.Ordinal)))
                {
                    _problems.Add($"{Kinds[kind].Name}/{System.IO.Path.GetFileName(file)}: the name can't be used as an id on the account");
                    continue;
                }
                files[id] = file;
            }
        }
        if (kind == SavesKind)
        {
            Scan(folders.Saves, "");
        }
        else
        {
            Scan(folders.Characters, "");
            Scan(System.IO.Path.Combine(folders.Characters, CharacterLibrary.GraveyardFolder), GraveyardPrefix);
        }
        return files;
    }

    private Local? ReadLocal(int kind, string id, string path)
    {
        Kind k = Kinds[kind];
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null; // gone since the folder was listed; the next pass sees that
        }
        if (Online.Parse(text) is not JsonObject body)
        {
            _problems.Add($"{k.Name}/{id}: the file isn't one the game wrote");
            return null;
        }
        string plain = body.ToJsonString();
        int bytes = Encoding.UTF8.GetByteCount(plain);
        if (bytes > k.MaxBytes)
        {
            _problems.Add($"{k.Name}/{id}: too large for the account ({bytes / 1024} KB)");
            return null;
        }
        long time = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
        return new Local(path, body, HashOf(plain), time);
    }

    private bool BackUp(int kind, string id, string path)
    {
        string to = System.IO.Path.Combine(_folders!.State, BackupFolder, Kinds[kind].Name, id + ".json");
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            File.Copy(path, to, true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _problems.Add($"{Kinds[kind].Name}/{id}: couldn't keep a backup, so the file was left as it is: {error.Message}");
            return false;
        }
    }

    private void LoadState()
    {
        _known[CharactersKind].Clear();
        _known[SavesKind].Clear();
        string path = System.IO.Path.Combine(_folders!.State, StateFile);
        string text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path) : "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            text = "";
        }
        // Another account's revisions mean nothing here: start over and let the newest win.
        if (Online.Parse(text) is not JsonObject state || Text(state, "format") != "yorehold.sync" || Text(state, "account") != _account)
        {
            return;
        }
        for (int kind = 0; kind < 2; kind++)
        {
            if (state[Kinds[kind].Name] is not JsonObject records)
            {
                continue;
            }
            foreach (KeyValuePair<string, JsonNode?> record in records)
            {
                if (record.Value is JsonObject r && Usable(kind, record.Key))
                {
                    _known[kind][record.Key] = new Known(Number(r, "revision"), Text(r, "hash") ?? "");
                }
            }
        }
    }

    private void SaveState()
    {
        var state = new JsonObject { ["format"] = "yorehold.sync", ["version"] = 1, ["account"] = _account };
        for (int kind = 0; kind < 2; kind++)
        {
            var records = new JsonObject();
            foreach (KeyValuePair<string, Known> known in _known[kind])
            {
                records[known.Key] = new JsonObject { ["revision"] = known.Value.Revision, ["hash"] = known.Value.Hash };
            }
            state[Kinds[kind].Name] = records;
        }
        try
        {
            Directory.CreateDirectory(_folders!.State);
            WriteAtomically(System.IO.Path.Combine(_folders.State, StateFile), state.ToJsonString(Indented));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _problems.Add($"{StateFile}: {error.Message}");
        }
    }

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static void WriteAtomically(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }

    // The server's rule for ids, plus no leading dot so an id is always a plain file name.
    private static bool ValidId(string id)
    {
        if (id.Length == 0 || id.Length > 64 || id[0] == '.')
        {
            return false;
        }
        foreach (char c in id)
        {
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.'))
            {
                return false;
            }
        }
        return true;
    }

    // Only files the game itself would write are synced, so nothing the server sends can land on
    // another file in the folder (the settings, say).
    private static bool Usable(int kind, string id)
    {
        if (!ValidId(id))
        {
            return false;
        }
        if (kind == SavesKind)
        {
            return id == "adventure" || id.StartsWith("adventure-", StringComparison.Ordinal);
        }
        return !id.StartsWith(GraveyardPrefix, StringComparison.Ordinal) || ValidId(id[GraveyardPrefix.Length..]);
    }

    // FNV-1a over the UTF-8 text: the same on every build, unlike string.GetHashCode.
    private static string HashOf(string text)
    {
        ulong hash = 14695981039346656037ul;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211ul;
        }
        return hash.ToString("x16");
    }

    private static long Number(JsonObject o, string field)
    {
        if (o[field] is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
        {
            return v.TryGetValue(out long whole) ? whole : (long)v.GetValue<double>();
        }
        return 0;
    }

    private static bool Flag(JsonObject o, string field) => o[field] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    private static string? Text(JsonObject o, string field) => o[field] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
}
