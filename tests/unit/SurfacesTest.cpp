#include <gtest/gtest.h>

#include "sim/World.h"
#include "WorldFixture.h"

class SurfacesTest : public ::testing::Test
{
protected:
    WorldFixture world;
};

// Test that surfaces can be created on the map
TEST_F(SurfacesTest, CreateSurface)
{
    ASSERT_TRUE(world.load("chapters/goblin-keep", 42));

    // No surfaces initially
    EXPECT_EQ(0, world.surfaces().size());

    // Create a fire surface at cell (10, 10) with 2 rounds duration
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({10, 10})},
        {"id", "fire"},
        {"size", 1},
        {"rounds", 2}
    }));

    // Surface should be created
    EXPECT_EQ(1, world.surfaces().size());
    EXPECT_EQ("fire", world.surfaces()[0].id);
    EXPECT_EQ(10, world.surfaces()[0].at.x);
    EXPECT_EQ(10, world.surfaces()[0].at.y);
    EXPECT_EQ(2, world.surfaces()[0].durationLeft);
}

// Test that surfaces expire after their duration
TEST_F(SurfacesTest, SurfaceExpires)
{
    ASSERT_TRUE(world.load("chapters/goblin-keep", 42));

    // Create a fire surface with 1 round duration
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({10, 10})},
        {"id", "fire"},
        {"size", 1},
        {"rounds", 1}
    }));

    EXPECT_EQ(1, world.surfaces().size());

    // Step to the end of the round
    world.step(0.1);

    // Surface should be gone
    EXPECT_EQ(0, world.surfaces().size());
}

// Test that surfacesAt returns surfaces at a specific cell
TEST_F(SurfacesTest, SurfacesAtCell)
{
    ASSERT_TRUE(world.load("chapters/goblin-keep", 42));

    // Create fire at (5, 5)
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({5, 5})},
        {"id", "fire"},
        {"size", 1},
        {"rounds", 5}
    }));

    // Create water at (6, 5)
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({6, 5})},
        {"id", "water"},
        {"size", 1},
        {"rounds", 5}
    }));

    auto at5_5 = world.surfacesAt(yh::Cell{5, 5});
    auto at6_5 = world.surfacesAt(yh::Cell{6, 5});
    auto at7_5 = world.surfacesAt(yh::Cell{7, 5});

    EXPECT_EQ(1, at5_5.size());
    EXPECT_EQ("fire", at5_5[0]);
    EXPECT_EQ(1, at6_5.size());
    EXPECT_EQ("water", at6_5[0]);
    EXPECT_EQ(0, at7_5.size());
}

// Test that water extinguishes fire
TEST_F(SurfacesTest, WaterExtinguishesFire)
{
    ASSERT_TRUE(world.load("chapters/goblin-keep", 42));

    // Create fire at (5, 5)
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({5, 5})},
        {"id", "fire"},
        {"size", 1},
        {"rounds", 5}
    }));

    EXPECT_EQ(1, world.surfaces().size());
    EXPECT_EQ("fire", world.surfaces()[0].id);

    // Create water on the same cell
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({5, 5})},
        {"id", "water"},
        {"size", 1},
        {"rounds", 5}
    }));

    // Fire should be extinguished by water
    auto at5_5 = world.surfacesAt(yh::Cell{5, 5});
    bool hasFire = false;
    bool hasWater = false;
    for (const auto& id : at5_5)
    {
        if (id == "fire") hasFire = true;
        if (id == "water") hasWater = true;
    }

    EXPECT_FALSE(hasFire);
    EXPECT_TRUE(hasWater);
}

// Test surface serialization
TEST_F(SurfacesTest, SurfaceSerialization)
{
    ASSERT_TRUE(world.load("chapters/goblin-keep", 42));

    // Create several surfaces
    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({5, 5})},
        {"id", "fire"},
        {"size", 1},
        {"rounds", 3}
    }));

    world.act("surface", nlohmann::json::object({
        {"cell", nlohmann::json::array({6, 5})},
        {"id", "ice"},
        {"size", 2},
        {"rounds", 4}
    }));

    // Serialize and verify
    auto json = world.stateJson();
    EXPECT_NE(std::string::npos, json.find("fire"));
    EXPECT_NE(std::string::npos, json.find("ice"));

    // Create a new world and restore the state
    WorldFixture world2;
    ASSERT_TRUE(world2.loadJson("chapters/goblin-keep", {}, 42));
    ASSERT_TRUE(world2.restoreState(json, nullptr));

    // Surfaces should be restored
    EXPECT_EQ(2, world2.surfaces().size());

    // Verify the surfaces
    bool foundFire = false;
    bool foundIce = false;
    for (const auto& surf : world2.surfaces())
    {
        if (surf.id == "fire")
        {
            foundFire = true;
            EXPECT_EQ(5, surf.at.x);
            EXPECT_EQ(5, surf.at.y);
            EXPECT_EQ(3, surf.durationLeft);
        }
        if (surf.id == "ice")
        {
            foundIce = true;
            EXPECT_EQ(6, surf.at.x);
            EXPECT_EQ(5, surf.at.y);
            EXPECT_EQ(4, surf.durationLeft);
        }
    }
    EXPECT_TRUE(foundFire);
    EXPECT_TRUE(foundIce);
}
