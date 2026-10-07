# Playtests

A long list of things to look at by hand, opened one at a time already set up, with a window beside
the game to say what is wrong.

## Running them

Double-click `playtest.cmd` (or run `playtest.ps1`). It builds the game, then for each test in
`playtest/queue.json` that has no answer yet:

1. starts the game on the test's screen and chapter, and plays the test's input script up to its
   `until` frame, so the game is where the test starts (a door open, a fight on, a panel showing);
2. opens the playtest window beside the game: the test's name, what to try, a box for a note, and
   **Good**, **Problem** and **Skip**, then **Start this test again** and **Stop**;
3. on an answer, adds a line to `feedback/playtest-results.jsonl` (beside the repos) and, for a
   problem or any note, a picture of the game to `feedback/playtest-pictures/`, then opens the next.

Each test starts from nothing: its own empty characters, saves and settings under `.dev`, never the
player's. The game runs at 1280x720 and 60 frames a second, as the screenshot runs do, so the
scripts land where they should; the window can still be resized.

`playtest.ps1 -From fight` starts at a test, `-Again` goes through all of them, `-Problems` only
those whose last answer was a problem (to check fixes).

## The queue

`playtest/queue.json`: `{"items": [...]}` in the order they are played. Each item:

| Field | Is |
|---|---|
| `id` | short name, unique; answers are kept by it |
| `name` | what the window shows as the title |
| `area` | the part of the game, shown under the title |
| `try` | what to do and what to look for |
| `screen` | `title`, `play` (default) or `create` |
| `chapter` | the chapter folder to play, like `chapters/spell-test` (default: chapter one) |
| `script` | an input script (`tests/visual/scripts`), its `shot` lines skipped |
| `until` | the last frame of the script to play; the rest is the player's |

A test goes in the queue only once `check.ps1 -Playtest <id>` passes and its picture
(`.dev/playtest-<id>.png`) shows the test set up right. `check.ps1 -Playtest all` checks every one.

## The results

`feedback/playtest-results.jsonl`, one answer per line, newest last:
`{"id", "name", "verdict": "good" | "problem" | "skip", "note", "picture", "at"}`. The last line for
an id is the one that counts. Fixes are checked with `-Problems`.
