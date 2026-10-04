# Game design

Decisions from 2026-10-03. Items marked **default** are provisional: they ship until someone decides
otherwise, and wherever possible they are data, so a ruleset or an adventure can change them.

## What Yorehold is

A BG3-style game that is also a library of player-made adventures, like an online store of tabletop
adventures you can play. Anyone can make maps, adventures and whole campaigns, share them, and
combine them. People play them alone or together.

- Fantasy is the base (D&D or BG3 in feel). Each adventure sets its own tone.
- Players make the campaigns. There is no overworld outside adventures, but an adventure can have
  its own overworld.
- Choice over prescription: death, difficulty and table rules are options, with sensible defaults.

## Characters

- Characters belong to the player, not to an adventure: any character can go into any campaign.
- Starting a new adventure begins character creation at the adventure's recommended level. A player
  can bring an existing character instead.
- Classes and levels. Adventures normally cover levels 1 to 20 but can declare any range.

## Rules

Our own d20 system, written from scratch. It starts as 5e reworded (the 5.1 SRD is CC-BY-4.0, so the
mechanics can be reused with attribution) plus new spells of our own. Pathfinder 2e ideas are
welcome where they play better.

- **One ruleset for the whole game.** Every adventure uses the same rules, so characters move
  between them freely. The ruleset is updated over time based on player feedback and votes.
- **Players vote on** skills, classes, spells, races and the like. The game ships with a starting set.
- **Abilities:** six.
- **Skills:** about as many as D&D has. Until the first vote, dialogue checks use the current list;
  the surrender talk's Persuasion check is a stand-in.
- **Adventures add** their own creatures, items and NPCs, but not player options. New skills,
  classes, spells and races only come through the shared ruleset (**default**).
- **The ruleset is data** (files, versioned), so an update is a content release, not a code change.
  The built-in "modern" and "classic" rulesets are prototypes. Letting adventures choose a ruleset
  will go away.

## Scaling

Power grows as in Pathfinder 2e, not 5e:
- **Proficiency:** level plus a training rank (untrained, trained, expert, master, legendary), so
  numbers rise steadily from 1 to 20 (**default**).
- **Martial classes** (melee above all) do the most damage: the best weapon ranks, more damage dice
  as they level, and features that add to their hits.
- **Casters and skill classes** trade damage for utility: control, healing, buffs, mobility and
  answers to problems a sword can't solve.
- **Magic items:** each character can have at most 3. The limit is a number in the ruleset and can
  change. A fourth can't be picked up or used until one is given up.
- **Spellcasting:** spell slots by default. The system also supports other casting resources (spell
  points, focus, charges...) so classes and content can be built on them.

## Turns

- **Free movement every turn**, up to the character's speed, at any point in the turn.
- **Two actions per turn.** They are spent on the generic actions everyone has, plus class
  abilities, spells and items. Default generic actions (data in the ruleset):
  - Strike
  - Stride (move your speed again)
  - Defend
  - Help
  - Hide
  - Seek
  - Shove
  - Grapple
  - Interact or use an item
  - Ready
- **Hands set the cost.** A Strike or spell costs one action per hand it needs:
  - A dagger, shortsword or one-handed spell: 1 action.
  - A greatsword, bow or spell with two-handed components: 2 actions.
  - What each weapon and spell needs is data, so the rule holds for anything added later.
- **Hands must be free.** Something held in the other hand (a shield, a torch, a second weapon)
  limits what can be used (**default**). Changing grip is part of Interact.
- **Reaction:** one per round (**default**).
- **Repeated Strikes:** no penalty. Two Strikes in a turn hit as hard as one.

## Play

- Combat, talking and exploration are balanced as in 5e or Pathfinder 2e adventures.
- Looting, an economy and crafting follow those games as well.
- **Stealth works like BG3:**
  - A sneak toggle slows movement and shows each enemy's detection radius.
  - Inside a radius, the character makes a Stealth check against the enemy's passive Perception
    every 5 m, a distance the ruleset can change.
  - Getting spotted starts the fight.
  - Attacking unseen surprises the enemies, who lose their first turn.
- **Enemy AI:**
  - It comes in several kinds.
  - Each kind is set in data by the creature, the chapter, the story so far, or the server.
  - Enemies whose nerve breaks may run, run for help, surrender (and can then be talked to), or
    fight on.
  - More advanced decision models can be added without changing content.

## Death

- **Default:** permadeath. A dead character stays dead; the player makes a new one.
- **Option:** BG3 style, where the fallen can be revived at camp or with magic.
- **Party wipe:** handled by the adventure's or the table's settings (reload, retreat, or the
  campaign ends).

## Multiplayer

- **Party against the game:** co-op as now, with the host running the enemies.
- **DM mode:**
  - One player runs the enemies and NPCs.
  - The DM gets a brief on each enemy and scene from the adventure's written content: what it
    wants, how it fights, and what it knows.
  - The DM plays those creatures directly; the AI takes over anything the DM leaves to it.

## What this changes in the code

- `yh::Encounter`'s action, bonus action, reaction and movement budget becomes free movement plus
  two actions plus one reaction. The AI's "dash" becomes the Stride action.
- Character creation, character libraries and level ranges per adventure.
- Death options per table.
- DM seats and enemy briefs in the chapter format.
- Stealth: the detection radius, sneaking and checks, which also feed surprise.

## Open questions (defaults apply until answered)

1. Magic items: does the limit of 3 count only items in use (worn or held), or everything carried
   (**default:** in use, like attunement)?
2. Votes: on the website, per season (**default**)? What happens to existing characters when a vote
   removes or changes something they use (**default:** they get a free rebuild)?
3. Rests: short and long rests as in 5e (**default**)?
