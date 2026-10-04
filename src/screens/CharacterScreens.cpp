// Play > Characters: the player's characters, making a new one in three steps, and levelling one
// up. The sheet beside them is rebuilt from the choices after every click.

#include "YoreholdGame.h"

#include <yorehold/framework/graphics/Renderer.h>

#include <SDL3/SDL_timer.h>

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <filesystem>
#include <functional>

namespace
{

std::string signedNumber(int n)
{
    return n >= 0 ? "+" + std::to_string(n) : std::to_string(n);
}

}

void YoreholdGame::openCharacters()
{
    loadCharacters();
    menu_ = Menu::Characters;
}

void YoreholdGame::loadCharacters()
{
    draft_.reset();
    creationError_.clear();
    if (!ContentLibrary::creation(YH_GAME_ASSETS, packages_, creationRules_, creationCompendium_, &creationError_))
        creationCompendium_ = {};
    std::vector<std::string> problems;
    characters_ = charactersDir().empty() ? std::vector<CharacterLibrary::Entry>{} : CharacterLibrary::list(charactersDir(), &problems);
    for (const std::string& problem : problems)
        std::fprintf(stderr, "Characters: %s\n", problem.c_str());
    if (!problems.empty())
    {
        notice_ = "Couldn't read " + std::to_string(problems.size()) + " character file" + (problems.size() == 1 ? "" : "s") + ": " + problems.front();
        noticeBad_ = true;
    }
    showCharacter(std::min(characterPick_, characters_.empty() ? size_t{0} : characters_.size() - 1));
}

void YoreholdGame::showCharacter(size_t index)
{
    characterPick_ = index;
    pickedSheet_.reset();
    pickedProblem_.clear();
    if (index < characters_.size())
        pickedSheet_ = creationCompendium_.build(creationRules_, characters_[index].choices, &pickedProblem_);
}

void YoreholdGame::newCharacter(Menu back)
{
    if (creationCompendium_.classes.empty())
        return;
    draftBack_ = back;
    draftDice_ = yh::Random(SDL_GetTicks());
    draft_.emplace(creationRules_, creationCompendium_);
    menu_ = Menu::NewCharacter;
}

void YoreholdGame::drawCharacters(const yh::Rect& screen)
{
    const float top = screen.h * 0.22f, listX = screen.w / 2 - 470, listW = 440, h = 44, gap = 10;
    float y = top;
    const size_t perPage = 6, pages = std::max<size_t>(1, (characters_.size() + perPage - 1) / perPage);
    if (characterPage_ >= pages)
        characterPage_ = 0;
    std::optional<size_t> picked;
    for (size_t i = characterPage_ * perPage; i < characters_.size() && i < (characterPage_ + 1) * perPage; i++)
    {
        const CharacterLibrary::Entry& c = characters_[i];
        std::string text = c.choices.name + "  (level " + std::to_string(c.choices.level()) + ")";
        if (c.retired)
            text += "  graveyard";
        else if (!c.away.empty())
            text += "  away";
        if (ui_.toggle({listX, y, listW, h}, text, i == characterPick_))
            picked = i;
        y += h + gap;
    }
    if (characters_.empty())
    {
        ui_.label({listX, y + 8}, "No characters yet. Each one can go into any adventure.", ui_.theme.textDim);
        y += 40;
    }
    if (pages > 1 && ui_.button({listX, y, listW, h}, "More (" + std::to_string(characterPage_ + 1) + "/" + std::to_string(pages) + ")"))
        characterPage_ = (characterPage_ + 1) % pages;
    y = std::max(y + (pages > 1 ? h + gap : 0), top + 4 * (h + gap));

    const CharacterLibrary::Entry* chosen = characterPick_ < characters_.size() ? &characters_[characterPick_] : nullptr;
    const bool canLevel = chosen && !chosen->retired && chosen->away.empty() && pickedSheet_
        && creationRules_.levelForXp(chosen->choices.xp) > chosen->choices.level();
    const float half = (listW - gap) / 2;
    if (ui_.button({listX, y, half, h}, "New character", !creationCompendium_.classes.empty()))
        newCharacter();
    if (ui_.button({listX + half + gap, y, half, h}, "Level up", canLevel))
    {
        draftDice_ = yh::Random(SDL_GetTicks());
        draftBack_ = Menu::Characters;
        draft_.emplace(CharacterDraft::levelUp(creationRules_, creationCompendium_, chosen->choices));
        menu_ = Menu::LevelUp;
    }
    y += h + gap;
    if (chosen && !chosen->retired && !chosen->away.empty())
        ui_.label({listX, y + 4}, "Away in an adventure until it ends.", ui_.theme.textDim);
    else if (chosen && chosen->retired)
        ui_.label({listX, y + 4}, "In the graveyard: kept to look at, never played again.", ui_.theme.textDim);
    else if (chosen && !canLevel)
        ui_.label({listX, y + 4}, "Levels up when its XP reaches the next level.", ui_.theme.textDim);
    y += 34;
    if (ui_.button({listX, y, listW, h}, "Back (Esc)"))
        openMenu(Menu::Play);
    if (!creationError_.empty())
        ui_.label({listX, y + h + gap}, creationError_, ui_.theme.bad);

    const yh::Rect sheetArea{screen.w / 2 + 10, top, 460, std::max(380.0f, y + h - top)};
    if (chosen)
        drawSheet(sheetArea, pickedSheet_ ? &*pickedSheet_ : nullptr, chosen->choices, pickedProblem_);
    if (chosen && pickedSheet_ && !chosen->inventory.empty())
    {
        std::string carried = "Carrying:";
        for (size_t i = 0; i < chosen->inventory.size() && i < 5; i++)
            carried += (i ? ", " : " ") + chosen->inventory[i].name;
        if (chosen->inventory.size() > 5)
            carried += "...";
        ui_.label({sheetArea.x + 16, sheetArea.y + sheetArea.h - 34}, carried, ui_.theme.textDim);
    }
    if (picked)
        showCharacter(*picked);
}

