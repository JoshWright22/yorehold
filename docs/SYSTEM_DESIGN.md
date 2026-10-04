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
src/screens/   PlayScreen, Menus (title, pause, settings) [Library, Character, Camp, Create]
src/hud/       PartyCards, ActionBar, InitiativeStrip, JournalPanel, DialoguePanel [InventoryPanel]
src/online/    Online (accounts, storage, registry)
src/create/    [the editor modes]
tests/unit/    WorldFixture: load a chapter, send intents, step time, read state
```

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
  end (save at end of turn, timed, until rest).

### Characters and progression (part)

- Creation: background, race, class; ability scores by point buy, roll or array. A screen per step
  with a live sheet.
- Levelling by XP from the table in `ruleset.json`. Level-up walks the class's level row: HP,
  features, feats, spells, skill ranks. Any level may go into any class.
- Launch set: fighter, rogue, cleric, wizard; human, elf, dwarf, halfling; all four classes cover
  levels 1 to 20 before more classes are added (decided 2026-10-03).
- Martials get weapon rank increases and extra damage dice from their class rows; casters get slots
  and lists. These are table entries, not code.

### Magic (todo)

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
  `rules/stealth.json`, enemy `facing` in the chapter file (CONTENT.md). The host makes the checks;
  "sneak" and "ambush" are intents like any other.
- Still to add: Hide and Seek as actions in a fight (roadmap B5), enemies turning or patrolling,
  noise.

### Enemy AI (done)

- Profiles as files, layered by creature, chapter, story flags and server config. Break behaviours:
  run, fetch help, surrender, fight on. New decision models plug in without content changes.
- Still to add: using spells and abilities (scores each available action through the same scorer).

### Story (part)

- Dialogue, quests and flags: done for NPCs in a chapter.
- Adventure structure: `adventure.json` lists chapters and the transitions between them
  (`from`, `exit` marker, `to`, `entry` marker, optional `when` flags). Flags are per adventure; a
  chapter may mark flags local.
- Companions: creatures with a character file, an approval number changed by dialogue actions and
  flags, and their own dialogue. They join through a dialogue action. Up to 4 player characters
  plus 2 companions (**default**).
- XP: each encounter and quest declares its XP; the editor proposes a value from creature levels.
- Cutscenes: done. Triggers gain `onEnter` (region or marker), `onFlag` and `onWipe`.

### Maps and world (part)

- FW done: sparse tile maps, floors, objects and kits, regions, fog, light, navigation.
- The game still loads its own small `map.json` (text rows). It moves to the framework's `TileMap`
  + `Objects` + `Regions` so doors, levers, chests, traps, floors and large areas work. The text
  format stays as an import for hand-written maps.
- Interacting: doors, levers, containers and traps through Interact; locks and traps use checks.
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
