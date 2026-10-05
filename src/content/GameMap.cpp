#include "GameMap.h"

#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/rpg/Effect.h>

#include <nlohmann/json.hpp>

#include <algorithm>
#include <cmath>
#include <set>
#include <stdexcept>

namespace
{

uint32_t hash(int x, int y, uint32_t seed)
{
    uint32_t h = static_cast<uint32_t>(x) * 374761393u + static_cast<uint32_t>(y) * 668265263u + seed * 2246822519u;
    h = (h ^ (h >> 13)) * 1274126177u;
    return h ^ (h >> 16);
}

float random01(int x, int y, uint32_t seed)
{
    return static_cast<float>(hash(x, y, seed) & 0xFFFFFF) / static_cast<float>(0x1000000);
}

yh::Color shade(yh::Color c, float f)
{
    auto channel = [f](uint8_t v) { return static_cast<uint8_t>(std::clamp(v * f, 0.0f, 255.0f)); };
    return {channel(c.r), channel(c.g), channel(c.b), c.a};
}

// One 32x32 tile, painted pixel by pixel. Placeholder art until real tile packs exist; tile
// types with an unknown `art` get a flat `color` with a little grain.
yh::Color tilePixel(const GameMap::TileType& type, uint32_t seed, int x, int y)
{
    const float grain = 0.9f + 0.2f * random01(x / 2, y / 2, seed * 31);
    const std::string& art = type.art;
    if (art == "grass")
        return shade({70, 112, 58, 255}, grain * (random01(x, y, 5) < 0.08f ? 1.25f : 1.0f));
    if (art == "dirt")
        return shade({120, 92, 62, 255}, grain);
    if (art == "stone")
    {
        // Flagstones: 16 px slabs, every other row shifted, dark grout between them.
        const int row = y / 16;
        const int sx = (x + (row % 2) * 8) % 16;
        if (sx == 0 || y % 16 == 0)
            return {58, 56, 60, 255};
        const float slab = 0.9f + 0.18f * random01((x + (row % 2) * 8) / 16, row, 9);
        return shade({122, 118, 116, 255}, grain * slab);
    }
    if (art == "wood")
    {
        // Planks 8 px tall with staggered ends.
        const int plank = y / 8;
        if (y % 8 == 0 || x == (plank * 13) % 32)
            return {62, 40, 24, 255};
        return shade({140, 96, 58, 255}, grain * (0.92f + 0.12f * random01(0, plank, 3)));
    }
    if (art == "wall")
    {
        // Bricks 16 x 8, mortar lines, a lighter top edge on each brick.
        const int row = y / 8;
        if ((x + (row % 2) * 8) % 16 == 0 || y % 8 == 0)
            return {30, 28, 32, 255};
        const float brick = 0.85f + 0.25f * random01((x + (row % 2) * 8) / 16, row, 17);
        return shade({92, 84, 86, 255}, grain * brick * (y % 8 == 1 ? 1.25f : 1.0f));
    }
    if (art == "water")
    {
        const float wave = std::sin((x + y * 0.5f) * 0.6f) * 0.5f + 0.5f;
        return shade({40, 78, 130, 255}, 0.9f + 0.25f * wave * (y % 6 == 0 ? 1.0f : 0.4f));
    }
    if (art == "tree")
    {
        const float dx = x - 15.5f, dy = y - 15.5f;
        const float d = std::sqrt(dx * dx + dy * dy);
        if (d > 14.5f)
            return {0, 0, 0, 0};
        const float light = 1.2f - (dx + dy) / 40.0f - d / 40.0f; // lit from the top-left, darker rim
        return shade({40, 92, 46, 255}, light * grain);
    }
    return shade(type.color, grain);
}

yh::Color colorFrom(const nlohmann::json& j)
{
    const auto v = j.get<std::vector<int>>();
    if ((v.size() != 3 && v.size() != 4) || std::any_of(v.begin(), v.end(), [](int c) { return c < 0 || c > 255; }))
        throw std::invalid_argument("colours are [r,g,b] or [r,g,b,a] in 0..255");
    return {static_cast<uint8_t>(v[0]), static_cast<uint8_t>(v[1]), static_cast<uint8_t>(v[2]), static_cast<uint8_t>(v.size() == 4 ? v[3] : 255)};
}

}

