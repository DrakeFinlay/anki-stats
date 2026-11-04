using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AnkiStats.Core;

/// <summary>Only the loopback Anki instance. No collection-file access and no automatic retries.</summary>
public sealed class AnkiConnectClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string? key;
    public AnkiConnectClient(string? apiKey = null, HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) : new HttpClient(handler);
        http.BaseAddress = new Uri("http://127.0.0.1:8765/"); http.Timeout = TimeSpan.FromSeconds(90);
        key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }
    public async Task<T> Call<T>(string action, object? parameters = null)
    {
        try
        {
            // AnkiConnect's HTTP parser requires Content-Length and rejects chunked bodies.
            // Buffer UTF-8 JSON so HttpClient knows its byte length before sending headers.
            using var content = new StringContent(JsonSerializer.Serialize(new { action, version = 6, @params = parameters ?? new { }, key }), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("", content);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = document.RootElement;
            if (!root.TryGetProperty("error", out var error) || !root.TryGetProperty("result", out var result)) throw new InvalidDataException("AnkiConnect returned an invalid response.");
            if (error.ValueKind != JsonValueKind.Null) throw new InvalidOperationException("AnkiConnect: " + error.ToString());
            return result.Deserialize<T>()!;
        }
        catch (TaskCanceledException e)
        {
            throw new IOException($"AnkiConnect timed out during {action}. Check that Anki is responsive. If creation was in progress, check pending additions before retrying it.", e);
        }
        catch (HttpRequestException e) when (e.StatusCode is not null)
        {
            throw new IOException($"AnkiConnect rejected {action} with HTTP {(int)e.StatusCode.Value} ({e.StatusCode.Value}). Check add-on compatibility and configuration. If creation was in progress, check pending additions before retrying it.", e);
        }
        catch (HttpRequestException e)
        {
            throw new IOException($"Could not reach AnkiConnect at 127.0.0.1:8765 during {action}. Keep Anki open on the selected profile with AnkiConnect enabled. Details: {e.Message} If creation was in progress, check pending additions before retrying it.", e);
        }
    }
    public void Dispose() => http.Dispose();
}

public sealed record LiveAnkiNote(long Id, string Model, Dictionary<string, string> Fields, List<string> Tags);
public sealed record AnkiInventory(List<LiveAnkiNote> Notes, List<string> ModelFields);

public static class AnkiConnection
{
    public static async Task Verify(AnkiConnectClient client, string profile, long modelId)
    {
        if (await client.Call<int>("version") < 6) throw new InvalidOperationException("AnkiConnect API version 6 or later is required.");
        await VerifyProfile(client, profile);
        var models = await client.Call<Dictionary<string, long>>("modelNamesAndIds");
        if (!models.TryGetValue("Japanese", out var id) || id != modelId) throw new InvalidOperationException("The running Anki note type does not match the imported Japanese note type. Refresh Anki Stats with the correct profile selected in Anki.");
        if (!(await client.Call<List<string>>("deckNames")).Contains(NoteCreation.Destination)) throw new InvalidOperationException("The destination deck 日本語::勉強してる does not exist in the running profile.");
    }
    public static async Task VerifyProfile(AnkiConnectClient client, string profile)
    {
        if (await client.Call<string>("getActiveProfile") != profile) throw new InvalidOperationException("The running Anki profile differs from the imported profile. Switch Anki to the correct profile.");
    }
    public static async Task<AnkiInventory> ReadInventory(AnkiConnectClient client, string profile, long modelId, IProgress<string>? progress = null)
    {
        await Verify(client, profile, modelId);
        var fields = await client.Call<List<string>>("modelFieldNames", new { modelName = "Japanese" });
        if (!fields.Contains("Front") || !fields.Contains("English")) throw new InvalidOperationException("Japanese must contain Front and English fields.");
        var ids = await client.Call<List<long>>("findNotes", new { query = "" });
        var notes = new List<LiveAnkiNote>();
        foreach (var batch in ids.Chunk(500))
        {
            progress?.Report($"Checking live Anki vocabulary for duplicates… {notes.Count:N0} / {ids.Count:N0} notes");
            var info = await client.Call<JsonElement[]>("notesInfo", new { notes = batch });
            if (info.Length != batch.Length) throw new InvalidDataException("Anki changed during the duplicate check. Prepare the batch again.");
            foreach (var item in info)
            {
                var values = item.GetProperty("fields").EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetProperty("value").GetString() ?? "");
                notes.Add(new(item.GetProperty("noteId").GetInt64(), item.GetProperty("modelName").GetString()!, values,
                    item.GetProperty("tags").EnumerateArray().Select(t => t.GetString()!).ToList()));
            }
        }
        await VerifyProfile(client, profile);
        return new(notes, fields);
    }
}
