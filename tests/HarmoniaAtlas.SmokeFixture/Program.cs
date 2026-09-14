using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.SmokeFixture;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("usage: HarmoniaAtlas.SmokeFixture <output.hxs>");
            return 2;
        }

        new HxsWriter().WriteEmpty(args[0]);
        return 0;
    }
}
