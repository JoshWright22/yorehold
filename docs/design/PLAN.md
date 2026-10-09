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
   Done 10/9: `ui/motion.json` (panels slide up into place, the chat slides, party HP bars run down), Options > Display > Less motion. Left: the turn banner's slide.
4. Editor screens, the same way: toolbar and breadcrumb, map, encounters, dialogue, cutscene,
   story graph, compendium, import from a book, build conflict, publish.
5. Website (`yorehold-web`): header, footer and each page, matched in its own repo.

## Pages

Status: not started, in progress, matches, differs (with why).

| Page | Picture | Game screen or file | Status | Notes |
| --- | --- | --- | --- | --- |
| Game flow | `game/00-flow.png` | screen order | not started | Lobby between the menu and play is new |
| Main menu | `game/01-main-menu.png` | `src/menus/MenuScreen.cs` | matches | 384 px band, spaced wordmark and tagline, amber Play, keys P/E/O/Alt F4 (pressable), version and sign-in at the foot, art card only without art; the docked chat column comes with the play screen's chat |
| Lobby | `game/02-lobby.png` | `src/characters/CharacterScreen.cs` (DrawParty) | close, offline | seat cards (number, face, You, the hero, how they come, READY or CHOOSING), Leave lobby in red, Start adventure in amber. Waits for online play: other players' names, invite code and Copy, Not ready, Add a computer hero, the adventure's page on the right |
| Character creation | `game/03-character-creation.png` | `src/characters/CharacterScreen.cs` | close | steps down the left with what each picked (the system's own steps; 5e now asks one thing per step: species, class, background, scores, skills and feat, name), Back: and Next: name their steps, Next in amber; the chat steps aside. Differs: picks are button grids, not the design's table with a page beside it; no Equipment or Appearance steps yet |
| Portrait crop | `game/04-portrait-crop.png` | `src/characters/PortraitCropView.cs` | matches | pictures from portraits folders (game and art packs) or a file copied into the my-pictures pack, a 3:4 amber frame to drag, zoom and Reset crop, previews at five sizes; Use this portrait keeps it in the character's choices |
| Options | `game/05-options.png` | `src/menus/SettingsPanel.cs` | matches | bar with search and Close, groups left with counts, rows divided by 1 px lines, choices outlined in amber, Reset to defaults per group; the design's resolution, interface size and frame limit rows are not settings yet |
| Play screen | `game/06-play.png` | `src/hud/PlayHud.cs`, `PlayScreen.cs` | matches but the hotbar split | done: party list (name, class line, thin HP bar, TURN), turn order (ROUND n and whose turn, squares underlined white or red), one bottom panel (who, AC, HP, action boxes, move, slots under ACTIONS, menu, End turn). Chat column with Combat log and Global tabs done (Party joins online). Dice buttons d4 to d20 over the map done (thrown, then said in the log). Hover card done (name, ENEMY or ALLY, HP, defence, chance to hit). Weapon chips beside ACTIONS done (two or more weapons; a press takes one in hand). Differs: one slot grid, not the design's everyone/class split; the menu is two columns of four |
| Party inventory | `game/07-party-inventory.png` | `src/hud/PartyGearView.cs` | matches but icons | a column per hero: slots round the portrait (head, cloak, body, hands, feet; neck, ring, main and off hand), AC and weapon boxes, a 6-wide bag, weight bar; drag to wear, take off or give, double-click to wear or use; tabs to Spells and Character; party coins. Item pictures come from icons/items/<id>.png in content or art packs, else the first letter |
| Chest or shop | `game/08-chest-or-shop.png` | `src/hud/TradeView.cs` | matches but Buy all | hero column with PACK, shelf grid with prices (red when too dear) and kind chips, the picked item's page with Buy, Sell, Take, Take all or Put; drag or double-click to trade. No buy-a-whole-stack yet |
| Spellbook | `game/09-spellbook.png` | `src/hud/SpellPanel.cs` | close | over the map above the hotbar (drag a spell down onto it), picked tab and chips in amber; differs: tabs by kind (Cantrips, Level 1...) instead of All/Prepared/On hotbar, no slot pips in the head |
| Conversation | `game/10-conversation.png` | `src/hud/PlayHud.cs` (ShowTalk) | matches but the top narration box | one panel along the bottom: the speaker's card (face, name, Merchant), the words in quotes, numbered replies with checks in blue on the right, T to trade; the map stays undimmed. Narration reads in the same panel under a NARRATION label |
| Pause | `game/11-pause.png` | `src/menus/MenuScreen.cs` (pause page) | matches but the online note | adventure, chapter and round under PAUSED; Options; Save and quit to main menu; last saved at the foot; the online-game note waits for online play |
| Wordmark | `parts/00-wordmark.png`, `01-wordmark-small.png` | title band | not started | |
| Chat closed and open | `parts/02-chat-closed.png`, `03-chat-open.png` | `src/hud/ChatPanel.cs` | matches | full-height column, open by default; tabs underlined in amber; > puts it away to a tab with < and the unread count; box with Send |
| Editor toolbar | `parts/06-editor-toolbar.png` | `src/create/CreateScreen.cs` | close | < Projects, the package and the chapter (pressed for the next), modes underlined in amber in story order, Test play, Save in amber (no Publish yet); Undo and Redo stay at the foot; the chat steps aside |
| Editor flow | `editor/00-flow.png` | Create's screens | not started | |
| Create pipeline | `editor/01-create-pipeline.png` | import and build | not started | |
| Edit (projects) | `editor/02-edit-projects.png` | Create's project list | in progress | fills the screen, titled Edit; differs: no size, status or edited columns, New buttons are one per system in the page's actions |
| Import from a book | `editor/03-import-from-book.png` | `src/create/CreateImport.cs` | not started | |
| Story graph | `editor/04-story-graph.png` | `src/create/StoryModePanel.cs` | not started | |
| Build conflict | `editor/05-build-conflict.png` | rebuild after edits | not started | |
| Map | `editor/06-map.png` | `src/create/MapModePanel.cs` | in progress | tools as a two-column grid with keys under TOOLS; differs: no room list, the inspector is the tile and floor column, no status bar with the cursor and zoom |
| Encounters | `editor/07-encounters.png` | `src/create/EncountersModePanel.cs` | not started | |
| Dialogue | `editor/08-dialogue.png` | `src/create/DialogueModePanel.cs` | not started | |
| Cutscene | `editor/09-cutscene.png` | `src/create/CutsceneModePanel.cs` | not started | |
| Compendium | `editor/10-compendium.png` | `src/create/CompendiumModePanel.cs` | not started | form left, "how players see it" page right |
| Publish | `editor/11-publish.png` | publishing | not started | |
| Website pages | `web/*.png`, `parts/04-web-header.png`, `05-web-footer.png` | `yorehold-web` | not started | step 5 |
