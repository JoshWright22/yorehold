# System design

The whole of Yorehold from the top down: what each repo is for, how the pieces fit, and what every
system is before it is built. [GAME_DESIGN.md](GAME_DESIGN.md) says how the game plays;
this file says how it is put together. [ROADMAP.md](ROADMAP.md) is the order of work.

Written 2026-10-03. Anything marked **default** was chosen so work can go on and can be changed
later; defaults are data wherever possible.

## 1. The four repos

| Repo | What it is | Knows about |
|---|---|---|
| `yorehold-framework` | Generic 2D tabletop RPG framework (C++20, namespace `yh`). Rendering, input, maps, tokens, generic RPG machinery, networking, UI widgets, editor helpers. | Nothing about Yorehold. No Yorehold numbers, names or screens. |
| `yorehold` | The game client. Yorehold's ruleset and starting content as files, the screens, the HUD, Create. | The framework, and the server's HTTP API. |
| `yorehold-server` | Nakama modules (TypeScript) and Postgres: accounts, storage, content registry, votes, config, dice for ranked play, relay. | Content only as files and metadata. Never runs game rules. |
| `yorehold-web` | Next.js site: library, content pages, votes and canon review, accounts, docs. | The server's API. |

Rule of placement: if another tabletop game could use it unchanged, it goes in the framework and
takes its numbers from data. If it is a Yorehold choice (two actions a turn, 40 supplies, the class
list), it is a file under `yorehold/assets/`. Client C++ is only for screens, flow and wiring.

## 2. Layers

```
web (Next.js) ───────────────┐
                              ├── server (Nakama + Postgres): accounts, storage, registry, votes, relay
client ── Online ────────────┘
  screens      Title, Library, Character, Lobby, Play, Camp, Create, Settings
  hud          party cards, action bar, initiative, log, journal, inventory, dialogue
  sim          World: the whole game state + rules, no drawing, no input
  content      Package, Adventure, Chapter, Compendium loading and validation
framework
  rpg          Ruleset, Character, Effects, Actions, Combat, Spells, Stealth, Tactics, Dialogue, Quests
  map          Grid, TileMap, Tokens, Objects, Regions, Fog, Light, Navigation, Templates
  net          Transport, Session (intents -> commands)
  graphics / ui / audio / text / assets / save / editor / input
```

### The one structural rule: sim and screens are separate

Everything that changes the game goes through one path:

```
input or AI or DM ──> intent ──> World::validate (host) ──> command ──> World::apply (everyone)
```

- `World` (client `src/sim/`) holds all game state: creatures, map state, flags, quests, inventory,
  the encounter in progress, the random stream. It has no renderer, no input and no clock except the
  seconds passed to `update`.
- Single player is a host with no clients. Co-op, DM mode and replays use the same path, so there is
  one set of rules to test.
- Screens read `World` to draw and send intents. They never change it directly.
- Dice are rolled in `validate` on the host and travel in the command, so every copy matches.
- A `World` can be built, played and checked in a unit test with no window. That is how rules are
  tested: scripted intents in, state out.

`YoreholdGame` is what is left around `World` and `PlayScreen`: the title and pause menus, settings, the library,
saves and the co-op session. It derives from `World` and overrides `act()` to carry intents through
the session.

Client source layout (planned screens in brackets):

```
src/main.cpp, YoreholdGame, Coop, Settings
src/content/   ContentPackage, Adventure, Chapter, GameMap
src/sim/       World, WorldCombat, WorldAi, WorldExplore, WorldStealth, WorldNet (validate/apply), Save
src/screens/   PlayScreen, Menus (title, pause, settings), CreateScreen [Library, Character, Camp]
src/hud/       PartyCards, ActionBar, InitiativeStrip, JournalPanel, DialoguePanel [InventoryPanel]
src/online/    Online (accounts, storage, registry)
tests/unit/    WorldFixture: load a chapter, send intents, step time, read state
```

### Create screen (G1)

The Create screen lets authors design and build content packages (adventures, rulesets, item collections). It sits at `src/screens/CreateScreen.h/cpp` and includes:

- **Package management**: Open an existing package folder or .yore file, or create a new one. The last opened package path is saved to `settings.lastCreatePackage` and reopened on the next session.
- **Mode tabs**: Map, Encounters, Dialogue, Compendium, Cutscene, Story (placeholder UI; full editors come in G2-G7).
- **Shared undo/redo**: One history for the whole package, managed by `PackageHistory`. Pressing Ctrl+Z/Ctrl+Y (future) undoes/redoes all changes.
- **Validation list**: Real-time validation of the package manifest and file structure. Errors are highlighted; warnings are dimmed. Invalid packages cannot be playtested or exported.
- **Playtest**: Loads the package and plays a chapter in-game to test rules, encounters, and dialogue (G1 is placeholder; actual playtest hooks come in later steps).
- **Export**: Saves the package as a .yore file or folder for sharing. Only valid packages can be exported.

