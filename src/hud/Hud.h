#pragma once

#include "sim/World.h"

#include <yorehold/framework/ui/Ui.h>

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
    bool inSession = false;        // co-op: cards show who plays each hero
    bool guest = false;            // joined someone else's game: only the host can start it again
};

namespace hud
{

void partyCards(Hud& hud);      // top left: one card per hero
void initiativeStrip(Hud& hud); // top: the turn order in a fight
void combatBar(Hud& hud);       // bottom left in a fight: movement, actions, Dash and End turn
void exploreBar(Hud& hud);      // under the cards between fights: rests and sneaking; Try again once it's over
void dialoguePanel(Hud& hud);   // the conversation going on, with its replies
// The active quests (top right), or with `open` the whole journal.
void journalPanel(Hud& hud, bool open, yh::Font* title);

}
