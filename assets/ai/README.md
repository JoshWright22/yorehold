# AI profiles

Each `.json` file here is one way of thinking that creatures can be given by name. Add a file to add
a new one; edit a file while the game is open and it takes effect on the next turn.

A profile starts from another one (`"base": "cunning"`) or from nothing, then sets any of these:

| Number | What it makes the creature care about |
|---|---|
| `damage` | dealing as much damage as it can |
| `finish` | landing a blow that will drop its target |
| `weak` | targets that are already hurt |
| `isolated` | targets with none of their friends next to them |
| `pack` | targets its own allies are already fighting |
| `nearby` | targets it doesn't have to walk far to reach |
| `danger` | not ending its turn where it will take a lot of damage |
| `random` | nothing: this is how often it makes a poor choice |
| `fleeHp` | runs at or below this share of its HP (0 = never, 1 = at once) |
| `fleeLosses` | runs once this share of its side is down (above 1 = never) |
| `fleeLeaderless` | `true`: runs once its side's leader is down |
| `leader` | `true`: it is a leader for its allies |
| `escapeAt` | squares from the nearest hero, out of sight, at which a runner gets away |

## Where an AI can be set

Each of these goes on top of the one before. An entry is a profile's name (`"ai": "coward"`) or just
the numbers to change (`"ai": {"fleeHp": 0.6}`).

1. The creature: `creatures/<id>.json`
2. A whole encounter: `"ai"` on an entry of `encounters` in `chapter.json`
3. One placed creature or NPC: `"ai"` on that entry in `chapter.json`
4. The story: `aiChanges` in `chapter.json`, which apply once their story flags are set:
   `{"when": ["chief_insulted"], "encounter": "chiefs-hall", "creature": "goblin", "ai": "brute"}`
5. The server: its `ai` settings can replace a profile or change a kind of creature for everyone,
   without a game update.

Press F8 in a fight to see which profile each creature used and what it chose.