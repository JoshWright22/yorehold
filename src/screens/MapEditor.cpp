// Map mode of the Create screen: the commands that change a map, and the desktop layout over them.

#include "MapEditor.h"

#include <nlohmann/json.hpp>
#include <SDL3/SDL_keycode.h>

#include <algorithm>
#include <cmath>
#include <memory>

namespace
{

using nlohmann::json;

constexpr float cell = GameMap::cellSize;
constexpr int maxSide = 4096;
constexpr int lowestFloor = -9, highestFloor = 9;

yh::Color colorFrom(const json& j, yh::Color fallback)
{
    if (!j.is_array() || (j.size() != 3 && j.size() != 4))
        return fallback;
    auto channel = [&](size_t i) { return static_cast<uint8_t>(std::clamp(j[i].get<int>(), 0, 255)); };
    return {channel(0), channel(1), channel(2), j.size() == 4 ? channel(3) : uint8_t(255)};
}

// The same layers with another size, or without one of them (`skip`).
std::unique_ptr<yh::TileMap> copyTiles(const yh::TileMap& from, int width, int height, int skip = -1)
{
    auto to = std::make_unique<yh::TileMap>(width, height, from.tileSize());
    const int w = std::min(width, from.width()), h = std::min(height, from.height());
    for (int layer = 0; layer < static_cast<int>(from.layerCount()); layer++)
    {
        if (layer == skip)
            continue;
        const int index = to->addLayer(from.layerName(layer), from.layerFloor(layer));
        if (!from.layerImagePath(layer).empty())
            to->setLayerImage(index, from.layerImagePath(layer), 0, from.layerImageArea(layer));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (const yh::TileId id = from.tile(layer, x, y))
                    to->setTile(index, x, y, id);
    }
    return to;
}

}

// ---------------------------------------------------------------- the map and its commands

std::string MapEditor::blankMap(std::string_view name, int width, int height)
{
    width = std::clamp(width, 1, maxSide);
    height = std::clamp(height, 1, maxSide);
    json j;
    j["name"] = std::string(name);
    // One type for each tile the game can paint by itself, so a new map has something to paint with.
    j["tiles"] = json::array({
        {{"name", "grass"}, {"art", "grass"}},
        {{"name", "dirt"}, {"art", "dirt"}},
        {{"name", "stone"}, {"art", "stone"}, {"indoors", true}},
        {{"name", "wood"}, {"art", "wood"}, {"indoors", true}},
        {{"name", "wall"}, {"art", "wall"}, {"walkable", false}, {"blocksSight", true}},
        {{"name", "water"}, {"art", "water"}, {"walkable", false}},
        {{"name", "tree"}, {"art", "tree"}, {"walkable", false}, {"blocksSight", true}},
    });
    j["legend"] = {{".", "grass"}};
    json ground{{"name", "ground"}, {"rows", std::vector<std::string>(height, std::string(width, '.'))}};
    j["layers"] = std::vector<json>{std::move(ground)};
    j["lights"] = json::array();
    j["markers"] = json::object();
    return j.dump(2);
}

bool MapEditor::load(std::string_view text, std::string* error, GameMap::Kits kits)
{
    std::optional<GameMap> map = GameMap::fromJson(text, error, kits);
    if (!map)
        return false;
    // GameMap has checked all of this, so reading it again can't fail.
    const json j = json::parse(text);
    std::vector<Light> lights;
    const json lightList = j.value("lights", json::array());
    for (const json& l : lightList)
    {
        Light light;
        light.x = l.at("at")[0].get<double>();
        light.y = l.at("at")[1].get<double>();
        light.radius = l.value("radius", 5.0);
        light.color = colorFrom(l.value("color", json()), light.color);
        light.flame = l.value("flame", true);
        lights.push_back(light);
    }
    Markers markers;
    // Kept in a variable: items() only points at the object it walks.
    const json markerList = j.value("markers", json::object());
    for (const auto& [name, at] : markerList.items())
        markers[name] = {at[0].get<int>(), at[1].get<int>()};

    map_ = std::move(map);
    kits_ = std::move(kits);
    lights_ = std::move(lights);
    markers_ = std::move(markers);
    stale_ = false;
    return true;
}

std::string MapEditor::toJson() const
{
    if (!map_)
        return "{}";
    json j = json::parse(map_->toJson());
    json lights = json::array();
    for (const Light& light : lights_)
    {
        json color = {light.color.r, light.color.g, light.color.b};
        if (light.color.a != 255)
            color.push_back(light.color.a);
        json entry{{"at", {light.x, light.y}}, {"radius", light.radius}, {"flame", light.flame}};
        entry["color"] = std::move(color);
        lights.push_back(std::move(entry));
    }
    j["lights"] = std::move(lights);
    json markers = json::object();
    for (const auto& [name, at] : markers_)
        markers[name] = {at.x, at.y};
    j["markers"] = std::move(markers);
    return j.dump(2);
}