Validation runs automatically every 2 seconds and also when a file is loaded or created. Old package files (without new manifest fields) still load and play with empty defaults; the manifest structure is backward-compatible.

## 3. Data

Everything a player can make is a file. One JSON file per thing, named after its `id`.

### Kinds

| Kind | Folder | Made by | Notes |
|---|---|---|---|
| Ruleset | `rulesets/` | project, by vote | One live ruleset for the whole game, versioned. |
| Action | `actions/` | ruleset | Strike, Stride, Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready. |
| Condition | `conditions/` | ruleset | off-guard, frightened, prone, slowed, downed... as modifiers plus flags. |
| Class | `classes/` | ruleset | Levels table: features, proficiency ranks, slots, feats at levels. |
| Race | `races/` | ruleset | Speed, senses, ability adjustments, race feats. |
| Background | `backgrounds/` | ruleset | Skills, a feat, starting gear. |
| Feat | `feats/` | ruleset | Kind (class, skill, general, race), prerequisites, effects. |
| Spell | `spells/` | ruleset | Level, hands, range, area, save, effects; listed per class. |
| Item | `items/` | anyone | Weapons, armour, gear, consumables, magic items. |
| Creature | `creatures/` | anyone | Stat block, AI profile, token, loot. |
| AI profile | `ai/` | anyone | How a creature thinks. |
| Kit | `kits/` | anyone | Map object prototypes: door, lever, chest, trap. |
| Map | in a chapter | anyone | Tiles, walls, lights, markers, objects. |
| Dialogue, quest, cutscene | in a chapter | writers | Framework formats. |
| Chapter | `chapters/<id>/` | writers | One play area with its encounters, NPCs and story. |
| Adventure | `adventure.json` | writers | Chapters in order, level range, recommended party, transitions. |
| Character | player's library | players | Belongs to the account, not to an adventure. |
| Skin, theme, language | `ui/`, `lang/` | anyone | Looks and text. |

Player options (class, race, background, feat, spell, action, condition) only come from the
ruleset. Adventures add items, creatures, AI profiles, kits and story (**default**, see GAME_DESIGN).

### Packages

A `.yore` file is a zip of a content folder with `content.json` at the top (see CONTENT.md). The
manifest gains:

- `kind`: `adventure`, `ruleset` or `definitions`.
- `id` and `revision`: stable id plus a number that goes up with each publish.
- `ruleset`: for adventures, the ruleset version it was written against.
- `requires`: other packages by id (asset packs, shared maps). **Default:** an adventure may
  require asset packs but carries its own definitions, so it plays the same for everyone.

### Effects: one vocabulary for everything that does something

Spells, class features, feats, items, conditions, traps and dialogue actions all use the same list
of effect steps, interpreted by the framework (`yh::Effect`). A new spell or trap is then a file,
never code.

```json
{ "id": "burning-hands", "level": 1, "hands": 2,
  "area": { "shape": "cone", "size": 15 },
  "save": { "ability": "dex", "dc": "caster" },
  "effects": [ { "do": "damage", "dice": "3d6", "type": "fire", "onSave": "half" } ] }
```

Steps: `damage`, `heal`, `tempHp`, `condition` (add/remove, duration), `modifier` (stat, op, value,
duration), `move` (push, pull, teleport), `resource` (spend/restore), `summon`, `light`, `surface`,
`flag` (story), `roll` (a check that gates the steps under it), `repeat`, `choose`. Each step has
`target` (`self`, `target`, `area`, `allies`, `enemies`), optional `when` (hit, crit, save failed,
start of turn...) and `scale` (by caster level or slot level). Unknown steps fail validation with
the file and field named.

The framework owns the interpreter and the hooks it needs from the game (who is in an area, apply
damage, place a token). Numbers and lists live in the ruleset.

### Rulesets and versions

- The ruleset is a package: `rulesets/yorehold/` in the game's assets, with the folders above and a
  `ruleset.json` that holds the numbers (actions per turn, proficiency by rank, rest costs, magic
  item limit, stealth step, XP table, encumbrance). The framework's `modern` and `classic` presets
  stay as test fixtures.
- `version` is `major.minor`. A minor change keeps characters. A major change retires them: the
  character moves to the graveyard and a rebuilt copy is made by a migration file
  (`rulesets/yorehold/migrations/<from>-<to>.json`: renamed ids, removed options and their
  stand-ins). Choices with no stand-in are left open and asked for before play.

### Characters

A character file holds choices, not results: race, background, ability scores and the method used,
each level's class and picks (feats, spells, skill increases), inventory, coins, XP, the ruleset
version, and notes. The sheet is rebuilt from those choices and the ruleset every time, so a rules
change shows up at once and a migration only has to rewrite choices.

- Library: `characters/` beside the save, mirrored to the account when online.
- Graveyard: `characters/graveyard/`, read-only.
- A character in an adventure is copied into that adventure's save. At the end of each chapter and
  on leaving, the copy is written back to the library (loot, XP, level). **Default:** one adventure
  at a time per character; a character in an unfinished adventure shows as "away".

