using System.Runtime.CompilerServices;
using FrameCore.Core;

namespace FrameCore.Tests;

internal static class TestIsolation
{
    /// <summary>Unit tests never write into the real %LOCALAPPDATA%\FrameCore (logs, journal, settings).</summary>
    [ModuleInitializer]
    internal static void Init() =>
        AppPaths.OverrideRoot(Path.Combine(Path.GetTempPath(), "framecore-unit-" + Environment.ProcessId));
}