std::optional<GameMap> GameMap::fromJson(std::string_view text, std::string* error, const Kits& kits)
{
    if (error) error->clear();
    try
    {
        const auto j = nlohmann::json::parse(text);
        const bool native = j.is_object() && j.contains("tileMap");
        if (!j.is_object() || !j.contains("tiles") || (!j.at("tiles").is_object() && !j.at("tiles").is_array())
            || (!native && (!j.contains("legend") || !j.at("legend").is_object() || !j.contains("layers") || !j.at("layers").is_array())))
            throw std::invalid_argument("maps need tiles and either a tileMap or legend/layers");
        GameMap m;
        m.name_ = j.value("name", "");
        if (j.contains("ambient")) m.ambient_ = colorFrom(j.at("ambient"));
        if (j.contains("lighting"))
        {
            const auto& l = j.at("lighting");
            const std::string mode = l.value("mode", "mood");
            if (mode == "off") m.lighting_.mode = LightingMode::Off;
            else if (mode == "mood") m.lighting_.mode = LightingMode::Mood;
            else if (mode == "rules") m.lighting_.mode = LightingMode::Rules;
            else throw std::invalid_argument("lighting mode is \"off\", \"mood\" or \"rules\"");
            const std::string level = l.value("ambient", "dark");
            if (level == "dark") m.lighting_.ambient = yh::LightLevel::Dark;
            else if (level == "dim") m.lighting_.ambient = yh::LightLevel::Dim;
            else if (level == "bright") m.lighting_.ambient = yh::LightLevel::Bright;
            else throw std::invalid_argument("lighting ambient is \"dark\", \"dim\" or \"bright\"");
            m.lighting_.brightFraction = l.value("brightFraction", m.lighting_.brightFraction);
            m.lighting_.carried = l.value("carried", m.lighting_.carried);
            m.lighting_.sight = l.value("sight", m.lighting_.sight);
            const std::string time = l.value("time", "night");
            if (time == "day") m.lighting_.time = Time::Day;
            else if (time == "dusk") m.lighting_.time = Time::Dusk;
            else if (time == "night") m.lighting_.time = Time::Night;
            else if (time == "underground") m.lighting_.time = Time::Underground;
            else throw std::invalid_argument("lighting time is \"day\", \"dusk\", \"night\" or \"underground\"");
            m.lighting_.daySight = l.value("daySight", m.lighting_.daySight);
            m.lighting_.duskSight = l.value("duskSight", m.lighting_.duskSight);
            if (l.contains("daySky")) m.lighting_.daySky = colorFrom(l.at("daySky"));
            if (l.contains("duskSky")) m.lighting_.duskSky = colorFrom(l.at("duskSky"));
            const Lighting& k = m.lighting_;
            if (!std::isfinite(k.brightFraction) || k.brightFraction < 0 || k.brightFraction > 1 || !std::isfinite(k.carried)
                || k.carried < 0 || k.carried > 100 || !std::isfinite(k.sight) || k.sight <= 0 || k.sight > 200
                || !std::isfinite(k.daySight) || k.daySight <= 0 || k.daySight > 200 || !std::isfinite(k.duskSight) || k.duskSight <= 0 || k.duskSight > 200)
                throw std::invalid_argument("lighting numbers are out of range");
        }

        std::map<std::string, yh::TileId> ids;
        auto addType = [&](const std::string& name, const nlohmann::json& t) {
            if (name.empty() || ids.contains(name)) throw std::invalid_argument("tile names must be unique and not empty");
            TileType type;
            type.name = name;
            type.art = t.value("art", name);
            if (t.contains("color")) type.color = colorFrom(t.at("color"));
            type.walkable = t.value("walkable", true);
            type.blocksSight = t.value("blocksSight", false);
            type.indoors = t.value("indoors", false);
            m.types_.push_back(std::move(type));
            ids[name] = static_cast<yh::TileId>(m.types_.size());
        };
        if (j.at("tiles").is_array())
            for (const auto& t : j.at("tiles"))
                addType(t.at("name").get<std::string>(), t);
        else
            for (const auto& [name, t] : j.at("tiles").items())
                addType(name, t);
        if (m.types_.empty() || m.types_.size() > 4096) throw std::invalid_argument("a map needs 1..4096 tile types");

        m.region_ = std::make_unique<yh::Region>();
        if (native)
        {
            std::string problem;
            m.region_->map = yh::TileMap::fromJson(j.at("tileMap").dump(), &problem);
            if (!m.region_->map) throw std::invalid_argument("tileMap: " + problem);
            m.width_ = m.region_->map->width();
            m.height_ = m.region_->map->height();
            if (m.width_ < 1 || m.height_ < 1 || m.width_ > 4096 || m.height_ > 4096) throw std::invalid_argument("maps are 1..4096 cells a side");
            if (m.region_->map->tileSize() != cellSize) throw std::invalid_argument("tileMap tiles are 64 units");
            if (m.region_->map->layerCount() == 0) throw std::invalid_argument("a map needs at least one layer");
            for (size_t layer = 0; layer < m.region_->map->layerCount(); layer++)
                for (int y = 0; y < m.height_; y++)
                    for (int x = 0; x < m.width_; x++)
                        if (m.region_->map->tile(static_cast<int>(layer), x, y) > m.types_.size())
                            throw std::invalid_argument("tileMap uses a tile id with no tile type");
        }
        else
        {
            // The text format: an import for hand-written maps, turned into the same TileMap.
            std::map<char, yh::TileId> legend;
            for (const auto& [symbol, name] : j.at("legend").items())
            {
                const auto id = ids.find(name.get<std::string>());
                if (symbol.size() != 1 || symbol == " ") throw std::invalid_argument("legend keys are single characters (space means empty)");
                if (id == ids.end()) throw std::invalid_argument("legend '" + symbol + "' names unknown tile \"" + name.get<std::string>() + "\"");
                legend[symbol[0]] = id->second;
            }
            std::vector<std::vector<yh::TileId>> layers;
            std::vector<std::string> layerNames;
            for (const auto& layer : j.at("layers"))
            {
                const auto rows = layer.at("rows").get<std::vector<std::string>>();
                if (layers.empty())
                {
                    m.height_ = static_cast<int>(rows.size());
                    m.width_ = rows.empty() ? 0 : static_cast<int>(rows[0].size());
                    if (m.width_ < 1 || m.height_ < 1 || m.width_ > 4096 || m.height_ > 4096) throw std::invalid_argument("maps are 1..4096 cells a side");
                }
                if (static_cast<int>(rows.size()) != m.height_) throw std::invalid_argument("every layer needs the same number of rows");
                std::vector<yh::TileId> cells(static_cast<size_t>(m.width_) * m.height_, 0);
                for (int y = 0; y < m.height_; y++)
                {
                    if (static_cast<int>(rows[y].size()) != m.width_)
                        throw std::invalid_argument("row " + std::to_string(y) + " of layer \"" + layer.value("name", "") + "\" isn't " + std::to_string(m.width_) + " wide");
                    for (int x = 0; x < m.width_; x++)
                    {
                        const char symbol = rows[y][x];
                        if (symbol == ' ')
                            continue;
                        const auto id = legend.find(symbol);
                        if (id == legend.end()) throw std::invalid_argument(std::string("'") + symbol + "' isn't in the legend");
                        cells[static_cast<size_t>(y) * m.width_ + x] = id->second;
                    }
                }
                layers.push_back(std::move(cells));
                layerNames.push_back(layer.value("name", "layer " + std::to_string(layers.size())));
            }
            if (layers.empty()) throw std::invalid_argument("a map needs at least one layer");
            m.region_->map = std::make_unique<yh::TileMap>(m.width_, m.height_, cellSize);
            for (size_t layer = 0; layer < layers.size(); layer++)
            {
                const int index = m.region_->map->addLayer(layerNames[layer]);
                for (int y = 0; y < m.height_; y++)
                    for (int x = 0; x < m.width_; x++)
                        if (const yh::TileId id = layers[layer][static_cast<size_t>(y) * m.width_ + x])
                            m.region_->map->setTile(index, x, y, id);
            }
        }
        m.region_->id = m.name_;
        m.cacheTiles();

        for (const auto& l : j.value("lights", nlohmann::json::array()))
        {
            const auto at = l.at("at").get<std::vector<float>>();
            const float radius = l.value("radius", 5.0f);
            if (at.size() != 2 || !std::isfinite(at[0]) || !std::isfinite(at[1]) || !std::isfinite(radius) || radius <= 0 || radius > 1000)
                throw std::invalid_argument("lights need \"at\": [x, y] and a positive radius (in cells)");
            if (at[0] < 0 || at[1] < 0 || at[0] >= m.width_ || at[1] >= m.height_)
                throw std::invalid_argument("light is outside the map");
            m.lights_.push_back({{at[0] * cellSize, at[1] * cellSize}, l.contains("color") ? colorFrom(l.at("color")) : yh::Color{255, 200, 120, 255},
                radius * cellSize, l.value("flame", true)});
        }
        const auto markers = j.value("markers", nlohmann::json::object());
        if (!markers.is_object()) throw std::invalid_argument("markers must be an object");
        for (auto it = markers.begin(); it != markers.end(); ++it)
        {
            const std::string& name = it.key();
            const auto cell = it.value().get<std::vector<int>>();
            if (cell.size() != 2 || !m.inside({cell[0], cell[1]})) throw std::invalid_argument("marker \"" + name + "\" is outside the map");
            m.markers_[name] = {cell[0], cell[1]};
        }

        // Objects: a kit on a cell ({"kit": "door", "at": [x, y]}, any other fields change that
        // copy, and "tags" add to the kit's), or a whole object with "at" or a world "area".
        const auto objects = j.value("objects", nlohmann::json::array());
        if (!objects.is_array()) throw std::invalid_argument("objects must be an array");
        for (size_t i = 0; i < objects.size(); i++)
        {
            const nlohmann::json& entry = objects[i];
            const std::string where = "objects[" + std::to_string(i) + "]: ";
            if (!entry.is_object()) throw std::invalid_argument(where + "each object is an object");
            nlohmann::json object = nlohmann::json::object();
            if (entry.contains("kit"))
            {
                const std::string kit = entry.at("kit").get<std::string>();
                const auto found = kits.find(kit);
                if (found == kits.end()) throw std::invalid_argument(where + "unknown kit \"" + kit + "\"");
                object = nlohmann::json::parse(found->second.toJson()).at("object");
                object.erase("id");
            }
            else
                object["area"] = {0, 0, cellSize, cellSize}; // one cell unless it says otherwise
            nlohmann::json changes = entry;
            changes.erase("kit");
            changes.erase("at");
            if (changes.contains("tags"))
            {
                std::set<std::string> tags = object.value("tags", std::set<std::string>{});
                for (const auto& tag : changes.at("tags")) tags.insert(tag.get<std::string>());
                changes["tags"] = tags;
            }
            object.merge_patch(changes);
            std::string problem;
            std::optional<yh::Kit> made = yh::Kit::fromJson(nlohmann::json{{"name", ""}, {"object", object}}.dump(), &problem);
            if (!made) throw std::invalid_argument(where + problem);
            yh::MapObject placed = made->prototype;
            if (entry.contains("at"))
            {
                const auto at = entry.at("at").get<std::vector<int>>();
                if (at.size() != 2) throw std::invalid_argument(where + "at is [x, y]");
                placed.area.x = at[0] * cellSize;
                placed.area.y = at[1] * cellSize;
            }
            if (placed.area.x < 0 || placed.area.y < 0 || placed.area.x + placed.area.w > m.width_ * cellSize || placed.area.y + placed.area.h > m.height_ * cellSize)
                throw std::invalid_argument(where + "is outside the map");
            if (placed.trap && !placed.trap->effect.empty() && !yh::Effect::fromJson(placed.trap->effect, &problem))
                throw std::invalid_argument(where + "trap effect: " + problem);
            m.region_->objects.add(std::move(placed));
        }
        m.authoredObjects_ = m.region_->objects.toJson();
        m.trapSpotRange_ = j.value("trapSpotRange", m.trapSpotRange_);
        if (!std::isfinite(m.trapSpotRange_) || m.trapSpotRange_ < 0 || m.trapSpotRange_ > 100)
            throw std::invalid_argument("trapSpotRange is 0 to 100 squares");

        nlohmann::json rest = j;
        for (const char* key : {"tiles", "legend", "layers", "tileMap", "objects"})
            rest.erase(key);
        m.rest_ = rest.dump();
        m.buildWalls();
        m.buildIndoorAreas();
        return m;
    }
    catch (const std::exception& e)
    {
        if (error) *error = e.what();
        return std::nullopt;
    }
}

