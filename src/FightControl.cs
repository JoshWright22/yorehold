using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The player's side of a fight: which action is picked, what the pointer is over, and turning
/// hotbar presses, keys and taps on the map into World calls. A tap with nothing picked moves, or
/// strikes an enemy (walking up first). With an action picked a tap on a creature uses it there,
/// one aimed at a square goes where the tap is, and a right click or Escape puts it away. The number keys pick hotbar slots, Space ends the turn.
/// On a touch screen the first tap on a square shows what it would do and a second one does it,
/// since a finger has no hover. Every rule is the World's; a refusal floats up where the tap was.
/// </summary>
public partial class FightControl : Node
{
    public FightAim Aim { get; } = new();
    /// <summary>The creature the camera should follow; null for the selected hero.</summary>
    public int? Watch { get; private set; }
    /// <summary>A screen is over the map: keys and clicks are not for the fight.</summary>
    public bool Paused { get; set; }

    private World? _world;
    private PlayCamera? _camera;
    private PlayHud? _hud;
    private FloatersView? _floaters;
    private Vector2 _pointer;
    private bool _pointerKnown;
    private bool _touch;
    private Cell? _touched;

    public void Bind(World world, PlayCamera camera, PlayHud hud, FloatersView floaters)
    {
        _world = world;
        _camera = camera;
        _hud = hud;
        _floaters = floaters;
        hud.ActionPressed += PickAction;
        hud.EndTurnPressed += EndTurn;
        hud.CreaturePressed += PickCreature;
        hud.ReactionAnswered += take => _world?.React(take);
    }

    /// <summary>A turn began: the camera goes to whoever has it, if the party can see them.</summary>
    public void TurnBegan()
    {
        Aim.Action = "";
        _touched = null;
        if (_world?.CurrentCreature is int now && _camera != null && Seen(_world, now))
        {
            Watch = now;
            _camera.Following = true;
        }
    }

