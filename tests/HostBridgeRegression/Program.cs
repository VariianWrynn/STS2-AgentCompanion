using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using Sts2LlmCoop.HostTest;

static void Check(bool condition, string behavior)
{
    if (!condition) throw new InvalidOperationException(behavior);
    Console.WriteLine($"PASS {behavior}");
}

var timeout = TimeSpan.FromSeconds(5);
var calls = 0;
using (var mailbox = new HostObservationMailbox(() => ++calls, capacity: 32))
{
    var requests = Enumerable.Range(0, 23).Select(_ => mailbox.ObserveAsync(timeout)).ToArray();
    Check(calls == 0 && requests.All(r => !r.IsCompleted), "enqueue does not read native state");
    mailbox.Drain();
    Check(calls == 10, "one frame reads at most ten observations");
    mailbox.Drain();
    mailbox.Drain();
    var replies = await Task.WhenAll(requests);
    Check(calls == 23 && replies.All(r => r.Status == "ok"), "all 23 requests complete across frame budgets");
    Check(replies.Select(r => r.Observation).Distinct().Count() == 23, "eleventh request is retained and read once");
}

using (var mailbox = new HostObservationMailbox(() => ++calls, capacity: 1))
{
    var first = mailbox.ObserveAsync(timeout);
    Check((await mailbox.ObserveAsync(timeout)).Status == "busy", "queue overflow returns busy");
    mailbox.Drain();
    Check((await first).Status == "ok", "overflow preserves the admitted request");
}

long clock = Stopwatch.GetTimestamp();
calls = 0;
using (var mailbox = new HostObservationMailbox(() => ++calls, clock: () => clock))
{
    var request = mailbox.ObserveAsync(timeout);
    clock += Stopwatch.Frequency * 6;
    mailbox.Drain();
    Check((await request).Status == "expired" && calls == 0, "expired work never captures on a later frame");
}

using (var mailbox = new HostObservationMailbox(() => ++calls))
{
    var reply = await mailbox.ObserveAsync(TimeSpan.FromMilliseconds(30));
    mailbox.Drain();
    Check(reply.Status == "expired" && calls == 0, "unavailable frames time out and cannot later read");
}

using (var mailbox = new HostObservationMailbox(() => throw new InvalidOperationException("private detail")))
{
    var request = mailbox.ObserveAsync(timeout);
    mailbox.Drain();
    Check((await request).Status == "capture_failed", "capture exceptions complete requests without leaking details");
}

using (var mailbox = new HostObservationMailbox(() => 0))
{
    var request = mailbox.ObserveAsync(timeout);
    mailbox.Dispose();
    Check((await request).Status == "shutdown" && (await mailbox.ObserveAsync(timeout)).Status == "shutdown",
        "shutdown completes queued waiters and rejects future work");
}

