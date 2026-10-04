#include "Hud.h"

// A small tracker of the active quests (top right); J opens the whole journal.
void hud::journalPanel(Hud& hud, bool open, yh::Font* title)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::Rect screen = hud.renderer.bounds();
    const std::vector<yh::QuestEntry> entries = world.journal()->entries(world.flags());
    const float line = ui.lineHeight();
    if (!open)
    {
        std::vector<const yh::QuestEntry*> active;
        for (const yh::QuestEntry& e : entries)
            if (e.progress.status == yh::QuestStatus::Active)
                active.push_back(&e);
        if (active.empty() || world.fighting())
            return;
        float rows = 0;
        for (const yh::QuestEntry* e : active)
            rows += 1 + e->quest->objectives.size();
        const yh::Rect area{screen.w - 350, 10, 340, 14 + rows * line};
        ui.panel(area);
        hud.panels.push_back(area);
        float y = area.y + 7;
        for (const yh::QuestEntry* e : active)
        {
            ui.label({area.x + 12, y}, e->quest->title, ui.theme.accent);
            y += line;
            for (size_t i = 0; i < e->quest->objectives.size(); i++, y += line)
                ui.label({area.x + 22, y}, (e->progress.objectiveComplete[i] ? "[x] " : "[ ] ") + e->quest->objectives[i].text,
                    e->progress.objectiveComplete[i] ? ui.theme.textDim : ui.theme.text);
        }
        return;
    }

    const yh::Rect area{screen.w / 2 - 300, screen.h * 0.12f, 600, screen.h * 0.7f};
    ui.panel(area);
    hud.panels.push_back(area);
    if (title)
        title->drawCentered(hud.renderer, {area.x, area.y + 10, area.w, 60}, "Journal", {255, 214, 140, 255});
    float y = area.y + 84;
    if (entries.empty())
        ui.label({area.x + 24, y}, "Nothing yet.", ui.theme.textDim);
    for (const yh::QuestEntry& e : entries)
    {
        const char* status = e.progress.status == yh::QuestStatus::Completed ? "  (complete)"
            : e.progress.status == yh::QuestStatus::Failed                 ? "  (failed)"
                                                                           : "";
        ui.label({area.x + 24, y}, e.quest->title + status,
            e.progress.status == yh::QuestStatus::Active ? ui.theme.accent : ui.theme.textDim);
        y += line;
        if (!e.quest->description.empty())
        {
            std::vector<std::string> lines{e.quest->description};
            if (ui.theme.font)
                lines = ui.theme.font->wrap(e.quest->description, area.w - 60);
            for (const std::string& text : lines)
            {
                ui.label({area.x + 36, y}, text, ui.theme.textDim);
                y += line;
            }
        }
        for (size_t i = 0; i < e.quest->objectives.size(); i++, y += line)
            ui.label({area.x + 36, y}, (e.progress.objectiveComplete[i] ? "[x] " : "[ ] ") + e.quest->objectives[i].text);
        y += 12;
    }
    ui.label({area.x + 24, area.y + area.h - 34}, "J or Esc: close", ui.theme.textDim);
}
