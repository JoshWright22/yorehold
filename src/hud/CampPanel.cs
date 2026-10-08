using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

public enum CampOrderKind
{
    MakeCamp,
    LeaveCamp,
    Rest,
    ToStash,
    FromStash,
    Revive,
}

/// <summary>Something the camp panel asks for; the play screen does it.</summary>
public sealed record CampOrder(CampOrderKind Kind, int Hero = 0, int Item = -1, string Rest = "", int Target = -1);

/// <summary>
/// Camp and rests (R) as a data panel: the camp itself (make it, break it), the ruleset's rests with
/// what each costs and has left, the stash, the hero's pack to put things in it, and the dead who can
/// be brought back. The heroes are buttons in the head; the entry has the buttons. It only reads.
/// </summary>
public sealed class CampPanel
{
    private static readonly string[] Chips = { "Usable" };
    private static readonly DataColumn[] Columns =
    {
        new("Name", 110), new("Left", 40, true), new("Supplies", 64, true), new("What it does", 300),
    };

    private readonly DataPanel _view;
    private int _hero;

    public CampPanel(DataPanel view)
    {
        _view = view;
        _view.ActionPressed += Act;
        _view.SourcePicked += id => HeroPicked?.Invoke(int.Parse(id));
    }

    public event Action<CampOrder>? Ordered;
    public event Action<int>? HeroPicked;

