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

        try
        {
            var result = ExecuteCore(context, args[0]);
            writer.WriteLine($"Review request artifact generated for {result.ExecutionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static ReviewRunResult ExecuteCore(CliContext context, string executionUnit)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        var queueStatePath = context.GetQueueStatePath();
        var queueState = QueueCommandSupport.LoadQueueState(context, TextWriter.Null);
        if (queueState is null)
        {
            throw new InvalidOperationException($"No queue state found at {queueStatePath}");
        }

        var queueItem = queueState.Items.FirstOrDefault(item =>
            string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));

        if (queueItem is null)
        {
            throw new InvalidOperationException($"Execution unit '{executionUnit}' was not found in queue state.");
        }

        var reviewContextRef = queueItem.PacketPaths.ReviewContext;
        var reviewContextPath = Path.Combine(
            context.RepoRoot,
            reviewContextRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(reviewContextPath))
        {
            throw new InvalidOperationException($"Review context artifact was not found at {reviewContextPath}");
        }

        var runLogPath = context.GetRunLogPath();
        if (!File.Exists(runLogPath))
        {
            throw new InvalidOperationException($"Run log was not found at {runLogPath}");
        }

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
        var artifactPath = Review.ReviewArtifactPathResolver.Resolve(executionUnit);

        return new ReviewRunResult
        {
            ExecutionUnit = executionUnit,
            ArtifactPath = artifactPath
        };
    }
}