### Saves

- One autosave per adventure per group, written after fights, rests, conversations, chapter changes
  and on quit. Never mid-fight (**default**; the wipe rule returns to it).
- Contents: adventure id and revision, chapter, the World snapshot (framework region deltas, flags,
  quests, the party copies, time, random state), who owns which character.
- Online, the save is stored for every account in the group; the newest wins, with the older kept
  as a backup.
- A save from an older revision of an adventure loads if the chapter's signature still matches;
  otherwise the group restarts the chapter with their characters as they were.

## 4. Systems

Status: **done**, **part** (exists, incomplete) or **todo**. "FW" = framework, "data" = ruleset or
content files, "client" = Yorehold code.

### Turns and actions (part)

- FW `Encounter`: initiative, free movement, an action budget, one reaction. Done.
- Actions as data: each `actions/*.json` has cost (`1`, `2`, `hands`), requirements, target and
  effects. The ten generic actions become files. Class abilities and spells are actions too, so the
  action bar is built from one list.
- Reactions: a trigger (`leavesReach`, `isHit`, `readied`) plus an action. Opportunity attack,
  Shield Block and Ready are data.
- Shared turns: neighbours in initiative on the same side form one block and act in any order.
- Surprise: the surprised side skips its first block. Done in FW.

### Checks and positioning (part)

- d20 + modifier against a DC; natural 1 and 20 are the only crits. Done.
- Proficiency = level + rank bonus (untrained 0, trained 2, expert 4, master 6, legendary 8 as
  **default** numbers in `ruleset.json`); untrained adds no level.
- Flanking: a foe on each opposite side gives off-guard. Cover: +2 / +4 from walls and creatures on
  the line. Both are FW helpers over the grid, with the numbers in data.
- Conditions are files: modifiers, flags (`cantAct`, `cantMove`, `offGuard`), duration and how they
  end (save at end of round, timed, an event such as a rest or being hit). Done: FW machinery, the
  ten files in `rulesets/yorehold/conditions/` (CONTENT.md).

### Characters and progression (part)

- Creation: background, race, class; ability scores by point buy, roll or array. A screen per step
  with a live sheet.
- Levelling by XP from the table in `ruleset.json`. Level-up walks the class's level row: HP,
  features, feats, spells, skill ranks. Any level may go into any class.
- Launch set: fighter, rogue, cleric, wizard; human, elf, dwarf, halfling; all four classes cover
  levels 1 to 20 before more classes are added (decided 2026-10-03).
- Martials get weapon rank increases and extra damage dice from their class rows; casters get slots
  and lists. These are table entries, not code.

### Magic (part)

- Slots by class level, cantrips that scale, focus points refilled on a short rest.
- A class row says `prepared` or `spontaneous`; other resource models use the generic `resource`
  effect.
- Spell lists are per class: `classes/wizard.json` lists spell ids by level.
- Targeting uses the framework's `AreaTemplate` (circle, cone, line, square) and the ruler.
- Concentration: one such spell at a time; damage forces a save (**default**, as 5e).

### Items, inventory and economy (part)

- Per-character inventory, equipment slots, hands. Weight slows movement in steps (**default:**
  carry limit from Strength, as BG3; over it halves speed; double it cannot move).
- Magic item limit from `ruleset.json` (3), counted across the whole inventory.
- Coins gp/sp/cp weigh nothing. Merchants are NPCs with a stock list and price multipliers.
- Loot: creatures drop their items plus a loot table; containers are map objects (kits).
- Camp stash is shared. Players can give but not take.
- No crafting.

### Rest, camp and death (part)

- Short rest: two between long rests. Long rest at camp costs supplies (40). Numbers in the ruleset.
- Camp is a small map each adventure can supply (**default:** a shared built-in clearing). Stash,
  companions and revival live there.
- 0 HP = downed: death saves at the start of each turn, three failures is dead, three successes is
  stable. Help, healing or a potion gets them up.
- Revival: scroll, spell, or a price at camp.
- Wipe: back to the autosave unless the chapter sets a `onWipe` cutscene and destination.

### Stealth, vision and light (part)

- FW `Stealth`, `FogOfWar`, `LightLevels` done. In the game (done): sneak toggle, cones while
  sneaking, a check every 5 m in a cone, light adjusting the roll, ambush for surprise. Numbers in
  `rulesets/yorehold/stealth.json`, enemy `facing` in the chapter file (CONTENT.md). The host makes the checks;
  "sneak" and "ambush" are intents like any other.
- Still to add: Hide and Seek as actions in a fight (roadmap B5), enemies turning or patrolling,
  noise.

### Enemy AI (done)

- Profiles as files, layered by creature, chapter, story flags and server config. Break behaviours:
  run, fetch help, surrender, fight on. New decision models plug in without content changes.
