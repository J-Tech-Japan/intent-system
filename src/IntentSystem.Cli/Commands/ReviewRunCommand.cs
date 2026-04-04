using IntentSystem.Review;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ReviewRunCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            writer.WriteLine("Review run command requires an execution unit.");
            return 1;
        }

        var queueState = QueueCommandSupport.LoadQueueState(context, writer);
        if (queueState is null)
        {
            return 1;
        }

        var executionUnit = args[0];
        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            writer.WriteLine($"Execution unit '{executionUnit}' was not found in queue state.");
            return 1;
        }

        var reviewContextRef = queueItem.PacketPaths.ReviewContext;
        var reviewContextPath = Path.Combine(
            context.RepoRoot,
            reviewContextRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(reviewContextPath))
        {
            writer.WriteLine($"Review context artifact was not found at {reviewContextPath}");
            return 1;
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            writer.WriteLine($"Run log was not found at {runLogPath}");
            return 1;
        }

        try
        {
            var reviewContext = ReviewContextMarkdownParser.Parse(File.ReadAllText(reviewContextPath));
            if (!string.Equals(reviewContext.SourceExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review context execution unit '{reviewContext.SourceExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
            var linkedPr = LatestLinkedPrResolver.Resolve(runEvents, executionUnit);
            var request = ReviewRequestFactory.Create(executionUnit, reviewContextRef, linkedPr, reviewContext);
            ReviewArtifactWriter.Write(request, executionUnit, context.RepoRoot, overwrite: true);

            writer.WriteLine($"Review request artifact generated for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }
}