void YoreholdGame::drawDraft(const yh::Rect& screen)
{
    if (!draft_)
    {
        menu_ = draftBack_;
        return;
    }
    CharacterDraft& d = *draft_;
    const float top = screen.h * 0.22f, x = screen.w / 2 - 470, w = 440, h = 34, gap = 8;
    float y = top;

    // A grid of toggles, `columns` across; returns the clicked id.
    auto grid = [&](const std::vector<std::string>& ids, const std::function<std::string(const std::string&)>& name,
                    const std::function<bool(const std::string&)>& on, int columns) {
        std::optional<std::string> clicked;
        const float cell = (w - gap * static_cast<float>(columns - 1)) / static_cast<float>(columns);
        for (size_t i = 0; i < ids.size(); i++)
        {
            const float cx = x + static_cast<float>(i % columns) * (cell + gap);
            const float cy = y + static_cast<float>(i / columns) * (h + gap);
            if (ui_.toggle({cx, cy, cell, h}, name(ids[i]), on(ids[i])))
                clicked = ids[i];
        }
        y += static_cast<float>((ids.size() + columns - 1) / columns) * (h + gap);
        return clicked;
    };
    auto heading = [&](const std::string& text) {
        ui_.label({x, y + 4}, text, ui_.theme.accent);
        y += 30;
    };
    auto className = [&](const std::string& id) { return creationCompendium_.classes.at(id).name; };
    auto classPicker = [&] {
        const std::string current = d.choices().levels.back().classId;
        if (const auto id = grid(d.classIds(), className, [&](const std::string& c) { return c == current; }, 3); id && *id != current)
            d.setClass(*id);
    };

    if (d.levellingUp())
        heading(d.choices().name + " reaches level " + std::to_string(d.choices().level()) + ": pick a class");
    else
    {
        // The three steps; an earlier one can always be reopened.
        const float tab = (w - 2 * gap) / 3;
        for (int i = 0; i < CharacterDraft::steps; i++)
        {
            const bool open = i <= d.step || (i == d.step + 1 && d.stepDone(d.step));
            if (ui_.toggle({x + static_cast<float>(i) * (tab + gap), y, tab, h}, std::to_string(i + 1) + ". " + CharacterDraft::stepNames[i], i == d.step) && open)
                d.step = i;
        }
        y += h + 16;
    }

    if (d.levellingUp())
        classPicker();
    if (d.step == 0)
    {
        heading("Name");
        std::string name = d.choices().name;
        ui_.textBox("character-name", {x, y, w, h + 4}, name, 64);
        if (name != d.choices().name)
            d.setName(name);
        y += h + 4 + gap;
        if (!creationCompendium_.races.empty())
        {
            heading("Race");
            const auto id = grid(d.raceIds(), [&](const std::string& r) { return creationCompendium_.races.at(r).name; },
                [&](const std::string& r) { return r == d.choices().race; }, 4);
            if (id) d.setRace(*id);
        }
        if (!creationCompendium_.backgrounds.empty())
        {
            heading("Background");
            const auto id = grid(d.backgroundIds(), [&](const std::string& b) { return creationCompendium_.backgrounds.at(b).name; },
                [&](const std::string& b) { return b == d.choices().background; }, 3);
            if (id) d.setBackground(*id);
        }
    }
    else if (d.step == 1)
    {
        heading("Class");
        classPicker();
        const std::string& method = d.choices().scoreMethod;
        heading(method == "pointBuy" ? "Ability scores: " + std::to_string(d.pointsLeft()) + " of " + std::to_string(creationRules_.scoreMethods.pointBudget) + " points left"
            : method == "roll" ? std::string("Ability scores: click Roll again to reroll") : std::string("Ability scores: + and - swap two scores"));
        const std::pair<const char*, const char*> methods[] = {{"array", "Standard array"}, {"pointBuy", "Point buy"}, {"roll", "Roll"}};
        const float third = (w - 2 * gap) / 3;
        for (int i = 0; i < 3; i++)
            if (ui_.toggle({x + static_cast<float>(i) * (third + gap), y, third, h}, methods[i].second, method == methods[i].first))
            {
                if (method != methods[i].first)
                    d.setMethod(methods[i].first, draftDice_);
                else if (method == "roll")
                    d.reroll(draftDice_);
            }
        y += h + gap;
        const float row = 30;
        for (const yh::AbilityDefinition& ability : creationRules_.abilities)
        {
            const int score = d.choices().scores.count(ability.id) ? d.choices().scores.at(ability.id) : 0;
            ui_.label({x, y + 5}, ability.name);
            ui_.label({x + 170, y + 5}, std::to_string(score) + "  (" + signedNumber(creationRules_.abilityModifier(score)) + ")");
            if (d.choices().scoreMethod != "roll")
            {
                if (ui_.button({x + w - 2 * row - gap, y, row, row - 2}, "-", d.canLower(ability.id)))
                    d.lower(ability.id);
                if (ui_.button({x + w - row, y, row, row - 2}, "+", d.canRaise(ability.id)))
                    d.raise(ability.id);
            }
            y += row;
        }
    }
    else
    {
        if (d.skillPicks() > 0)
        {
            heading("Skills: train " + std::to_string(d.skillPicks()));
            const auto& picked = d.picked("skills");
            const auto id = grid(d.skillOptions(), [&](const std::string& s) { return creationRules_.skill(s)->name; },
                [&](const std::string& s) { return std::find(picked.begin(), picked.end(), s) != picked.end(); }, 3);
            if (id) d.toggleSkill(*id);
        }
        for (const std::string& kind : d.featKinds())
        {
            heading(std::string(1, static_cast<char>(std::toupper(static_cast<unsigned char>(kind[0])))) + kind.substr(1) + " feat");
            const auto& picked = d.picked("feats");
            const auto id = grid(d.featOptions(kind), [&](const std::string& f) { return creationCompendium_.feats.at(f).name; },
                [&](const std::string& f) { return std::find(picked.begin(), picked.end(), f) != picked.end(); }, 2);
            if (id) d.pickFeat(*id);
        }
        if (d.skillPicks() == 0 && d.featKinds().empty())
            ui_.label({x, y + 4}, "Nothing to pick at this level.", ui_.theme.textDim);
        y += 34;
    }

    // What is still missing, then the way on.
    y = std::max(y + gap, top + 424);
    const std::string missing = d.stepProblem(d.step);
    if (!missing.empty())
        ui_.label({x, y + 4}, missing, ui_.theme.bad);
    y += 30;
    const float third = (w - 2 * gap) / 3;
    if (ui_.button({x, y, third, h + 6}, "Cancel (Esc)"))
    {
        draft_.reset();
        menu_ = draftBack_;
        return;
    }
    if (!d.levellingUp() && d.step > 0 && ui_.button({x + third + gap, y, third, h + 6}, "Back"))
        d.step--;
    const bool last = d.levellingUp() || d.step == CharacterDraft::steps - 1;
    if (!last && ui_.button({x + 2 * (third + gap), y, third, h + 6}, "Next", d.stepDone(d.step)))
        d.step++;
    else if (last && ui_.button({x + 2 * (third + gap), y, third, h + 6}, d.levellingUp() ? "Level up" : "Finish", d.finished()))
    {
        finishDraft();
        return;
    }

    drawSheet({screen.w / 2 + 10, top, 460, y + h + 6 - top}, d.sheet() ? &*d.sheet() : nullptr, d.choices(), d.problem());
}

