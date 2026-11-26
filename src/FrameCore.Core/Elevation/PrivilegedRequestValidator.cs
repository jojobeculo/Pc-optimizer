using FrameCore.Core.Catalog;
using FrameCore.Core.Engine;
using FrameCore.Core.Models;

namespace FrameCore.Core.Elevation;

/// <summary>
/// Whitelist enforced by the elevated worker. A request is accepted only if:
/// - it passes the <see cref="SafetyPolicy"/>, and
/// - it is byte-for-byte an action of the named catalog tweak (catalog loaded from the install folder), or
/// - it is a dynamic change of an allowed shape (startup entry approval, third-party service start type,
///   non-protected scheduled task, shell extension block list).
/// </summary>
public sealed class PrivilegedRequestValidator
{
    private readonly TweakCatalog _catalog;

    private static readonly string[] StartupApprovedRoots =
    {
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked",
    };

    public PrivilegedRequestValidator(TweakCatalog catalog) => _catalog = catalog;

    public void Validate(PrivilegedActionRequest request)
    {
        var a = request.Action ?? throw new UnauthorizedAccessException("Missing action");
        var violations = SafetyPolicy.Check(a).ToList();
        if (violations.Count > 0) throw new UnauthorizedAccessException(string.Join("; ", violations));

        var origin = request.Context?.Origin ?? "";
        if (origin == "catalog")
        {
            var tweak = _catalog.Find(request.Context!.TweakId ?? "") ?? throw new UnauthorizedAccessException("Unknown tweak");
            var canonical = a.Canonical();
            if (!tweak.Actions.Any(x => x.Canonical() == canonical))
                throw new UnauthorizedAccessException("Action does not belong to the catalog entry");
            return;
        }

        switch (origin)
        {
            case "startup" when a.Type == "registry":
                if (a.Path == null || !StartupApprovedRoots.Any(r => string.Equals(SafetyPolicy.NormalizeRegistryPath(a.Path), r, StringComparison.OrdinalIgnoreCase)))
                    throw new UnauthorizedAccessException("Startup change outside the allowed keys");
                if (!a.Delete && a.ValueKind is not ("Binary" or "String"))
                    throw new UnauthorizedAccessException("Unexpected value kind for startup approval");
                return;
            case "startup" when a.Type == "task":
            case "task" when a.Type == "task":
                if (a.Path == null || SafetyPolicy.IsProtectedTask(a.Path)) throw new UnauthorizedAccessException("Protected task");
                return;
            case "service" when a.Type == "service":
            case "startup" when a.Type == "service":
                if (a.Name == null || SafetyPolicy.IsProtectedService(a.Name)) throw new UnauthorizedAccessException("Protected service");
                if (a.StartType is not ("Automatic" or "AutomaticDelayed" or "Manual" or "Disabled")) throw new UnauthorizedAccessException("Invalid start type");
                return;
            default:
                throw new UnauthorizedAccessException($"Origin '{origin}' / type '{a.Type}' not allowed");
        }
    }
}
