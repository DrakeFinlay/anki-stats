using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AnkiStats.Core;

public sealed record Deck(long Id, string Name, int Cards);
public sealed record NoteType(long Id, string Name, List<string> Fields);
public sealed record CollectionMetadata(int Schema, List<Deck> Decks, List<NoteType> NoteTypes);
public sealed record DeckRule(string Name, bool Include);
public sealed class FieldMapping
{
    public long NoteTypeId { get; set; }
    public string Category { get; set; } = "Ignored";
    public int Expression { get; set; }
    public int Reading { get; set; } = -1;
    public int Meaning { get; set; } = -1;
    public string ExpressionName { get; set; } = "";
    public string ReadingName { get; set; } = "";
    public string MeaningName { get; set; } = "";
    public bool StripBracketReadings { get; set; }
}
public sealed class Settings
{
    public string ProfileFolder { get; set; } = "";
    public List<DeckRule> DeckRules { get; set; } = [];
    public List<DeckProfile> Profiles { get; set; } = [];
    public string ActiveProfileId { get; set; } = "";
    public List<FieldMapping> Mappings { get; set; } = [];
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    [System.Text.Json.Serialization.JsonIgnore]
    public string CollectionKey => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(ProfileFolder).ToUpperInvariant())));
    public bool Includes(string name) => Profiles.Count > 0 ? Profiles.Any(p => p.Includes(name)) : DeckProfiles.Includes(DeckRules, name);
    [System.Text.Json.Serialization.JsonIgnore]
    public string Signature => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ProfileFolder, DeckRules, Profiles, Mappings, TimeZoneId })));
}
public sealed record NoteRecord(long Id, string Expression, string Reading, string Meaning, string Category, string NoteType, string SourceExpression = "");
public sealed record CardRecord(long Id, long NoteId, string Deck, int Queue, int Type, int LifetimeLapses);
public sealed record ReviewRecord(long Id, long CardId, int Rating, int Milliseconds, int Kind);
public sealed class ImportedCollection
{
    public List<DeckProfile> Profiles { get; set; } = [];
    public string CollectionKey { get; set; } = "";
    public string Signature { get; set; } = "";
    public string Scope { get; set; } = "";
    public string SourceProfileFolder { get; set; } = "";
    public bool IsSynthetic { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public DateTimeOffset RefreshedAt { get; set; }
    public CollectionMetadata Metadata { get; set; } = new(0, [], []);
    public List<NoteRecord> Notes { get; set; } = [];
    public List<CardRecord> Cards { get; set; } = [];
    public List<ReviewRecord> Reviews { get; set; } = [];
    public int UnresolvedReviews { get; set; }
    public int UnknownReviewKinds { get; set; }
}
public sealed record VocabularyRow(string Expression, string Reading, string Meaning, int Notes, int Cards, string Decks, string NoteIds, string CardIds);
public sealed record SkippedRow(long NoteId, string NoteType, string Reason);
public sealed record DailyRow(DateOnly Day, int Answers);
public sealed record BucketRow(string Period, int Answers, int Again, double? AgainPercent, double? MedianSeconds, int TimedAnswers)
{
    public string AgainDisplay => AgainPercent is {} p ? $"{p:F1}%" : "No data";
    public string TimeDisplay => MedianSeconds is {} s ? $"{s:F2} s" : "No data";
    public string Confidence => Answers < 30 ? "Limited data" : "";
}
public sealed record RankingRow(long CardId, string Expression, string Deck, int Reviews, int Again, int LifetimeLapses);
public sealed record Insight(string Id, string Title, string Detail, string Explanation = "");
public sealed class AnalysisResult
{
    public List<VocabularyRow> Vocabulary { get; set; } = [];
    public List<SkippedRow> Skipped { get; set; } = [];
    public List<DailyRow> Daily { get; set; } = [];
    public List<BucketRow> Buckets { get; set; } = [];
    public List<RankingRow> Rankings { get; set; } = [];
    public List<Insight> Insights { get; set; } = [];
    public int TotalAnswers { get; set; }
    public int OrdinaryReviews { get; set; }
    public int Again { get; set; }
    public string AgainDisplay => OrdinaryReviews > 0 ? $"{100.0 * Again / OrdinaryReviews:F1}%" : "No data";
}