void YoreholdGame::drawSheet(const yh::Rect& area, const yh::Character* sheet, const yh::CharacterChoices& choices, const std::string& problem)
{
    ui_.panel(area);
    const float x = area.x + 16, w = area.w - 32;
    float y = area.y + 14;
    ui_.label({x, y}, choices.name.empty() ? std::string("New character") : choices.name, ui_.theme.accent);
    y += 30;
    if (!sheet)
    {
        ui_.label({x, y}, problem.empty() ? std::string("Not ready yet.") : problem, ui_.theme.bad);
        return;
    }
    std::string who = "Level " + std::to_string(sheet->level);
    if (!sheet->ancestry.empty()) who += " " + sheet->ancestry;
    who += " " + sheet->characterClass;
    if (const yh::BackgroundDefinition* background = creationCompendium_.background(choices.background))
        who += ", " + background->name;
    ui_.label({x, y}, who);
    y += 28;
    ui_.label({x, y}, "HP " + std::to_string(sheet->maxHp()) + "    AC " + std::to_string(sheet->armorClass(creationRules_))
        + "    Speed " + std::to_string(sheet->speedFeet()) + " ft    XP " + std::to_string(choices.xp));
    y += 34;
    // Scores two to a row, then trained skills and feats as wrapped lists.
    const float column = w / 3;
    for (size_t i = 0; i < creationRules_.abilities.size(); i++)
    {
        const yh::AbilityDefinition& a = creationRules_.abilities[i];
        std::string upper = a.id;
        std::transform(upper.begin(), upper.end(), upper.begin(), [](unsigned char c) { return static_cast<char>(std::toupper(c)); });
        ui_.label({x + static_cast<float>(i % 3) * column, y}, upper + " " + std::to_string(sheet->abilityScore(a.id)) + " ("
            + signedNumber(sheet->abilityModifier(creationRules_, a.id)) + ")");
        if (i % 3 == 2 || i + 1 == creationRules_.abilities.size())
            y += 28;
    }
    y += 8;
    auto wrapped = [&](const std::string& title, const std::vector<std::string>& names) {
        if (names.empty()) return;
        std::string line = title;
        for (size_t i = 0; i < names.size(); i++)
        {
            const std::string next = line + (i ? ", " : " ") + names[i];
            const float width = ui_.theme.font ? ui_.theme.font->measure(next) : static_cast<float>(next.size()) * 9;
            if (width > w && i > 0)
            {
                ui_.label({x, y}, line + ",", ui_.theme.textDim);
                y += 26;
                line = "    " + names[i];
            }
            else
                line = next;
        }
        ui_.label({x, y}, line, ui_.theme.textDim);
        y += 30;
    };
    std::vector<std::string> skills;
    for (const yh::SkillDefinition& skill : creationRules_.skills)
        if (sheet->proficiencies.contains(skill.id) || (sheet->proficiencyRanks.contains(skill.id) && sheet->proficiencyRanks.at(skill.id) != creationRules_.untrainedRank))
            skills.push_back(skill.name + " " + signedNumber(sheet->checkModifier(creationRules_, skill.id)));
    wrapped("Skills:", skills);
    std::vector<std::string> feats;
    auto addFeats = [&](const std::vector<std::string>& ids) {
        for (const std::string& id : ids)
            if (const yh::FeatDefinition* feat = creationCompendium_.feat(id)) feats.push_back(feat->name);
    };
    if (const yh::RaceDefinition* race = creationCompendium_.race(choices.race)) addFeats(race->feats);
    if (const yh::BackgroundDefinition* background = creationCompendium_.background(choices.background)) addFeats(background->feats);
    for (const yh::LevelChoice& level : choices.levels)
        if (const auto picked = level.picks.find("feats"); picked != level.picks.end()) addFeats(picked->second);
    wrapped("Feats:", feats);
    std::vector<std::string> resources;
    for (const auto& [id, resource] : sheet->resources)
    {
        // "second-wind" -> "Second wind"
        std::string name = id;
        std::replace(name.begin(), name.end(), '-', ' ');
        name[0] = static_cast<char>(std::toupper(static_cast<unsigned char>(name[0])));
        resources.push_back(name + " " + std::to_string(resource.max));
    }
    wrapped("Uses:", resources);
}

