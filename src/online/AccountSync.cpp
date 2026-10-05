#include "AccountSync.h"

#include <yorehold/framework/save/SaveFile.h>

#include <chrono>
#include <cstdio>
#include <filesystem>
#include <set>
#include <string_view>
#include <system_error>
#include <utility>

namespace
{

namespace fs = std::filesystem;
using nlohmann::json;

constexpr size_t characters = 0;
constexpr size_t saves = 1;
constexpr std::string_view graveyardPrefix = "graveyard.";
constexpr int maxFailures = 3; // a pass gives up after this many unanswered calls

// The server's rule for ids, plus no leading dot so an id is always a plain file name.
bool validId(std::string_view id)
{
    if (id.empty() || id.size() > 64 || id.front() == '.')
        return false;
    for (const char c : id)
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.'))
            return false;
    return true;
}

// Only files the game itself would write are synced, so nothing the server sends can land on
// another file in the folder (the settings, say).
bool usable(size_t kind, std::string_view id)
{
    if (!validId(id))
        return false;
    if (kind == saves)
        return id == "adventure" || id.starts_with("adventure-");
    return !id.starts_with(graveyardPrefix) || validId(id.substr(graveyardPrefix.size()));
}

// FNV-1a: the same on every build, unlike std::hash.
std::string hashOf(std::string_view text)
{
    uint64_t hash = 14695981039346656037ull;
    for (const char c : text)
    {
        hash ^= static_cast<unsigned char>(c);
        hash *= 1099511628211ull;
    }
    char hex[17];
    std::snprintf(hex, sizeof hex, "%016llx", static_cast<unsigned long long>(hash));
    return hex;
}

int64_t fileTime(const fs::path& path)
{
    std::error_code error;
    const fs::file_time_type time = fs::last_write_time(path, error);
    if (error)
        return 0;
    const auto system = std::chrono::clock_cast<std::chrono::system_clock>(time);
    return std::chrono::duration_cast<std::chrono::milliseconds>(system.time_since_epoch()).count();
}

void setFileTime(const fs::path& path, int64_t milliseconds)
{
    const std::chrono::sys_time<std::chrono::milliseconds> system{std::chrono::milliseconds(milliseconds)};
    std::error_code error;
    fs::last_write_time(path, std::chrono::time_point_cast<fs::file_time_type::duration>(std::chrono::clock_cast<fs::file_time_type::clock>(system)), error);
}

int64_t number(const json& object, const char* field)
{
    const auto found = object.find(field);
    return found != object.end() && found->is_number() ? found->get<int64_t>() : 0;
}

bool flag(const json& object, const char* field)
{
    const auto found = object.find(field);
    return found != object.end() && found->is_boolean() && found->get<bool>();
}

}

const AccountSync::Kind AccountSync::kinds[2] = {
    {"characters", "characters_list", "characters_get", "characters_put", "characters_delete", "characters", "id", "updatedAt", "character", maxCharacterBytes},
    {"saves", "saves_list", "saves_get", "saves_put", "saves_delete", "saves", "adventure", "savedAt", "save", maxSaveBytes},
};

AccountSync::AccountSync(AccountServer& server)
    : server_(server)
    , clock_([] { return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count(); })
{
}

void AccountSync::setFolder(std::string folder)
{
    if (folder == folder_)
        return;
    // A pass for the old folder must not write into the new one.
    pass_++;
    running_ = waiting_ = signedIn_ = false;
    jobs_.clear();
    folder_ = std::move(folder);
}

std::string AccountSync::summary() const
{
    std::string text;
    const auto add = [&text](int count, const char* what) {
        if (count == 0)
            return;
        text += (text.empty() ? "" : ", ") + std::to_string(count) + " " + what;
    };
    add(last_.uploaded, "sent");
    add(last_.downloaded, "received");
    add(last_.removedHere, "removed here");
    add(last_.removedThere, "removed there");
    add(last_.failed, "not answered");
    add(static_cast<int>(problems_.size()), "left alone");
    return text.empty() ? text : "Account sync: " + text;
}

void AccountSync::update(double deltaSeconds)
{
    timer_ -= deltaSeconds;
    if (!running_)
    {
        const bool signedIn = !folder_.empty() && !server_.account().empty();
        if (signedIn && !signedIn_)
            wanted_ = true; // just came online: send what was done offline
        signedIn_ = signedIn;
        if (!signedIn)
            return;
        if (timer_ <= 0)
            wanted_ = true;
        if (!wanted_)
            return;
        begin();
    }
    // An answer may arrive inside the call, so this carries on until one is still out.
    while (running_ && !waiting_)
        step();
}