GameMap& MapEditor::map()
{
    if (stale_)
    {
        map_->tilesChanged();
        stale_ = false;
    }
    return *map_;
}

std::vector<int> MapEditor::layersOn(int floor) const
{
    std::vector<int> layers;
    for (int layer = 0; layer < layerCount(); layer++)
        if (map_->map().layerFloor(layer) == floor)
            layers.push_back(layer);
    return layers;
}

std::pair<int, int> MapEditor::floors() const
{
    std::pair<int, int> range{0, 0};
    for (int layer = 0; layer < layerCount(); layer++)
    {
        range.first = std::min(range.first, map_->map().layerFloor(layer));
        range.second = std::max(range.second, map_->map().layerFloor(layer));
    }
    return range;
}

MapEditor::Snapshot MapEditor::snapshot(bool tiles) const
{
    Snapshot s;
    if (tiles)
        s.tiles = map_->map().toJson();
    s.objects = map_->objects().toJson();
    s.lights = lights_;
    s.markers = markers_;
    return s;
}

void MapEditor::restore(const Snapshot& snapshot)
{
    if (snapshot.tiles)
        if (std::unique_ptr<yh::TileMap> tiles = yh::TileMap::fromJson(*snapshot.tiles))
            map_->setTiles(std::move(tiles));
    if (std::optional<yh::Objects> objects = yh::Objects::fromJson(snapshot.objects))
        map_->objects() = std::move(*objects);
    lights_ = snapshot.lights;
    markers_ = snapshot.markers;
    stale_ = true;
}

template <typename Change>
void MapEditor::edit(std::string_view label, bool tiles, Change change, std::string_view mergeKey)
{
    const auto before = std::make_shared<const Snapshot>(snapshot(tiles));
    change();
    stale_ = true;
    const auto after = std::make_shared<const Snapshot>(snapshot(tiles));
    history_.record(label, [this, after] { restore(*after); }, [this, before] { restore(*before); }, mergeKey);
}

bool MapEditor::paint(int layer, yh::Cell at, yh::TileId id)
{
    if (!map_ || layer < 0 || layer >= layerCount() || !map_->inside(at) || id > map_->tileTypes().size())
        return false;
    const yh::TileId old = map_->map().tile(layer, at.x, at.y);
    if (old == id)
        return false;
    // One cell at a time, so a long stroke on a big map doesn't copy the map for every cell.
    auto set = [this, layer, at](yh::TileId tile) {
        return [this, layer, at, tile] {
            map_->map().setTile(layer, at.x, at.y, tile);
            stale_ = true;
        };
    };
    history_.perform(id ? "Paint tiles" : "Erase tiles", set(id), set(old), "paint");
    return true;
}

bool MapEditor::fill(int layer, yh::Cell from, yh::Cell to, yh::TileId id)
{
    if (!map_ || layer < 0 || layer >= layerCount() || id > map_->tileTypes().size())
        return false;
    const int x0 = std::max(0, std::min(from.x, to.x)), x1 = std::min(width() - 1, std::max(from.x, to.x));
    const int y0 = std::max(0, std::min(from.y, to.y)), y1 = std::min(height() - 1, std::max(from.y, to.y));
    if (x0 > x1 || y0 > y1)
        return false;
    auto old = std::make_shared<std::vector<yh::TileId>>();
    bool changes = false;
    for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            old->push_back(map_->map().tile(layer, x, y));
            changes |= old->back() != id;
        }
    if (!changes)
        return false;
    history_.perform(id ? "Fill tiles" : "Erase tiles",
        [this, layer, x0, x1, y0, y1, id] {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    map_->map().setTile(layer, x, y, id);
            stale_ = true;
        },
        [this, layer, x0, x1, y0, y1, old] {
            size_t i = 0;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    map_->map().setTile(layer, x, y, (*old)[i++]);
            stale_ = true;
        });
    return true;
}

int MapEditor::addLayer(std::string name, int floor)
{
    if (!map_ || floor < lowestFloor || floor > highestFloor)
        return -1;
    if (name.empty())
        name = "layer " + std::to_string(layerCount() + 1);
    int index = -1;
    edit("Add layer", true, [&] { index = map_->map().addLayer(std::move(name), floor); });
    return index;
}

bool MapEditor::removeLayer(int layer)
{
    if (!map_ || layer < 0 || layer >= layerCount() || layerCount() <= 1)
        return false;
    edit("Remove layer", true, [&] { map_->setTiles(copyTiles(map_->map(), width(), height(), layer)); });
    return true;
}

