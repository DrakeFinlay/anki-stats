using System.Text.Json;

namespace AnkiStats.Core;

public sealed class DeckProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New profile";
    public bool Enabled { get; set; } = true;
    public List<DeckRule> DeckRules { get; set; } = [];
    public bool Includes(string name) => DeckProfiles.Includes(DeckRules, name);
    public override string ToString() => Name;
}

public static class DeckProfiles
{
    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
    public static bool Includes(IEnumerable<DeckRule> rules, string name) => rules
        .Where(r => name == r.Name || name.StartsWith(r.Name + "::", StringComparison.Ordinal))
        .OrderByDescending(r => r.Name.Length).FirstOrDefault()?.Include ?? false;

    // Migrate the old single selection once; never replace a user's existing profiles.
    public static void Ensure(Settings settings, CollectionMetadata metadata)
    {
        if (settings.Profiles.Count > 0) return;
        var roots = new[] { "日本語", "漢字" }.Where(root => metadata.Decks.Any(d => d.Name == root || d.Name.StartsWith(root + "::", StringComparison.Ordinal))).ToList();
        foreach (var root in roots)
            settings.Profiles.Add(new() { Name = root, DeckRules = [new(root, true), .. settings.DeckRules.Where(r => r.Name.StartsWith(root + "::", StringComparison.Ordinal))] });
        if (roots.Count == 2)
            settings.Profiles.Add(new() { Name = "日本語 + 漢字", DeckRules = settings.Profiles.SelectMany(p => p.DeckRules).ToList() });
        var previous = metadata.Decks.Where(d => Includes(settings.DeckRules, d.Name)).Select(d => d.Name).ToHashSet();
        var matching = settings.Profiles.Find(p => previous.SetEquals(metadata.Decks.Where(d => p.Includes(d.Name)).Select(d => d.Name)));
        if (matching is null && (previous.Count > 0 || settings.Profiles.Count == 0))
        {
            matching = new() { Name = "Saved selection", DeckRules = [.. settings.DeckRules] };
            settings.Profiles.Insert(0, matching);
        }
        settings.ActiveProfileId = (matching ?? settings.Profiles[0]).Id;
    }

    public static void Validate(Settings settings, CollectionMetadata metadata)
    {
        if (settings.Profiles.Count == 0 || !settings.Profiles.Any(p => p.Enabled))
            throw new InvalidDataException("Enable at least one deck profile.");
        if (settings.Profiles.Any(p => string.IsNullOrWhiteSpace(p.Name)) || settings.Profiles.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Profiles.Count)
            throw new InvalidDataException("Give each deck profile a unique, nonempty name.");
        if (settings.Profiles.Select(p => p.Id).Distinct().Count() != settings.Profiles.Count)
            throw new InvalidDataException("Deck profile identifiers must be unique.");
        foreach (var profile in settings.Profiles)
            if (!metadata.Decks.Any(d => profile.Includes(d.Name)))
                throw new InvalidDataException($"Include at least one available deck in profile '{profile.Name}'.");
    }

    public static ImportedCollection Select(ImportedCollection source, DeckProfile profile)
    {
        var cards = source.Cards.Where(c => profile.Includes(c.Deck)).ToList();
        var cardIds = cards.Select(c => c.Id).ToHashSet();
        var noteIds = cards.Select(c => c.NoteId).ToHashSet();
        var reviews = source.Reviews.Where(r => cardIds.Contains(r.CardId)).ToList();
        return new()
        {
            CollectionKey = source.CollectionKey, Signature = source.Signature, Metadata = source.Metadata,
            SourceProfileFolder = source.SourceProfileFolder, IsSynthetic = source.IsSynthetic,
            TimeZoneId = source.TimeZoneId, RefreshedAt = source.RefreshedAt,
            Scope = profile.Name + " · " + string.Join(", ", profile.DeckRules.Select(r => (r.Include ? "+ " : "− ") + r.Name)),
            Cards = cards, Notes = source.Notes.Where(n => noteIds.Contains(n.Id)).ToList(), Reviews = reviews,
            UnresolvedReviews = source.UnresolvedReviews, UnresolvedReviewsAvailable = source.UnresolvedReviewsAvailable, ImportMethod = source.ImportMethod,
            UnknownReviewKinds = reviews.Count(r => r.Kind is < 0 or > 5)
        };
    }
}
