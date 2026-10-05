// Map mode of the Create screen: its commands, undo, and what it writes to map.json.

#include "content/GameMap.h"
#include "screens/CreateScreen.h"
#include "screens/MapEditor.h"

#include <yorehold/framework/save/SaveFile.h>

#include <nlohmann/json.hpp>

#include <filesystem>
#include <functional>

namespace
{

using Check = std::function<void(bool, const char*)>;
using nlohmann::json;

// A walled room in a field, written by hand the way chapters are.
const char* roomJson = R"({
  "name": "Test Map",
  "tiles": {
    "grass": {"art": "grass"},
    "wall": {"art": "wall", "walkable": false, "blocksSight": true},
    "stone": {"art": "stone", "indoors": true}
  },
  "legend": {".": "grass", "#": "wall", "_": "stone"},
  "layers": [
    {"name": "ground", "rows": ["........", ".######.", ".#____#.", ".#____#.", ".######.", "........"]}
  ],
  "lighting": {"mode": "rules", "time": "dusk"},
  "lights": [{"at": [3.5, 2.5], "color": [255, 200, 120], "radius": 5, "flame": true}],
  "markers": {"partyStart": [0, 0], "exit": [7, 5]}
})";

GameMap::Kits testKits()
{
    GameMap::Kits kits;
    kits["door"] = *yh::Kit::fromJson(R"({"name":"Door","object":{"name":"the door","area":[0,0,64,64],"tags":["blocksMovement","blocksSight"],"door":{}}})");
    return kits;
}

// The id of a tile type by name, as the editor's palette numbers them.
yh::TileId tileNamed(MapEditor& editor, std::string_view name)
{
    const auto& types = editor.map().tileTypes();
    for (size_t i = 0; i < types.size(); i++)
        if (types[i].name == name)
            return static_cast<yh::TileId>(i + 1);
    return 0;
}

void loadAndSave(const Check& check)
{
    yh::History history;
    MapEditor editor(history);
    std::string error;
    check(!editor.load("{not json", &error) && !editor.hasMap() && !error.empty(), "A broken map is refused with a reason");
    check(editor.load(roomJson, &error, testKits()) && editor.hasMap(), "A hand-written map loads into the editor");
    check(editor.width() == 8 && editor.height() == 6 && editor.layerCount() == 1, "It has the size and layers of the file");
    check(editor.lights().size() == 1 && editor.lights()[0].x == 3.5 && editor.lights()[0].radius == 5, "Lights are read in cells");
    check(editor.markers().size() == 2 && editor.markers().at("exit") == yh::Cell{7, 5}, "Markers are read");
    check(history.size() == 0 && !history.dirty(), "Loading is not an edit");

    const json saved = json::parse(editor.toJson());
    check(saved.value("name", "") == "Test Map" && saved.contains("tileMap") && saved.at("tiles").is_array(), "It saves in the editor's form");
    check(saved.at("lights").size() == 1 && saved.at("lights")[0].at("at") == json({3.5, 2.5}) && saved.at("lights")[0].at("color").size() == 3
        && saved.at("lights")[0].at("radius") == 5 && saved.at("lights")[0].at("flame") == true, "Lights are written as the game reads them");
    check(saved.at("markers").at("partyStart") == json({0, 0}), "Markers are written");
    check(saved.at("lighting").value("time", "") == "dusk", "Fields the editor has no tool for are kept");

    const std::optional<GameMap> again = GameMap::fromJson(editor.toJson(), &error);
    check(again && again->width() == 8 && again->lights().size() == 1 && again->marker("exit").has_value() && !again->walkable({1, 1}),
        "The game loads what the editor saves");

    MapEditor old(history);
    check(old.load(R"({"name":"Old","tiles":{"grass":{"art":"grass"}},"legend":{".":"grass"},"layers":[{"name":"ground","rows":["..."]}]})")
        && old.lights().empty() && old.markers().empty(), "A map with no lights or markers loads");
    check(json::parse(old.toJson()).at("lights").empty(), "... and saves with none");

    MapEditor blank(history);
    check(blank.load(MapEditor::blankMap("New", 24, 16), &error) && blank.width() == 24 && blank.height() == 16, "A blank map loads");
    check(blank.wallTile() != 0 && blank.map().walkable({5, 5}), "It has ground to walk on and a tile for walls");
}

