using System.Globalization;
using System.Text.Json;
using FrameCore.Core.Localization;
using FrameCore.Core.Models;

namespace FrameCore.Core.Catalog;

public sealed record ConditionResult(bool Passed, IReadOnlyList<string> FailedReasons);

/// <summary>Evaluates declarative catalog conditions against <see cref="MachineFacts"/>.</summary>
public static class ConditionEvaluator
{
    public static readonly string[] Operators = { "==", "!=", ">=", "<=", ">", "<", "in", "notIn", "contains", "exists", "notExists", "isTrue", "isFalse" };

    public static ConditionResult Evaluate(IEnumerable<Condition> conditions, MachineFacts facts)
    {
        var reasons = new List<string>();
        foreach (var c in conditions)
        {
            if (!Evaluate(c, facts))
                reasons.Add(c.Reason?.ToString() ?? Loc.T("condition.failed", c.Fact, c.Op, Describe(c.Value)));
        }
        return new ConditionResult(reasons.Count == 0, reasons);
    }

    public static bool Evaluate(Condition c, MachineFacts facts)
    {
        if (c.Any is { Count: > 0 })
            return c.Any.Any(sub => Evaluate(sub, facts));

        var actual = facts[c.Fact];
        switch (c.Op)
        {
            case "exists": return actual != null;
            case "notExists": return actual == null;
            case "isTrue": return facts.Bool(c.Fact);
            case "isFalse": return actual != null && !facts.Bool(c.Fact);
        }

        if (actual == null) return false; // unknown facts never satisfy a comparison
        var expected = c.Value;

        switch (c.Op)
        {
            case "==": return EqualsValue(actual, expected);
            case "!=": return !EqualsValue(actual, expected);
            case ">=": case "<=": case ">": case "<":
                var a = facts.Number(c.Fact);
                var b = ToNumber(expected);
                if (a == null || b == null) return false;
                return c.Op switch { ">=" => a >= b, "<=" => a <= b, ">" => a > b, _ => a < b };
            case "in": return ToList(expected).Any(e => EqualsValue(actual, e));
            case "notIn": return !ToList(expected).Any(e => EqualsValue(actual, e));
            case "contains":
                var needle = ToText(expected) ?? "";
                return actual switch
                {
                    IEnumerable<string> list => list.Any(x => x.Equals(needle, StringComparison.OrdinalIgnoreCase)),
                    _ => (facts.Text(c.Fact) ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase),
                };
            default: throw new InvalidOperationException($"Unknown operator '{c.Op}'");
        }
    }

    private static bool EqualsValue(object actual, JsonElement? expected)
    {
        if (expected is not { } e) return false;
        return e.ValueKind switch
        {
            JsonValueKind.True => actual is true || actual is string s1 && s1.Equals("true", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.False => actual is false || actual is string s2 && s2.Equals("false", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => ToDouble(actual) is { } d && Math.Abs(d - e.GetDouble()) < 1e-9,
            JsonValueKind.String => string.Equals(Convert.ToString(actual, CultureInfo.InvariantCulture), e.GetString(), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool EqualsValue(object actual, JsonElement e) => EqualsValue(actual, (JsonElement?)e);

    private static double? ToDouble(object o) => o switch
    {
        int i => i, long l => l, double d => d, float f => f,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static double? ToNumber(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
        { ValueKind: JsonValueKind.String } s when double.TryParse(s.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static string? ToText(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.String } s => s.GetString(),
        { } x => x.GetRawText(),
        _ => null,
    };

    private static IEnumerable<JsonElement> ToList(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } arr ? arr.EnumerateArray().ToArray() : e is { } single ? new[] { single } : Array.Empty<JsonElement>();

    private static string Describe(JsonElement? e) => e?.GetRawText() ?? "";
}
