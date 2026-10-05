# Roadmap

The order of work for [SYSTEM_DESIGN.md](SYSTEM_DESIGN.md). Top to bottom. A step is done when its
"done when" holds, `check.ps1` passes, and it is committed and pushed (framework before client).
Tick the box in the same commit. If a step turns out to be blocked, write one line under it saying
why and go on to the next.

Each step is sized to be one or a few commits. Numbers come from data, never from code.

## A. Foundation

- [x] **A1. Stealth in the game.** Finish the sneak toggle, vision cones, checks every few metres,
  light, and ambush for surprise, in single player and co-op.
  Done when: unit checks cover a sneak past a watcher, getting spotted and an ambush; the keep has
  one enemy with a set `facing`; CONTENT.md describes `rules/stealth.json` and `facing`.
- [x] **A2. Split the simulation from the screens.** Move game state and rules out of
  `YoreholdGame.cpp` into `src/sim/World*` with no renderer or input, as in SYSTEM_DESIGN section 2.
  Do it in slices that each build and pass: (1) content loading into `src/content/`; (2) state and
  save into `World`; (3) exploring; (4) combat; (5) validate/apply; (6) what is left becomes
  `PlayScreen` plus `hud/`.
  Done when: no file in `src/` is over 900 lines; a unit test plays the keep to victory through
  `World` with scripted intents and no window; the scripted run and auto-play seeds give the same
  results as before the split.
- [x] **A3. World test helpers.** A small fixture: load a chapter from a folder or from JSON
  strings, send intents, step time, read state. Use it in every later step.

## B. Rules machinery (framework first, numbers in `assets/rulesets/yorehold/`)

- [x] **B1. Yorehold ruleset as a folder.** `rulesets/yorehold/ruleset.json` with the numbers the
  game uses today (actions per turn, rests, XP table, stealth step, magic item limit). The game
  loads it by default; `modern`/`classic` stay for tests.
- [x] **B2. Conditions as files.** `conditions/*.json`: modifiers, flags, duration, how they end.
  Off-guard, frightened, prone, slowed, grabbed, hidden, downed, dying, dead, shielded.
- [x] **B3. Effects.** `yh::Effect`: the step list from SYSTEM_DESIGN section 3, parsed and
  validated from JSON, run against a small host interface. Unit checks for each step, `when`,
  `scale`, saves and half damage.
- [x] **B4. Actions as files.** `actions/*.json` with cost, requirements, targeting and effects.
  Strike, Stride and End turn move onto it with no change in play. The action bar lists whatever
  the acting creature has.
- [x] **B5. The other generic actions.** Defend, Help, Hide, Seek, Shove, Grapple, Interact, Ready.
  Interact stands up; equipment and objects follow in D1 and F1.
- [x] **B6. Reactions.** Triggers and a reaction budget; opportunity attacks when leaving reach
  (free movement included); Ready. Players get a short prompt with a default of "take it"
  (**default:** opportunity attacks are automatic, a setting turns the prompt on).
- [x] **B7. Shared turns.** Neighbours in initiative on one side act as a block in any order.
- [x] **B8. Flanking and cover.** Grid helpers in the framework; numbers in the ruleset.
- [x] **B9. Proficiency ranks.** Level plus rank bonus for attacks, saves, skills, AC and DCs.
- [x] **B10. Downed and death.** Death saves, stable, dead, getting up through Help, healing or a
  potion; wipe returns to the autosave or the chapter's `onWipe`.

## C. Characters

- [x] **C1. Character files hold choices.** Race, background, scores, per-level picks; the sheet is
  rebuilt from them. Migration from the current sheets in saves.
- [x] **C2. Races, backgrounds and feats as files.** Human, elf, dwarf, halfling; six backgrounds;
  a first set of feats per kind.
- [x] **C3. Class level tables.** Fighter, rogue, cleric, wizard, levels 1 to 20: HP, features,
  ranks, feats, slots. Level-up from XP, any level into any class. Build the table format and
  levels 1 to 5 first, then fill 6 to 20 in the same format; a unit check builds each class at
  every level.
- [x] **C4. Character library.** `characters/` beside the save, graveyard folder, the "away" flag,
  writing a character back at chapter end.
- [x] **C5. Character screens.** Create (three steps, three score methods, live sheet), level up,
  library list. Play > Characters.
- [x] **C6. Starting an adventure.** Pick the adventure, pick or make up to four characters at its
  recommended level, lobby with seats, then play. The keep's fixed party becomes its pregenerated
  characters.