void tiles(const Check& check)
{
    yh::History history;
    MapEditor editor(history);
    editor.load(roomJson);
    const yh::TileId wall = tileNamed(editor, "wall"), grass = tileNamed(editor, "grass");
    const size_t wallsBefore = editor.map().walls().size();

    check(editor.paint(0, {0, 0}, wall) && editor.paint(0, {1, 0}, wall) && editor.paint(0, {2, 0}, wall), "Painting changes cells");
    editor.endStroke();
    check(!editor.paint(0, {0, 0}, wall), "Painting the same tile again is not an edit");
    check(!editor.paint(0, {99, 0}, wall) && !editor.paint(3, {0, 0}, wall) && !editor.paint(0, {0, 0}, 99), "Cells, layers and tiles that aren't there are refused");
    check(history.size() == 1, "One stroke is one undo step");
    check(!editor.map().walkable({1, 0}) && editor.map().blocksSight({1, 0}) && editor.map().walls().size() > wallsBefore,
        "Painted walls block walking and sight straight away");
    check(history.undo() && editor.map().map().tile(0, 2, 0) == grass && editor.map().walkable({1, 0}) && editor.map().walls().size() == wallsBefore,
        "Undo takes the whole stroke back");
    check(history.redo() && editor.map().map().tile(0, 0, 0) == wall, "Redo paints it again");

    check(editor.paint(0, {5, 0}, 0) && !editor.map().walkable({5, 0}), "Erasing the ground leaves a hole");
    editor.endStroke();
    check(editor.paint(0, {6, 0}, wall), "A new stroke starts after the last one ended");
    editor.endStroke();
    check(history.size() == 3, "... as its own undo step");

    check(editor.fill(0, {6, 5}, {3, 3}, grass), "A box can be filled from any corner");
    check(editor.map().map().tile(0, 3, 3) == grass && editor.map().map().tile(0, 6, 5) == grass && editor.map().walkable({6, 4}), "Every cell in it changes");
    check(!editor.fill(0, {3, 3}, {6, 5}, grass), "Filling with what is there is not an edit");
    check(history.undo() && editor.map().map().tile(0, 6, 4) == wall && editor.map().map().tile(0, 3, 3) != grass, "Undo puts back each cell's own tile");
    check(editor.fill(0, {-5, -5}, {0, 0}, grass) && editor.map().map().tile(0, 0, 0) == grass, "A box partly off the map fills the part on it");
}

void layersAndSize(const Check& check)
{
    yh::History history;
    MapEditor editor(history);
    editor.load(roomJson, nullptr, testKits());
    const yh::TileId wall = tileNamed(editor, "wall"), stone = tileNamed(editor, "stone");

    const int upstairs = editor.addLayer("upstairs", 1);
    check(upstairs == 1 && editor.layerCount() == 2 && editor.layersOn(1) == std::vector<int>{1} && editor.floors() == std::pair<int, int>{0, 1},
        "A layer can be added on another floor");
    editor.paint(upstairs, {2, 2}, stone);
    editor.endStroke();
    const std::optional<GameMap> saved = GameMap::fromJson(editor.toJson());
    check(saved && saved->map().layerCount() == 2 && saved->map().layerFloor(1) == 1 && saved->map().tile(1, 2, 2) == stone, "Floors and their tiles are saved");
    check(saved && saved->walkable({2, 2}), "Only floor 0 decides where the party walks");

    const int walls = editor.wallLayer(0);
    check(walls == 2 && editor.map().map().layerName(walls) == "walls" && editor.wallLayer(0) == walls, "A floor gets one walls layer");
    check(editor.wallLayer(1) == 3, "Each floor has its own");
    editor.paint(walls, {0, 3}, wall);
    editor.endStroke();
    check(!editor.map().walkable({0, 3}), "A wall over the ground stops walking");

    check(editor.removeLayer(upstairs) && editor.layerCount() == 3 && editor.map().map().layerName(1) == "walls", "A layer can be removed");
    check(editor.map().map().tile(1, 0, 3) == wall, "The layers after it keep their tiles");
    check(history.undo() && editor.layerCount() == 4 && editor.map().map().tile(upstairs, 2, 2) == stone && editor.map().map().layerFloor(upstairs) == 1,
        "Undo brings the layer back with what was painted on it");
    history.redo();

    MapEditor single(history);
    single.load(roomJson);
    check(!single.removeLayer(0), "The last layer stays");

    // Shrinking drops what is left outside; undo brings all of it back.
    editor.placeKit("door", {7, 0}, 0);
    const size_t steps = history.size();
    check(editor.resize(6, 4) && editor.width() == 6 && editor.height() == 4 && editor.map().width() == 6, "The map can be made smaller");
    check(!editor.markers().contains("exit") && editor.markers().contains("partyStart") && editor.map().objects().all().empty(),
        "Markers and objects outside the new size go");
    check(editor.map().map().tile(0, 1, 1) == wall && history.size() == steps + 1, "Cells are kept from the top-left");
    check(GameMap::fromJson(editor.toJson()).has_value(), "The smaller map still loads");
    check(history.undo() && editor.width() == 8 && editor.markers().contains("exit") && editor.map().objects().all().size() == 1
        && editor.map().map().tile(0, 6, 4) == wall, "Undo restores the size and everything dropped");
    check(editor.resize(10, 8) && editor.map().map().tile(0, 9, 7) == 0 && !editor.map().walkable({9, 7}), "A bigger map starts empty in the new part");
    check(!editor.resize(0, 5) && !editor.resize(10, 8), "Sizes that are wrong or unchanged are refused");
}

