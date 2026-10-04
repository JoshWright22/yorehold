// Tests for map loading, objects, and interactions (F1 implementation).

#include "WorldFixture.h"

#include <gtest/gtest.h>

namespace
{

// A minimal chapter with a map containing interactive objects
const std::string chapterJson = R"({
  "id": "test-map",
  "title": "Test Map",
  "level": 1,
  "party": [
    {"name": "Hero", "classId": "fighter", "color": [255, 0, 0, 255], "at": [1, 1]}
  ],
  "encounters": [],
  "npcs": []
})";

const std::string mapJson = R"({
  "name": "Test Map",
  "tiles": {
    "grass": {"art": "grass"},
    "wall": {"art": "wall", "walkable": false, "blocksSight": true},
    "stone": {"art": "stone"}
  },
  "legend": {".": "grass", "#": "wall", "_": "stone"},
  "layers": [
    {
      "name": "ground",
      "rows": [
        "......#",
        ".##..#",
        ".#_..#",
        ".#...#",
        ".#####"
      ]
    }
  ],
  "markers": {
    "start": [1, 1]
  },
  "objects": [
    {
      "name": "Test Door",
      "at": [2, 1],
      "width": 64,
      "height": 64,
      "tags": ["door", "blocksMovement"],
      "door": {"open": false, "locked": false}
    },
    {
      "name": "Test Chest",
      "at": [4, 1],
      "width": 64,
      "height": 64,
      "tags": ["container"],
      "contents": {"gold-ring": 1, "potion-healing": 2},
      "coins": 150
    },
    {
      "name": "Test Lever",
      "at": [2, 2],
      "width": 32,
      "height": 32,
      "tags": ["lever", "link:door-test"],
      "door": {"open": false, "locked": false}
    }
  ]
})";

class MapTests : public ::testing::Test
{
protected:
    WorldFixture world;
    std::string error;
};

TEST_F(MapTests, LoadMapWithObjects)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    // Verify map loaded
    const GameMap& map = world.map();
    EXPECT_EQ(map.name(), "Test Map");
    EXPECT_EQ(map.width(), 7);
    EXPECT_EQ(map.height(), 5);

    // Verify objects loaded
    const yh::Objects& objects = map.objects();
    EXPECT_EQ(objects.all().size(), 3);
}

TEST_F(MapTests, MapObjectsBlocking)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    const GameMap& map = world.map();
    const yh::Objects& objects = map.objects();

    // Verify door blocks movement when closed
    EXPECT_FALSE(objects.passable({128, 64, 64, 64}, 0));
}

TEST_F(MapTests, DoorInteraction)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    GameMap& map = world.map();
    yh::Objects& objects = map.objects();

    // Get the door object (first one in the test data)
    const auto& allObjects = objects.all();
    ASSERT_FALSE(allObjects.empty());

    // Get the door and verify it's closed
    yh::ObjectId doorId = allObjects.begin()->first;
    yh::MapObject* door = objects.get(doorId);
    ASSERT_TRUE(door);
    ASSERT_TRUE(door->door);
    EXPECT_FALSE(door->door->open);

    // Interact with it to open it
    yh::Interaction result = objects.interact(doorId, {});
    EXPECT_EQ(result, yh::Interaction::Opened);

    // Verify it's now open
    EXPECT_TRUE(door->door->open);

    // Interact again to close it
    result = objects.interact(doorId, {});
    EXPECT_EQ(result, yh::Interaction::Closed);
    EXPECT_FALSE(door->door->open);
}

TEST_F(MapTests, LeverInteraction)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    GameMap& map = world.map();
    yh::Objects& objects = map.objects();

    // Find the lever (has "lever" tag)
    yh::ObjectId leverId = 0;
    for (const auto& [id, obj] : objects.all()) {
        if (obj.has("lever")) {
            leverId = id;
            break;
        }
    }
    ASSERT_NE(leverId, 0);

    // Lever should activate without issue
    yh::Interaction result = objects.interact(leverId, {});
    EXPECT_EQ(result, yh::Interaction::Activated);
}

TEST_F(MapTests, ContainerInteraction)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    GameMap& map = world.map();
    yh::Objects& objects = map.objects();

    // Find the chest (has "container" tag)
    yh::ObjectId chestId = 0;
    for (const auto& [id, obj] : objects.all()) {
        if (obj.has("container")) {
            chestId = id;
            break;
        }
    }
    ASSERT_NE(chestId, 0);

    yh::MapObject* chest = objects.get(chestId);
    ASSERT_TRUE(chest);

    // Take items from the container
    int potion = objects.take(chestId, "potion-healing", 1);
    EXPECT_EQ(potion, 1);

    // Verify item count decreased
    auto it = chest->contents.find("potion-healing");
    ASSERT_TRUE(it != chest->contents.end());
    EXPECT_EQ(it->second, 1); // Had 2, now has 1

    // Take coins
    int coins = objects.take(chestId, "copper", 50);
    EXPECT_EQ(coins, 0); // Coins aren't stored as items in contents
}

TEST_F(MapTests, LockAndKey)
{
    const std::string mapWithLock = R"({
  "name": "Locked Map",
  "tiles": {
    "grass": {"art": "grass"},
    "wall": {"art": "wall", "walkable": false, "blocksSight": true}
  },
  "legend": {".": "grass", "#": "wall"},
  "layers": [{
    "name": "ground",
    "rows": [".....", "#####"]
  }],
  "markers": {"start": [0, 0]},
  "objects": [{
    "name": "Locked Door",
    "at": [2, 0],
    "width": 64,
    "height": 64,
    "tags": ["door", "key:gold"],
    "door": {"open": false, "locked": true}
  }]
})";

    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapWithLock}
    }, 0, &error)) << error;

    GameMap& map = world.map();
    yh::Objects& objects = map.objects();

    yh::ObjectId doorId = objects.all().begin()->first;

    // Try to interact without key - should be locked
    yh::Interaction result = objects.interact(doorId, {});
    EXPECT_EQ(result, yh::Interaction::Locked);

    // Try with wrong key
    result = objects.interact(doorId, {"key:silver"});
    EXPECT_EQ(result, yh::Interaction::Locked);

    // Try with correct key
    result = objects.interact(doorId, {"key:gold"});
    EXPECT_EQ(result, yh::Interaction::Opened);

    // Should now be unlocked
    yh::MapObject* door = objects.get(doorId);
    EXPECT_FALSE(door->door->locked);
    EXPECT_TRUE(door->door->open);
}

TEST_F(MapTests, MapLoadPreservesPlayerMarkersAndWalls)
{
    ASSERT_TRUE(world.loadJson("fixture:chapters/test", {
        {"chapters/test/chapter.json", chapterJson},
        {"chapters/test/map.json", mapJson}
    }, 0, &error)) << error;

    const GameMap& map = world.map();

    // Verify marker loaded
    EXPECT_TRUE(map.marker("start"));
    yh::Cell start = *map.marker("start");
    EXPECT_EQ(start.x, 1);
    EXPECT_EQ(start.y, 1);

    // Verify walls exist
    EXPECT_FALSE(map.walls().empty());
}

}
