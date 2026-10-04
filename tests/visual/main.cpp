#include "YoreholdGame.h"

#include <yorehold/framework/testing/TestBrowser.h>

#include <SDL3/SDL.h>
#include <SDL3/SDL_main.h>

#include <algorithm>

// Runs the whole game inside the browser, so client screens can be checked next to future gameplay scenes.
class TestSceneGame : public yh::TestScene
{
public:
    TestSceneGame() { game_.load(); }

    void update(double deltaSeconds) override { game_.update(deltaSeconds); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }

private:
    YoreholdGame game_;
};

class ReactionGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event enter{};
        enter.type = SDL_EVENT_KEY_DOWN;
        enter.key.key = SDLK_RETURN;
        handleEvent(enter);
        handleEvent(enter);
        newAdventure(7);
        setOptions({0, 0, true, true});
        if (!chapter_ || heroCount_ >= creatures_.size()) return;
        const size_t enemy = heroCount_;
        // An open strip of three squares, apart from the chapter's normal placements.
        yh::Cell at{1, 1};
        for (int y = 1; y < map().height() - 1; y++)
        {
            bool found = false;
            for (int x = 1; x < map().width() - 3; x++)
                if (walkable({x, y}) && walkable({x + 1, y}) && walkable({x + 2, y})
                    && !occupied({x, y}, 0) && !occupied({x + 1, y}, enemy) && !occupied({x + 2, y}, enemy))
                {
                    at = {x, y};
                    found = true;
                    break;
                }
            if (found) break;
        }
        creatures_[0].sheet.stats.setBase("dex", 1000);
        creatures_[enemy].sheet.stats.setBase("dex", 900);
        tokens_.tokens[0].position = grid_.center(at);
        tokens_.tokens[enemy].position = grid_.center({at.x + 1, at.y});
        startCombat(creatures_[enemy].group);
        while (currentCreature() && currentCreature() != enemy)
            endTurn();
        World::act("step", nlohmann::json{{"at", {at.x + 2, at.y}}}.dump());
    }
};

// Time is held so the two-second prompt can be inspected and clicked in the browser.
class TestSceneReactionPrompt : public yh::TestScene
{
public:
    TestSceneReactionPrompt() { game_.prepare(); }
    void update(double) override { game_.update(0); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    ReactionGame game_;
};

class SharedTurnGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event enter{};
        enter.type = SDL_EVENT_KEY_DOWN;
        enter.key.key = SDLK_RETURN;
        handleEvent(enter); handleEvent(enter);
        newAdventure(7);
        if (!chapter_ || heroCount_ < 2 || heroCount_ >= creatures_.size()) return;
        for (size_t i = 0; i < creatures_.size(); i++)
            creatures_[i].sheet.stats.setBase("dex", i < heroCount_ ? 1000.0f - static_cast<float>(i) * 100.0f : 10.0f);
        startCombat(creatures_[heroCount_].group);
        World::act("use", R"({"action":"defend"})");
        World::act("turn", R"({"creature":1})");
        update(2); // let the combat banner clear while the heroes wait for input
    }
};

class TestSceneSharedTurns : public yh::TestScene
{
public:
    TestSceneSharedTurns() { game_.prepare(); }
    void update(double seconds) override { game_.update(seconds); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    SharedTurnGame game_;
};

class PositioningGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event enter{}; enter.type = SDL_EVENT_KEY_DOWN; enter.key.key = SDLK_RETURN;
        handleEvent(enter); handleEvent(enter); newAdventure(7);
        if (!chapter_ || heroCount_ < 3 || heroCount_ >= creatures_.size()) return;
        yh::Cell at{1, 1};
        bool found = false;
        for (int y = 1; y < map().height() - 1 && !found; y++)
            for (int x = 1; x < map().width() - 4 && !found; x++)
            {
                bool open = true;
                for (int dx = 0; dx < 4; dx++) open &= walkable({x + dx, y}) && !occupied({x + dx, y}, 0);
                if (open) { at = {x, y}; found = true; }
            }
        if (!found) return;
        const size_t enemy = heroCount_;
        for (size_t i = 0; i < heroCount_; i++)
            creatures_[i].sheet.stats.setBase("dex", 1000.0f - static_cast<float>(i) * 100.0f);
        tokens_.tokens[0].position = grid_.center(at);
        tokens_.tokens[2].position = grid_.center({at.x + 1, at.y});
        tokens_.tokens[enemy].position = grid_.center({at.x + 2, at.y});
        tokens_.tokens[1].position = grid_.center({at.x + 3, at.y});
        for (auto& action : chapter_->actions)
            if (action.id == "strike") action.range = 6; // ranged test action; the shipped melee Strike stays as authored
        startCombat(creatures_[enemy].group);
        update(2);
    }
};

