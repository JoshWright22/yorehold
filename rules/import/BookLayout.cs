using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// What a book's layout says without a story model: whose each picture is (the name set right
/// under or over it), which picture is the map (the one with the place numbers printed on it,
/// and where each number sits), and the numbered places with the passages boxed for reading out.
/// Draft turns that into a first outline, so a run with no model still has places to review.
/// </summary>
public static partial class BookLayout
{
    /// <summary>How far a name may sit from its picture, in points (about four lines of text).</summary>
    public const float NameReach = 48;

    /// <summary>A place the book numbers: "1: OUTSIDE THE CAVES", its passages and its notes.</summary>
    public sealed record NumberedPlace(string Label, string Name, int Page, string Heading, List<string> ReadAloud, List<string> Notes);

    /// <summary>The book's map: a picture and where on it each place's number is, as a share of its width and height.</summary>
    public sealed record MapPicture(string File, Dictionary<string, (float X, float Y)> Labels);

    [GeneratedRegex(@"^(?:AREA\s+|ROOM\s+)?(\d{1,3}[A-Za-z]?)\s*[:.]\s*(\S.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceHeading();

    [GeneratedRegex(@"^\d{1,3}[A-Za-z]?(?:\s+\d{1,3}[A-Za-z]?)*$")]
    private static partial Regex NumbersOnly();

    /// <summary>Each picture's name: the heading in its column nearest under it, else nearest over it.</summary>
    public static Dictionary<string, string> PictureNames(SourceBook book)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SourceBook.Picture picture in book.Pictures)
        {
            SourceBook.Page? page = book.Pages.FirstOrDefault(p => p.Number == picture.Page);
            if (page == null)
            {
                continue;
            }
            SourceBook.Block? best = null;
            float bestGap = float.MaxValue;
            foreach (SourceBook.Block block in page.Blocks)
            {
                if (block.Kind != "heading" || block.Box.Length > 0 || NumbersOnly().IsMatch(block.Text) || !SameColumn(block, picture))
                {
                    continue;
                }
                float under = block.Y - (picture.Y + picture.Height);
                float over = picture.Y - (block.Y + block.Height);
                // a name under the picture wins a tie with one over it: that's how books caption figures
                float gap = under >= -2 ? under : over >= -2 ? over + 0.5f : float.MaxValue;
                if (gap <= NameReach && gap < bestGap)
                {
                    best = block;
                    bestGap = gap;
                }
            }
            // a name is short; a long heading by a picture is the page's own title or a back-cover blurb
            if (best != null && best.Text.Length <= 30)
            {
                names[picture.File] = best.Text;
            }
        }
        return names;
    }

    // overlapping across by at least half of the narrower one
    private static bool SameColumn(SourceBook.Block block, SourceBook.Picture picture)
    {
        float overlap = Math.Min(block.X + block.Width, picture.X + picture.Width) - Math.Max(block.X, picture.X);
        return overlap >= 0.5f * Math.Min(block.Width, picture.Width);
    }

    /// <summary>The places the book numbers, in its order, each with what follows it up to the next one.</summary>
    public static List<NumberedPlace> Places(SourceBook book)
    {
        var places = new List<NumberedPlace>();
        NumberedPlace? current = null;
        foreach (SourceBook.Page page in book.Pages)
        {
            foreach (SourceBook.Block block in page.Blocks)
            {
                if (block.Kind == "heading" && block.Box.Length == 0 && PlaceHeading().Match(block.Text.Trim()) is { Success: true } match)
                {
                    current = new NumberedPlace(match.Groups[1].Value.ToUpperInvariant(), TitleCase(match.Groups[2].Value), page.Number, block.Text.Trim(), new List<string>(), new List<string>());
                    places.Add(current);
                    continue;
                }
                if (current == null || block.Kind != "text")
                {
                    continue;
                }
                if (block.Box == "shaded")
                {
                    current.ReadAloud.Add(block.Text);
                }
                else if (block.Box == "framed")
                {
                    current.Notes.Add(block.Text);
                }
            }
        }
        // a book numbers each place once; a second "1:" is a list somewhere else, not a place
        return places.GroupBy(p => p.Label).Select(g => g.First()).ToList();
    }

