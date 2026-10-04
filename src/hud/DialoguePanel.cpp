#include "Hud.h"

#include <algorithm>

void hud::dialoguePanel(Hud& hud)
{
    World& world = hud.world;
    yh::Ui& ui = hud.ui;
    const yh::DialogueSession& talk = *world.talk();
    const yh::DialogueNode* node = talk.current();
    if (!node)
        return;
    const yh::Rect screen = hud.renderer.bounds();
    const std::vector<const yh::DialogueChoice*> choices = talk.choices();
    // Between the party cards and the log.
    const float width = std::clamp(screen.w - 300 - 460, 360.0f, 720.0f);
    const float line = ui.lineHeight();
    std::vector<std::string> lines{node->text};
    if (ui.theme.font)
        lines = ui.theme.font->wrap(node->text, width - 32);
    const size_t buttons = std::max<size_t>(1, choices.size());
    const float height = 16 + line + lines.size() * line + 10 + buttons * 44 + 8;
    const yh::Rect area{300, screen.h - height - 10, width, height};
    ui.panel(area);
    hud.panels.push_back(area);

    float y = area.y + 12;
    const std::string speaker = node->speaker.empty() ? world.creatures()[world.talkWith()].sheet.name : node->speaker;
    ui.label({area.x + 16, y}, speaker, ui.theme.accent);
    y += line;
    for (const std::string& text : lines)
    {
        ui.label({area.x + 16, y}, text);
        y += line;
    }
    y += 10;
    if (choices.empty())
    {
        if (ui.button({area.x + 16, y, area.w - 32, 38}, "1. (Leave)"))
            world.act("reply", nlohmann::json{{"choice", 0}, {"hero", world.leaderIndex()}}.dump());
        return;
    }
    for (size_t i = 0; i < choices.size(); i++, y += 44)
    {
        std::string text = std::to_string(i + 1) + ". " + choices[i]->text;
        if (choices[i]->check)
            text += "  [" + choices[i]->check->skill + " " + std::to_string(choices[i]->check->difficulty) + "]";
        if (ui.button({area.x + 16, y, area.w - 32, 38}, text))
        {
            world.act("reply", nlohmann::json{{"choice", i}, {"hero", world.leaderIndex()}}.dump());
            return; // the conversation may be gone now
        }
    }
}