int MapEditor::wallLayer(int floor)
{
    for (const int layer : layersOn(floor))
        if (map_->map().layerName(layer) == "walls")
            return layer;
    return addLayer("walls", floor);
}

yh::TileId MapEditor::wallTile() const
{
    if (map_)
        for (size_t i = 0; i < map_->tileTypes().size(); i++)
            if (map_->tileTypes()[i].blocksSight)
                return static_cast<yh::TileId>(i + 1);
    return 0;
}

bool MapEditor::resize(int newWidth, int newHeight)
{
    if (!map_ || newWidth < 1 || newHeight < 1 || newWidth > maxSide || newHeight > maxSide || (newWidth == width() && newHeight == height()))
        return false;
    edit("Resize map", true, [&] {
        map_->setTiles(copyTiles(map_->map(), newWidth, newHeight));
        std::erase_if(lights_, [&](const Light& l) { return l.x >= newWidth || l.y >= newHeight; });
        std::erase_if(markers_, [&](const auto& marker) { return !map_->inside(marker.second); });
        std::vector<yh::ObjectId> outside;
        for (const auto& [id, object] : map_->objects().all())
            if (object.area.x + object.area.w > newWidth * cell || object.area.y + object.area.h > newHeight * cell)
                outside.push_back(id);
        for (const yh::ObjectId id : outside)
            map_->objects().remove(id);
    });
    return true;
}

std::optional<size_t> MapEditor::addLight(const Light& light)
{
    if (!map_ || !(light.x >= 0 && light.y >= 0 && light.x < width() && light.y < height()) || !(light.radius > 0 && light.radius <= 1000))
        return std::nullopt;
    edit("Place light", false, [&] { lights_.push_back(light); });
    return lights_.size() - 1;
}

bool MapEditor::setLight(size_t index, const Light& light, std::string_view mergeKey)
{
    if (!map_ || index >= lights_.size() || lights_[index] == light
        || !(light.x >= 0 && light.y >= 0 && light.x < width() && light.y < height()) || !(light.radius > 0 && light.radius <= 1000))
        return false;
    edit("Change light", false, [&] { lights_[index] = light; }, mergeKey);
    return true;
}

bool MapEditor::removeLight(size_t index)
{
    if (!map_ || index >= lights_.size())
        return false;
    edit("Remove light", false, [&] { lights_.erase(lights_.begin() + static_cast<std::ptrdiff_t>(index)); });
    return true;
}

bool MapEditor::setMarker(const std::string& name, yh::Cell at)
{
    if (!map_ || name.empty() || name.size() > 60 || !map_->inside(at))
        return false;
    const auto found = markers_.find(name);
    if (found != markers_.end() && found->second == at)
        return false;
    edit(found == markers_.end() ? "Place marker" : "Move marker", false, [&] { markers_[name] = at; });
    return true;
}

bool MapEditor::removeMarker(std::string_view name)
{
    const auto found = markers_.find(name);
    if (!map_ || found == markers_.end())
        return false;
    edit("Remove marker", false, [&] { markers_.erase(found); });
    return true;
}

std::optional<yh::ObjectId> MapEditor::objectAt(yh::Cell at, int floor) const
{
    return map_ ? map_->objects().at({(at.x + 0.5f) * cell, (at.y + 0.5f) * cell}, floor) : std::nullopt;
}

std::optional<yh::ObjectId> MapEditor::placeKit(std::string_view kit, yh::Cell at, int floor)
{
    const auto found = kits_.find(kit);
    if (!map_ || found == kits_.end() || !map_->inside(at) || objectAt(at, floor))
        return std::nullopt;
    const yh::Rect& area = found->second.prototype.area;
    if (at.x * cell + area.w > width() * cell || at.y * cell + area.h > height() * cell)
        return std::nullopt;
    std::optional<yh::ObjectId> id;
    try
    {
        edit("Place " + found->first, false, [&] { id = map_->objects().place(found->second, {at.x * cell, at.y * cell}, floor); });
    }
    catch (const std::exception&)
    {
        return std::nullopt; // a kit the framework won't take; nothing was placed
    }
    return id;
}

bool MapEditor::removeObject(yh::ObjectId id)
{
    if (!map_ || !map_->objects().get(id))
        return false;
    edit("Remove object", false, [&] { map_->objects().remove(id); });
    return true;
}

