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
- Choice over prescription: difficulty and table rules are options, with sensible defaults.
- **2D top-down on a grid**, like a virtual tabletop (Foundry). Combat and movement use squares.
- **Everything is community content.** Classes, spells, races, items, maps and adventures can all be
  made by players, shared as files, voted on and ranked (see Community).

## Characters

- Characters belong to the player, not to an adventure: any character can go into any campaign.
- Starting a new adventure begins character creation at the adventure's recommended level. A player
  can bring an existing character instead.
- Classes and levels. Adventures normally cover levels 1 to 20 but can declare any range.
- **Creation:** background + race + class.
- **Ability scores:** point buy, rolling or a standard array, whichever the player likes.
- **Feats** at set levels, PF2e style (class, skill, general and race feats). There are fewer of
  them than in Pathfinder, and each one should be interesting rather than a small number bump.
- **Multiclassing** as in BG3: any level can go into any class.
- **Party:** up to 4 player characters for now. One player can run several.
- **Companions:** an adventure can include NPCs who join the party, with their own approval and
  story as in BG3. They can take the party past 4.
- **Launch content:** 4 classes and 4 races is the starting goal. The full SRD set (12 classes,
  9 races) comes after.
- **Loot comes along.** Characters keep everything between adventures, whatever the level range.
  Matching gear to an adventure is up to the players: they can agree to bring the right gear, or
  make a character for that game.

## Rules

Our own d20 system, written from scratch. It starts as 5e reworded (the 5.1 SRD is CC-BY-4.0, so the
mechanics can be reused with attribution) plus new spells of our own. Pathfinder 2e ideas are
welcome where they play better.

- **One ruleset for the whole game.** Every adventure uses the same rules, so characters move
  between them freely. The ruleset is updated over time based on player feedback and votes.
- **Players vote on** skills, classes, spells, races and the like. The game ships with a starting set.
- **Ruleset updates retire old characters.** When a new ruleset version comes out:
  - Characters built on the old version go to the graveyard. They stay viewable but can't be played.
  - A new version of each one is made automatically under the new rules, as close to the old build
    as the new rules allow. The player plays that one from then on.
  - Where the new rules can't keep a choice (a removed class, say), the player picks a replacement
    before playing.
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
- **Magic items:** each character can have at most 3, counting everything in their inventory, not
  just what is worn or held. The limit is a number in the ruleset and can change. A fourth can't be
  picked up until one is given up.
- **Magic items are rare and special.** Most upgrades are not magical: a better sword or better
  armour is just better gear and doesn't count toward the limit.

## Magic

- **Spell slots**, cantrips that scale with level, and focus spells.
- **Prepared and spontaneous casting:** both of Pathfinder's styles are supported, so a class can
  use either. Other resources (spell points, charges...) are also possible for new classes.
- **Spell lists belong to classes**, not shared traditions. Adding a spell to the warlock's and
  wizard's lists doesn't touch the druid's, so votes on one class never change another.

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
- **Reaction:** one per round: opportunity attacks, Shield Block, readied actions and the like.
- **Opportunity attacks:** leaving an enemy's reach provokes one, free movement included
  (**default:** everyone can make them).
- **Repeated Strikes:** no penalty. Two Strikes in a turn hit as hard as one.
- **Turn order as in BG3:** everyone rolls initiative. Party members whose turns come back to back
  share that turn and can act in any order, and so can enemies.

## Checks and teamwork

- **Degrees of success:** a natural 1 is a critical failure and a natural 20 a critical success.
  Anything else simply succeeds or fails against the DC or AC.
- **Flanking:** an enemy with a foe on each side is off-guard (-2 AC).
- **Cover:** +2 for half cover, +4 for three-quarters.
- **Teamwork is the point**, as in Pathfinder. Classes and encounters are designed so that fights go
  badly without setup: flanking, debuffs from spells and skills (frightened, off-guard, prone,
  slowed), and buffs. A party that just trades blows will struggle.
- **Dialogue checks** are rolled by whoever is speaking, as in BG3.

## Play

- Combat, talking and exploration are balanced as in 5e or Pathfinder 2e adventures.
- Looting and an economy (gp, sp, cp) follow those games as well.
- **No crafting for now**, as in BG3. It may come later.
- **Encumbrance:** the more weight a character carries, the slower they move, so nobody picks up
  everything. Coins weigh nothing.
- **Inventory as in BG3:**
  - Each character has their own inventory, and the camp has a shared stash.
  - A player manages the characters they control.
  - Players can't change other players' inventories, but they can give them items.