void AccountSync::begin()
{
    running_ = true;
    waiting_ = false;
    wanted_ = false;
    pass_++;
    counts_ = {};
    problems_.clear();
    jobs_.clear();
    remote_[characters].clear();
    remote_[saves].clear();
    listing_ = 0;
    account_ = server_.account();
    loadState();
}

void AccountSync::step()
{
    if (listing_ < 2)
        listRemote(listing_);
    else if (listing_ == 2)
    {
        plan();
        listing_++;
    }
    else if (jobs_.empty() || counts_.failed >= maxFailures || server_.account() != account_)
        finish();
    else
    {
        const Job job = jobs_.front();
        jobs_.pop_front();
        run(job);
    }
}

void AccountSync::finish()
{
    saveState();
    last_ = counts_;
    passes_++;
    pass_++;
    running_ = waiting_ = false;
    jobs_.clear();
    timer_ = interval;
}

void AccountSync::call(const char* rpc, const json& payload, std::function<void(const json*)> then)
{
    waiting_ = true;
    server_.call(rpc, payload, [this, pass = pass_, then = std::move(then)](std::optional<json> answer) {
        if (pass != pass_)
            return;
        waiting_ = false;
        const bool answered = answer && answer->is_object();
        if (!answered)
            counts_.failed++;
        then(answered ? &*answer : nullptr);
    });
}

void AccountSync::listRemote(size_t kind)
{
    call(kinds[kind].list, json{{"headers", true}, {"deleted", true}}, [this, kind](const json* answer) {
        const Kind& k = kinds[kind];
        const auto list = answer ? answer->find(k.listField) : json::const_iterator();
        if (!answer || list == answer->end() || !list->is_array())
        {
            // Without the whole list nothing can be decided: try again next time.
            if (answer)
                counts_.failed++;
            finish();
            return;
        }
        for (const json& header : *list)
        {
            if (!header.is_object() || !header.contains(k.idField) || !header[k.idField].is_string())
                continue;
            const std::string id = header[k.idField].get<std::string>();
            if (usable(kind, id))
                remote_[kind][id] = Remote{number(header, "revision"), number(header, k.timeField), flag(header, "deleted")};
        }
        listing_++;
    });
}

void AccountSync::plan()
{
    for (size_t kind = 0; kind < 2; kind++)
    {
        const std::map<std::string, std::string> files = localFiles(kind);
        std::set<std::string> ids;
        for (const auto& [id, path] : files) ids.insert(id);
        for (const auto& [id, remote] : remote_[kind]) ids.insert(id);
        for (const auto& [id, known] : known_[kind]) ids.insert(id);

        for (const std::string& id : ids)
        {
            const auto file = files.find(id);
            std::optional<Local> local;
            if (file != files.end())
            {
                local = readLocal(kind, id, file->second);
                if (!local)
                    continue; // unreadable or too large: left alone, already noted
            }
            const auto knownAt = known_[kind].find(id);
            const Known* known = knownAt != known_[kind].end() ? &knownAt->second : nullptr;
            const auto remoteAt = remote_[kind].find(id);
            const Remote* remote = remoteAt != remote_[kind].end() ? &remoteAt->second : nullptr;

            // A file that is gone counts as a change only if the last pass saw it.
            const bool localChanged = local ? !known || known->hash != local->hash : known && !known->hash.empty();
            const bool remoteChanged = remote && (!known || known->revision != remote->revision);
            // Both changed: the newer one wins, and the account's on a tie.
            const bool localWins = local && localChanged && (!remoteChanged || local->time > remote->time);

            if (!remote)
            {
                if (local)
                    jobs_.push_back({Job::Type::Upload, kind, id});
                else
                    known_[kind].erase(id);
            }
            else if (localWins)
                jobs_.push_back({Job::Type::Upload, kind, id});
            else if (remote->deleted)
            {
                if (local)
                    removeHere(kind, id, remote->revision);
                else
                    known_[kind][id] = Known{remote->revision, {}};
            }
            else if (local)
            {
                if (remoteChanged)
                    jobs_.push_back({Job::Type::Download, kind, id});
            }
            else if (localChanged && !remoteChanged)
                jobs_.push_back({Job::Type::RemoveThere, kind, id});
            else
                // Deleted here but changed there since: the newer work is kept.
                jobs_.push_back({Job::Type::Download, kind, id});
        }
    }
}

