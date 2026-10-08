using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The load screen: every save file and backup as a row, the picked one's party and place as its
/// page, Load and Delete under it. Delete has to be pressed twice.
/// </summary>
public sealed class LoadPanel
{
    private static readonly DataColumn[] Columns =
    {
        new("Save", 150), new("Chapter", 150), new("Party", 46, true), new("Saved", 120),
    };

    public DataPanel View { get; }
    public event Action<string>? LoadPressed;
    /// <summary>A file was removed; the list has to be read again.</summary>
    public event Action? Changed;

    private List<SaveSummary> _saves = new();
    private string _refusal = "";
    private string _deleteAsked = "";

    public LoadPanel(DataPanel view)
    {
        View = view;
        view.ActionPressed += Act;
    }

    public void Refresh(List<SaveSummary> saves, string refusal)
    {
        _saves = saves;
        _refusal = refusal;
        int unreadable = saves.Count(s => s.Problem.Length > 0);
        View.SetHead("Load", $"{saves.Count} {(saves.Count == 1 ? "save" : "saves")}" + (unreadable > 0 ? $", {unreadable} that can't be read" : ""));
        View.SetSources(Array.Empty<(string, string)>(), "");
        View.SetTabs(saves.Any(s => s.Backup) ? new[] { "All", "Saves", "Backups" } : Array.Empty<string>());
        // the chips only help once something is broken
        View.SetChips(unreadable > 0 ? new[] { "Loadable", "Broken" } : Array.Empty<string>());
        // the newest save first: the one a player most likely wants
        View.SetColumns(Columns, 3, true);

        var rows = new List<DataRow>();
        foreach (SaveSummary save in saves)
        {
            bool broken = save.Problem.Length > 0;
            rows.Add(new DataRow
            {
                Key = save.Path,
                Cells = new[] { Name(save), broken ? "can't be read" : save.ChapterTitle, broken ? "" : save.Heroes.Count.ToString(), save.Written.ToString("d MMM yyyy HH:mm") },
                Sort = new IComparable?[] { Name(save), null, save.Heroes.Count, save.Written },
                Tags = new HashSet<string> { save.Backup ? "Backups" : "Saves", broken ? "Broken" : "Loadable" },
                Search = string.Join(" ", save.Heroes.Select(h => h.Name + " " + h.ClassName)) + " " + save.FileName,
                Dim = broken,
            });
        }
        View.SetRows(rows);

        SaveSummary? picked = saves.Find(s => s.Path == View.Picked);
        if (picked == null)
        {
            View.SetEntry(new BookPage().Note("No saves yet. The game saves itself as the adventure goes.").ToString(), Array.Empty<DataAction>(), "");
            View.SetFoot("");
            return;
        }
        if (_deleteAsked != picked.Path)
        {
            _deleteAsked = "";
        }
        var page = new BookPage().Title(Name(picked));
        Write(page, picked);
        bool canLoad = picked.Problem.Length == 0 && refusal.Length == 0;
        var actions = new List<DataAction>
        {
            new("load", "Load", canLoad, picked.Problem.Length > 0 ? "This file can't be read: " + picked.Problem : refusal),
            new("delete", _deleteAsked.Length > 0 ? "Delete for good" : "Delete"),
        };
        View.SetEntry(page.ToString(), actions, _deleteAsked.Length > 0 ? $"Press again to remove {picked.FileName}. It can't be brought back." : "");
        View.SetFoot("Double-click a save to load it.");
    }

    /// <summary>A save's page under its title: where, when, who. Shared with the title's Continue.</summary>
    public static void Write(BookPage page, SaveSummary save)
    {
        if (save.Problem.Length > 0)
        {
            page.Sub(save.FileName).Rule().Warn("This file can't be read: " + save.Problem);
            page.Stat("Written", save.Written.ToString("d MMM yyyy HH:mm"));
            return;
        }
        page.Sub(save.CampReturn.Length > 0 ? $"{save.ChapterTitle}, at camp" : save.ChapterTitle).Rule();
        page.Stat("Saved", save.Written.ToString("d MMM yyyy HH:mm"));
        page.Stats(("Fights",save.Fights.ToString()));
        page.Gap().Heading("Party");
        var cells = new List<string>();
        foreach (SaveHero hero in save.Heroes)
        {
            cells.Add(hero.Name);
            cells.Add(hero.ClassName);
            cells.Add(hero.Level.ToString());
            cells.Add(hero.Dead ? "dead" : Math.Max(0, hero.Hp).ToString());
        }
        page.Table(new[] { "Name", "Class", "Level", "HP" }, cells);
    }

    private static string Name(SaveSummary save)
    {
        string stem = save.FileName.Replace(".json.bak", "").Replace(".json", "");
        string name = stem == "adventure" ? "Autosave" : stem;
        return save.Backup ? name + ", the one before" : name;
    }

    /// <summary>Enter on the load screen: loads the picked save.</summary>
    public void LoadPicked() => Act("load");

    private void Act(string id)
    {
        SaveSummary? picked = _saves.Find(s => s.Path == View.Picked);
        if (picked == null)
        {
            return;
        }
        if (id == "load")
        {
            // Enter gets here without the button, so the greyed case is said here too
            if (picked.Problem.Length > 0 || _refusal.Length > 0)
            {
                View.ShowWarning(picked.Problem.Length > 0 ? "This file can't be read: " + picked.Problem : _refusal);
                return;
            }
            LoadPressed?.Invoke(picked.Path);
            return;
        }
        if (_deleteAsked != picked.Path)
        {
            _deleteAsked = picked.Path;
            return;
        }
        _deleteAsked = "";
        try
        {
            System.IO.File.Delete(picked.Path);
            App.FilesWritten();
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            View.ShowWarning("Couldn't remove it: " + error.Message);
        }
        Changed?.Invoke();
    }
}
