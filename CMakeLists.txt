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

# Voice lines: recordings are decoded with miniaudio and turned into words with timings by
# whisper.cpp, only in Create. The browser and phone builds leave whisper out.
include(FetchContent)
FetchContent_Declare(
    miniaudio
    URL https://github.com/mackron/miniaudio/archive/refs/tags/0.11.25.tar.gz
    URL_HASH SHA256=b900edcffe979816e2560a0580b9b1216d674b4f17fbadeca8f777a7f8ab0274
    SOURCE_SUBDIR none # only its two files are used, not its own CMake project
)
FetchContent_MakeAvailable(miniaudio)

if(NOT ANDROID AND NOT EMSCRIPTEN AND NOT CMAKE_SYSTEM_NAME STREQUAL "iOS")
    set(YOREHOLD_VOICE_DEFAULT ON)
else()
    set(YOREHOLD_VOICE_DEFAULT OFF)
endif()
option(YOREHOLD_VOICE "Build whisper.cpp in, for voice lines in Create" ${YOREHOLD_VOICE_DEFAULT})
set(YOREHOLD_VOICE_MODEL "base.en" CACHE STRING "Speech model shipped beside the exe: base.en or tiny.en")
set_property(CACHE YOREHOLD_VOICE_MODEL PROPERTY STRINGS base.en tiny.en)