class TestScenePositioning : public yh::TestScene
{
public:
    TestScenePositioning() { game_.prepare(); }
    void update(double seconds) override { game_.update(seconds); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    PositioningGame game_;
};

class DeathGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event enter{};
        enter.type = SDL_EVENT_KEY_DOWN; enter.key.key = SDLK_RETURN;
        handleEvent(enter); handleEvent(enter);
        newAdventure(7);
        if (!chapter_ || heroCount_ < 4 || heroCount_ >= creatures_.size()) return;
        const int dex = creatures_[0].sheet.abilityScore("dex");
        creatures_[0].sheet.stats.setBase("dex", 1000);
        startCombat(creatures_[heroCount_].group);
        creatures_[0].sheet.stats.setBase("dex", static_cast<float>(dex));
        for (size_t i = 1; i < 4; i++) creatures_[i].sheet.hp = 0;
        creatures_[1].sheet.death.successes = 1; creatures_[1].sheet.death.failures = 2;
        creatures_[2].sheet.death.successes = 3; creatures_[2].sheet.death.stable = true;
        creatures_[3].sheet.death.failures = 3; creatures_[3].sheet.death.dead = true;
        fallenConditions();
        update(2);
    }
};

class TestSceneDeath : public yh::TestScene
{
public:
    TestSceneDeath() { game_.prepare(); }
    void update(double seconds) override { game_.update(seconds); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    DeathGame game_;
};

class MerchantGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event key{};
        key.type = SDL_EVENT_KEY_DOWN; key.key.key = SDLK_RETURN;
        handleEvent(key); handleEvent(key); newAdventure(7);
        if (!chapter_ || chapter_->npcs.empty()) return;
        const yh::Cell at = chapter_->npcs[0].at;
        tokens_.tokens[0].position = grid_.center({at.x - 1, at.y});
        creatures_[0].sheet.coins = 2000;
        update(0);
        key.key.key = SDLK_E;
        handleEvent(key);
    }
};

class TestSceneMerchant : public yh::TestScene
{
public:
    TestSceneMerchant() { game_.prepare(); }
    void update(double) override { game_.update(0); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    MerchantGame game_;
};

class ConsumableGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event key{};
        key.type = SDL_EVENT_KEY_DOWN; key.key.key = SDLK_RETURN;
        handleEvent(key); handleEvent(key); newAdventure(7);
        if (!chapter_ || heroCount_ < 2) return;
        creatures_[0].sheet.hp = 1;
        creatures_[0].sheet.inventory.push_back(*chapter_->compendium.item("ward-scroll"));
        creatures_[0].sheet.inventory.push_back(*chapter_->compendium.item("ember-scroll"));
        creatures_[1].sheet.takeDamage(1000, rules_);
        tokens_.tokens[1].position = grid_.center({cellOf(0).x, cellOf(0).y + 1});
        update(0);
        key.key.key = SDLK_I;
        handleEvent(key);
    }
};

class TestSceneConsumables : public yh::TestScene
{
public:
    TestSceneConsumables() { game_.prepare(); }
    void update(double) override { game_.update(0); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    ConsumableGame game_;
};

// A caster's turn with Mire armed and empty hands: the burst, the squares, the goblins it would
// slow and the range ruler follow the pointer. Flame fan (a cone from the caster) is the other.
class SpellGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event key{};
        key.type = SDL_EVENT_KEY_DOWN; key.key.key = SDLK_RETURN;
        handleEvent(key); handleEvent(key); newAdventure(7);
        if (!chapter_ || heroCount_ >= creatures_.size()) return;
        // The keep's party has no wizard: the first hero with slots learns the wizard's spells.
        size_t wizard = heroCount_;
        for (size_t i = 0; i < heroCount_ && wizard == heroCount_; i++)
            if (creatures_[i].sheet.resources.contains("slots-1"))
                wizard = i;
        if (wizard == heroCount_ || !findSpell("flame-fan") || !findSpell("mire")) return;
        creatures_[wizard].sheet.spells.insert(creatures_[wizard].sheet.spells.end(), {"flame-fan", "mire"});
        // An open strip: the wizard, then two enemies in a row.
        yh::Cell at{1, 1};
        bool found = false;
        for (int y = 2; y < map().height() - 2 && !found; y++)
            for (int x = 1; x < map().width() - 5 && !found; x++)
            {
                bool open = true;
                for (int dx = 0; dx < 5; dx++)
                    for (int dy = -1; dy <= 1; dy++) open &= walkable({x + dx, y + dy});
                if (open) { at = {x, y}; found = true; }
            }
        if (!found) return;
        yh::Character& sheet = creatures_[wizard].sheet;
        for (size_t i = 0; i < sheet.inventory.size(); i++)
            if (sheet.inventory[i].equipped && yh::Character::held(sheet.inventory[i])) sheet.unequip(i);
        const int dex = sheet.abilityScore("dex");
        sheet.stats.setBase("dex", 1000);
        // The camera centres the caster under the action bar, so the burst is aimed off to the
        // right where it can be seen; the cone is in the unit checks.
        tokens_.tokens[wizard].position = grid_.center(at);
        tokens_.tokens[heroCount_].position = grid_.center({at.x + 4, at.y});
        if (heroCount_ + 1 < creatures_.size() && creatures_[heroCount_ + 1].group == creatures_[heroCount_].group)
            tokens_.tokens[heroCount_ + 1].position = grid_.center({at.x + 5, at.y + 1});
        startCombat(creatures_[heroCount_].group);
        sheet.stats.setBase("dex", static_cast<float>(dex));
        while (currentCreature() && currentCreature() != wizard) endTurn();
        update(2); // shows the events first: a new adventure puts any armed action away
        armAction("mire");
        aimAt_ = grid_.center({at.x + 4, at.y});
    }
    // Where the pointer goes: the first goblin, wherever the camera has got to.
    yh::Vec2 aim() { return playCamera().worldToScreen(aimAt_); }
    yh::Vec2 aimAt_;
};

