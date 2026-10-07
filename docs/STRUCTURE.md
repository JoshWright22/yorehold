# Structure

## Folders

| Folder | What is in it |
|---|---|
| `rules/` | Plain C# with no Godot types: content loading, rules, World, AI, saves. Its own project (`Yorehold.Rules.csproj`). |
| `rules/content/` | One type per content kind (ruleset, conditions, effects, actions, classes, creatures, maps, chapters, voice lines...) and the code that reads it from JSON. |
| `rules/core/` | The rules themselves: `Rng` and `Dice`, `Checks`, `StatBlock` and `CharacterSheet` (modifiers, proficiency, conditions, gear and hands, spells known), `Item` (one inventory entry), `Coins`, `Loot` (rolling loot tables, stacking what is taken), `Merchant` (a shop's purse, prices and stock), `EffectHost` and the effect runner, `Grid`, `Sight`, `Positioning`, `SaveFormat` (the versioned file envelope), `CharacterSheetJson` (a sheet as a save writes it), `Companions` (who has been met, approval, who is in the party) and `Stash` (the chest at camp and its supplies). |
| `rules/characters/` | Characters as choices: `CharacterChoices` (the file), `CharacterBuild` (choices to a sheet), `CharacterDraft` (making one or adding a level, step by step) and `CharacterLibrary` (the player's files and the graveyard). |
| `rules/fight/` | A fight on its own, with no map: `Encounter` (initiative, rounds, shared turn blocks, each combatant's actions, movement and reaction, death saves, its log) and `Tactics` (the AI scorer: who to hit, where to stand, when to run or give up). |
| `rules/world/` | An adventure in play: `World` (party, creatures, flags, triggers, objects in use, sight and sneaking), `WorldCharacters` (who takes each seat, levels from XP, the copy that goes back to the library), `MapState` (walls, roofs and objects as they are now), `Paths`, `TokenMover`, `FogOfWar`, `LightLevels` and `Stealth`. Fights are the `World` files beside it: `WorldFight` (starting, turns, ending, the AI's turn), `WorldActions` (the `actions/` files and what a screen asks before using one), `WorldReactions` (moving and what it sets off), `WorldPositioning` (flanking and cover) and `WorldAi` (profiles and what the AI sees). `WorldItems` is what is carried: piles to take from (containers, chests, what the dead leave), giving, gear on and off, using things up and shops. The adventure around the chapter is `WorldTalk` (conversations through a `DialogueSession`, companions joining, leaving and approving, picking a fight with someone), `WorldTravel` (exit markers into the adventure's next chapter, camp and back, rests, the stash, revival), `WorldSave` (the save's data, restoring it, the checkpoint a wiped party returns to) and `CutsceneRun` (a cutscene's timing: camera, bars, fade, captions). |
| `rules/app/` | What the game keeps outside an adventure: `GameSettings` (the settings file), `KeyBindings` (the actions in `ui/keys.json` and the keys the player gave them), `SaveSummary` (what a save holds, read without loading it) and `Credits` (`ui/credits.json`). |
| `rules/online/` | The account server (`../yorehold-server`, Nakama): `IAccountServer` (who is signed in and a call to one of the server's functions), `Online` (device sign-in, the version check, RPCs and the server's config, over an `IWebTransport` whose answers are handed back when the game polls; `HttpTransport` is the real one) and `AccountSync` (saves and characters kept the same as the account's copies). Tests put stand-ins behind both interfaces. |
| `rules/create/` | Create with no screen: `History` (one undo history for the open package), `MapEditor` (a chapter's map and the commands that change it), `EncountersEditor` (a chapter's encounter groups), `DialogueEditor` (one conversation file), `Form` (the framework's form schema: fields read from a forms file, typed text to values, what is wrong with a value), `CompendiumEditor` (every definition file of the package through those forms), `CutsceneEditor` (one cutscene file, its timing and the frame at any moment), `CutsceneHooks` (where a chapter.json plays its cutscenes), `StoryEditor` (the package's story.json graph and its suggestions), `VoiceWords` (heard tokens to words, lined up with the written line), `VoiceAudio` (a WAV recording as 16 kHz mono), `VoiceTranscriber` (what listens, behind `IVoiceTranscriber`), `VoiceImporter` (the voice files of the conversations) and `VoiceImport` (one import off the main thread), `CreatePackage` (opening and making packages, the chapter worked on, saving, the problems list; each mode's files are a part of it in its own file, like `CreateDialogues`) and `CreateJson` (how it writes files). |
| `tests/` | xunit tests for `rules/`, plus the content check that loads every JSON file under `assets/` into its type. `WorldFixture` builds a World from a chapter folder, from files written in the test, or from a few map rows (`WorldFixture.Small`). |
| `tests/visual/scripts/` | Input scripts for screenshot runs. |
| `src/` | The Godot side: drawing, input and UI. Calls into `rules/`, never the other way. `src/hud/` is the panels, `src/characters/` the character screens, `src/menus/` the title and the screens behind it, `src/create/` Create. |
| `scenes/` | Godot scenes. `Main.tscn` is the start scene: the menus, and under them the screen the title started. `scenes/hud/` is the panels and their theme, `scenes/characters/` the character screens, `scenes/menus/` the menus, `scenes/create/` Create. |
| `assets/` | Content as JSON and images, same formats as the C++ client (`docs/CONTENT.md`). |
| `docs/` | `ROADMAP.md` (order of work), `CONTENT.md` (the file formats) and this file. |

`Yorehold.csproj` is the game project and leaves `rules/` and `tests/` out of its own build.

## Starting and the menus

`Main` (`src/Main.cs`) owns the flow. It loads the settings and keys (`App`), shows the title and starts what
the title asks for: `PlayScreen.tscn` is made when a game starts and freed on the way back to the title. The
game waits (its process mode is off) while a menu is over it. After `--` on the command line `--screen play`
goes straight into the game, as does `--chapter`; with neither the game starts on the title.

`scenes/menus/MenuScreen.tscn` with `src/menus/MenuScreen.cs` is every menu, on a canvas layer above the game:

| Page | Is |
|---|---|
| Title | a contents page: the entries down the left with a fact beside each (the save's chapter, how many saves), and the picked one on the right as a book page. Continue, New adventure (the seats first), Quick start (the ready-made party), Load, Characters, Create, Settings, Credits, Exit. Up, Down and Enter work it too |
| Pause | the same page over a game, from Escape with no panel open: Resume, Settings, Load, Save and quit to title |
| Load (`LoadPanel`) | a `DataPanel` of the files in the saves folder, backups too, with the picked save's chapter and party. Load (or Enter) and Delete, which has to be pressed twice. A file that can't be read is listed with why |
| Settings (`SettingsPanel`) | a `DataPanel` of every setting and every key. The picked one is explained on its page with the buttons that change it. Change key waits for the next key; a key another action had moves over and the page says so. Changes are applied and written as they are made. The Account tab has the server, its key (both taken from the clipboard, as the panels have no text boxes) and Sign-in, with the status, the last sync pass, Sign in again and Sync now |
| Credits (`CreditsPanel`) | a `DataPanel` of `ui/credits.json`, the engine with its MIT licence text and every library inside it, asked of the engine itself |

`App` keeps the settings (`settings.json` in the user folder, `Places.SettingsFile`) and the key bindings, and
turns the bindings into input map actions under their ids, so the game asks `App.Pressed(event, "sheet")` and
the camera reads `pan_left`. Only an action's keys are replaced; the gamepad bindings in `project.godot` stay.
`App` also holds `Online` and `AccountSync`: it signs in when the game starts or the server setting changes,
`Main` steps both every frame, and whatever writes a save or a character calls `App.FilesWritten` for a pass.
The title shows the sign-in status bottom right, and the play screen puts the server's AI config on its World.
`App.Changed` tells the play screen, which puts lighting, time of day, the shared view and reaction prompts on
the `World` and pan speed, zoom to the pointer, edge panning and following on the camera.

## The play screen

`scenes/PlayScreen.tscn` with `src/PlayScreen.cs` loads a chapter into a `World`, steps it each frame and
turns taps into `World` calls. It draws through its children, one script each:

| Node | Does |
|---|---|
| `Map` (`MapView`) | a layer's painted picture, tiles from `TileArt` (the creator's tile pictures, else plain CC-29 fills) and grid lines, in blocks of 8 by 8 cells |
| `Objects` (`ObjectsView`) | doors, levers, chests, sacks the dead left, found traps and lamp flames |
| `Tokens` (`TokensView`) | one `Token.tscn` (`TokenView`) per creature, and the heroes' paths |
| `Camera` (`PlayCamera`) | pan and zoom from keys, wheel, right or middle drags, finger drags and pinches; a left click, or a tap that isn't a drag, comes out as `Tapped` and leaves the view where it is |
| `LightMap` (SubViewport) and `Lighting` (`LightingView`) | the light map: white ground under the ambient colour, `Light.tscn` lamps and carried lights, walls as occluders |
| `Shading/LightMap` | the light map over the world (`scenes/lighting.gdshader`): each colour steps down a ramp of darker CC-29 colours per band of lost light, ending in 352b42 or 212123, so shadow never goes muddy or black |
| `Overlay/Fog` (`FogView`), `Overlay/Floaters` | fog of war from the party's view, words that float up (damage big and red, healing green, a miss pale) |
| `Overlay/FightGround` (`FightGroundView`) | on a hero's turn: the squares in reach, the path a click would walk, a picked action's range and area |
| `Overlay/TokenBars` (`TokenBarsView`) | in a fight: HP bars over tokens, a ring on whose turn it is and on who can be targeted |
| `Palette/Snap` | snaps everything under it to the nearest CC-29 colour (`scenes/palette.gdshader`), so fog, the fight overlay and text edges on the map stay in the palette. Create's map has the same pass |
| `Fight` (`FightControl`) | the player's side of a fight: the picked action, what the pointer is over (`FightAim`), taps and keys into `World` calls |
| `Hud` | chapter title, banner, and `Panels` (`scenes/hud/PlayHud.tscn`) |
| `Hud/Cutscene` (`CutsceneView`) | plays a cutscene from `CutsceneRun`: steers the camera, draws the bars, fade, captions and titles with the panels hidden. A click, Space, Enter or Escape skips it |
| `Characters` (`CharacterScreen`) | the character screens over everything; the world waits while they are up |

The camera's input actions (`pan_left`, `pan_right`, `pan_up`, `pan_down`, `zoom_in`, `zoom_out`, `recenter`)
are in `project.godot` for the gamepad; their keys, and the keys for the panels, End turn, Save and Load, come
from `ui/keys.json` and the settings. `--chapter chapters/goblin-keep` after `--` plays another chapter than the
scene's, and `--seed 7` another run of the dice.

The World says when a save is due (after a fight, a rest, a door, a conversation, travel, camp) with a `Save`
event and the play screen writes it to `saves/adventure.json` in Godot's user folder (`Places.SaveFile`); F5
writes one too and F9 loads it. When the World goes to another chapter (travel, camp, a load) it sends
`ChapterChanged` or `Resumed` and the play screen builds the map views again.
`Yorehold.slnx` builds all three. Scratch files, logs and screenshots go in `../.dev/`, never in the repo.

## The panels

`scenes/hud/PlayHud.tscn` with `src/hud/PlayHud.cs` is every panel over the map, laid out with containers and
anchors and styled by `scenes/hud/hud-theme.tres` (flat opaque panels, a 1 px leather trim, corners of 2 px at
most, no shadows; the type variations in it are the bars, frames, tabs, chips, rows, the book page and label
styles). Headings and column titles are small capitals in the book face, entry text is the book face (Georgia,
or the system serif) and numbers the monospace one (Consolas, or the system monospace); both are system fonts,
so nothing is shipped for them. Every colour in the theme and in the panels' own drawing is from the CC-29
palette, named in `src/hud/Palette.cs`, and nothing is see-through: a greyed thing uses `GreyButton` or slate,
never a faded alpha. `PaletteTests` reads the scenes, the theme, the shaders and `src/` and fails on any other
colour. `PlayScreen` hands it the `World` and the `FightAim` each frame. It
only shows them and raises an event when something is pressed; `FightControl` does the acting.

| Part | Is |
|---|---|
| `Party` | a `PartyCard.tscn` per hero down the left edge: portrait, HP, conditions as small badges, a gold frame on whose turn it is (or who is selected between fights) and a green one on who can take the shared turn. Pressing one selects that hero |
| `Top` | the turn order in a fight: an `InitiativeCard.tscn` each from the block whose turn it is, the current one bigger, allies who share a turn together with a gap before the next block, the dead left out |
| `Bottom` | in a fight: the acting hero's portrait and HP, the hotbar (action, reaction and bonus pips, the movement bar, an `ActionSlot.tscn` per action with its key) and End Turn |
| `Log` (`LogPanel.tscn`) | the log, bottom right; its header folds it |
| `Reaction` | use it or pass, with the time left, when a hero's reaction is offered |
| `Defeat` | the chapter's defeat text once the party is wiped, with Back to the autosave under it |
| `Talk` | the conversation, bottom left of the log: who speaks, the line, a numbered button per reply (1 to 9 pick them, Escape walks away) and Trade (T) under a merchant's. It grows up from the bottom edge to fit |
| `Journal` (`DataPanel.tscn`, filled by `JournalPanel`) | the journal (J): the chapter's quests with their objectives ticked, and the companions met with their approval |
| `Camp` (`DataPanel.tscn`, filled by `CampPanel`) | rest and camp (R): make or break camp, the ruleset's rests with what each costs and has left, and at camp the stash, the hero's pack and the dead who can be brought back. What is pressed goes out as a `CampOrder` |
| `Tip`, `Cursor` | the tooltip, and the words at the pointer (chance to hit, what a move costs) |
| `Menu` | the buttons along the top right: Characters, one per panel (Sheet, C; Gear, I; Spells, K; Journal, J; Camp, R), then Save (F5) and Load (F9). A panel's key or button opens it and again closes it, so does Escape between fights; one is open at a time |
| `Sheet` (`DataPanel.tscn`, filled by `SheetPanel`) | the sheet panel (C): the hero's abilities, skills, feats, uses and conditions as rows by type, with their stat block (`SheetPage`) on the right and the picked row spelled out under it. The heroes are buttons in its head. It shows the acting hero in a fight and the selected one between fights |
| `Gear` (`DataPanel.tscn`, filled by `GearPanel`) | the gear panel (I): the hero's pack, or a pile or shop beside them, by kind of item, with the picked item's page and what can be done with it (put on, use, give, take, buy, sell). What is pressed goes out as an `ItemOrder` and `PlayScreen` does it |

`DataPanel` is the data screens' shared look: a tab bar, search box and filter chips over tight rows that sort
by any column header, and the picked row's entry on the right as a book page (`BookPage` writes its bbcode)
with buttons under it. Its owner fills it every frame; it only rebuilds what changed.

## Create

`scenes/create/CreateScreen.tscn` with `src/create/CreateScreen.cs` is the editor, from Create on the title (or
`-- --screen create`). It is a layout over a `CreatePackage` and holds no rules of its own.

| Part | Is |
|---|---|
| `Start` (`DataPanel.tscn`) | with nothing open: the packages made here (`create/` in the user folder, `Places.CreateFolder`), the last one opened and the game's own content, the picked one's page, and Open, New adventure, Back to title |
| `Editor/Top` | the mode tabs, the package's name and the chapter being worked on (a click goes to the next) |
| `Editor/Body/Modes/Map` (`MapModePanel`) | the tools on the left, the map in the middle, floors, layers, size and the tool's settings on the right |
| `Editor/Body/Modes/Encounters` (`EncountersModePanel`) | Select and Place and the groups on the left, the map in the middle, the picked group, creature or the creatures to place on the right |
| `Editor/Body/Modes/Dialogue` (`DialogueModePanel`) | the chapter's conversation files along the top with Voice and New conversation, the nodes on the left, the picked node's line and replies in the middle, the picked reply or the node's own flags and actions on the right. Voice swaps the three for `VoicePanel`: the nodes with how far each one's voice is, and the picked one's recording, buttons, words along it (`VoiceStrip`) and a table of the words |
| `Editor/Body/Modes/Compendium` (`CompendiumModePanel`) | a `DataPanel` of the package's definitions with a tab per kind, and the picked one's form (built from its `FormSchema`) in place of the book page, with New id, Add and Copy |
| `Editor/Body/Modes/Cutscene` (`CutsceneModePanel`) | the chapter's cutscene files along the top with New cutscene, the steps on the left, the preview (an `EditorMapView` the panel steers), the timeline (`CutsceneTimeline`) and when the file plays in the middle, the picked step on the right |
| `Editor/Body/Modes/Story` (`StoryModePanel`) | new nodes, the node list and the suggestions on the left, the graph (`StoryGraphView`) in the middle, the picked node or link on the right |
| `Editor/Body/Problems` | `CreatePackage.Problems`, looked at once a second: errors in red stop a save, the rest are worth a look |
| `Editor/Bar` | Undo, Redo, Save, Playtest, Close and what was done last |

Both modes draw the map with `EditorMapView`: one floor's tiles from `TileArt` with the floor below dimmed,
the grid, objects outlined by kind and the edges that block sight. The wheel zooms, the middle button and
the arrow keys move it, and left and right presses on a cell go to the mode, which draws its own marks over
it. Their side columns are `ToolColumn`s, made again only when what they list changes, so a text box keeps
its focus while it is typed in. Colours from content (creature tokens, lights) are drawn as the nearest
CC-29 colour (`Palette.Nearest`).

Playtest saves first, then `Main` hides Create and starts a `PlayScreen` with `Playtest` on and the package
laid over the game's content; Escape frees it and shows Create again as it was.

## The character screens

`scenes/characters/CharacterScreen.tscn` with `src/characters/CharacterScreen.cs`, opened by Characters on the
menu. Characters is the library as a `DataPanel` (`Library`): every character a row, tabs for those ready, away
and in the graveyard, a chip for who can level up, the picked one's stat block on the right with New character
and Level up (when the XP is there) under it. The other views have the left side made again after every click
from `CharacterDraft`, and on the right a `SheetView` (the same stat block) of whatever is picked. New adventure has one seat per hero the chapter places: each holds its
ready-made hero until a library character or a new one takes it, and Start (or Enter) plays the chapter again
with them. Making a character has three steps, Origin, Class and scores, Skills and feats; levelling up only
the class and the last. The library folder is `characters/` in Godot's user folder (`Places`).

Cards and slots are `TipButton`s: the pointer over one shows its tooltip, and so does holding it down, which
is how a touch screen gets one. A greyed slot can still be pressed and then says why it is greyed. Nothing
needs a hover: on a touch screen the first tap on a square shows the path or the chance to hit and the second
tap does it.

In a fight: a click on a square in reach walks there, a click on an enemy strikes it (walking up first), the
number keys or a slot pick an action and the next click aims it, a right click or Escape puts it away, Space
ends the turn. Portraits are the token's disc and action icons are plain shapes (`ActionIcon`) until there is
art; `assets/ui/action-icons.json` says which shape each action id gets.

## Checking

`check.ps1` builds, runs the tests and starts the game headless, once on the title, once straight in the
game and once in Create. `ALL OK` and exit code 0 means good.

`check.ps1 -Shot name.png -Frames 120 -Script tests\visual\scripts\smoke.txt` also runs the game in a
window kept off screen, plays the script and saves `../.dev/name.png`. Look at the picture for anything drawn.
`-Chapter chapters\goblin-keep` plays another chapter in that run. A run goes straight into the game;
`-Screen title` starts it on the title.

Input scripts have one step per line: a frame number, a command and its words. Lines that do not
start with a number are skipped, so `#` notes work.

| Command | Does |
|---|---|
| `move X Y` | moves the mouse, in viewport pixels (1280 by 720) |
| `down left` / `up left` | mouse button, also `right` and `middle` |
| `wheel N` | scrolls, positive is up |
| `key Name` / `keyup Name` | a key by name, underscores for spaces (`Left_Ctrl`). SDL names like `Return` work |
| `text some words` | types the rest of the line |
| `shot .dev/name.png` | saves an extra picture on that frame, path from the workspace folder |
| `cell X Y` | moves the mouse to the middle of map cell X, Y wherever the camera is; in Create, on the editor's map |
| `creature Name` | moves the mouse onto the token with that name, wherever it stands (underscores for spaces: `Goblin_1`) |
| `button Words` | moves the mouse onto the first button on screen whose text starts with the words (`New_character`), a text box by the words it shows when empty, or a data panel's row by its first cell (`Healing`) |
| `tap` | one finger down and up where the mouse is, the way a touch screen clicks |
| `pinch F` | two fingers either side of the mouse move apart by F (below 1 pinches in) and lift |

The game reads these after `--` on the command line: `--shot file`, `--frames N`, `--script file`
(`src/ShotRunner.cs`).

A screenshot run prints every log line with the frame it came on (`../.dev/godot-shot.log`), which is how to
time a script against a fight. The camera goes to whoever's turn it is, so a click on a cell should wait
until it has settled. `tests/visual/scripts/fight.txt` plays chapter one's fight from the door to the last
goblin with keys and clicks only (440 frames), and `fight-hud.txt` goes through the tooltips, cancelling,
the log and a touch tap (570 frames). `characters.txt` opens the sheet, makes a character and starts the
chapter with them (280 frames). `loot.txt` opens the gear panel and empties the chest in chapter one's corner
(290 frames), `loot-fight.txt` wins the fight and takes what the goblin left (650 frames), and `shop.txt`
talks to Wren, then sells and buys at her shop in the goblin keep (270 frames, `-Chapter chapters\goblin-keep`).
`talk.txt` talks Tamsin round, asks her along and opens the journal (230 frames), `camp.txt` makes camp, uses
the stash and breaks camp (270 frames), `travel.txt` saves, wins the fight, goes on into chapter two and loads
(690 frames), and `scene.txt` sits through the trigger test's opening cutscene and conversation (620 frames,
`-Chapter chapters\trigger-test`). `title.txt` goes through the title, settings (pan speed, a key taken from
another action), credits, a quick start, the pause list, save and quit, and loads the save from the load
screen (320 frames, `-Screen title`); `title-party.txt` opens New adventure from the title and backs out (80
frames, `-Screen title`). `create.txt` makes a new adventure in Create, paints walls, a stone floor, a light
and a door, places two goblins, saves, playtests and comes back (470 frames, `-Screen create`), and
`create-keep.txt` opens the game's own goblin keep in both modes (150 frames, `-Screen create`).
`create-dialogue.txt` opens Tobb's conversation in Dialogue mode, picks a reply with a check and starts a new
conversation with a line and a reply (200 frames, `-Screen create`). `create-compendium.txt` goes through the
game's items and creatures in Compendium mode and adds an item (220 frames, `-Screen create`). `create-cutscene.txt`
plays the keep's ending in Cutscene mode, shows the whole map and makes a new cutscene that plays at the start
and when the chapter is cleared (320 frames, `-Screen create`). `create-story.txt` takes every suggestion for the
keep in Story mode and picks a fight on the graph (160 frames, `-Screen create`). `create-voice.txt` opens the Voice
view on Tobb's lines (120 frames, `-Screen create`). In Create
`cell X Y` goes to the cell on the editor's map. A run keeps its library in `../.dev/shot-characters`, its
save in `../.dev/shot-saves`, its settings in `../.dev/shot-settings` and Create's packages in
`../.dev/shot-create`, all emptied as it starts.

## C# style

- 4 spaces, braces on their own line.
- PascalCase types and methods, `_camelCase` fields, camelCase locals and parameters.
- File-scoped namespaces: `Yorehold.Rules` in `rules/`, `Yorehold` in `src/`.
- Nullable is on. No `!` to quiet a warning unless a comment says why it is safe.
- One main type per file, file named after it.
- Comments say why, not what.
- New rules get xunit tests. Anything drawn gets a screenshot run.
- Numbers and names that belong to the game live in `assets/`, not in code.
- Bad content fails with a message that names the file and the field (`ContentException`).

## Reading content

`ContentFiles` is the content tree: one or more folders, later ones on top, with forward-slash paths
(`classes/fighter.json`). `ContentNode` is one JSON value plus the file and field path it came from, so a
reader says where a problem is with `node.Fail(...)`. Each content type has a static `Read(ContentNode)`
that checks the file on its own; what needs other files (an item a class names, a condition an effect
applies) is checked by whatever loads them together: `Compendium`, `RulesFolder`, `Chapter`, `Adventure`,
`ContentPackage`.

The game's own folder is `Places.GameContent()`: `assets/` itself from the project, and in an exported game
a copy in `user://content` that `PackedContent` writes out of the pack each time the game starts, since
System.IO can't read inside a .pck or an APK. Exports are `export.ps1` (`docs/EXPORT.md`).

A new field goes in the type, its `Read`, a test in `tests/` and `docs/CONTENT.md`, in the same commit.
A new file under `assets/` has to be loaded by something or `ContentTests.EveryShippedFileLoads` fails.
