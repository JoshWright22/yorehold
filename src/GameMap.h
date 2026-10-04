#pragma once

#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/map/Grid.h>
#include <yorehold/framework/map/LightLevels.h>
#include <yorehold/framework/map/TileMap.h>

#include <map>
#include <memory>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

// A chapter's map, read from a JSON file (see assets/chapters/*/map.json):
//   "tiles"   named tile types: art, walkable, blocksSight
//   "legend"  one character per tile type, so layers are readable text grids
//   "layers"  rows of legend characters; the first layer is the ground, later ones sit on top
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

    static std::optional<GameMap> fromJson(std::string_view json, std::string* error = nullptr);

    const std::string& name() const { return name_; }
    int width() const { return width_; }
    int height() const { return height_; }
    yh::TileMap& map() { return *map_; }
    const yh::TileMap& map() const { return *map_; }
    // Needs the renderer, so it's done on the first draw.
    void bindTileset(yh::Renderer& renderer);

    bool inside(yh::Cell c) const { return c.x >= 0 && c.y >= 0 && c.x < width_ && c.y < height_; }
    bool walkable(yh::Cell c) const;
    bool blocksSight(yh::Cell c) const;
    std::optional<yh::Cell> marker(std::string_view name) const;

    // Edges between sight-blocking and open cells: they block sight and light.
    const std::vector<yh::Wall>& walls() const { return walls_; }
    const std::vector<Light>& lights() const { return lights_; }
    // Light where no lamp reaches ("ambient" in the file): moonlight by default.
    yh::Color ambient() const { return ambient_; }
    const Lighting& lighting() const { return lighting_; }

private:
    void buildWalls();

    std::string name_;
    int width_ = 0;
    int height_ = 0;
    std::vector<TileType> types_;            // index = tile id - 1
    std::vector<std::vector<yh::TileId>> layers_; // [layer][y * width + x], 0 = empty
    std::unique_ptr<yh::TileMap> map_;
    std::vector<yh::Wall> walls_;
    std::vector<yh::Rect> indoorAreas_;
    std::vector<Light> lights_;
    yh::Color ambient_{46, 54, 86, 255};
    Lighting lighting_;
    std::map<std::string, yh::Cell, std::less<>> markers_;
    bool tilesetBound_ = false;
};
