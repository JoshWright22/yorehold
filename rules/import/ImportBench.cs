using System.Globalization;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// A comparison run of the import: every book in a folder goes through every version of the
/// import the folder lists, each is built and scored, and the scores are set beside the run
/// before. It is how a change to the import (new code, another model, a switch turned off) is
/// judged: on more than one book, by the same measure, against what it was.
///
/// The folder: books/ holds the books, each with its answer key beside it (name.key.json, see
/// ImportKey); bench.json lists the versions; runs/label/ gets each run's packages and report.txt;
/// history.jsonl keeps one line per book and version of every run.
/// </summary>
public sealed class ImportBench
{
    public const string Format = "yorehold.import-bench";
    public const string FileName = "bench.json";
    public const string BooksFolder = "books";
    public const string RunsFolder = "runs";
    public const string HistoryFile = "history.jsonl";
    public const string ReportFile = "report.txt";

    /// <summary>One way of running the import: which of its stages are on, and the story model, if any.</summary>
    public sealed record Version(string Name, bool Cast, bool Walls, string Model, string ModelName);

    /// <summary>One book through one version.</summary>
    public sealed record Result(string Label, string Book, string Version, int Judged, int Overall, Dictionary<string, (int Found, int Of)> Parts,
        List<string> Missed, List<string> Extras, List<string> Problems, double Seconds);

    private readonly string _folder;
    private readonly ContentFiles _game;

    public ImportBench(string folder, ContentFiles game)
    {
        _folder = folder;
        _game = game;
    }

    /// <summary>Makes a model for a version that names one; a test puts its own in.</summary>
    public Func<Version, IStoryModel?> ModelFor { get; init; } = v => v.Model.Length > 0 ? new ChatModel(v.Model, v.ModelName) : null;

    /// <summary>The versions bench.json lists; without the file, the layout alone and everything on.</summary>
    public List<Version> Versions()
    {
        string file = Path.Combine(_folder, FileName);
        if (!File.Exists(file))
        {
            return new List<Version> { new("layout", false, false, "", ""), new("full", true, true, "", "") };
        }
        ContentNode root = ContentNode.Parse(FileName, File.ReadAllText(file)).RequireObject("a comparison run");
        root.Only("format", "versions");
        if (root.Text("format", "") != Format)
        {
            throw root.Fail("format", $"is \"{Format}\"");
        }
        var versions = new List<Version>();
        foreach (ContentNode v in root.At("versions").Items())
        {
            v.Only("name", "cast", "walls", "model", "modelName");
            string name = v.At("name").AsText(40);
            if (OutlineBuilder.Slug(name) != name)
            {
                throw v.Fail("name", "is lower-case letters, digits and dashes: it names a folder");
            }
            if (versions.Any(other => other.Name == name))
            {
                throw v.Fail("name", $"two versions are called \"{name}\"");
            }
            versions.Add(new Version(name, v.Bool("cast", true), v.Bool("walls", true), v.Text("model", "", 400), v.Text("modelName", "", 100)));
        }
        if (versions.Count == 0)
        {
            throw root.Fail("versions", "lists no versions");
        }
        return versions;
    }

