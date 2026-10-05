namespace Yorehold.Rules;

/// <summary>
/// A content file that can't be used. The message always starts with the file, then the field when
/// one is known: "items/mace.json: hands: is a whole number from 0 to 4".
/// </summary>
public class ContentException : Exception
{
    public string File { get; }
    public string Field { get; }
    public string Problem { get; }

    public ContentException(string file, string field, string problem)
        : base(Describe(file, field, problem))
    {
        File = file;
        Field = field;
        Problem = problem;
    }

    private static string Describe(string file, string field, string problem)
    {
        string where = field.Length == 0 ? "" : field + ": ";
        return file.Length == 0 ? where + problem : file + ": " + where + problem;
    }
}