std::vector<std::string> MapEditor::problems()
{
    std::vector<std::string> out;
    if (!map_)
        return out;
    const GameMap& m = map();
    for (const auto& [name, at] : markers_)
        if (!m.walkable(at))
            out.push_back("marker " + name + " is on a cell nobody can stand on");
    for (size_t i = 0; i < lights_.size(); i++)
        if (m.blocksSight({static_cast<int>(lights_[i].x), static_cast<int>(lights_[i].y)}))
            out.push_back("light " + std::to_string(i + 1) + " is inside a wall");
    return out;
}

// ---------------------------------------------------------------- the desktop layout

namespace
{

constexpr float toolColumn = 132, propertyColumn = 236;
constexpr float minZoom = 0.02f, maxZoom = 3.0f;

const std::pair<MapEditorPanel::Tool, const char*> tools[] = {
    {MapEditorPanel::Tool::Paint, "Paint"},   {MapEditorPanel::Tool::Rect, "Fill box"}, {MapEditorPanel::Tool::Wall, "Wall"},
    {MapEditorPanel::Tool::Light, "Light"},   {MapEditorPanel::Tool::Marker, "Marker"}, {MapEditorPanel::Tool::Kit, "Kit"},
};

const yh::Color lightColors[] = {
    {255, 200, 120, 255}, {255, 240, 220, 255}, {255, 120, 70, 255}, {120, 200, 255, 255}, {140, 255, 170, 255}, {200, 140, 255, 255},
};

}

yh::Vec2 MapEditorPanel::toWorld(yh::Vec2 screen, const yh::Rect& view) const
{
    return (screen - view.position()) / zoom_ + pan_;
}

int MapEditorPanel::paintLayer(MapEditor& editor)
{
    if (layer_ < 0)
        layer_ = editor.addLayer(floor_ == 0 ? "ground" : "floor " + std::to_string(floor_), floor_);
    return layer_;
}

void MapEditorPanel::draw(MapEditor& editor, yh::Ui& ui, const yh::Input& input, yh::Renderer& renderer, const yh::Rect& area)
{
    if (!editor.hasMap())
        return;
    const yh::Rect left{area.x, area.y, toolColumn, area.h};
    const yh::Rect right{area.x + area.w - propertyColumn, area.y, propertyColumn, area.h};
    const yh::Rect view{left.x + left.w, area.y, area.w - left.w - right.w, area.h};

    // An undo can take away the layer or light that was selected.
    const std::vector<int> layers = editor.layersOn(floor_);
    if (std::find(layers.begin(), layers.end(), layer_) == layers.end())
        layer_ = layers.empty() ? -1 : layers.front();
    tile_ = std::clamp<yh::TileId>(tile_, 1, static_cast<yh::TileId>(editor.map().tileTypes().size()));
    if (light_ && *light_ >= editor.lights().size())
        light_.reset();
    if (kit_.empty() && !editor.kits().empty())
        kit_ = editor.kits().begin()->first;

    if (zoom_ <= 0 && view.w > 0 && view.h > 0)
    {
        const yh::Rect world = editor.map().map().worldBounds();
        zoom_ = std::clamp(std::min(view.w / world.w, view.h / world.h) * 0.94f, minZoom, maxZoom);
        pan_ = {(world.w - view.w / zoom_) / 2, (world.h - view.h / zoom_) / 2};
    }

    useTool(editor, ui, input, view);
    drawMap(editor, ui, renderer, view);
    ui.panel(left);
    drawTools(editor, ui, left);
    ui.panel(right);
    drawProperties(editor, ui, renderer, right);
}

