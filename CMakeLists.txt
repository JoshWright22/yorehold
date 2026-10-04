cmake_minimum_required(VERSION 3.24)
project(Yorehold LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY MultiThreadedDLL)
include(CTest)
# All executables land in build/bin/<config>, so tools and the dev script know where to look.
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

option(YOREHOLD_TESTS "Build the client's visual test browser" ON)

# The framework lives next to this repo.
set(YOREHOLD_FRAMEWORK_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../yorehold-framework" CACHE PATH "Path to yorehold-framework")
add_subdirectory(${YOREHOLD_FRAMEWORK_DIR} yorehold-framework)

# Everything except main.cpp, so the game and its tests share one copy.
add_library(yorehold-game STATIC
    src/content/Chapter.cpp
    src/content/ContentPackage.cpp
    src/content/GameMap.cpp
    src/hud/ActionBar.cpp
    src/hud/DialoguePanel.cpp
    src/hud/InitiativeStrip.cpp
    src/hud/JournalPanel.cpp
    src/hud/PartyCards.cpp
    src/online/Online.cpp
    src/screens/Menus.cpp
    src/screens/PlayScreen.cpp
    src/sim/Save.cpp
    src/sim/World.cpp
    src/sim/WorldActions.cpp
    src/sim/WorldAi.cpp
    src/sim/WorldCombat.cpp
    src/sim/WorldExplore.cpp
    src/sim/WorldNet.cpp
    src/sim/WorldStealth.cpp
    src/Coop.cpp
    src/FileAssociation.cpp
    src/Settings.cpp
    src/YoreholdGame.cpp
)
target_include_directories(yorehold-game PUBLIC src)
target_link_libraries(yorehold-game PUBLIC yorehold::framework)
# Authored content mounted over the framework's assets.
target_compile_definitions(yorehold-game PUBLIC YH_GAME_ASSETS="${CMAKE_CURRENT_SOURCE_DIR}/assets")
yorehold_warnings(yorehold-game)

if(ANDROID)
    add_library(yorehold SHARED src/main.cpp)
else()
    add_executable(yorehold src/main.cpp)
endif()
target_link_libraries(yorehold PRIVATE yorehold-game)
yorehold_warnings(yorehold)
yorehold_copy_gpu_runtime(yorehold)
yorehold_platform_app(yorehold)

if(NOT ANDROID AND NOT EMSCRIPTEN AND NOT CMAKE_SYSTEM_NAME STREQUAL "iOS")
    add_executable(yorehold-content src/content_main.cpp)
    target_link_libraries(yorehold-content PRIVATE yorehold-game)
    yorehold_warnings(yorehold-content)
    yorehold_copy_gpu_runtime(yorehold-content)
endif()

if(YOREHOLD_TESTS)
    if(ANDROID)
        add_library(yorehold-tests SHARED tests/visual/main.cpp)
    else()
        add_executable(yorehold-tests tests/visual/main.cpp)
    endif()
    target_link_libraries(yorehold-tests PRIVATE yorehold-game)
    yorehold_warnings(yorehold-tests)
    yorehold_copy_gpu_runtime(yorehold-tests)
    yorehold_platform_app(yorehold-tests)
endif()

if(BUILD_TESTING AND NOT ANDROID AND NOT EMSCRIPTEN AND NOT CMAKE_SYSTEM_NAME STREQUAL "iOS")
    add_executable(yorehold-unit-tests tests/unit/ContentTests.cpp tests/unit/WorldFixture.cpp tests/unit/WorldTests.cpp)
    target_link_libraries(yorehold-unit-tests PRIVATE yorehold-game)
    yorehold_warnings(yorehold-unit-tests)
    yorehold_copy_gpu_runtime(yorehold-unit-tests)
    add_test(NAME client-unit COMMAND yorehold-unit-tests)
endif()

if(MSVC)
    set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT yorehold)
endif()