- Still to add: using spells and abilities (scores each available action through the same scorer).

### Story (part)

- Dialogue, quests and flags: done for NPCs in a chapter.
- Adventure structure (done): `adventure.json` lists chapters and the transitions between them
  (`from`, `exit` marker, `to`, `entry` marker, optional `when` flags). Flags are per adventure; a
  chapter may mark flags local (`localFlags`). A hero stepping onto an open exit takes the party
  on ("travel", sent by the host), sheets and flags included; saves follow the party's chapter.
- Companions: creatures with a character file, an approval number changed by dialogue actions and
  flags, and their own dialogue. They join through a dialogue action. Up to 4 player characters
  plus 2 companions (**default**).
- XP: each encounter and quest declares its XP; the editor proposes a value from creature levels.
- Cutscenes: done. Triggers gain `onEnter` (region or marker), `onFlag` and `onWipe`.

### Maps and world (part)

- FW done: sparse tile maps, floors, objects and kits, regions, fog, light, navigation, lock and
  trap components.
- Done: each chapter's map lives in a `yh::Region` (TileMap + Objects). `map.json` takes the
  framework's TileMap JSON or the old text rows as an import, plus kits placed as `objects`.
- Done: doors, levers, containers and traps through Interact; locks and traps use checks. Object
  state is saved and checked between co-op copies.
- Still to add: playing on floors other than 0, and moving between regions inside one chapter.
- Surfaces (fire, grease, water, ice) as a map layer of effect areas (**default:** yes, after
  spells).

### Multiplayer (part)

- Host-authoritative intents and commands over TCP: done on a local network.
- Seats: each character has an owner. Leaving hands them to the host. Joining mid-fight waits.
- Internet play: the server relays the same messages between host and clients (Nakama realtime
  match as a dumb relay), so no port forwarding. The host still runs the rules.
- DM mode (last): a DM seat owns team 1 and the NPCs, sees each creature's brief, may leave any
  creature to the AI, and has live tools (spawn, change DC, give loot) that are ordinary commands
  and show in every player's log.
- Cheating (decided 2026-10-03): the server does not check rules, and a host who wants to cheat
  can. Accounts record which adventures a player has beaten, so cheating that is reported or found
  is handled by banning the account, not by making the server run the game.
- Verified runs (later, optional): dice from the server and the command log uploaded for checking.

### Accounts and online (part)

Server API, all under Nakama RPCs with JSON bodies:

| Area | Calls | Storage |
|---|---|---|
| Accounts | device login now; email and link later | Nakama users |
| Characters | list, put, delete | storage collection `characters`, owner-only |
| Saves | list, put, get | collection `saves`, one row per adventure per account |
| Registry | publish (metadata + file to object storage), get, search, list by kind and tag | Postgres table `content`, files in R2-compatible storage (local disk in dev) |
| Votes | up, down, clear; one per account per item | table `votes`; score cached on `content` |
| Canon | nominate, sign off, flag, list queue | tables `approvers`, `signoffs` |
| Config | get | collection `config` (done: AI settings) |
| Completions | record a finished adventure (id, revision, party, difficulty, date), list | table `completions`; shown on the profile |
| Reports | report, block, ban | tables `reports`, `bans` |

The game works fully offline; online adds sync, the library browser and co-op over the internet.

### Web (todo)

Next.js (App Router, TypeScript), talking only to the server API.

- `/` featured and new; `/library` search by kind, tag, level range, score.
- `/c/<id>` a content page: description, screenshots, revisions, votes, "Open in Yorehold"
  (`yorehold://` link the client registers, falling back to a `.yore` download).
- `/canon` the review queue for approvers.
- `/u/<name>` profile and published work. `/docs` the format docs from the repos.
- `/play` the game itself, embedded: the browser build of the client runs in the page, signed in
  with the site's session, so playing and Create work on the web without a download (decided
  2026-10-03). The site around it is ordinary pages: sign in, account, forums, library.
- `/forums` discussion per category and per content item.
- Sign in with the same account as the game.

### Create (todo)

Inside the client, one screen with modes that share one open package, one undo history
(`yh::History`) and one Playtest button. Writers can move between modes at any time.

| Mode | Does |
|---|---|
| Story | The node graph: scene, encounter, dialogue, quest and ending nodes with links and writing. Source of suggestions. |
| Map | Paint tiles by layer and floor, walls, lights, markers, objects from kits; import a painted image. |
| Encounters | Place creatures and groups, facing, AI profile, XP, loot, reinforcements. |
| Dialogue | Tree editor for the framework's dialogue format with checks, flags and actions. |
| Cutscene | Timeline of steps with live preview. |
| Compendium | Forms for items, creatures, kits and AI profiles (and, for ruleset authors, the ruleset kinds). |
| Package | Manifest, validation results (click to jump to the problem), export, publish. |

- Layout: mode tabs along the top, tool column on the left, properties on the right, the map or
  graph in the middle (**default:** Foundry-style layer toolbar).
