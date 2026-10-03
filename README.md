# Yorehold

A free, community-written tabletop RPG. Play chapters with friends, write your own, and get them voted into the shared canon.

This is the game client: desktop, mobile and browser. It's built on [yorehold-framework](https://github.com/JoshWright22/yorehold-framework).

## Status

Very early. The first playable slice is in: a party of four explores a goblin-held keep at night (fog of war, torches and lanterns with shadows), and spotting enemies starts a turn-based fight on the grid. There's a title screen (Play, Create, Settings, Exit), an Esc pause menu, rests from the ruleset between fights, a chapter-authored end cutscene, and an autosave you can Continue from. Settings and the save live in SDL's per-user folder (`%APPDATA%/Yorehold/Yorehold` on Windows). Classes, items, creatures, maps, party and enemy placements, chapter text, endings and UI theme now come from JSON files under `assets/`.

Create will cover game design, UI design, maps, tokens and encounters, cutscenes and dialogue. Game and UI design come before building those editing tools. The visual editor is still planned; the file foundation and transfer tools work now. Decisions and scope: [Create design](docs/CREATE_DESIGN.md). Formats, validation and `.yore` export/import: [content files](docs/CONTENT.md).

Testing whole runs: F9 turns on auto-play (the party explores and fights by itself and prints its log to stdout). `YOREHOLD_SEED=<n>` replays the same adventure, skips the title and never touches your save; `YOREHOLD_SAVE_DIR=<dir>` moves the save somewhere else. `YOREHOLD_CONTENT=<folder-or.yore>` loads transferred content, and `YOREHOLD_CHAPTER=<virtual-folder>` selects a chapter in it. Broken content stays on the title screen with an error. Saves record the chapter and its content identity so they cannot silently resume a different or edited adventure.

`yorehold-tests` opens the client's visual test browser. In any build, F3 shows frame times and F12 saves a screenshot with a note to `../feedback/`. Layout and test commands: [yorehold-framework/docs/STRUCTURE.md](https://github.com/JoshWright22/yorehold-framework/blob/main/docs/STRUCTURE.md).

What it's going to be:

- Top-down 2D maps like a virtual tabletop (grid, tokens, fog of war, lighting), with real-time exploring, turn-based combat, dialogue with dice checks.
- Single player or co-op (party of 4), plus a DM mode where one player runs the enemies and voices the NPCs.
- Chapters written by players. Good ones get nominated and reviewed into the shared canon.
- Almost everything can be customized with data-only skins: UI, dice, particles, themes. No code mods.

## Repos

| Repo | What it is |
|---|---|
| [yorehold](https://github.com/JoshWright22/yorehold) | Game client (this repo) |
| [yorehold-framework](https://github.com/JoshWright22/yorehold-framework) | C++ 2D framework the client runs on |
| [yorehold-server](https://github.com/JoshWright22/yorehold-server) | Online backend (Nakama) |
| [yorehold-web](https://github.com/JoshWright22/yorehold-web) | Website: accounts, chapter library, canon voting |

## Developing Yorehold

### Prerequisites

- Windows 10+ for now (Linux, macOS, Android and web builds come later)
- Visual Studio 2026 (or its Build Tools) with "Desktop development with C++". The prebuilt Dawn (WebGPU) library needs its compiler; VS 2022 won't link.
- CMake 4.2+ (the copy bundled with VS 2026 works)
- Git

### Getting the code

The client expects the framework cloned next to it:

```shell
mkdir Yorehold && cd Yorehold
git clone https://github.com/JoshWright22/yorehold-framework
git clone https://github.com/JoshWright22/yorehold
cd yorehold
```

If your framework lives somewhere else, pass `-DYOREHOLD_FRAMEWORK_DIR=<path>` to CMake.

### Building

```shell
cmake -S . -B out -G "Visual Studio 18 2026" -A x64
cmake --build out --config RelWithDebInfo
```

The first configure downloads SDL3 and a ~120 MB prebuilt Dawn into `../.deps` (shared by all build folders). Executables land in `out/bin/RelWithDebInfo`. You can also open `out/Yorehold.slnx` in Visual Studio; `yorehold` is the startup project.

Easiest: run `dev.ps1` from the folder above the repos. It builds, opens the app, and rebuilds + reopens it whenever a source file is saved.

## Contributing

Open an issue before starting anything big so effort isn't wasted, and keep pull requests small and focused. Framework-level work (rendering, input, particles, skins) goes in yorehold-framework, not here.

## Licence

Not picked yet. Until it is, the code is all rights reserved.
