#pragma once

#include "content/GameMap.h"

#include <yorehold/framework/editor/History.h>
#include <yorehold/framework/graphics/Renderer.h>
#include <yorehold/framework/graphics/Types.h>
#include <yorehold/framework/input/Input.h>
#include <yorehold/framework/map/Grid.h>
#include <yorehold/framework/ui/Ui.h>

#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

// Map mode of the Create screen, in two parts: MapEditor is the map and the commands that change
// it (no drawing, so tests and other layouts can use it), MapEditorPanel is the desktop layout.
//
// The map is a GameMap, edited in place and written back as the map.json the game loads. Every
// command goes on the history it was given, which the whole Create screen shares.
class MapEditor
{
public:
    // In cells, as map.json writes them.
    struct Light
    {
        double x = 0, y = 0;
        double radius = 5;
        yh::Color color{255, 200, 120, 255};
        bool flame = true;
        bool operator==(const Light&) const = default;
    };
    using Markers = std::map<std::string, yh::Cell, std::less<>>;

    explicit MapEditor(yh::History& history) : history_(history) {}
    MapEditor(const MapEditor&) = delete;
    MapEditor& operator=(const MapEditor&) = delete;

    // An empty map of one ground layer, with a tile type for each built-in tile art.
    static std::string blankMap(std::string_view name, int width, int height);

    // `kits` are what the map's objects may name, and what can be placed.
    bool load(std::string_view json, std::string* error = nullptr, GameMap::Kits kits = {});
    bool hasMap() const { return map_.has_value(); }
    // The map.json to save: the editor's form (see CONTENT.md, Maps).
    std::string toJson() const;

    // Walls and walkable cells are worked out again here if tiles or objects changed.
    GameMap& map();
    const GameMap::Kits& kits() const { return kits_; }
    const std::vector<Light>& lights() const { return lights_; }
    const Markers& markers() const { return markers_; }
    int width() const { return map_ ? map_->width() : 0; }
    int height() const { return map_ ? map_->height() : 0; }
    int layerCount() const { return map_ ? static_cast<int>(map_->map().layerCount()) : 0; }
    std::vector<int> layersOn(int floor) const;
    // Lowest and highest floor any layer is on.
    std::pair<int, int> floors() const;

    // Tiles. `id` 0 erases. Cells painted one after another are one undo step until endStroke().
    bool paint(int layer, yh::Cell cell, yh::TileId id);
    bool fill(int layer, yh::Cell from, yh::Cell to, yh::TileId id);
    void endStroke() { history_.breakMerge(); }

    // Layers. A map keeps at least one.
    int addLayer(std::string name, int floor);
    bool removeLayer(int layer);
    // The "walls" layer of a floor, added the first time it is asked for.
    int wallLayer(int floor);
    // The first tile type that blocks sight, or 0.
    yh::TileId wallTile() const;
    // Cells are kept from the top-left; lights, markers and objects left outside are dropped.
    bool resize(int width, int height);

    std::optional<size_t> addLight(const Light& light);
    // `mergeKey` joins a slider drag into one undo step.
    bool setLight(size_t index, const Light& light, std::string_view mergeKey = {});
    bool removeLight(size_t index);

    // Places the marker, or moves it if the name is already on the map.
    bool setMarker(const std::string& name, yh::Cell cell);
    bool removeMarker(std::string_view name);

    std::optional<yh::ObjectId> placeKit(std::string_view kit, yh::Cell cell, int floor);
    bool removeObject(yh::ObjectId id);
    std::optional<yh::ObjectId> objectAt(yh::Cell cell, int floor) const;

    // Things a writer should look at: nothing here stops the map from saving.
    std::vector<std::string> problems();

private:
    // What an undo step puts back. Tiles are left out of steps that don't touch them.
    struct Snapshot
    {
        std::optional<std::string> tiles; // TileMap JSON
        std::string objects;              // Objects JSON, ids included
        std::vector<Light> lights;
        Markers markers;
    };
    Snapshot snapshot(bool tiles) const;
    void restore(const Snapshot& snapshot);
    // Runs `change` and records it with what was there before and after.
    template <typename Change> void edit(std::string_view label, bool tiles, Change change, std::string_view mergeKey = {});

    yh::History& history_;
    std::optional<GameMap> map_;
    GameMap::Kits kits_;
    std::vector<Light> lights_;
    Markers markers_;
    bool stale_ = false; // tiles or objects changed since the walls were built
};

// The desktop layout over a MapEditor: tools on the left, the map in the middle, layers and the
// tool's settings on the right. Left button uses the tool, right button removes, middle drags the
// view and the wheel zooms.
class MapEditorPanel
{
public:
    enum class Tool { Paint, Rect, Wall, Light, Marker, Kit };

    void draw(MapEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area);

private:
    void drawMap(MapEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& view);
    void useTool(MapEditor& editor, yh::Ui& ui, const yh::Input& input, const yh::Rect& view);
    void drawTools(MapEditor& editor, yh::Ui& ui, const yh::Rect& column);
    void drawProperties(MapEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column);
    yh::Vec2 toWorld(yh::Vec2 screen, const yh::Rect& view) const;
    // The layer to paint on; adds one if the floor has none.
    int paintLayer(MapEditor& editor);

    Tool tool_ = Tool::Paint;
    int floor_ = 0;
    int layer_ = 0;
    yh::TileId tile_ = 1;
    float zoom_ = 0;        // screen pixels per world unit; 0 = fit the map on the first draw
    yh::Vec2 pan_;          // world position at the view's top-left
    bool grid_ = true;
    bool walls_ = true;
    std::optional<yh::Cell> hover_;
    std::optional<yh::Cell> rectFrom_;
    yh::TileId rectTile_ = 0;
    bool stroking_ = false;
    MapEditor::Light brush_;            // what the next light is placed with
    float radius_ = 5;                  // the slider's own value
    std::optional<size_t> light_;       // selected
    std::string markerName_ = "partyStart";
    std::string kit_;
    float scroll_ = 0;
    std::string hint_;
};