- Every mode edits the same JSON the game loads; there is no separate project format.
- Suggestions (maps for scenes, XP, quest steps) are proposals the writer accepts or edits.
- Playtest starts the chapter from the cursor with a chosen party, without saving.
- One editor, several layouts (decided 2026-10-03): the modes and the files are the same
  everywhere. Desktop and the web build use the layout above. Mobile gets its own touch layout
  (one panel at a time, larger targets, tools in a bottom sheet). So editor logic lives apart from
  editor layout: each mode is a model plus commands, with a layout drawn over it.

#### Voice lines (todo)

Turns a recorded line into its words and their timings once, when the file is added. The game
only reads the result; nothing listens to speech while the game runs.

- Pipeline: `voice/<name>.wav` (or `.ogg`) is decoded, mixed to mono and resampled to 16 kHz
  floats (miniaudio, which audio playback will use too), run through whisper.cpp, and written to
  `voice/<name>.voice.json` beside it. The original file is never changed and is what plays.
- whisper.cpp (MIT) comes in through `FetchContent` at a pinned tag and is part of the normal
  client build, since Create lives inside the client: a button in Dialogue mode, plus a
  `yorehold-voice` command for batches. It only runs in Create, never during play. The browser
  build leaves it out.
- Model: `base.en` quantized q5_1 (about 57 MB) ships with the build. CMake downloads it once at
  configure time from the whisper.cpp model page, checks a pinned SHA-256, keeps it in the build
  cache and copies it beside the exe, so it is never in git and nobody fetches anything at run
  time. `tiny.en` q5_1 (about 31 MB) can be picked instead with a CMake setting.
- Timings come from whisper.cpp's DTW token timestamps, which use the model's alignment-heads
  preset and are tighter than its older per-token estimates. Flash attention stays off because
  DTW needs the attention weights. Tokens are joined into words. Expect an error of tens of
  milliseconds up to roughly 150 ms. That is good for subtitles, word highlighting and dialogue
  events, but not good enough for lip-sync.
- Confidence: each word stores the lowest probability among its tokens. The editor flags words
  below 0.6, and that cutoff is data.
- Vocabulary: the names listed in `voice/vocabulary.txt` in a package (Kharos and the like) are
  given to whisper as its initial prompt, which steers it towards those spellings. No training is
  involved.
- The written line wins, because writers own the text. If the dialogue node already has its
  line, the tool keeps that text and only borrows the timings. It matches the heard words to the
  written ones by edit distance over normalized words. A written word with no match gets a time
  spread between its matched neighbours and is flagged. The same matching applies a writer's fix
  ("Carlos" to "Kharos") without running the model again. If the node has no line yet, the heard
  text becomes a suggestion the writer accepts or edits.
- File: `{ "format": 1, "audio", "model", "text", "words": [{ "text", "start", "end",
  "confidence", "matched" }] }`, with times in seconds. Plain text, SRT and VTT are exported
  from it on demand and not stored. Later outputs (phonemes, visemes) are added as new fields
  under a raised `format`.
- Later, and only if tests show it is needed: a forced aligner for phoneme timings, then a
  phoneme-to-viseme table for mouth animation.
- Testing: a set of lines spoken by Windows' built-in speech synthesizer. It reports where each
  word falls in the audio, so the checks measure word accuracy and timing error for `tiny.en`
  and `base.en` against known answers. Real recordings are added once there are some.

### UI

- Flow: Title > Play (Continue, Adventures, Characters, Join) / Create / Settings / Exit.
  Starting an adventure: pick adventure > pick or make characters > lobby (seats, difficulty,
  table rules) > play.
- HUD: party cards top left, initiative strip top centre, quest tracker top right, action bar
  bottom centre (movement, actions left, the action list, End turn), log bottom left, minimap off
  by default.
- Panels: sheet (C), inventory (I), journal (J), spellbook (K), map (M), camp button.
- All text through `yh::Strings`; all looks through the skin. Layout stays in code until the UI
  pass.

### Audio, text and settings

- Audio: music per chapter and per state (explore, fight, camp), effects by event name in the
  skin (`hit`, `miss`, `door`, `ui.click`), so skins and adventures can replace them.
- Languages: `lang/` in the game, a skin or a package.
- Settings: controls, camera, lighting mode, shared view, text size, colour-blind safe team
  colours, dice speed, auto-end turn.

## 5. Testing

- Framework: unit checks for every new module, plus a visual scene for anything drawn.
- Client rules: `World` tests that load content, feed intents and check state, through
  `WorldFixture` (tests/unit). Every roadmap item adds some.
- Content: `yorehold-content check` on the shipped assets and on a packed `.yore`.
- Whole game: scripted input runs and auto-play over fixed seeds, with screenshots.
- Server: `tsc` clean, and RPC tests against the local server where it is running.
- One command runs the lot: `check.ps1` beside the repos.