using (var entered = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
using (var mailbox = new HostObservationMailbox(() => { entered.Set(); release.Wait(); return 1; }))
{
    var request = mailbox.ObserveAsync(timeout);
    var drain = Task.Run(() => mailbox.Drain());
    Check(entered.Wait(TimeSpan.FromSeconds(2)), "capture can be claimed before shutdown");
    mailbox.Dispose();
    var reply = await request.WaitAsync(TimeSpan.FromSeconds(2));
    release.Set();
    await drain;
    Check(reply.Status == "shutdown", "shutdown completes a claimed waiter even while capture is blocked");
}

using (var entered = new ManualResetEventSlim())
using (var release = new ManualResetEventSlim())
using (var mailbox = new HostObservationMailbox(() => { entered.Set(); release.Wait(); return 1; }))
{
    var request = mailbox.ObserveAsync(TimeSpan.FromMilliseconds(200));
    var drain = Task.Run(() => mailbox.Drain());
    Check(entered.Wait(TimeSpan.FromSeconds(2)), "capture claims before its caller deadline");
    var reply = await request.WaitAsync(TimeSpan.FromSeconds(2));
    release.Set();
    await drain;
    Check(reply.Status == "timeout", "claimed reads retain a bounded caller wait");
}

var valid = new HostIdentityFacts(true, 1, 1, 1, 1, 2, 1, 1, 1, true, true, false, true);
Check(HostIdentityPolicy.Allows(valid), "authoritative host with exactly one AI is admitted");
HostIdentityFacts[] invalid =
[
    valid with { ConnectedLoopbackHost = false }, valid with { Host = 0 }, valid with { Local = null },
    valid with { Local = 2 }, valid with { ResolvedLocal = 2 }, valid with { Sender = 2 },
    valid with { ParticipantCount = 3 }, valid with { HostCount = 2 }, valid with { AiCount = 0 },
    valid with { ControllerCount = 2 }, valid with { HostParticipantMatches = false },
    valid with { AiParticipantMatches = false }, valid with { HostHasController = true },
    valid with { RunPlayersMatch = false }
];
Check(invalid.All(f => !HostIdentityPolicy.Allows(f)), "every identity inconsistency denies host scope");
Check(HostBridgeConfig.FromArguments(["game.exe"]) is null, "ordinary launch leaves the bridge disabled");
try
{
    HostBridgeConfig.FromArguments([HostBridgeConfig.ArgumentPrefix + "relative.json"]);
    throw new Exception("Relative configuration accepted.");
}
catch (InvalidDataException) { Console.WriteLine("PASS relative configuration rejected"); }
var reservation = new TcpListener(IPAddress.Loopback, 0);
reservation.Start();
var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
reservation.Stop();
var config = new HostBridgeConfig(port, new string('A', 48), new string('B', 64));
var processView = new HostProcessIdentity(1234, DateTime.UnixEpoch, "fake.exe", "fake.dll", new string('B', 64), "v0.113.1");
var playerView = new HostPlayerObservation(1, "Play", false, 1, 80, 80, 99, 3, 5);
var fakeObservation = new HostObservation("observe_host", "runtime_unverified", processView, "host",
    "CombatRoom", 1, playerView, playerView with { NetId = 10002 });
var httpCaptures = 0;
using (var mailbox = new HostObservationMailbox(() => { httpCaptures++; return fakeObservation; }))
using (var transport = new HostTestTransport(config, mailbox))
using (var client = new HttpClient(new HttpClientHandler { UseProxy = false })
       { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) })
{
    using (var denied = await client.GetAsync("/v1/host/observation"))
        Check(denied.StatusCode == HttpStatusCode.Unauthorized, "HTTP rejects missing authorization");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('C', 48));
    using (var denied = await client.GetAsync("/v1/host/observation"))
        Check(denied.StatusCode == HttpStatusCode.Unauthorized, "HTTP rejects the wrong token");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/v1/host/observation"))
    {
        request.Headers.Add("Origin", "http://example.invalid");
        using var denied = await client.SendAsync(request);
        Check(denied.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects browser Origin headers");
    }
    using (var denied = await client.PostAsync("/v1/host/observation", new StringContent("{}")))
        Check(denied.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects POST");
    using (var denied = await client.GetAsync("/v1/host/observation?actor=10002"))
        Check(denied.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects query parameters including actor selection");
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/v1/host/observation") { Content = new StringContent("{}") })
    using (var denied = await client.SendAsync(request))
        Check(denied.StatusCode == HttpStatusCode.BadRequest, "HTTP rejects GET bodies");
    Check(httpCaptures == 0, "invalid HTTP requests never capture state");

    var pending = client.GetAsync("/v1/host/observation");
    var frameDeadline = Stopwatch.StartNew();
    while (!pending.IsCompleted && frameDeadline.Elapsed < TimeSpan.FromSeconds(4))
    {
        mailbox.Drain();
        await Task.Delay(5);
    }
    using (var response = await pending)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var observation = root.GetProperty("Observation");
        var expected = JsonSerializer.Serialize(fakeObservation);
        Check(response.StatusCode == HttpStatusCode.OK && root.GetProperty("Status").GetString() == "ok" &&
              observation.GetRawText() == expected && observation.GetProperty("Host").GetProperty("NetId").GetUInt64() == 1,
            "HTTP returns the exact detached PascalCase observation contract");
        Check(response.Headers.CacheControl?.NoStore == true, "HTTP observation disables caching");
    }
    Check(httpCaptures == 1, "valid HTTP request captures once through the frame mailbox");

    using (var response = await client.GetAsync("/v1/host/observation"))
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(response.StatusCode == HttpStatusCode.GatewayTimeout &&
              document.RootElement.GetProperty("Status").GetString() == "expired",
            "HTTP returns 504 when frames are unavailable");
    }
    mailbox.Drain();
    Check(httpCaptures == 1, "HTTP timeout cannot trigger a late capture");

    async Task<string> RawAsync(string text, bool fragmented = false, bool allowReset = false)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync(IPAddress.Loopback, port);
        var stream = socket.GetStream();
        var bytes = Encoding.ASCII.GetBytes(text);
        if (fragmented)
        {
            await stream.WriteAsync(bytes.AsMemory(0, bytes.Length / 2));
            await Task.Delay(10);
            await stream.WriteAsync(bytes.AsMemory(bytes.Length / 2));
        }
        else await stream.WriteAsync(bytes);
        using var reader = new StreamReader(stream);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        try { return await reader.ReadToEndAsync(deadline.Token); }
        catch (IOException exception) when (allowReset && exception.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        { return "reset"; }
    }

    var auth = $"Authorization: Bearer {config.Token}\r\n";
    var duplicate = await RawAsync("GET /v1/host/observation HTTP/1.1\r\n" + auth + auth + "\r\n");
    Check(duplicate.StartsWith("HTTP/1.1 400"), "HTTP rejects duplicate authorization headers");
    var duplicateLength = await RawAsync("GET /v1/host/observation HTTP/1.1\r\n" + auth +
        "Content-Length: 0\r\nContent-Length: 0\r\n\r\n");
    Check(duplicateLength.StartsWith("HTTP/1.1 400"), "HTTP rejects duplicate length headers");
    var chunked = await RawAsync("GET /v1/host/observation HTTP/1.1\r\n" + auth + "Transfer-Encoding: chunked\r\n\r\n");
    Check(chunked.StartsWith("HTTP/1.1 400"), "HTTP rejects transfer encoding");
    var absolute = await RawAsync("GET http://127.0.0.1/v1/host/observation HTTP/1.1\r\n" + auth + "\r\n");
    Check(absolute.StartsWith("HTTP/1.1 400"), "HTTP rejects absolute URL request targets");
    var oversized = await RawAsync("GET /v1/host/observation HTTP/1.1\r\nX: " + new string('A', 8200), allowReset: true);
    Check((oversized.StartsWith("HTTP/1.1 400") || oversized == "reset") && httpCaptures == 1,
        "HTTP rejects headers beyond 8192 bytes without capture");
    var fragmentedRequest = RawAsync("GET /v1/host/observation HTTP/1.1\r\n" + auth + "\r\n", fragmented: true);
    frameDeadline.Restart();
    while (!fragmentedRequest.IsCompleted && frameDeadline.Elapsed < TimeSpan.FromSeconds(4))
    {
        mailbox.Drain();
        await Task.Delay(5);
    }
    var fragmentedResponse = await fragmentedRequest;
    Check(fragmentedResponse.StartsWith("HTTP/1.1 200") && httpCaptures == 2,
        "HTTP accepts fragmented headers and captures exactly once");
    using (var slow = new TcpClient())
    {
        await slow.ConnectAsync(IPAddress.Loopback, port);
        await slow.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /v1/host/observation HTTP/1.1\r\n"));
        using var slowDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var closed = false;
        try { closed = await slow.GetStream().ReadAsync(new byte[1], slowDeadline.Token) == 0; }
        catch (IOException) { closed = true; }
        Check(closed && httpCaptures == 2, "HTTP closes stalled headers within the read deadline without capture");
    }
}
Console.WriteLine("Host bridge regression passed. Only a test-owned loopback listener was opened. No game or profile was opened.");
