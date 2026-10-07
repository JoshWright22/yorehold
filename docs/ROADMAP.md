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
- [ ] **S4. What needs no model.** A picture goes to the name of the nearest heading under or
  over it in its column. A picture with numbers printed on it that match numbered headings is
  the map, and the numbers give each place its spot on it. A plain paper ground around a figure
  is made see-through (off by a switch). Source system tables in `import/systems/`.
- [ ] **S5. The story model.** `IStoryModel` with one request shape: a chunk of `source.json`
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
- [ ] **S5b. Import on the server, metered.** A `yorehold-server` module takes the request, queues
  it, runs it on the GPU backend and returns outline entries, so the game never holds a model
  key. Each account has an allowance (pages a month, data in the server's config) with a paid
  tier as a flag; the game shows what is left before a run and says plainly when it is used up.
  Only the book's text blocks and pictures are sent, never the user's other files, and the server
  keeps nothing after the reply but the outline and its cost. The OpenRouter key lives in the
  server's environment, never in the repo or the game. Blocked on an OpenRouter account and key,
  and the allowance and price, which are Josh's call; until then the module runs against the
  stand-in.
- [ ] **S6. Create > Import.** Pick a file, watch the stages, then the outline as a data panel:
  a tab per kind, the entry on the right as a book page with its source words and its picture
  (picked from the book's pictures), chips for invented, unplaced and dropped. Build writes the
  package and opens it in the other modes. `-- --import <file>` does the same without the
  screen for check runs.
- [ ] **S7. The book's map under the editor.** Map mode shows the book's map picture under the
  cells, moved and sized by hand until its grid meets the editor's, so rooms are traced rather
  than guessed. Later: the server's model reads the grid, walls and doors off the picture and
  proposes the rooms, which the writer checks over the picture.

## U. Game feel (playtest, 10/7)

Josh's first ten playtest answers (`feedback/playtest-results.jsonl`): the screens read as lists
of small buttons, not a game. Layouts to copy are in `docs/UI_REFERENCE.md`. The 10/5 look rules
stay (one palette, Apollo since 10/7; flat, 1 px lines, dense data screens); what changes is that game-facing screens lead
with a large picture and a few big choices. Pictures come from art packs and content, never
drawn by the game. Each step ends with screenshots of the screens it touched and its playtest
reopened (`playtest.ps1 -Problems`).

- [ ] **U1. Game screens kit.** One set of shared pieces for title, pause, lobby, creation and
  library: a full-window picture with an ink band over it, a column of large plain buttons
  (name and key), a heading face bigger than the data screens', a picture slot that takes art
  pack or content pictures and falls back to a palette fill. Sizes and spacing in `ui/` data.
- [ ] **U2. Title** (playtest `title`). A large logo top left, the menu as a column of big
  buttons down the left, the rest of the window a banner of player art: adventure covers and
  art pack pictures, changing every few seconds. Until there is a logo picture
  (`ui/logo.png`, content), the name is set large in the heading face.
  - Josh, 10/7: temporary logos are fine for now; real logo work is needed (the name set in
    type and `icon.svg`, the placeholder app icon, are both stand-ins until then).
- [ ] **U3. Settings** (`settings`). Groups down the left (Display, Sound, Controls, Gameplay,
  Account), the group's options on the right with a one-line help under each, a search box.
  No file paths shown. Keys (`keys`) becomes the Controls group.
- [ ] **U4. Pause** (`pause`). The world stays drawn and dimmed, "Paused" large, a short column of
  big buttons (Resume, Save, Load, Settings, Leave). No list of small buttons.
- [ ] **U5. Credits** (`credits`). Off the title menu for now; the library list goes. Art pack
  sources stay in each pack's own folder.
- [ ] **U6. Lobby first** (`seats`). New adventure opens a lobby: the adventure's picture, its
  seats as player slots, each player picking an existing character or "make one when we start".
  Start goes into the adventure and character creation opens there for every seat that asked to
  make one. A player joining later lands in the lobby's pick. Local seats now; the same lobby
  takes online players when play goes online.
- [ ] **U7. Character creation visuals** (`character-creation`). The hero's picture large in the
  middle (from art packs by race and class, or picked from the player's own pictures), the steps
  down one side, what each pick gives shown beside it, as in BG3.
- [ ] **U8. Library** (`library`). A row of adventure covers in sections (new, for new players,
  by world) above a list left and the chosen adventure's page right: cover, blurb, chapters,
  length, made by.
- [ ] **U9. Hotbar and fight bar** (`first-look`). Josh, 10/7: the BG3 look is fine now. Two
  changes: the actions every hero always has (strike, stride, defend, help, hide, seek, shove,
  grapple, interact, ready) sit in their own group apart from spells and items; and as in BG3
  every action is picked first and then aimed, including the ones on yourself, so Defend or
  Stride (dash) is a press then a click on your own hero (or the key again), never one click
  that acts at once. Escape or a right click puts it back.
- [ ] **U10. Art pack coverage.** Mostly done 10/7 while checking for generated art: objects
  now show `objects/<name>.png` (CONTENT.md), the code-drawn icon shapes and object drawings are
  gone (plain blocks and letters remain as fallbacks), the old client's 13 UI skin pictures are
  deleted, and the test packs hold Dungeon Crawl objects (CC0) and game-icons.net icons (CC BY
  3.0) for every action and spell but `gathered-mending`. Left: `tiles/wood.png`, `objects/lever.png`
  and `objects/sack.png` in the test pack, and a check run that looks for any picture in the repo.
- [ ] **U11. The other 40 playtests.** Josh answers the rest of the queue once U1-U9 are in;
  their answers become steps here.