## 6. Defaults chosen here

These were open; each is the provisional answer and is data or a small switch where it can be.

1. No loading back to earlier encounters: one autosave.
2. Party cap: 4 player characters plus 2 companions.
3. Encumbrance from Strength as in BG3, in two steps.
4. Key tags (`key:red`) link levers, plates, doors and locks.
5. Tiles are square and match the grid; autotiling for walls comes with the map editor.
6. Thrown objects hit only where they land.
7. The editor is built into the client, with a layer toolbar.
8. Concentration works as in 5e.
9. Proficiency rank bonuses 0/2/4/6/8, untrained without level.
10. One adventure at a time per character.
11. Launch classes fighter, rogue, cleric, wizard; races human, elf, dwarf, halfling.
12. Internet co-op relays through the server; the host runs the rules.
13. Surfaces come after spells.
14. Adventures may require asset packs but carry their own definitions.
23. Stealth: an enemy with no `facing` looks toward where the party starts, watches as far as heroes
    see, and stands still. A player's heroes sneak together. An ambush is Attack on an unaware enemy
    while sneaking, from any distance at which it is seen.
24. A chapter that names no ruleset plays by `rulesets/yorehold`. Its numbers are the ones the keep
    was already played with; the magic item limit is 3 and is held there until inventory uses it.
    Stealth numbers moved into the folder as `stealth.json`. A creature running for help has to get
    within 3 squares of its allies (`alarmReach` in its AI profile).
25. Condition numbers, all in `rulesets/yorehold/conditions/`: off-guard, prone and grabbed each take
    2 from AC, and penalties from different conditions add up; prone also takes 2 from attacks;
    frightened takes its value (up to 4) from attacks and AC and drops by one a round; a raised shield
    adds 2 AC until the next turn; slowed halves speed. Sneaking is `hidden`; a hero at 0 HP is
    `downed`, anyone else `dead`. `dying` waits for death saves (roadmap B10).