void lightsMarkersKits(const Check& check)
{
    yh::History history;
    MapEditor editor(history);
    editor.load(roomJson, nullptr, testKits());

    MapEditor::Light torch;
    torch.x = 4.5;
    torch.y = 3.5;
    torch.radius = 3;
    torch.color = {120, 200, 255, 255};
    torch.flame = false;
    const std::optional<size_t> placed = editor.addLight(torch);
    check(placed && *placed == 1 && editor.lights().size() == 2, "A light is placed");
    torch.x = 20;
    check(!editor.addLight(torch), "... but not outside the map");
    MapEditor::Light wider = editor.lights()[1];
    for (int radius = 4; radius <= 8; radius++)
    {
        wider.radius = radius;
        editor.setLight(1, wider, "light-1");
    }
    check(editor.lights()[1].radius == 8 && history.size() == 2, "Dragging a light's radius is one undo step");
    const json light = json::parse(editor.toJson()).at("lights")[1];
    check(light.at("at") == json({4.5, 3.5}) && light.at("radius") == 8 && light.at("color") == json({120, 200, 255}) && light.at("flame") == false,
        "The light is saved with its colour, radius and flame");
    check(history.undo() && editor.lights()[1].radius == 3, "Undo puts the radius back");
    check(editor.removeLight(0) && editor.lights().size() == 1 && editor.lights()[0].radius == 3, "A light can be removed");
    check(history.undo() && editor.lights().size() == 2 && editor.lights()[0].x == 3.5, "Undo puts it back in its place");

    check(editor.setMarker("ambush", {3, 2}) && editor.markers().at("ambush") == yh::Cell{3, 2}, "A marker is placed");
    check(editor.setMarker("ambush", {4, 3}) && editor.markers().size() == 3, "Placing a name again moves it");
    check(!editor.setMarker("ambush", {4, 3}) && !editor.setMarker("", {1, 1}) && !editor.setMarker("far", {40, 1}), "Unnamed or off-map markers are refused");
    check(history.undo() && editor.markers().at("ambush") == yh::Cell{3, 2}, "Undo moves it back");
    check(editor.removeMarker("ambush") && !editor.removeMarker("ambush"), "A marker can be removed once");
    check(history.undo() && editor.markers().contains("ambush"), "Undo brings it back");
    check(editor.setMarker("stuck", {1, 1}) && editor.problems().size() == 1, "A marker inside a wall is pointed out");
    history.undo();
    check(editor.problems().empty(), "The room as written has no problems");

    check(editor.kits().size() == 1 && !editor.placeKit("portcullis", {0, 0}, 0), "Only kits the package has can be placed");
    check(editor.map().walkable({0, 2}), "The cell is open before the door");
    const std::optional<yh::ObjectId> door = editor.placeKit("door", {0, 2}, 0);
    check(door && editor.objectAt({0, 2}, 0) == door && !editor.map().walkable({0, 2}), "A placed door is on its cell and shut");
    check(!editor.placeKit("door", {0, 2}, 0) && !editor.placeKit("door", {-1, 0}, 0), "Not on top of another object or off the map");
    check(editor.placeKit("door", {0, 2}, 1).has_value(), "Another floor is another place");
    const std::optional<GameMap> saved = GameMap::fromJson(editor.toJson());
    check(saved && saved->objects().all().size() == 2 && saved->objectAt({0, 2}) && !saved->walkable({0, 2}), "Placed kits are saved as whole objects, no kit file needed");
    check(editor.removeObject(*door) && !editor.objectAt({0, 2}, 0) && editor.map().walkable({0, 2}), "An object can be removed");
    check(history.undo() && editor.objectAt({0, 2}, 0) == door, "Undo brings it back as the same object");
}

