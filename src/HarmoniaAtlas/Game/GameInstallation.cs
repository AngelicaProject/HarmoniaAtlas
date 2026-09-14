namespace HarmoniaAtlas.Game;

public sealed record GameInstallation
{
    private GameInstallation(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public static GameInstallation FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A game path is required.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"The game path does not exist: {fullPath}");
        }

        if (!Directory.Exists(Path.Combine(fullPath, "game", "sqpack")))
        {
            throw new DirectoryNotFoundException("The supplied path is not a usable FFXIV installation: game/sqpack is missing.");
        }

        return new GameInstallation(fullPath);
    }
}
