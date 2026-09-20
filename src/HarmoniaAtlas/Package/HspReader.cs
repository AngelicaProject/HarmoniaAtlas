namespace HarmoniaAtlas.Package;

public static class HspReader
{
    public static HspPackageSummary Open(string path) => HspPackageValidator.Validate(path);

    public static HspPackageSummary Validate(string path) => HspPackageValidator.Validate(path);
}
