namespace HarmoniaAtlas.Game;

public sealed record GameInstallation
{
    private GameInstallation(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public string SqpackPath => Path.Combine(FullPath, "game", "sqpack");

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

        GameInstallation installation = new(fullPath);
        if (!Directory.Exists(installation.SqpackPath))
        {
            throw new DirectoryNotFoundException("The supplied path is not a usable FFXIV installation: game/sqpack is missing.");
        }

        return installation;
    }
}