void sharedHistory(const Check& check, const std::filesystem::path& scratch)
{
    // Two maps on one history, as two chapters open in the Create screen are.
    {
        yh::History history;
        MapEditor first(history), second(history);
        first.load(roomJson);
        second.load(roomJson);
        first.setMarker("a", {0, 1});
        second.setMarker("b", {0, 2});
        check(history.undo() && !second.markers().contains("b") && first.markers().contains("a"), "Undo takes back the latest edit, whichever map it was on");
        check(history.undo() && !first.markers().contains("a") && !history.canUndo(), "... then the one before, on the other map");
    }

    yh::Ui ui;
    yh::Input input;
    yh::Font* title = nullptr;
    CreateScreen screen(ui, input, title);
    screen.table.stateDir = (scratch / "create-state").generic_string() + "/";
    screen.newPackage();
    check(screen.isOpen(), "New makes a package and opens it");
    const std::filesystem::path folder = screen.packagePath();
    const std::filesystem::path file = folder / "chapters/chapter-one/map.json";
    check(std::filesystem::exists(folder / "content.json") && std::filesystem::exists(file), "It is a folder with a manifest, a chapter and a map");
    MapEditor* editor = screen.mapEditor();
    check(editor && editor->width() == 24 && editor->markers().contains("partyStart"), "Map mode opens the chapter's map");
    if (!editor)
        return;
    check(!editor->kits().empty(), "The game's own kits are there to place");

    const std::string untouched = *yh::readTextFile(file.string());
    check(screen.save() && *yh::readTextFile(file.string()) == untouched, "Saving with nothing changed leaves the file alone");
    const yh::TileId wall = editor->wallTile();
    editor->paint(editor->wallLayer(0), {5, 5}, wall);
    editor->endStroke();
    editor->setMarker("exit", {20, 10});
    check(screen.history().dirty(), "Edits make the package unsaved");
    check(screen.save() && !screen.history().dirty(), "Save writes it");
    std::string error;
    const std::optional<GameMap> written = GameMap::fromJson(*yh::readTextFile(file.string()), &error);
    check(written && !written->walkable({5, 5}) && written->marker("exit") == yh::Cell{20, 10} && written->marker("partyStart").has_value(),
        "map.json on disk has the edits and loads in the game");
    screen.undo();
    check(!editor->markers().contains("exit") && screen.history().dirty(), "Undo after a save makes it unsaved again");
    screen.redo();
    check(editor->markers().contains("exit") && !screen.history().dirty(), "Redo comes back to the saved state");

    // Opening it again reads what was saved, with a fresh history.
    screen.openPackage(folder.generic_string());
    check(screen.isOpen() && !screen.history().canUndo() && screen.mapEditor() && screen.mapEditor()->markers().contains("exit"), "The saved package opens again");
    screen.newPackage();
    check(screen.isOpen() && std::filesystem::path(screen.packagePath()) != folder, "A second new package gets its own folder");

    // The game's own content opens too, hand-written maps and kit names included.
    screen.openPackage(YH_GAME_ASSETS);
    check(screen.isOpen() && screen.mapEditor() && screen.mapEditor()->width() > 0, "The built-in adventure's map opens in the editor");
    screen.openPackage((scratch / "nowhere").generic_string());
    check(!screen.isOpen() && !screen.status().empty(), "A path with no package says so");
}

}

void mapEditorTests(const Check& check, const std::filesystem::path& scratch)
{
    loadAndSave(check);
    tiles(check);
    layersAndSize(check);
    lightsMarkersKits(check);
    sharedHistory(check, scratch);
}