void GameMap::cacheTiles()
{
    const size_t cells = static_cast<size_t>(width_) * height_;
    ground_.assign(cells, 0);
    open_.assign(cells, 1);
    sight_.assign(cells, 0);
    roofed_.assign(cells, 0);
    const yh::TileMap& tiles = *region_->map;
    bool first = true;
    for (size_t layer = 0; layer < tiles.layerCount(); layer++)
    {
        if (tiles.layerFloor(static_cast<int>(layer)) != 0)
            continue;
        for (int y = 0; y < height_; y++)
            for (int x = 0; x < width_; x++)
            {
                const yh::TileId id = tiles.tile(static_cast<int>(layer), x, y);
                const size_t i = static_cast<size_t>(y) * width_ + x;
                if (first)
                    ground_[i] = id != 0; // empty ground is a hole, not a floor
                if (id == 0)
                    continue;
                const TileType& type = types_[id - 1];
                open_[i] &= type.walkable ? 1 : 0;
                sight_[i] |= type.blocksSight ? 1 : 0;
                roofed_[i] |= type.indoors ? 1 : 0;
            }
        first = false;
    }
}

void GameMap::buildIndoorAreas()
{
    indoorAreas_.clear();
    for (int y = 0; y < height_; y++)
    {
        for (int x = 0; x < width_; x++)
        {
            if (!indoors({x, y}))
                continue;
            const int start = x;
            while (x + 1 < width_ && indoors({x + 1, y}))
                x++;
            indoorAreas_.push_back({start * cellSize, y * cellSize, (x - start + 1) * cellSize, cellSize});
        }
    }
}

