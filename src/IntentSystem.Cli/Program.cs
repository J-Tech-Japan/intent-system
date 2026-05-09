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
            if (IsIntakeInitCommand(args)
                || IsIntentInitCommand(args)
                || IsAutomationWorktreeCommand(args)
                || IsGuideOneshotCommand(args)
                || IsWorkerCommand(args))
            {
                return CommandRouter.Execute(args, CreateBootstrapContext(currentDirectory, args), Console.Out);
            }

            var repoRoot = RepoRootResolver.Resolve(currentDirectory);
            if (repoRoot is null)
            {
                // G299: fail-closed structured guidance instead of the bare
                // "Could not find .intent-cli directory" error. The bare
                // error encouraged agents to fall back to ordinary GitHub
                // review or raw PR comments when invoked from a child
                // implementation checkout that has no `.intent-cli/`.
                return MissingHostStateGuidance.Emit(Console.Out, args, currentDirectory);
            }

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

    private static bool IsIntentInitCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "intent", StringComparison.Ordinal)
            && string.Equals(args[1], "init", StringComparison.Ordinal);
    }

    private static bool IsAutomationWorktreeCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "automation", StringComparison.Ordinal)
            && (string.Equals(args[1], "base-branch-check", StringComparison.Ordinal)
                || string.Equals(args[1], "check", StringComparison.Ordinal)
                || string.Equals(args[1], "clarification-stop", StringComparison.Ordinal)
                || string.Equals(args[1], "complete", StringComparison.Ordinal)
                || string.Equals(args[1], "doctor", StringComparison.Ordinal)
                || string.Equals(args[1], "host-review-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "host-review-diagnostics", StringComparison.Ordinal)
                || string.Equals(args[1], "host-sync-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "issue-publish", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-transition", StringComparison.Ordinal)
                || string.Equals(args[1], "publish-lifecycle-repair", StringComparison.Ordinal)
                || string.Equals(args[1], "publish-recovery", StringComparison.Ordinal)
                || string.Equals(args[1], "reconcile", StringComparison.Ordinal)
                || string.Equals(args[1], "summary", StringComparison.Ordinal)
                || string.Equals(args[1], "workspace-guard", StringComparison.Ordinal));
    }

    private static bool IsGuideOneshotCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "guide", StringComparison.Ordinal)
            && (string.Equals(args[1], "oneshot", StringComparison.Ordinal)
                || string.Equals(args[1], "automation", StringComparison.Ordinal)
                || string.Equals(args[1], "collaborate", StringComparison.Ordinal)
                || string.Equals(args[1], "rules", StringComparison.Ordinal)
                || string.Equals(args[1], "workflow", StringComparison.Ordinal)
                || string.Equals(args[1], "model", StringComparison.Ordinal)
                || string.Equals(args[1], "commands", StringComparison.Ordinal)
                || string.Equals(args[1], "onboarding", StringComparison.Ordinal));
    }

    /// <summary>
    /// G300: child worker commands (next-action / claim / complete /
    /// result-summary / *-preflight) are GitHub-contract-only and must be
    /// runnable from a child implementation repo cwd that has no
    /// `.intent-cli/` directory. The worker family takes <c>--repo
    /// &lt;owner/repo&gt;</c> on the command line and uses installed
    /// `intent-cli` only for GitHub label transitions; parent host queue
    /// state, runs.jsonl, and intent metadata stay host-only. Bootstrapping
    /// the context with cwd as RepoRoot lets these commands run; commands
    /// that incidentally read queue-state already degrade to a warning
    /// when the file is missing (G205 / G300).
    /// </summary>
    private static bool IsWorkerCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "worker", StringComparison.Ordinal)
            && (string.Equals(args[1], "next-action", StringComparison.Ordinal)
                || string.Equals(args[1], "claim", StringComparison.Ordinal)
                || string.Equals(args[1], "complete", StringComparison.Ordinal)
                || string.Equals(args[1], "result-summary", StringComparison.Ordinal)
                || string.Equals(args[1], "issue-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-review-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-comment-preflight", StringComparison.Ordinal));
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
