using System.Net;
using System.Net.Sockets;
using System.Text;
using AnkiStats.Core;

internal static class AnkiTransportChecks
{
    // Exercise real HTTP framing: an in-memory handler cannot reveal chunked transfer.
    public static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(deadline.Token);
            using var stream = socket.GetStream();
            var bytes = new List<byte>(); var next = new byte[1];
            while (bytes.Count < 65536)
            {
                await stream.ReadExactlyAsync(next, deadline.Token); bytes.Add(next[0]);
                if (bytes.Count >= 4 && bytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
            }
            var headers = Encoding.ASCII.GetString(bytes.ToArray());
            var lengthLine = headers.Split("\r\n").FirstOrDefault(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
            var length = lengthLine is null ? 0 : int.Parse(lengthLine.Split(':')[1]);
            var body = new byte[length]; await stream.ReadExactlyAsync(body, deadline.Token);
            var valid = length > 0 && !headers.Contains("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase);
            var json = valid ? "{\"result\":6,\"error\":null}" : "";
            var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {(valid ? "200 OK" : "400 Bad Request")}\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");
            await stream.WriteAsync(response, deadline.Token);
            return (valid, Body: Encoding.UTF8.GetString(body));
        }, deadline.Token);
        // Redirect only in this test handler; production remains pinned to port 8765.
        using var client = new AnkiConnectClient(handler: new LocalRedirect(port));
        var version = await client.Call<int>("version", new { text = "食べる" });
        var received = await server;
        check(version == 6 && received.valid, "AnkiConnect transport sends Content-Length without chunked encoding");
        using var parsed = System.Text.Json.JsonDocument.Parse(received.Body);
        check(parsed.RootElement.GetProperty("params").GetProperty("text").GetString() == "食べる", "buffered HTTP JSON preserves Japanese parameters");
        using var rejected = new AnkiConnectClient(handler: new Rejected());
        var specific = false;
        try { await rejected.Call<int>("version"); }
        catch (IOException e) { specific = e.Message.Contains("HTTP 400") && e.Message.Contains("version"); }
        check(specific, "HTTP protocol rejection is distinguished from an unreachable add-on");
    }
    private sealed class LocalRedirect(int port) : DelegatingHandler(new HttpClientHandler { UseProxy = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri($"http://127.0.0.1:{port}/");
            return base.SendAsync(request, cancellationToken);
        }
    }
    private sealed class Rejected : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
    }
}