void YoreholdGame::openParty()
{
    if (!chapter_)
        return;
    loadCharacters();
    partyChoice_.assign(chapter_->party.size(), std::nullopt);
    partySeat_ = 0;
    menu_ = Menu::Party;
}

void YoreholdGame::releaseCharacters()
{
    if (testRun_ || client_ || charactersDir().empty() || savePath().empty())
        return;
    const std::string save = std::filesystem::path(savePath()).filename().string();
    for (CharacterLibrary::Entry& entry : CharacterLibrary::list(charactersDir()))
        if (!entry.retired && entry.away == save)
        {
            entry.away.clear();
            std::string error;
            if (!CharacterLibrary::write(charactersDir(), entry, &error))
                std::fprintf(stderr, "Characters: %s\n", error.c_str());
        }
}

void YoreholdGame::startParty()
{
    if (!chapter_)
        return;
    std::vector<std::optional<PartyPick>> picks;
    for (const std::optional<CharacterLibrary::Entry>& choice : partyChoice_)
        picks.push_back(choice ? std::optional(PartyPick{choice->choices, choice->inventory, choice->fileName(), choice->coins}) : std::nullopt);
    releaseCharacters();
    setParty(std::move(picks));
    newAdventure(SDL_GetTicks());
    menu_ = Menu::None;
    notice_.clear();
    saveAdventure(); // the characters brought along are away in this adventure from now on
}

