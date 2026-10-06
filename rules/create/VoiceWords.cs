using System.Text;

namespace Yorehold.Rules;

/// <summary>What the speech model heard, one token at a time. Times are in seconds; Dtw is negative when there is none.</summary>
public sealed record HeardToken(string Text, float Probability, double Start, double End, double Dtw = -1);

/// <summary>
/// Turning what was heard into the written line's words: tokens joined into words, and heard
/// words lined up with the written ones. Ported from the C++ client's voice code.
/// </summary>
public static class VoiceWords
{
    /// <summary>"Kharos," is "kharos". Letters and digits only, lower case; apostrophes inside a word stay.</summary>
    public static string Normalize(string word)
    {
        var text = new StringBuilder();
        foreach (char c in word)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9' || c >= 0x80)
            {
                text.Append(c);
            }
            else if (c is >= 'A' and <= 'Z')
            {
                text.Append((char)(c - 'A' + 'a'));
            }
            else if (c == '\'' && text.Length > 0)
            {
                text.Append(c);
            }
        }
        return text.ToString().TrimEnd('\'');
    }

    /// <summary>Words as written, split on spaces, punctuation kept on them.</summary>
    public static List<string> Split(string text) => text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Tokens of one segment into words. DTW puts a token about where it ends, so a word starts at
    /// the DTW time of the token before it, else at its first token's start. It ends where the next
    /// word starts, the last one at segmentEnd. Its confidence is the lowest among its tokens.
    /// </summary>
    public static List<VoiceWord> JoinTokens(IReadOnlyList<HeardToken> tokens, double segmentEnd)
    {
        var words = new List<VoiceWord>();
        double before = -1; // the DTW time of the token before this one
        foreach (HeardToken token in tokens)
        {
            double previous = before;
            before = token.Dtw;
            bool starts = token.Text.StartsWith(' ');
            string text = token.Text.TrimStart(' ');
            if (text.Length == 0)
            {
                continue;
            }
            // a token of punctuation alone ("!", " -") belongs to the word before it
            if (words.Count > 0 && (!starts || Normalize(text).Length == 0))
            {
                words[^1] = words[^1] with { Text = words[^1].Text + text, Confidence = Math.Min(words[^1].Confidence, token.Probability) };
                continue;
            }
            words.Add(new VoiceWord { Text = text, Start = previous >= 0 ? previous : token.Start, Confidence = token.Probability });
        }
        for (int i = 0; i < words.Count; i++)
        {
            double end = i + 1 < words.Count ? words[i + 1].Start : segmentEnd;
            words[i] = words[i] with { End = Math.Max(end, words[i].Start) };
        }
        return words;
    }

    /// <summary>
    /// The written line wins: its words get the timings of the heard words they line up with, by
    /// edit distance over normalized words. A written word with no close heard word gets a time
    /// spread between its matched neighbours and Matched false. An empty written line keeps the
    /// heard words as they are, as a suggestion. length is the recording's length in seconds.
    /// </summary>
    public static List<VoiceWord> Match(IReadOnlyList<VoiceWord> heard, string written, double length)
    {
        List<string> wordsWritten = Split(written);
        if (wordsWritten.Count == 0)
        {
            return heard.ToList();
        }
        int n = wordsWritten.Count, m = heard.Count;
        string[] a = wordsWritten.Select(Normalize).ToArray();
        string[] b = heard.Select(h => Normalize(h.Text)).ToArray();

        // cost[i, j]: the first i written words lined up with the first j heard ones. Besides the
        // usual steps, one written word may take two heard ones, for a name heard in pieces.
        var cost = new double[n + 1, m + 1];
        var step = new Step[n + 1, m + 1];
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                cost[i, j] = double.PositiveInfinity;
            }
        }
        cost[0, 0] = 0;
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                void Offer(double value, Step how)
                {
                    if (value < cost[i, j])
                    {
                        cost[i, j] = value;
                        step[i, j] = how;
                    }
                }
                // skips are offered first so a tie leaves out the later words: a recording that
                // stops before the written line does pairs its last word with the earlier one of the same
                if (i > 0)
                {
                    Offer(cost[i - 1, j] + 1, Step.Skip); // written, not heard
                }
                if (j > 0)
                {
                    Offer(cost[i, j - 1] + 1, Step.Drop); // heard, not written
                }
                if (i > 0 && j > 0)
                {
                    Offer(cost[i - 1, j - 1] + WordCost(a[i - 1], b[j - 1]), Step.Pair);
                }
                // only when neither piece is the word by itself, so "uh the" stays "uh" left out and "the"
                if (i > 0 && j > 1 && !Alike(a[i - 1], b[j - 2]) && !Alike(a[i - 1], b[j - 1]))
                {
                    Offer(cost[i - 1, j - 2] + WordCost(a[i - 1], b[j - 2] + b[j - 1]) + 0.25, Step.Pair2);
                }
            }
        }

        var result = new VoiceWord[n];
        var timed = new bool[n];
        for (int i = n, j = m; i > 0 || j > 0;)
        {
            switch (step[i, j])
            {
                case Step.Pair:
                    result[i - 1] = heard[j - 1] with { Matched = Alike(a[i - 1], b[j - 1]) };
                    timed[i - 1] = true;
                    i--;
                    j--;
                    break;
                case Step.Pair2:
                    result[i - 1] = heard[j - 2] with
                    {
                        End = heard[j - 1].End,
                        Confidence = Math.Min(heard[j - 2].Confidence, heard[j - 1].Confidence),
                        Matched = Alike(a[i - 1], b[j - 2] + b[j - 1]),
                    };
                    timed[i - 1] = true;
                    i--;
                    j -= 2;
                    break;
                case Step.Skip:
                    i--;
                    break;
                case Step.Drop:
                    j--;
                    break;
            }
        }

        // words nobody heard share the time between the heard ones around them, by their length
        double first = m > 0 ? heard[0].Start : 0;
        double last = Math.Max(Math.Max(m > 0 ? heard[^1].End : 0, first), length);
        for (int i = 0; i < n;)
        {
            if (timed[i])
            {
                result[i] = result[i] with { Text = wordsWritten[i] };
                i++;
                continue;
            }
            int to = i;
            while (to < n && !timed[to])
            {
                to++;
            }
            double from = i > 0 ? result[i - 1].End : first;
            double until = Math.Max(from, to < n ? result[to].Start : last);
            int letters = 0;
            for (int k = i; k < to; k++)
            {
                letters += Math.Max(1, a[k].Length);
            }
            double at = from;
            for (int k = i; k < to; k++)
            {
                double share = (until - from) * Math.Max(1, a[k].Length) / letters;
                result[k] = new VoiceWord { Text = wordsWritten[k], Start = at, End = at + share, Confidence = 1, Matched = false };
                at += share;
            }
            i = to;
        }
        return result.ToList();
    }

    private enum Step : byte
    {
        Pair,
        Pair2,
        Skip,
        Drop,
    }

    // Letters changed, added or taken away to turn one word into the other.
    private static int LetterDistance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            int diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    // 0 for the same word, up to 1 for nothing alike.
    private static double WordCost(string a, string b)
    {
        int longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 0 : (double)LetterDistance(a, b) / longest;
    }

    // Close enough that the heard word is the written one, misheard ("Carlos" for "Kharos").
    private static bool Alike(string a, string b) => LetterDistance(a, b) * 2 <= Math.Max(a.Length, b.Length);
}
