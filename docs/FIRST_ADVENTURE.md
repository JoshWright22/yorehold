# Your first adventure

A walk from nothing to a fight you can play, about half an hour. Everything below is in the game;
the last part is for people who would rather write the files by hand (as Foundry users often do).

## 1. Make the package

Title > Edit > New adventure (one button per rules system: pick the one you want to play under).
The game makes a folder in your Create folder with:

- `content.json`, the package: its name, the system it plays, its chapters;
- `chapters/<id>/chapter.json` and `map.json`, one chapter with one hero on an empty 24 x 16 map;
- `items/` and `creatures/` with one `example-...` file each: a copy of one of the system's own,
  there to change and to copy.

Rename the adventure on Story > the package's own node, or in `content.json` (`name`).

## 2. Draw the map

Map mode. Paint (P) grass and stone, Wall (W) around a room, Light (L) a torch or two. Right-click
takes a tile off. The party starts on the `partyStart` marker (Marker, M); drag it where you like.
Test play (top right) drops you on the map as it is now, and Esc brings you back.

## 3. Put a fight in it

Encounters mode, Place: click the map to put creatures down, the example creature among them.
The fight panel names it, says what the game shows as it starts, and "Play it out" plays it 20
times with the game's own AI on both sides: "won 9 in 10, 1 in 5 lose a hero". Too hard: "Fit to
the party" takes foes out until it isn't.

## 4. Give someone something to say

Dialogue mode, New conversation. The graph shows the lines and the replies; double-click a line to
type its words, add replies with Add reply, give one a check (Insight DC 10) with pass and fail
lines. Put the speaker on the map in Map mode (a creature with the conversation) and talk to them
in Test play.

## 5. Make your own things

Open the package folder in VS Code (or any editor that reads JSON schemas): with
`.vscode/settings.json` from `docs/schemas/vscode-settings.json`, every file kind completes and
checks as you type, `ruleset.json` too.

- `items/example-longsword.json`: change `damage` to `"1d10"`, give it `"modifiers": [{"stat":
  "attack", "value": 1}]`, save.
- Spells, races, backgrounds and feats belong to a ruleset folder: a package that adds them has
  `rulesets/<its system>/` with a `ruleset.json` (copy the system's own) and `spells/` beside it.
  Copy one of the game's spells there, give it a new `id` and `name`; class files' `spells` lists
  say who can learn it.
- `creatures/example-goblin.json`: `hp`, `armorClass`, `abilities`, the `items` it fights with,
  `actions` for what else it can do (a trigger like `regeneration`).

Field by field, every kind is in CONTENT.md; the rules language (formulas, dice, effect steps) is
RULES_LANGUAGE.md. If you know Foundry, FOUNDRY_NOTES.md maps its words to these
(`@abilities.str.mod` is read here too) and imports a Foundry export (Create > Compendium > From
Foundry...).

## 6. Check it and play it

- `check.ps1 -Package <your folder>` checks every file as the game reads it and names the file
  and the field of each problem (`items/rusty-sword.json: range: is a whole number from 1 to 1000`).
  Create shows the same list on its right.
- While playing (Test play or a saved game), F6 reads every file again and carries on where the
  party stands, between fights: change a number, press F6, try it.

When it plays the way you want, Save (top right) and the adventure is on Play > New adventure.
