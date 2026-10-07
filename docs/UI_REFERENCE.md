# UI reference

How similar tools, rules sites and games lay out their screens, gathered 2026-10-07 for the UI rework asked for in
the playtest (title, settings, pause, library, lobby, Create). Each section ends with what Yorehold
takes from it.

## Dungeon Scrawl (map maker, browser)

- Canvas fills the window. A slim tool strip holds the drawing tools: pan, select object, rectangle
  room, circle/polygon, path (corridors), freeform polygon, wall, area select, door, stairs, text.
- Rectangle is the tool picked on first load, so a new user can drag out a room straight away.
- Hovering a tool shows its name and hotkey.
- Left side: one menu (Presets/"Scrawl", Images, Clipboard, Textures, Lighting, Plugins,
  Export/Sharing, send to Roll20, send to a table screen). Since Nov 2025 it is one menu that opens
  the panel needed now instead of fixed tabs.
- Presets are style buttons; with a layer selected, the lower half of a preset applies that style
  to the layer.
- Right side: layers list and the properties of what is selected. Selecting on the canvas
  highlights the layer and selecting a layer highlights the object.

Takes: Create > Map opens on a room tool, tools show hotkeys on hover, one left menu that swaps
panels, properties on the right follow the selection both ways.

## Inkarnate 2.0 (map maker, browser)

- Dark theme, flat panels. Sliders run the full panel width.
- Layers sit in their own panel beside the object list. The panel folds to a thin column of icons.
  Right click gives every action; layers reorder by dragging and rename in place.
- Advanced brush settings moved into a pop-up so the side panel stays short.
- "Scene stamps": a built group of stamps saved as one reusable piece.

Takes: side panels that fold to a strip, right-click menus for list actions, rare settings in a
pop-up rather than a long panel, saved groups of map pieces (rooms, camps) as reusable stamps.

## Dungeondraft (map maker, desktop, built on Godot)

- Left: the active tool's options (each tool has its own options panel). Right: the asset library
  with tags to filter it.
- Fixed layer stack by default; mods add custom layers.

Takes: same left/right split as Dungeon Scrawl, and proof that this layout works in Godot.

## Foundry VTT (virtual tabletop)

- Canvas in the middle with everything else around its edges.
- Top: scene navigation (switch maps).
- Left: layer controls (tokens, measure, walls, lights, notes); picking one changes what clicks on
  the map do, and its tools open beside it.
- Right: sidebar with tabs (chat, combat tracker, actors, items, journals, tables, playlists,
  compendiums, settings). Clicking the open tab folds the sidebar; folded it still shows who is
  online.
- Bottom left: players list, then a 10-slot macro hotbar with pages, roll mode and chat entry.
- Right-click on a token: a small ring of buttons around it (bars, conditions, elevation, target,
  combat toggle).
- v13 follows the system light/dark theme and can be overridden.
- Pause shows one large banner in the middle of the screen.

Takes: DM mode and Create share this edge layout (mode tools left, data tabs right, map centre),
the right sidebar folds by clicking its open tab, right-click on a token gives quick actions on the
map instead of a window, pause shows a banner over the world.

## Owlbear Rodeo 2.0 (minimal virtual tabletop)

- Built to be running in under a minute: drop a map, drop tokens, roll.
- The scene menu was taken out from behind two dialogs and made one click.
- Options for a selected token show in a tab only when asked for, instead of handles on the token.
- Extras (initiative, dice, clock) are add-ons switched on when wanted.

Takes: count the clicks to the common actions and cut them; show options only for what is
selected; keep rarely used tools off the screen until switched on.

## Baldur's Gate 3 (the game the playtest named)

- Hotbar at the bottom centre with the selected character's portrait on its left, hit points under
  the portrait. Patch 7 grouped the hotbar into "decks" (common actions, class actions, spells,
  items) to cut clutter.
- Party portraits on the edge, shown on hover or on that character's turn.
- Character creation: two halves, Character (origin, race, class, background, abilities) and
  Appearance (behind an Edit Appearance button under the model). Each choice shows what it gives
  right beside it.
- Main menu: the menu down one side over a large scene picture, logo above it.

Takes: hotbar grouped into decks with the portrait at its left, character creation as a few steps
with the effect of each pick beside it and the character picture large, title as a side menu over a
large picture.

## osu! (the other game the playtest named)

- Main menu: one big logo; clicking it opens the few top choices beside it. The background is a
  picture from the player's own maps, which changes.
- Song select: a carousel list on one side, the selected entry's details on the other, filter and
  search at the top.

Takes: title shows a large logo over player art (cover pictures from the library), few choices;
library as list left and the chosen adventure's details right, which matches the data-screen rule.

