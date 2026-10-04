#include "World.h"

#include <yorehold/framework/graphics/Lighting.h>

bool World::isFlanked(size_t creature) const
{
    if (!chapter_ || !chapter_->positioning.enabled || chapter_->positioning.flankingCondition.empty()
        || creature >= creatures_.size() || !fighting() || creatures_[creature].sheet.down()) return false;
    std::vector<yh::Cell> foes;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        const auto index = orderIndex(i);
        if (index && encounter_->order()[*index].standing() && creatures_[i].team != creatures_[creature].team
            && !creatures_[i].sheet.hasFlag(rules_, "cantAct")) foes.push_back(cellOf(i));
    }
    return yh::isFlanked(grid_, cellOf(creature), foes, chapter_->positioning.flankingReach,
        [this](yh::Vec2 a, yh::Vec2 b) { return !yh::lineOfSight(a, b, map().walls()); });
}

yh::Cover World::coverFrom(size_t from, size_t target) const
{
    if (!chapter_ || !chapter_->positioning.enabled || from >= creatures_.size() || target >= creatures_.size()) return yh::Cover::None;
    const yh::Cell a = cellOf(from), b = cellOf(target);
    const yh::Cover terrain = yh::coverBetween(grid_, a, b,
        [this](yh::Vec2 start, yh::Vec2 end) { return !yh::lineOfSight(start, end, map().walls()); });
    if (terrain != yh::Cover::None || !chapter_->positioning.creaturesProvideCover) return terrain;
    std::vector<yh::Wall> bodies;
    for (size_t i = 0; i < creatures_.size(); i++)
    {
        if (i == from || i == target || creatures_[i].sheet.down() || tokens_.tokens[i].floor != 0 || creatures_[i].fled) continue;
        const yh::Vec2 centre = grid_.center(cellOf(i));
        const float radius = grid_.size() * 0.25f;
        const yh::Vec2 nw = centre - yh::Vec2{radius, radius}, se = centre + yh::Vec2{radius, radius};
        const yh::Vec2 ne{se.x, nw.y}, sw{nw.x, se.y};
        bodies.push_back({nw, ne}); bodies.push_back({ne, se}); bodies.push_back({se, sw}); bodies.push_back({sw, nw});
    }
    const auto screened = yh::coverBetween(grid_, a, b,
        [&bodies](yh::Vec2 start, yh::Vec2 end) { return !yh::lineOfSight(start, end, bodies); });
    // Standing creatures screen a shot but cannot make it impossible to shoot through the crowd.
    return screened == yh::Cover::None ? yh::Cover::None : yh::Cover::Half;
}

int World::positionalArmorClass(size_t target) const
{
    if (target >= creatures_.size()) return 0;
    const yh::Character& sheet = creatures_[target].sheet;
    if (!isFlanked(target) || sheet.hasCondition(chapter_->positioning.flankingCondition)) return sheet.armorClass(rules_);
    yh::Character shown = sheet;
    shown.addCondition(rules_, chapter_->positioning.flankingCondition);
    return shown.armorClass(rules_);
}

int World::attackArmorClass(size_t from, size_t target, bool ranged) const
{
    const int base = positionalArmorClass(target);
    if (!chapter_ || !chapter_->positioning.enabled || (!ranged && !chapter_->positioning.coverAgainstMelee)) return base;
    const auto cover = coverFrom(from, target);
    return base + (cover == yh::Cover::Half ? chapter_->positioning.halfCoverArmorClass
        : cover == yh::Cover::ThreeQuarters ? chapter_->positioning.threeQuartersCoverArmorClass : 0);
}
