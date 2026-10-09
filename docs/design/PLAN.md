# Matching the design

The screens follow the design Josh made on 10/9: 45 pages in four parts, rendered here as
pictures. This file is the working plan. Each step updates its row below (status, what differs,
what was decided), and when the game is meant to differ from a page, the note says why. When a
new export replaces a source, `render.py` draws the pictures again and the rows are checked anew.

- Pictures: `game/`, `editor/`, `web/`, `parts/` (game and editor cropped to 1280x720, the game's window).
- Sources: the exported HTML files live outside the repos, in `D:\_Projects\Dev\Yorehold\design-source\`
  (`game.html`, `editor.html`, `web.html`, `parts.html`). `py -3.12 docs/design/render.py [part]` renders them.
- Compare: `check.ps1 -Shot x.png` saves the game's screen at the same size; put the two side by side.
- Roadmap: R20 (screens and tokens), R19 (fight beats, dice and attack animations).

## Tokens the design uses

Read from the game and shared pages (counts of use in brackets). They replace the current
`ui/colors.json`, `ui/fonts.json` and `ui/shapes.json` values in step 1; the role names stay, so skins keep working.

| Kind | Design values |
| --- | --- |
| Greys, dark to light | `#0b0b0b` (47), `#121212`, `#151515` (79), `#161616`, `#1f1f1f` (78), `#262626`, `#2a2a2a`, `#333333` (122, lines), `#4d4d4d`, `#707070` (108, faint text), `#a0a0a0` (256, muted text), `#c8c8c8`, `#ededed` (127, text) |
| Amber (the main action, turn, picked) | `#e8b33a` main, `#f5c451` light, `#c99a2e` dark |
| Blue (information, links, allies, roll lines) | `#6ca6ff` main, `#8fbcff` light, `#4a8cf0` dark |
| Red (enemies, danger, damage) | `#ff5c5c` main, `#ff7a7a` light, `#d94444` dark |
| Type | Source Sans 3 for words, JetBrains Mono for numbers, dice and times; 12, 14, 16, 20 and 32 px; small caps headings at 12 px |
| Shapes | 2 px corners on boxes, round tokens and pips, 1 px lines, no shadows |
| Motion | a dice throw of 1.15 s (ease `cubic-bezier(.2,.7,.3,1)`), the result after 1.4 s; panels slide (chat) |

## Steps

1. Tokens: the values above into `assets/ui/colors.json`, `fonts.json` (Source Sans 3 and
   JetBrains Mono as font files shipped under `assets/ui/fonts/`, both OFL), `shapes.json`
   (2 px corners); `Palette.cs`, `hud-theme.tres` and the site's `theme.css` the same. One
   screenshot of the title and the play screen against the pictures.
   - Done 10/9: the colours, in colors.json, Palette.cs, the theme and every scene (old role to
     new: greys to the design's, gold to amber `#e8b33a`, red `#ff5c5c`, blue `#6ca6ff`; the
     darkest red and blue shades, which the design doesn't draw, are darkened from its mains).
     Corners were already 2 px.
   - Done 10/9, second try: the fonts, shipped as engine-imported resources under `assets/ui/fonts/`
     (`.import` beside each; the game loads them through Godot; a skin's own still loads from disk).
   - Was blocked 10/9: the fonts. Font files loaded at run time (`FontFile.LoadDynamicFont` or
     from bytes) render, but every one makes the text server log "Parameter fd is null" on
     ascent, descent and spacing, which fails the check; three ways tried, reverted. Next try:
     ship the files as Godot-imported resources (`res://` .ttf with .import, loaded with
     `GD.Load<FontFile>`) and only load a skin's own files at run time. Until then the faces
     are the system fonts already listed.
   - Website `theme.css`: with step 5, in yorehold-web.
2. Game screens, one per step, in the order players meet them: main menu, options, lobby,
   character creation, portrait crop, play screen (with the combat log tab in the chat column),
   party inventory, chest or shop, spellbook, conversation, pause. Each step: look at its
   picture, change the screen, one screenshot, update its row.
3. UI motion, from one data file (`ui/motion.json`: durations and easings by name) shared with
   R19's beats: panels slide, the chat column pushes in and out, the turn banner, HP bars run
   down, the dice throw. A Settings switch for less motion.
4. Editor screens, the same way: toolbar and breadcrumb, map, encounters, dialogue, cutscene,
   story graph, compendium, import from a book, build conflict, publish.
5. Website (`yorehold-web`): header, footer and each page, matched in its own repo.

## Pages

Status: not started, in progress, matches, differs (with why).

| Page | Picture | Game screen or file | Status | Notes |
| --- | --- | --- | --- | --- |
| Game flow | `game/00-flow.png` | screen order | not started | Lobby between the menu and play is new |
| Main menu | `game/01-main-menu.png` | `src/menus/MenuScreen.cs` | done but the chat | 384 px band, spaced wordmark and tagline, amber Play, keys P/E/O/Alt F4 (pressable), version and sign-in at the foot, art card only without art; the docked chat column comes with the play screen's chat |
| Lobby | `game/02-lobby.png` | new screen | not started | seats, ready, invite |
| Character creation | `game/03-character-creation.png` | `src/characters/CharacterScreen.cs` | not started | 8 steps |
| Portrait crop | `game/04-portrait-crop.png` | new screen | not started | one 3:4 crop |
| Options | `game/05-options.png` | `src/menus/SettingsPanel.cs` | matches | bar with search and Close, groups left with counts, rows divided by 1 px lines, choices outlined in amber, Reset to defaults per group; the design's resolution, interface size and frame limit rows are not settings yet |
| Play screen | `game/06-play.png` | `src/hud/PlayHud.cs`, `PlayScreen.cs` | in progress | done: party list (name, class line, thin HP bar, TURN), turn order (ROUND n and whose turn, squares underlined white or red), one bottom panel (who, AC, HP, action boxes, move, slots under ACTIONS, menu, End turn). Left: weapon chips, chat column with Party, Combat log and Global tabs, dice buttons on the map, the hover card |
| Party inventory | `game/07-party-inventory.png` | `src/hud/GearPanel.cs` | not started | drag and drop |
| Chest or shop | `game/08-chest-or-shop.png` | containers and merchants | not started | drag to buy, sell, take |
| Spellbook | `game/09-spellbook.png` | `src/hud/SpellPanel.cs` | not started | drag to the hotbar |
| Conversation | `game/10-conversation.png` | talk panel | not started | |
| Pause | `game/11-pause.png` | `src/menus/MenuScreen.cs` (pause page) | matches but the online note | adventure, chapter and round under PAUSED; Options; Save and quit to main menu; last saved at the foot; the online-game note waits for online play |
| Wordmark | `parts/00-wordmark.png`, `01-wordmark-small.png` | title band | not started | |
| Chat closed and open | `parts/02-chat-closed.png`, `03-chat-open.png` | `src/hud/ChatPanel.cs` | not started | |
| Editor toolbar | `parts/06-editor-toolbar.png` | `src/create/CreateScreen.cs` | not started | breadcrumb, mode tabs, Test play, Publish |
| Editor flow | `editor/00-flow.png` | Create's screens | not started | |
| Create pipeline | `editor/01-create-pipeline.png` | import and build | not started | |
| Edit (projects) | `editor/02-edit-projects.png` | Create's project list | not started | |
| Import from a book | `editor/03-import-from-book.png` | `src/create/CreateImport.cs` | not started | |
| Story graph | `editor/04-story-graph.png` | `src/create/StoryModePanel.cs` | not started | |
| Build conflict | `editor/05-build-conflict.png` | rebuild after edits | not started | |
| Map | `editor/06-map.png` | `src/create/MapModePanel.cs` | not started | |
| Encounters | `editor/07-encounters.png` | `src/create/EncountersModePanel.cs` | not started | |
| Dialogue | `editor/08-dialogue.png` | `src/create/DialogueModePanel.cs` | not started | |
| Cutscene | `editor/09-cutscene.png` | `src/create/CutsceneModePanel.cs` | not started | |
| Compendium | `editor/10-compendium.png` | `src/create/CompendiumModePanel.cs` | not started | form left, "how players see it" page right |
| Publish | `editor/11-publish.png` | publishing | not started | |
| Website pages | `web/*.png`, `parts/04-web-header.png`, `05-web-footer.png` | `yorehold-web` | not started | step 5 |
