using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>
/// Account sync, the C++ AccountSyncTests: two installs and a stand-in for the server that keeps
/// the real one's rules (revisions, newest wins, a backup of the copy a write replaces, markers for
/// deletes). Covers playing offline and sending it later, both sides changing, deletes, the
/// graveyard, a server that doesn't answer, and files that must be left alone.
/// </summary>
public class AccountSyncTests
{
    private const long Start = 1790000000000;

    private sealed class FakeServer : IAccountServer
    {
        public sealed class Record
        {
            public long Time;
            public long Revision;
            public bool Deleted;
            public JsonNode? Body;
        }

        public string User = "ada";
        public bool IsOnline = true;
        public long Now = Start;
        public int FailNext;             // calls that get no answer
        public bool Hold;                // answers wait for Release()
        public Action? BeforePut;        // runs once, as another device getting in first
        public readonly List<string> Calls = new();
        public readonly List<Action> Held = new();
        // per kind (0 characters, 1 saves), per user, per id
        private readonly Dictionary<string, Dictionary<string, Record>>[] _live = { new(), new() };
        private readonly Dictionary<string, Dictionary<string, Record>>[] _backup = { new(), new() };

        public string Account => IsOnline ? User : "";

        public void Call(string rpc, JsonNode? payload, Action<JsonNode?> answer)
        {
            Calls.Add(rpc);
            JsonNode? result = null;
            if (IsOnline && FailNext-- <= 0)
            {
                result = Handle(rpc, (JsonObject)payload!);
            }
            if (FailNext < 0)
            {
                FailNext = 0;
            }
            if (Hold)
            {
                Held.Add(() => answer(result));
            }
            else
            {
                answer(result);
            }
        }

        public void Release()
        {
            var waiting = new List<Action>(Held);
            Held.Clear();
            foreach (Action a in waiting)
            {
                a();
            }
        }

        public int Count(string rpc) => Calls.Count(c => c == rpc);

        public Dictionary<string, Record> Records(int kind, bool fromBackup = false)
        {
            var all = (fromBackup ? _backup : _live)[kind];
            if (!all.TryGetValue(User, out var records))
            {
                records = new Dictionary<string, Record>();
                all[User] = records;
            }
            return records;
        }

        public Record? Find(int kind, string id, bool fromBackup = false) => Records(kind, fromBackup).GetValueOrDefault(id);

        public void Put(int kind, string id, long time, JsonNode body)
        {
            var records = Records(kind);
            Record? old = records.GetValueOrDefault(id);
            if (old != null && old.Revision > 0 && !old.Deleted)
            {
                Records(kind, true)[id] = old;
            }
            records[id] = new Record { Time = time, Revision = (old?.Revision ?? 0) + 1, Body = body };
        }