void YoreholdGame::drawParty(const yh::Rect& screen)
{
    if (!chapter_ || partyChoice_.size() != chapter_->party.size())
    {
        menu_ = Menu::Play;
        return;
    }
    const float top = screen.h * 0.22f, x = screen.w / 2 - 470, w = 440, h = 38, gap = 8;
    float y = top;
    const size_t seats = chapter_->party.size();
    ui_.label({x, y}, chapter_->title + ": level " + std::to_string(chapter_->level) + ", " + std::to_string(seats) + " heroes", ui_.theme.accent);
    y += 32;
    auto className = [&](const std::string& id) {
        const yh::ClassDefinition* c = chapter_->compendium.characterClass(id);
        return c ? c->name : id;
    };
    for (size_t i = 0; i < seats; i++)
    {
        const std::optional<CharacterLibrary::Entry>& choice = partyChoice_[i];
        const std::string who = choice ? choice->choices.name + "  (level " + std::to_string(choice->choices.level()) + ")"
                                       : chapter_->party[i].name + "  (ready-made " + className(chapter_->party[i].classId) + ")";
        if (ui_.toggle({x, y, w, h}, std::to_string(i + 1) + ". " + who, i == partySeat_))
            partySeat_ = i;
        y += h + gap;
    }
    y += 8;
    ui_.label({x, y}, "Who takes seat " + std::to_string(partySeat_ + 1) + "?", ui_.theme.textDim);
    y += 28;
    // The ready-made hero, then every character free to come: not away, not in the graveyard,
    // not already in another seat.
    if (ui_.toggle({x, y, w, h}, "Ready-made: " + chapter_->party[partySeat_].name, !partyChoice_[partySeat_]))
        partyChoice_[partySeat_].reset();
    y += h + gap;
    size_t shown = 0;
    const std::string thisSave = savePath().empty() ? std::string() : std::filesystem::path(savePath()).filename().string();
    for (const CharacterLibrary::Entry& c : characters_)
    {
        const bool seated = std::any_of(partyChoice_.begin(), partyChoice_.end(), [&](const auto& other) { return other && other->path == c.path; });
        const bool here = partyChoice_[partySeat_] && partyChoice_[partySeat_]->path == c.path;
        // Away in this very adventure: starting it over frees them.
        const bool free = c.away.empty() || c.away == thisSave;
        if (c.retired || !free || (seated && !here) || shown == 4)
            continue;
        if (ui_.toggle({x, y, w, h}, c.choices.name + "  (level " + std::to_string(c.choices.level()) + ")", here))
            partyChoice_[partySeat_] = c;
        y += h + gap;
        shown++;
    }
    if (ui_.button({x, y, w, h}, "New character for this seat", !creationCompendium_.classes.empty()))
    {
        newCharacter(Menu::Party);
        return;
    }
    y = std::max(y + h + gap * 2, top + 424);
    const float half = (w - gap) / 2;
    if (ui_.button({x, y, half, h + 6}, "Back (Esc)"))
        openMenu(Menu::Play);
    if (ui_.button({x + half + gap, y, half, h + 6}, "Start (Enter)"))
    {
        startParty();
        return;
    }

    const yh::Rect sheetArea{screen.w / 2 + 10, top, 460, y + h + 6 - top};
    if (const std::optional<CharacterLibrary::Entry>& choice = partyChoice_[partySeat_])
    {
        std::string problem;
        const std::optional<yh::Character> sheet = creationCompendium_.build(creationRules_, choice->choices, &problem);
        drawSheet(sheetArea, sheet ? &*sheet : nullptr, choice->choices, problem);
    }
    else
    {
        ui_.panel(sheetArea);
        const Chapter::PartyMember& member = chapter_->party[partySeat_];
        ui_.label({sheetArea.x + 16, sheetArea.y + 14}, member.name, ui_.theme.accent);
        ui_.label({sheetArea.x + 16, sheetArea.y + 44}, "Level " + std::to_string(chapter_->level) + " " + className(member.classId));
        ui_.label({sheetArea.x + 16, sheetArea.y + 78}, "Ready-made for this adventure: its scores", ui_.theme.textDim);
        ui_.label({sheetArea.x + 16, sheetArea.y + 104}, "are rolled when the adventure starts.", ui_.theme.textDim);
    }
}

