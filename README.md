# Yorehold

A free, community-written tabletop RPG. Play chapters with friends, write your own, and get them voted into the shared canon.

This is the game client: desktop, mobile and browser. It's built on [yorehold-framework](https://github.com/JoshWright22/yorehold-framework).

## Status

Very early. Right now the client opens a window and draws an empty scene. Nothing is playable yet.

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
- [CMake](https://cmake.org/download/) 3.24+
- A C++20 compiler. Visual Studio 2022 with "Desktop development with C++" is the easiest on Windows.
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
cmake -S . -B build
cmake --build build
```

The first configure downloads SDL3, so it takes a minute. On Windows, open `build/Yorehold.sln` in Visual Studio; `yorehold` is already set as the startup project.

## Contributing

Open an issue before starting anything big so effort isn't wasted, and keep pull requests small and focused. Framework-level work (rendering, input, particles, skins) goes in yorehold-framework, not here.

## Licence

Not picked yet. Until it is, the code is all rights reserved.
