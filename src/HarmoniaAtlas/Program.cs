using HarmoniaAtlas.Cli;

namespace HarmoniaAtlas;

public static class Program
{
    public static int Main(string[] args)
    {
        CliParseResult parseResult = CommandLineParser.Parse(args);

        if (parseResult.Error is not null)
        {
            Console.Error.WriteLine($"error: {parseResult.Error}");
            Console.Error.WriteLine(CliUsage.Text);
            return 2;
        }

        if (parseResult.Command is CliCommand.Help)
        {
            Console.WriteLine(CliUsage.Text);
            return 0;
        }

        try
        {
            return CliApplication.Execute(parseResult);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }
}
