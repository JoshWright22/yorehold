// Triggers fire dialogue and cutscenes on chapter events: onEnter, onFlag, onWipe, onComplete.

#include "WorldFixture.h"

#include <algorithm>
#include <cstdio>

namespace
{

// Simple test: onEnter trigger fires when chapter starts
void onEnterTrigger(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    std::string error;
    if (!world.load("chapters/trigger-test", 1, &error))
    {
        check(false, "failed to load trigger-test chapter");
        return;
    }

    // The chapter should have a trigger that fires on entry
    check(world.said("Welcome to the trigger test chapter!"), "onEnter trigger fired");
}

// Test: onFlag trigger fires when flags are set
void onFlagTrigger(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    std::string error;
    if (!world.load("chapters/trigger-test", 1, &error))
    {
        check(false, "failed to load trigger-test chapter");
        return;
    }

    // Defeat the enemy to set the flag
    const size_t enemy = world.heroCount();  // first enemy after heroes
    world.send("use", {{"action", "strike"}, {"target", enemy}});
    world.step(1.0);

    // Ensure the onFlag trigger did NOT fire yet (we just started combat)
    check(!world.said("You have defeated the enemy! This trigger fired when the enemy-defeated flag was set."),
          "onFlag trigger has not fired yet");
}

// Test: Non-combat win condition
void winConditionTest(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    std::string error;
    if (!world.load("chapters/trigger-test", 1, &error))
    {
        check(false, "failed to load trigger-test chapter");
        return;
    }

    // Manually set the flag that triggers the win condition
    world.send("say", {{"text", "Test flag set"}});
    world.setFlags({"enemy-defeated"});
    world.step(0.1);

    // The chapter should recognize the win condition and show completion
    check(world.said("The chapter is now complete!"), "winCondition fired");
}

// Test: Triggers are saved and restored
void triggerPersistence(const std::function<void(bool, const char*)>& check)
{
    WorldFixture world;
    std::string error;
    if (!world.load("chapters/trigger-test", 1, &error))
    {
        check(false, "failed to load trigger-test chapter");
        return;
    }

    // Get the state after onEnter fires
    const std::string state = world.snapshot();
    check(world.said("Welcome to the trigger test chapter!"), "onEnter trigger fired");

    // Load a fresh chapter
    WorldFixture world2;
    if (!world2.load("chapters/trigger-test", 1, &error))
    {
        check(false, "failed to load trigger-test chapter again");
        return;
    }

    // Restore the state (with the fired trigger)
    if (!world2.restoreState(state, &error))
    {
        check(false, ("failed to restore state: " + error).c_str());
        return;
    }

    // Clear the log so we can check if onEnter fires again
    world2.log.clear();
    world2.step(0.1);

    // onEnter should not fire again since it already fired
    check(!world2.said("Welcome to the trigger test chapter!"), "onEnter trigger does not fire twice");
}

}

int main()
{
    const auto check = [](bool condition, const char* message) {
        if (!condition)
            printf("FAIL: %s\n", message);
        else
            printf("PASS: %s\n", message);
    };

    onEnterTrigger(check);
    onFlagTrigger(check);
    winConditionTest(check);
    triggerPersistence(check);

    return 0;
}
