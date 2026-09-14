using System.Text;
using HarmoniaAtlas.Game;

namespace HarmoniaAtlas.Tests;

public sealed class GameInstallationTests
{
    [Fact]
    public void SqpackPathIsDerivedFromInstallationRoot()
    {
        string root = NewInstallationRoot();
        try
        {
            GameInstallation installation = GameInstallation.FromPath(root);

            Assert.Equal(Path.Combine(root, "game", "sqpack"), installation.SqpackPath);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void GameVersionReaderReadsVersionFromInstallationRoot()
    {
        string root = NewInstallationRoot();
        try
        {
            File.WriteAllText(
                Path.Combine(root, "game", "ffxivgame.ver"),
                "2026.09.01.0000.0000\r\n",
                Encoding.UTF8);

            Assert.Equal("2026.09.01.0000.0000", new GameVersionReader().Read(GameInstallation.FromPath(root)));
        }
        finally
        {
            Delete(root);
        }
    }

    private static string NewInstallationRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"harmonia-atlas-installation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "game", "sqpack"));
        return root;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