26. Effects (the framework's FRAMEWORK.md has the format): a critical hit rolls the damage dice twice
    unless the step says `"crit": "normal"`; an area's damage is rolled once for everyone in it; a
    save or check succeeds on a total at or above the DC, with no special 1 or 20; temporary HP never
    add up; a step that waits for an event (`"when": "turnStart"`) only runs when the effect is run
    for that event.

27. Generic actions: Defend uses Shielded; Help revives a downed ally at 1 HP or aids the next attack. Hide must beat every standing enemy's passive Perception; Seek beats passive Stealth within 6 squares; Shove and Grapple beat passive Athletics. Shove moves one square; Grapple lasts through the fight. Interact first stands up from prone (equipment in D1, objects in F1). Ready costs 2 actions, ends the turn and records a Strike until the next turn; triggers come in B6. All numbers and conditions are action files.

28. Reactions: everyone can Strike when a foe leaves reach; Ready fires on a foe entering reach. Both spend one reaction regardless of weapon hands. Free movement and Dash provoke; pushes do not. Each crossed square is checked, lethal reactions stop the move, and planned movement is paid up front. Reactions are automatic; the host's optional prompt defaults to Take after 2 seconds (`promptSeconds` in each reaction file).

29. Shared turns: `sharedTurns: true` enables blocks of consecutive allies. Budgets and turn-start conditions refresh together; switching preserves them, and End turn finishes one member. Blocks follow the full initiative list, including down or withdrawn entries. Reinforcements first act next round. Movement and reaction decisions finish before switching; enemies default to initiative order.

30. Positioning: opposite standing foes within one square with clear centre rays flank a creature. The Off-guard file supplies its AC and flags without stacking an existing Off-guard. Corner rays classify wall cover; any blocked corner gives half cover, three of four gives three-quarters, and all gives full cover. Standing bodies give at most half cover; downed bodies give none. Cover applies to ranged attacks by default. `positioning.json` holds the condition, reach and +2/+4 bonuses.

31. Proficiency: the existing skill lists fall back to trained; starting classes have trained weapons, armour, DCs and two saves (STR/CON fighter and barbarian, WIS/CHA cleric, DEX/INT rogue). DC uses the class's STR/WIS/DEX respectively. Goblins train weapons, armour and DCs; commoners only armour. Creature AC stays the written final value at its authored level. All choices are class/creature data; later advancement belongs to the level tables.

32. Death: DC 10 unmodified saves, three successes stable, three failures dead; natural 1 adds two failures and natural 20 heals 1 HP. Unabsorbed damage at zero adds one failure (critical two), and damage to a stable character starts fresh counters. Monsters die immediately unless their file enables saves. Each starting hero carries one resource-backed 2d4+2 potion until consumable inventory is built. A wipe restores the latest checkpoint; optional onWipe cutscene precedes return, and destination supplies one current-chapter cell per hero.

33. Character choices: levels after the first add the class's hit die averaged and rounded up, plus CON (at least 1). Base speed, darkvision, ranks and gear come from the first class until level tables exist. Levels gained from XP go into the latest class until the level-up screen exists. Inventory and coins stay on the saved sheet until the character library (C4) writes them back.
34. Races, backgrounds and feats: a race's speed replaces the class's, darkvision is the better of the two, and score changes are +2/-2 (human +1 to all); a background is +1 to one score, two trained skills and a skill feat making one of them expert. Feats given by a race or background skip their requirements. The first feat set is passive (ranks, skills, small stat changes); feats that act in play come with the effects vocabulary. Backgrounds give no gear until there are more items than weapons and armour. The keep's fixed party has no race or background until it becomes pregenerated characters (C6).
35. Class level tables: ten feats over twenty levels (class at 2, 6, 10, 14, 18; skill at 4, 12, 20; general at 8, 16) and a skill to train at odd levels from 3. Features are passive for now (ranks, small stat changes, named resources such as Second Wind that the actions using them will spend). Casters use one full-caster slot table; with several casting classes each slot level takes the most any one gives. Picks left out stay open and are asked for by the level-up screen (C5). XP thresholds from level 11 follow the same curve up to 355,000 at level 20.
36. Character library: one file per character in `characters/`, named from the character's name (`ser-ada.json`, then `ser-ada-2.json`). The save keeps its own copy and names the library file; leaving writes the copy back with "away" set to the save's file name, and the adventure's end clears it. Write-back carries choices, XP and inventory; coins stay as they are until loot exists (D2). Death is permanent: a hero whose sheet is dead goes to the graveyard when written back. Co-op guests and test runs never write to the library.
37. Character screens (Play > Characters): creation is Origin (name, race, background), Class and scores, then Skills and feats, with the sheet rebuilt beside every click. A new character starts as a fighter on the standard array; point buy is 27 points over 8 to 15 at the usual costs, and rolling is 4d6 drop the lowest, all in `scoreMethods` in `ruleset.json`. Only feats a trial build accepts are offered. Level up is offered when XP reaches the next level and the character isn't away or in the graveyard; the level starts in the latest class and can go into any. A new character's gear is its class's and background's.
38. Starting an adventure: Play > New adventure shows one seat per party member in the chapter, each holding its ready-made hero until a library character or a new one takes it; Enter starts. Quick start (Enter on the Play menu) plays the ready-made party, as the keep always did. A brought character keeps the seat's position and colour, the ready-made hero is still rolled so the dice don't change, and one the adventure can't build (a class it doesn't carry) leaves the seat to the ready-made hero with a message. Characters of any level may come; one made for a seat gets the XP to level up to the chapter's `level`. Starting over frees characters away in that save. The seats are the lobby: in co-op, players who join take seats as before (host's choice of seats comes with the online lobby).
39. Gear and hands: I opens the selected hero's gear (in a fight, the acting hero's if it is yours); a click puts an item on or away. Everyone has two hands shared by the held slots: taking up an item whose `hands` aren't free puts the other held items away, so a two-handed weapon and a shield never go together. Between fights changing gear is free; in a fight it happens on the hero's own turn and costs what the Interact action costs (1 action). A grip is what is held; weapons with a one- and a two-handed use come when an item needs one.
40. Loot and coins: coins are counted in copper on each sheet (10 cp = 1 sp, 10 sp = 1 gp) and come along to the character library. A container is a chapter entry on a walkable cell of its own; it doesn't block the way and stays on the map, dull, once emptied (kits replace it in F1). Enemies drop when the fight is won, not as they fall: what they carried plus their loot table, rolled from the adventure's seed and the fight's number so co-op machines agree. Whoever picks something up gets it; a hero must stand on or beside the pile. Giving is free between fights, at any distance, and the gear panel hands over all of a hero's coins at once. Carried-only items stack; anything worn or held stays its own entry. Loot is not written to the log until someone takes it.
41. Weight and magic items: capacity is STR x 15 lb; over it a hero walks and moves in fights at half speed, over twice it not at all (`encumberedAt`, `immobileAt`, `encumberedSpeed` in `ruleset.json`). An item is magic when its file says `"magic": true`; the limit of 3 counts every one carried, worn or not, each of a stack. A fourth can't be picked up or handed over, and Take all leaves it behind with a message; nothing is ever dropped automatically. The first magic item is the warding ring (+1 AC), sometimes in the storeroom chest.

42. Merchants: stock and purse are finite and saved, with no restocking. Wren carries two maces (5 gp) and two shields (10 gp), with 100 gp to buy loot. Buy at the item's value, sell at half, rounded up/down respectively; values and multipliers live in content. Trade one unit at a time, beside a peaceful NPC and between fights; worn items must be put away before selling. Zero-valued items have no offer, and buying respects the magic item limit. Shops close when the hero leaves, talks, or starts fighting.

