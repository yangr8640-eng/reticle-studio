using System.Buffers.Binary;
using System.Text.Json;

namespace Reticle.Core;

public sealed record OverlayMessage
{
    public int Version { get; init; } = IndependentOverlayProtocol.Version;
    public string Kind { get; init; } = "";
    public string Session { get; init; } = "";
    public long Sequence { get; init; }
    public bool Enabled { get; init; }
    public CrosshairSettings? Settings { get; init; }
    public int ProcessId { get; init; }
    public bool UiAccess { get; init; }
    public long WindowHandle { get; init; }
    public string? Error { get; init; }
}

// A bounded, length-prefixed protocol. Each connection has one reader and one
// serialized writer; disconnects invalidate the entire session.
public static class IndependentOverlayProtocol
{
    public const int Version = 1, MaxFrameBytes = 32 * 1024;
    public const int HeartbeatIntervalSeconds = 2, SilenceTimeoutSeconds = 10, ConnectTimeoutSeconds = 30;
    private static readonly JsonSerializerOptions JsonOptions = new() { MaxDepth = 16 };

    public static CrosshairSettings Snapshot(CrosshairSettings settings)
    {
        var copy = new CrosshairSettings();
        copy.Apply(settings);
        return copy;
    }

    public static async ValueTask<OverlayMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxFrameBytes) throw new InvalidDataException("Invalid overlay frame length.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        OverlayMessage? message;
        try { message = JsonSerializer.Deserialize<OverlayMessage>(payload, JsonOptions); }
        catch (JsonException error) { throw new InvalidDataException("Invalid overlay message.", error); }
        if (message is null) throw new InvalidDataException("Missing overlay message.");
        Validate(message);
        return message;
    }

    public static async ValueTask WriteAsync(Stream stream, OverlayMessage message, CancellationToken cancellationToken = default)
    {
        Validate(message);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (payload.Length > MaxFrameBytes) throw new InvalidDataException("Overlay message is too large.");
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(OverlayMessage message)
    {
        if (message.Version != Version || message.Kind is not ("hello" or "state" or "applied" or "stop" or "error"))
            throw new InvalidDataException("Unsupported overlay protocol.");
        if (message.Session is not { Length: 32 } || !message.Session.All(Uri.IsHexDigit) || message.Sequence < 0)
            throw new InvalidDataException("Invalid overlay session.");
        if (message.Kind is "state" && (message.Sequence == 0 || message.Settings is null))
            throw new InvalidDataException("Overlay state is incomplete.");
        if (message.Kind is "applied" && message.Sequence == 0)
            throw new InvalidDataException("Overlay acknowledgement is incomplete.");
        if (message.Settings is { } settings && (settings.Monitor.Length > 256 || !Enum.IsDefined(settings.Shape) || !CrosshairSettings.IsColor(settings.Color)))
            throw new InvalidDataException("Invalid overlay settings.");
        if (message.Error?.Length > 1024) throw new InvalidDataException("Overlay error is too long.");
    }
}