void AccountSync::run(const Job& job)
{
    switch (job.type)
    {
    case Job::Type::Upload: upload(job.kind, job.id); break;
    case Job::Type::Download: download(job.kind, job.id); break;
    case Job::Type::RemoveThere: removeThere(job.kind, job.id); break;
    }
}

void AccountSync::upload(size_t kind, const std::string& id)
{
    const Kind& k = kinds[kind];
    const std::optional<Local> local = readLocal(kind, id, pathFor(kind, id));
    if (!local)
        return;
    const json payload = {{k.idField, id}, {k.timeField, local->time}, {k.bodyField, local->body}};
    call(k.put, payload, [this, kind, id, hash = local->hash](const json* answer) {
        if (!answer)
            return;
        const int64_t revision = number(*answer, "revision");
        if (flag(*answer, "stored"))
        {
            known_[kind][id] = Known{revision, hash};
            counts_.uploaded++;
        }
        // The account holds something newer after all (another device got there first).
        else if (flag(*answer, "deleted"))
            removeHere(kind, id, revision);
        else
            jobs_.push_front({Job::Type::Download, kind, id});
    });
}

void AccountSync::download(size_t kind, const std::string& id)
{
    call(kinds[kind].get, json{{kinds[kind].idField, id}}, [this, kind, id](const json* answer) {
        if (!answer)
            return;
        const Kind& k = kinds[kind];
        const auto body = answer->find(k.bodyField);
        if (body == answer->end() || !body->is_object())
        {
            problems_.push_back(std::string(k.name) + "/" + id + ": the account's copy isn't a file the game can use");
            return;
        }
        const std::string path = pathFor(kind, id);
        std::error_code error;
        if (fs::exists(path, error) && !backUp(kind, id, path))
            return;
        fs::create_directories(fs::path(path).parent_path(), error);
        std::string problem;
        if (!yh::writeFileAtomically(path, body->dump(2), false, &problem))
        {
            problems_.push_back(std::string(k.name) + "/" + id + ": " + problem);
            return;
        }
        // The file is as old as the change it holds, so a later pass doesn't take it for newer.
        if (const int64_t time = number(*answer, k.timeField); time > 0)
            setFileTime(path, time);
        known_[kind][id] = Known{number(*answer, "revision"), hashOf(body->dump())};
        counts_.downloaded++;
        localChanges_++;
    });
}

void AccountSync::removeThere(size_t kind, const std::string& id)
{
    const Kind& k = kinds[kind];
    // When the file was deleted isn't known, only that it was before now.
    call(k.remove, json{{k.idField, id}, {k.timeField, clock_()}}, [this, kind, id](const json* answer) {
        if (!answer)
            return;
        const int64_t revision = number(*answer, "revision");
        if (flag(*answer, "deleted"))
        {
            known_[kind][id] = Known{revision, {}};
            if (flag(*answer, "stored"))
                counts_.removedThere++;
        }
        else if (revision > 0)
            jobs_.push_front({Job::Type::Download, kind, id}); // a newer copy stands
        else
            known_[kind].erase(id);
    });
}

void AccountSync::removeHere(size_t kind, const std::string& id, int64_t revision)
{
    const std::string path = pathFor(kind, id);
    std::error_code error;
    if (fs::exists(path, error))
    {
        if (!backUp(kind, id, path))
            return;
        fs::remove(path, error);
        if (error)
        {
            problems_.push_back(std::string(kinds[kind].name) + "/" + id + ": couldn't remove the file: " + error.message());
            return;
        }
        fs::remove(path + ".bak", error); // the game reads a .bak when the file itself is missing
        counts_.removedHere++;
        localChanges_++;
    }
    known_[kind][id] = Known{revision, {}};
}

std::string AccountSync::pathFor(size_t kind, const std::string& id) const
{
    const fs::path folder(folder_);
    if (kind == saves)
        return (folder / (id + ".json")).string();
    if (id.starts_with(graveyardPrefix))
        return (folder / "characters" / "graveyard" / (id.substr(graveyardPrefix.size()) + ".json")).string();
    return (folder / "characters" / (id + ".json")).string();
}