    public void Refresh(World world, int hero)
    {
        _hero = hero;
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        _view.SetHead(world.AtCamp ? world.Chapter.Title : "Rest and camp", $"supplies {world.SuppliesHeld()}");
        _view.SetSources(Enumerable.Range(0, world.HeroCount).Select(i => (i.ToString(), world.Creatures[i].Sheet.Name)).ToList(), hero.ToString());
        _view.SetTabs(world.AtCamp ? new[] { "All", "Rests", "Stash", "Pack", "Revive" } : new[] { "All", "Rests" });
        _view.SetChips(Chips);
        _view.SetColumns(Columns);

        var rows = new List<DataRow>();
        bool campUsable = world.AtCamp ? world.CanLeaveCamp : world.CanMakeCamp(out _);
        rows.Add(new DataRow
        {
            Key = "camp",
            Cells = world.AtCamp
                ? new[] { "The camp", "", "", "back to the road, as you left it" }
                : new[] { "The camp", "", "",world.CampFolder.Length == 0 ? "this adventure has no camp" : "a safe place to rest and use the stash" },
            Sort = new IComparable?[] { "", -1, -1, "" },
            Tags = new HashSet<string>(campUsable ? new[] { "Usable" } : Array.Empty<string>()),
            Dim = !campUsable,
        });
        foreach (RestDefinition rest in world.Rules.Rests)
        {
            int left = world.RestsLeft(rest);
            bool can = world.CanRest(rest, out _);
            rows.Add(new DataRow
            {
                Key = "rest:" + rest.Id,
                Cells = new[] { Name(rest), left < 0 ? "any" : left.ToString(), rest.SupplyCost.ToString(), $"heals {RecoveryText(rest.Recovery)}" + (rest.CampOnly ? ", at camp only" : "") },
                Sort = new IComparable?[] { Name(rest), left < 0 ? 999 : left, rest.SupplyCost, rest.CampOnly ? 1 : 0 },
                Tags = new HashSet<string>(can ? new[] { "Rests", "Usable" } : new[] { "Rests" }),
                Dim = !can,
            });
        }
        if (world.AtCamp)
        {
            for (int i = 0; i < world.Stash.Items.Count; i++)
            {
                Item item = world.Stash.Items[i];
                rows.Add(ItemRow($"stash:{i}", item, "stash", "Stash", true));
            }
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                Item item = sheet.Inventory[i];
                rows.Add(ItemRow($"pack:{i}", item, item.Equipped ? "worn" : "pack", "Pack", true));
            }
            for (int i = 0; i < world.HeroCount; i++)
            {
                if (!world.Creatures[i].Sheet.Death.Dead)
                {
                    continue;
                }
                bool can = world.CanRevive(hero, i, out _);
                rows.Add(new DataRow
                {
                    Key = $"dead:{i}",
                    Cells = new[] { world.Creatures[i].Sheet.Name, "", "", $"dead; brought back for {Coins.Text(world.Rules.RevivePrice)}" },
                    Sort = new IComparable?[] { world.Creatures[i].Sheet.Name, 0, 0, world.Rules.RevivePrice },
                    Tags = new HashSet<string>(can ? new[] { "Revive", "Usable" } : new[] { "Revive" }),
                    Dim = !can,
                });
            }
        }
        _view.SetRows(rows);
        ShowEntry(world, hero);
        _view.SetFoot($"Supplies {world.SuppliesHeld()} (stash {world.Stash.Supplies})     {sheet.Name}: {Coins.Text(sheet.Coins)}"
            + (world.AtCamp ? "     The stash and revival are only here." : "     Long rests, the stash and revival are at camp."));
    }

    /// <summary>Says what the world refused, under the entry.</summary>
    public void Refused(string why)
    {
        _view.ShowWarning(why);
    }

    private static DataRow ItemRow(string key, Item item, string kind, string tab, bool usable)
    {
        return new DataRow
        {
            Key = key,
            Cells = new[] { item.Quantity > 1 ? $"{item.Name} x{item.Quantity}" : item.Name, "", item.Supplies > 0 ? (item.Supplies * item.Quantity).ToString() : "", kind == "stash" ? "in the stash" : kind == "worn" ? "worn" : "in the pack" },
            Sort = new IComparable?[] { item.Name, 0, item.Supplies * item.Quantity, kind },
            Tags = new HashSet<string>(usable ? new[] { tab, "Usable" } : new[] { tab }),
            Search = item.Definition.Description,
        };
    }

    private void ShowEntry(World world, int hero)
    {
        string picked = _view.Picked;
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var actions = new List<DataAction>();
        var page = new BookPage();
        string? whole = null;
        if (picked == "camp")
        {
            if (world.AtCamp)
            {
                page.Title("Break camp").Sub("back to where the party left the road").Rule()
                    .Text("Everyone goes back to the chapter as they left it, as they are now.");
                actions.Add(new DataAction("leave", "Break camp", world.CanLeaveCamp, "Not now."));
            }
            else
            {
                bool can = world.CanMakeCamp(out string why);
                page.Title("Make camp").Sub("a safe place off the map").Rule()
                    .Text("The party goes to camp and comes back to this same spot. The long rest, the stash and revival are only there.");
                actions.Add(new DataAction("make", "Make camp", can, why));
            }
        }
        else if (picked.StartsWith("rest:", StringComparison.Ordinal) && world.Rules.Rest(picked[5..]) is RestDefinition rest)
        {
            bool can = world.CanRest(rest, out string why);
            int left = world.RestsLeft(rest);
            page.Title(Name(rest)).Sub(rest.CampOnly ? "at camp only" : "anywhere between fights").Rule();
            page.Stats(("Left", left < 0 ? "no limit" : left.ToString()), ("Supplies", rest.SupplyCost.ToString()));
            page.Stat("Heals", RecoveryText(rest.Recovery));
            if (rest.Restores.Count > 0)
            {
                page.Stat("Brings back", string.Join(", ", rest.Restores.Select(r => r.TrimEnd('*').TrimEnd('-').Replace("slots", "spell slots"))));
            }
            if (rest.Resets.Count > 0)
            {
                page.Stat("Gives back", string.Join(", ", rest.Resets.Select(id => world.Rules.Rest(id) is RestDefinition r ? Name(r) : id)));
            }
            actions.Add(new DataAction("rest", "Rest", can, why));
        }
        else if (picked.StartsWith("stash:", StringComparison.Ordinal) && int.TryParse(picked[6..], out int s) && s < world.Stash.Items.Count)
        {
            // the same page the gear panel gives it, so its numbers and use are there too
            whole = GearPanel.Page(world, world.Stash.Items[s], world.Stash.Items[s].Value);
            actions.Add(new DataAction($"take:{s}", $"{sheet.Name} takes it", !sheet.Down, $"{sheet.Name} can't."));
        }
        else if (picked.StartsWith("pack:", StringComparison.Ordinal) && int.TryParse(picked[5..], out int p) && p < sheet.Inventory.Count)
        {
            whole = GearPanel.Page(world, sheet.Inventory[p], sheet.Inventory[p].Value);
            actions.Add(new DataAction($"store:{p}", "Put in the stash", !sheet.Death.Dead, $"{sheet.Name} can't."));
        }
        else if (picked.StartsWith("dead:", StringComparison.Ordinal) && int.TryParse(picked[5..], out int dead) && dead < world.HeroCount)
        {
            bool can = world.CanRevive(hero, dead, out string why);
            page.Title(world.Creatures[dead].Sheet.Name).Sub("dead").Rule()
                .Text($"Brought back for {Coins.Text(world.Rules.RevivePrice)}, with {Math.Max(1, world.Rules.ReviveHp)} HP.");
            actions.Add(new DataAction($"revive:{dead}", $"{sheet.Name} pays", can, why));
        }
        else
        {
            page.Note("Pick a rest, the camp or something in the stash.");
        }
        _view.SetEntry(whole ?? page.ToString(), actions, "");
    }

    private void Act(string id)
    {
        string[] parts = id.Split(':');
        int number = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : -1;
        string picked = _view.Picked;
        CampOrder? order = parts[0] switch
        {
            "make" => new CampOrder(CampOrderKind.MakeCamp, _hero),
            "leave" => new CampOrder(CampOrderKind.LeaveCamp, _hero),
            "rest" => new CampOrder(CampOrderKind.Rest, _hero, Rest: picked.StartsWith("rest:", StringComparison.Ordinal) ? picked[5..] : ""),
            "take" => new CampOrder(CampOrderKind.FromStash, _hero, number),
            "store" => new CampOrder(CampOrderKind.ToStash, _hero, number),
            "revive" => new CampOrder(CampOrderKind.Revive, _hero, Target: number),
            _ => null,
        };
        if (order != null)
        {
            Ordered?.Invoke(order);
        }
    }

    private static string Name(RestDefinition rest) => rest.Name.Length == 0 ? rest.Id : rest.Name;

    private static string RecoveryText(Recovery recovery) => recovery.Kind switch
    {
        RecoveryKind.Full => "all HP" + (recovery.ReviveDowned ? ", the downed get up" : ""),
        RecoveryKind.Fraction => $"{Math.Round(recovery.Fraction * 100)}% of HP",
        RecoveryKind.Flat => $"{recovery.Amount} HP",
        RecoveryKind.HitDice => "hit dice",
        _ => "nothing",
    };
}
