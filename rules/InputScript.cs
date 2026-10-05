namespace Yorehold.Rules;

/// <summary>One line of an input script: on this frame, do this.</summary>
public readonly record struct InputStep(int Frame, string Command, string A, string B);

/// <summary>
/// Reads the input scripts used by screenshot runs. One step per line: a frame number, a command
/// and up to two words. The format is the one the C++ client used, so old scripts still read.
///   move X Y | down left|right|middle | up ... | key Name | keyup Name | text some words |
///   wheel N | shot path.png | cell X Y | pinch F
/// </summary>
public static class InputScript
{
    public static List<InputStep> Parse(string text)
    {
        var steps = new List<InputStep>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            string[] words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Anything that does not start with a frame number is skipped, so "# notes" work as comments.
            if (words.Length < 2 || !int.TryParse(words[0], out int frame))
            {
                continue;
            }

            string command = words[1];
            if (command == "text")
            {
                // Typed text keeps its spaces: everything after the command word.
                int at = line.IndexOf("text", StringComparison.Ordinal) + 4;
                steps.Add(new InputStep(frame, command, line[at..].TrimStart(), ""));
                continue;
            }

            steps.Add(new InputStep(frame, command, words.Length > 2 ? words[2] : "", words.Length > 3 ? words[3] : ""));
        }
        return steps;
    }
}
