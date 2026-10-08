using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace Yorehold;

/// <summary>One column of a data panel's list. Number columns sort by value and sit on the right.</summary>
public sealed record DataColumn(string Title, float Width, bool Number = false);

/// <summary>One row of a data panel's list.</summary>
public sealed class DataRow
{
    /// <summary>Stays the same while the thing it stands for is there, so a pick survives a refresh.</summary>
    public string Key { get; init; } = "";
    public string[] Cells { get; init; } = Array.Empty<string>();
    /// <summary>Per column, what it sorts by; null sorts by the cell's text.</summary>
    public IComparable?[] Sort { get; init; } = Array.Empty<IComparable?>();
    /// <summary>The tabs and chips it shows under. The first tab shows everything.</summary>
    public HashSet<string> Tags { get; init; } = new();
    /// <summary>More words the search box finds it by, beside its cells.</summary>
    public string Search { get; init; } = "";
    /// <summary>Greyed: there but not usable now.</summary>
    public bool Dim { get; init; }

    // Only a panel in grid mode (the spell book) reads these.
    /// <summary>The heading it sits under in the grid.</summary>
    public string Section { get; init; } = "";
    /// <summary>The creator's icon when there is one; else the tile shows its first letter.</summary>
    public Texture2D? Picture { get; init; }
    /// <summary>A short mark in the tile's corner.</summary>
    public string Badge { get; init; } = "";
    /// <summary>What dragging the tile carries (onto the hotbar); "" for none.</summary>
    public string Drag { get; init; } = "";
}

/// <summary>A button under a data panel's entry. An action that can't be taken is greyed and says why when pressed.</summary>
public sealed record DataAction(string Id, string Label, bool Enabled = true, string Why = "");

/// <summary>
/// The dense look the data screens share (gear, spells, the sheet): a tab bar of types, a search
/// box and filter chips over a tight list sorted by any column, and the picked row's full entry on
/// the right as a book page with its buttons under it. The owner fills it each frame; it only
/// rebuilds what changed, so a click or the text being typed isn't lost.
/// </summary>
public partial class DataPanel : PanelContainer
{
    [Export] public float RowHeight { get; set; } = 22;

    public event Action<string>? ActionPressed;
    public event Action<string>? SourcePicked;
    public event Action? ClosePressed;

    public string Tab { get; private set; } = "";
    /// <summary>The picked row's key, "" when the list is empty.</summary>
    public string Picked { get; private set; } = "";
    public string SearchText => _search.Text;

    /// <summary>
    /// Shows the list as icon tiles under section headings, like a spell book, instead of a table.
    /// Rows are still filtered by tab, chip and search, and the picked one still fills the entry.
    /// </summary>
    public bool Grid
    {
        get => _grid;
        set
        {
            if (_grid == value)
            {
                return;
            }
            _grid = value;
            _columns.Visible = !value;
            Clear(_items);
            _rowButtons.Clear();
            _tiles.Clear();
            _tilesShown = "";
            _dirty = true;
        }
    }

    // the scene has all of these
    private Label _title = null!;
    private Label _sub = null!;
    private HBoxContainer _sources = null!;
    private HBoxContainer _tabs = null!;
    private LineEdit _search = null!;
    private HBoxContainer _chips = null!;
    private HBoxContainer _columns = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _items = null!;
    private Label _count = null!;
    private RichTextLabel _page = null!;
    private HFlowContainer _actions = null!;
    private Label _warning = null!;
    private Label _foot = null!;

    private readonly List<DataColumn> _columnList = new();
    private readonly List<DataRow> _rows = new();
    private readonly HashSet<string> _chipsOn = new();
    private readonly List<Button> _rowButtons = new();
    private string _rowsShown = "";
    private string _tabsShown = "";
    private string _chipsShown = "";
    private string _sourcesShown = "";
    private string _entryShown = "";
    private readonly Dictionary<string, IconTile> _tiles = new();
    private string _tilesShown = "";
    private bool _grid;
    private int _sortColumn;
    private bool _sortDown;
    private bool _dirty = true;

