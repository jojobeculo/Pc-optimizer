using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace FrameCore.Core.Elevation;

public sealed class PipeRequest
{
    public int Id { get; set; }
    public string Op { get; set; } = "";
    public JsonElement Payload { get; set; }
    /// <summary>SID of the user running the UI (HKCU of that user is targeted by the worker).</summary>
    public string? UserSid { get; set; }
}

public sealed class PipeResponse
{
    public int Id { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public string? ErrorType { get; set; }
    public JsonElement? Payload { get; set; }
}

public sealed class PrivilegedOperationException : Exception
{
    public string? RemoteType { get; }
    public PrivilegedOperationException(string message, string? remoteType = null) : base(message) => RemoteType = remoteType;
}

public sealed class ElevationDeclinedException : Exception
{
    public ElevationDeclinedException() : base(Localization.Loc.T("elevation.declined")) { }
}

/// <summary>Length-prefixed (int32 LE) UTF-8 JSON frames. Frames above 32 MiB are rejected.</summary>
public static class PipeProtocol
{
    public const int MaxFrame = 32 * 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, Json.Canonical);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return default;
        var len = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (len <= 0 || len > MaxFrame) throw new InvalidDataException($"Invalid frame length {len}");
        var buffer = new byte[len];
        if (!await ReadExactAsync(stream, buffer, ct).ConfigureAwait(false)) throw new EndOfStreamException();
        return JsonSerializer.Deserialize<T>(buffer, Json.Canonical);
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return read == 0 ? false : throw new EndOfStreamException();
            read += n;
        }
        return true;
    }

    public static JsonElement ToElement(object? value) =>
        JsonSerializer.SerializeToElement(value, Json.Canonical);

    public static T? FromElement<T>(JsonElement? e) =>
        e is { } el && el.ValueKind != JsonValueKind.Undefined ? el.Deserialize<T>(Json.Canonical) : default;

    public static string Describe(PipeRequest r) => $"#{r.Id} {r.Op} ({Encoding.UTF8.GetByteCount(r.Payload.GetRawText())} B)";
}