void YoreholdGame::finishDraft()
{
    if (!draft_ || !draft_->finished())
        return;
    const bool levelling = draft_->levellingUp();
    CharacterLibrary::Entry entry;
    if (levelling && characterPick_ < characters_.size())
        entry = characters_[characterPick_];
    else
        entry.inventory = draft_->sheet()->inventory; // a new character starts with its class's and background's gear
    entry.choices = draft_->choices();
    draft_.reset();
    // Made for an adventure written for a higher level: it brings the XP to level up to it.
    const bool forParty = draftBack_ == Menu::Party && chapter_;
    if (forParty && !levelling && chapter_->level > 1 && !creationRules_.xpForLevel.empty())
        entry.choices.xp = creationRules_.xpForLevel[std::min<size_t>(chapter_->level - 2, creationRules_.xpForLevel.size() - 1)];
    std::string error;
    if (charactersDir().empty())
    {
        notice_ = "This run doesn't keep characters.";
        noticeBad_ = true;
    }
    else if (!CharacterLibrary::write(charactersDir(), entry, &error))
    {
        notice_ = "Couldn't save " + entry.choices.name + ": " + error;
        noticeBad_ = true;
    }
    else
    {
        notice_ = levelling ? entry.choices.name + " is now level " + std::to_string(entry.choices.level()) + "." : "Saved " + entry.choices.name + ".";
        noticeBad_ = false;
    }
    loadCharacters();
    menu_ = forParty ? Menu::Party : Menu::Characters;
    for (size_t i = 0; i < characters_.size(); i++)
        if (characters_[i].path == entry.path)
        {
            characterPage_ = i / 6;
            showCharacter(i);
            if (forParty && partySeat_ < partyChoice_.size())
                partyChoice_[partySeat_] = characters_[i];
        }
}
