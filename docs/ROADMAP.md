# Port roadmap

Yorehold moves from the C++ client (`../yorehold`, `../yorehold-framework`) to Godot 4.6 with C#.
The C++ repos are frozen and serve as the reference: same rules, same content files, same design
docs (`../yorehold/docs/GAME_DESIGN.md`, `SYSTEM_DESIGN.md`, `CONTENT.md`). The web game is dropped:
the game is native only (Windows, then Android and iOS); the site keeps downloads, forms and the
compendium.

Layout: `rules/` is plain C# with no Godot types (content loading, rules, World, AI, saves) and is
tested with xunit in `tests/`. The Godot project in the root only draws, takes input and shows UI.
Content stays as the JSON files in `assets/`; old files keep loading.

Look: panels use the Apollo palette (CC-29 until 10/7) and nothing else (`src/hud/Palette.cs`, `scenes/hud/hud-theme.tres`)
and read like the website: ink ground, thin iron lines, bone text, straw as the one accent, no serif.
The fight screen keeps its hotbar and portraits. Data screens (sheet, gear, spells, library) are dense
like a database: a tab bar of types, a search box, filter chips, tight rows sorted by any column, the
list on the left and the full entry on the right.

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
  - Default: one finger pans, so does a middle or right drag (a left drag no longer does, see P6); a press that moves under 10 px is a click. Two fingers pinch to zoom. Zoom goes from the whole map on screen to 4x.
  - Default: cutscenes end as soon as they start and conversations are only printed until P10. A noticed party stops and waits for fights (P5).
  - Default: tokens are discs with an initial and tiles are the C++ placeholder painters; token images and painted layer pictures are not drawn yet.
  - Default: content is read from `res://assets` as a plain folder, which works from the project on desktop. Exports need another way in (P15).
- [x] **P5. Combat rules.** Initiative, shared turns, free move plus two actions, all generic
  actions (Strike, Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready), reactions and
  opportunity attacks, downed and death, the AI scorer (including flee and surrender).
  - Default: fights take typed calls like exploring does (`Use`, `MoveTo`, `Attack`, `ChooseTurn`, `EndTurn`, `React`, `Ambush`, `StartFight`) and answer `CurrentCreature`, `ActionsLeft`, `MovementLeft`, `UsableActions`, `ReachableCells`, `ValidTargets` and `HitChance` for the screen.
  - Default: noticing the party starts the fight at once, so `FightGroup` is only set while one is on. Until P6 draws it the play screen stops at the first hero turn.
  - Default: the AI plays its turns inside `Update` with the C++ pauses (think, walk, strike, wait). `Options.AutoPlay` hands it the heroes too, which is how the tests play every shipped encounter out.
  - Default: heroes and creatures fight with the first weapon among their class or creature items until inventories (P8).
  - Default: an action aimed at a square is refused until spells (P9). The `potion` action only shows for a sheet that carries the `potions` resource.
  - Default: a wiped party sets `PartyWiped` and plays the `onWipe` cutscene; going back to the checkpoint and its destination come with saves (P10). Picking a fight with an NPC or someone who surrendered waits for dialogue (P10).
  - Not ported: the C++ keep run with the scripted party, since it rests between fights (P10). The yard fight from sight to victory and the AI play-through of every encounter cover it for now.
- [x] **P6. Combat on screen.** Action bar, initiative strip, party cards, targeting with ranges
  and areas, hit and damage text, the log. A scripted fight in a screenshot run. First demo.
  - Layout follows Baldur's Gate 3: the hotbar bottom centre with action, bonus and movement
	pips and End Turn beside it, party portraits with HP down the left, the initiative strip of
	portraits top centre, the combat log bottom right, hit chance at the cursor when targeting,
	tooltips on every action. Our own art and names, the same arrangement and feel.
  - Default: the panels are styled by `scenes/hud/hud-theme.tres` (flat dark boxes, a 1 px warm trim). The pixel art in `assets/ui` is the C++ client's purple theme and isn't used here.
  - Default: action icons are plain shapes picked by `assets/ui/action-icons.json`, conditions are two-letter badges. Art replaces them later.
  - Default: portraits and tokens show a face from the content's `portraits/` (by creature id, or a hero's class), see CONTENT.md; a hero has no picture of their own yet, only their class's.
  - Default (Josh, 10/6): the game makes no art of its own. Faces, tiles and painted maps are pictures from the content, which players and creators supply (`PlayerArt`); without one a token is a disc with an initial and a tile a plain palette fill. The generated stand-in faces, the downloaded ones and the tile painters are gone. Objects (doors, chests, levers) and action icons are still plain shapes drawn in code, waiting on a picture field of their own.
  - Default: the hotbar stays along the bottom between fights with the selected hero's portrait, its slots greyed ("Used in a fight") and no End Turn; a conversation or a cutscene takes it away. The menu is a column down the right edge so the turn order has the top of the screen.
  - Default (Josh, 10/6): the sheet is laid out like a tabletop character sheet rather than a data table, the most important numbers biggest. The spell book is icon tiles by level (and the hero's other actions) like BG3 or WoW; tiles drag onto two bars of twelve slots, slots drag to swap and off onto the map to clear. The arrangement is on the sheet and saved with it. Icons are the creator's `icons/<id>.png` or plain shapes.
  - Default (Josh, 10/6): conversations look like Hades: dimmed map, the speaker's picture large on the left, the hero's dimmed on the right, a name plate and a wide text box with the replies at the bottom. Without pictures both are big discs with an initial. One picture per creature serves cards, tokens and talk for now; a separate talk picture (or expressions per line) would be a later field.
  - Default (Josh, 10/6): feedback comes through the playtest queue (docs/PLAYTEST.md): `playtest.cmd` opens each test set up, with a window for Good, Problem with a note, or Skip; answers go to `feedback/playtest-results.jsonl`. New tests go on the queue once `check.ps1 -Playtest <id>` passes.
  - Default: a left click never moves the camera. The mouse pans with the right or middle button, the keys or the edges; one finger still drags on a touch screen. Once panned away, the view stays put through clicks until Home, a new turn in a fight or the fight's end brings it back to the hero.
  - Default: panels are styled like the website (ink, iron lines, bone text, one straw accent, sans faces from the system: Inter Tight if installed, else Segoe UI, Arial for entry text), all still CC-29. The fonts are not shipped with the game yet.
  - Not working, and it wasn't before this either: `tests/visual/scripts/loot.txt` ends on `button Pack`, which isn't there once the chest is empty.
  - Default: the ruleset has no bonus actions, so that pip is hidden and a reaction pip sits beside the action pips.
  - Default: a click on an enemy with nothing picked strikes it, walking up first. An action on oneself (Dash, Defend, Hide, Ready) is used as soon as its slot is pressed. End turn is the big button and Space, with no slot.
  - Default: reaction prompts are on (`ReactionPrompts` on the play screen) and take the reaction when the ruleset's `promptSeconds` run out. The panel sits low in the middle so it doesn't cover who is moving.
  - Default: on a touch screen the first tap on a square previews and the second acts. A greyed slot can be pressed and says why.
  - Default: the party cards and the log show between fights too. The log moved from bottom left to bottom right.
  - Default: reach, range and paths are drawn above the lighting and the fog, or they wouldn't show in a dark room. They show the map's shape under unexplored fog.
  - Default: the area of an area action is previewed from its shape (burst, cone, line, square) around the pointer. No shipped action has one until spells (P9), so it hasn't been seen on screen.
  - Default: a successful or failed save floats "Saved" or "Failed" from the creature, like "Miss".
  - Not seen in a screenshot run: the defeat panel. The goblin in chapter one runs away before it can wipe a party that only ends its turns.
- [x] **P7. Characters.** Character files hold choices, the sheet is computed; class tables 1 to
  20; character library with graveyard; create and level-up screens; starting an adventure.
  - Default: as in the C++ client, each seat's ready-made hero is rolled (4d6 drop the lowest) from the world seed, even when a library character takes the seat, so the dice after it don't change. This changed chapter one's fight: `fight.txt` is now Alice striking the goblin down, and `fight-hud.txt` lost its shared-turn step.
  - Default: the sheet carries its gear (`Item`), worn as the class and background give it; the weapon is the one held in the main hand. A weapon set by hand is the fallback for sheets without gear. The gear panel, loot and weight are P8.
  - Default: library files are the C++ client's (`yorehold.character` envelope). Until saves (P10) nothing is marked away: a brought character's copy goes back to its file when the chapter is cleared, when a new adventure starts and when the game closes, and a dead one then goes to the graveyard.
  - Default: until the menus (P11) the play screen's Characters button opens both the library and New adventure; Start plays the chapter again with a new seed (the same seed in screenshot runs). Screenshot runs keep their library in `.dev/shot-characters`.
  - Default: levels from XP in play go into the hero's latest class, as in the C++ client; the level-up screen is for library characters with XP to spare.
  - Not ported yet: reading choices back off an old sheet (`choicesFromSheet`), which only older saves need (P10).
- [x] **P8. Items.** Inventory and hands, loot and containers, weight and the magic item limit,
  merchants, consumables; their panels.
  - Default: what can be taken lies in piles: the chapter's containers and the chests on the map fill when it starts, and the dead leave theirs when a fight is won. A group's own loot table lies with the last of it to fall. Saving piles, purses and stock comes with saves (P10).
  - Default: taking, giving and trading are between fights only. Changing gear is free then and costs what Interact costs on the hero's own turn in a fight; a consumable costs its own `use` cost. Hostile consumables wait for a fight, so they can't get round initiative.
  - Default: a creature's gear is worn like a hero's, except armour on top of its stat block's AC, which already counts it.
  - Default: weight slows walking between fights by the ruleset's `encumberedSpeed`, and twice the limit stops it; in a fight the squares a hero may move say the same.
  - Default: one gear panel (I) for everything: the hero's pack, and a pile or shop beside them as other sources in its head. Clicking a chest, a sack or a merchant walks the leader beside it and opens the panel there; a locked chest is tried first.
  - Default: a shop puts what it buys back as a new stock line, as in the C++ client. What a hero takes stacks with the same unworn thing.
  - Default: the dead leave a sack in the corner of their square, since their token lies over the middle.
- [x] **P9. Magic.** Spell files and casting, slots, prepared and spontaneous casters, focus
  points, starter lists, surfaces; the spell panel; AI uses abilities.
  - Default: a spell is an action: it joins the hotbar after the ruleset's general ones. One aimed at a square is aimed on the map with the squares the rules would cover, orange where it can go and red where it can't, and a ring on everyone it would land on.
  - Default: the spell panel (K) lists the hero's whole list, prepared or not, with tabs by level and Focus and chips for Castable, Concentration, Area and Helps. Between fights it casts helping spells with a button per party member; in a fight Use now picks the spell on the hotbar.
  - Default: a surface's size is a radius in squares around its cell and a round between fights is six seconds. Starting a turn in one deals its damage; slowing and slipping aren't read yet. Surfaces are tinted squares under the tokens, fire in red since amber is the aimed area.
  - Default: creatures may list `spells`. The AI casts the one worth most when it beats a strike (worth is about HP per action, a slot costs 1.5 a level so they're kept), walks only as far as it must to get a foe in range, and looks again after any walk. The C++ AI never cast spells.
  - Default: `chapters/spell-test` (not in the adventure) has two casters, a fighter, two goblins, a goblin hexer and a flame vent that lays fire, for `spells.txt` and `spell-fight.txt` (`-Chapter chapters/spell-test`).
  - Default: rests that give slots back and open preparing again come with camp (P10), and so does saving concentration, slots and surfaces.
  - Not ported: the C++ SurfacesTest's save check (P10). Its other checks are in `WorldSpellTests.Surfaces` in this port's terms.
- [x] **P10. Adventure.** `adventure.json`, chapter transitions, camp and long rests, companions
  and approval, dialogue and its panel, journal, cutscenes, saves (seeded, deterministic).
  - Default: a chapter that `adventure.json` lists plays as part of the adventure, any other on its own. A hero stepping onto an open exit marker takes the party on; arriving on one doesn't, they have to step off and on.
  - Default: one World goes from chapter to chapter (travel, camp, a load) and sends `ChapterChanged`, so the play screen builds the map again and keeps everything else.
  - Default: the save is one file, `saves/adventure.json` in the user folder, format version 4. It keeps the seed, the roll and fight counters and the stealth dice's state, so a loaded game rolls like one that never stopped. The C++ client's versions 1 to 3 are refused with a message, so `choicesFromSheet` isn't ported.
  - Default: the game saves itself after a won fight, a rest, a door or lever, a conversation, travel and anything done at camp. F5 saves and F9 loads between fights. Nothing is saved in a fight or once the chapter is cleared.
  - Default: a save is checked against the chapter id, the map's size and the counts of creatures, objects and NPCs, all before anything changes. The content signature and `.yore` archives wait for P14.
  - Default: a wiped party gets Back to the autosave on the defeat panel. It goes to the last save, or the chapter's start, onto the chapter's wipe destination when nobody stands there.
  - Default: brought library characters are marked away while the save holds them and come home when the chapter is cleared or a new adventure starts.
  - Default: Camp (R) is a data panel with the rests anywhere and the stash, pack and revival at camp. Camp is the adventure's `camp` chapter or the shared `chapters/camp`; the chapter left behind is kept whole and Break camp puts the party back.
  - Default: clicking anyone with something to say walks the leader up and opens the conversation along the bottom, replies on 1 to 9 and Escape to walk away. A merchant's shop opens from Trade (T) there instead of straight from the click.
  - Default: a trigger's conversations queue up and open one after the other. A reply that needs a check rolls it for the leader.
  - Default: a companion who joins fights on the party's side with the player taking their turns (no party card for them yet), follows between fights and goes along to the next chapter and to camp. Approval moves with flags and replies; the journal (J) shows it beside the quests.
  - Default: cutscenes play with the panels hidden and skip on a click, Space, Enter or Escape. The fade is always to palette ink, whatever colour the file names.
  - Default: unexplored fog and the ground off the map are ink (212123) and so is the window's clear colour. The tile painters are still the C++ placeholders and not CC-29 yet.
  - Not seen in a screenshot run: the defeat panel's button and revival at camp. Both are in the tests.
  - Not done: `.uid` files for `CutsceneView`, `CampPanel`, `JournalPanel` and `DialogueTests`. The editor makes them when it next opens the project.
- [x] **P11. Menus and settings.** Title, load, settings, key bindings, credits with the Godot
  MIT licence text.
  - Default: the game starts on the title. `--screen play` or `--chapter` after `--` skips it, and screenshot runs do unless `-Screen title` is given, so the older scripts play as before.
  - Default: the title is one list (Continue, New adventure, Quick start, Load, Characters, Create, Settings, Credits, Exit) with no Play page in between. Join co-op and the list of installed adventures wait for P14.
  - Default: New adventure and Characters from the title open the character screens over the first chapter; leaving them without starting goes back to the title.
  - Default: Escape with no panel open and no action picked is the pause list (Resume, Settings, Load, Save and quit to title). The game stands still under any menu. Save and quit saves only when the rules allow a save, so in a fight the last one stands.
  - Default: the load screen lists `adventure.json` and its `.bak`, each loadable on its own. There is still one save; named saves are not in.
  - Default: settings are the C++ client's file and field names. Zoom toward the pointer and reaction prompts ship on here (off there), since the camera and the fight screen were built that way. Controls presets and skins are not ported; their fields are kept in the file.
  - Default: keys are data (`ui/keys.json`) and the settings file keeps only what the player changed. An action takes one key when rebound, and a key another action had moves over. Escape, Enter and the number keys are fixed.
  - Default: edge panning needs the pointer within 3 px of the window's edge and stops while a button is held.
  - Default: the engine's part of the credits (its MIT licence text and every library in it) is asked of the engine at run time, so it matches the build. The game's own entries are `ui/credits.json`; it names nobody yet, that is the owner's to fill in.
  - Not done: text size, colour-blind team colours, dice speed and auto-end turn from the design's settings list. The C++ client had none of them either.
- [x] **P12. Create: shell, map and encounters modes.**
  - Default: Create's rules are `rules/create` (history, map editor, encounters editor, the open package) and port the C++ MapEditor and EncountersEditor checks; the screen only lays them out, like the C++ panels.
  - Default: only the Map and Encounters tabs are shown. Dialogue, Compendium, Cutscene and Story come with P13 rather than as tabs that do nothing.
  - Default: with nothing open Create is a data panel of packages: the ones made in `create/` in the user folder, the last one opened and the game's own content (whose page warns that saving writes into the game's files). Open, New adventure, Back to title.
  - Default: New names the chapter folder and id after the package (`chapters/new-adventure`), not `chapter-one` as in the C++ client, since the package is played over the game's content and would stand in for the game's own chapter-one.
  - Default: the problems list checks the package's files over the game's content as a playtest plays them, and only the package's own `adventure.json` (`ContentPackage.Validate` takes the package's own files for that). Editor problems follow edits at once; the files on disk are checked again on open and save.
  - Default: Playtest saves first and plays the open chapter with `PlayScreen.Playtest` on (no save, no library write-back). Escape ends it and Create comes back as it was. The C++ client never had a playtest.
  - Default: Close with unsaved work needs pressing twice; Escape is Close.
  - Default: the editor has no checkboxes, sliders or drop-down menus since the theme has none: toggles, +/- buttons and < > steppers do their jobs. A light's colour is one of six CC-29 colours (Torch, Candle, White, Ember, Cold, Witchlight).
  - Default: content colours (creature tokens, lights, heroes and NPCs) are drawn in Create as the nearest CC-29 colour. Tiles are still the placeholder painters, as in play.
  - Default: the view fits the map again whenever its size changes until it is zoomed or moved by hand.
  - Not ported: the C++ `.yore` open and export buttons (folders only until P14), and dragging a light's radius as a slider (it steps by one cell and still merges into one undo step).