void MapEditorPanel::useTool(MapEditor& editor, yh::Ui& ui, const yh::Input& input, const yh::Rect& view)
{
    using yh::MouseButton;
    const yh::Vec2 mouse = input.mouse();
    const bool over = input.mouseInside() && view.contains(mouse);
    const bool typing = ui.editing("map-marker");
    const std::optional<yh::Cell> last = hover_;
    hover_.reset();
    const yh::Vec2 world = toWorld(mouse, view);
    const yh::Cell at{static_cast<int>(std::floor(world.x / cell)), static_cast<int>(std::floor(world.y / cell))};
    if (over && editor.map().inside(at))
        hover_ = at;

    // The view: the wheel zooms about the cursor, the middle button and the arrow keys move it.
    if (over && input.wheel() != 0)
    {
        zoom_ = std::clamp(zoom_ * std::pow(1.15f, input.wheel()), minZoom, maxZoom);
        pan_ = pan_ + world - toWorld(mouse, view);
    }
    if (input.buttonDown(MouseButton::Middle) && view.contains(input.buttonPressPosition(MouseButton::Middle)))
        pan_ = pan_ - input.mouseDelta() / zoom_;
    if (!typing && !input.shortcutDown())
    {
        const float step = 14 / zoom_;
        if (input.keyDown(SDLK_LEFT)) pan_.x -= step;
        if (input.keyDown(SDLK_RIGHT)) pan_.x += step;
        if (input.keyDown(SDLK_UP)) pan_.y -= step;
        if (input.keyDown(SDLK_DOWN)) pan_.y += step;
    }

    // Only presses that began on the map count, so letting go of a slider over it does nothing.
    auto held = [&](MouseButton button) { return input.buttonDown(button) && view.contains(input.buttonPressPosition(button)); };
    auto clicked = [&](MouseButton button) { return over && hover_ && input.buttonClicked(button) && view.contains(input.buttonPressPosition(button)); };
    const bool leftHeld = held(MouseButton::Left), rightHeld = held(MouseButton::Right);

    if (tool_ == Tool::Paint || tool_ == Tool::Wall)
    {
        if ((leftHeld || rightHeld) && hover_)
        {
            int layer = -1;
            yh::TileId id = 0;
            if (tool_ == Tool::Paint)
            {
                layer = leftHeld ? paintLayer(editor) : layer_;
                id = leftHeld ? tile_ : yh::TileId(0);
            }
            else
            {
                const bool blocks = editor.map().tileTypes()[tile_ - 1].blocksSight;
                id = leftHeld ? (blocks ? tile_ : editor.wallTile()) : yh::TileId(0);
                if (leftHeld && id == 0)
                    hint_ = "No tile on this map blocks sight";
                else if (leftHeld)
                    layer = editor.wallLayer(floor_);
                else
                    for (const int candidate : editor.layersOn(floor_))
                        if (editor.map().map().layerName(candidate) == "walls")
                            layer = candidate;
            }
            if (layer >= 0)
            {
                // A fast drag skips cells between frames: paint the line from the last one.
                const yh::Cell from = stroking_ && last ? *last : *hover_;
                const int steps = std::max(std::abs(hover_->x - from.x), std::abs(hover_->y - from.y));
                for (int i = 0; i <= steps; i++)
                {
                    const float t = steps ? static_cast<float>(i) / steps : 0;
                    editor.paint(layer, {static_cast<int>(std::lround(from.x + (hover_->x - from.x) * t)),
                        static_cast<int>(std::lround(from.y + (hover_->y - from.y) * t))}, id);
                }
                stroking_ = true;
            }
        }
        else if (stroking_ && !leftHeld && !rightHeld)
        {
            editor.endStroke();
            stroking_ = false;
        }
    }
    else if (stroking_)
    {
        editor.endStroke();
        stroking_ = false;
    }

    if (tool_ == Tool::Rect)
    {
        const bool pressLeft = input.buttonPressed(MouseButton::Left), pressRight = input.buttonPressed(MouseButton::Right);
        if (!rectFrom_ && hover_ && (pressLeft || pressRight))
        {
            rectFrom_ = hover_;
            rectTile_ = pressLeft ? tile_ : yh::TileId(0);
        }
        else if (rectFrom_ && !input.buttonDown(MouseButton::Left) && !input.buttonDown(MouseButton::Right))
        {
            const int layer = rectTile_ ? paintLayer(editor) : layer_;
            if (hover_ && layer >= 0)
                editor.fill(layer, *rectFrom_, *hover_, rectTile_);
            rectFrom_.reset();
        }
    }
    else
        rectFrom_.reset();

    if (tool_ == Tool::Light)
    {
        std::optional<size_t> under;
        for (size_t i = 0; i < editor.lights().size(); i++)
        {
            const MapEditor::Light& light = editor.lights()[i];
            if (std::abs(light.x * cell - world.x) < cell * 0.6f && std::abs(light.y * cell - world.y) < cell * 0.6f)
                under = i;
        }
        if (clicked(MouseButton::Left))
        {
            if (under)
            {
                light_ = under;
                brush_ = editor.lights()[*under];
                radius_ = static_cast<float>(brush_.radius);
            }
            else
            {
                MapEditor::Light light = brush_;
                light.x = hover_->x + 0.5;
                light.y = hover_->y + 0.5;
                light_ = editor.addLight(light);
            }
        }
        else if (clicked(MouseButton::Right) && under)
        {
            editor.removeLight(*under);
            light_.reset();
        }
        if (light_ && !typing && input.keyPressed(SDLK_DELETE))
        {
            editor.removeLight(*light_);
            light_.reset();
        }
    }

    if (tool_ == Tool::Marker && hover_)
    {
        std::string under;
        for (const auto& [name, where] : editor.markers())
            if (where == *hover_)
                under = name;
        if (clicked(MouseButton::Left))
        {
            if (!under.empty())
                markerName_ = under;
            else if (!markerName_.empty())
                editor.setMarker(markerName_, *hover_);
            else
                hint_ = "Give the marker a name first";
        }
        else if (clicked(MouseButton::Right) && !under.empty())
            editor.removeMarker(under);
    }

    if (tool_ == Tool::Kit && hover_)
    {
        const std::optional<yh::ObjectId> under = editor.objectAt(*hover_, floor_);
        if (clicked(MouseButton::Left))
        {
            if (under)
                hint_ = "Something is already there";
            else if (!editor.placeKit(kit_, *hover_, floor_))
                hint_ = kit_.empty() ? "This package has no kits" : "It doesn't fit there";
            else
                hint_.clear();
        }
        else if (clicked(MouseButton::Right) && under)
            editor.removeObject(*under);
    }
}