void GameMap::tilesChanged()
{
    cacheTiles();
    tileWalls_.clear();
    buildWalls();
    buildIndoorAreas();
}

void GameMap::setTiles(std::unique_ptr<yh::TileMap> tiles)
{
    if (!region_ || !tiles)
        return;
    tiles->setTileset(region_->map->tileset());
    region_->map = std::move(tiles);
    width_ = region_->map->width();
    height_ = region_->map->height();
    tilesChanged();
}

bool GameMap::walkable(yh::Cell c) const
{
    if (!inside(c))
        return false;
    const size_t i = static_cast<size_t>(c.y) * width_ + c.x;
    if (!ground_[i] || !open_[i])
        return false;
    // A little inside the cell, so an object on the next cell over doesn't count.
    return region_->objects.passable({c.x * cellSize + 1, c.y * cellSize + 1, cellSize - 2, cellSize - 2}, 0);
}

bool GameMap::indoors(yh::Cell c) const
{
    return inside(c) && roofed_[static_cast<size_t>(c.y) * width_ + c.x];
}

void GameMap::resetObjects()
{
    if (!region_)
        return;
    region_->objects = *yh::Objects::fromJson(authoredObjects_);
    refreshWalls();
}

void GameMap::refreshWalls()
{
    walls_ = tileWalls_;
    if (region_)
        for (const yh::Wall& wall : region_->objects.walls(0))
            walls_.push_back(wall);
}