- **Stealth works like BG3:**
  - Enemies see in vision cones, shown while sneaking.
  - A sneak toggle slows movement.
  - Inside a cone, the character makes a Stealth check against the enemy's passive Perception
    every 5 m, a distance the ruleset can change.
  - Light matters: darkness and shadow make hiding easier, bright light harder.
  - Getting spotted starts the fight.
  - Attacking unseen surprises the enemies, who lose their first turn.
- **Enemy AI:**
  - It comes in several kinds.
  - Each kind is set in data by the creature, the chapter, the story so far, or the server.
  - Enemies whose nerve breaks may run, run for help, surrender (and can then be talked to), or
    fight on.
  - More advanced decision models can be added without changing content.
- **Levelling** is by XP, mostly from encounters. The adventure sets how much each one gives.

## Rest and death (as in BG3)

These follow BG3 for now and may be revisited.

- **Short rests:** 2 between long rests. They heal some HP and restore short-rest abilities.
- **Long rests** happen at camp and use up camp supplies. Supplies are a resource: food found and
  bought while adventuring. A long rest restores HP, spell slots and daily abilities.
  - The party can rest anywhere outside combat, unless the area forbids it.
  - The cost is a number in the ruleset (**default:** 40 supplies, as in BG3).
- **Going down:** a character at 0 HP is downed and makes death saving throws. Allies can get them
  up with Help, healing or a potion.
- **Death isn't permanent.** The dead are revived with a scroll of revivify (found or bought), a
  spell, or at camp for a price. Scrolls save the trip back.
- **Party wipe:** go back to the last autosave (**default**). An adventure can choose something
  else, such as waking up captured.

## Saving

- **Autosave only.** Players never have to save or manage files.
- **Saves go to every player's account**, not just the host's, so anyone in the group can carry the
  game on.
- Not decided: letting players load back to each major encounter. It is probably a bad idea.

## Multiplayer

- **Party against the game:** up to 4 players, with the host running the enemies.
- **DM mode** (built last):
  - Up to 4 players plus a DM for now; more if people want it.
  - The DM runs the enemies and NPCs. For each enemy and scene they get a brief from the
    adventure's written content: what it wants, how it fights, and what it knows.
  - The DM plays those creatures directly; the AI takes over anything the DM leaves to it.
  - The DM can also change things live (spawn creatures, change DCs, hand out loot). The players
    see a log of every change.

## Making adventures

- **Writing comes first.** Writers build an adventure from story nodes: scenes, encounters,
  dialogue and quests, linked together, each with its writing.
- **The editor makes suggestions from the nodes.** It proposes:
  - maps and layouts for the scenes and their triggers
  - XP for enemies and encounters
  - dialogue and quest steps
  Every suggestion is a starting point the writer edits freely.
- **Difficulty:** an adventure has one difficulty by default. A writer can add more if they want;
  the editor may suggest them but never pushes.
- Everything an adventure uses is files, so it can be shared, combined and packaged (see
  CONTENT.md).

## Community

- **Votes:** anyone with an account can upvote or downvote anything, in the game or on the website.
- **Canon:** content that ranks above a threshold in its category also needs 1 to 3 approved users
  (curators; the name isn't settled) to sign off before it becomes canon. Canon content goes into
  the shared ruleset or the featured library.
- **Thresholds** are set by the project team, per category.
- **Who approves:**
  - Approvers belong to a category (classes, spells, maps...).
  - Only a category's approvers can make someone else an approver there, and only someone who
    already has content approved in that category.
  - The project team are the first approvers in every category.
  - An approver loses the role by a vote of the category's other approvers, or after a year of
    inactivity.
- **Price:** everything is free for now. Selling adventures or taking tips may be added later if
  creators want it.
- **Homebrew is the point:** everything in the game can be made by players, submitted, ranked and
  passed around as files.

## What this changes in the code

- Done: a turn in `yh::Encounter` is free movement plus the ruleset's actions (two) plus one
  reaction, and a Strike costs the weapon's hands. The AI can Stride then Strike, or Strike twice.
  Still to do: the other generic actions, and opportunity attacks spending the reaction.
- Done: vision cones, the sneak toggle, light and sneak checks, and ambushes that surprise.
- Shared turns for side-by-side initiative.
- Character creation with multiclassing, character libraries, and level ranges per adventure.
- Camp, supplies and long rests; downed, death saves and revival.
- Per-character inventories, giving items, and the camp stash.
- DM seats and enemy briefs in the chapter format.
- The node-based writing tool and its suggestions.

## Open questions (defaults apply until answered)

1. Saving: can players load back to the start of each major encounter (**default:** no, only the
   latest autosave)?
2. Companions: is there a cap on party size once companions join (**default:** 4 player characters
   plus up to 2 companions)?
3. Encumbrance: how much can a character carry before slowing down (**default:** as in BG3, from
   Strength)?
