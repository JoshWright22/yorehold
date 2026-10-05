#pragma once

#include "AccountServer.h"

#include <nlohmann/json.hpp>

#include <cstdint>
#include <deque>
#include <functional>
#include <map>
#include <optional>
#include <string>
#include <vector>

// Keeps the characters and adventure saves beside the game's own files the same as the copies
// on the player's account. The files stay the truth while offline; a pass runs at sign-in, when
// the game asks for one after writing, and every `interval` seconds. Per record the newest
// change wins, and the copy that lost is kept: the server keeps the one it replaced, and a local
// file a pass replaces or removes goes to `sync-backup/` first.
//
// What changed since the last pass is worked out from `sync.json` (the server's revision and the
// file's contents as they were then), so edits and deletions made offline are sent later.
class AccountSync
{
public:
    static constexpr const char* stateFile = "sync.json";
    static constexpr const char* backupFolder = "sync-backup";
    // The server's limits; a larger file is left alone and named in problems().
    static constexpr size_t maxCharacterBytes = 256 * 1024;
    static constexpr size_t maxSaveBytes = 1024 * 1024;

    struct Counts
    {
        int uploaded = 0;      // files sent to the account
        int downloaded = 0;    // files written from the account
        int removedHere = 0;   // files removed because another device deleted them
        int removedThere = 0;  // records deleted on the account because the file was deleted here
        int failed = 0;        // calls the server didn't answer
        bool any() const { return uploaded || downloaded || removedHere || removedThere || failed; }
    };

    explicit AccountSync(AccountServer& server);

    // The folder holding `adventure*.json` and `characters/`. "" turns sync off.
    void setFolder(std::string folder);
    // Milliseconds since 1970; the system clock unless a check replaces it.
    void setClock(std::function<int64_t()> clock) { clock_ = std::move(clock); }
    double interval = 30;

    // Asks for a pass as soon as one can run (after the game wrote or removed a file).
    void request() { wanted_ = true; }
    // Call once per frame.
    void update(double deltaSeconds);

    bool running() const { return running_; }
    int passes() const { return passes_; } // finished, whether or not every call was answered
    const Counts& last() const { return last_; }
    // Goes up whenever a pass wrote or removed one of the game's files.
    int localChanges() const { return localChanges_; }
    // Files the last pass left alone and why.
    const std::vector<std::string>& problems() const { return problems_; }
    // One line about the last pass, "" if it had nothing to do.
    std::string summary() const;

private:
    struct Kind
    {
        const char* name;      // the key in sync.json and the folder under sync-backup/
        const char* list;      // RPCs
        const char* get;
        const char* put;
        const char* remove;
        const char* listField; // the array in the list's answer
        const char* idField;
        const char* timeField;
        const char* bodyField;
        size_t maxBytes;
    };
    static const Kind kinds[2];

    struct Known // what the last pass left on both sides
    {
        int64_t revision = 0;
        std::string hash; // of the file; "" = no file (deleted)
    };
    struct Remote
    {
        int64_t revision = 0;
        int64_t time = 0;
        bool deleted = false;
    };
    struct Local
    {
        std::string path;
        nlohmann::json body;
        std::string hash;
        int64_t time = 0;
    };
    struct Job
    {
        enum class Type { Upload, Download, RemoveThere };
        Type type;
        size_t kind;
        std::string id;
    };

    void begin();
    void step();
    void finish();
    void listRemote(size_t kind);
    void plan();
    void run(const Job& job);
    void upload(size_t kind, const std::string& id);
    void download(size_t kind, const std::string& id);
    void removeThere(size_t kind, const std::string& id);
    void removeHere(size_t kind, const std::string& id, int64_t revision);
    // Calls the server; `then` runs with the answer (null = none) unless the pass was dropped
    // meanwhile.
    void call(const char* rpc, const nlohmann::json& payload, std::function<void(const nlohmann::json*)> then);

    std::string pathFor(size_t kind, const std::string& id) const;
    std::map<std::string, std::string> localFiles(size_t kind); // id -> path
    std::optional<Local> readLocal(size_t kind, const std::string& id, const std::string& path);
    bool backUp(size_t kind, const std::string& id, const std::string& path);
    void loadState();
    void saveState();

    AccountServer& server_;
    std::function<int64_t()> clock_;
    std::string folder_;
    std::string account_; // the one the pass in progress is for
    std::map<std::string, Known> known_[2];
    std::map<std::string, Remote> remote_[2];
    std::deque<Job> jobs_;
    size_t listing_ = 0;
    bool running_ = false;
    bool waiting_ = false; // a call is out
    bool wanted_ = false;
    bool signedIn_ = false;
    double timer_ = 0;
    int pass_ = 0; // answers to an earlier pass are ignored
    int passes_ = 0;
    int localChanges_ = 0;
    Counts counts_, last_;
    std::vector<std::string> problems_;
};
