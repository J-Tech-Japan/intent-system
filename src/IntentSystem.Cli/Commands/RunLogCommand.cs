using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class RunLogCommand
{
    private const string UsageLine =
        "Usage: intent-cli run log <execution-unit> [--target-repo <owner/repo>]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var executionUnit, out var targetRepo, out var argError))
        {
            writer.WriteLine(argError);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        // G327: when an explicit `--target-repo` is supplied, this read
        // is scoped to (domain, owner/repo) — we route through
        // `ResolveRunLogPathForRead` so a scoped runs.jsonl wins when on
        // disk, with a legacy-root fallback during the migration window.
        // The packet lookup remains at `.intent-cli/issues/<execution-unit>`
        // (queueItem.PacketPaths.Yaml), unchanged.
        string runLogPath;
        StateLocationKind? scopedKind = null;
        if (string.IsNullOrWhiteSpace(targetRepo))
        {
            runLogPath = context.GetRunLogPath();
        }
        else
        {
            var location = RuntimeScopedStateResolver.ResolveRunLogPathForRead(
                context.RepoRoot,
                context.Config.Project.Domain,
                targetRepo);
            runLogPath = location.Path;
            scopedKind = location.Kind;
        }

        if (!File.Exists(runLogPath))
        {
            writer.WriteLine($"Run log was not found at {runLogPath}");
            return 1;
        }

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath))
            .Where(runEvent => string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal))
            .ToArray();

        if (scopedKind is { } kind)
        {
            // Surface the chosen layout so operators can see whether the
            // run log came from scoped or legacy storage during the
            // G327 migration window.
            writer.WriteLine(kind switch
            {
                StateLocationKind.Scoped => $"Run log source: scoped ({runLogPath})",
                StateLocationKind.Legacy => $"Run log source: legacy ({runLogPath})",
                _ => $"Run log source: {kind.ToString().ToLowerInvariant()} ({runLogPath})"
            });
        }

        RunLogRenderer.Write(writer, queueItem, runEvents);
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string executionUnit,
        out string? targetRepo,
        out string error)
    {
        executionUnit = string.Empty;
        targetRepo = null;
        error = string.Empty;

        if (args.Length == 0)
        {
            error = "Run log command requires an execution unit.";
            return false;
        }

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo).";
                        return false;
                    }
                    targetRepo = args[index + 1];
                    index++;
                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{argument}'.";
                        return false;
                    }
                    if (!string.IsNullOrEmpty(executionUnit))
                    {
                        error = $"Unexpected positional argument '{argument}' (execution unit already set to '{executionUnit}').";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        error = "Run log command requires an execution unit.";
                        return false;
                    }
                    executionUnit = argument;
                    break;
            }
        }

        if (string.IsNullOrEmpty(executionUnit))
        {
            error = "Run log command requires an execution unit.";
            return false;
        }

        return true;
    }
}
