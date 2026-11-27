using FrameCore.Core.Localization;
using Xunit;

namespace FrameCore.Tests;

/// <summary>Seen on Windows CI: two parallel loads corrupted the shared translation tables.</summary>
public class LocalizationConcurrencyTests
{
    [Fact]
    public void Translations_can_be_loaded_and_read_from_many_threads_at_once()
    {
        var dir = Path.Combine(Repo.Root, "lang");
        var errors = 0;
        Parallel.For(0, 64, i =>
        {
            try
            {
                if (i % 2 == 0) Loc.Instance.LoadDirectory(dir);
                else for (var k = 0; k < 200; k++) _ = Loc.T("common.apply");
            }
            catch { Interlocked.Increment(ref errors); }
        });
        Assert.Equal(0, errors);
        Assert.NotEqual("common.apply", Loc.T("common.apply"));
    }
}