## D&D Beyond (rules site, character builder and sheet)

- Top bar: logo left, five menus (Play, Rules, Library, Community, Marketplace), search and sign-in
  right. The Rules menu lists each kind of entry (classes, backgrounds, species, feats, spells,
  equipment, magic items, monsters) with a small picture each.
- Front page: dark background with gold accents, one large banner picture with a single button,
  then a row of product covers.
- Spell list: filters in a column of folding groups (class, name, level, tags, casting time, school,
  save, attack type, damage type, conditions, components, concentration, ritual, source) and one
  "Reset all filters" link. Table columns: level, name, casting time, duration, range/area,
  attack/save, damage/effect; school and components sit in small grey text under the name. Numbered
  pages.
- Monster page: name and picture at the top, type line, then AC, hit points, speed, the six
  ability scores with modifiers, skills and senses, languages and challenge, traits, actions, then
  the description. Thin bars divide the parts. The source book and page, tags and habitat sit
  under the stat block.
- Character sheet: abilities, saves, skills and senses fixed at the top and left; the rest in tabs
  (Actions, Spells, Inventory, Features & Traits, Description, Notes, Extras). Clicking any entry
  opens its full text in a side panel. Inventory switches between "My inventory" and "Party
  inventory".
- Common complaint: in a fight players flip through several tabs, and some routine things
  (ammunition) take three clicks over three panels. Players ask for one tab they arrange
  themselves.

Takes: the entry page order (name, type line, numbers, traits, actions, then description, then
source and tags) for creatures, items and spells; filter groups that fold, with one reset; the
Rules menu as the way into the data screens; the sheet's fixed core with tabs and a side panel for
details, but the fight stays on the hotbar so nobody flips tabs mid-turn.

## Paizo store and Pathfinder Nexus

- Store: product covers in a grid, each with title, short blurb, price and buttons; sections by
  game line (Pathfinder 2E, Starfinder 2E), then rows for new players, digital, player and GM
  essentials.
- Pathfinder Nexus splits the digital side into three parts: a free Game Compendium (rules data), a
  Digital Reader (the books) and a Character Builder with a digital sheet.
- Archives of Nethys, the free rules site, is the dense table style the 10/5 rules already copy.

Takes: the library's adventure covers can sit in sections (new, for new players, by world) above
the list, like a store front; keep three separate places for rules data (Bestiary/Items), the
adventure text (story graph/journal) and the character (sheet).

## Where this meets the 10/5 look rules

The look rules (one palette, now Apollo, flat, 1 px borders, dense database-like data screens) still hold.
The playtest asks for screens that feel like a game, not lists of small buttons. Both fit if:

- Game-facing screens (title, pause, lobby, character creation) put a large picture first, player
  or content art, with few big choices beside it; still flat and in the palette.
- Data screens (library, bestiary, items, Create lists) stay dense lists with a details panel.
- Tool screens (Create, DM mode) use the editor layout above: map centre, mode tools left,
  selection and data right, panels folding to a strip.
- Settings become a few named groups down the left with the group's options on the right, as in
  BG3 and osu, and no file paths.

## Sources

- Dungeon Scrawl help (Roll20): https://help.roll20.net/hc/articles/16979743281943,
  https://help.roll20.net/hc/articles/39323166614935, https://help.roll20.net/hc/articles/17558111123863
- Dungeon Scrawl blog, Nov 2025 update: https://blog.dungeonscrawl.com/november-2025-update
- Inkarnate 2.0 changelog: https://feedback.inkarnate.com/changelog/inkarnate-20-star
- Dungeondraft wiki, ToolPanel: https://github-wiki-see.page/m/Megasploot/Dungeondraft/wiki/ToolPanel
- Foundry VTT: https://foundryvtt.com/article/player-orientation/, https://foundryvtt.com/article/glossary/,
  https://foundryvtt.com/releases/13.341
- Owlbear Rodeo: https://blog.owlbear.rodeo/towards-owlbear-rodeo-2-0-2/,
  https://blog.owlbear.rodeo/owlbear-rodeo-2-0-dev-log-1/
- Baldur's Gate 3: https://www.escapistmagazine.com/all-patch-notes-for-baldurs-gate-3-patch-7/,
  https://baldursgate3.wiki.fextralife.com/Character+Creation
- osu!: https://blog.ppy.sh/page/11
- D&D Beyond: https://www.dndbeyond.com/, https://www.dndbeyond.com/spells,
  https://www.dndbeyond.com/monsters/16907-goblin,
  https://dndbeyond-support.wizards.com/hc/en-us/articles/7747193946388-Sheet-Sections
- Paizo: https://store.paizo.com/, https://paizo.com/pathfinder
