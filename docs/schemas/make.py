"""Writes the JSON schemas in docs/schemas/ that let an editor (VS Code and others) check and
complete Yorehold's content files as people type. The game's own readers stay the authority:
a file the schema passes can still be refused by the game, with the file and field named.

    py -3.12 docs/schemas/make.py

The field lists follow the readers in rules/content/; change both together.
"""
import json, os

HERE = os.path.dirname(os.path.abspath(__file__))
ID = {"type": "string", "pattern": "^[a-z0-9_-]{1,64}$", "description": "a-z, 0-9, - and _; the file name without .json"}
TEXT = {"type": "string"}
INT = {"type": "integer"}
NUM = {"type": "number"}
BOOL = {"type": "boolean"}
IDS = {"type": "array", "items": ID}
TEXTS = {"type": "array", "items": TEXT}
FORMULA = {"type": "string", "description": "a formula: numbers, names (level, mod.str, stat.ac, scale.x), + - * / %, comparisons, && || !, ?:, max min floor ceil clamp; Foundry's @abilities.str.mod reads too"}
DICE = {"type": "string", "description": "dice such as 2d6+1, with formulas in braces: 2d8+{mod.wis}; Foundry's @paths read too"}
NUMBERS = {"type": "object", "additionalProperties": INT}

MODIFIER = {
    "type": "object", "required": ["stat", "value"], "additionalProperties": False,
    "properties": {
        "stat": {"type": "string", "description": "the number it changes: ac, attack, damage, saves, checks, speed, maxHp, initiative, an ability, resist.fire..."},
        "op": {"enum": ["add", "multiply", "override", "max", "min"], "description": "max: at least this much; min: at most"},
        "value": NUM,
        "type": {"type": "string", "description": "typed bonuses don't stack: only the best of each type counts"},
        "if": dict(FORMULA, description="makes it situational: it counts only on a roll this holds for (ranged, save.dex, trait.agile, targetFlag.undead)"),
    },
}
MODIFIERS = {"type": "array", "items": MODIFIER}

STEP = {"$ref": "#/$defs/step"}
STEPS = {"type": "array", "items": STEP}
SCALE = {"type": "object", "additionalProperties": False, "properties": {
    "by": {"enum": ["level", "slot"]}, "from": INT, "every": INT, "dice": DICE, "value": INT}}
STEP_DEF = {
    "type": "object", "required": ["do"],
    "properties": {
        "do": {"enum": ["damage", "heal", "tempHp", "condition", "modifier", "move", "resource", "summon", "light", "surface", "flag", "roll", "repeat", "choose"]},
        "target": {"enum": ["self", "target", "area", "allies", "enemies"]},
        "when": {"type": "string", "description": "an outcome of the roll above it (hit, miss, crit, saveFailed, success, criticalFailure...) or an event"},
        "ifFlag": TEXT, "scale": SCALE, "onSave": {"enum": ["half", "none", "full"]},
        "dice": {"type": ["string", "integer"], "description": DICE["description"]}, "type": TEXT, "crit": DICE, "minimum": INT, "track": TEXT,
        "id": TEXT, "remove": BOOL, "duration": INT, "value": INT,
        "stat": TEXT, "op": TEXT,
        "how": {"type": "string", "description": "a move: push, pull, teleport, approach; a resource: spend, restore"},
        "distance": {"type": ["integer", "string"]}, "amount": {"type": ["integer", "string"]},
        "count": INT, "radius": NUM, "size": NUM,
        "kind": {"type": "string", "description": "a roll: attack, check, save, or one of the system's roll kinds"},
        "ability": TEXT, "dc": {"type": ["integer", "string"], "description": "a number, or \"caster\""}, "against": TEXT, "reach": INT,
        "steps": STEPS, "times": {"type": "string"},
        "options": {"type": "array", "items": {"type": "object", "properties": {"name": TEXT, "steps": STEPS}}},
    },
}
SAVE = {"type": "object", "additionalProperties": False, "properties": {"ability": TEXT, "dc": {"type": ["integer", "string"]}}}
TARGET = {"type": "object", "additionalProperties": False, "properties": {
    "kind": {"enum": ["self", "creature", "point"]}, "side": {"enum": ["enemy", "ally", "any"]},
    "range": {"type": ["integer", "string"], "description": "squares, or \"weapon\": as far as the weapon in hand"}, "downed": BOOL}}
