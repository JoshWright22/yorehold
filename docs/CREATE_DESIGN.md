# Create design

Decisions confirmed on 2026-10-03. The current keep and its rules are a playable prototype; they do not settle the final game or UI design.

## Order of work

1. Game design: agree on the play loop, character/classes, combat, recovery, chapter progression, co-op/DM roles and author-controlled outcomes.
2. UI design: agree on the Play/Create/Settings flow, gameplay HUD and how authors move between their design, content, map, encounter and story work. Review screen layouts and interactions before building the visual tools.
3. Transferable files: provide the common data foundation that both the game and future Create tools use. This is the current implementation step, so content can already be authored, validated and transferred.
4. Create tools: support all the required work, with map painting, walls and lights, token/NPC/enemy placement, encounters, cutscenes and dialogue, plus playtesting and import/export.

The order concerns building the editor, not forcing chapter writers through a wizard. Authors should be able to revisit design, UI, maps and story as they develop a chapter. No choice of just one editing feature replaces the others.

## Ownership

- Game designers own the rules and shared definitions. Rulesets, classes, items and creatures are content files; the prototype uses a built-in ruleset until a designer supplies one.
- UI designers own the interface and presentation. Colors and frame styling are already in `ui/theme.json`; screen layout and flows remain client code until the UI design is agreed.
- Chapter writers own all story content: dialogue, captions, introductions, encounter lines, names, ending/completion and resume text. They also choose the enemies, encounter groups and placements, and the map's tiles, walls and lights.
- Code runs the content and provides general controls, validation errors and mechanical combat reporting. It does not decide a chapter's story or rewrite an author's captions.

## File foundation delivered

`assets/content.json` declares playable chapters and an optional UI theme. Definitions live in `classes/`, `items/` and `creatures/`. Chapters can add or override those definitions locally. Each chapter references its map and ending, and declares party and encounter placements. Dialogue and additional cutscene files can be declared in the package manifest for validation; playable NPC dialogue integration remains future work.

The `yorehold-content` tool checks a folder or `.yore` archive and packages a complete content folder. The game loads either representation with the same readers. Packaging includes design documents, custom rulesets, dialogue, UI files and art stored in that folder; it does not publish anything.

## Still to design

Final rules/class progression, adventure-to-chapter progression, non-combat completion triggers, co-op and DM interactions, editor layout, and UI screens need design work. The file-based keep currently completes when all authored encounters have been won; chapters with no encounters remain explorable. The UI theme is transferable now, while arbitrary UI layouts are not yet interpreted as content.