        private JsonNode? Handle(string rpc, JsonObject request)
        {
            int kind = rpc.StartsWith("characters_", StringComparison.Ordinal) ? 0 : 1;
            string what = rpc[(rpc.IndexOf('_') + 1)..];
            string idField = kind == 0 ? "id" : "adventure";
            string timeField = kind == 0 ? "updatedAt" : "savedAt";
            string bodyField = kind == 0 ? "character" : "save";
            JsonObject Header(string id, Record record) => new()
            {
                [idField] = id, [timeField] = record.Time, ["serverTime"] = Now, ["revision"] = record.Revision, ["deleted"] = record.Deleted,
            };
            if (what == "put" && BeforePut != null)
            {
                Action first = BeforePut;
                BeforePut = null;
                first();
            }
            var records = Records(kind);
            if (what == "list")
            {
                var list = new JsonArray();
                foreach (var (id, record) in records.OrderBy(r => r.Key, StringComparer.Ordinal))
                {
                    if (!record.Deleted || (request["deleted"]?.GetValue<bool>() ?? false))
                    {
                        list.Add(Header(id, record));
                    }
                }
                return new JsonObject { [kind == 0 ? "characters" : "saves"] = list };
            }
            string key = request[idField]?.GetValue<string>() ?? "";
            if (what == "get")
            {
                Record? record = Find(kind, key, request["backup"]?.GetValue<bool>() ?? false);
                if (record == null || record.Deleted)
                {
                    return null;
                }
                JsonObject got = Header(key, record);
                got[bodyField] = record.Body!.DeepClone();
                return got;
            }
            bool removing = what == "delete";
            long time = Math.Min(request[timeField]?.GetValue<long>() ?? Now, Now + 5 * 60 * 1000);
            Record? current = records.GetValueOrDefault(key);
            if (current != null && current.Time > time)
            {
                JsonObject refused = Header(key, current);
                refused["stored"] = false;
                return refused;
            }
            if (removing && (current == null || current.Deleted))
            {
                JsonObject nothing = current == null ? new JsonObject { [idField] = key } : Header(key, current);
                nothing["deleted"] = current != null;
                nothing["stored"] = false;
                return nothing;
            }
            var next = new Record { Time = time, Revision = current == null ? 1 : current.Revision + 1, Deleted = removing, Body = removing ? null : request[bodyField]!.DeepClone() };
            if (current != null && !current.Deleted)
            {
                Records(kind, true)[key] = current;
            }
            records[key] = next;
            JsonObject stored = Header(key, next);
            stored["stored"] = true;
            return stored;
        }
    }

    // One install: its folders and its sync.
    private sealed class Device
    {
        public readonly string Folder;
        public readonly AccountSync Sync;

        public Device(FakeServer server, string where)
        {
            Folder = where;
            Directory.CreateDirectory(Folder);
            Sync = new AccountSync(server) { Interval = 1e9, Clock = () => server.Now }; // passes run when asked
            Sync.SetFolders(Folders);
        }

        public SyncFolders Folders => new(Path.Combine(Folder, "saves"), Path.Combine(Folder, "characters"), Folder);

        // Runs one pass to its end, if one can run.
        public AccountSync.Counts Pass()
        {
            Sync.Request();
            for (int i = 0; i < 100; i++)
            {
                Sync.Update(0.01);
                if (!Sync.Running)
                {
                    break;
                }
            }
            return Sync.Last;
        }

        public string Hero(string name) => Path.Combine(Folder, "characters", name + ".json");
        public string Grave(string name) => Path.Combine(Folder, "characters", "graveyard", name + ".json");
        public string Adventure => Path.Combine(Folder, "saves", "adventure.json");
        public string Kept(string kind, string id) => Path.Combine(Folder, AccountSync.BackupFolder, kind, id + ".json");
        public string State => Path.Combine(Folder, AccountSync.StateFile);
    }