AREA = {"type": "object", "additionalProperties": False, "properties": {
    "shape": {"enum": ["burst", "cone", "line"]}, "size": NUM, "width": NUM, "angle": NUM}}
ACTION_PROPS = {
    "id": ID, "name": TEXT, "description": TEXT, "order": INT,
    "cost": {"type": ["integer", "string"], "description": "actions it takes, or \"bonus\"; a weapon's hands if left out"},
    "endsTurn": BOOL, "general": {"type": "boolean", "description": "false: only those granted it have it"},
    "readies": TEXT, "secret": {"type": "boolean", "description": "its rolls aren't thrown on screen"},
    "requires": {"type": "object", "additionalProperties": False, "properties": {"flags": TEXTS, "without": TEXTS, "resources": NUMBERS}},
    "target": TARGET, "area": AREA, "log": TEXT, "save": SAVE, "effects": STEPS,
}
GRANTS = {
    "modifiers": MODIFIERS, "proficiencies": IDS, "ranks": {"type": "object", "additionalProperties": TEXT},
    "resources": NUMBERS, "actions": {"type": "array", "items": TEXT, "description": "the system's granted-only actions, reactions and triggers"},
    "spells": dict(IDS, description="spells it knows from this: cantrips at will, others with a slot"),
}

def schema(title, properties, required=("id",), strict=True, defs=None):
    s = {"$schema": "https://json-schema.org/draft/2020-12/schema", "title": title, "type": "object",
         "required": list(required), "properties": properties}
    if strict:
        s["additionalProperties"] = False
    if defs:
        s["$defs"] = defs
    return s

