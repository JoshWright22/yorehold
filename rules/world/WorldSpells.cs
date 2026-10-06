using System.Numerics;

namespace Yorehold.Rules;

/// <summary>A surface lying on the map: fire, ice, water... around a cell for some rounds.</summary>
public sealed class SurfacePatch
{
    public string Id { get; init; } = "";
    public Cell At { get; init; }
    /// <summary>Radius in squares.</summary>
    public float Size { get; set; }
    public int RoundsLeft { get; set; }
}

// Spells: where their areas fall, casting them in and out of fights, preparing, concentration,
// and the surfaces some of them leave.
public sealed partial class World
{
    /// <summary>World seconds that make a round between fights, for surfaces to run out.</summary>
    public const double SecondsPerRound = 6;

    private double _roundClock;
    // where the action being done was aimed, for steps that leave something on the map
    private Cell? _aim;

    public List<SurfacePatch> Surfaces { get; } = new();

    public SpellRules SpellRules => Chapter.Rules.Spellcasting;

    public SpellDefinition? FindSpell(string id) => Chapter.Compendium.Spells.GetValueOrDefault(id);

    /// <summary>The spell this action is, when it is one.</summary>
    public SpellDefinition? SpellOf(ActionDefinition action)
    {
        SpellDefinition? spell = FindSpell(action.Id);
        return spell != null && ReferenceEquals(spell.Action, action) ? spell : null;
    }

    // ---------------------------------------------------------------- areas

    /// <summary>The area of an action by creature aimed at aim (a cone or line from the creature, a burst on the aim).</summary>
    public AreaTemplate? AreaOf(int creature, ActionDefinition action, Cell aim)
    {
        if (action.Area == null || creature < 0 || creature >= Creatures.Count)
        {
            return null;
        }
        return AreaTemplate.Place(action.Area, Grid, Grid.Center(CellOf(creature)), Grid.Center(aim));
    }

    /// <summary>The squares an area would cover, for the screen to show before it is used.</summary>
    public List<Cell> AreaCells(int creature, ActionDefinition action, Cell aim)
    {
        return AreaOf(creature, action, aim)?.Cells(Grid).Where(c => c.X >= 0 && c.Y >= 0 && c.X < Map.Width && c.Y < Map.Height).ToList()
            ?? new List<Cell>();
    }

