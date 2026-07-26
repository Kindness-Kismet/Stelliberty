using Stelliberty.Infrastructure.Storage;
using Xunit;

namespace Stelliberty.Infrastructure.Tests;

public sealed class AtomicFileTests
{
    [Fact(DisplayName = "Atomic write creates, replaces and leaves no temp file")]
    public void AtomicWriteCreatesReplacesAndLeavesNoTempFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"stelliberty-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "target.json");

        try
        {
            AtomicFile.WriteAllText(path, "first");
            AtomicFile.WriteAllText(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