class TestSceneSpellTargeting : public yh::TestScene
{
public:
    TestSceneSpellTargeting() { game_.prepare(); }
    void update(double) override
    {
        // Until someone moves the mouse, the pointer stays on the goblin as the camera settles.
        if (!moved_)
        {
            SDL_Event move{};
            move.type = SDL_EVENT_MOUSE_MOTION;
            move.motion.x = game_.aim().x;
            move.motion.y = game_.aim().y;
            game_.handleEvent(move);
        }
        game_.update(1.0 / 60);
    }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override
    {
        moved_ |= event.type == SDL_EVENT_MOUSE_MOTION;
        return game_.handleEvent(event);
    }
private:
    SpellGame game_;
    bool moved_ = false;
};

// The spell panel (K) of the first hero who prepares spells, before the first fight: prepared and
// unprepared spells with their Prepare buttons, the focus pool beside the slots, a focus spell.
class PreparingGame : public YoreholdGame
{
public:
    void prepare()
    {
        load();
        SDL_Event key{};
        key.type = SDL_EVENT_KEY_DOWN; key.key.key = SDLK_RETURN;
        handleEvent(key); handleEvent(key); newAdventure(7);
        if (!chapter_) return;
        size_t caster = heroCount_;
        for (size_t i = 0; i < heroCount_ && caster == heroCount_; i++)
            if (!creatures_[i].sheet.preparable.empty())
                caster = i;
        if (caster == heroCount_) return;
        for (size_t i = 0; i < heroCount_; i++)
            tokens_.tokens[i].selected = i == caster;
        // Something to choose between: the wizard's list beside the hero's own.
        yh::Character& sheet = creatures_[caster].sheet;
        for (const char* id : {"flame-fan", "mire"})
            if (findSpell(id) && std::find(sheet.preparable.begin(), sheet.preparable.end(), id) == sheet.preparable.end())
                sheet.preparable.push_back(id);
        update(0);
        key.key.key = SDLK_K;
        handleEvent(key);
    }
};

class TestScenePreparing : public yh::TestScene
{
public:
    TestScenePreparing() { game_.prepare(); }
    void update(double) override { game_.update(0); }
    void draw(yh::Renderer& renderer) override { game_.draw(renderer); }
    bool handleEvent(const SDL_Event& event) override { return game_.handleEvent(event); }
private:
    PreparingGame game_;
};

int main(int argc, char** argv)
{
    yh::TestBrowser browser;
    browser.add<TestSceneGame>("Game");
    browser.add<TestSceneReactionPrompt>("Reaction prompt");
    browser.add<TestSceneSharedTurns>("Shared turns");
    browser.add<TestScenePositioning>("Flanking/cover");
    browser.add<TestSceneDeath>("Death saves");
    browser.add<TestSceneMerchant>("Merchants");
    browser.add<TestSceneConsumables>("Consumables");
    browser.add<TestSceneSpellTargeting>("Spell targeting");
    browser.add<TestScenePreparing>("Preparing spells");

    yh::HostSettings settings;
    settings.title = "yorehold tests";
    settings.feedbackDir = YH_FEEDBACK_DIR;
    settings.stateDir = YH_DEV_STATE_DIR;
    return browser.run(argc, argv, settings);
}
