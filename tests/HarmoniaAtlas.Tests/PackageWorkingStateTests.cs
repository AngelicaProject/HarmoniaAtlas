using HarmoniaAtlas.Package;

namespace HarmoniaAtlas.Tests;

public sealed class PackageWorkingStateTests
{
    [Fact]
    public void OutputPathsMapToStableDistinctOwnedRoots()
    {
        string firstOutput = Path.Combine(Path.GetTempPath(), "harmonia-atlas-working-state-" + Guid.NewGuid().ToString("N"), "one.hsp");
        string secondOutput = Path.Combine(Path.GetTempPath(), "harmonia-atlas-working-state-" + Guid.NewGuid().ToString("N"), "two.hsp");

        string firstRoot = PackageWorkingState.GetRoot(firstOutput);
        Assert.Equal(firstRoot, PackageWorkingState.GetRoot(firstOutput));
        Assert.NotEqual(firstRoot, PackageWorkingState.GetRoot(secondOutput));
        Assert.NotEqual(Path.GetDirectoryName(Path.GetFullPath(firstOutput)), firstRoot);
    }

    [Fact]
    public void PrepareRemovesOnlyTheOwnedStaleRoot()
    {
        string output = Path.Combine(Path.GetTempPath(), "harmonia-atlas-working-state-" + Guid.NewGuid().ToString("N"), "source.hsp");
        string root = PackageWorkingState.GetRoot(output);
        string? parent = Path.GetDirectoryName(root);
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "stale.txt"), "stale");
            string prepared = PackageWorkingState.Prepare(output);

            Assert.Equal(root, prepared);
            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            Assert.NotNull(parent);
            Assert.True(Directory.Exists(parent));
        }
        finally
        {
            PackageWorkingState.Cleanup(root);
            if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
    }
}