std::optional<yh::ObjectId> GameMap::objectAt(yh::Cell c) const
{
    return region_ ? region_->objects.at({(c.x + 0.5f) * cellSize, (c.y + 0.5f) * cellSize}, 0) : std::nullopt;
}

yh::Cell GameMap::cellOf(const yh::MapObject& object) const
{
    return {static_cast<int>(std::floor((object.area.x + 1) / cellSize)), static_cast<int>(std::floor((object.area.y + 1) / cellSize))};
}

std::vector<yh::Cell> GameMap::cellsOf(const yh::MapObject& object) const
{
    std::vector<yh::Cell> out;
    const yh::Cell first = cellOf(object);
    const int lastX = static_cast<int>(std::floor((object.area.x + object.area.w - 1) / cellSize));
    const int lastY = static_cast<int>(std::floor((object.area.y + object.area.h - 1) / cellSize));
    for (int y = first.y; y <= std::max(first.y, lastY); y++)
        for (int x = first.x; x <= std::max(first.x, lastX); x++)
            out.push_back({x, y});
    return out;
}

std::string GameMap::toJson() const
{
    nlohmann::json j = nlohmann::json::parse(rest_.empty() ? "{}" : rest_);
    nlohmann::json tiles = nlohmann::json::array();
    for (const TileType& t : types_)
    {
        nlohmann::json tile{{"name", t.name}, {"art", t.art}, {"color", {t.color.r, t.color.g, t.color.b, t.color.a}},
            {"walkable", t.walkable}, {"blocksSight", t.blocksSight}};
        if (t.indoors) tile["indoors"] = true;
        tiles.push_back(std::move(tile));
    }
    j["tiles"] = std::move(tiles);
    if (region_)
    {
        nlohmann::json tileMap = nlohmann::json::parse(region_->map->toJson());
        tileMap.erase("tracking"); // authored maps carry no edit history
        tileMap.erase("delta");
        j["tileMap"] = std::move(tileMap);
        j["objects"] = nlohmann::json::parse(region_->objects.toJson()).at("objects");
    }
    return j.dump(2);
}

