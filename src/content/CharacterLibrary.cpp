#include "CharacterLibrary.h"

#include <yorehold/framework/rpg/Character.h>
#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <filesystem>
#include <stdexcept>

namespace
{

namespace fs = std::filesystem;

// "Ser Ada (2)" -> "ser-ada-2"; empty when the name has no letters or digits.
std::string plainName(const std::string& name)
{
    std::string plain;
    for (const char c : name)
    {
        const char lower = c >= 'A' && c <= 'Z' ? static_cast<char>(c - 'A' + 'a') : c;
        if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            plain += lower;
        else if (!plain.empty() && plain.back() != '-')
            plain += '-';
    }
    while (!plain.empty() && plain.back() == '-') plain.pop_back();
    return plain.substr(0, 60);
}

// The first of base.json, base-2.json... that isn't taken in `folder`.
fs::path freeFile(const fs::path& folder, const std::string& base)
{
    fs::path path = folder / (base + ".json");
    for (int n = 2; fs::exists(path); n++)
        path = folder / (base + "-" + std::to_string(n) + ".json");
    return path;
}

// The inventory is written exactly as a sheet writes it, so the two never drift apart.
nlohmann::json inventoryJson(const std::vector<yh::Item>& inventory)
{
    yh::Character holder;
    holder.inventory = inventory;
    return nlohmann::json::parse(holder.toJson()).at("inventory");
}

}

std::string CharacterLibrary::Entry::fileName() const
{
    return fs::path(path).filename().string();
}

const yh::SaveFormat& CharacterLibrary::format()
{
    static const yh::SaveFormat format("yorehold.character", 1);
    return format;
}

std::optional<CharacterLibrary::Entry> CharacterLibrary::read(const std::string& path, std::string* error)
{
    std::string problem;
    const std::optional<std::string> text = format().readFile(path, &problem);
    try
    {
        if (!text)
            throw std::runtime_error(problem);
        const nlohmann::json data = nlohmann::json::parse(*text);
        Entry entry;
        entry.path = path;
        entry.retired = fs::path(path).parent_path().filename() == graveyardFolder;
        const std::optional<yh::CharacterChoices> choices = yh::CharacterChoices::fromJson(data.at("choices").dump(), &problem);
        if (!choices)
            throw std::runtime_error("choices: " + problem);
        if (choices->name.empty() || choices->levels.empty())
            throw std::runtime_error("a character needs a name and at least one level");
        entry.choices = *choices;
        const nlohmann::json inventory = data.value("inventory", nlohmann::json::array());
        if (!inventory.is_array())
            throw std::runtime_error("inventory must be a list");
        const std::optional<yh::Character> holder = yh::Character::fromJson(nlohmann::json{{"inventory", inventory}}.dump(), &problem);
        if (!holder)
            throw std::runtime_error("inventory: " + problem);
        entry.inventory = holder->inventory;
        entry.coins = data.value("coins", 0);
        if (entry.coins < 0)
            throw std::runtime_error("coins can't be negative");
        entry.away = data.value("away", std::string{});
        return entry;
    }
    catch (const std::exception& e)
    {
        if (error)
            *error = fs::path(path).filename().string() + ": " + e.what();
        return std::nullopt;
    }
}

std::vector<CharacterLibrary::Entry> CharacterLibrary::list(const std::string& folder, std::vector<std::string>* problems)
{
    std::vector<Entry> living, retired;
    for (const fs::path& dir : {fs::path(folder), fs::path(folder) / graveyardFolder})
    {
        std::error_code missing;
        for (const fs::directory_entry& file : fs::directory_iterator(dir, missing))
        {
            if (!file.is_regular_file() || file.path().extension() != ".json")
                continue;
            std::string error;
            if (std::optional<Entry> entry = read(file.path().string(), &error))
                (entry->retired ? retired : living).push_back(std::move(*entry));
            else if (problems)
                problems->push_back(error);
        }
    }
    const auto byName = [](const Entry& a, const Entry& b) { return std::pair(a.choices.name, a.path) < std::pair(b.choices.name, b.path); };
    std::sort(living.begin(), living.end(), byName);
    std::sort(retired.begin(), retired.end(), byName);
    living.insert(living.end(), retired.begin(), retired.end());
    return living;
}

bool CharacterLibrary::write(const std::string& folder, Entry& entry, std::string* error)
{
    try
    {
        if (entry.retired || (!entry.path.empty() && fs::path(entry.path).parent_path().filename() == graveyardFolder))
            throw std::runtime_error("characters in the graveyard can't be changed");
        if (entry.choices.name.empty() || entry.choices.levels.empty())
            throw std::runtime_error("a character needs a name and at least one level");
        if (entry.coins < 0)
            throw std::runtime_error("coins can't be negative");
        fs::create_directories(folder);
        if (entry.path.empty())
        {
            const std::string base = plainName(entry.choices.name);
            entry.path = freeFile(folder, base.empty() ? "character" : base).string();
        }
        nlohmann::json data;
        data["choices"] = nlohmann::json::parse(entry.choices.toJson());
        data["inventory"] = inventoryJson(entry.inventory);
        data["coins"] = entry.coins;
        data["away"] = entry.away;
        std::string problem;
        if (!format().writeFile(entry.path, data.dump(2), &problem))
            throw std::runtime_error(problem);
        return true;
    }
    catch (const std::exception& e)
    {
        if (error)
            *error = e.what();
        return false;
    }
}

bool CharacterLibrary::retire(const std::string& folder, Entry& entry, std::string* error)
{
    if (entry.retired)
        return true;
    std::error_code problem;
    const fs::path graveyard = fs::path(folder) / graveyardFolder;
    fs::create_directories(graveyard, problem);
    const fs::path to = freeFile(graveyard, fs::path(entry.path).stem().string());
    fs::rename(entry.path, to, problem);
    if (problem)
    {
        if (error)
            *error = "couldn't move " + entry.fileName() + " to the graveyard: " + problem.message();
        return false;
    }
    fs::remove(entry.path + ".bak", problem); // a backup left behind would come back as a live file
    entry.path = to.string();
    entry.retired = true;
    return true;
}

std::optional<CharacterLibrary::Entry> CharacterLibrary::find(const std::string& folder, const std::string& fileName, std::string* error)
{
    // Saves name the file only; anything with a folder in it isn't one of ours.
    if (fileName.empty() || fs::path(fileName).filename().string() != fileName || fs::path(fileName).extension() != ".json")
    {
        if (error)
            *error = "not a character file name: " + fileName;
        return std::nullopt;
    }
    return read((fs::path(folder) / fileName).string(), error);
}