    public void FightEnded()
    {
        Aim.Action = "";
        Aim.ClearHover();
        Aim.HeroTurn = false;
        _touched = null;
        Watch = null;
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventScreenTouch or InputEventScreenDrag:
                _touch = true;
                break;
            case InputEventMouseMotion motion:
                _pointer = motion.Position;
                _pointerKnown = true;
                if (motion.Device != InputEvent.DeviceIdEmulation)
                {
                    _touch = false;
                }
                break;
            case InputEventMouseButton button:
                _pointer = button.Position;
                _pointerKnown = true;
                break;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_world == null || !_world.Fighting || Paused)
        {
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            Cancel();
            return;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (App.Pressed(@event, "end_turn"))
        {
            EndTurn();
            return;
        }
        switch (key.Keycode)
        {
            case Key.Escape:
                // with nothing picked Escape is left for the play screen, which opens the pause list
                if (Aim.Action.Length > 0)
                {
                    Cancel();
                    GetViewport().SetInputAsHandled();
                }
                break;
            case >= Key.Key0 and <= Key.Key9:
            {
                // 1 to 9 are the first nine slots and 0 the tenth, as the keys sit on the keyboard
                int slot = key.Keycode == Key.Key0 ? 9 : (int)(key.Keycode - Key.Key1);
                if (_hud?.SlotAction(slot) is string id)
                {
                    PickAction(id);
                }
                break;
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_world == null || _camera == null)
        {
            return;
        }
        World w = _world;
        Aim.ClearHover();
        Aim.HeroTurn = w.Fighting && !w.PartyWiped && !w.InCutscene && w.ReactionPrompt == null && !w.Options.AutoPlay
            && w.CurrentCreature is int now && w.Creatures[now].Team == 0;
        if (!Aim.HeroTurn || w.CurrentCreature is not int me)
        {
            Aim.Action = "";
            return;
        }
        ActionDefinition? action = Aim.Action.Length > 0 ? w.FindAction(Aim.Action) : null;
        if (action != null && !w.CanUse(me, action, out _))
        {
            // the actions ran out, or something took it away
            Aim.Action = "";
            action = null;
        }

        Cell? hover = _touch ? _touched : PointerCell(w);
        if (hover is not Cell cell)
        {
            return;
        }
        Aim.Hover = cell;
        Aim.LabelAt = _touch ? _camera.WorldToScreen(w.Grid.Center(cell).ToGodot()) : _pointer;
        int? target = CreatureAt(w, cell);

        if (action != null && action.Target == ActionTarget.Point)
        {
            // the area follows the pointer, with a ring on everyone it would land on
            if (w.ValidAim(me, action, cell, out string why))
            {
                Aim.Hit.AddRange(w.CreaturesIn(me, action, cell));
                float feet = w.Grid.Distance(w.CellOf(me), cell) * w.Rules.FeetPerSquare;
                Aim.Label = $"{action.Name}: {(int)MathF.Round(feet)} ft, {Aim.Hit.Count} in it";
            }
            else
            {
                Aim.AimBad = true;
                Aim.Label = why.Length > 0 ? why.TrimEnd('.') : "Can't go there";
                Aim.LabelBad = true;
            }
            return;
        }
        if (action != null)
        {
            if (action.Target != ActionTarget.Creature || target is not int aimed || w.OrderIndex(aimed) == null)
            {
                return;
            }
            if (w.ValidTarget(me, action, aimed))
            {
                Aim.Target = aimed;
                Aim.Label = HudText.RollsAttack(action) ? w.AttackOddsLine(me, aimed, action.Id) : action.Name;
            }
            else
            {
                Aim.Label = "Out of range";
                Aim.LabelBad = true;
            }
            return;
        }

        if (target is int foe && w.Creatures[foe].Team == 1 && w.OrderIndex(foe) != null)
        {
            // a plain click on an enemy strikes it
            if (w.CanUse(me, w.StrikeAction))
            {
                Aim.Target = foe;
                Aim.Label = w.AttackOddsLine(me, foe);
            }
            return;
        }
        Token token = w.Tokens.Tokens[me];
        Cell standing = w.CellOf(me);
        if (target != null || token.Path.Count > 0 || cell == standing)
        {
            return;
        }
        var reach = w.ReachableCells();
        if (!reach.TryGetValue(cell, out float cost))
        {
            return;
        }
        Aim.Path.AddRange(Paths.Find(w.Grid, standing, cell, c => c == standing || reach.ContainsKey(c)));
        Aim.PathCost = MathF.Ceiling(cost - 0.01f);
        Aim.Label = $"{(int)Aim.PathCost * w.Rules.FeetPerSquare} ft";
    }

    /// <summary>A tap on the map while a fight is on.</summary>
    public void Tap(Vector2 screen)
    {
        if (_world == null || _camera == null || !Aim.HeroTurn || _world.CurrentCreature is not int me)
        {
            return;
        }
        World w = _world;
        Vector2 at = _camera.ScreenToWorld(screen);
        Cell cell = w.Grid.CellAt(at.ToRules());
        if (_touch && _touched != cell)
        {
            _touched = cell; // shows the path or the chance first; the next tap here goes through
            return;
        }
        _touched = null;
        int? target = CreatureAt(w, cell);

        if (Aim.Action.Length > 0 && w.FindAction(Aim.Action) is ActionDefinition action)
        {
            // an action on oneself is aimed at oneself: a click anywhere else puts it away
            if (action.Target == ActionTarget.Self)
            {
                if (target == me && w.Use(action.Id))
                {
                    Aim.Action = "";
                }
                else if (target == me)
                {
                    Refuse(at);
                }
                else
                {
                    Aim.Action = "";
                }
                return;
            }
            // an enemy out of range is walked up to; everything else is used from here or refused
            bool done = action.Target == ActionTarget.Point ? w.Use(action.Id, null, cell)
                : target is int aimed
                ? action.Side == ActionSide.Ally || action.Target != ActionTarget.Creature ? w.Use(action.Id, aimed) : w.Attack(aimed, action.Id)
                : w.Use(action.Id);
            if (done)
            {
                Aim.Action = "";
            }
            else
            {
                Refuse(at);
            }
            return;
        }

        if (target is int who)
        {
            if (w.Creatures[who].Team == 0)
            {
                PickCreature(who);
                return;
            }
            if (w.Creatures[who].Team == 1 && w.OrderIndex(who) != null)
            {
                if (!w.Attack(who))
                {
                    Refuse(at);
                }
                return;
            }
        }
        // a click never moves the camera; a new turn or Home does
        if (!w.MoveTo(cell))
        {
            Refuse(at);
        }
    }

    /// <summary>
    /// A hotbar slot or its key. Every action is picked first and then aimed, as in BG3 (Josh,
    /// 10/7): one on oneself (Defend, Stride) is used by a click on the hero or by its key again;
    /// any other is put away by picking it again.
    /// </summary>
    public void PickAction(string id)
    {
        if (_world == null || !Aim.HeroTurn || _world.CurrentCreature is not int me || _world.FindAction(id) is not ActionDefinition action)
        {
            return;
        }
        _touched = null;
        if (!_world.CanUse(me, action, out _))
        {
            return; // the slot is greyed and its tooltip says why
        }
        if (action.Target == ActionTarget.Self && Aim.Action == id)
        {
            Aim.Action = "";
            if (!_world.Use(id))
            {
                Refuse(_world.Tokens.Tokens[me].Position.ToGodot());
            }
            return;
        }
        Aim.Action = Aim.Action == id ? "" : id;
    }

    public void EndTurn()
    {
        if (_world == null || !Aim.HeroTurn)
        {
            return;
        }
        Aim.Action = "";
        _touched = null;
        _world.EndTurn();
    }

    public void Cancel()
    {
        Aim.Action = "";
        _touched = null;
    }

    /// <summary>
    /// A card or a hero's token. In a fight it hands the shared turn to that hero when the rules
    /// allow, and otherwise only looks at whoever it is. Between fights it selects the hero.
    /// </summary>
    public void PickCreature(int creature)
    {
        if (_world == null || _camera == null || creature < 0 || creature >= _world.Creatures.Count)
        {
            return;
        }
        World w = _world;
        if (w.Fighting)
        {
            if (creature != w.CurrentCreature && w.CanChooseTurn(creature) && w.ChooseTurn(creature))
            {
                Aim.Action = "";
            }
            if (Seen(w, creature))
            {
                Watch = creature; // Home goes to them; the click itself leaves the camera
            }
            return;
        }
        if (creature >= w.HeroCount || w.Creatures[creature].Sheet.Down)
        {
            return;
        }
        for (int i = 0; i < w.HeroCount; i++)
        {
            w.Tokens.Tokens[i].Selected = i == creature;
        }
    }

    private void Refuse(Vector2 at)
    {
        if (_world != null && _floaters != null && _world.Refusal.Length > 0)
        {
            _floaters.Add(at, _world.Refusal, Palette.Ash);
        }
    }

    // The map cell under the mouse; null over a panel, off the map or before the mouse has moved.
    private Cell? PointerCell(World w)
    {
        if (!_pointerKnown || _camera == null || GetViewport().GuiGetHoveredControl() != null)
        {
            return null;
        }
        Cell cell = w.Grid.CellAt(_camera.ScreenToWorld(_pointer).ToRules());
        return cell.X >= 0 && cell.Y >= 0 && cell.X < w.Map.Width && cell.Y < w.Map.Height ? cell : null;
    }

    // Who stands on a cell that the party can see: anyone on their feet, or a hero who is down.
    private static int? CreatureAt(World w, Cell cell)
    {
        int? fallen = null;
        for (int i = 0; i < w.Creatures.Count; i++)
        {
            if (w.CellOf(i) != cell)
            {
                continue;
            }
            Token token = w.Tokens.Tokens[i];
            if (token.Floor == 0)
            {
                return i;
            }
            if (token.Floor == World.DeadFloor && w.Creatures[i].Team == 0 && !w.Creatures[i].Fled)
            {
                fallen = i;
            }
        }
        return fallen;
    }

    private static bool Seen(World w, int creature)
    {
        return w.Creatures[creature].Team == 0 || w.Tokens.Tokens[creature].Floor == 0;
    }
}