GameMap::Sky GameMap::sky(Time time) const
{
    if (time != Time::Day && time != Time::Dusk)
        return {ambient_, ambient_, lighting_.ambient, lighting_.sight, false};
    const bool day = time == Time::Day;
    const yh::Color open = day ? lighting_.daySky : lighting_.duskSky;
    // Some of the daylight finds its way in through doors and windows.
    const float leak = day ? 0.3f : 0.15f;
    auto mix = [&](uint8_t dark, uint8_t bright) { return static_cast<uint8_t>(dark + (std::max(dark, bright) - dark) * leak); };
    const yh::Color roofed{mix(ambient_.r, open.r), mix(ambient_.g, open.g), mix(ambient_.b, open.b), 255};
    return {open, roofed, day ? yh::LightLevel::Bright : yh::LightLevel::Dim, std::max(lighting_.sight, day ? lighting_.daySight : lighting_.duskSight), true};
}

bool GameMap::blocksSight(yh::Cell c) const
{
    return !inside(c) || sight_[static_cast<size_t>(c.y) * width_ + c.x];
}

std::optional<yh::Cell> GameMap::marker(std::string_view name) const
{
    const auto found = markers_.find(name);
    return found == markers_.end() ? std::nullopt : std::optional(found->second);
}

void GameMap::bindTileset(yh::Renderer& renderer)
{
    if (tilesetBound_)
        return;
    tilesetBound_ = true;

    constexpr int px = 32, columns = 8;
    const int count = static_cast<int>(types_.size());
    const int rows = (count + columns - 1) / columns;
    std::vector<unsigned char> pixels(static_cast<size_t>(columns) * px * rows * px * 4, 0);
    yh::Tileset tileset;
    tileset.columns = columns;
    tileset.rows = rows;
    tileset.tilePixels = px;
    for (int t = 0; t < count; t++)
    {
        const int ox = (t % columns) * px, oy = (t / columns) * px;
        float r = 0, g = 0, b = 0, a = 0;
        for (int y = 0; y < px; y++)
        {
            for (int x = 0; x < px; x++)
            {
                const yh::Color c = tilePixel(types_[t], static_cast<uint32_t>(t + 1), x, y);
                unsigned char* p = &pixels[(static_cast<size_t>(oy + y) * columns * px + ox + x) * 4];
                p[0] = c.r;
                p[1] = c.g;
                p[2] = c.b;
                p[3] = c.a;
                r += c.r;
                g += c.g;
                b += c.b;
                a += c.a;
            }
        }
        const float n = static_cast<float>(px * px);
        tileset.averageColors.push_back({static_cast<uint8_t>(r / n), static_cast<uint8_t>(g / n), static_cast<uint8_t>(b / n), static_cast<uint8_t>(a / n)});
    }
    tileset.texture = renderer.createTexture(columns * px, rows * px, pixels.data());
    region_->map->setTileset(std::move(tileset));
}

void GameMap::buildWalls()
{
    // One segment per straight run of edge, so sight and light test as few walls as possible.
    auto edge = [this](yh::Cell a, yh::Cell b) { return inside(a) && inside(b) && blocksSight(a) != blocksSight(b); };
    for (int y = 1; y < height_; y++)
    {
        int start = -1;
        for (int x = 0; x <= width_; x++)
        {
            const bool on = x < width_ && edge({x, y - 1}, {x, y});
            if (on && start < 0)
                start = x;
            if (!on && start >= 0)
            {
                tileWalls_.push_back({{start * cellSize, y * cellSize}, {x * cellSize, y * cellSize}});
                start = -1;
            }
        }
    }
    for (int x = 1; x < width_; x++)
    {
        int start = -1;
        for (int y = 0; y <= height_; y++)
        {
            const bool on = y < height_ && edge({x - 1, y}, {x, y});
            if (on && start < 0)
                start = y;
            if (!on && start >= 0)
            {
                tileWalls_.push_back({{x * cellSize, start * cellSize}, {x * cellSize, y * cellSize}});
                start = -1;
            }
        }
    }
    refreshWalls();
}
