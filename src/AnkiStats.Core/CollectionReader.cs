namespace AnkiStats.Core;

// Keep file import available without Anki or its add-on. A running Anki commonly
// holds an exclusive SQLite lock; only Anki itself can read that connection.
public sealed class CollectionReader(string temporaryRoot, Func<AnkiConnectClient> clientFactory, bool allowLive = true)
{
    public Task<CollectionMetadata> Discover(string profileFolder) => Read(
        () => new CollectionImporter(temporaryRoot).Discover(profileFolder),
        client => new LiveCollectionImporter(client).Discover(profileFolder));

    public Task<ImportedCollection> Import(Settings settings, IProgress<string>? progress = null) => Read(
        () => new CollectionImporter(temporaryRoot).Import(settings, progress),
        client => new LiveCollectionImporter(client).Import(settings, progress));

    public Task<CollectionRefresh> Refresh(Settings settings, ImportedCollection? previous, bool includeMedia,
        IProgress<string>? progress = null) => Read(
        () => new CollectionImporter(temporaryRoot).WithPrivateCopy(settings.ProfileFolder, db =>
        {
            var metadata = CollectionImporter.ReadMetadata(db);
            var stats = CollectionImporter.ReadCollection(db, settings, progress, metadata);
            var media = includeMedia ? CollectionImporter.ReadCollection(db, MediaAnalysis.CollectionSettings(settings, metadata),
                metadata: metadata, includeReviews: false) : null;
            return new CollectionRefresh(stats, media);
        }),
        client => new LiveCollectionImporter(client).Refresh(settings, previous, includeMedia, progress));

    public Task<ImportedCollection> ImportMedia(Settings settings) => Read(
        () => new CollectionImporter(temporaryRoot).WithPrivateCopy(settings.ProfileFolder, db =>
        {
            var metadata = CollectionImporter.ReadMetadata(db);
            return CollectionImporter.ReadCollection(db, MediaAnalysis.CollectionSettings(settings, metadata),
                metadata: metadata, includeReviews: false);
        }),
        client => new LiveCollectionImporter(client).ImportMedia(settings));

    private async Task<T> Read<T>(Func<T> fileRead, Func<AnkiConnectClient, Task<T>> liveRead)
    {
        try { return await Task.Run(fileRead); }
        catch (IOException) when (allowLive)
        {
            using var client = clientFactory();
            return await liveRead(client);
        }
    }
}