if(YOREHOLD_VOICE)
    set(WHISPER_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(WHISPER_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(WHISPER_BUILD_SERVER OFF CACHE BOOL "" FORCE)
    set(WHISPER_ALL_WARNINGS OFF CACHE BOOL "" FORCE)
    set(GGML_OPENMP OFF CACHE BOOL "" FORCE)
    # Static, so nothing new has to be copied beside the exe. whisper.cpp asks for an old CMake,
    # whose option() would ignore this and make ggml a DLL.
    set(CMAKE_POLICY_DEFAULT_CMP0077 NEW)
    set(BUILD_SHARED_LIBS OFF)
    FetchContent_Declare(
        whisper
        URL https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v1.9.4.tar.gz
        URL_HASH SHA256=57e280cee375ab02425b806ad5146b99f6eb9357e3c2b31357c8a6af2e2e44ae
        EXCLUDE_FROM_ALL
    )
    FetchContent_MakeAvailable(whisper)
    unset(BUILD_SHARED_LIBS)
    unset(CMAKE_POLICY_DEFAULT_CMP0077)

    # The model is fetched once into the build folder and checked, never kept in git.
    if(YOREHOLD_VOICE_MODEL STREQUAL "tiny.en")
        set(YOREHOLD_VOICE_MODEL_HASH c77c5766f1cef09b6b7d47f21b546cbddd4157886b3b5d6d4f709e91e66c7c2b)
    else()
        set(YOREHOLD_VOICE_MODEL base.en)
        set(YOREHOLD_VOICE_MODEL_HASH 4baf70dd0d7c4247ba2b81fafd9c01005ac77c2f9ef064e00dcf195d0e2fdd2f)
    endif()
    set(YOREHOLD_VOICE_MODEL_FILE "${CMAKE_BINARY_DIR}/models/ggml-${YOREHOLD_VOICE_MODEL}-q5_1.bin")
    if(NOT EXISTS "${YOREHOLD_VOICE_MODEL_FILE}")
        message(STATUS "Downloading the ${YOREHOLD_VOICE_MODEL} speech model")
        file(DOWNLOAD "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-${YOREHOLD_VOICE_MODEL}-q5_1.bin"
            "${YOREHOLD_VOICE_MODEL_FILE}.part" EXPECTED_HASH SHA256=${YOREHOLD_VOICE_MODEL_HASH} STATUS YOREHOLD_MODEL_STATUS)
        list(GET YOREHOLD_MODEL_STATUS 0 YOREHOLD_MODEL_CODE)
        if(YOREHOLD_MODEL_CODE EQUAL 0)
            file(RENAME "${YOREHOLD_VOICE_MODEL_FILE}.part" "${YOREHOLD_VOICE_MODEL_FILE}")
        else()
            file(REMOVE "${YOREHOLD_VOICE_MODEL_FILE}.part")
            message(WARNING "The speech model couldn't be downloaded (${YOREHOLD_MODEL_STATUS}); voice import will say it is missing")
        endif()
    endif()
endif()

# Everything except main.cpp, so the game and its tests share one copy.
add_library(yorehold-game STATIC
    src/content/Adventure.cpp
    src/content/Chapter.cpp
    src/content/CharacterDraft.cpp
    src/content/CharacterLibrary.cpp
    src/content/ContentPackage.cpp
    src/content/GameMap.cpp
    src/hud/ActionBar.cpp
    src/hud/DialoguePanel.cpp
    src/hud/InitiativeStrip.cpp
    src/hud/InventoryPanel.cpp
    src/hud/MerchantPanel.cpp
    src/hud/JournalPanel.cpp
    src/hud/PartyCards.cpp
    src/hud/SpellPanel.cpp
    src/online/Online.cpp
    src/screens/CharacterScreens.cpp
    src/screens/CompendiumEditor.cpp
    src/screens/CreateScreen.cpp
    src/screens/CutsceneEditor.cpp
    src/screens/DialogueEditor.cpp
    src/screens/EncountersEditor.cpp
    src/screens/MapEditor.cpp
    src/screens/Menus.cpp
    src/screens/PlayScreen.cpp
    src/screens/StoryEditor.cpp
    src/screens/VoiceImporter.cpp
    src/voice/AudioDecode.cpp
    src/voice/VoiceLine.cpp
    src/voice/VoiceTranscriber.cpp
    src/sim/Save.cpp
    src/sim/World.cpp
    src/sim/WorldActions.cpp
    src/sim/WorldAi.cpp
    src/sim/WorldCombat.cpp
    src/sim/WorldExplore.cpp
    src/sim/WorldLoot.cpp
    src/sim/WorldObjects.cpp
    src/sim/WorldTravel.cpp
    src/sim/WorldCamp.cpp
    src/sim/WorldCompanions.cpp
    src/sim/WorldTrade.cpp
    src/sim/WorldItems.cpp
    src/sim/WorldSpells.cpp
    src/sim/WorldNet.cpp
    src/sim/WorldReactions.cpp
    src/sim/WorldPositioning.cpp
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
target_include_directories(yorehold-game PRIVATE "${miniaudio_SOURCE_DIR}")
# miniaudio is someone else's code: built as it is, without our warning level.
set_source_files_properties(src/voice/AudioDecode.cpp PROPERTIES COMPILE_OPTIONS "$<$<CXX_COMPILER_ID:MSVC>:/W0>")
if(YOREHOLD_VOICE)
    target_link_libraries(yorehold-game PRIVATE whisper)
    target_compile_definitions(yorehold-game PRIVATE YH_VOICE_WHISPER=1 YH_VOICE_MODEL="${YOREHOLD_VOICE_MODEL}")
endif()

# Puts the speech model beside an exe, so nothing is fetched while it runs.
function(yorehold_copy_voice_model target)
    if(YOREHOLD_VOICE AND EXISTS "${YOREHOLD_VOICE_MODEL_FILE}")
        add_custom_command(TARGET ${target} POST_BUILD
            COMMAND ${CMAKE_COMMAND} -E copy_if_different "${YOREHOLD_VOICE_MODEL_FILE}" "$<TARGET_FILE_DIR:${target}>")
    endif()
endfunction()

if(ANDROID)
    add_library(yorehold SHARED src/main.cpp)
else()
    add_executable(yorehold src/main.cpp)
endif()
target_link_libraries(yorehold PRIVATE yorehold-game)
yorehold_warnings(yorehold)
yorehold_copy_gpu_runtime(yorehold)
yorehold_copy_voice_model(yorehold)
yorehold_platform_app(yorehold)

if(NOT ANDROID AND NOT EMSCRIPTEN AND NOT CMAKE_SYSTEM_NAME STREQUAL "iOS")
    add_executable(yorehold-content src/content_main.cpp)
    target_link_libraries(yorehold-content PRIVATE yorehold-game)
    yorehold_warnings(yorehold-content)
    yorehold_copy_gpu_runtime(yorehold-content)

    # Voice lines in batches: a package's voice/ folder, or the tiny.en and base.en comparison.
    add_executable(yorehold-voice src/voice_main.cpp)
    target_link_libraries(yorehold-voice PRIVATE yorehold-game)
    yorehold_warnings(yorehold-voice)
    yorehold_copy_gpu_runtime(yorehold-voice)
    yorehold_copy_voice_model(yorehold-voice)
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
    add_executable(yorehold-unit-tests tests/unit/ContentTests.cpp tests/unit/WorldFixture.cpp tests/unit/WorldTests.cpp tests/unit/WorldActionTests.cpp tests/unit/WorldTurnTests.cpp tests/unit/WorldPositioningTests.cpp tests/unit/WorldProficiencyTests.cpp tests/unit/WorldDeathTests.cpp tests/unit/WorldCharacterTests.cpp)
    target_link_libraries(yorehold-unit-tests PRIVATE yorehold-game)
    target_sources(yorehold-unit-tests PRIVATE tests/unit/WorldItemTests.cpp tests/unit/WorldSpellTests.cpp tests/unit/WorldObjectTests.cpp tests/unit/WorldTravelTests.cpp tests/unit/WorldCampTests.cpp tests/unit/WorldCompanionTests.cpp tests/unit/MapEditorTests.cpp tests/unit/EncountersEditorTests.cpp tests/unit/DialogueEditorTests.cpp tests/unit/CompendiumEditorTests.cpp tests/unit/CutsceneEditorTests.cpp tests/unit/StoryEditorTests.cpp tests/unit/VoiceTests.cpp)
    yorehold_warnings(yorehold-unit-tests)
    yorehold_copy_gpu_runtime(yorehold-unit-tests)
    add_test(NAME client-unit COMMAND yorehold-unit-tests)
endif()

if(MSVC)
    set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT yorehold)
endif()