- [x] **P13. Create: dialogue, compendium, cutscene, story and voice modes.**
  - Default: each mode's files are a part of `CreatePackage` in a file of its own (`CreateDialogues`...), on the one history, and Save checks every part before it writes any file.
  - Default: Dialogue mode is the C++ DialogueEditor and its checks as they are. Its columns are `ToolColumn`s like the other modes; Skill check is a toggle since the theme has no tick boxes.
  - Default: Compendium mode ports the framework's form schema into `rules/create/Form.cs` and reads the same `create/compendium.json`. Its list is a `DataPanel` (a tab per kind and All, chips for changed, errors and a chapter's own) with the form where the book page goes, since it is the most database-like of the modes.
  - Default: the port's own readers check each entry, so an entry is refused here exactly when the game would refuse it. Every shipped definition opens with no errors.
  - Default: Cutscene mode is the C++ CutsceneEditor and CutsceneHooks with their checks. The preview is the editor's own map view steered to the frame's camera, with the bars, captions and titles drawn over it, and the fade in ink at the file's alpha like play.
  - Default: Story mode is the C++ StoryEditor with its suggestions and checks. What the graph can point at is read again on the way into the tab and after a save, and from the other modes' open editors, as in the C++ client.
  - Default: voice lines are the C++ voice file, token joining, matching and importer with their checks, as a Voice view in Dialogue mode. Recordings are read as WAV (PCM or float), mixed to mono and resampled by straight lines. Imports go through `IVoiceTranscriber`, so tests use a stand-in.
  - Blocked: listening to a recording. The build has no speech model (the C++ client built whisper.cpp in and fetched a 57 MB model at configure time). Picking a C# binding (Whisper.net or a wrapper of our own), where the model ships and how it exports to phones is Josh's call; until then Import is greyed and says why, and OGG recordings wait for it too.
  - Not done, as in the C++ client: a graph view and playing a recording in Dialogue mode, renaming and deleting compendium entries, dragging steps on the timeline, making a chapter from a scene's map suggestion, the `yorehold-voice` command.
- [x] **P14. Online.** Account sign-in and character/save sync against the existing Nakama server
  (`../yorehold-server`), through an interface tests can fake.
  - Default: `rules/online` is the C++ Online and AccountSync call for call: device sign-in with no password, protocol 0.1, sync passes at sign-in, after a write and every 30 seconds, newest wins with a backup. The C++ AccountSyncTests are ported whole with the same stand-in server; sign-in is tested against a stand-in for the network.
  - Default: the network sits behind `IWebTransport`, whose answers are handed over when the game polls each frame, so nothing from another thread touches the game.
  - Default: sync uses the port's own folders (`saves/`, `characters/`), with `sync.json` and `sync-backup/` in the user folder. Screenshot runs sign in as `yorehold-test-run` and never sync.
  - Default: the server and its key are set on a new Account tab in Settings, by Off, This computer (`http://127.0.0.1:7350`) or pasting from the clipboard, since the panels have no text boxes. `YOREHOLD_SERVER`, `YOREHOLD_SERVER_KEY` and `YOREHOLD_DEVICE` win over the settings like in the C++ client.
  - Default: the title shows the sign-in status bottom right, as the C++ client did. Connection errors are put in plain words ("nothing answers at that address").
  - Default: the server's `ai` config is read at sign-in and every minute and put on the World as a last layer, as the C++ client's `applyServerAi`.
  - Blocked: never run against a live server, same as the C++ client's H1. Needs one sign-in with two installs to call it done.
  - Not ported: the C++ client's LAN co-op (host and join), so Join co-op stays off the title, and `.yore` archives with the content signature. The content registry, votes and completions were never in the C++ client either.
  - Not done: `.uid` files for `rules/online` and the two test files. The editor makes them when it next opens the project.
- [x] **P15. Exports.** Windows build, then Android; an iOS export test early since C# on iOS is
  still experimental in Godot 4.
  - Default: `export.ps1 windows|android|ios` makes each build into `../.dev/export/`, out of the repo. Windows also starts the exported exe headless and can take a screenshot run of it. `docs/EXPORT.md` has the rest.
  - Default: an exported game copies `res://assets` out of the pack to `user://content` each start and reads that, since System.IO can't open a .pck or an APK. Create's "game's own content" is that copy in an export, so saving into it lasts one run.
  - Default: Android is arm64 only, prebuilt template (no Gradle), the game project on `net9.0` for Android builds only, ETC2/ASTC import on. The release keystore and its password are made in `export/keystore/` (git ignores it) and passed in by environment variables.
  - Default: `icon.svg` is a placeholder icon (amber Y on ink) since Android won't export without one. The Windows exe has no icon until there is an `.ico`.
  - Not done: the APK on a phone. No device was attached and the SDK here has no emulator; `adb install` steps are in EXPORT.md.
  - Blocked: iOS. Godot refuses C# iOS exports off macOS (NativeAOT needs Xcode). Needs a Mac with Xcode and an Apple developer team ID; the preset is ready.

## R. Any rules system, a free table for players (Josh, 10/8)

Josh, 10/8: "a player-facing Foundry VTT, free to download, for assets and making adventures;
the current structure is very close, it needs abstracting a bit." Yorehold stays a video game to
play (the fight screen, hotbar, portraits, BG3 feel), but what a turn, a check, a save or dying
means comes from the rules system the adventure names, and systems, adventures and art are
packages players make and share on the site. Unlike Foundry there are no add-on modules: every
package is one kind of content in the game's own format (below, R11). There is no game master:
the AI decides for enemies and NPCs, and the import turns a PDF (an adventure or a rulebook) into
game content quickly.

Where it stands (audit, 10/8): the numbers are data already (`rulesets/yorehold`: abilities,
skills, ranks, conditions, actions, reactions, spells, rests). The procedures are C#: checks are
hit/miss except attacks, a free move each turn, one reaction with two triggers, 5e death saves
and level-up HP, untyped bonuses, saves are abilities, class features can't grant actions, and
classes, items and creatures sit at the content root rather than in their system. The shipped
"yorehold" set mixes 5e and PF2e. Systems to prove it: D&D 5e from SRD 5.2 (CC BY 4.0) and
Pathfinder 2e Remaster (ORC); only their open text is used. Each step below also changes the
screens it touches, so the game shows whatever the system says, and is tried on both systems.