std::map<std::string, std::string> AccountSync::localFiles(size_t kind)
{
    std::map<std::string, std::string> files;
    const fs::path folder(folder_);
    const auto scan = [&](const fs::path& dir, const std::string& prefix) {
        std::error_code missing;
        for (const fs::directory_entry& file : fs::directory_iterator(dir, missing))
        {
            if (!file.is_regular_file(missing) || file.path().extension() != ".json")
                continue;
            const std::string stem = file.path().stem().string();
            if (kind == saves && stem != "adventure" && !stem.starts_with("adventure-"))
                continue; // the settings and the game's other files
            const std::string id = prefix + stem;
            // A plain character file named like a graveyard id would be read back into the graveyard.
            if (!usable(kind, id) || (kind == characters && prefix.empty() && stem.starts_with(graveyardPrefix)))
            {
                problems_.push_back(std::string(kinds[kind].name) + "/" + file.path().filename().string() + ": the name can't be used as an id on the account");
                continue;
            }
            files[id] = file.path().string();
        }
    };
    if (kind == saves)
        scan(folder, {});
    else
    {
        scan(folder / "characters", {});
        scan(folder / "characters" / "graveyard", std::string(graveyardPrefix));
    }
    return files;
}

std::optional<AccountSync::Local> AccountSync::readLocal(size_t kind, const std::string& id, const std::string& path)
{
    const Kind& k = kinds[kind];
    const std::optional<std::string> text = yh::readTextFile(path);
    if (!text)
        return std::nullopt; // gone since the folder was listed; the next pass sees that
    Local local;
    local.path = path;
    local.body = json::parse(*text, nullptr, false);
    if (!local.body.is_object())
    {
        problems_.push_back(std::string(k.name) + "/" + id + ": the file isn't one the game wrote");
        return std::nullopt;
    }
    const std::string plain = local.body.dump();
    if (plain.size() > k.maxBytes)
    {
        problems_.push_back(std::string(k.name) + "/" + id + ": too large for the account (" + std::to_string(plain.size() / 1024) + " KB)");
        return std::nullopt;
    }
    local.hash = hashOf(plain);
    local.time = fileTime(path);
    return local;
}

bool AccountSync::backUp(size_t kind, const std::string& id, const std::string& path)
{
    const fs::path to = fs::path(folder_) / backupFolder / kinds[kind].name / (id + ".json");
    std::error_code error;
    fs::create_directories(to.parent_path(), error);
    fs::copy_file(path, to, fs::copy_options::overwrite_existing, error);
    if (error)
        problems_.push_back(std::string(kinds[kind].name) + "/" + id + ": couldn't keep a backup, so the file was left as it is: " + error.message());
    return !error;
}

void AccountSync::loadState()
{
    known_[characters].clear();
    known_[saves].clear();
    const std::optional<std::string> text = yh::readTextFile((fs::path(folder_) / stateFile).string());
    const json state = text ? json::parse(*text, nullptr, false) : json();
    // Another account's revisions mean nothing here: start over and let the newest win.
    if (!state.is_object() || state.value("format", std::string()) != "yorehold.sync" || !state.contains("account")
        || !state["account"].is_string() || state["account"].get<std::string>() != account_)
        return;
    for (size_t kind = 0; kind < 2; kind++)
    {
        const auto records = state.find(kinds[kind].name);
        if (records == state.end() || !records->is_object())
            continue;
        for (const auto& [id, record] : records->items())
            if (record.is_object() && usable(kind, id))
                known_[kind][id] = Known{number(record, "revision"), record.contains("hash") && record["hash"].is_string() ? record["hash"].get<std::string>() : std::string()};
    }
}

void AccountSync::saveState()
{
    json state = {{"format", "yorehold.sync"}, {"version", 1}, {"account", account_}};
    for (size_t kind = 0; kind < 2; kind++)
    {
        json records = json::object();
        for (const auto& [id, known] : known_[kind])
            records[id] = {{"revision", known.revision}, {"hash", known.hash}};
        state[kinds[kind].name] = std::move(records);
    }
    std::string problem;
    if (!yh::writeFileAtomically((fs::path(folder_) / stateFile).string(), state.dump(2), false, &problem))
        problems_.push_back(std::string(stateFile) + ": " + problem);
}
