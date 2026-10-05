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
- [x] **P1. Content model.** Ruleset folder, conditions, effects, actions, races, backgrounds,
  feats, classes, creatures, items, spells, kits, loot tables, adventure and chapter files, read
  into C# types with System.Text.Json. Same formats as CONTENT.md; clear messages on bad files.
  - Default: files are read by hand from `JsonElement` (no attribute mapping), so every message can name the file and the field path.
  - Default: `docs/CONTENT.md` is a copy of the C++ client's with a "In the Godot port" section on top; FRAMEWORK.md stays in the framework repo.
  - Default: the framework's `modern` and `classic` rulesets are not ported (numbers stay in `assets/`); a chapter naming one is refused, and content needs `rulesets/yorehold` under it.
  - Default: the four built-in AI presets and the three basic actions are in code like the framework had them; the shipped `ai/` and `actions/` files replace them.
  - Default: surface files keep the extra fields the shipped ones carry (damage, slipping, what puts them out) though no rule reads them yet.
  - Default: content is read from folders only. `.yore` archives, the save signature, the merchant `inventory` form and a `tileMap` `delta` wait for the steps that need them (P10).
  - Default: map lights and markers stay in cells and object areas in world units (64 a cell), as the files write them. Walls and indoor areas are built in P2/P3.
  - Default: `DiceText` only checks that dice text can be rolled. Rolling is P2.
- [x] **P2. Core rules.** Seeded RNG, dice, checks and DCs, proficiency ranks, modifiers,
  conditions with durations, the effect step list, grid helpers (distance, reach, flanking,
  cover, line of sight). Port the C++ unit checks for these.
  - Default: `Rng` is the C++ client's PCG32, so a seed gives the same rolls in both. A save keeps `State` and `Increment`; `Rng.Restore` carries on from them.
  - Default: `Checks.DegreeOf` gives four degrees, a natural 1 or 20 deciding the critical ones. Attacks in effects use it. Checks and saves inside effects still compare the total to the DC, as the C++ client does.
  - Default: the sheet is `CharacterSheet` with what these rules read: stats, HP, conditions, proficiencies, resources and a plain `Weapon`. Inventory and encumbrance (P8), death saves (P5), rests and the sheet's save format (P10) come with their steps; until then damage and healing only move HP.
  - Default: stats and grid distances are summed in float like the C++ client so they round the same. World positions are `System.Numerics.Vector2`.
  - Default: `Positioning` has flanking and cover, with creatures as half cover, and `PositioningRules.CoverArmorClass` the bonus. Who counts as a foe and the flanked armour class are the fight's to work out (P5).
  - Not ported yet: the C++ check that an effect's attack rolls the same dice as the encounter's own attack. It needs the encounter (P5).
- [x] **P3. World and exploring.** World state from a chapter: map tiles, objects (doors, levers,
  locks, chests), regions, creatures and party; free movement with paths on the grid; vision and
  stealth (sneak, cones, checks); triggers (`onEnter`, `onFlag`). World test helpers like A3.
  - Default: `World` takes typed calls (`Go`, `Interact`, `Sneak`, `SetFlags`) that return false with a `Refusal`. JSON intents and the co-op checks come with saves and online (P10, P14).
  - Default: heroes are a plain build from their class file (tens across, class proficiencies, HP from the hit die) until P7 builds them from choices, so dice rolled after the party is made don't match the C++ client yet. Creatures take their stat block without rolled abilities.
  - Default: creatures carry item ids for keys, and chests keep their contents on the object, until inventories, piles and looting come in P8. Weight doesn't slow walking until then either.
  - Default: an enemy noticing the party wakes its encounter, stops everyone, ends sneaking and sets `FightGroup`; P5 starts the fight from there.
  - Default: the win condition fires once when all its flags are set. The C++ check never fired, since it waited for the chapter not to be cleared while those same flags clear it.
  - Default: trigger and win dialogue and cutscenes go out as `Talk` and `Cutscene` events with the content path; P10 opens them.
  - Default: a map is one region. The framework's streaming of several regions isn't ported; no chapter uses it.
  - Default: paths use a heap that breaks ties like the C++ client's `std::priority_queue`, so routes match cell for cell.
  - Default: a sprung trap logs saves, damage, healing and conditions only; the fight's full log comes with P5.
- [x] **P4. Play screen, first look.** Draw the chapter map (floors, walls, objects, lights),
  party and creatures, camera pan and zoom, click to move, fog from party vision. Screenshot run.
  - Default: the play screen opens `chapters/chapter-one`, the adventure's first chapter; `--chapter` picks another until the menus (P11).
  - Default: lights go into a light map (a SubViewport with Light2D lamps and wall occluders) multiplied over the world like the C++ pass, so light stops at full and daylight doesn't blow out.
  - Default: clicking a door, lever, locked chest or found trap walks the leader beside it (`World.GoNear`) and uses it on arrival. An unlocked chest is just walked onto until looting (P8).
  - Default: a left drag or one finger pans, so does a middle or right drag; a press that moves under 10 px is a click. Two fingers pinch to zoom. Zoom goes from the whole map on screen to 4x.
  - Default: cutscenes end as soon as they start and conversations are only printed until P10. A noticed party stops and waits for fights (P5).
  - Default: tokens are discs with an initial and tiles are the C++ placeholder painters; token images and painted layer pictures are not drawn yet.
  - Default: content is read from `res://assets` as a plain folder, which works from the project on desktop. Exports need another way in (P15).
- [ ] **P5. Combat rules.** Initiative, shared turns, free move plus two actions, all generic
  actions (Strike, Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready), reactions and
  opportunity attacks, downed and death, the AI scorer (including flee and surrender).
- [ ] **P6. Combat on screen.** Action bar, initiative strip, party cards, targeting with ranges
  and areas, hit and damage text, the log. A scripted fight in a screenshot run. First demo.
  - Layout follows Baldur's Gate 3: the hotbar bottom centre with action, bonus and movement
    pips and End Turn beside it, party portraits with HP down the left, the initiative strip of
    portraits top centre, the combat log bottom right, hit chance at the cursor when targeting,
    tooltips on every action. Our own art and names, the same arrangement and feel.
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
