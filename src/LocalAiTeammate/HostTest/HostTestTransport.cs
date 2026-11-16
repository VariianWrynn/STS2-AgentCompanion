using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2LlmCoop.HostTest;

internal sealed record HostBridgeConfig(int Port, string Token, string ExpectedAssemblySha256)
{
    internal const string ArgumentPrefix = "--local-ai-host-bridge-config=";

    internal static HostBridgeConfig? FromArguments(string[] arguments)
    {
        var paths = arguments.Where(a => a.StartsWith(ArgumentPrefix, StringComparison.Ordinal)).ToArray();
        if (paths.Length == 0) return null;
        if (paths.Length != 1) throw new InvalidDataException("Duplicate bridge configuration argument.");
        var path = paths[0][ArgumentPrefix.Length..];
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || new FileInfo(path).Length > 4096)
            throw new InvalidDataException("Bridge configuration must be a small absolute local file.");
        var config = JsonSerializer.Deserialize<HostBridgeConfig>(File.ReadAllText(path), new JsonSerializerOptions
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Missing bridge configuration.");
        if (config.Port is < 1024 or > 65535 || config.Token is null || config.Token.Length is < 32 or > 128 ||
            config.Token.Any(c => !char.IsAsciiLetterOrDigit(c)) || config.ExpectedAssemblySha256 is null ||
            config.ExpectedAssemblySha256.Length != 64 || !config.ExpectedAssemblySha256.All(char.IsAsciiHexDigit))
            throw new InvalidDataException("Invalid bridge configuration.");
        return config;
    }
}

internal sealed class HostTestTransport : IDisposable
{
    private readonly TcpListener _listener;
    private readonly HostObservationMailbox _mailbox;
    private readonly HostSubmissionMailbox? _submissions;
    private readonly byte[] _token;
    private readonly SemaphoreSlim _handlers = new(8);
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _acceptLoop;
    private int _disposed;

    internal HostTestTransport(HostBridgeConfig config, HostObservationMailbox mailbox, HostSubmissionMailbox? submissions = null)
    {
        _mailbox = mailbox;
        _submissions = submissions;
        _token = Encoding.ASCII.GetBytes("Bearer " + config.Token);
        _listener = new TcpListener(IPAddress.Loopback, config.Port);
        _listener.Start(16);
        _acceptLoop = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                if (!_handlers.Wait(0)) { client.Dispose(); continue; }
                _clients.TryAdd(client, 0);
                _ = HandleAsync(client);
            }
        }
        catch (Exception) when (_shutdown.IsCancellationRequested) { }
        catch { Dispose(); }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();
            if (client.Client.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))
            {
                await RespondAsync(stream, 403, null, deadline.Token).ConfigureAwait(false);
                return;
            }
            var header = await ReadHeaderAsync(stream, deadline.Token).ConfigureAwait(false);
            var lines = header.Split("\r\n", StringSplitOptions.None);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 1; index < lines.Length; index++)
            {
                if (lines[index].Length == 0) break;
                var separator = lines[index].IndexOf(':');
                if (separator <= 0 || lines[index][..separator].Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') ||
                    !headers.TryAdd(lines[index][..separator], lines[index][(separator + 1)..].Trim()))
                    throw new InvalidDataException("Invalid request header.");
            }
            if (!headers.TryGetValue("Authorization", out var authorization) || authorization.Length != _token.Length ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(authorization), _token))
            {
                await RespondAsync(stream, 401, null, deadline.Token).ConfigureAwait(false);
                return;
            }
            var observe = lines[0] == "GET /v1/host/observation HTTP/1.1";
            var submit = lines[0] == "POST /v1/host/submit HTTP/1.1" && _submissions is not null;
            if ((!observe && !submit) || headers.ContainsKey("Origin") ||
                headers.ContainsKey("Transfer-Encoding") ||
                (headers.TryGetValue("Content-Length", out var length) && length != "0"))
            {
                await RespondAsync(stream, 400, null, deadline.Token).ConfigureAwait(false);
                return;
            }
            var commandHeaders = headers.Keys.Where(k => k.StartsWith("X-Host-", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (submit)
            {
                if (commandHeaders.Length != 3 || !headers.TryGetValue("X-Host-Sequence", out var sequenceText) ||
                    !long.TryParse(sequenceText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sequence) ||
                    sequence <= 0 || sequence == long.MaxValue || sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) != sequenceText ||
                    !headers.TryGetValue("X-Host-Observation", out var observation) || !HostSubmissionMailbox.Token(observation) ||
                    !headers.TryGetValue("X-Host-Choice", out var choice) || !HostSubmissionMailbox.Token(choice) ||
                    headers.Keys.Any(k => k.Contains("actor", StringComparison.OrdinalIgnoreCase)))
                {
                    await RespondAsync(stream, 400, null, deadline.Token).ConfigureAwait(false);
                    return;
                }
                var receipt = await _submissions!.SubmitAsync(new HostSubmission(sequence, observation, choice), TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await RespondAsync(stream, 200, receipt, deadline.Token).ConfigureAwait(false);
                return;
            }
            if (commandHeaders.Length != 0)
            {
                await RespondAsync(stream, 400, null, deadline.Token).ConfigureAwait(false);
                return;
            }
            var reply = await _mailbox.ObserveAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            var status = reply.Status switch { "ok" => 200, "expired" or "timeout" => 504, _ => 503 };
            await RespondAsync(stream, status, reply, deadline.Token).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            try
            {
                using var errorDeadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                errorDeadline.CancelAfter(TimeSpan.FromSeconds(1));
                await RespondAsync(client.GetStream(), 400, null, errorDeadline.Token).ConfigureAwait(false);
            }
            catch { }
        }
        catch { }
        finally
        {
            _clients.TryRemove(client, out _);
            client.Dispose();
            _handlers.Release();
        }
    }

    private static async Task<string> ReadHeaderAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var bytes = new byte[8192];
        // Read only the header. No request body is accepted or allocated.
        for (int count = 0; count < bytes.Length; count++)
        {
            if (await stream.ReadAsync(bytes.AsMemory(count, 1), cancellation).ConfigureAwait(false) == 0)
                throw new InvalidDataException("Truncated request.");
            var value = bytes[count];
            if (value != 13 && value != 10 && (value < 32 || value > 126))
                throw new InvalidDataException("Invalid request byte.");
            if (count >= 3 && bytes[count - 3] == 13 && bytes[count - 2] == 10 && bytes[count - 1] == 13 && value == 10)
                return Encoding.ASCII.GetString(bytes, 0, count + 1);
        }
        throw new InvalidDataException("Request header exceeds limit.");
    }

    private static async Task RespondAsync(NetworkStream stream, int status, object? reply, CancellationToken cancellation)
    {
        var body = reply is null ? [] : JsonSerializer.SerializeToUtf8Bytes(reply);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 504 => "Gateway Timeout", _ => "Service Unavailable" };
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nCache-Control: no-store\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellation).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellation).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _shutdown.Cancel();
        _mailbox.Dispose();
        _submissions?.Dispose();
        _listener.Stop();
        foreach (var client in _clients.Keys) client.Dispose();
    }
}