KINDS = {
    "item": schema("A Yorehold item (items/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "slot": {"enum": ["", "mainHand", "offHand", "armor", "ring"]},
        "hands": INT, "damage": DICE, "damageType": TEXT, "attackAbility": TEXT, "traits": TEXTS,
        "range": dict(INT, description="squares a weapon reaches; 1 by default"), "weight": NUM, "value": dict(INT, description="copper"),
        "quantity": INT, "magic": BOOL, "supplies": INT, "modifiers": MODIFIERS,
        "actions": dict(TEXTS, description="granted-only actions it gives while worn or held"),
        "use": {"type": "object", "description": "what using it up does: an action's fields"},
    }, strict=False),
    "creature": schema("A Yorehold creature (creatures/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "hp": INT, "armorClass": INT, "speed": INT, "level": INT, "darkvision": INT,
        "abilities": NUMBERS, "stats": {"type": "object", "additionalProperties": NUM},
        "proficiencies": IDS, "proficiencyRanks": {"type": "object", "additionalProperties": TEXT},
        "dcAbility": TEXT, "deathSaves": BOOL, "resources": {"type": "object"}, "items": IDS, "spells": IDS, "actions": TEXTS,
        "loot": {"type": "object", "properties": {"coins": DICE, "items": {"type": "array"}}},
        "token": {"type": "object", "properties": {"color": {"type": "array", "items": INT}, "size": NUM, "image": TEXT}},
        "ai": {"type": ["string", "object"], "description": "an AI profile's name (cunning, brute, animal...) or changes to one"},
    }, strict=False),
    "action": schema("A Yorehold action (actions/<id>.json)", ACTION_PROPS, defs={"step": STEP_DEF}),
    "spell": schema("A Yorehold spell (spells/<id>.json)", dict(ACTION_PROPS, **{
        "level": dict(INT, description="0 = cantrip"), "hands": INT, "concentration": BOOL, "spends": NUMBERS}), defs={"step": STEP_DEF}),
    "condition": schema("A Yorehold condition (conditions/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "flags": TEXTS, "modifiers": MODIFIERS, "duration": INT,
        "ends": dict(TEXTS, description="events that end it: turnStart, turnEnd, attack, damage, move, rest, fightEnd, healed..."),
        "stacking": {"enum": ["refresh", "longest", "value"]}, "maxValue": INT, "perValue": BOOL, "decay": INT, "removes": IDS,
        "save": {"type": "object", "properties": {"ability": TEXT, "dc": INT}},
        "advantageOnAttacks": BOOL, "disadvantageOnAttacks": BOOL, "attackersAdvantage": BOOL, "attackersDisadvantage": BOOL,
        "attackersWithin": INT, "attackersBeyond": {"type": ["string", "object"]}, "hitsAreCritical": BOOL, "attackersFlatCheck": INT,
        "advantageOnChecks": BOOL, "disadvantageOnChecks": BOOL,
    }, strict=False),
    "feat": schema("A Yorehold feat (feats/<id>.json)", dict({
        "id": ID, "name": TEXT, "description": TEXT, "kind": dict(TEXT, description="one of the system's featKinds"), "repeatable": BOOL,
        "requires": {"type": "object", "additionalProperties": False, "properties": {
            "level": INT, "races": IDS, "classes": IDS, "abilities": NUMBERS, "proficiencies": IDS}},
    }, **GRANTS)),
    "option": schema("A Yorehold option: a heritage, subclass... (options/<id>.json)", dict({
        "id": ID, "name": TEXT, "description": TEXT, "kind": dict(TEXT, description="one of the system's optionKinds"),
        "races": IDS, "classes": IDS, "feats": IDS}, **GRANTS), required=("id", "kind")),
    "race": schema("A Yorehold race, species or ancestry (races/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "speed": INT, "darkvision": INT, "bonusHp": INT,
        "abilities": NUMBERS, "proficiencies": IDS, "feats": IDS}),
    "background": schema("A Yorehold background (backgrounds/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "abilities": NUMBERS, "proficiencies": IDS, "feats": IDS, "items": IDS}),
    "trigger": schema("A Yorehold trigger (triggers/<id>.json)", {
        "id": ID, "name": TEXT, "description": TEXT, "on": {"enum": ["hit", "miss", "crit", "hitBy", "kill", "turnStart"]},
        "if": FORMULA, "once": {"enum": ["turn"]}, "general": BOOL, "save": SAVE, "effects": STEPS}, defs={"step": STEP_DEF}),
    "reaction": schema("A Yorehold reaction (reactions/<id>.json)", {
        "id": ID, "name": TEXT, "trigger": {"enum": ["leavesReach", "entersReach", "hit", "missed", "allyHit", "beforeHit", "spellCast"]},
        "action": TEXT, "spell": TEXT, "readied": BOOL, "order": INT, "promptSeconds": NUM, "general": BOOL, "unless": TEXT}),
}
CLASS_ROW = {"type": "object", "additionalProperties": False, "properties": {
    "features": {"type": "array", "items": {"type": "object", "required": ["id"], "additionalProperties": False,
                 "properties": dict({"id": ID, "name": TEXT, "description": TEXT}, **GRANTS)}},
    "ranks": {"type": "object", "additionalProperties": TEXT}, "feats": dict(TEXTS, description="the feat kinds picked at this level"),
    "skills": INT, "slots": NUMBERS, "spells": INT, "boosts": INT, "boostStep": INT, "boostsRepeat": BOOL,
    "options": dict(TEXTS, description="option kinds picked at this level (a subclass)"),
    "scale": dict(NUMBERS, description="named numbers reached at this level, read as scale.<id>")}}
KINDS["class"] = schema("A Yorehold class (classes/<id>.json)", {
    "id": ID, "name": TEXT, "description": TEXT, "hitDie": INT, "bonusHp": INT, "speed": INT, "darkvision": INT, "dcAbility": TEXT,
    "proficiencies": IDS, "proficiencyRanks": {"type": "object", "additionalProperties": TEXT}, "items": IDS, "resources": {"type": "object"},
    "casting": {"enum": ["known", "prepared", "spontaneous"]}, "spells": {"type": "object", "additionalProperties": IDS},
    "levels": {"type": "array", "items": CLASS_ROW}}, strict=False)

for kind, s in KINDS.items():
    with open(os.path.join(HERE, kind + ".schema.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(s, f, indent=2)
        f.write("\n")
    print(kind)

# which files each schema checks, for an editor's settings
folders = {"item": "items", "creature": "creatures", "action": "actions", "spell": "spells", "condition": "conditions", "feat": "feats",
           "option": "options", "race": "races", "background": "backgrounds", "trigger": "triggers", "reaction": "reactions", "class": "classes"}
settings = {"json.schemas": [{"fileMatch": [f"**/{folder}/*.json"], "url": f"./docs/schemas/{kind}.schema.json"} for kind, folder in folders.items()]}
with open(os.path.join(HERE, "vscode-settings.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(settings, f, indent=2)
    f.write("\n")
