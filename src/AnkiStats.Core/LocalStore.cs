using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AnkiStats.Core;

public sealed class LocalStore
{
    public string Root { get; }
    public bool IsIsolated { get; }
    public string TemporaryRoot => Path.Combine(Root, "temporary");
    public LocalStore(string? root = null)
    {
        var overrideRoot = Environment.GetEnvironmentVariable("ANKISTATS_DATA_DIR");
        IsIsolated = root is not null || !string.IsNullOrWhiteSpace(overrideRoot);
        Root = root ?? (string.IsNullOrWhiteSpace(overrideRoot) ? null : overrideRoot) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnkiStats");
        Directory.CreateDirectory(Root);
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(Root, "analysis.sqlite"), Pooling = false }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS cache (id INTEGER PRIMARY KEY CHECK(id=1), payload BLOB NOT NULL); CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS insight_history (collection TEXT NOT NULL, day TEXT NOT NULL, template TEXT NOT NULL, PRIMARY KEY(collection,day));";
        cmd.ExecuteNonQuery(); return db;
    }
    public Settings? LoadSettings()
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM settings WHERE id=1";
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<Settings>(json) : null;
    }
    public void SaveSettings(Settings settings)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settings)); cmd.ExecuteNonQuery();
    }
    public ImportedCollection? LoadCache(string collectionKey)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT payload FROM cache WHERE id=1";
        if (cmd.ExecuteScalar() is not byte[] bytes) return null;
        using var compressed = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        var data = JsonSerializer.Deserialize<ImportedCollection>(compressed);
        return data?.CollectionKey == collectionKey ? data : null;
    }
    public void SaveCache(ImportedCollection collection, Settings? settings = null)
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.Fastest, true)) JsonSerializer.Serialize(gzip, collection);
        using var db = Open(); using var tx = db.BeginTransaction(); using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO cache(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$payload", bytes.ToArray()); cmd.ExecuteNonQuery();
        if (settings is not null)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT INTO settings(id,payload) VALUES(1,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(settings)); cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public Insight SelectInsight(string collection, DateOnly day, IReadOnlyList<Insight> candidates)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT day,template FROM insight_history WHERE collection=$collection AND day >= $start ORDER BY day";
        cmd.Parameters.AddWithValue("$collection", collection); cmd.Parameters.AddWithValue("$start", day.AddDays(-7).ToString("yyyy-MM-dd"));
        var history = new Dictionary<string, string>();
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) history[reader.GetString(0)] = reader.GetString(1);
        var key = day.ToString("yyyy-MM-dd");
        if (history.TryGetValue(key, out var existing)) return candidates.FirstOrDefault(c => c.Id == existing) ?? new Insight("none", "Today's insight needs more data", "The selected filters do not support today's template. Try a wider date range.");
        var recent = history.Where(p => string.CompareOrdinal(p.Key, key) < 0).Select(p => p.Value).ToHashSet();
        var eligible = candidates.Where(c => !recent.Contains(c.Id)).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        if (eligible.Count == 0) return new("none", "More observations are on the way", "No supported template is available outside the seven-day repeat window. Your overview and review tables are still available.");
        // Date-based selection is stable and cannot accidentally repeat a recent template.
        var selected = eligible[day.DayNumber % eligible.Count];
        cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO insight_history(collection,day,template) VALUES($collection,$day,$template) ON CONFLICT(collection,day) DO NOTHING";
        cmd.Parameters.AddWithValue("$collection", collection); cmd.Parameters.AddWithValue("$day", key); cmd.Parameters.AddWithValue("$template", selected.Id); cmd.ExecuteNonQuery();
        return selected;
    }

    // Separate personal decisions from replaceable dictionary data and import caches.
    public List<T> LoadCoverageDecisions<T>(string collection, string kind)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS coverage_decisions (collection TEXT NOT NULL, kind TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(collection,kind));";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT payload FROM coverage_decisions WHERE collection=$collection AND kind=$kind";
        cmd.Parameters.AddWithValue("$collection", collection); cmd.Parameters.AddWithValue("$kind", kind);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<List<T>>(json) ?? [] : [];
    }
    public void SaveCoverageDecisions<T>(string collection, string kind, List<T> decisions)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS coverage_decisions (collection TEXT NOT NULL, kind TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(collection,kind));";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "INSERT INTO coverage_decisions(collection,kind,payload) VALUES($collection,$kind,$payload) ON CONFLICT(collection,kind) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$collection", collection); cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(decisions)); cmd.ExecuteNonQuery();
    }
}