    /// <summary>The picture with the most of the places' numbers printed over it; null when none has two.</summary>
    public static MapPicture? FindMap(SourceBook book, IReadOnlyCollection<string> labels)
    {
        MapPicture? best = null;
        foreach (SourceBook.Picture picture in book.Pictures)
        {
            SourceBook.Page? page = book.Pages.FirstOrDefault(p => p.Number == picture.Page);
            if (page == null || picture.Width <= 0 || picture.Height <= 0)
            {
                continue;
            }
            var found = new Dictionary<string, (float X, float Y)>(StringComparer.Ordinal);
            foreach (SourceBook.Block block in page.Blocks)
            {
                float middleX = block.X + block.Width / 2, middleY = block.Y + block.Height / 2;
                bool over = middleX >= picture.X && middleX <= picture.X + picture.Width && middleY >= picture.Y && middleY <= picture.Y + picture.Height;
                if (!over || !NumbersOnly().IsMatch(block.Text.Trim()))
                {
                    continue;
                }
                // numbers side by side can come out as one block ("7 6"): share its width between them
                string[] numbers = block.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < numbers.Length; i++)
                {
                    string label = numbers[i].ToUpperInvariant();
                    if (!labels.Contains(label) || found.ContainsKey(label))
                    {
                        continue;
                    }
                    float x = block.X + block.Width * (i + 0.5f) / numbers.Length;
                    found[label] = ((x - picture.X) / picture.Width, (middleY - picture.Y) / picture.Height);
                }
            }
            if (found.Count >= 2 && (best == null || found.Count > best.Labels.Count))
            {
                best = new MapPicture(picture.File, found);
            }
        }
        return best;
    }

    /// <summary>
    /// The outline layout alone can give: the adventure and one chapter named for the book, a place
    /// for every numbered place with its passages, the book's notes, and which picture is the map.
    /// Everything carries the page it came from. The story model fills in the rest.
    /// </summary>
    public static Outline Draft(SourceBook book)
    {
        string title = CleanTitle(book.Title);
        var outline = new Outline { Title = title };
        var from = new OutlineSource(book.Pages.FirstOrDefault()?.Number ?? 1, book.Title);
        outline.Entries.Add(new OutlineEntry { Id = "adventure", Kind = OutlineKind.Adventure, Data = new JsonObject { ["title"] = title }, From = from });
        List<NumberedPlace> places = Places(book);
        MapPicture? map = FindMap(book, places.Select(p => p.Label).ToList());
        var chapter = new JsonObject { ["title"] = title };
        if (map != null)
        {
            chapter["mapPicture"] = map.File;
        }
        outline.Entries.Add(new OutlineEntry { Id = "chapter-one", Kind = OutlineKind.Chapter, Data = chapter, From = from });
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (NumberedPlace place in places)
        {
            string id = UniqueId("place-" + OutlineBuilder.Slug(place.Label + " " + place.Name), used);
            var data = new JsonObject
            {
                ["name"] = place.Name,
                ["label"] = place.Label,
                ["size"] = new JsonArray(8, 8),
            };
            if (place.ReadAloud.Count > 0)
            {
                data["readAloud"] = new JsonArray(place.ReadAloud.Select(t => (JsonNode?)t).ToArray());
            }
            if (map != null && map.Labels.TryGetValue(place.Label, out (float X, float Y) at))
            {
                data["mapAt"] = new JsonArray(Math.Round(at.X, 3), Math.Round(at.Y, 3));
            }
            outline.Entries.Add(new OutlineEntry { Id = id, Kind = OutlineKind.Place, Data = data, From = new OutlineSource(place.Page, place.Heading) });
            for (int n = 0; n < place.Notes.Count; n++)
            {
                outline.Entries.Add(new OutlineEntry
                {
                    Id = UniqueId($"{id}-note-{n + 1}", used),
                    Kind = OutlineKind.Note,
                    Data = new JsonObject { ["text"] = place.Notes[n], ["place"] = id, ["why"] = "advice for whoever runs the game" },
                    From = new OutlineSource(place.Page, Quote(place.Notes[n])),
                });
            }
        }
        if (map != null)
        {
            outline.Entries.Add(new OutlineEntry
            {
                Id = UniqueId("book-map", used),
                Kind = OutlineKind.Note,
                Data = new JsonObject
                {
                    ["text"] = "The book's map, with each place's number on it: " + string.Join(", ", map.Labels.OrderBy(l => l.Key, StringComparer.Ordinal)
                        .Select(l => $"{l.Key} at {l.Value.X.ToString("0.00", CultureInfo.InvariantCulture)}, {l.Value.Y.ToString("0.00", CultureInfo.InvariantCulture)}")),
                    ["why"] = "rooms are laid out from the links; trace them over this picture in Map mode",
                },
                From = new OutlineSource(book.Pictures.First(p => p.File == map.File).Page, "map"),
                Picture = map.File,
            });
        }
        return outline;
    }

    private static string UniqueId(string id, HashSet<string> used)
    {
        string unique = id;
        for (int n = 2; !used.Add(unique); n++)
        {
            unique = $"{id}-{n}";
        }
        return unique;
    }

    /// <summary>A title taken from a file name, "Caves_of_Shadow_(3.0)", as "Caves of Shadow".</summary>
    public static string CleanTitle(string title)
    {
        string clean = Regex.Replace(title.Replace('_', ' '), @"\([^)]*\)|\[[^\]]*\]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        return clean.Length > 0 ? clean : title;
    }

    private static string Quote(string text) => text.Length <= 120 ? text : text[..120];

    /// <summary>"OUTSIDE THE CAVES" as "Outside the Caves": small words stay small but the first.</summary>
    public static string TitleCase(string text)
    {
        string[] small = { "a", "an", "and", "at", "by", "for", "in", "of", "on", "or", "the", "to", "with" };
        string[] words = text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (i == 0 || !small.Contains(words[i]))
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }
        }
        return string.Join(' ', words);
    }
}