void MapEditorPanel::drawMap(MapEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& view)
{
    GameMap& map = editor.map();
    map.bindTileset(renderer);
    renderer.fillRect(view, {10, 11, 16, 255});
    renderer.pushViewport(view);
    renderer.pushTransform({-pan_.x * zoom_, -pan_.y * zoom_}, zoom_);

    const yh::Rect visible{pan_.x, pan_.y, view.w / zoom_, view.h / zoom_};
    const yh::Rect bounds = map.map().worldBounds();
    const float px = 1 / zoom_; // one screen pixel in world units
    renderer.fillRect(bounds, {0, 0, 0, 255});
    // The floor below shows through, dimmed, so stairs and walls can be lined up.
    if (floor_ > editor.floors().first)
    {
        map.map().draw(renderer, visible, zoom_, floor_ - 1);
        renderer.fillRect(bounds, {0, 0, 0, 150});
    }
    map.map().draw(renderer, visible, zoom_, floor_);

    const yh::Rect shown = visible.intersect(bounds);
    if (grid_ && cell * zoom_ >= 8 && shown.w > 0 && shown.h > 0)
    {
        const yh::Color line{0, 0, 0, 70};
        for (float x = std::floor(shown.x / cell) * cell; x <= shown.x + shown.w; x += cell)
            renderer.drawLine({x, shown.y}, {x, shown.y + shown.h}, line, px);
        for (float y = std::floor(shown.y / cell) * cell; y <= shown.y + shown.h; y += cell)
            renderer.drawLine({shown.x, y}, {shown.x + shown.w, y}, line, px);
    }

    // Objects as the game draws them for now, with an outline that says what kind each is.
    map.objects().draw(renderer, floor_);
    for (const auto& [id, object] : map.objects().all())
    {
        if (object.floor != floor_)
            continue;
        const yh::Color outline = object.trap ? yh::Color{230, 80, 70, 255} : !object.contents.empty() || object.has("container") ? yh::Color{240, 200, 80, 255}
            : object.door ? yh::Color{120, 200, 255, 255} : yh::Color{230, 230, 240, 255};
        renderer.drawRect(object.area, outline, 2 * px);
    }

    // What blocks sight, as the game will build it: tile edges and shut doors. Floor 0 only, like play.
    if (walls_ && floor_ == 0)
        for (const yh::Wall& wall : map.walls())
            renderer.drawLine(wall.a, wall.b, {255, 130, 60, 255}, 3 * px);

    for (size_t i = 0; i < editor.lights().size(); i++)
    {
        const MapEditor::Light& light = editor.lights()[i];
        const yh::Vec2 center{static_cast<float>(light.x) * cell, static_cast<float>(light.y) * cell};
        renderer.fillCircle(center, static_cast<float>(light.radius) * cell, {light.color.r, light.color.g, light.color.b, 26}, 48);
        renderer.fillCircle(center, cell * 0.24f, {20, 20, 20, 255}, 16);
        renderer.fillCircle(center, cell * 0.18f, light.color, 16);
        if (light_ && *light_ == i)
            renderer.drawRect({center.x - cell * 0.4f, center.y - cell * 0.4f, cell * 0.8f, cell * 0.8f}, ui.theme.accent, 2 * px);
    }

    for (const auto& [name, at] : editor.markers())
    {
        const yh::Rect box{(at.x + 0.2f) * cell, (at.y + 0.2f) * cell, cell * 0.6f, cell * 0.6f};
        renderer.fillRect(box, {110, 220, 160, 150});
        renderer.drawRect(box, name == markerName_ && tool_ == Tool::Marker ? ui.theme.accent : yh::Color{20, 60, 40, 255}, 2 * px);
    }

    if (rectFrom_ && hover_)
    {
        const int x0 = std::min(rectFrom_->x, hover_->x), x1 = std::max(rectFrom_->x, hover_->x);
        const int y0 = std::min(rectFrom_->y, hover_->y), y1 = std::max(rectFrom_->y, hover_->y);
        const yh::Rect box{x0 * cell, y0 * cell, (x1 - x0 + 1) * cell, (y1 - y0 + 1) * cell};
        renderer.fillRect(box, rectTile_ ? yh::Color{255, 255, 255, 50} : yh::Color{230, 80, 70, 60});
        renderer.drawRect(box, {255, 255, 255, 220}, 2 * px);
    }
    else if (hover_)
        renderer.drawRect({hover_->x * cell, hover_->y * cell, cell, cell}, {255, 255, 255, 220}, 2 * px);
    renderer.drawRect(bounds, {150, 152, 170, 255}, 2 * px);
    renderer.pop();

    // Names, at screen size whatever the zoom.
    if (cell * zoom_ >= 14)
    {
        auto text = [&](yh::Vec2 worldAt, const std::string& words, yh::Color color) {
            const yh::Vec2 at = (worldAt - pan_) * zoom_;
            if (ui.theme.font)
            {
                ui.theme.font->draw(renderer, at + yh::Vec2{1, 1}, words, {0, 0, 0, 220});
                ui.theme.font->draw(renderer, at, words, color);
            }
            else
                renderer.drawText(at, words, color, 1.5f);
        };
        for (const auto& [name, at] : editor.markers())
            text({at.x * cell, (at.y + 0.8f) * cell}, name, {170, 245, 200, 255});
        for (const auto& [id, object] : map.objects().all())
            if (object.floor == floor_ && !object.name.empty())
                text({object.area.x, object.area.y + object.area.h}, object.name, {235, 235, 245, 255});
    }
    renderer.pop();
    renderer.drawRect(view, ui.theme.panelBorder, 1);
}

