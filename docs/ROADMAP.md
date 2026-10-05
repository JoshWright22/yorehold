# Port roadmap

Yorehold moves from the C++ client (`../yorehold`, `../yorehold-framework`) to Godot 4.6 with C#.
The C++ repos are frozen and serve as the reference: same rules, same content files, same design
docs (`../yorehold/docs/GAME_DESIGN.md`, `SYSTEM_DESIGN.md`, `CONTENT.md`). The web game is dropped:
the game is native only (Windows, then Android and iOS); the site keeps downloads, forms and the
compendium.

Layout: `rules/` is plain C# with no Godot types (content loading, rules, World, AI, saves) and is
tested with xunit in `tests/`. The Godot project in the root only draws, takes input and shows UI.
Content stays as the JSON files in `assets/`; old files keep loading.

Order: get a playable demo early (walk a map, fight, win), then widen. Tick a box when its step
builds, passes `check.ps1` and is committed.

## P. Port

- [x] **P0. Tooling.** `check.ps1 -Shot name.png [-Frames N] [-Script file]`: run the game in a
  window placed off screen, play an input script, save a screenshot to `../.dev/` and quit. A
  content check in the test run that loads every shipped file under `assets/` and fails on errors.
  `docs/STRUCTURE.md` for the C# style. Copy of `yorehold/assets` already in `assets/`.
  - Default: the shot window sits at -4000,-4000 (not minimised, a minimised window may not draw) and runs at a fixed 60 fps.
  - Default: `shot` paths inside a script are taken from the workspace folder, so `.dev/x.png` works like before.
  - Default: the content check only parses JSON for now, strict (no comments or trailing commas). Typed loading is P1.
  - Default: `Main` draws the version and the last input as a placeholder until P4.
- [ ] **P1. Content model.** Ruleset folder, conditions, effects, actions, races, backgrounds,
  feats, classes, creatures, items, spells, kits, loot tables, adventure and chapter files, read
  into C# types with System.Text.Json. Same formats as CONTENT.md; clear messages on bad files.
- [ ] **P2. Core rules.** Seeded RNG, dice, checks and DCs, proficiency ranks, modifiers,
  conditions with durations, the effect step list, grid helpers (distance, reach, flanking,
  cover, line of sight). Port the C++ unit checks for these.
- [ ] **P3. World and exploring.** World state from a chapter: map tiles, objects (doors, levers,
  locks, chests), regions, creatures and party; free movement with paths on the grid; vision and
  stealth (sneak, cones, checks); triggers (`onEnter`, `onFlag`). World test helpers like A3.
- [ ] **P4. Play screen, first look.** Draw the chapter map (floors, walls, objects, lights),
  party and creatures, camera pan and zoom, click to move, fog from party vision. Screenshot run.
- [ ] **P5. Combat rules.** Initiative, shared turns, free move plus two actions, all generic
  actions (Strike, Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready), reactions and
  opportunity attacks, downed and death, the AI scorer (including flee and surrender).
- [ ] **P6. Combat on screen.** Action bar, initiative strip, party cards, targeting with ranges
  and areas, hit and damage text, the log. A scripted fight in a screenshot run. First demo.
- [ ] **P7. Characters.** Character files hold choices, the sheet is computed; class tables 1 to
  20; character library with graveyard; create and level-up screens; starting an adventure.
- [ ] **P8. Items.** Inventory and hands, loot and containers, weight and the magic item limit,
  merchants, consumables; their panels.
- [ ] **P9. Magic.** Spell files and casting, slots, prepared and spontaneous casters, focus
  points, starter lists, surfaces; the spell panel; AI uses abilities.
- [ ] **P10. Adventure.** `adventure.json`, chapter transitions, camp and long rests, companions
  and approval, dialogue and its panel, journal, cutscenes, saves (seeded, deterministic).
- [ ] **P11. Menus and settings.** Title, load, settings, key bindings, credits with the Godot
  MIT licence text.
- [ ] **P12. Create: shell, map and encounters modes.**
- [ ] **P13. Create: dialogue, compendium, cutscene, story and voice modes.**
- [ ] **P14. Online.** Account sign-in and character/save sync against the existing Nakama server
  (`../yorehold-server`), through an interface tests can fake.
- [ ] **P15. Exports.** Windows build, then Android; an iOS export test early since C# on iOS is
  still experimental in Godot 4.
