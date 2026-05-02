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
            if (DirectRunDetachedCaptureCommand.TryExecute(args, out var directRunDetachedCaptureExitCode))
            {
                return directRunDetachedCaptureExitCode;
            }

            if (DirectRunExitMonitorCommand.TryExecute(args, out var directRunExitMonitorExitCode))
            {
                return directRunExitMonitorExitCode;
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            if (IsIntakeInitCommand(args) || IsAutomationWorktreeCommand(args))
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

    private static bool IsAutomationWorktreeCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "automation", StringComparison.Ordinal)
            && (string.Equals(args[1], "check", StringComparison.Ordinal)
                || string.Equals(args[1], "clarification-stop", StringComparison.Ordinal)
                || string.Equals(args[1], "complete", StringComparison.Ordinal)
                || string.Equals(args[1], "doctor", StringComparison.Ordinal));
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
