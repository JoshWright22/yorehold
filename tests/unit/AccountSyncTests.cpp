// Account sync: two installs and a stand-in for the server that keeps the real one's rules
// (revisions, newest wins, a backup of the copy a write replaces, markers for deletes). Covers
// playing offline and sending it later, both sides changing, deletes, the graveyard, a server
// that doesn't answer, and files that must be left alone.

#include "online/AccountSync.h"

#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <functional>
#include <map>
#include <string>
#include <vector>

namespace
{

namespace fs = std::filesystem;
using nlohmann::json;
using Check = std::function<void(bool, const char*)>;

constexpr int64_t start = 1790000000000;

struct FakeServer : AccountServer
{
    struct Record
    {
        int64_t time = 0;
        int64_t revision = 0;
        bool deleted = false;
        json body;
    };

    std::string user = "ada";
    bool online = true;
    int64_t now = start;
    int failNext = 0;                // calls that get no answer
    bool hold = false;               // answers wait for release()
    std::function<void()> beforePut; // runs once, as another device getting in first
    std::vector<std::string> calls;
    std::vector<std::function<void()>> held;
    std::map<std::string, std::map<std::string, Record>> live[2], backup[2]; // per user, per id

    std::string account() const override { return online ? user : std::string(); }

    void call(std::string_view rpc, const json& payload, Answer answer) override
    {
        calls.emplace_back(rpc);
        std::optional<json> result;
        if (online && failNext-- <= 0)
            result = handle(rpc, payload);
        if (failNext < 0)
            failNext = 0;
        if (hold)
            held.push_back([answer, result] { answer(result); });
        else
            answer(result);
    }

    void release()
    {
        std::vector<std::function<void()>> waiting;
        waiting.swap(held);
        for (const auto& answer : waiting) answer();
    }

    int count(std::string_view rpc) const { return static_cast<int>(std::count(calls.begin(), calls.end(), rpc)); }
    const Record* find(size_t kind, const std::string& id, bool fromBackup = false)
    {
        auto& records = (fromBackup ? backup : live)[kind][user];
        const auto found = records.find(id);
        return found == records.end() ? nullptr : &found->second;
    }
    void put(size_t kind, const std::string& id, int64_t time, json body)
    {
        Record& record = live[kind][user][id];
        if (record.revision > 0 && !record.deleted)
            backup[kind][user][id] = record;
        record = Record{time, record.revision + 1, false, std::move(body)};
    }

private:
    std::optional<json> handle(std::string_view rpc, const json& request)
    {
        const size_t kind = rpc.starts_with("characters_") ? 0 : 1;
        const std::string_view what = rpc.substr(rpc.find('_') + 1);
        const char* idField = kind == 0 ? "id" : "adventure";
        const char* timeField = kind == 0 ? "updatedAt" : "savedAt";
        const char* bodyField = kind == 0 ? "character" : "save";
        const auto header = [&](const std::string& id, const Record& record) {
            return json{{idField, id}, {timeField, record.time}, {"serverTime", now}, {"revision", record.revision}, {"deleted", record.deleted}};
        };
        if (what == "put" && beforePut)
        {
            const std::function<void()> first = std::move(beforePut);
            beforePut = nullptr;
            first();
        }
        auto& records = live[kind][user];
        if (what == "list")
        {
            json list = json::array();
            for (const auto& [id, record] : records)
                if (!record.deleted || request.value("deleted", false))
                    list.push_back(header(id, record));
            return json{{kind == 0 ? "characters" : "saves", std::move(list)}};
        }
        const std::string id = request.value(idField, std::string());
        if (what == "get")
        {
            const Record* record = find(kind, id, request.value("backup", false));
            if (!record || record->deleted)
                return std::nullopt;
            json answer = header(id, *record);
            answer[bodyField] = record->body;
            return answer;
        }
        const bool removing = what == "delete";
        const int64_t time = std::min<int64_t>(request.value(timeField, now), now + 5 * 60 * 1000);
        const auto current = records.find(id);
        if (current != records.end() && current->second.time > time)
        {
            json answer = header(id, current->second);
            answer["stored"] = false;
            return answer;
        }
        if (removing && (current == records.end() || current->second.deleted))
        {
            json answer = current == records.end() ? json{{idField, id}} : header(id, current->second);
            answer["deleted"] = current != records.end();
            answer["stored"] = false;
            return answer;
        }
        Record next{time, current == records.end() ? 1 : current->second.revision + 1, removing, removing ? json() : request.at(bodyField)};
        if (current != records.end() && !current->second.deleted)
            backup[kind][user][id] = current->second;
        records[id] = next;
        json answer = header(id, next);
        answer["stored"] = true;
        return answer;
    }
};

void setTime(const fs::path& path, int64_t milliseconds)
{
    const std::chrono::sys_time<std::chrono::milliseconds> system{std::chrono::milliseconds(milliseconds)};
    fs::last_write_time(path, std::chrono::time_point_cast<fs::file_time_type::duration>(std::chrono::clock_cast<fs::file_time_type::clock>(system)));
}

int64_t timeOf(const fs::path& path)
{
    const auto system = std::chrono::clock_cast<std::chrono::system_clock>(fs::last_write_time(path));
    return std::chrono::duration_cast<std::chrono::milliseconds>(system.time_since_epoch()).count();
}

void put(const fs::path& path, const json& data, int64_t time)
{
    fs::create_directories(path.parent_path());
    {
        std::ofstream file(path, std::ios::binary);
        file << data.dump(2);
    }
    setTime(path, time);
}

json read(const fs::path& path)
{
    std::ifstream file(path, std::ios::binary);
    return file ? json::parse(file, nullptr, false) : json();
}

json character(const char* name, int level)
{
    return {{"format", "yorehold.character"}, {"version", 1}, {"data", {{"choices", {{"name", name}, {"level", level}}}, {"coins", 12.5}}}};
}

json save(int turn)
{
    return {{"format", "yorehold.save"}, {"version", 3}, {"data", {{"turn", turn}}}};
}

int level(const json& file)
{
    return file.is_object() && file.contains("data") ? file["data"]["choices"].value("level", 0) : 0;
}

// One install: its folder and its sync.
struct Device
{
    fs::path folder;
    AccountSync sync;