    private static void Put(string path, JsonNode data, long time)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, data.ToJsonString());
        File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeMilliseconds(time).UtcDateTime);
    }

    private static long TimeOf(string path) => new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();

    private static JsonNode? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static JsonNode Character(string name, int level) => JsonNode.Parse(
        $$$"""{"format": "yorehold.character", "version": 1, "data": {"choices": {"name": "{{{name}}}", "level": {{{level}}}}, "coins": 12.5}}""")!;

    private static JsonNode Save(int turn) => JsonNode.Parse($$$"""{"format": "yorehold.save", "version": 3, "data": {"turn": {{{turn}}}}}""")!;

    private static int Level(JsonNode? file) => file?["data"]?["choices"]?["level"]?.GetValue<int>() ?? 0;

    private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    [Fact]
    public void TwoInstallsStayTheSame()
    {
        using var scratch = new Scratch();
        string root = Path.Combine(scratch.Folder, "account-sync");
        var server = new FakeServer();
        var a = new Device(server, Path.Combine(root, "a"));
        var b = new Device(server, Path.Combine(root, "b"));

        // ---- offline: nothing is sent, nothing is touched
        server.IsOnline = false;
        Put(a.Hero("ser-ada"), Character("Ser Ada", 1), Start - 10000);
        Put(a.Adventure, Save(1), Start - 9000);
        Put(Path.Combine(a.Folder, "saves", "settings.json"), new JsonObject { ["server"] = "http://127.0.0.1:7350" }, Start - 9000);
        a.Pass();
        Assert.True(server.Calls.Count == 0 && a.Sync.Passes == 0, "offline, no pass runs and the server isn't called");
        Assert.True(!File.Exists(a.State) && Level(Read(a.Hero("ser-ada"))) == 1, "offline, the files are left as they are");

        // ---- signing in sends what was made offline
        server.IsOnline = true;
        a.Sync.Update(0.01); // no request: coming online is enough
        Assert.True(a.Sync.Passes == 1 && a.Sync.Last.Uploaded == 2 && a.Sync.Last.Failed == 0, "signing in sends the character and the save made offline");
        FakeServer.Record? stored = server.Find(0, "ser-ada");
        Assert.True(stored != null && stored.Revision == 1 && stored.Time == Start - 10000 && Level(stored.Body) == 1,
            "the account's character has revision 1 and the file's modified time");
        Assert.True(Same(server.Find(1, "adventure")?.Body, Save(1)), "the account's save is the file's contents");
        Assert.True(server.Records(1).Count == 1, "a settings file in the saves folder isn't taken for a save");
        JsonNode state = Read(a.State)!;
        Assert.True(state["format"]!.GetValue<string>() == "yorehold.sync" && state["account"]!.GetValue<string>() == "ada"
            && state["characters"]!["ser-ada"]!["revision"]!.GetValue<long>() == 1 && state["characters"]!["ser-ada"]!["hash"]!.GetValue<string>().Length > 0
            && state["saves"]!["adventure"] != null, "sync.json records the account and each record's revision and contents");
        Assert.Equal("Account sync: 2 sent", a.Sync.Summary());

        int puts = server.Count("characters_put") + server.Count("saves_put");
        a.Pass();
        Assert.True(a.Sync.Passes == 2 && !a.Sync.Last.Any && server.Count("characters_put") + server.Count("saves_put") == puts,
            "a pass with nothing changed sends nothing");
        Assert.Equal("", a.Sync.Summary());

        // ---- a second install gets both
        int changes = b.Sync.LocalChanges;
        b.Pass();
        Assert.True(b.Sync.Last.Downloaded == 2 && b.Sync.Last.Uploaded == 0, "a second install receives the character and the save");
        Assert.True(Same(Read(b.Hero("ser-ada")), Character("Ser Ada", 1)) && Same(Read(b.Adventure), Save(1)), "the received files hold the same data");
        Assert.True(TimeOf(b.Hero("ser-ada")) == Start - 10000, "a received file carries the change's time, not the time it arrived");
        Assert.True(b.Sync.LocalChanges == changes + 2, "the game is told files changed");
        Assert.False(Directory.Exists(Path.Combine(b.Folder, AccountSync.BackupFolder)), "a file that replaced nothing leaves no backup");
        b.Pass();
        Assert.False(b.Sync.Last.Any, "received files don't count as changed on the next pass");

        // ---- an edit goes up, the other install takes it and keeps its old copy
        Put(a.Hero("ser-ada"), Character("Ser Ada", 2), Start + 1000);
        a.Pass();
        Assert.True(a.Sync.Last.Uploaded == 1 && server.Find(0, "ser-ada")!.Revision == 2, "an edited character is sent as the next revision");
        Assert.True(Level(server.Find(0, "ser-ada", true)?.Body) == 1, "the account keeps the copy it replaced");
        b.Pass();
        Assert.True(b.Sync.Last.Downloaded == 1 && Level(Read(b.Hero("ser-ada"))) == 2, "the other install receives the edit");
        Assert.True(Level(Read(b.Kept("characters", "ser-ada"))) == 1, "and keeps the file it replaced in sync-backup");

        // ---- both edit offline: the newer wins on both, the older is kept
        server.IsOnline = false;
        Put(a.Hero("ser-ada"), Character("Ser Ada", 3), Start + 5000);
        Put(b.Hero("ser-ada"), Character("Ser Ada", 4), Start + 8000);
        a.Pass();
        b.Pass();
        Assert.True(Level(Read(a.Hero("ser-ada"))) == 3 && Level(Read(b.Hero("ser-ada"))) == 4, "offline edits stay on their own install");
        server.IsOnline = true;
        a.Pass();
        b.Pass();
        Assert.True(b.Sync.Last.Uploaded == 1 && Level(server.Find(0, "ser-ada")!.Body) == 4 && server.Find(0, "ser-ada")!.Revision == 4,
            "of two offline edits the newer one ends up on the account");
        Assert.True(Level(server.Find(0, "ser-ada", true)!.Body) == 3, "the older edit is the account's backup");
        a.Pass();
        Assert.True(a.Sync.Last.Downloaded == 1 && Level(Read(a.Hero("ser-ada"))) == 4 && Level(Read(a.Kept("characters", "ser-ada"))) == 3,
            "the install with the older edit takes the newer one and keeps its own in sync-backup");

        // the other way round: the install that syncs second has the older edit
        Put(a.Hero("ser-ada"), Character("Ser Ada", 5), Start + 20000);
        Put(b.Hero("ser-ada"), Character("Ser Ada", 6), Start + 15000);
        a.Pass();
        b.Pass();
        Assert.True(b.Sync.Last.Downloaded == 1 && b.Sync.Last.Uploaded == 0 && Level(Read(b.Hero("ser-ada"))) == 5, "an older local edit loses to the account's newer one");
        Assert.True(Level(Read(b.Kept("characters", "ser-ada"))) == 6 && Level(server.Find(0, "ser-ada")!.Body) == 5, "the losing edit is kept in sync-backup and never sent");

        // another device gets in between the list and the write: the server refuses, and its copy is fetched
        Put(a.Hero("ser-ada"), Character("Ser Ada", 7), Start + 30000);
        server.BeforePut = () => server.Put(0, "ser-ada", Start + 40000, Character("Ser Ada", 8));
        a.Pass();
        Assert.True(a.Sync.Last.Uploaded == 0 && a.Sync.Last.Downloaded == 1 && Level(Read(a.Hero("ser-ada"))) == 8 && Level(Read(a.Kept("characters", "ser-ada"))) == 7,
            "a write the server refuses as older is replaced by the account's copy, and kept in sync-backup");
        b.Pass();

        // ---- deletes travel too
        File.Delete(a.Adventure); // the adventure was finished
        server.Now = Start + 50000;
        a.Pass();
        Assert.True(a.Sync.Last.RemovedThere == 1 && server.Find(1, "adventure")!.Deleted && server.Find(1, "adventure")!.Revision == 2,
            "a save removed here is deleted on the account");
        Assert.True(Same(server.Find(1, "adventure", true)?.Body, Save(1)), "the account keeps the deleted save as its backup");
        Put(b.Adventure + ".bak", Save(0), Start - 20000);
        b.Pass();
        Assert.True(b.Sync.Last.RemovedHere == 1 && !File.Exists(b.Adventure) && !File.Exists(b.Adventure + ".bak"), "the other install removes the save and its .bak");
        Assert.True(Same(Read(b.Kept("saves", "adventure")), Save(1)), "after keeping it in sync-backup");
        a.Pass();
        b.Pass();
        Assert.True(!a.Sync.Last.Any && !b.Sync.Last.Any, "a delete is settled after one pass each");

        // starting the adventure again brings the save back everywhere
        Put(b.Adventure, Save(10), Start + 60000);
        b.Pass();
        a.Pass();
        Assert.True(b.Sync.Last.Uploaded == 1 && !server.Find(1, "adventure")!.Deleted && a.Sync.Last.Downloaded == 1 && Same(Read(a.Adventure), Save(10)),
            "a save written after the delete comes back on the account and the other install");

        // a character deleted here but changed elsewhere since is kept: the newer work wins
        Put(b.Hero("ser-ada"), Character("Ser Ada", 9), Start + 70000);
        b.Pass();
        File.Delete(a.Hero("ser-ada"));
        a.Pass();
        Assert.True(a.Sync.Last.RemovedThere == 0 && a.Sync.Last.Downloaded == 1 && Level(Read(a.Hero("ser-ada"))) == 9 && !server.Find(0, "ser-ada")!.Deleted,
            "a file deleted here but changed on the account since comes back instead");

        // ---- the graveyard: a dead character moves there on every install
        server.Now = Start + 75000;
        Directory.CreateDirectory(Path.GetDirectoryName(a.Grave("ser-ada"))!);
        File.Move(a.Hero("ser-ada"), a.Grave("ser-ada"));
        a.Pass();
        Assert.True(a.Sync.Last.RemovedThere == 1 && a.Sync.Last.Uploaded == 1 && server.Find(0, "ser-ada")!.Deleted && server.Find(0, "graveyard.ser-ada") != null
            && !server.Find(0, "graveyard.ser-ada")!.Deleted, "a retired character is deleted and sent again under its graveyard id");
        b.Pass();
        Assert.True(!File.Exists(b.Hero("ser-ada")) && Level(Read(b.Grave("ser-ada"))) == 9, "the other install moves the character to its graveyard");

        // ---- a server that doesn't answer: nothing is lost, the next pass sends it
        Put(a.Hero("grak"), Character("Grak", 1), Start + 80000);
        int passes = a.Sync.Passes;
        server.FailNext = 1; // the list
        a.Pass();
        Assert.True(a.Sync.Passes == passes + 1 && a.Sync.Last.Failed == 1 && a.Sync.Last.Uploaded == 0 && server.Find(0, "grak") == null,
            "a pass whose list isn't answered does nothing");
        a.Pass();
        Assert.True(a.Sync.Last.Uploaded == 1 && a.Sync.Last.Failed == 0 && server.Find(0, "grak") != null, "the next pass sends it");
        Put(a.Hero("grak"), Character("Grak", 2), Start + 81000);
        Put(a.Adventure, Save(11), Start + 81000);
        // one write isn't answered: the other still goes up, and the missed one follows next pass
        server.BeforePut = () => server.FailNext = 1;
        a.Pass();
        Assert.True(a.Sync.Last.Uploaded == 1 && a.Sync.Last.Failed == 1, "a write that isn't answered doesn't stop the others");
        a.Pass();
        Assert.True(a.Sync.Last.Uploaded == 1 && a.Sync.Last.Failed == 0 && Level(server.Find(0, "grak")!.Body) == 2 && Same(server.Find(1, "adventure")!.Body, Save(11)),
            "and is sent by the next pass");

        // answers that come on a later frame
        Put(a.Hero("grak"), Character("Grak", 3), Start + 82000);
        server.Hold = true;
        a.Sync.Request();
        a.Sync.Update(0.01);
        Assert.True(a.Sync.Running && server.Held.Count == 1, "one call is out at a time while the pass waits");
        passes = a.Sync.Passes;
        for (int i = 0; i < 20 && a.Sync.Running; i++)
        {
            server.Release();
            a.Sync.Update(0.01);
        }
        server.Hold = false;
        Assert.True(!a.Sync.Running && a.Sync.Passes == passes + 1 && a.Sync.Last.Uploaded == 1 && Level(server.Find(0, "grak")!.Body) == 3,
            "a pass whose answers arrive on later frames finishes the same");

        // going offline in the middle drops the rest; it is sent after signing in again
        Put(a.Hero("grak"), Character("Grak", 4), Start + 83000);
        server.Hold = true;
        a.Sync.Request();
        a.Sync.Update(0.01);
        server.IsOnline = false;
        for (int i = 0; i < 20 && a.Sync.Running; i++)
        {
            server.Release();
            a.Sync.Update(0.01);
        }
        server.Hold = false;
        Assert.True(!a.Sync.Running && Level(server.Find(0, "grak")!.Body) == 3, "a pass cut off by going offline stops");
        server.IsOnline = true;
        a.Sync.Update(0.01);
        Assert.True(a.Sync.Last.Uploaded == 1 && Level(server.Find(0, "grak")!.Body) == 4, "and the change goes up on signing in again");

        // ---- a pass runs by itself every Interval seconds
        a.Sync.Interval = 30;
        a.Pass();
        Put(a.Hero("grak"), Character("Grak", 5), Start + 84000);
        a.Sync.Update(10);
        Assert.True(Level(server.Find(0, "grak")!.Body) == 4, "nothing is sent before the interval is up");
        a.Sync.Update(21);
        Assert.True(Level(server.Find(0, "grak")!.Body) == 5, "a pass runs once the interval is up");
        a.Sync.Interval = 1e9;
        a.Pass();

        // ---- files and records that are left alone
        File.WriteAllText(a.Hero("broken"), "not json");
        Put(a.Hero("has space"), Character("Has Space", 1), Start);
        Put(a.Hero("huge"), new JsonObject { ["notes"] = new string('x', AccountSync.MaxCharacterBytes + 10) }, Start);
        server.Put(1, "settings", Start + 90000, new JsonObject { ["server"] = "elsewhere" });
        server.Put(1, "adventure-..-evil", Start + 90000, Save(1));
        a.Pass();
        Assert.True(a.Sync.Problems.Count == 3 && server.Find(0, "broken") == null && server.Find(0, "has space") == null && server.Find(0, "huge") == null,
            "a broken file, a name that isn't an id and a file over the limit are left alone and named");
        Assert.True(Read(Path.Combine(a.Folder, "saves", "settings.json"))!["server"]!.GetValue<string>() == "http://127.0.0.1:7350",
            "a record named like another file of the game's is never written");
        Assert.True(File.Exists(Path.Combine(a.Folder, "saves", "adventure-..-evil.json")) && !File.Exists(Path.Combine(a.Folder, "-evil.json")),
            "an id can't climb out of the folder");
        File.Delete(a.Hero("broken"));
        File.Delete(a.Hero("has space"));
        File.Delete(a.Hero("huge"));

        // ---- another account: its records are separate and the old revisions are forgotten
        server.User = "bo";
        a.Pass();
        Assert.True(Read(a.State)!["account"]!.GetValue<string>() == "bo" && server.Find(0, "grak")?.Revision == 1 && Level(server.Find(0, "grak")!.Body) == 5,
            "signing in to another account sends this install's files to it");
        server.User = "ada";
        Assert.True(server.Find(0, "grak")!.Revision >= 5, "and leaves the first account's records as they were");

        // ---- sync.json lost: the same files aren't taken for edits newer than the account's
        File.Delete(b.State);
        b.Pass();
        long revision = server.Find(0, "grak")!.Revision;
        b.Pass();
        Assert.True(Level(Read(b.Hero("grak"))) == 5 && server.Find(0, "grak")!.Revision == revision, "without sync.json the account's copy is taken and nothing is sent");

        // ---- turning it off
        var off = new Device(server, Path.Combine(root, "off"));
        off.Sync.SetFolders(null);
        Put(off.Hero("nobody"), Character("Nobody", 1), Start);
        off.Pass();
        Assert.True(off.Sync.Passes == 0 && server.Find(0, "nobody") == null, "without folders nothing runs");
    }
}