- [x] **R1. Names the code leans on become the system's.** One `roles` object in `ruleset.json`:
  the HP ability, default attack ability, initiative, perception and stealth skills, the hidden and
  downed conditions, the strike, stride and end-turn actions, the slot prefix and focus pool.
  Every hard-coded "con", "str", "dex", "perception", "hidden", "downed", "focus", "slots-" goes
  through it. UI: the sheet's initiative uses the system's (it reads "dex" today).
  - Done 10/8: `roles` in ruleset.json (CONTENT.md), with `carryAbility`, `thievery` (locks and
    traps that name no skill), `dead` and `interact` as well. Weapons that name no ability use
    `attackAbility`; the sheet shows initiative's own skill or ability. A default a system lacks
    (dex, con) now means none instead of refusing the file.
  - Left for R13: the import's own guesses (`BookCast` "dex"/"athletics", `SystemTable`
    "perception") still name yorehold ids; they map to the system once the import drafts systems.
- [x] **R2. A system owns its classes, items, creatures, races and feats.** They load from the
  ruleset folder first, the content root after (so today's packages keep loading). Manifests'
  `ruleset` and `requires` are read; a character records its system and is refused, with the
  reason, by an adventure on another. UI: New adventure in Create asks which system; the library
  shows each adventure's system.
  - Done 10/8: the game's classes, items and creatures moved into `rulesets/yorehold/`; a
    chapter loads its ruleset folder's, then the root's, then its own. A package's chapters must
    all play the system its manifest names. A hero made for another system is turned away with
    the reason and the ready-made hero takes the seat. The adventure list has a Rules column
    (`.dev\p11-adventures.png`); Create offers one New adventure per installed system.
  - Default: AI profiles (`ai/`) stay at the content root: they are how a creature behaves,
    not part of a system, until R12 makes the fight AI read the system.
  - Left for R11: `requires` is read but nothing is installed from it yet, and the version
    after the `@` in `ruleset` isn't checked. The character screen doesn't yet grey out heroes
    of another system before Start; the refusal shows in the log.
- [ ] **R2b. A rules language, so every rule is data.** Josh, 10/8: all of it data the AI can
  evaluate when making adventures. Instead of a switch per known system, R3-R8 are written in a
  small language the game interprets: dice expressions (pools, keep/drop, exploding, success
  counts, custom faces), formulas over a creature's numbers, comparisons into named outcomes,
  and procedures as lists of steps (roll, compare, branch on outcome, apply effect, ask a choice,
  repeat a bounded number of times). It has no file, network or engine access, every loop has a
  limit and every roll uses the game's seeded dice, so an uploaded system is safe to run and can
  be replayed. Each step writes a plain line to the log, so a result can be explained.
  - Josh, 10/8: it is all JSON. There is no separate script format: a system is JSON files, a
    procedure is a JSON list of steps (the `effects` lists actions and spells already use), and
    a formula is a short text value inside the JSON (`"degree": "total >= dc"`).
  - Done 10/8: formulas (`rules/core/Formula.cs`) and `checks` in ruleset.json (CONTENT.md,
    "Checks and formulas"): each roll kind's dice, named outcomes worst to best, the formula that
    picks one, what a save's outcome lets through, and critical damage as doubled dice or a
    formula. Attacks, checks, saves and initiative resolve through it, steps may wait on any
    outcome the system names, and the hit chance is counted from the same data
    (`CheckKind.Odds`, the first piece of the evaluator). The yorehold set's own rules are
    written out in its ruleset.json and every earlier test passes unchanged.
  - Done 10/8: `formulas` in ruleset.json for a creature's own numbers (ability modifier,
    proficiency, attack, damage, AC, checks, saves, DC, passive scores).
  - Done 10/8: exploding dice, success counts and Fate dice ("1d6!", "6d6s5", "4dF").
  - Done 10/8: the fight AI scores attacks with the system's own odds (`Tactics.HitChance`).
  - Done 10/8: opposed rolls (`"opposed": true` on a roll kind).
  - Done 10/8: traps, locks and dialogue checks pass by the system's check outcomes; the
    fumble is the `trapFumble` formula.
  - Done 10/8: expected damage: any dice's exact average (keep, exploding, success counts) and
    an attack's worth by outcome with the system's critical rule; the fight AI weighs targets
    and danger by it (`Tactics.ExpectedDamage`, `World.ExpectedDamage`).
  - Done 10/8: simulated fights (`FightSimulation.Forecast`): an encounter played out many
    times by the AI on both sides from seeds, giving the win chance, the chance of losing a hero,
    deaths and rounds, as one line ("this fight: won 9 in 10, 1 in 5 lose a hero, 3 rounds").
  - Done 10/8: Create's encounter panel plays a saved fight out 20 times in the background
    ("Forecast", "Play it out").
  - Done 10/8: `docs/RULES_LANGUAGE.md`, the whole language on one page for people and the
    story model: the folder, formulas and their names, dice, every ruleset.json key, effect
    steps, moments, the fixed core and how a draft is checked and measured.
  - Done 10/8: the forecast says when a fight is too hard (wins under 3 in 4, or loses a hero
    over a third of the time) or too easy (always won in two rounds unscathed); Create shows it.
  - Left: the builder's balancing by forecast (and parties of a chosen level, not only the
    chapter's own); a machine-readable schema beside the reference.
  - Readable by the AI: the language has a schema and a reference written for people and for
    the story model, and every rule in a system carries a one-line plain description. The
    import (R13) writes systems in it, and the game checks the draft by loading and running it.
  - Evaluable: a rules evaluator runs a system without screens: the odds of a check or attack,
    expected damage, and many simulated fights between a party and a group of creatures. The
    adventure builder uses it to set DCs and balance encounters for the party's level in any
    system, and Create shows the result ("this fight: 1 in 5 parties lose a hero").
  - Shipped systems, the yorehold set first, are rewritten in the language, and their tests
    must still pass, so the language is proved before R3-R8 build on it. An unusual mechanic
    (a dice pool, a card draw, a stress track) is then a new system file, not a game update;
    only a new kind of screen (R6's sheet layout, R4's hotbar) needs code.
