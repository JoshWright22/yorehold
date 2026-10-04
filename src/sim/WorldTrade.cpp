#include "World.h"

#include <stdexcept>

const yh::Merchant* World::merchant(size_t npc) const
{
    return npc < merchants_.size() && merchants_[npc] ? &*merchants_[npc] : nullptr;
}

bool World::canTrade(size_t hero, size_t npc) const
{
    const size_t creature = npcToken(npc);
    return merchant(npc) && hero < heroCount_ && creature < creatures_.size() && !creatures_[hero].sheet.down()
        && creatures_[creature].team == 2 && !creatures_[creature].sheet.down() && !creatures_[creature].fled
        && !fighting() && !talk_ && !inCutscene_ && tokens_.tokens[hero].path.empty() && adjacent(hero, creature);
}

std::optional<size_t> World::merchantNear(size_t hero) const
{
    for (size_t npc = 0; npc < merchants_.size(); npc++)
        if (canTrade(hero, npc)) return npc;
    return std::nullopt;
}

nlohmann::json World::merchantsJson() const
{
    auto saved = nlohmann::json::array();
    for (const auto& merchant : merchants_)
        saved.push_back(merchant ? nlohmann::json::parse(merchant->toJson()) : nlohmann::json(nullptr));
    return saved;
}

std::vector<std::optional<yh::Merchant>> World::merchantsFrom(const nlohmann::json& saved) const
{
    if (!saved.is_array() || saved.size() != chapter_->npcs.size())
        throw std::runtime_error("saved merchants don't match the chapter");
    std::vector<std::optional<yh::Merchant>> merchants;
    for (size_t i = 0; i < saved.size(); i++)
    {
        const auto& definition = chapter_->npcs[i].merchant;
        if (saved[i].is_null() != !definition)
            throw std::runtime_error("saved merchant is missing or unexpected");
        std::optional<yh::Merchant> merchant;
        if (definition)
        {
            std::string error;
            merchant = yh::Merchant::fromJson(saved[i].dump(), {}, &error);
            if (!merchant || merchant->buyMultiplier != definition->buyMultiplier || merchant->sellMultiplier != definition->sellMultiplier)
                throw std::runtime_error("invalid saved merchant: " + error);
        }
        merchants.push_back(std::move(merchant));
    }
    return merchants;
}