    public override void _Ready()
    {
        _title = GetNode<Label>("Rows/Head/Title");
        _sub = GetNode<Label>("Rows/Head/Sub");
        _sources = GetNode<HBoxContainer>("Rows/Head/Sources");
        _tabs = GetNode<HBoxContainer>("Rows/Tabs");
        _search = GetNode<LineEdit>("Rows/Filter/Search");
        _chips = GetNode<HBoxContainer>("Rows/Filter/Chips");
        _columns = GetNode<HBoxContainer>("Rows/Body/List/Columns");
        _scroll = GetNode<ScrollContainer>("Rows/Body/List/Scroll");
        _items = GetNode<VBoxContainer>("Rows/Body/List/Scroll/Items");
        _count = GetNode<Label>("Rows/Body/List/Count");
        _page = GetNode<RichTextLabel>("Rows/Body/Entry/Page/Text");
        _actions = GetNode<HFlowContainer>("Rows/Body/Entry/Actions");
        _warning = GetNode<Label>("Rows/Body/Entry/Warning");
        _foot = GetNode<Label>("Rows/Foot");
        _other = GetNode<VBoxContainer>("Rows/Body/Other");
        _otherItems = GetNode<VBoxContainer>("Rows/Body/Other/Scroll/Items");
        // yours on the left, theirs beside the page, as a chest or a shop reads in most games
        GetNode<Control>("Rows/Body").MoveChild(_other, 0);
        _search.TextChanged += _ => _dirty = true;
        GetNode<Button>("Rows/Head/Close").Pressed += () => ClosePressed?.Invoke();
    }

    public void SetHead(string title, string sub)
    {
        _title.Text = title;
        _sub.Text = sub;
    }

    public void SetFoot(string text)
    {
        _foot.Text = text;
        _foot.Visible = text.Length > 0;
    }