    /// <summary>
    /// Who an area lands on: those inside it on the action's side, with a clear line from where it
    /// starts. In a fight whoever is in it; between fights only the party.
    /// </summary>
    public List<int> CreaturesIn(int me, ActionDefinition action, Cell aim)
    {
        var inside = new List<int>();
        AreaTemplate? area = AreaOf(me, action, aim);
        if (area == null)
        {
            return inside;
        }
        List<Cell> cells = area.Cells(Grid);
        Cell origin = Grid.CellAt(area.Origin);
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            if (i == me && action.Area!.Directed)
            {
                continue; // a cone or line starts at the doer's feet
            }
            if (Fighting)
            {
                if (OrderIndex(i) is not int index || Encounter!.Order[index].Out)
                {
                    continue;
                }
            }
            else if (i >= HeroCount)
            {
                continue;
            }
            if (c.Fled || c.Sheet.Death.Dead || c.Sheet.HasFlag(Rules, "dead") || (c.Sheet.Down && !action.AllowsDowned))
            {
                continue;
            }
            bool sameSide = c.Team == Creatures[me].Team;
            if ((action.Side == ActionSide.Enemy && sameSide) || (action.Side == ActionSide.Ally && !sameSide))
            {
                continue;
            }
            Cell at = CellOf(i);
            if (!cells.Contains(at) || (at != origin && !Sight.LineOfSight(area.Origin, Grid.Center(at), Map.Walls)))
            {
                continue; // walls stop it
            }
            inside.Add(i);
        }
        return inside;
    }

    /// <summary>A square an action aimed at the map may go at: on the map, in range, in sight; a cone or line anywhere but the doer's own square.</summary>
    public bool ValidAim(int me, ActionDefinition action, Cell at, out string why)
    {
        why = "";
        if (action.Target != ActionTarget.Point || action.Area == null || me < 0 || me >= Creatures.Count)
        {
            return false;
        }
        if (at.X < 0 || at.Y < 0 || at.X >= Map.Width || at.Y >= Map.Height)
        {
            why = "That is off the map.";
            return false;
        }
        Cell here = CellOf(me);
        if (action.Area.Directed)
        {
            why = at == here ? "Aim it away from yourself." : "";
            return at != here;
        }
        if (Grid.Distance(here, at) > action.Range + 0.01f)
        {
            why = "That spot is out of range.";
            return false;
        }
        if (at != here && !Sight.LineOfSight(Grid.Center(here), Grid.Center(at), Map.Walls))
        {
            why = "There is no clear line to that spot.";
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- casting between fights

    /// <summary>
    /// A hero can cast a spell they know on a party member between fights: helpful spells only,
    /// so damage can't get round initiative. why says what is wrong.
    /// </summary>
    public bool CanCast(int hero, string id, int target, out string why)
    {
        why = "";
        SpellDefinition? spell = FindSpell(id);
        if (spell == null || hero < 0 || hero >= HeroCount || target < 0 || target >= Creatures.Count || Fighting || InCutscene || PartyWiped)
        {
            why = "That spell cannot be cast now.";
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        if (!sheet.Spells.Contains(spell.Id))
        {
            why = "They do not know that spell.";
            return false;
        }
        if (sheet.Down || sheet.HasFlag(Rules, "cantAct") || Tokens.Tokens[hero].Path.Count > 0)
        {
            why = "Wait until that hero can act.";
            return false;
        }
        ActionDefinition action = spell.Action;
        bool aimed = action.Target != ActionTarget.Self || action.Area != null;
        if ((action.Side == ActionSide.Enemy && aimed) || action.Target == ActionTarget.Point || target >= HeroCount)
        {
            why = "Cast that during a fight.";
            return false;
        }
        if (!Spellcasting.CanCast(sheet, spell, SpellRules, out string lacks))
        {
            why = $"{action.Name} {lacks}.";
            return false;
        }
        if (action.Target == ActionTarget.Self)
        {
            why = target == hero ? "" : "Cast that on yourself.";
            return target == hero;
        }
        CharacterSheet subject = Creatures[target].Sheet;
        if (subject.Death.Dead || subject.HasFlag(Rules, "dead") || (subject.Down && !action.AllowsDowned))
        {
            why = "That spell cannot help them.";
            return false;
        }
        if (target != hero && (!InRange(hero, action, target) || !Sight.LineOfSight(Grid.Center(CellOf(hero)), Grid.Center(CellOf(target)), Map.Walls)))
        {
            why = "Stand within clear reach of the target.";
            return false;
        }
        return true;
    }

    /// <summary>Casts a spell between fights, free of actions but spending its slot: the lowest that will do, or slot when it is asked for.</summary>
    public bool Cast(int hero, string id, int? target = null, int slot = 0)
    {
        Refusal = "";
        int aimed = target ?? hero;
        if (!CanCast(hero, id, aimed, out string why))
        {
            Refusal = why;
            return false;
        }
        SpellDefinition spell = FindSpell(id)!;
        if (Spellcasting.SlotFor(Creatures[hero].Sheet, spell, SpellRules, slot) is not int spent)
        {
            Refusal = $"{spell.Action.Name}: no slot of level {slot} left.";
            return false;
        }
        CastSpell(hero, spell, aimed, null, spent);
        return true;
    }

    /// <summary>Everyone the hero could cast the spell on now, between fights.</summary>
    public List<int> CastTargets(int hero, string id)
    {
        return Enumerable.Range(0, HeroCount).Where(t => CanCast(hero, id, t, out _)).ToList();
    }

    // ---------------------------------------------------------------- preparing

    /// <summary>A prepared caster may choose its spells between fights, until a fight starts; after that, after the rest the rules name.</summary>
    public bool CanPrepare(int hero, IReadOnlyList<string> spells, out string why)
    {
        why = "";
        if (hero < 0 || hero >= HeroCount || Fighting || InCutscene)
        {
            why = "Spells are prepared between fights.";
            return false;
        }
        WorldCreature c = Creatures[hero];
        if (c.Sheet.Preparable.Count == 0)
        {
            why = "They do not prepare spells.";
            return false;
        }
        if (c.Sheet.Death.Dead)
        {
            why = "They cannot prepare spells now.";
            return false;
        }
        if (!c.MayPrepare)
        {
            RestDefinition? rest = SpellRules.PrepareAfter.Count == 0 ? null : Rules.Rests.Find(r => r.Id == SpellRules.PrepareAfter[0]);
            string name = rest == null ? "" : (rest.Name.Length == 0 ? rest.Id : rest.Name).ToLowerInvariant();
            why = rest != null ? $"Spells can be chosen again after a {name}." : "Spells can't be chosen again in this adventure.";
            return false;
        }
        CharacterSheet trial = c.Sheet.Copy();
        if (!trial.Prepare(spells, out string reason))
        {
            why = $"{c.Sheet.Name} {reason}.";
            return false;
        }
        return true;
    }

    public bool Prepare(int hero, IReadOnlyList<string> spells)
    {
        Refusal = "";
        if (!CanPrepare(hero, spells, out string why))
        {
            Refusal = why;
            return false;
        }
        CharacterSheet sheet = Creatures[hero].Sheet;
        sheet.Prepare(spells, out _);
        Say($"{sheet.Name} prepares {string.Join(", ", sheet.Prepared.Select(id => FindSpell(id)?.Action.Name ?? id))}.");
        return true;
    }

    // ---------------------------------------------------------------- casting and concentration

    internal void CastSpell(int caster, SpellDefinition spell, int? target, Cell? at, int slot)
    {
        Spellcasting.SpendCasting(Creatures[caster].Sheet, spell, SpellRules, slot);
        Say($"{Creatures[caster].Sheet.Name} casts {spell.Action.Name}{(spell.Spends.Count == 0 && slot > spell.Level ? $" from a level {slot} slot." : ".")}");
        // One spell at a time: a new one that needs concentration ends the old.
        if (spell.Concentration)
        {
            EndConcentration(caster, "to cast another");
        }
        EffectResult result = RunActionEffect(caster, spell.Action, target, at, slot);
        if (!spell.Concentration)
        {
            return;
        }
        WorldCreature c = Creatures[caster];
        c.Concentration = Concentration.Begin(spell.Id, result);
        // Nothing took hold (everyone saved), or the casting dropped the caster: nothing to hold.
        if (c.Concentration.Tidy(SheetOf) && !(c.Sheet.Down && SpellRules.EndsWhenDown))
        {
            Say($"{c.Sheet.Name} concentrates on {spell.Action.Name}.");
        }
        else
        {
            c.Concentration = new Concentration();
        }
    }

    private CharacterSheet? SheetOf(int who) => who >= 0 && who < Creatures.Count ? Creatures[who].Sheet : null;

    /// <summary>Lets go of what a creature concentrates on, taking off everything it held.</summary>
    public void EndConcentration(int creature, string why)
    {
        WorldCreature c = Creatures[creature];
        if (!c.Concentration.Active)
        {
            return;
        }
        string name = FindSpell(c.Concentration.Spell)?.Action.Name ?? c.Concentration.Spell;
        List<Concentration.Hold> removed = c.Concentration.End(SheetOf);
        Say($"{c.Sheet.Name} stops concentrating on {name}{(why.Length == 0 ? "" : $" ({why})")}.");
        foreach (Concentration.Hold hold in removed)
        {
            // modifiers have no name of their own; conditions are told as they are elsewhere
            if (Rules.Condition(hold.Id) is ConditionDefinition condition)
            {
                Say($"{Creatures[hold.Who].Sheet.Name} is no longer {condition.Name}");
            }
        }
    }

    // One check for all the damage an effect did to each concentrating creature.
    private void ConcentrationChecks(EffectResult result, Rng random)
    {
        var hurt = new SortedDictionary<int, int>();
        foreach (EffectEvent e in result.Events)
        {
            if (e.Kind == EffectEventKind.Damage && e.Amount > 0 && e.Who >= 0 && e.Who < Creatures.Count)
            {
                hurt[e.Who] = hurt.GetValueOrDefault(e.Who) + e.Amount;
            }
        }
        foreach ((int who, int amount) in hurt)
        {
            WorldCreature c = Creatures[who];
            if (!c.Concentration.Active || c.Sheet.Down)
            {
                continue; // someone who fell is dealt with by TidyConcentration
            }
            ConcentrationCheck check = Spellcasting.CheckConcentration(c.Sheet, Rules, SpellRules, amount, random);
            if (check.Rolled)
            {
                Say($"{c.Sheet.Name} holds concentration ({SpellRules.SaveAbility}, DC {check.Dc}): {check.Roll.Describe()}{(check.Kept ? " - held" : " - lost")}");
            }
            if (!check.Kept)
            {
                EndConcentration(who, "hurt");
            }
        }
    }

    // Whoever dropped lets their spell go; one with nothing left to hold is over.
    private void TidyConcentration()
    {
        for (int i = 0; i < Creatures.Count; i++)
        {
            WorldCreature c = Creatures[i];
            if (!c.Concentration.Active)
            {
                continue;
            }
            if (c.Sheet.Down && SpellRules.EndsWhenDown)
            {
                EndConcentration(i, "down");
                continue;
            }
            string name = FindSpell(c.Concentration.Spell)?.Action.Name ?? c.Concentration.Spell;
            if (!c.Concentration.Tidy(SheetOf))
            {
                Say($"{c.Sheet.Name}'s {name} has run its course.");
            }
        }
    }

    // ---------------------------------------------------------------- surfaces

    /// <summary>The surfaces that cover a cell.</summary>
    public List<SurfacePatch> SurfacesAt(Cell cell)
    {
        return Surfaces.Where(s => Covers(s, cell)).ToList();
    }

    /// <summary>
    /// Lays a surface around a cell for some rounds. One of the same kind already there grows to
    /// the larger size and time; whatever it puts out (or what puts it out) goes where they meet.
    /// </summary>
    public bool AddSurface(string id, Cell at, float size, int rounds)
    {
        SurfaceDefinition? kind = Rules.Surface(id);
        if (kind == null || size <= 0 || at.X < 0 || at.Y < 0 || at.X >= Map.Width || at.Y >= Map.Height)
        {
            return false;
        }
        int lasts = rounds > 0 ? rounds : kind.Duration;
        var laid = new SurfacePatch { Id = id, At = at, Size = size, RoundsLeft = lasts };
        List<Cell> under = CellsOf(laid);
        foreach (SurfacePatch other in Surfaces.ToList())
        {
            SurfaceDefinition? them = Rules.Surface(other.Id);
            bool meet = CellsOf(other).Any(under.Contains);
            if (meet && (kind.Extinguishes.Contains(other.Id) || (them != null && them.ExtinguishedBy.Contains(id))))
            {
                Surfaces.Remove(other);
                Say($"The {kind.Name.ToLowerInvariant()} puts out the {(them?.Name ?? other.Id).ToLowerInvariant()}.");
            }
        }
        if (Surfaces.Find(s => s.Id == id && s.At == at) is SurfacePatch same)
        {
            same.Size = Math.Max(same.Size, size);
            same.RoundsLeft = Math.Max(same.RoundsLeft, lasts);
            return true;
        }
        Surfaces.Add(laid);
        return true;
    }

    /// <summary>The squares a surface covers: a burst of its size around its cell, as an area of that size would.</summary>
    public List<Cell> CellsOf(SurfacePatch s)
    {
        var burst = new AreaTemplate { Shape = AreaShape.Burst, Origin = Grid.Center(s.At), Size = s.Size * Grid.Size };
        return burst.Cells(Grid).Where(c => Map.Walkable(c)).ToList();
    }

    private bool Covers(SurfacePatch s, Cell cell)
    {
        var burst = new AreaTemplate { Shape = AreaShape.Burst, Origin = Grid.Center(s.At), Size = s.Size * Grid.Size };
        return burst.Contains(Grid.Center(cell), Grid.Size);
    }

    // A round has passed: surfaces with a time run down, those at 0 go. 0 rounds from the start lasts until something ends it.
    private void SurfacesAge()
    {
        foreach (SurfacePatch s in Surfaces)
        {
            if (s.RoundsLeft > 0)
            {
                s.RoundsLeft--;
                if (s.RoundsLeft == 0)
                {
                    s.Size = 0;
                }
            }
        }
        Surfaces.RemoveAll(s => s.Size <= 0);
    }

    // Between fights rounds pass with time.
    private void SurfaceClock(double deltaSeconds)
    {
        if (Fighting || Surfaces.Count == 0)
        {
            _roundClock = 0;
            return;
        }
        _roundClock += deltaSeconds;
        while (_roundClock >= SecondsPerRound)
        {
            _roundClock -= SecondsPerRound;
            SurfacesAge();
        }
    }

    // A creature starting its turn in a surface that burns takes its damage, with a save if it has one.
    private void SurfaceDamage(int creature)
    {
        Cell at = CellOf(creature);
        foreach (SurfacePatch patch in SurfacesAt(at))
        {
            SurfaceDefinition? kind = Rules.Surface(patch.Id);
            if (kind == null || kind.DamagePerRound.Length == 0 || Creatures[creature].Sheet.Down)
            {
                continue;
            }
            string onSave = kind.OnSave.Length == 0 ? "full" : kind.OnSave;
            var steps = new List<EffectStep>
            {
                new()
                {
                    Kind = EffectKind.Damage,
                    Amount = kind.DamagePerRound,
                    Type = kind.DamageType,
                    OnSave = onSave == "half" ? OnSave.Half : onSave == "none" ? OnSave.None : OnSave.Full,
                },
            };
            var effect = new Effect { Steps = steps, Save = kind.SaveAbility.Length > 0 ? new EffectSave(kind.SaveAbility, kind.SaveDc) : new EffectSave() };
            var context = new EffectContext(Rules, Encounter!.Random) { Self = creature, Targets = new List<int> { creature }, Source = "surface:" + kind.Id };
            Say($"{Creatures[creature].Sheet.Name} is in the {kind.Name.ToLowerInvariant()}.");
            EffectResult result = effect.Run(new WorldEffectHost(this), context);
            Narrate(result);
            ConcentrationChecks(result, Encounter.Random);
            AfterEffect(result);
            TidyConcentration();
        }
    }
}