43. Consumables: healing potions restore 2d4+2 HP (50 gp); ward scrolls grant 1d6+2 temporary HP (25 gp); ember scrolls deal 2d6 fire with a DC 12 Dexterity save for half (50 gp). Each starting hero carries one potion. Using one spends one inventory unit, costs its authored actions in combat, and is free between fights; hostile scrolls require combat. Scrolls need no class or spell slots, and consumables count toward the magic item limit only when marked magic. All prices, targets, saves and effects are item data; legacy potion resources remain usable on older sheets.

44. Spells: hands must be free to cast (a shield or weapon in them is in the way) and a spell costs one action per hand; cantrips spend no slot and a spell spends the lowest slot of its level or above; a long rest restores slots. Concentration breaks on a new concentration spell, a failed CON save against 10 or half the damage, dropping to 0 HP, the fight ending or a rest. Spells that harm wait for a fight; helpful ones can be cast between fights. All of it is `spellcasting.json` and spell files.

45. Package manifest fields: `kind` categorizes a package (adventure, ruleset, compendium, character_class, race, feat; inferred from structure if empty), `id` is a stable identifier across versions, `revision` is a publish counter, `ruleset` names a ruleset version requirement, `requires` lists dependent packages. Old packages without manifest fields still load with empty defaults. Dependency checking and version matching happen at the library level when loading installed packages.

46. Map objects: a key is an item whose id a lock's `key:<id>` tag names; locks without a key are picked with a dex check, traps found with passive perception within 2 squares (`trapSpotRange`) and disarmed with dex. Failing a disarm by 5 sets the trap off. Only heroes set traps off, since enemies know where their own are. Paths avoid found traps. Using an object is free between fights and costs Interact in one. Only floor 0 is played; layers on other floors load and are kept.

47. Travel between chapters: any standing hero stepping onto an open exit between fights moves the whole party, no vote or prompt. Every chapter of an adventure seats the same number of heroes. Heroes keep their sheets; rests taken, flags and fired triggers carry, except a chapter's `localFlags`. Companions don't travel yet (F4). The adventure's level range only checks chapter levels for now.

48. Camp: the party goes from anywhere between fights (a chapter can say no) and comes back to the same spot with the chapter as it was left; the long rest is only taken there, costs 40 supplies, has no other limit and gives the two short rests back. Supplies come from the stash first, then the heroes' packs. A supplies item is worth 10 and costs 5 sp; the keep's storeroom chest has four. Anyone at camp can take from the stash ("give but not take" is about each other's packs). Revival at camp costs 200 gp from the selected hero and brings the hero back at 1 HP. Nobody holds a spell on the way to camp. Camp has no NPCs yet; companions come with F4.

### Structure choices made in this document

These shape the code and are the costly ones to change later.

15. The simulation is split from the screens before any new feature is added (roadmap A2).
16. One effects vocabulary runs spells, features, feats, items, conditions and traps.
17. Character files hold choices; the sheet is rebuilt from the ruleset on every load.
18. The editor is C++ inside the client and edits the same JSON the game loads. Confirmed
    2026-10-03, with a separate touch layout on mobile and the same editor on the web through the
    embedded game.
19. The host runs the rules, also over the internet; the server never does. Confirmed 2026-10-03:
    cheaters are banned rather than prevented.
20. The DM can change things live with a visible log, as GAME_DESIGN says. An earlier note had the
    DM only playing creatures as written.
21. The rules start from 5e reworded with Pathfinder 2e ideas, as GAME_DESIGN says. An earlier note
    had a B/X-style base.
22. Build order: rules machinery, characters, items, magic, adventure structure, Create, online,
    then DM mode and ruleset versions.

### Numbers and details not yet chosen

Each gets a provisional value in `rulesets/yorehold/` when its step is built. None is settled.

- Scores: point-buy budget, the standard array, the rolling method.
- HP per level (fixed or rolled), the XP table and how fast levels come.
- Which levels give which kind of feat, and how many feats exist at launch.
- Spell slot tables, focus points, and the starter spell lists.
- The skill list (the current one stands until the first vote), damage types and resistances.
- What a critical hit does (**default:** double the damage dice).
- How Hide, Seek, Shove, Grapple and Ready work in detail.
- Opportunity attacks happen on their own (**default**); a setting adds a prompt.
- Carry limits, revival price, merchant prices, supplies per food item.
- Camp: one shared built-in map unless an adventure brings its own.
- Co-op: who picks dialogue replies (anyone, as now), who gets loot (whoever picks it up), what
  happens to a leaver's character mid-fight (the host takes it).
- Difficulty options and the list of table rules in the lobby.

### Not designed yet

Nothing below blocks the roadmap before section H, and each needs a decision from the project.

- Upload licence and moderation rules; vote thresholds and how many sign-offs per category.
- Sign-in methods beyond a device login; display names.
- Where the server and the files are hosted.
- The shared setting, if canon means one world rather than a featured list.
- The UI look and final HUD layout (the layout here is a working guess).
- The starter art set, music and sound.
- Mobile.
