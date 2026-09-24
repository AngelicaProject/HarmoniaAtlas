using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Tests;

public sealed class HxsFoundationTests
{
    [Fact]
    public void CurrentFormatVersionIsTwo()
    {
        Assert.Equal(2, HxsFormatVersion.Current);
        Assert.Equal(HxsFormatVersion.Current, HxsConstants.InitialUserVersion);
        Assert.NotEqual(AtlasApplicationVersion.Current, HxsFormatVersion.Current.ToString());
    }

    [Fact]
    public void SqliteIdentityAndIntegrityRoundTrip()
    {
        string path = CreateTemporaryPath();
        try
        {
            using HxsDatabase database = HxsDatabase.CreateNew(path);
            database.SetHxsIdentity();

            using var transaction = database.BeginTransaction();
            transaction.Commit();

            Assert.Equal(HxsConstants.ApplicationId, database.ApplicationId);
            Assert.Equal(HxsFormatVersion.Current, database.UserVersion);
            Assert.True(database.RunIntegrityCheck());
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public void WrongApplicationIdIsRejected()
    {
        string path = CreateTemporaryPath();
        try
        {
            using (HxsDatabase database = HxsDatabase.CreateNew(path))
            {
                database.SetHxsIdentity();
                database.SetApplicationId(HxsConstants.ApplicationId + 1);
            }

            Assert.Throws<HxsFormatException>(() => HxsReader.Open(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public void UnsupportedFormatVersionIsRejected()
    {
        string path = CreateTemporaryPath();
        try
        {
            using (HxsDatabase database = HxsDatabase.CreateNew(path))
            {
                database.SetHxsIdentity();
                database.SetUserVersion(HxsFormatVersion.Current + 1);
            }

            Assert.Throws<HxsFormatException>(() => HxsVerifier.Verify(path));
        }
        finally
        {
            DeleteIfPresent(path);
        }
    }

    [Fact]
    public void EmptyWriterCompletesThroughPartialFile()
    {
        string path = CreateTemporaryPath();
        try
        {
            new HxsWriter().WriteEmpty(path);

            HxsVerificationResult result = HxsVerifier.Verify(path);
            Assert.Equal(HxsConstants.ApplicationId, result.ApplicationId);
            Assert.Equal(HxsFormatVersion.Current, result.FormatVersion);
            Assert.True(result.IntegrityCheckPassed);
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            DeleteIfPresent(path);
            DeleteIfPresent(path + ".partial");
        }
    }

    private static string CreateTemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"harmonia-atlas-{Guid.NewGuid():N}.hxs");

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