    /// <summary>What the list is of (a hero's pack, a chest, a shop), as buttons in the head.</summary>
    public void SetSources(IReadOnlyList<(string Id, string Label)> sources, string picked)
    {
        string signature = string.Join("|", sources.Select(s => s.Id + "=" + s.Label)) + "#" + picked;
        if (signature == _sourcesShown)
        {
            return;
        }
        _sourcesShown = signature;
        Clear(_sources);
        foreach ((string id, string label) in sources)
        {
            var button = new Button { Text = label, ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(id == picked);
            button.Pressed += () => SourcePicked?.Invoke(id);
            _sources.AddChild(button);
        }
        _sources.Visible = sources.Count > 1;
    }

    /// <summary>The tab bar. The current tab stays picked while it is still there; otherwise the first.</summary>
    public void SetTabs(IReadOnlyList<string> tabs)
    {
        if (!tabs.Contains(Tab))
        {
            Tab = tabs.Count > 0 ? tabs[0] : "";
            _dirty = true;
        }
        string signature = string.Join("|", tabs) + "#" + Tab;
        if (signature == _tabsShown)
        {
            return;
        }
        _tabsShown = signature;
        Clear(_tabs);
        foreach (string tab in tabs)
        {
            var button = new Button { Text = tab, ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(tab == Tab);
            button.Pressed += () =>
            {
                Tab = tab;
                _tabsShown = "";
                _dirty = true;
                SetTabs(tabs);
            };
            _tabs.AddChild(button);
        }
    }

    /// <summary>Filter chips: a row shows when it has every chip that is on.</summary>
    public void SetChips(IReadOnlyList<string> chips)
    {
        _chipsOn.IntersectWith(chips);
        string signature = string.Join("|", chips);
        if (signature == _chipsShown)
        {
            return;
        }
        _chipsShown = signature;
        Clear(_chips);
        foreach (string chip in chips)
        {
            var button = new Button { Text = chip, ToggleMode = true, ThemeTypeVariation = "ChipButton", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(_chipsOn.Contains(chip));
            button.Toggled += on =>
            {
                if (on)
                {
                    _chipsOn.Add(chip);
                }
                else
                {
                    _chipsOn.Remove(chip);
                }
                _dirty = true;
            };
            _chips.AddChild(button);
        }
        _dirty = true;
    }

    /// <summary>The columns, sorted at first by sortColumn (newest first for a date, with down).</summary>
    public void SetColumns(IReadOnlyList<DataColumn> columns, int sortColumn = 0, bool sortDown = false)
    {
        if (_columnList.SequenceEqual(columns))
        {
            return;
        }
        _columnList.Clear();
        _columnList.AddRange(columns);
        _sortColumn = sortColumn;
        _sortDown = sortDown;
        _rowsShown = "";
        _dirty = true;
        ShowColumns();
    }

    public void SetRows(IReadOnlyList<DataRow> rows)
    {
        var signature = new StringBuilder();
        foreach (DataRow row in rows)
        {
            signature.Append(row.Key).Append('\u001f').AppendJoin('\u001f', row.Cells).Append(row.Dim ? '-' : '+').AppendJoin(',', row.Tags)
                .Append('\u001f').Append(row.Section).Append(row.Badge).Append(row.Drag).Append(row.Picture?.GetInstanceId() ?? 0).Append('\u001e');
        }
        if (signature.ToString() == _rowsShown && !_dirty)
        {
            return;
        }
        _rowsShown = signature.ToString();
        _rows.Clear();
        _rows.AddRange(rows);
        // a search box and filters over a short list are only more to read; they come with a longer one
        GetNode<Control>("Rows/Filter").Visible = _rows.Count >= FilterFrom || _search.Text.Length > 0 || _chipsOn.Count > 0;
        ShowRows();
    }

    /// <summary>
    /// A second list left of the main one, for two sides of a trade: the hero's pack beside a
    /// chest or a shop. Its rows' keys must differ from the main list's; picking one fills the
    /// entry like a main row. heading names each list; null rows take the second list away.
    /// </summary>
    public void SetOther(string heading, string mainHeading, IReadOnlyList<DataRow>? rows)
    {
        bool shown = rows != null;
        var head = GetNode<Label>("Rows/Body/List/Heading");
        head.Visible = shown;
        head.Text = mainHeading.ToUpperInvariant();
        _other.Visible = shown;
        GetNode<Label>("Rows/Body/Other/Heading").Text = heading.ToUpperInvariant();
        var signature = new StringBuilder();
        foreach (DataRow row in rows ?? Array.Empty<DataRow>())
        {
            signature.Append(row.Key).Append('\u001f').AppendJoin('\u001f', row.Cells).Append('\u001e');
        }
        if (signature.ToString() != _otherShown)
        {
            _otherShown = signature.ToString();
            _otherRows.Clear();
            _otherRows.AddRange(rows ?? Array.Empty<DataRow>());
            _dirty = true;
        }
    }

    private VBoxContainer _other = null!;
    private VBoxContainer _otherItems = null!;
    private readonly List<DataRow> _otherRows = new();
    private readonly List<Button> _otherButtons = new();
    private string _otherShown = "";

    /// <summary>The fewest rows that get the search box and filter chips.</summary>
    public const int FilterFrom = 8;

    /// <summary>The picked row's page (bbcode), its buttons and a warning line. Only redrawn when one of them changes.</summary>
    public void SetEntry(string page, IReadOnlyList<DataAction> actions, string warning)
    {
        string signature = page + "\u001e" + string.Join("\u001f", actions.Select(a => $"{a.Id}={a.Label}={a.Enabled}={a.Why}")) + "\u001e" + warning;
        if (signature == _entryShown)
        {
            return;
        }
        _entryShown = signature;
        _page.Text = page;
        Clear(_actions);
        // the first action is what the panel is for (Load, Start, Take all): it stands out, and a
        // double click on the picked row or Enter does it too
        _mainAction = actions.FirstOrDefault(a => a.Enabled && !IsClose(a));
        foreach (DataAction action in actions)
        {
            if (action.Label == "Close (Esc)" && GetNode<Control>("Rows/Head/Close").Visible)
            {
                continue; // the head already has Close; a second one only makes the row longer
            }
            var button = new TipButton { Text = action.Label, FocusMode = FocusModeEnum.None };
            if (!action.Enabled)
            {
                button.ThemeTypeVariation = "GreyButton";
                button.TooltipText = action.Why;
            }
            else if (action == _mainAction)
            {
                button.ThemeTypeVariation = "MainButton";
            }
            button.Clicked += _ =>
            {
                if (action.Enabled)
                {
                    ActionPressed?.Invoke(action.Id);
                }
                else
                {
                    ShowWarning(action.Why);
                }
            };
            _actions.AddChild(button);
        }
        // a greyed first button says why without being clicked or hovered: "Buy" with no coins
        if (warning.Length == 0 && actions.FirstOrDefault(a => !IsClose(a)) is { Enabled: false, Why.Length: > 0 } greyed)
        {
            warning = greyed.Why;
        }
        ShowWarning(warning);
    }

    private DataAction? _mainAction;

    // an action that only shuts the panel, however its owner spells it ("Close", "Close (Esc)")
    private static bool IsClose(DataAction action) => action.Label.StartsWith("Close", StringComparison.Ordinal) || action.Label == "Cancel";

    /// <summary>Does the panel's main action, as its button would: a double click on a row, or Enter.</summary>
    public void DoMainAction()
    {
        if (_mainAction != null && Visible)
        {
            ActionPressed?.Invoke(_mainAction.Id);
        }
    }

    /// <summary>
    /// Puts a control of the owner's (a form to edit the entry) where the book page is, and takes
    /// the Close button away, for a panel that is a whole screen's body rather than a popup.
    /// </summary>
    public void UseForm(Control form, float width)
    {
        var entry = GetNode<VBoxContainer>("Rows/Body/Entry");
        GetNode<Control>("Rows/Body/Entry/Page").Visible = false;
        GetNode<Control>("Rows/Head/Close").Visible = false;
        entry.CustomMinimumSize = new Vector2(width, 0);
        form.SizeFlagsVertical = SizeFlags.ExpandFill;
        entry.AddChild(form);
        entry.MoveChild(form, 0);
    }

    /// <summary>Says something under the entry until the entry changes, like why an action is greyed.</summary>
    public void ShowWarning(string text)
    {
        _warning.Text = text;
        _warning.Visible = text.Length > 0;
    }

    /// <summary>Picks a row by key, as if it was clicked.</summary>
    public void Pick(string key)
    {
        if (Picked != key)
        {
            Picked = key;
            _dirty = true;
        }
    }

    /// <summary>Puts the search box, the chips and the tab back to how a panel opens.</summary>
    public void Reset()
    {
        _search.Text = "";
        _chipsOn.Clear();
        _chipsShown = "";
        _tabsShown = "";
        Tab = "";
        _dirty = true;
    }

    public override void _Process(double delta)
    {
        if (_dirty && IsVisibleInTree())
        {
            ShowRows();
        }
    }

    private void ShowColumns()
    {
        Clear(_columns);
        for (int i = 0; i < _columnList.Count; i++)
        {
            int column = i;
            DataColumn c = _columnList[i];
            string arrow = column == _sortColumn ? (_sortDown ? " ▾" : " ▴") : "";
            var button = new Button
            {
                Text = c.Title.ToUpperInvariant() + arrow,
                ThemeTypeVariation = "ColumnButton",
                CustomMinimumSize = new Vector2(c.Width, 0),
                SizeFlagsHorizontal = column == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill,
                Alignment = c.Number ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
            };
            button.Pressed += () =>
            {
                _sortDown = _sortColumn == column && !_sortDown;
                _sortColumn = column;
                ShowColumns();
                _dirty = true;
            };
            _columns.AddChild(button);
        }
    }

    private bool Shows(DataRow row)
    {
        bool everything = _tabs.GetChildCount() == 0 || Tab == ((Button)_tabs.GetChild(0)).Text;
        if (!everything && !row.Tags.Contains(Tab))
        {
            return false;
        }
        if (_chipsOn.Any(chip => !row.Tags.Contains(chip)))
        {
            return false;
        }
        string search = _search.Text.Trim();
        return search.Length == 0
            || row.Cells.Any(c => c.Contains(search, StringComparison.OrdinalIgnoreCase))
            || row.Search.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private int Compare(DataRow a, DataRow b)
    {
        int column = _sortColumn;
        IComparable? x = column < a.Sort.Length ? a.Sort[column] : null;
        IComparable? y = column < b.Sort.Length ? b.Sort[column] : null;
        int order = x != null && y != null && x.GetType() == y.GetType()
            ? x.CompareTo(y)
            : string.Compare(column < a.Cells.Length ? a.Cells[column] : "", column < b.Cells.Length ? b.Cells[column] : "", StringComparison.OrdinalIgnoreCase);
        if (order == 0 && column != 0)
        {
            order = string.Compare(a.Cells.FirstOrDefault() ?? "", b.Cells.FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase);
        }
        return _sortDown ? -order : order;
    }

    private void ShowRows()
    {
        _dirty = false;
        List<DataRow> shown = _rows.Where(Shows).ToList();
        // a stable sort, so rows that tie keep the owner's order; a grid keeps the owner's sections as they are
        if (!Grid)
        {
            shown = shown.Select((row, i) => (row, i)).OrderBy(p => p.row, Comparer<DataRow>.Create(Compare)).ThenBy(p => p.i).Select(p => p.row).ToList();
        }
        List<DataRow> other = _other.Visible
            ? _otherRows.Where(Shows).Select((row, i) => (row, i)).OrderBy(p => p.row, Comparer<DataRow>.Create(Compare)).ThenBy(p => p.i).Select(p => p.row).ToList()
            : new List<DataRow>();
        if (shown.All(r => r.Key != Picked) && other.All(r => r.Key != Picked))
        {
            Picked = shown.Count > 0 ? shown[0].Key : other.Count > 0 ? other[0].Key : "";
        }
        // a count only says something when a filter hides part of the list
        _count.Text = shown.Count == _rows.Count ? "" : $"{shown.Count} of {_rows.Count}";
        if (Grid)
        {
            ShowTiles(shown);
            return;
        }
        FillRows(_items, _rowButtons, shown);
        FillRows(_otherItems, _otherButtons, other);
    }

    // one list's rows as buttons: made as needed, kept and reused, the rest hidden
    private void FillRows(VBoxContainer items, List<Button> buttons, List<DataRow> shown)
    {
        while (buttons.Count < shown.Count)
        {
            var button = new Button
            {
                ToggleMode = true,
                CustomMinimumSize = new Vector2(0, RowHeight),
                FocusMode = FocusModeEnum.None,
                ClipContents = true,
            };
            var cells = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            cells.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            cells.OffsetLeft = 6;
            cells.OffsetRight = -6;
            cells.AddThemeConstantOverride("separation", 6);
            button.AddChild(cells);
            button.Pressed += () => Pick(button.GetMeta("key").AsString());
            button.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true })
                {
                    Pick(button.GetMeta("key").AsString());
                    // owners fill the entry for the picked row each frame; act once that has happened
                    GetTree().CreateTimer(0.1).Timeout += DoMainAction;
                }
            };
            items.AddChild(button);
            buttons.Add(button);
        }
        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            button.Visible = i < shown.Count;
            if (!button.Visible)
            {
                continue;
            }
            DataRow row = shown[i];
            button.SetMeta("key", row.Key);
            button.SetMeta("words", row.Cells.Length > 0 ? row.Cells[0] : ""); // input scripts click a row by it
            button.ThemeTypeVariation = i % 2 == 0 ? "RowButton" : "RowOddButton";
            button.SetPressedNoSignal(row.Key == Picked);
            var cells = button.GetChild<HBoxContainer>(0);
            while (cells.GetChildCount() < _columnList.Count)
            {
                cells.AddChild(new Label { ClipText = true, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
            }
            for (int c = 0; c < cells.GetChildCount(); c++)
            {
                var label = cells.GetChild<Label>(c);
                label.Visible = c < _columnList.Count;
                if (!label.Visible)
                {
                    continue;
                }
                DataColumn column = _columnList[c];
                label.Text = c < row.Cells.Length ? row.Cells[c] : "";
                label.CustomMinimumSize = new Vector2(column.Width, 0);
                label.SizeFlagsHorizontal = c == 0 ? SizeFlags.ExpandFill : SizeFlags.Fill;
                label.HorizontalAlignment = column.Number ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                label.ThemeTypeVariation = row.Dim ? "DimLabel" : c == 0 ? "" : column.Number ? "NumberLabel" : "CellLabel";
                label.AddThemeFontSizeOverride("font_size", column.Number ? 13 : 14);
            }
        }
    }

    // The grid: a heading per section and its tiles under it, rebuilt only when what is shown
    // changes, so a tile being dragged isn't pulled out from under the pointer.
    private void ShowTiles(List<DataRow> shown)
    {
        string layout = string.Join("|", shown.Select(r => r.Section + "/" + r.Key));
        if (layout != _tilesShown)
        {
            _tilesShown = layout;
            Clear(_items);
            _rowButtons.Clear();
            _tiles.Clear();
            HFlowContainer? flow = null;
            string section = "\u0000";
            foreach (DataRow row in shown)
            {
                if (row.Section != section || flow == null)
                {
                    section = row.Section;
                    if (section.Length > 0)
                    {
                        _items.AddChild(new Label { Text = section.ToUpperInvariant(), ThemeTypeVariation = "CapsLabel" });
                    }
                    flow = new HFlowContainer();
                    flow.AddThemeConstantOverride("h_separation", 4);
                    flow.AddThemeConstantOverride("v_separation", 4);
                    _items.AddChild(flow);
                }
                var tile = new IconTile();
                tile.Pressed += () => Pick(tile.Key);
                flow.AddChild(tile);
                _tiles[row.Key] = tile;
            }
        }
        foreach (DataRow row in shown)
        {
            IconTile tile = _tiles[row.Key];
            tile.Drag = row.Drag;
            tile.Show(row.Key, row.Cells.Length > 0 ? row.Cells[0] : "", row.Picture, row.Badge, row.Dim, row.Key == Picked);
        }
    }

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
