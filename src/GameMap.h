#pragma once

#include <yorehold/framework/graphics/Lighting.h>
#include <yorehold/framework/map/Grid.h>
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
    };

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

private:
    void buildWalls();

    std::string name_;
    int width_ = 0;
    int height_ = 0;
    std::vector<TileType> types_;            // index = tile id - 1
    std::vector<std::vector<yh::TileId>> layers_; // [layer][y * width + x], 0 = empty
    std::unique_ptr<yh::TileMap> map_;
    std::vector<yh::Wall> walls_;
    std::vector<Light> lights_;
    yh::Color ambient_{46, 54, 86, 255};
    std::map<std::string, yh::Cell, std::less<>> markers_;
    bool tilesetBound_ = false;
};
