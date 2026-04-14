using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (DirectRunExitMonitorCommand.TryExecute(args, out var directRunExitMonitorExitCode))
            {
                return directRunExitMonitorExitCode;
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            if (IsIntakeInitCommand(args))
            {
                return CommandRouter.Execute(args, CreateBootstrapContext(currentDirectory, args), Console.Out);
            }

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

    private static bool IsIntakeInitCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "intake", StringComparison.Ordinal)
            && string.Equals(args[1], "init", StringComparison.Ordinal);
    }

    private static CliContext CreateBootstrapContext(string currentDirectory, string[] args)
    {
        var domain = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
            ? args[2].Trim()
            : "bootstrap";

        return new CliContext
        {
            RepoRoot = currentDirectory,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = domain,
                    ArtifactRoot = CliRuntimeContracts.IntentCliDirectoryName,
                    WorktreeRoot = CliRuntimeContracts.DefaultWorktreeRoot
                }
            }
        };
    }
}