- [ ] **R2c. Only the grid is assumed.** Josh, 10/8: the one real assumption is looking down on a
  square grid, playing a multiplayer BG3-like version of the imported system, with the AI driving
  the enemies instead of a game master. Everything else is abstracted the way Foundry VTT's
  systems are: HP, AC, initiative, abilities and skills, levels and XP, and classes are what
  some systems declare, not what the game assumes. Each slice keeps the yorehold, 5e, PF2e and
  Fate sets playing with their tests passing; Fate is the proof (no hit die, no AC, no class,
  stress and consequences instead of HP).
  - Tracks: a system declares its tracks (5e's HP; Fate's stress boxes and consequence slots;
    PF2e's dying and wounded; mana), each with a max formula, how damage is spread over them,
    and "down" and "dead" as formulas over them. HP becomes one system's track.
    Done 10/8: `tracks` (max formula, damage per point, heals, cleared by the fight's end or a
    rest), damage running through them in order and down when none takes the rest; HP is their
    total. Fate plays stress and consequences. Done 10/8: damage aimed at one track and the `shared`
    ones (Fate Core's physical and mental stress). Done 10/8: `death.dead`, a formula for a blow
    that kills outright (5e and PF2e massive damage).
    Done 10/8: party cards and the hotbar card draw tracks as marks ("●●○  ●  ●  ○"),
    the tip names each; logs name the defence ("Defend 1"); `chapters/fate-test` plays Fate.
  - Defences: named defences, each a formula (AC, a Fortitude DC) or a roll (Fate's Defend);
    each roll kind and action says which it is rolled against. AC stops being special.
    Done 10/8: `defences` as formulas, a roll kind's `defence`, an attack step's `against`; the
    AI, the aim, flanking and cover use whichever applies. Fate attacks meet Defend (the better of
    Quick and Careful), no AC. Left: the aim's odds for an action naming another defence.
  - The character's data: a system declares its numbers in groups (abilities, skills,
    approaches), text fields (Fate's aspects and high concept) and lists. The sheet is drawn from
    those groups, not a fixed menu of sections.
    Done 10/8: `fields`: words a character is made of (Fate's high concept, trouble, aspects),
    asked for in creation, kept on the character and the sheet, read by formulas as
    `field.<id>`. Left: number groups beyond abilities and skills; lists of things (gear-like
    entries a system invents); invoking an aspect as an action.
  - Options in place of classes: a system declares its option kinds (class, ancestry,
    heritage, background, archetype; Fate: none) and what each grants as the character advances.
    Creation's parts come from these.
    Done 10/8: `optionKinds` and `options/` files (PF2e heritages: forge and strong-blooded
    dwarf, cavern and woodland elf, skilled and hardy human), open by race and class, granting
    like features, picked in creation. Class is optional: a system whose creation never asks
    for one builds on its first class unseen (Fate), and the sheet names no level without
    advancement. Done 10/8: options taken at later levels (a class row's `options`: an archetype, a
    subclass), picked on the level-up screen.
  - Advancement: levels and XP are one mode beside milestones, Fate's milestones (swap a skill,
    +1 to an approach) and none.
    Done 10/8: `advancement`: xp, milestone (a level for each chapter gone on from) or none;
    level-ups keep tracks and fields. Fate plays milestones: each chapter gone on from
    raises one approach by 1 (class rows' boosts); the sheet names no level. Left: Fate's minor
    milestones (swap two approaches, rename an aspect), a chapter or
    effect step that grants a milestone.
  - Turn order: an initiative roll by formula, side by side, or Fate's order where whoever acted
    picks who goes next; the AI picks for its side.
    Done 10/8: `turnOrder`: one initiative order or side by side, which side first, rolled or
    by the modifier alone (Fate orders by Quick). Done 10/8: `picked`: whoever acted names who goes
    next among those yet to act (an ally by default, which is how the AI picks; a hero's player
    clicks someone in the turn order). Left: the last to act picking who opens the next round.
  - Effect steps and triggers name the system's tracks, defences and numbers, not built-in
    ones, so a system's own mechanic needs no new step kind. The kinds that stay fixed are the
    language's documented core.
    Done 10/8: a `resource` step reaches a track by its id, attacks name a defence with
    `against`, and formulas read `field.<id>`, `stat.<name>` and the defences. Left: a damage
    step aimed at one track; the documented core (with R2b's reference).
- [x] **R3. Checks in data.** A resolution table per system: degrees of success (5e: hit/miss and
  nat 20; PF2e: four degrees, ±10, nat 20/1 move a step), what a critical does (double dice or
  double total), and advantage from the target or the place as well as the attacker. Every
  effect branches on all four outcomes for attacks, saves and checks. UI: hit chance at the
  cursor shows the system's outcomes (one figure for 5e, four bands for PF2e); the log names them.
  - Done 10/8 (through R2b): degrees, criticals and opposed rolls in data, steps waiting on any
    outcome, the log naming the outcome; the aim reads "55%, 5% critical" from the system's own
    outcomes.
  - Done 10/8: the place gives conditions for a roll: `unseenAttackerCondition` and
    `unseenTargetCondition` in positioning.json, from light and darkvision; 5e's give advantage
    and disadvantage. Left: high ground (maps have no height yet).
- [x] **R4. The turn in data.** Action kinds per system (5e: action, bonus action, reaction,
  movement; PF2e: three actions, a reaction, free actions), a switch for the free move, attacks
  counted for a multiple attack penalty and `agile`, conditions that add or take actions
  (slowed, stunned, quickened), reaction triggers beyond leaving and entering reach (hit, missed,
  ally hit, spell cast, turn start). UI: the hotbar's pips and move bar are drawn from the
  system's turn; costs read "2 actions", "bonus action" or "reaction" as the system names them.
  - Done 10/8: `freeMove`, `attackPenalty` (a formula from attacks made this turn, used by the
    roll and the hit chance), conditions changing the turn's actions through the `actions` stat,
    and `"cost": "bonus"` actions that spend the bonus action (the hotbar tip says so).
  - Done 10/8: reaction triggers `hit`, `missed` and `allyHit` (after the attack, no prompt);
    agile and other traits through formulas.
  - Done 10/8: `turnWords`: costs read as the system names them ("2 actions", "free action").
  - Done 10/8: `beforeHit` reactions (Shield): taken as a hit would land, the roll read again.
  - Done 10/8: `spellCast` reactions, before the spell acts; a `spellLost` flag counters it.
  - Done 10/8: a hero's reactions to hits and misses are asked for when prompts are on (the
    fight waits on the answer); spells cast as reactions (Shield, slot spent).
  - Done 10/8: reactions taken in the middle of an attack (`beforeHit`) or a casting
    (`spellCast`) can't wait for an answer, so a player holds any of a hero's reactions back
    ahead of time: Sheet > Features lists them, each "used when it comes up" or "held back"
    (`HoldReaction`, kept in the save).
  - Done 10/8: a `beforeHit` guard is taken only when the defence it adds would make the roll
    miss (a Shield against a hit by 10 is kept, slot and all).
- [ ] **R5. Features that do things.** Classes, subclasses, races, heritages and feats grant
  actions, reactions, passive modifiers and triggered effects, not only numbers; feat kinds come
  from the system. Creatures get a list of strikes and abilities of their own. UI: the sheet's
  Features tab; Create's class, feat and creature editors gain "grants".
  - Done 10/8: `"general": false` actions and reactions, granted by class features, feats and
    creature files (`"actions": [...]`), checked against the ruleset, kept on the sheet.
  - Done 10/8: triggers (`triggers/`, on hit, miss or crit, with a formula and once a turn),
    granted like actions; Sneak Attack in 5e and PF2e.
  - Done 10/8: triggers on being hit (`hitBy`, landing on the attacker), on a kill and on turn
    start.
  - Done 10/8: feat kinds from the system (`featKinds`: PF2e's ancestry, class, skill and
    general; 5e's origin, general, fighting style and epic boon).
  - Done 10/8: the sheet's Features tab lists fields, options, feats by the system's kind names,
    and every granted action, trigger and reaction with its cost in the system's words; a
    system without levels shows no hit die or proficiency.
  - Done 10/8: Create's creature and feat forms have an "actions" list from the system's actions,
    reactions and triggers, and a feat's kind is picked from the system's feat kinds.
  - Done 10/8: Create's compendium has an Options form (a system's heritages and subclasses:
    kind, who may take them, grants) and feats list the spells they grant.
  - Left: grants on class features inside the levels table (edited as JSON there).
- [x] **R6. Defences in data.** Saves as their own list (5e: six abilities; PF2e: Fortitude,
  Reflex, Will), typed bonuses where only the best of a type counts, resistances, immunities and
  weaknesses by damage type, weapon traits (finesse, reach, ranged, agile, versatile), armour's
  Dex cap. UI: the sheet's layout (vitals, saves, defences) is a list the system gives.
  - Done 10/8: `saves` as their own list (Fortitude, Reflex, Will with their abilities);
    resistances, weaknesses and immunities as `resist.<type>`/`weak.<type>`/`immune.<type>`
    stats through a `damageTaken` formula; creatures' `stats`. A Dex cap is a formula already
    (`armorClass`: `min(mod.dex, stat.dexCap)`).
  - Done 10/8: typed bonuses (the best bonus and worst penalty of a type) and weapon `traits`
    that formulas and the attack penalty read (`trait.finesse`, `trait.agile`).
  - Done 10/8: `sheet` in ruleset.json: the sections the sheet shows, in order, and their
    labels; PF2e's saves listed by name; defences shown. Fate shows Stress and Stunts, no hit die.
  - Done 10/8: sections a system invents come from its own data: `fields` (Fate's aspects) and
    `tracks` (stress and consequences, drawn as marks on the cards).
- [x] **R7. Level-up HP and dying in data.** HP growth by formula, and a death mode: death saves
  (5e) or dying and wounded values with a recovery check (PF2e). UI: a downed hero's card shows
  the system's track.
  - Done 10/8: `death.track` (start, damage, the recovery roll and DC, each outcome's change,
    when it kills, wounded) beside the death saves; `hpFirstLevel` and `hpPerLevel` formulas.
    The log names the recovery check and the dying value; the dying condition carries it.
  - Done 10/8: wounded goes back to 0 on the rests `woundedClearedBy` lists (PF2e: the night's).
  - Done 10/8: camp plays the adventure's system (a chapter naming no ruleset can borrow one),
    and 5e and PF2e have supplies for their night's rest.
  - Done 10/8: a downed hero's card reads the system's track in place of HP ("dying 2/4",
    "saves 1/3, 2/3", "stable", "taken out"), the death value found from the system's formula;
    its tip says it in a line.
- [x] **R8. Character creation from the system.** Its steps (5e: species, background, class,
  scores; PF2e: ancestry, heritage, background, class, boosts), score methods and level-up
  choices. UI: the creation screen lists the system's steps.
  - Done 10/8: `creation` in ruleset.json: the steps, which parts each picks, what the parts are
    called and the score methods offered; the screen draws its tabs and parts from it (Fate is
    one step: name, concept, approaches; PF2e names Ancestry and Attributes).
  - Done 10/8: heritages as the system's own option kind (R2c), fields (Fate's aspects), class
    optional.
  - Done 10/8: `boosts` as a score method (PF2e: four boosts of 2 from 10).
  - Done 10/8: ability raises at a level (`boosts` on class rows; 5e's level 4 improvement).
  - Done 10/8: 5e's +2 to one or +1 to two (`boostsRepeat`).
  - Done 10/8: a level gained in play with no picks (from experience or a milestone) is offered
    in Characters as Level up, which fills that level's picks without adding one (`FillLevel`).
  - Done 10/8: PF2e's classes to level 5: four boosts at 5, saves and weapons rising in rank
    (Bravery, Weapon Mastery, Reflex and Perception Expertise), a skill trained at 3 and 5,
    third-rank slots. Left: a boost past 18 counting half, class and skill feats (the set has
    no feat files yet).
- [ ] **R9. D&D 5e (SRD 5.2) as a package.** Everything the SRD has that the steps above allow;
  what doesn't fit goes back as a step. Today's "yorehold" set stays as Yorehold's own system.
  - Done 10/8, first slice: `rulesets/dnd5e` (written by `.dev\make-dnd5e.py`): the six
    abilities and 18 skills, d20 checks, proficiency by level, DC 8 + mod + proficiency, death
    saves, the SRD conditions plus dodging/helped/hidden, Attack, Dash, Dodge, Help, Hide, Stand
    up, Opportunity Attack, Second Wind and Cunning Action as granted bonus actions, armour with
    a Dexterity cap by formula, resistance halving and vulnerability doubling, fighter, rogue,
    cleric and wizard to level 5, eight spells, fifteen items, five creatures. A fight under it
    plays to the end (SystemPackageTests). The SRD's CC BY credit is in the credits.
  - Engine pieces it needed, now general: formulas in dice (`2d8+{mod.caster}`), spell attacks,
    attacks against a creature with advantage from its conditions, role-named built-in actions,
    bonus-action spells.
  - Done 10/8: Sneak Attack as a trigger; Disengage and the rogue's bonus-action Disengage (a
    reaction's `unless` flag); unseen attackers and targets in the dark; origin, general,
    fighting-style and epic-boon feat kinds; supplies for the long rest; poisoned and frightened
    hindering checks (`disadvantageOnChecks`).
  - Done 10/8: prone caring how far the attacker is, hits on the paralysed and unconscious from
    beside them as critical hits; Shield cast as a reaction (a reaction may name a spell; the
    slot is spent); versatile weapons by the hands they leave free (`hands.free` in weapon dice).
  - Doesn't fit yet (back to the steps):
    two-weapon fighting; Scorching Ray's extra ray per slot; Hold Person's repeat save at the
    caster's DC (the held condition saves at 13). Then the rest of the SRD: all classes and
    levels, the spell list, the bestiary.
  - Done 10/8: ten spells for the 2nd and 3rd level slots (Inflict Wounds, Command, Scorching
    Ray, Shatter, Hold Person, Lesser Restoration, Fireball, Lightning Bolt, Mass Healing Word,
    and Counterspell as a reaction to a casting), prepared counts by level, massive damage.
  - Done 10/8: a first bestiary of 22: zombie, ghoul, ogre, hobgoblin, bugbear, giant spider,
    giant rat, kobold, cultist, cult fanatic (a caster with slots), guard, tough, brown bear,
    dire wolf, worg and owlbear beside the first six, with natural weapons and on-hit riders as
    triggers with a save (a wolf's bite knocks down, a spider's venom, a ghoul's paralysis).
  - Done 10/8: the other eight classes to level 5: barbarian (Rage), bard (Bardic Inspiration),
    druid, monk (Martial Arts, focus for Flurry of Blows, Patient Defense, Stunning Strike),
    paladin (Lay on Hands, Divine Smite), ranger and warlock (Hunter's Mark and Hex as a spell
    plus a granted trigger), sorcerer; Extra Attack at 5 for the martial ones; Eldritch Blast,
    Produce Flame and Vicious Mockery. Done 10/8: ranged weapons: an attack's target range may be
    `"weapon"`, an item a `range` (shortbow, longbow, light crossbow; PF2e shortbow and longbow;
    goblin archers), and an enemy with a bow shoots from where it stands. Doesn't fit yet: long
    range with disadvantage, a ranged attack beside a foe, ammunition, unarmoured defence as a formula of the armour worn, warlock slots back on a short
    rest, Wild Shape, subclasses beyond a single path.
  - Done 10/8: origins: nine species (traits as "species" feats: Breath Weapon, Draconic and
    Dwarven Resilience, Adrenaline Rush, Infernal Legacy), the four SRD backgrounds with their
    origin feats (Alert, Magic Initiate, Savage Attacker, Skilled), two fighting styles. Feats,
    features and options can grant spells.
- [ ] **R10. Pathfinder 2e (Remaster, ORC) as a package.** The real test of R3-R8.
  - Done 10/8, first slice: `rulesets/pf2e` (`.dev\make-pf2e.py`): three actions and no free
    move, Strike/Stride/Raise a Shield/Demoralize/Hide/Stand, the multiple attack penalty with
    agile, four degrees on attacks, checks and saves with nat 20/1 steps, critical damage as
    double the total, basic saves (double, full, half, none), ranks with level, Fortitude/
    Reflex/Will, Perception for initiative, ancestry + class HP, armour with item bonuses and
    Dex caps, typed status and circumstance penalties, frightened/slowed/quickened/off-guard/
    prone/grabbed, flanking, the dying and wounded track, Reactive Strike as a fighter grant,
    four classes to level 3, five spells, three ancestries, four creatures. A fight plays to
    the end. The ORC notice is in the credits.
  - Done 10/8: weapons carry a `damageType`, so resistances meet Strikes in both systems.
  - Done 10/8: Sudden Charge (`approach` moves and attacks with `reach`).
  - Doesn't fit yet:
    monsters use the PC proficiency maths plus a flat `attack` stat; PF2e's versatile (a
    damage type choice); deadly's die by weapon (all d8 here); heightening cantrips by rank is
    written per spell as a formula.
  - Done 10/8: classes to level 5 (R8) and ten more spells for ranks 1 to 3 (Breathe Fire,
    Thunderstrike, Harm, Acid Grip, Blazing Bolt, Spiritual Armament, Fireball, Slow, Chilling
    Darkness, Heroism), massive damage.
  - Done 10/8: a bestiary of 16: zombie shambler, ghoul, ogre warrior, hobgoblin soldier,
    hunting spider, giant rat, cultist, cult leader (a caster), guard, grizzly bear, warg and
    owlbear beside the first four; knockdown, venom and paralysis as triggers with a save.
  - Done 10/8: seven more classes to level 5: barbarian (Rage), champion (Retributive Strike as
    an allyHit reaction, Lay on Hands on focus), ranger (Hunt Prey, Hunter's Edge, a longbow),
    monk (Flurry of Blows once a turn), bard (Courageous Anthem, Daze), druid and sorcerer; bows.
    Doesn't fit yet: rage's wait before raging again, compositions lasting while sustained,
    instincts, causes and bloodlines beyond one each.
  - Done 10/8: gnome, goblin, halfling, orc and leshy with two heritages each; eight
    backgrounds; feats of all four kinds (ancestry lores, First World Magic, Power Attack, Nimble
    Dodge, Counterspell, Hunted Shot, Crane Stance, Battle Medicine, Toughness, Fleet,
    Incredible Initiative), offered at levels 1 to 5 as the book does.
  - Done 10/8: deadly and backstabber as general triggers (anyone wielding the trait), the
    hidden as a DC 11 flat check (`attackersFlatCheck`), heritages, boosts, a free action named.
- [ ] **R10b. Fate Accelerated (SRD, CC BY 3.0) as a package.** Josh, 10/8: a third, more
  abstract free system. No classes, no HP, no d20: 4dF plus an approach (Careful, Clever,
  Flashy, Forceful, Quick, Sneaky) against the ladder, four outcomes (fail, tie, succeed,
  succeed with style), shifts as damage into stress boxes and consequences, aspects and fate
  points. If it plays from JSON, the language holds for anything d20-shaped and beyond; what
  doesn't fit (aspects you invoke, compels) goes back as steps.
  - Done 10/8, first slice: `rulesets/fate-accelerated` (`.dev\make-fate.py`): six approaches as
    the abilities (rating = modifier by formula), 4dF with fail/tie/succeed/style, attacks by
    approach against the defender's Quick, shifts as stress (damage `{max(margin, 1)}`), Create
    an Advantage for free invokes, fate points spent to Invoke (+2), one "Character" class with
    refresh 3, three foes. A fight plays to the end.
  - Engine pieces it needed, now general: `margin` in dice formulas (how far a roll beat its
    DC), attacks that name the ability they use, scores of 0.
  - Done 10/8: active defence (`"opposed": true` on its attacks and checks).
  - Done 10/8 (R2c): stress and mild, moderate and severe consequences as tracks, stress
    clearing when the fight ends; Defend as its own defence, no AC; order by Quick with no
    roll; high concept, trouble and aspects as fields; no class asked for, no levels.
  - Done 10/8: stunts as the system's one feat kind, one picked when the character is made and
    one more at the third and sixth milestones (Tough as Nails, Sure-Footed, Hard Hitter, Quick
    off the Mark, Lucky Break, Shadow-Stepper), read through the stress and Defend formulas;
    six more foes (cultist, guard and shambling dead as mooks, a giant spider, an ogre, a
    masked duelist).
  - Doesn't fit yet: aspects as things on the scene and invoking a character's own aspects
    by name; compels; zones instead of squares; ties' boosts; concessions; choosing which
    consequence takes a hit (the game fills them mildest first).
- [ ] **R11. Packages by kind, no add-ons.** Josh, 10/8: all content is in the format the game
  sets and lives on the site; no add-ons, just skins, systems, monsters and so on. A package
  has one `kind`: `system` (a ruleset folder, R1-R8), `adventure`, `skin` (R16), `art`, or a
  content set of one type for one system (`creatures`, `items`, `spells`, `classes`, `feats`,
  `races`, `backgrounds`, `maps`). A set only adds entries of its type in the game's files; it
  can't change rules, other packages' entries, screens or code, and nothing is scripted. A game
  loads its system, the sets the player turned on for that system, the adventure, then art and
  the skin; a set or adventure names its system and is refused on another. The same loader
  checks a package in the game, in Create and on the site, so anything that uploads also loads.
  `.yore` archives are ported. UI: a Library screen with a tab per kind (installed, update,
  remove, on/off for sets), and an adventure's page says what it needs and gets it.
  - Done 10/8: the manifest's kinds (`system`, `skin`, `art` and the one-type sets beside the
    older names), and `ContentPackage.CheckSet`: a set names its system, holds only its type and
    pictures, and its entries load under that system.
  - Done 10/8: sets installed in user://sets join the games of the system they name, under the
    adventure (`ContentSets.For`); one for another system or turned off stays out, with the
    reason in the log. Option sets (feats, spells, races, backgrounds) read from a set's root.
  - Done 10/8: Settings > Content lists the installed sets (name, kind, the system's name, or why
    one can't be used), each On or Off; the settings file keeps the ids turned off (`setsOff`).
  - Done 10/8: Options > Content is the library for now (Josh's four title buttons stay): the
    rules systems with what each holds, installed skins, art packs and content sets, each kind
    with its folder to open.
  - Left: update and remove from the site (R14), the same check in Create and on the site,
    `.yore` archives.
- [x] **R12. The AI plays the other side, in any system.** Josh, 10/8: no game master and no
  host tools; the AI makes the enemies' and NPCs' decisions. The fight AI reads the loaded
  system's actions, costs and odds (R3, R4, through R2b's evaluator) instead of d20 maths and the `strike` action, so it
  plays a PF2e turn (three actions, the attack penalty, Raise a Shield) as well as a 5e one, and
  keeps its flee, surrender and stealth behaviour. What a GM would decide outside fights (an
  NPC's answer, whether a guard believes a bluff) comes from the adventure's words and the
  system's checks; no story model runs during play (Josh, 10/8: models are for creating, S5c).
  - Done 10/8: targets and danger weighed by the system's odds and expected damage (R2b); a
    guard (Raise a Shield, Dodge: any action that puts a condition on its doer) is weighed by
    the damage it saves under the system's odds, and the AI thinks again between strikes, so a
    third Strike at -10 gives way to a shield.
  - Done 10/8: the AI attacks with its best attack action (`BestAttack`: every non-spell action
    that rolls attacks at a creature, by expected damage per action with repeats and the
    system's critical counted); 5e's Bandit Captain uses Multiattack.
  - Done 10/8: a condition an action puts on someone is weighed by what it changes under the
    system's odds (`ConditionWorth`: damage dealt and taken for a round, a lost turn), not a
    flat guess, so Demoralize and a stun are worth what they do; non-spell tricks aimed at someone
    else (Demoralize) are weighed like spells; a guard only once a foe is beside it, at three
    quarters of what it saves (guarding puts a fight off, striking ends it).
  - Done 10/8: the AI's walk: a cell past its move costs it a Stride (or Dash), and short of its
    target with actions left it thinks again, so a PF2e creature strides twice and strikes, or
    strides three times, rather than stopping after one.
- [ ] **R13. Rulebooks become systems.** The import takes a rulebook PDF as well as an adventure:
  it drafts a system package (abilities, skills, conditions, actions, spells, classes, feats,
  creatures, the check and turn tables from R3-R4) in the vocabulary, quoting the page for each
  entry and listing what it couldn't place. The writer checks it in Create's compendium. Only
  books the writer owns; a system made from someone else's book stays private unless its licence
  allows sharing (SRD, ORC).
- [ ] **R13b. Adventures import and are built for the chosen system.** Josh, 10/8: imports and
  AI-driven adventure creation working in every system without much lag. The adventure import
  and the outline builder stop writing yorehold ids (`BookCast` "dex"/"athletics", the game's
  classes and goblin, `SystemTable` "perception"): the writer picks a system, the story model
  is given that system's skills, conditions, classes and creatures from its JSON, and stat
  blocks land in its creature format (S12-S15). DCs and encounters are set by R2b's evaluator
  for that system. Speed: rules, odds and the fight AI stay local; simulated fights for balance
  run in the background in Create, never in play; only what the story model writes waits on it.
  - Done 10/8: the builder takes a target system (`OutlineBuilder(..., system)`,
    `StoryImport.RulesSystem`): chapters name it, checks the system has no skill for go to what
    it notices with, heroes take its plainest class, the game's own items and creatures take
    the system's of the same name or are left out, NPCs with no creature stand as a bystander
    written into the package, and the report says each. The sample adventure builds and plays
    under 5e, PF2e and Fate.
  - Done 10/8: the import review's Rules button picks the system the adventure is built for, and
    the next book is read for the same one: the story model is offered that system's creatures,
    items and classes.
  - Left: the reading stage (`BookCast`) still
    writes the game's own ids for the builder to fit; stat blocks in the system's own creature
    format (a Fate target needs its approaches); DCs set by the system's odds.
  - Done 10/8: encounters fitted by the forecast: Create > Encounters > Fit to the party plays
    the fight in the background with the last foes standing aside, one more each try, until it
    is no longer too hard, and offers to take them out (`FightSimulation.Fit`). Left: running
    it for every fight of a freshly imported adventure, adding foes to one that is too easy.
- [ ] **R14. Sharing.** Upload and download every kind from R11 through the site and in the game,
  with licence, author, links and the no-AI-uploads declaration; credits built from what an
  adventure uses. The site runs the game's checks on each upload and refuses one that wouldn't
  load, with the file and field. The site browses by kind and by system. UI: the Library's Browse
  tab; the site's Submit form.
- [ ] **R15. Premium content and publishers.** Josh, 10/8: support for companies like Paizo and
  Wizards of the Coast, and any paid content.
  - Accounts own what they bought or redeemed (a code from a book or a publisher's store); the
	server holds the list. A premium package is signed and its files encrypted to the owner's
	account, with an offline grace period so play doesn't need the internet every session. This
	keeps honest players honest; it can't stop copying, and isn't sold as if it could.
  - Publishers get verified accounts: upload with prices, previews and their own licence terms,
	sales reports, payouts, takedowns, and their names and logos on their own store pages
	(nowhere else). Any creator can sell too; free stays the default.
  - Dependencies show what a player has: an adventure that needs a premium system or pack says so
	on its page ("needs Pathfinder 2e Core, owned / €x"), and the game offers to get it before
	loading. Art a player doesn't own falls back as usual, so a shared adventure still plays.
  - A player's own import of a book they own stays private; publishers can sell their books
	already converted, which is the better product.
  - Selling uploads also need the no-AI declaration; a publisher signs it once for its catalogue.
  - Needs decisions only Josh can make: the payment provider (a merchant of record such as Paddle
	handles tax and VAT worldwide; Stripe Connect needs that done by hand), the store's cut, and
	the publisher agreements themselves. Until then this step builds entitlements and locked
	packages with test purchases only.
  - UI: the Library's Browse tab shows price, publisher and owned; the site gets a store page
	per package, a publisher dashboard, and Redeem a code.

- [ ] **R16. Skins redo the whole look.** Josh, 10/8. A skin is a package a player picks in
  Settings (or an adventure suggests) that can change everything drawn, not the rules or the
  layout's jobs: the screens' colours (today `ui/colors.json`, read at start instead of the fixed
  table in `src/hud/Palette.cs`), the map's palette, fonts and sizes, panel edges, corners and
  shadows, buttons, the hotbar and portrait frames, token frames (already a skin), condition and
  action icons, the cursor, sounds, and the title's banners and logo. The game's own look stays
  the default and keeps its rules (Apollo, flat, 1 px edges); a skin may break them on purpose.
  - Josh, 10/8: the UI is driven by data, styled like CSS. Every screen is a layout file (a tree
	of panels, lists, tables, bars and buttons, anchored and sized, bound to the system's data the
	way R6's sheet layout is), and a stylesheet styles it: selectors by element type, class, id
	and state (hover, pressed, disabled, selected), properties for colours, fonts, sizes, padding,
	borders, corners, icons and sounds, with variables and later rules winning. Godot has no CSS,
	so this is a subset the game parses into Godot themes and per-control overrides; no
	animation, scripts or web layout. A skin is a stylesheet plus pictures, fonts and sounds, and
	may also replace a screen's layout file, as long as every control the screen's job needs is
	still there (the loader checks by id and falls back to the default layout if not).
  - The Godot theme is built from the stylesheet at start, not from the hand-made
	`scenes/hud/hud-theme.tres`, so a skin is files and no code. A skin that leaves something out
	gets the default for it. The game's own look becomes the default stylesheet.
  - Checks: every screen's screenshot taken with a test skin as well as the default, so no
	colour or font is left hard-coded (the palette test becomes "only colours the skin names").
  - Skins share and sell like any package (R14, R15); a publisher's system can ship its own look.
  - UI: Settings > Display > Skin with a preview; Create gains a skin editor (colours, fonts,
	edges, with the screens shown live beside it).
  - Done 10/8: the screens' colours are read from `ui/colors.json` at start (`UiColors`,
    `Palette.Load`): a file laid on top (an art pack now, a skin later) recolours every screen
    drawn in code; a role it leaves out keeps the game's own.
  - Done 10/8: the shared Godot theme follows: every fill, edge and text colour in it that
    matches a role the file changed takes the new colour at start.
  - Done 10/8: the faces come from `ui/fonts.json` the same way (sans, book, mono: system font
    lists swapped into the shared theme at start).
  - Done 10/8: skins: a folder in user://skins picked in Settings > Display > Skin is laid over
    the game's content last, so its ui/colors.json, ui/fonts.json and token frames win (from the
    next start).
  - Done 10/8: font files a skin brings: a role in ui/fonts.json may start with a .ttf, .otf or
    .woff file inside the skin, the system names after it as fallbacks.
  - Done 10/8: edges, corners and a hard shadow from `ui/shapes.json`, laid on the shared theme.
  - Left: icons; skins as packages picked in Settings; the stylesheet and layout files; boxes
    the code draws itself (the map's frames) following the shapes too.
- [ ] **R17. 3D dice.** Josh, 10/8: dice render in 3D when rolled, like Foundry's Dice So Nice. The
  result comes first from the game's seeded dice (R2b), so rolls stay fair and replayable; the
  physics throw is then played with the die turned so the rolled face lands up. Dice shapes come
  from the system's dice expressions (d4 to d100, custom faces such as Fudge or symbol dice, as
  many as a pool rolls), and their look (colour, material, numbers, sound) from the skin, with a
  player's own dice set chosen in Settings. Rolls are shown in a strip over the fight or the
  sheet and then leave the numbers in the log. Settings: off, fast, or full; with many rolls at
  once (a fireball's saves) they throw together. Hidden rolls (an enemy's stealth) aren't shown.
  - Done 10/8: each roll's dice reach the screen with the face rolled (`DiceFaces`), the solids
    d4 to d20, d100 as two d10s and Fate dice are built in `rules/` (`DiceSolids`), and
    `DiceTray` throws them in a strip over the fight, up to six at once, the unkept die of
    advantage dimmed, numbers upright. Settings > Dice: off, fast, full.
  - Done 10/8: hidden rolls stay off the strip: an action's `secret` and anyone not in the party
    whom the party can't see.
  - Done 10/8: the look from the skin (`ui/dice.json`: colours by role or hex, how many at once,
    size), the rest of a big roll as "+N".
  - Left: sound; a player's own dice set beside the skin's; symbol faces from a system's custom
    dice; rolls made on the sheet.

- [ ] **R18. A large free asset library for the map maker and portraits.** Josh, 10/8: anything
  the game may ship without asking, with the authors in the credits. Allowed: CC0, public domain,
  CC BY, OGA-BY. CC BY-SA only as separate, unchanged files outside paid packs (it forbids
  locking). Not allowed: non-commercial (NC), no-derivatives (ND), or "free for personal use"
  terms (Forgotten Adventures, 2-Minute Tabletop, Tom Cartos), and anything AI-made (itch's "No
  AI" tag where there is one; otherwise uploads from before 2022 or an author's statement).
  - Sources, first pass: Kenney (CC0: roguelike, dungeon, indoors, map packs); 0x72 DungeonTileset
    II (CC0); Dungeon Crawl Stone Soup tiles (public domain, 6000+ incl. the supplemental set:
    terrain, walls, monsters, items, spell effects); Screaming Brain Studios (CC0 top-down
    tiles); OpenGameArt's CC0 collections, item by item; ambientCG and Poly Haven (CC0
    textures for painted floors, terrain and dice materials); Quaternius (CC0 3D, for 3D dice
    and props rendered top-down); game-icons.net (CC BY, 4000+ icons for actions, conditions,
    items). Portraits: museum open access (the Met, Art Institute of Chicago, Cleveland,
    Smithsonian, National Gallery of Art, Rijksmuseum): CC0 paintings, cropped to faces.
  - Each file keeps its source, author, licence and link in the art pack's manifest; the
    credits screen is built from it. A small downloader in `.dev` tool form fetches, checks the
    licence field, crops and sorts into art packs (tiles, props, tokens, portraits, icons) and
    tags them for search in the map maker.
  - Mixed styles don't mix on one map: packs are grouped by style (pixel 16/32 px, painted,
    museum painting), the map maker filters by the map's style, and a pack can be recoloured
    to the Apollo palette on import (CC BY credits note the change).

Order: R1 and R2 first (small, and every later step needs them), then R3-R8 each tried on both
systems, R9 and R10 alongside them as the checks, then R11-R17. The bestiary import (S12-S15)
waits until R6, so stat blocks land in a system's creature format.

## S. Story import

A writer brings a book (an adventure module as a PDF, a manuscript, a pitch) and gets a package
they refine in Create. Five stages, each leaving a file the next one reads, so any stage can be
run again or done by hand:

```text
book.pdf / .txt / .md
  1 read      import/source.json + import/pictures/   pages, text blocks, pictures; no guessing
  2 outline   import/outline.json                     the game's own data shapes, filled from the book (server)
  3 review    Create > Import                         the writer takes, changes or drops each card
  4 build     the package's normal content files      deterministic; the same outline gives the same files
  5 refine    Create's other modes, Playtest
```

The outline is not a summary of the book: it is the adventure already in Yorehold's data, the
creatures, items, chapters, encounters, containers, dialogue, quests, triggers and story graph in
the shapes their content files have, so the book's ideas are cut down to what the game can hold
(Josh, 10/7). The only things it holds that a package doesn't are where each entry came from, the
picture it goes with, maps as rooms and links rather than tiles, and the notes on what didn't fit.

Rules for all of it:
- The writer's words stay as written. Every outline entry says where it came from (page and the
  words quoted) or is marked invented, and the review shows which is which.
- Pictures are only ever taken from the book. A picture is cut out of its page and named; nothing
  is drawn, filled in or fetched.
- Imports are made in `create/` in the user folder. A book's text and pictures never go into this
  repo; the tests read a small book written for them.
- The game has one ruleset. A book's own numbers are kept in the outline as written and turned
  into Yorehold's by a table per source system (data), never by adding that system's rules.
- What the game has no way to play yet (a chasm to jump, a ledge to push someone off, a room that
  needs a torch) is not dropped: it goes on the story graph as a note and in `import/report.json`.

- [x] **S1. Reading the book.** `rules/import`: a PDF (PdfPig from NuGet, pinned), a `.txt` or a
  `.md` into `import/source.json`: pages, text blocks with their box, font and size, a guess at
  heading, body or boxed text from size and face, reading order across columns, and each picture
  bigger than a thumbnail saved to `import/pictures/` with its page and box. Page backgrounds
  (a picture covering the page) are left out. Described in CONTENT.md.
  - Default: blocks are cut by PdfPig's recursive XY cut, then split where a heading sits on its paragraph. Columns are found from the paragraphs, not the page's middle, and cells on one row of a column are joined into one block.
  - Default: a font that gives its letters no height (seen in a real module) gets half its size as height, or lines and columns can't be told apart. Doubled letters from drop shadows are dropped.
  - Default: `box` is only given for real rectangles; a frame drawn as four loose lines isn't seen yet.
  - Default: a JPEG is saved as it is in the file. One in CMYK would not open in the game; none seen yet.
  - Default: the tests write their own PDF. `YOREHOLD_IMPORT_BOOK` and `YOREHOLD_IMPORT_OUT` make the test run read any book into a folder for a look by hand.
- [x] **S2. The outline.** `import/outline.json` and its reader. Entries are
  `{id, kind, data, from, picture}`: `kind` is a content kind (adventure, chapter, creature, item,
  hero seat, dialogue, quest, trigger, container, encounter, story node, place, link, note);
  `data` is that kind's own content file shape, so the reader checks it with the game's own
  loaders and a valid outline is valid content; `from` is the page and quoted words, or
  `invented`. Two kinds have no file of their own: `place` (a room: label from the book's map,
  size, what it holds, read-out text) and `link` (between places: open, door, locked door with
  its key or check, climb, jump). Things the game can't play are `note` entries. A JSON schema
  per kind is written from the same loaders into `assets/import/schemas/`, so the story model
  is asked for exactly these shapes. A sample outline for the tests' book.
  - Default: the schemas are one hand-kept file, `assets/import/schemas.json`, not written from the
    loaders (they aren't built from a schema). A test holds them to the kinds and to the sample;
    the reader stays the real check and what fails it goes back to the model.
  - Default: chapter parts (seat, NPC, encounter, container, trigger) name a place instead of a
    cell, since the builder lays the map out; their full check is the built chapter's own load.
  - Default: `story node` from the list above isn't a kind: the builder makes the story graph from
    places, fights and flags. Read-aloud text sits on its place.
  - Default: every entry has one id and the model must use ids, not names, after first mention
	(Story2Game's main failure was one key called "Key" and "Metallic Key"). Things in the way
    are written as preconditions and effects (needs a key; opens the door), which is what made
    Story2Game's actions work.
- [x] **S3. Building.** `OutlineBuilder`: since entries already have the files' shapes, building
  is mostly writing each entry to its file (`content.json`, `adventure.json`, chapters, creatures,
  items, dialogue, quests, `story.json`, portraits) and turning ids into paths. The real work is
  maps: rooms laid on the grid from the places, their sizes and links, doors and locks as kits,
  encounters and containers put in their room, read-out text as room triggers. A creature or item
  the game already has by name is used, not copied.
  A party seat takes `image` so a book's hero keeps their own picture. The built package passes
  `ContentPackage.Validate` and its fights play out under `AutoPlay`.
  - Default: the model never places tiles; rooms are laid out by code from the outline's sizes
    and links (Word2World and the roguelike map paper both found models bad at layout and good
    at naming what goes where). A path check from the start reaches every place, and what fails
    goes back to the model once with the reason before it lands in the report.
  - Default: rooms go east, south, west then north of the room they link from, sliding along its
    side until they fit with one wall between; a link that can't be a shared wall is a corridor
    cut straight through. A place with no link is joined to the one before by an open way.
  - Default: `secret` is played as a plain door, `climb` and `jump` as open ways; each says so in
    the report. The book's notes go on the chapter's scene in the story graph.
  - Default: the map is written as text rows (stone, grass, wall, tree), so Create's Map mode
    rewrites it in its own form on the first save.
- [x] **S3b. Room text on entering.** A map area (a place's rectangle) that sets a flag the first
  time a hero steps in, so a chapter trigger can show that room's read-aloud passage then. The
  builder writes one per place with a passage, and the report line about it goes.
  - Default: the passage shows as a one-line conversation spoken by the room's name; a cutscene
    caption would hold up play. Areas wait for a fight to end.
- [x] **S4. What needs no model.** A picture goes to the name of the nearest heading under or
  over it in its column. A picture with numbers printed on it that match numbered headings is
  the map, and the numbers give each place its spot on it. A plain paper ground around a figure
  is made see-through (off by a switch). Source system tables in `import/systems/`.
  - Default: on Caves of Shadow the layout alone finds all seven numbered places with their
    passages, the map with all seven numbers on it, and the four heroes' names under their
    pictures. A long heading by a picture (a page title, a back-cover blurb) is not a name.
  - Default: pictures are read and written with StbImageSharp and StbImageWriteSharp (public
    domain, NuGet, pinned), since `rules/` has no Godot. Paper is cleared from the edge only.
  - Default: the outline keeps the book's skill names as written ("Open Lock"); the table maps
    them. `dnd-3.0` and `dnd-5e` ship with no number changes, the d20 numbers being close enough;
    a table for a system with other scales says so in its own lines.
  - Default: the draft's places are 8 by 8 until the model or the writer sizes them, and are not
    linked, so the builder joins them in order; the map note carries where each number sits.
- [x] **S5. The story model.** `IStoryModel` with one request shape: a chunk of `source.json`
  (with the page pictures, for a model that can see them) and the outline so far in, outline
  entries out, checked against the outline's own reader. The main path is `yorehold-server`
  sending the request to large models on rented GPUs; a service on this computer (Ollama's
  address, model named in settings) is an option for writers who want it offline. Prompts are
  files in `assets/import/`. Tests use a stand-in; a run with no model still does S1 and S4 and
  leaves the cards to fill by hand.
  - Josh, 10/7: needing the server and GPU clusters is fine for this; it may be a paid feature
    with use limited per account. The models are reached through OpenRouter, picking very cheap
    ones; the aim is under one cent per book.
  - Default: budget sized from Caves of Shadow (16 pages, about 8,000 words, about 11,000 tokens
    of text). Text read in a few passes with the outline so far is about 35,000 tokens in and
    10,000 out; at about $0.10 in and $0.40 out per million tokens that is under half a cent.
    Page pictures go only to the passes that need them (which picture is whose, the map), about
    1,000 tokens a page.
  - Default: every model is reached with the same OpenAI-style chat request
    (`/v1/chat/completions`, a JSON schema in `response_format`), which OpenRouter and Ollama
    both take, so the model is an address and a name in settings. While testing (Josh, 10/7),
    the address is a stand-in on `127.0.0.1:8765` kept in `.dev\story-model-shim.py`, outside
    the repo, that answers on Josh's own subscription; it is swapped for OpenRouter later.
    Pictures aren't passed through it yet.
  - Default: the model for each pass is named in the server's config, not in the game, so a
    cheaper one can be swapped in without a release. Each run logs its tokens and cost, and a
    book already read (same file hash) is answered from the last result for free.
  - Done 10/7: `ChatModel` (the chat request, any address), `StoryReader` (pages in chunks of about
    14,000 characters with the outline so far; entries checked by the outline's reader, failures
    sent back once, then dropped; quotes not in the book make the entry invented; captioned
    pictures given to heroes, people and creatures by name), prompts in `assets/import/`, the
    address and model name in settings (`storyModel`, `storyModelName`).
  - First real run, Caves of Shadow through the stand-in on Sonnet: 6 calls, 65,000 tokens in and
    12,500 out, about two minutes; 7 places, 7 links (two locked doors, the crevice as a jump),
    4 fights, the 4 heroes, Jeffries and his talk, 10 items, 8 chests and a quest. It built with
    no problems. At OpenRouter's cheap models that many tokens is about a cent, just over the
    aim; the outline sent with every chunk is most of it, and is the first thing to trim.
  - Not done: pictures aren't sent to the model, and the per-book cost log and same-book cache
    belong to the server (S5b).
- [ ] **S5c. A local model first, or the player's own (Josh, 10/8).** The story model is used
  only while creating (imports, drafting adventures and systems in Create), never in play: NPCs
  answer from the adventure's written dialogue and the fight AI is the game's own code. The
  default is the best model the player's PC can run: Create looks at the machine (GPU memory,
  RAM), recommends a model that fits from a list kept as data (`assets/import/models.json`:
  name, size, memory needed, what it is good at), and talks to it through a local service
  (Ollama or llama.cpp's server, the same chat request `ChatModel` already sends). A player can
  instead give their own model: any OpenAI-compatible address and model name, local or
  hosted, with their own key kept on their machine. The server (S5b) becomes the fallback for a
  PC that can't run anything useful. Prompts get shorter for small models (the outline so far
  is trimmed first), and a run says how long it will take on this machine before it starts.
  UI: Settings > Story model (detected hardware, the recommended model and a Get button, or an
  address and name), and the same choice at the top of Create > Import.
- [ ] **S5b. Import on the server, metered.** Now the fallback (S5c). A `yorehold-server` module takes the request, queues
  it, runs it on the GPU backend and returns outline entries, so the game never holds a model
  key. Each account has an allowance (pages a month, data in the server's config) with a paid
  tier as a flag; the game shows what is left before a run and says plainly when it is used up.
  Only the book's text blocks and pictures are sent, never the user's other files, and the server
  keeps nothing after the reply but the outline and its cost. The OpenRouter key lives in the
  server's environment, never in the repo or the game. Blocked on an OpenRouter account and key,
  and the allowance and price, which are Josh's call; until then the module runs against the
  stand-in.
  - 10/7 night: the server module is written and its 76 checks pass (`yorehold-server`:
    `modules/imports.ts`, RPCs `story_allowance` and `story_ask`, config `imports`, the key as
    runtime env `STORY_MODEL_KEY`, README section Story import), but it is not committed: git
    commands in that repo were refused partway through. Left: commit it, then the game side (a
    story model that calls `story_ask` through `Online`, picked in Create > Import when signed in
    and no local model is set, showing the pages left).
- [x] **S6. Create > Import.** Pick a file, watch the stages, then the outline as a data panel:
  a tab per kind, the entry on the right as a book page with its source words and its picture
  (picked from the book's pictures), chips for invented, unplaced and dropped. Build writes the
  package and opens it in the other modes. `-- --import <file>` does the same without the
  screen for check runs.
  - Default: the review keeps or drops entries; changing one is done after the build in the
    other modes, which already edit every kind. Dropping a place drops what stands in it.
  - Default: the entry's picture is named on its page, not shown: the book page is text. Seeing
    the pictures comes with S7's picture layer.
  - Default: the read runs in the background while the screen shows its stage; an import read
    but not built is listed in Create as `import` and opens in the review again.
  - Default: `check.ps1 -Import <book>` adds `--import` to a screenshot run. Caves of Shadow with
    no model builds and opens with nothing wrong (`.dev\import-caves.png`).
  - Not done: picking the model and seeing its cost before a run; that waits for S5b.
- [x] **S7. The book's map under the editor.** Map mode shows the book's map picture under the
  cells, moved and sized by hand until its grid meets the editor's, so rooms are traced rather
  than guessed. Later: the server's model reads the grid, walls and doors off the picture and
  proposes the rooms, which the writer checks over the picture.
  - Default: the picture is a map's `trace` field, Create-only, drawn half see-through over the
    tiles (under them it would be hidden); a layer's painted picture would show in play.
  - Default: the layout's own reading goes further than tracing by hand: each place's number on
    the map gives it a spot (`mapAt`), the picture is taken as 60 squares across, places no link
    reaches go where the book draws them, and the trace is lined up with them. Caves of Shadow
    with no model comes out as its seven caves in the book's shape (`.dev\import-caves.png`).
  - Later, as written: the model reading the walls and doors off the picture.
- [x] **S8. Scoring an import.** (Josh, 10/7: in-game dialogue doesn't feel right, no pictures, walls
  wrong, no fights.) `ImportScore` counts each import part by part against what the book itself
  shows: pictures copied, the book's words in the game, numbered places, read-out passages, ways
  given, the map's shape (rooms left/right and over/under as the book's numbers lie) and quoted
  speech in conversations; a hand-written answer key per book (`ImportKey`) adds what only a reader
  knows (the fights and who is in them, the heroes, what people say, what is in each chest). Kept
  in `import/score.json`, shown on the review's first page, printed on build, and every build adds
  a line to `user://import-scores/history.jsonl` so a change is held against the runs before it.
  Described in CONTENT.md.
  - Caves of Shadow, with its key (`caves-of-shadow.key.json` in the scores folder): Josh's run
    from Create had **no story model** (`storyModel` empty in settings), so 31 of 100 and 7 of 42
    key lines: no fights, no Jeffries, no heroes, no hero pictures, no ways (all joined by
    corridors). The 10/7 run through the stand-in scored 82 and 40 of 42 (missing: the storeroom's
    and the den's things in chests). Both: rooms 8 by 8 boxes, walls not read from the map.
  - Default: the overall score is the plain mean of the counted parts; `words` counts rules and
    credits too, so it stays low and is read for its list of what's missing, not its number.
- [ ] **S9. What the scores show is missing**, each held against the Caves of Shadow key and history:
  - Done 10/7 (Josh: "it isn't generating any stat blocks or encounters or dialogue or populating
    the rooms"): `BookCast` reads them from the layout with no model. Heroes from a short name
    under a picture with a "Human Fighter" line; foes from framed boxes listing hit point boxes
    ("Orc #1: o o o o o o"), their "13 or better" to hit, "roll 1 die" damage and the weapon the
    text has them hold; a fight per place with such a box; talk from bullet questions each
    followed by a quoted answer, spoken by the person the text names ("name is Jeffries");
    finds under "Look in the Sack" / "Search the..." headings (gold and known things, potions by
    colour, a find on a foe goes on the foe); "go to Area 3" as ways, locked with a Dex check
    when the text around says so, a jump over a chasm; shaded passages before area 1 as the
	chapter's intro. Rooms with a number on the book's map are put where it draws them, joined
    by corridors. Caves of Shadow with no model: score 31 to 89, key 7 to 41 of 42 (orc 6 HP,
	hit on 13, spear 1d6; rat 5, 14, teeth; Jezer the ogre 18, 14, axe 2d6). The key's coins
	are gold now.
  - Done 10/7: a room's passage is narration (no speaker, no faces), not a line spoken by the
	room's name.
  - Done 10/7: rooms take the floor, walls and doors the book's map draws (`BookMap`); shape 35
	to 42 of 42. A room's foes that see the party from the doorway wait for its passage to be read.
  - Done 10/7 (Josh: "hill climb the pipeline with variations; need a way of judging it"):
	comparison runs, `check.ps1 -Bench`, with a `judged` score that also costs what was made up.
	Caves of Shadow: layout 32, cast 95, full 96.
  - Left: Create > Import says before reading that no story model is set, with Read anyway; a
	quest from the opening passage; more books with keys in the comparison folder.

### Bestiaries (Josh, 10/8: a monster book, many monsters, tokens that frame them well)

Tried on a scanned 2024-style monster book (390 pages, 28-page excerpt). What it showed: the
pages are full-page scans with a poor text layer, so the reader skips the only picture on each
page and reads scrambled words; there is no stat-block reader (without a model, foes come only
from fight boxes); and round tokens squashed the whole picture onto the disc. The book stays in
Josh's Downloads; nothing from it goes into a repo.

- [x] **S10. Tokens framed around the creature.** `token.focus` and `token.zoom` on a creature;
  tokens and cards cut a square or the card's shape around it instead of squashing (f95859c).
- [x] **S11. Scanned pages read again.** A page that is one full-page picture is read with the
  OCR built into Windows (a small helper program beside the game, since the game also builds for
  Android), as lines with their places, then laid out like any other page. Windows OCR on the
  excerpt reads names, AC, HP, speeds, the ability rows and actions nearly cleanly; the PDF's own
  text layer does not. Without the helper, scanned pages say they need it.
  - Done 10/8: `ocr/` (yorehold-ocr, built with the solution, kept out of the game project),
	`IPageReader`/`OcrHelper` in rules, scans kept as `import/scans/p<n>.jpg` with the page's `scan`
	field. Not yet run over the whole excerpt; shipping the helper with an export is open.
- [ ] **S12. Paintings cut out of scans, with a focus.** Prototype in `..\.dev\mm\art.py`: a block
  is art when more than 55% of its pixels are not paper (text is about a fifth ink); the focus is
  the largest part that stands out from the painting's own edges, a little above its middle.
  On the excerpt about 17 of 24 tokens land well; misses are crowd scenes and a stat block laid
  over the painting. Port to `rules/import`, set `token.focus`/`zoom` at build, and show the
  framing in Create so a writer can drag it (the canvas's Portrait crop screen).
- [ ] **S13. Stat blocks.** Both the 2014 and 2024 layouts, tolerant of OCR slips (l for 1, a
  for 4): name, size/type/alignment, AC, initiative, HP and its dice, speeds, the six abilities
  with saves, skills, resistances and immunities, senses, languages, CR, traits, actions, bonus
  actions, reactions, legendary actions. What the creature schema holds is filled in; the rest
  is kept as text on the creature and counted in the import report, so the gap is visible.
  - Needs a decision later: the creature format has no CR, saves, resistances, reactions or
	legendary actions yet; they are game rules, not import work.
  - Done 10/8: `StatBlockText` reads both layouts (name, size and type, AC, HP, speed,
    darkvision, the six abilities, resistances/immunities/vulnerabilities as `resist.`/`immune.`/
    `weak.` stats, CR as level, weapon attacks as natural-weapon items, Multiattack), tolerant of
    l/I/O slips; the rest stays in the creature's `text`. Reading without a model, every stat
    block on a page becomes a creature; built for a system with a `multiattack` action, it gets
    it. Left: saves, skills, senses beyond darkvision, traits, reactions and legendary actions as
    the system's own; running it over the whole excerpt.
- [ ] **S14. Which painting is whose.** A caption naming the creature wins; else the painting goes
  to the stat block or heading on its page it sits nearest; one painting over a family's page
  (Black Dragon Wyrmling, Young, Adult) goes to the one it names and is offered to the rest.
- [ ] **S15. The whole book through `-Bench`.** Creatures found against the book's CR lines,
  fields read, paintings found, and a sheet of every token to check by eye (`..\.dev\mm\`).

## U. Game feel (playtest, 10/7)

Josh's first ten playtest answers (`feedback/playtest-results.jsonl`): the screens read as lists
of small buttons, not a game. Layouts to copy are in `docs/UI_REFERENCE.md`. The 10/5 look rules
stay (one palette, Apollo since 10/7; flat, 1 px lines, dense data screens); what changes is that game-facing screens lead
with a large picture and a few big choices. Pictures come from art packs and content, never
drawn by the game. Each step ends with screenshots of the screens it touched and its playtest
reopened (`playtest.ps1 -Problems`).

- [x] **U1. Game screens kit.** One set of shared pieces for title, pause, lobby, creation and
  library: a full-window picture with an ink band over it, a column of large plain buttons
  (name and key), a heading face bigger than the data screens', a picture slot that takes art
  pack or content pictures and falls back to a palette fill. Sizes and spacing in `ui/` data.
  - Default: `BannerView` (pictures in `ui/banners/` from content and art packs, covering the
	window, one every `bannerSeconds`), `GameScreen.BigButton` (flat, 1 px line, name left and
	key or fact right), `GameScreen.Logo` (`ui/logo.png`), sizes in `ui/screens.json`. The band
	and the heading are laid out in the menu scene.
  - Default: banner pictures are shown as they are, not snapped to the palette like the map:
	they are the players' paintings. Say if they should be snapped too.
- [x] **U2. Title** (playtest `title`). A large logo top left, the menu as a column of big
  buttons down the left, the rest of the window a banner of player art: adventure covers and
  art pack pictures, changing every few seconds. Until there is a logo picture
  (`ui/logo.png`, content), the name is set large in the heading face.
  - Josh, 10/7: temporary logos are fine for now; real logo work is needed (the name set in
	type and `icon.svg`, the placeholder app icon, are both stand-ins until then).
  - Default: the picked button's page stays, as a card at the bottom right over the banner (the
	save it would continue, the adventure it would start). Adventure covers join the banner
	when adventures have covers (U8). Test banners: five CC0 paintings from OpenGameArt in the
	art pack `banners-cc0` (`.dev\title.png`).
- [x] **U3. Settings** (`settings`). Groups down the left (Display, Sound, Controls, Gameplay,
  Account), the group's options on the right with a one-line help under each, a search box.
  No file paths shown. Keys (`keys`) becomes the Controls group.
  - Default: groups are Display, Gameplay, Camera, Controls and Account; Sound comes with the
	first sound setting (the game has none yet). Each row's choices are chips on the row itself,
	pan speed a - and + stepper, keys Change and Shipped. The search looks through every group.
	Account's Sign-in row has Sign in again and Sync now (`.dev\p11-settings.png`, `p11-keys.png`).
- [x] **U4. Pause** (`pause`). The world stays drawn and dimmed, "Paused" large, a short column of
  big buttons (Resume, Save, Load, Settings, Leave). No list of small buttons.
  - Default: the world beside the band is drawn as it is, not dimmed: a see-through veil puts
	the map in colours off the palette, and the band already says the game waits. The buttons
	are the ones pause had (Resume, Settings, Load, Save and quit to title); saving on its own is
	F5 in play. No page card on pause (`.dev\playtest-pause.png`).
- [x] **U5. Credits** (`credits`). Off the title menu for now; the library list goes. Art pack
  sources stay in each pack's own folder.
  - Default: Credits is a small link at the foot of the title's band. The libraries inside the
	engine are no longer rows; their notices are one block on the engine's page, since their
	licences ask for them to ship with the game.
- [x] **U6. Lobby first** (`seats`). New adventure opens a lobby: the adventure's picture, its
  seats as player slots, each player picking an existing character or "make one when we start".
  Start goes into the adventure and character creation opens there for every seat that asked to
  make one. A player joining later lands in the lobby's pick. Local seats now; the same lobby
  takes online players when play goes online.
  - Default: every seat of a new adventure starts on "Make one when we start" (Josh, 10/7: by
	default characters are made after the lobby). Start runs creation for those seats one after
	another over the loaded chapter, then starts it with them; Escape in creation goes back to
	the lobby. A seat can also take the ready-made hero or one of the player's characters.
  - Not done: the adventure's picture (adventures have no cover yet; U8), and the lobby still
	has the character screen's look rather than the band (`.dev\p7-seats.png`). Every seat says
	"you" until seats have owners online.
- [x] **U7. Character creation visuals** (`character-creation`). The hero's picture large in the
  middle (from art packs by race and class, or picked from the player's own pictures), the steps
  down one side, what each pick gives shown beside it, as in BG3.
  - Default: a 260 px square picture between the steps and the sheet, with the name and "Level 1
	Dwarf Fighter" under it, in creation and the lobby. It is `portraits/<race>-<class>.png`,
	else `<class>.png`, else `<race>.png`, from content or art packs; else the disc and initial.
	What each pick gives is the sheet on the right, rebuilt after every click (`.dev\p7-scores.png`).
  - Not done: picking a picture of the player's own for a character; that needs a picture field
	on character files (format change) and a way to choose one.
- [x] **U8. Library** (`library`). A row of adventure covers in sections (new, for new players,
  by world) above a list left and the chosen adventure's page right: cover, blurb, chapters,
  length, made by.
  - Done as New adventure's page: every adventure on the machine (`AdventureLibrary`: the game's,
	each package in the create folder made there, and imported books) in a list with a tab for
	each, the picked one's page on the right and its `cover` (new in `adventure.json`) filling the
	window behind, as osu does. Start opens that adventure's lobby with its package over the
	game's content. An import's cover is the first of the book's pictures it uses
	(`.dev\p11-adventures.png`).
  - Default: covers fill the background rather than a row of small tiles; sections are the tabs
	(the game's, made here, imported), since there are no worlds or "new" marks yet.
  - Saves remember their package (`package` in the save's data); Continue and Load play from it
	again, and a save whose package was deleted says so on the load screen.
  - Not done: the playtest's `library` answer was about the character library, which is the
	data panel it was.
- [x] **U9. Hotbar and fight bar** (`first-look`). Josh, 10/7: the BG3 look is fine now. Two
  changes: the actions every hero always has (strike, stride, defend, help, hide, seek, shove,
  grapple, interact, ready) sit in their own group apart from spells and items; and as in BG3
  every action is picked first and then aimed, including the ones on yourself, so Defend or
  Stride (dash) is a press then a click on your own hero (or the key again), never one click
  that acts at once. Escape or a right click puts it back.
  - Default: the first bar is the actions every hero has (general, needing nothing), the second
	the hero's spells and class actions; each spills into the other when full. Only actions
	placed from now on follow this: a hotbar the player already arranged (in a save or a
	character file) keeps its slots (`.dev\playtest-spell-fight.png`).
  - Default: an action on oneself, picked, waits like any other; a click on the hero or its
	key again uses it, a click anywhere else puts it away.
- [x] **U10. Art pack coverage.** Mostly done 10/7 while checking for generated art: objects
  now show `objects/<name>.png` (CONTENT.md), the code-drawn icon shapes and object drawings are
  gone (plain blocks and letters remain as fallbacks), the old client's 13 UI skin pictures are
  deleted, and the test packs hold Dungeon Crawl objects (CC0) and game-icons.net icons (CC BY
  3.0) for every action and spell but `gathered-mending`. Left: `tiles/wood.png`, `objects/lever.png`
  and `objects/sack.png` in the test pack, and a check run that looks for any picture in the repo.
  - Done: `tiles/wood.png` and `objects/sack.png` (a gold pile) in the test pack, built by
	`.dev\make-art-pack.py`; `PaletteTests.TheGameShipsNoPicturesOfItsOwn` fails on any picture
	under `assets/`. Left plain: the lever (the Dungeon Crawl set has none) and Gathered
	Mending's icon.
- [x] **U12. Friendlier everywhere (Josh, 10/7: "go through the entire UI and make it more user
  friendly").** All 50 playtest screens shot (`check.ps1 -Playtest all`) and gone through. Fixed
  across every list screen (load, gear, chests, shop, spells, journal, camp, characters, Create):
  - the panel's main button is filled (Load, Start, Take all, New character), a double click on
	a row does it, and a greyed main button says why under it (Buy with no coins) and on hover;
  - search and filter chips only on lists of 8 or more; the "N entries" count only when a filter
	hides rows; no second Close button.
  - No file paths, ids or revision numbers on Load or Create's list; "Flags set" gone from saves;
	the newest save first; Load's tabs and chips only when there are backups or broken files.
  - Keys read as a player says them ("=", "Numpad +", not "Equal", "Kp Add"); "Shipped" is
	"Default".
  - Play: the chapter name and news ("Combat", a quest) are one large line centred at the top,
	not a second title over the party; the side menu steps back during a conversation like the
	party and log do; dice working in the log is faint so "Alice initiative 20" reads first.
  - Camp: rows say what each choice does ("heals all HP, at camp only") in place of a kind column.
  - Create: the problems column only shows when there is a problem.
- [ ] **U13. What the walk-through found that needs more than a fix:**
  - Done 10/7: a "How to play" card the first time play opens (`shownHelp` in settings) and on
	F1 (`help` in `ui/keys.json`), with the keys bound now; Esc or Got it puts it away. Screenshot
	and playtest runs don't open it by themselves (`.dev\help-card.png`).
  - Done 10/7: the play menu is two columns of small buttons right of the hotbar, as in BG3
	(panels, Characters, Save, and Menu for the pause list); Load is F9 and on the pause list. The
	top right is the map's again (`.dev\playtest-fight.png`, `playtest-keep.png`).
  - Done 10/7: "Alice's turn" is in the turn order strip, after the cards. The log is as tall as
	its lines, up to its old height (`.dev\playtest-first-look.png`).
  - Gear, chest, shop and loot are one panel with Pack and the other side as tabs; a chest or
	body would read better as two lists side by side (yours, theirs) with Take and Give between.
  - Done 10/7: a companion's journal page says how they feel and when they would join or leave
    in words, the numbers in small print under it.
  - Done 10/7: Create's Dialogue and Encounters say what each field is for in plain words
	("Conversation: tobb", "Lines", "Who says it", "goes to freed", "Story flags it sets when won,
	for a door or a conversation to wait for", "Fights" for groups). Map, Compendium, Cutscene
	and Story still use some engine words; they get the same pass when next touched.
  - Done 10/7: the character library opened from the title has no Lobby tab.
- [ ] **U11. The other 40 playtests.** Josh answers the rest of the queue once U1-U9 are in;
  their answers become steps here.
- [ ] **U14. Pictures found by tags (Josh, 10/7: "match everything in the game with tags so
  player-made content is easy to reuse").** A writer never picks a picture to get something
  playable: creatures, heroes, tiles, objects, icons, banners and map pieces get one from the
  installed art packs by what they are.
  - Art packs gain an optional `tags.json`: picture path to tags (`"portraits/old-knight.png":
	["humanoid", "human", "knight", "old", "armoured"]`). A pack without one is tagged from its
	paths and file names (`portraits/goblin-archer.png` is `portrait`, `goblin`, `archer`).
  - Content already says what a thing is (a creature's kind, race, class, a tile's ground, an
	object's kit, a spell's school and damage type, its `tags`); `rules/` turns that into the
	tags to look for. Lookup order: the exact path as now, then the picture with the most shared
	tags of the right kind (portrait, tile, icon, ...), then the plain fill, disc or letter.
  - Same thing, same picture: the choice is stable (ties broken by path), so a goblin looks the
	same every session and on every player's machine with the same packs. Content can pin one
	with `image` and the tag match never overrides it.
  - Create shows the matched picture with "picked by tags: goblin, archer" and a Change list of
	the next best matches; picking one writes `image`. A tag vocabulary file in `ui/` (or content)
	lists the shared words so packs and content use the same ones.
  - Checks: `rules/` tests for the match order and stability; a run over the test packs that
	reports content left on a fallback.
  - Default: tags are lower case single words or hyphenated; matching is plain counting with
	the kind required, no weights, until a playtest says otherwise.
