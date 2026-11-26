using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrameCore.Core;

public static class Json
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);

    /// <summary>Compact form without nulls; used for canonical comparisons and the pipe protocol.</summary>
    public static readonly JsonSerializerOptions Canonical = CreateCanonical();

    private static JsonSerializerOptions Create(bool indented)
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = indented,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    private static JsonSerializerOptions CreateCanonical()
    {
        var o = Create(indented: false);
        o.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return o;
    }

    public static T? Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);

    /// <summary>Atomic write: temp file + replace, so a crash never leaves a truncated journal.</summary>
    public static void WriteAtomic<T>(string path, T value)
    {
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}