    public List<string> Books()
    {
        string books = Path.Combine(_folder, BooksFolder);
        return Directory.Exists(books)
            ? Directory.GetFiles(books).Where(BookReader.CanRead).OrderBy(b => b, StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();
    }

    /// <summary>
    /// Runs every book through every version under this label (a run made before under the same
    /// label is replaced), writes the run's report and adds it to the history. say gets a line
    /// as each import ends, for whoever is watching.
    /// </summary>
    public async Task<List<Result>> Run(string label, Action<string>? say = null, CancellationToken cancel = default)
    {
        label = OutlineBuilder.Slug(label);
        string run = Path.Combine(_folder, RunsFolder, label);
        if (Directory.Exists(run))
        {
            Directory.Delete(run, true);
        }
        var results = new List<Result>();
        List<Version> versions = Versions();
        foreach (string book in Books())
        {
            string name = OutlineBuilder.Slug(BookLayout.CleanTitle(Path.GetFileNameWithoutExtension(book)));
            foreach (Version version in versions)
            {
                DateTime began = DateTime.UtcNow;
                string package = Path.Combine(run, name, version.Name);
                var import = new StoryImport(package, _game) { ScoresFolder = Path.Combine(_folder, BooksFolder), Cast = version.Cast, Walls = version.Walls, KeepHistory = false };
                var problems = new List<string>();
                try
                {
                    await import.Read(book, ModelFor(version), cancel);
                    problems.AddRange(import.Build());
                }
                catch (Exception error) when (error is ContentException or IOException or HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    problems.Add(error.Message);
                }
                if (import.ScoreProblem.Length > 0)
                {
                    problems.Add(import.ScoreProblem);
                }
                ImportScore? score = import.Score;
                var result = new Result(label, name, version.Name, problems.Count > 0 && score?.Built != true ? 0 : score?.Judged ?? 0, score?.Overall ?? 0,
                    score?.Parts.Where(p => p.Scored).ToDictionary(p => p.Id, p => (p.Found, p.Of)) ?? new(),
                    score?.Find("key")?.Missing.ToList() ?? new(), score?.Extras.ToList() ?? new(), problems,
                    Math.Round((DateTime.UtcNow - began).TotalSeconds, 1));
                results.Add(result);
                say?.Invoke($"{name} / {version.Name}: {result.Judged}" + (problems.Count > 0 ? $" ({problems[0]})" : ""));
            }
        }
        List<Result> history = History().Where(r => r.Label != label).ToList();
        Directory.CreateDirectory(run);
        File.WriteAllLines(Path.Combine(run, ReportFile), Report(results, history));
        File.WriteAllLines(Path.Combine(_folder, HistoryFile), history.Concat(results).Select(r => CreateJson.Compact(ToJson(r))));
        return results;
    }

    /// <summary>Every result of every run so far, oldest first.</summary>
    public List<Result> History()
    {
        string file = Path.Combine(_folder, HistoryFile);
        var results = new List<Result>();
        if (!File.Exists(file))
        {
            return results;
        }
        foreach (string line in File.ReadAllLines(file).Where(l => l.Trim().Length > 0))
        {
            if (JsonNode.Parse(line) is not JsonObject j)
            {
                continue;
            }
            var parts = new Dictionary<string, (int, int)>();
            foreach (KeyValuePair<string, JsonNode?> part in j["parts"] as JsonObject ?? new JsonObject())
            {
                if (part.Value is JsonArray { Count: 2 } pair)
                {
                    parts[part.Key] = (pair[0]!.GetValue<int>(), pair[1]!.GetValue<int>());
                }
            }
            List<string> Texts(string key) => (j[key] as JsonArray ?? new JsonArray()).Select(t => t?.ToString() ?? "").ToList();
            results.Add(new Result(j["label"]?.ToString() ?? "", j["book"]?.ToString() ?? "", j["version"]?.ToString() ?? "",
                j["judged"]?.GetValue<int>() ?? 0, j["overall"]?.GetValue<int>() ?? 0, parts, Texts("missed"), Texts("extras"), Texts("problems"),
                j["seconds"]?.GetValue<double>() ?? 0));
        }
        return results;
    }

    private static JsonObject ToJson(Result r)
    {
        var parts = new JsonObject();
        foreach ((string id, (int found, int of)) in r.Parts)
        {
            parts[id] = new JsonArray(found, of);
        }
        return new JsonObject
        {
            ["label"] = r.Label,
            ["at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["book"] = r.Book,
            ["version"] = r.Version,
            ["judged"] = r.Judged,
            ["overall"] = r.Overall,
            ["parts"] = parts,
            ["missed"] = CreateJson.Texts(r.Missed),
            ["extras"] = CreateJson.Texts(r.Extras),
            ["problems"] = CreateJson.Texts(r.Problems),
            ["seconds"] = r.Seconds,
        };
    }

    /// <summary>
    /// The run in words: each version's mean over the books, then each book and version with its
    /// parts, each held against the same book and version in the run before (the last label in
    /// the history) and against the best any run has done; then the key's lines won and lost.
    /// </summary>
    public static List<string> Report(List<Result> run, List<Result> history)
    {
        var lines = new List<string>();
        if (run.Count == 0)
        {
            lines.Add("No books: put them in the books folder, each with its answer key beside it.");
            return lines;
        }
        string before = history.LastOrDefault()?.Label ?? "";
        Result? Before(Result r) => history.LastOrDefault(h => h.Label == before && h.Book == r.Book && h.Version == r.Version);
        string Change(int now, int? was) => was is int w ? (now == w ? "  =" : $"{now - w,3:+#;-#}") : "new";

        lines.Add($"Run {run[0].Label}" + (before.Length > 0 ? $", held against {before}" : ", the first run here"));
        lines.Add("");
        lines.Add("Each version over all books (the judged score: the answer key met, marked down for what was made up; without a key, the overall score)");
        foreach (IGrouping<string, Result> version in run.GroupBy(r => r.Version))
        {
            int mean = (int)Math.Round(version.Average(r => r.Judged));
            List<Result> was = version.Select(Before).OfType<Result>().ToList();
            int best = history.Where(h => h.Version == version.Key).GroupBy(h => h.Label)
                .Where(g => g.Select(h => h.Book).ToHashSet().SetEquals(version.Select(r => r.Book)))
                .Select(g => (int)Math.Round(g.Average(h => h.Judged))).DefaultIfEmpty(-1).Max();
            lines.Add($"  {version.Key,-16} {mean,3}  {Change(mean, was.Count == version.Count() ? (int)Math.Round(was.Average(r => r.Judged)) : null)}"
                + (best >= 0 ? $"   best before {best}" + (mean > best ? ", beaten" : "") : ""));
        }
        Result top = run.GroupBy(r => r.Version).OrderByDescending(g => g.Average(r => r.Judged)).First().First();
        lines.Add($"  best this run: {top.Version}");
        foreach (IGrouping<string, Result> book in run.GroupBy(r => r.Book))
        {
            lines.Add("");
            lines.Add(book.Key);
            foreach (Result r in book)
            {
                Result? was = Before(r);
                lines.Add($"  {r.Version,-16} {r.Judged,3}  {Change(r.Judged, was?.Judged)}   overall {r.Overall}, {r.Seconds:0.#} s   "
                    + string.Join("  ", r.Parts.Select(p => $"{p.Key} {p.Value.Found}/{p.Value.Of}")));
                foreach (string problem in r.Problems)
                {
                    lines.Add($"      PROBLEM {problem}");
                }
                if (was != null)
                {
                    foreach (string won in was.Missed.Except(r.Missed))
                    {
                        lines.Add($"      won   {won}");
                    }
                    foreach (string lost in r.Missed.Except(was.Missed))
                    {
                        lines.Add($"      lost  {lost}");
                    }
                    foreach (string extra in r.Extras.Except(was.Extras))
                    {
                        lines.Add($"      now made up: {extra}");
                    }
                }
                else
                {
                    lines.AddRange(r.Missed.Take(12).Select(m => $"      missed {m}"));
                    lines.AddRange(r.Extras.Take(12).Select(e => $"      made up: {e}"));
                }
            }
        }
        return lines;
    }
}
