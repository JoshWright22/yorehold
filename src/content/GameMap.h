#pragma once

#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/map/Grid.h>
#include <yorehold/framework/map/LightLevels.h>
#include <yorehold/framework/map/Regions.h>
#include <yorehold/framework/map/TileMap.h>

#include <map>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

// A chapter's map, read from a JSON file (see assets/chapters/*/map.json). It lives in a
// yh::Region: the framework's TileMap for the tiles and Objects for doors, levers, chests and traps.
//   "tiles"   tile types: art, walkable, blocksSight. An object of names, or an array (id = place + 1)
//   "tileMap" the framework's TileMap JSON, as the editor writes it; or, for hand-written maps:
//   "legend"  one character per tile type, so layers are readable text grids
//   "layers"  rows of legend characters; the first layer is the ground, later ones sit on top
//   "objects" kits placed on cells, or whole objects
//   "lights"  torches and braziers in cell coordinates
//   "markers" named cells the chapter refers to ("partyStart")
//   "lighting" how light works on this map: mode, ambient level, carried lights, time of day
//              ("time": "day", "dusk", "night" or "underground"; tiles marked "indoors" stay dark by day)
class GameMap
{
public:
    struct TileType
    {
        std::string name;
        std::string art;  // built-in placeholder painter ("grass", "wall"...); unknown = flat `color`
        yh::Color color{128, 128, 128, 255};
        bool walkable = true;
        bool blocksSight = false;
        bool indoors = false; // under a roof: daylight doesn't reach it
    };

    // Day and dusk light everything outdoors and let the party see further there; indoors stays
    // as dark as the map's own lighting says. Underground has no outdoors at all.
    enum class Time { Day, Dusk, Night, Underground };

    // Off: everything lit, no darkness drawn. Mood: lights and darkness are only for looks.
    // Rules: what the party can see depends on light (bright/dim/dark) and darkvision.
    enum class LightingMode { Off, Mood, Rules };

    struct Lighting
    {
        LightingMode mode = LightingMode::Mood;
        yh::LightLevel ambient = yh::LightLevel::Dark; // light level where no lamp reaches (rules mode)
        float brightFraction = 0.5f; // part of each light's radius that is bright; the rest is dim
        float carried = 3.5f;        // radius in cells of the light each hero carries; 0 = none
        float sight = 8.5f;          // how far heroes see, in cells
        Time time = Time::Night;
        float daySight = 40;         // how far they see outdoors by day, and at dusk
        float duskSight = 18;
        yh::Color daySky{255, 250, 238, 255};
        yh::Color duskSky{176, 136, 128, 255};
    };

    // What the sky gives at a time of day. At night (and underground) it's the map's own ambient.
    struct Sky
    {
        yh::Color outdoors;       // ambient colour outside
        yh::Color indoors;        // ... and under a roof: the map's ambient with some daylight leaking in
        yh::LightLevel level;     // light level outside where no lamp reaches
        float sight;              // how far heroes see outside, in cells
        bool differs;             // indoors and outdoors aren't lit the same
    };
    Sky sky(Time time) const;
    bool indoors(yh::Cell c) const;
    // The indoor cells as rectangles in world units (one per run along a row).
    const std::vector<yh::Rect>& indoorAreas() const { return indoorAreas_; }

    struct Light
    {
        yh::Vec2 position; // world units
        yh::Color color;
        float radius;      // world units
        bool flame = true; // draw a flickering flame at the light
    };

    // World units per grid square. Art is scaled to it, so it isn't a pixel size.
    static constexpr float cellSize = 64;

    using Kits = std::map<std::string, yh::Kit, std::less<>>;
    // `kits` are what the map's "objects" may name ("kit": "door").
    static std::optional<GameMap> fromJson(std::string_view json, std::string* error = nullptr, const Kits& kits = {});
    // The map in the editor's form: tiles as an array, the TileMap and whole objects as they are now.
    std::string toJson() const;

    const std::string& name() const { return name_; }
    int width() const { return width_; }
    int height() const { return height_; }
    yh::TileMap& map() { return *region_->map; }
    const yh::TileMap& map() const { return *region_->map; }
    yh::Region& region() { return *region_; }
    yh::Objects& objects() { return region_->objects; }
    const yh::Objects& objects() const { return region_->objects; }
    // Back to the objects as the file placed them (a new adventure).
    void resetObjects();
    // Call after a door opened or anything else that changes what blocks sight.
    void refreshWalls();
    // The object on this cell, if any (the last placed wins).
    std::optional<yh::ObjectId> objectAt(yh::Cell c) const;
    yh::Cell cellOf(const yh::MapObject& object) const;
    // The cells an object covers.
    std::vector<yh::Cell> cellsOf(const yh::MapObject& object) const;
    // How near, in squares, a hero has to come for their passive score to find a hidden trap.
    float trapSpotRange() const { return trapSpotRange_; }
    // Needs the renderer, so it's done on the first draw.
    void bindTileset(yh::Renderer& renderer);

    bool inside(yh::Cell c) const { return c.x >= 0 && c.y >= 0 && c.x < width_ && c.y < height_; }
    // Tiles and objects: a shut door stops you, an open one doesn't.
    bool walkable(yh::Cell c) const;
    bool blocksSight(yh::Cell c) const; // tiles only; objects add their own walls
    std::optional<yh::Cell> marker(std::string_view name) const;

    // Edges between sight-blocking and open cells: they block sight and light.
    const std::vector<yh::Wall>& walls() const { return walls_; }
    const std::vector<Light>& lights() const { return lights_; }
    // Light where no lamp reaches ("ambient" in the file): moonlight by default.
    yh::Color ambient() const { return ambient_; }
    const Lighting& lighting() const { return lighting_; }

private:
    void buildWalls();
    void cacheTiles(); // the per-cell flags below, from the TileMap's floor 0 layers

    std::string name_;
    int width_ = 0;
    int height_ = 0;
    std::vector<TileType> types_;            // index = tile id - 1
    // Per cell, y * width + x. Floor 0 only: the game plays on one floor for now.
    std::vector<uint8_t> ground_, open_, sight_, roofed_;
    std::unique_ptr<yh::Region> region_;
    std::string authoredObjects_;            // Objects JSON as loaded
    std::string rest_;                       // lights, lighting, markers... as written (JSON), for toJson
    float trapSpotRange_ = 2;
    std::vector<yh::Wall> tileWalls_;
    std::vector<yh::Wall> walls_;
    std::vector<yh::Rect> indoorAreas_;
    std::vector<Light> lights_;
    yh::Color ambient_{46, 54, 86, 255};
    Lighting lighting_;
    std::map<std::string, yh::Cell, std::less<>> markers_;
    bool tilesetBound_ = false;
};
