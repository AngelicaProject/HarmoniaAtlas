using System.Text;

namespace HarmoniaAtlas.Game;

public sealed class GameVersionReader
{
    public string Read(GameInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        string versionPath = Path.Combine(installation.FullPath, "game", "ffxivgame.ver");
        if (!File.Exists(versionPath))
        {
            throw new FileNotFoundException("The FFXIV game version file is missing.", versionPath);
        }

        string value = File.ReadAllText(versionPath, Encoding.UTF8).Trim().Normalize(NormalizationForm.FormC);
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n'))
        {
            throw new InvalidDataException("The FFXIV game version file is empty or malformed.");
        }

        return value;
    }
}
