using System.Text.RegularExpressions;

namespace AnkiStats.Core;

/// <summary>Local gloss evidence; arbitrary English paraphrases are not assumed equivalent.</summary>
public static class DefinitionMatcher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private static readonly HashSet<string> FunctionWords = ["to", "a", "an", "the"];
    private static readonly HashSet<string> Negatives = ["not", "no", "without", "never", "cannot", "can't", "won't", "isn't", "aren't", "wasn't", "weren't", "don't", "doesn't", "didn't", "wouldn't", "shouldn't", "couldn't"];
    private static readonly HashSet<string> BroadWords = ["thing", "things", "person", "people", "one", "do", "get", "be", "have", "make", "take", "use", "way", "something", "someone", "it", "that", "this", "not", "no", "without", "never", "cannot", "isn't", "don't", "doesn't"];
    private static List<string> Phrases(string meaning)
    {
        var clean = TextNormalizer.Clean(meaning).Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant().Replace("’", "'").Replace("‘", "'");
        // Qualifiers can distinguish meanings (river bank / financial bank); retain their words.
        clean = Regex.Replace(clean, @"[()\[\]]", " ", RegexOptions.None, Timeout);
        return Regex.Split(clean, @"[;,/\r\n]+", RegexOptions.None, Timeout).Select(phrase =>
            string.Join(" ", Regex.Matches(phrase, @"[a-z]+(?:'[a-z]+)?", RegexOptions.None, Timeout)
                .Select(m => m.Value).Where(word => !FunctionWords.Contains(word))))
            .Where(p => p.Length > 0).Distinct().ToList();
    }
    public static string Fingerprint(string meaning) => Regex.Replace(TextNormalizer.Clean(meaning)
        .Normalize(System.Text.NormalizationForm.FormKC).ToLowerInvariant(), @"\s+", " ", RegexOptions.None, Timeout).Trim();
    public static int Score(string meaning, string dictionaryMeaning)
    {
        var best = 0;
        foreach (var note in Phrases(meaning))
            foreach (var gloss in Phrases(dictionaryMeaning))
            {
                var noteWords = note.Split(' '); var glossWords = gloss.Split(' ');
                if (noteWords.Any(Negatives.Contains) != glossWords.Any(Negatives.Contains)) continue;
                if (noteWords.All(w => BroadWords.Contains(w) || Negatives.Contains(w)) || glossWords.All(w => BroadWords.Contains(w) || Negatives.Contains(w))) continue;
                if (note == gloss) best = Math.Max(best, 3);
                else if (noteWords.Distinct().Count() >= 2 && glossWords.Distinct().Count() >= 2
                    && (noteWords.All(glossWords.Contains) || glossWords.All(noteWords.Contains))) best = Math.Max(best, 2);
                else if (Math.Max(noteWords.Length, glossWords.Length) <= 4
                    && (ContainsPhrase(note, gloss) || ContainsPhrase(gloss, note))) best = Math.Max(best, 1);
            }
        return best;
    }
    private static bool ContainsPhrase(string longer, string shorter) => (" " + longer + " ").Contains(" " + shorter + " ", StringComparison.Ordinal);
}