## D. Items and economy

- [x] **D1. Inventory panel and hands.** Per character, equip and swap grips through Interact.
- [x] **D2. Loot and containers.** Creature drops, loot tables, chests as map objects, give to an
  ally, coins.
- [x] **D3. Weight and the magic item limit.**
- [x] **D4. Merchants.** An NPC with stock and prices, buy and sell.
- [x] **D5. Consumables.** Potions and scrolls as items with effects.

## E. Magic

- [x] **E1. Spell files and casting.** Slots, cantrips, hands as cost, targeting with area
  templates and the ruler, saves, concentration.
- [x] **E2. Prepared and spontaneous casters, focus points.**
- [ ] **E3. Starter lists.** About eight spells each for cleric and wizard across levels 0 to 3,
  written fresh, each with a unit check.
  Blocked: E2 complete but spell lists not yet written.
- [x] **E4. AI uses abilities.** The scorer rates every action a creature has.
- [x] **E5. Surfaces.** Fire, grease, water, ice as effect areas on the map.

## F. Adventure structure

- [ ] **F1. The game map on the framework's TileMap, Objects and Regions.** Doors, levers, locks,
  traps and chests work through Interact; the text map stays as an import.
  Blocked: Build hangs mid-SDL compilation (CMakeLists cache issue); 3 attempts failed.
- [ ] **F2. `adventure.json`.** Several chapters, transitions between markers, adventure-wide
  flags, level range. A two-chapter test adventure (test content, not shipped as story).
  Blocked: Build hangs (F1 dependency); code ready with tests and docs.
- [ ] **F3. Camp, supplies and long rests.** A camp map, the stash, rest costs, revival at camp.
- [ ] **F4. Companions.** Joining through dialogue, approval, the party cap.
- [x] **F5. Triggers.** `onEnter`, `onFlag`, `onWipe` for cutscenes and dialogue; non-combat
  completion.
- [x] **F6. Package manifest fields.** `kind`, `id`, `revision`, `ruleset`, `requires`; the
  compendium and library read them; old packages still load.
  Done when: unit checks cover manifest loading, dependency checking, and version mismatch handling;
  CONTENT.md describes manifest fields; ContentPackage reads and validates them;
  the Compendium can be extended to index packages by kind and check requirements.

## G. Create

- [x] **G1. The shell.** Create screen with mode tabs, open/new package, shared undo, validation
  list, Playtest and export.
- [ ] **G2. Map mode.** Tiles by layer and floor, walls, lights, markers, kits.
- [ ] **G3. Encounters mode.** Creatures, groups, facing, AI, XP, loot.
- [ ] **G4. Dialogue mode.**
- [ ] **G8. Voice lines.** A recorded line becomes words with timings in `<name>.voice.json`,
  produced locally by whisper.cpp at import and matched to the written line. Includes a
  measured tiny.en vs base.en comparison. See SYSTEM_DESIGN "Voice lines".
- [ ] **G5. Compendium mode.** Forms generated from each kind's fields.
- [ ] **G6. Cutscene mode.**
- [ ] **G7. Story mode.** The node graph and its suggestions.

## H. Online

- [ ] **H1. Characters and saves in account storage.** Server RPCs and client sync, newest wins
  with a backup.
- [ ] **H2. Content registry.** Publish, get, search; files on local disk in development.
- [ ] **H3. Votes and scores.**
- [ ] **H4. Library browser in the game.** Search, install, update.
- [ ] **H5. Internet co-op through the server relay.**
- [x] **H6. Web site.** Next.js: home, library, content page, sign in, profile.
- [ ] **H7. Canon review.** Approvers, sign-offs and the queue, on the server and the site.
- [ ] **H8. Reports, blocking and bans.**
- [ ] **H9. Completions.** Finishing an adventure is recorded on the account and shown on the
  profile.
- [ ] **H10. The game embedded in the site.** The browser build on `/play`, signed in through the
  site.
- [ ] **H11. Forums.**

## I. Later

- [ ] **I1. DM mode.** The DM seat, creature briefs in chapter files, live tools with a log.
- [ ] **I2. Ruleset versions.** Migration files, the graveyard, rebuilt characters.
- [ ] **I3. UI pass.** Once most of the game is implemented; until then screens keep the
  placeholder look and only need to work.
- [ ] **I4. Audio events and music states.**
- [ ] **I5. The remaining SRD classes and races.**
- [ ] **I7. Touch layout for Create on mobile.**
- [ ] **I6. One CI run for Linux and macOS.**
