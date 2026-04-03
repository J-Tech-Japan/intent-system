using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var repoRoot = RepoRootResolver.Resolve(currentDirectory)
                ?? throw new InvalidOperationException(
                    $"Could not find {CliRuntimeContracts.IntentCliDirectoryName} directory from '{currentDirectory}'.");

            var context = new CliContext
            {
                RepoRoot = repoRoot,
                Config = CliConfigLoader.LoadFromFile(CliRuntimeContracts.GetConfigPath(repoRoot))
            };

            return CommandRouter.Execute(args, context, Console.Out);
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException
            or FileNotFoundException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