    Device(FakeServer& server, fs::path where) : folder(std::move(where)), sync(server)
    {
        fs::create_directories(folder);
        sync.interval = 1e9; // passes run when asked
        sync.setClock([&server] { return server.now; });
        sync.setFolder(folder.string());
    }

    // Runs one pass to its end, if one can run.
    const AccountSync::Counts& pass()
    {
        sync.request();
        for (int i = 0; i < 100; i++)
        {
            sync.update(0.01);
            if (!sync.running())
                break;
        }
        return sync.last();
    }

    fs::path hero(const char* name) const { return folder / "characters" / (std::string(name) + ".json"); }
    fs::path adventure() const { return folder / "adventure.json"; }
    fs::path kept(const char* kind, const char* id) const { return folder / AccountSync::backupFolder / kind / (std::string(id) + ".json"); }
};

}

void accountSyncTests(const Check& check, const fs::path& scratch)
{
    const fs::path root = scratch / "account-sync";
    FakeServer server;
    Device a(server, root / "a");
    Device b(server, root / "b");

    // ---- offline: nothing is sent, nothing is touched
    server.online = false;
    put(a.hero("ser-ada"), character("Ser Ada", 1), start - 10000);
    put(a.adventure(), save(1), start - 9000);
    put(a.folder / "settings.json", json{{"server", "http://127.0.0.1:7350"}}, start - 9000);
    a.pass();
    check(server.calls.empty() && a.sync.passes() == 0, "sync: offline, no pass runs and the server isn't called");
    check(!fs::exists(a.folder / AccountSync::stateFile) && level(read(a.hero("ser-ada"))) == 1, "sync: offline, the files are left as they are");

    // ---- signing in sends what was made offline
    server.online = true;
    a.sync.update(0.01); // no request: coming online is enough
    check(a.sync.passes() == 1 && a.sync.last().uploaded == 2 && a.sync.last().failed == 0, "sync: signing in sends the character and the save made offline");
    const FakeServer::Record* stored = server.find(0, "ser-ada");
    check(stored && stored->revision == 1 && stored->time == start - 10000 && level(stored->body) == 1,
        "sync: the account's character has revision 1 and the file's modified time");
    check(server.find(1, "adventure") && server.find(1, "adventure")->body == save(1), "sync: the account's save is the file's contents");
    check(server.live[1]["ada"].size() == 1, "sync: the settings file isn't taken for a save");
    const json state = read(a.folder / AccountSync::stateFile);
    check(state.value("format", "") == "yorehold.sync" && state.value("account", "") == "ada" && state["characters"]["ser-ada"].value("revision", 0) == 1
        && !state["characters"]["ser-ada"].value("hash", "").empty() && state["saves"].contains("adventure"),
        "sync: sync.json records the account and each record's revision and contents");
    check(a.sync.summary() == "Account sync: 2 sent", "sync: the summary says what the pass did");

    int puts = server.count("characters_put") + server.count("saves_put");
    a.pass();
    check(a.sync.passes() == 2 && !a.sync.last().any() && server.count("characters_put") + server.count("saves_put") == puts,
        "sync: a pass with nothing changed sends nothing");
    check(a.sync.summary().empty(), "sync: and has nothing to say");

    // ---- a second install gets both
    int changes = b.sync.localChanges();
    b.pass();
    check(b.sync.last().downloaded == 2 && b.sync.last().uploaded == 0, "sync: a second install receives the character and the save");
    check(read(b.hero("ser-ada")) == character("Ser Ada", 1) && read(b.adventure()) == save(1), "sync: the received files hold the same data");
    check(timeOf(b.hero("ser-ada")) == start - 10000, "sync: a received file carries the change's time, not the time it arrived");
    check(b.sync.localChanges() == changes + 2, "sync: the game is told files changed");
    check(!fs::exists(b.folder / AccountSync::backupFolder), "sync: a file that replaced nothing leaves no backup");
    b.pass();
    check(!b.sync.last().any(), "sync: received files don't count as changed on the next pass");

    // ---- an edit goes up, the other install takes it and keeps its old copy
    put(a.hero("ser-ada"), character("Ser Ada", 2), start + 1000);
    a.pass();
    check(a.sync.last().uploaded == 1 && server.find(0, "ser-ada")->revision == 2, "sync: an edited character is sent as the next revision");
    check(server.find(0, "ser-ada", true) && level(server.find(0, "ser-ada", true)->body) == 1, "sync: the account keeps the copy it replaced");
    b.pass();
    check(b.sync.last().downloaded == 1 && level(read(b.hero("ser-ada"))) == 2, "sync: the other install receives the edit");
    check(level(read(b.kept("characters", "ser-ada"))) == 1, "sync: and keeps the file it replaced in sync-backup");

    // ---- both edit offline: the newer wins on both, the older is kept
    server.online = false;
    put(a.hero("ser-ada"), character("Ser Ada", 3), start + 5000);
    put(b.hero("ser-ada"), character("Ser Ada", 4), start + 8000);
    a.pass();
    b.pass();
    check(level(read(a.hero("ser-ada"))) == 3 && level(read(b.hero("ser-ada"))) == 4, "sync: offline edits stay on their own install");
    server.online = true;
    a.pass();
    b.pass();
    check(b.sync.last().uploaded == 1 && level(server.find(0, "ser-ada")->body) == 4 && server.find(0, "ser-ada")->revision == 4,
        "sync: of two offline edits the newer one ends up on the account");
    check(level(server.find(0, "ser-ada", true)->body) == 3, "sync: the older edit is the account's backup");
    a.pass();
    check(a.sync.last().downloaded == 1 && level(read(a.hero("ser-ada"))) == 4 && level(read(a.kept("characters", "ser-ada"))) == 3,
        "sync: the install with the older edit takes the newer one and keeps its own in sync-backup");

    // The other way round: the install that syncs second has the older edit.
    put(a.hero("ser-ada"), character("Ser Ada", 5), start + 20000);
    put(b.hero("ser-ada"), character("Ser Ada", 6), start + 15000);
    a.pass();
    b.pass();
    check(b.sync.last().downloaded == 1 && b.sync.last().uploaded == 0 && level(read(b.hero("ser-ada"))) == 5, "sync: an older local edit loses to the account's newer one");
    check(level(read(b.kept("characters", "ser-ada"))) == 6 && level(server.find(0, "ser-ada")->body) == 5, "sync: the losing edit is kept in sync-backup and never sent");

    // Another device gets in between the list and the write: the server refuses, and its copy is fetched.
    put(a.hero("ser-ada"), character("Ser Ada", 7), start + 30000);
    server.beforePut = [&server] { server.put(0, "ser-ada", start + 40000, character("Ser Ada", 8)); };
    a.pass();
    check(a.sync.last().uploaded == 0 && a.sync.last().downloaded == 1 && level(read(a.hero("ser-ada"))) == 8 && level(read(a.kept("characters", "ser-ada"))) == 7,
        "sync: a write the server refuses as older is replaced by the account's copy, and kept in sync-backup");
    b.pass();

    // ---- deletes travel too
    fs::remove(a.adventure()); // the adventure was finished
    server.now = start + 50000;
    a.pass();
    check(a.sync.last().removedThere == 1 && server.find(1, "adventure")->deleted && server.find(1, "adventure")->revision == 2,
        "sync: a save removed here is deleted on the account");
    check(server.find(1, "adventure", true) && server.find(1, "adventure", true)->body == save(1), "sync: the account keeps the deleted save as its backup");
    put(b.folder / "adventure.json.bak", save(0), start - 20000);
    b.pass();
    check(b.sync.last().removedHere == 1 && !fs::exists(b.adventure()) && !fs::exists(b.folder / "adventure.json.bak"),
        "sync: the other install removes the save and its .bak");
    check(read(b.kept("saves", "adventure")) == save(1), "sync: after keeping it in sync-backup");
    a.pass();
    b.pass();
    check(!a.sync.last().any() && !b.sync.last().any(), "sync: a delete is settled after one pass each");

    // Starting the adventure again brings the save back everywhere.
    put(b.adventure(), save(10), start + 60000);
    b.pass();
    a.pass();
    check(b.sync.last().uploaded == 1 && !server.find(1, "adventure")->deleted && a.sync.last().downloaded == 1 && read(a.adventure()) == save(10),
        "sync: a save written after the delete comes back on the account and the other install");

    // A character deleted here but changed elsewhere since is kept: the newer work wins.
    put(b.hero("ser-ada"), character("Ser Ada", 9), start + 70000);
    b.pass();
    fs::remove(a.hero("ser-ada"));
    a.pass();
    check(a.sync.last().removedThere == 0 && a.sync.last().downloaded == 1 && level(read(a.hero("ser-ada"))) == 9 && !server.find(0, "ser-ada")->deleted,
        "sync: a file deleted here but changed on the account since comes back instead");

    // ---- the graveyard: a dead character moves there on every install
    server.now = start + 75000;
    fs::create_directories(a.folder / "characters" / "graveyard");
    fs::rename(a.hero("ser-ada"), a.folder / "characters" / "graveyard" / "ser-ada.json");
    a.pass();
    check(a.sync.last().removedThere == 1 && a.sync.last().uploaded == 1 && server.find(0, "ser-ada")->deleted && server.find(0, "graveyard.ser-ada")
        && !server.find(0, "graveyard.ser-ada")->deleted, "sync: a retired character is deleted and sent again under its graveyard id");
    b.pass();
    check(!fs::exists(b.hero("ser-ada")) && level(read(b.folder / "characters" / "graveyard" / "ser-ada.json")) == 9,
        "sync: the other install moves the character to its graveyard");

    // ---- a server that doesn't answer: nothing is lost, the next pass sends it
    put(a.hero("grak"), character("Grak", 1), start + 80000);
    int passes = a.sync.passes();
    server.failNext = 1; // the list
    a.pass();
    check(a.sync.passes() == passes + 1 && a.sync.last().failed == 1 && a.sync.last().uploaded == 0 && !server.find(0, "grak"),
        "sync: a pass whose list isn't answered does nothing");
    a.pass();
    check(a.sync.last().uploaded == 1 && a.sync.last().failed == 0 && server.find(0, "grak"), "sync: the next pass sends it");
    put(a.hero("grak"), character("Grak", 2), start + 81000);
    put(a.adventure(), save(11), start + 81000);
    // One write isn't answered: the other still goes up, and the missed one follows next pass.
    server.beforePut = [&server] { server.failNext = 1; };
    a.pass();
    check(a.sync.last().uploaded == 1 && a.sync.last().failed == 1, "sync: a write that isn't answered doesn't stop the others");
    a.pass();
    check(a.sync.last().uploaded == 1 && a.sync.last().failed == 0 && level(server.find(0, "grak")->body) == 2 && server.find(1, "adventure")->body == save(11),
        "sync: and is sent by the next pass");

    // Answers that come on a later frame.
    put(a.hero("grak"), character("Grak", 3), start + 82000);
    server.hold = true;
    a.sync.request();
    a.sync.update(0.01);
    check(a.sync.running() && server.held.size() == 1, "sync: one call is out at a time while the pass waits");
    passes = a.sync.passes();
    for (int i = 0; i < 20 && a.sync.running(); i++)
    {
        server.release();
        a.sync.update(0.01);
    }
    server.hold = false;
    check(!a.sync.running() && a.sync.passes() == passes + 1 && a.sync.last().uploaded == 1 && level(server.find(0, "grak")->body) == 3,
        "sync: a pass whose answers arrive on later frames finishes the same");

    // Going offline in the middle drops the rest; it is sent after signing in again.
    put(a.hero("grak"), character("Grak", 4), start + 83000);
    server.hold = true;
    a.sync.request();
    a.sync.update(0.01);
    server.online = false;
    for (int i = 0; i < 20 && a.sync.running(); i++)
    {
        server.release();
        a.sync.update(0.01);
    }
    server.hold = false;
    check(!a.sync.running() && level(server.find(0, "grak")->body) == 3, "sync: a pass cut off by going offline stops");
    server.online = true;
    a.sync.update(0.01);
    check(a.sync.last().uploaded == 1 && level(server.find(0, "grak")->body) == 4, "sync: and the change goes up on signing in again");

    // ---- a pass runs by itself every `interval` seconds
    a.sync.interval = 30;
    a.pass();
    put(a.hero("grak"), character("Grak", 5), start + 84000);
    a.sync.update(10);
    check(level(server.find(0, "grak")->body) == 4, "sync: nothing is sent before the interval is up");
    a.sync.update(21);
    check(level(server.find(0, "grak")->body) == 5, "sync: a pass runs once the interval is up");
    a.sync.interval = 1e9;
    a.pass();

    // ---- files and records that are left alone
    {
        fs::create_directories(a.folder / "characters");
        std::ofstream(a.hero("broken"), std::ios::binary) << "not json";
        put(a.hero("has space"), character("Has Space", 1), start);
        put(a.hero("huge"), json{{"notes", std::string(AccountSync::maxCharacterBytes + 10, 'x')}}, start);
        server.put(1, "settings", start + 90000, json{{"server", "elsewhere"}});
        server.put(1, "adventure-..-evil", start + 90000, save(1));
        a.pass();
        check(a.sync.problems().size() == 3 && !server.find(0, "broken") && !server.find(0, "has space") && !server.find(0, "huge"),
            "sync: a broken file, a name that isn't an id and a file over the limit are left alone and named");
        check(read(a.folder / "settings.json").value("server", "") == "http://127.0.0.1:7350", "sync: a record named like another file of the game's is never written");
        check(fs::exists(a.folder / "adventure-..-evil.json") && !fs::exists(a.folder.parent_path() / "-evil.json"), "sync: an id can't climb out of the folder");
        fs::remove(a.hero("broken"));
        fs::remove(a.hero("has space"));
        fs::remove(a.hero("huge"));
    }

    // ---- another account: its records are separate and the old revisions are forgotten
    server.user = "bo";
    a.pass();
    check(read(a.folder / AccountSync::stateFile).value("account", "") == "bo" && server.find(0, "grak") && server.find(0, "grak")->revision == 1
        && level(server.find(0, "grak")->body) == 5, "sync: signing in to another account sends this install's files to it");
    server.user = "ada";
    check(server.find(0, "grak")->revision >= 5, "sync: and leaves the first account's records as they were");

    // ---- sync.json lost: the same files aren't taken for edits newer than the account's
    fs::remove(b.folder / AccountSync::stateFile);
    b.pass();
    const int revision = static_cast<int>(server.find(0, "grak")->revision);
    b.pass();
    check(level(read(b.hero("grak"))) == 5 && server.find(0, "grak")->revision == revision, "sync: without sync.json the account's copy is taken and nothing is sent");

    // ---- turning it off
    Device off(server, root / "off");
    off.sync.setFolder({});
    put(off.hero("nobody"), character("Nobody", 1), start);
    off.pass();
    check(off.sync.passes() == 0 && !server.find(0, "nobody"), "sync: without a folder nothing runs");
}
