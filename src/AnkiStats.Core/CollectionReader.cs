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
