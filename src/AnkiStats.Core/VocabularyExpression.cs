using System.Text.RegularExpressions;

namespace AnkiStats.Core;

public sealed record VocabularyExpression(string Word, List<string> Readings, string? Error, string? JoinedReading = null)
{
    private const string Kana = @"^[ぁ-ゖァ-ヺー]+$";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
    private static List<string> Alternatives(string value) => Regex.Split(value.Trim(), @"[・/／、,，;；|｜\s]+", RegexOptions.None, Timeout)
        .Where(v => v.Length > 0).Select(VocabularyMatcher.Key).ToList();

    public static VocabularyExpression Parse(NoteRecord note)
    {
        var source = TextNormalizer.Clean(string.IsNullOrWhiteSpace(note.SourceExpression) ? note.Expression : note.SourceExpression)
            .Normalize(System.Text.NormalizationForm.FormKC);
        var readings = new List<string> { "" };
        var word = ""; var cursor = 0; var joined = ""; var hasSeparators = false;
        var annotations = Regex.Matches(source, @"([\p{L}\p{N}々ー]+)\s*\[([^\[\]]+)\]", RegexOptions.None, Timeout);
        foreach (Match annotation in annotations)
        {
            var alternatives = Alternatives(annotation.Groups[2].Value);
            if (alternatives.Count == 0 || alternatives.Any(v => !Regex.IsMatch(v, Kana, RegexOptions.None, Timeout)))
                return new(source, [], "Unsupported bracket annotation; review the target expression");
            var preceding = source[cursor..annotation.Index];
            word += preceding + annotation.Groups[1].Value;
            joined += preceding + string.Concat(alternatives);
            hasSeparators |= alternatives.Count > 1;
            if (readings.Count * alternatives.Count > 32)
                return new(source, [], "Too many reading combinations; map a simpler target field");
            readings = readings.SelectMany(prefix => alternatives.Select(reading => prefix + preceding + reading)).ToList();
            cursor = annotation.Index + annotation.Length;
        }
        word += source[cursor..];
        readings = readings.Select(r => r + source[cursor..]).ToList();
        joined += source[cursor..];
        word = word.Replace(" ", "").Replace("　", "").Normalize(System.Text.NormalizationForm.FormKC).Trim();
        if (word.Contains('[') || word.Contains(']')) return new(word, [], "Unsupported bracket annotation; review the target expression");
        if (!string.IsNullOrWhiteSpace(note.Reading))
        {
            readings = Alternatives(TextNormalizer.Clean(note.Reading));
            joined = string.Concat(readings); hasSeparators = readings.Count > 1;
            if (readings.Count == 0 || readings.Any(v => !Regex.IsMatch(v, Kana, RegexOptions.None, Timeout)))
                return new(word, [], "The mapped reading is not a kana reading or explicit reading list");
        }
        else if (annotations.Count == 0)
        {
            // An unannotated kana word supplies its own reading, while preserving its written script.
            readings = Regex.IsMatch(word, Kana, RegexOptions.None, Timeout) ? [word] : [];
        }
        else if (readings.Any(v => !Regex.IsMatch(v.Replace(" ", "").Replace("　", ""), Kana, RegexOptions.None, Timeout)))
            return new(word, [], "Furigana does not provide a complete kana reading");
        joined = VocabularyMatcher.Key(joined.Replace(" ", "").Replace("　", ""));
        return new(word, readings.Select(r => VocabularyMatcher.Key(r.Replace(" ", "").Replace("　", ""))).Distinct().ToList(), null,
            hasSeparators && Regex.IsMatch(joined, Kana, RegexOptions.None, Timeout) ? joined : null);
    }
}