void MapEditorPanel::drawTools(MapEditor& editor, yh::Ui& ui, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 30;
    float y = column.y + 8;
    for (const auto& [tool, name] : tools)
    {
        if (ui.toggle({x, y, w, h}, name, tool_ == tool))
        {
            tool_ = tool;
            hint_.clear();
        }
        y += h + 4;
    }
    y += 10;
    ui.checkbox({x, y, w, h}, "Grid", grid_);
    y += h + 4;
    ui.checkbox({x, y, w, h}, "Walls", walls_);
    y += h + 4;
    if (ui.button({x, y, w, h}, "Fit map"))
        zoom_ = 0;
    y += h + 12;

    ui.label({x, y}, std::to_string(editor.width()) + " x " + std::to_string(editor.height()), ui.theme.textDim);
    y += 22;
    if (hover_)
        ui.label({x, y}, std::to_string(hover_->x) + ", " + std::to_string(hover_->y), ui.theme.textDim);
    y += 30;
    for (const char* line : {"Left: use", "Right: remove", "Middle: move", "Wheel: zoom"})
    {
        ui.label({x, y}, line, ui.theme.textDim);
        y += 20;
    }
}

void MapEditorPanel::drawProperties(MapEditor& editor, yh::Ui& ui, yh::Renderer& renderer, const yh::Rect& column)
{
    const float x = column.x + 8, w = column.w - 16, h = 28;
    float y = column.y + 8;
    GameMap& map = editor.map();

    ui.label({x, y + 4}, "Floor " + std::to_string(floor_), ui.theme.accent);
    if (ui.button({x + w - 68, y, 32, h}, "-", floor_ > lowestFloor))
        floor_--;
    if (ui.button({x + w - 32, y, 32, h}, "+", floor_ < highestFloor))
        floor_++;
    y += h + 6;

    const std::vector<int> layers = editor.layersOn(floor_);
    for (const int layer : layers)
    {
        if (ui.toggle({x, y, w, h}, map.map().layerName(layer), layer == layer_))
            layer_ = layer;
        y += h + 3;
    }
    if (layers.empty())
    {
        ui.label({x, y + 4}, "Nothing on this floor yet", ui.theme.textDim);
        y += h + 3;
    }
    if (ui.button({x, y, w / 2 - 2, h}, "Add layer"))
        layer_ = editor.addLayer({}, floor_);
    if (ui.button({x + w / 2 + 2, y, w / 2 - 2, h}, "Remove", layer_ >= 0 && editor.layerCount() > 1))
        editor.removeLayer(layer_);
    y += h + 10;

    ui.label({x, y + 4}, "Size " + std::to_string(editor.width()) + " x " + std::to_string(editor.height()));
    y += h;
    const float quarter = (w - 9) / 4;
    const std::pair<const char*, yh::Cell> sizes[] = {{"W-", {-1, 0}}, {"W+", {1, 0}}, {"H-", {0, -1}}, {"H+", {0, 1}}};
    for (int i = 0; i < 4; i++)
        if (ui.button({x + i * (quarter + 3), y, quarter, h}, sizes[i].first))
            editor.resize(editor.width() + sizes[i].second.x, editor.height() + sizes[i].second.y);
    y += h + 12;

    const float bottom = column.y + column.h - 8;
    if (tool_ == Tool::Paint || tool_ == Tool::Rect || tool_ == Tool::Wall)
    {
        ui.label({x, y}, tool_ == Tool::Wall ? "Wall tile" : "Tile", ui.theme.accent);
        y += 24;
        const std::vector<GameMap::TileType>& types = map.tileTypes();
        const yh::Tileset& tileset = map.map().tileset();
        const float row = 34;
        const yh::Rect list{x, y, w, std::max(row, bottom - y - (hint_.empty() ? 0.0f : 24.0f))};
        ui.beginScroll(list, types.size() * row, scroll_);
        for (size_t i = 0; i < types.size(); i++)
        {
            const yh::TileId id = static_cast<yh::TileId>(i + 1);
            const float top = i * row;
            // In wall mode only tiles that block sight make walls; the rest are dimmed.
            const bool usable = tool_ != Tool::Wall || types[i].blocksSight;
            if (ui.toggle({36, top, w - 36, row - 4}, types[i].name, id == tile_) && usable)
                tile_ = id;
            if (tileset.texture)
                renderer.drawSpriteRegion(tileset.texture, {0, top, row - 4, row - 4}, tileset.uv(id), usable ? yh::Color{255, 255, 255, 255} : yh::Color{255, 255, 255, 90});
            else
                renderer.fillRect({0, top, row - 4, row - 4}, types[i].color);
        }
        ui.endScroll();
        y = list.y + list.h + 4;
    }
    else if (tool_ == Tool::Light)
    {
        ui.label({x, y}, light_ ? "Light " + std::to_string(*light_ + 1) : std::string("New light"), ui.theme.accent);
        y += 24;
        MapEditor::Light changed = brush_;
        ui.label({x, y}, "Radius " + std::to_string(static_cast<int>(changed.radius)) + " cells");
        y += 22;
        if (ui.slider({x, y, w, 18}, radius_, 1, 20))
            changed.radius = std::round(radius_);
        y += 28;
        const float swatch = (w - 5 * 4) / 6;
        for (size_t i = 0; i < std::size(lightColors); i++)
        {
            const yh::Rect box{x + i * (swatch + 4), y, swatch, swatch};
            if (ui.button(box, ""))
                changed.color = lightColors[i];
            renderer.fillRect({box.x + 4, box.y + 4, box.w - 8, box.h - 8}, lightColors[i]);
            if (lightColors[i] == brush_.color)
                renderer.drawRect(box, ui.theme.accent, 2);
        }
        y += swatch + 8;
        ui.checkbox({x, y, w, h}, "Flame", changed.flame);
        y += h + 8;
        if (!(changed == brush_))
        {
            brush_ = changed;
            if (light_)
            {
                // The brush keeps the settings; the light keeps its place.
                MapEditor::Light light = brush_;
                light.x = editor.lights()[*light_].x;
                light.y = editor.lights()[*light_].y;
                editor.setLight(*light_, light, "light-" + std::to_string(*light_));
            }
        }
        if (ui.button({x, y, w, h}, "Remove (Del)", light_.has_value()))
        {
            editor.removeLight(*light_);
            light_.reset();
        }
        y += h + 8;
        ui.label({x, y}, std::to_string(editor.lights().size()) + " on this map", ui.theme.textDim);
        y += 24;
    }
    else if (tool_ == Tool::Marker)
    {
        ui.label({x, y}, "Marker name", ui.theme.accent);
        y += 24;
        ui.textBox("map-marker", {x, y, w, 34}, markerName_, 60);
        y += 42;
        for (const auto& [name, at] : editor.markers())
        {
            if (y + h > bottom - 24)
                break;
            if (ui.toggle({x, y, w, h}, name + "  " + std::to_string(at.x) + ", " + std::to_string(at.y), name == markerName_))
                markerName_ = name;
            y += h + 3;
        }
    }
    else if (tool_ == Tool::Kit)
    {
        ui.label({x, y}, "Kit", ui.theme.accent);
        y += 24;
        for (const auto& [id, kit] : editor.kits())
        {
            if (y + h > bottom - 24)
                break;
            if (ui.toggle({x, y, w, h}, kit.name.empty() ? id : kit.name, id == kit_))
                kit_ = id;
            y += h + 3;
        }
        if (editor.kits().empty())
        {
            ui.label({x, y}, "No kits in this package", ui.theme.textDim);
            y += 24;
        }
    }
    if (!hint_.empty())
        ui.label({x, std::min(y, bottom - 20)}, hint_, ui.theme.bad);
}
