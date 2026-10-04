#pragma once

#include "sim/World.h"

#include <yorehold/framework/ui/Ui.h>

#include <optional>
#include <string>
#include <vector>

// What every HUD panel draws with. Panels read the world and send intents through act(); the
// party cards also pick the leader, which is this machine's own selection.
struct Hud
{
    World& world;
    yh::Renderer& renderer;
    yh::Ui& ui;
    const yh::Input& input;
    std::vector<yh::Rect>& panels; // where each panel went, so clicks on them don't walk the party
    std::string& armed;            // the aimed action a click on an enemy uses; empty = the first the creature has
    std::optional<size_t>& giving; // gear panel: the item waiting for someone to be handed to
    std::optional<size_t>& looting; // the pile whose contents are shown
    bool inSession = false;        // co-op: cards show who plays each hero
    bool guest = false;            // joined someone else's game: only the host can start it again
};

namespace hud
{

void partyCards(Hud& hud);      // top left: one card per hero
void initiativeStrip(Hud& hud); // top: the turn order in a fight
void combatBar(Hud& hud);       // bottom left in a fight: movement, actions left, and a button for each action the acting creature has
// The aimed action a click on an enemy uses for `creature`: `armed` if it has it, else its first.
const yh::ActionDefinition* armedAction(const World& world, size_t creature, const std::string& armed);
void exploreBar(Hud& hud);      // under the cards between fights: rests and sneaking; Try again once it's over
void dialoguePanel(Hud& hud);   // the conversation going on, with its replies
// The active quests (top right), or with `open` the whole journal.
void journalPanel(Hud& hud, bool open, yh::Font* title);
void inventoryPanel(Hud& hud);  // I: what the selected (or acting) hero carries, to put on, put away or hand over
// Between fights, beside something to take: a button to open it (E), or its contents once open.
void lootPanel(Hud& hud);

}
