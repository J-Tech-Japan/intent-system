using IntentSystem.Review;
using IntentSystem.Review.Models;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

internal static class ReviewCommentCommand
{
    private const string TransitionActor = "intent-cli";

    public static Func<IReviewCommentPublisher> PublisherFactory { get; set; } = () => new GhReviewCommentPublisher();

    public static Func<DateTimeOffset> TimestampFactory { get; set; } = () => DateTimeOffset.UtcNow;

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length != 3
            || string.IsNullOrWhiteSpace(args[0])
            || !string.Equals(args[1], "--from-file", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[2]))
        {
            writer.WriteLine("Review comment command requires an execution unit and '--from-file <path>'.");
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

        var reviewRequestRef = ReviewArtifactPathResolver.Resolve(executionUnit);
        var reviewRequestPath = Path.Combine(
            context.RepoRoot,
            reviewRequestRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(reviewRequestPath))
        {
            writer.WriteLine($"Review request artifact was not found at {reviewRequestPath}");
            return 1;
        }

        var bodyPath = ResolveBodyPath(context.RepoRoot, args[2]);
        if (!File.Exists(bodyPath))
        {
            writer.WriteLine($"Review comment body file was not found at {bodyPath}");
            return 1;
        }

        var body = File.ReadAllText(bodyPath);
        if (string.IsNullOrWhiteSpace(body))
        {
            writer.WriteLine("Review comment body file must not be empty.");
            return 1;
        }

        try
        {
            var request = ReviewRequestSerializer.Deserialize(File.ReadAllText(reviewRequestPath));
            if (!string.Equals(request.ExecutionUnit, queueItem.ExecutionUnit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Review request execution unit '{request.ExecutionUnit}' must match queue item execution unit '{queueItem.ExecutionUnit}'.");
            }

            if (string.IsNullOrWhiteSpace(request.LinkedPr))
            {
                throw new InvalidOperationException("Review request must contain a linked PR.");
            }

            var commentRef = PublisherFactory().PostComment(request.LinkedPr, body);
            var artifact = new ReviewCommentArtifact
            {
                ExecutionUnit = executionUnit,
                ReviewRequestRef = reviewRequestRef,
                LinkedPr = request.LinkedPr,
                CommentRef = commentRef,
                BodyPath = bodyPath
            };

            ReviewCommentArtifactWriter.Write(artifact, executionUnit, context.RepoRoot, overwrite: true);

            var transition = QueueManager.RequestFix(
                queueState,
                executionUnit,
                TransitionActor,
                TimestampFactory());

            PersistTransition(
                context,
                transition with
                {
                    Event = transition.Event with
                    {
                        LinkedPr = request.LinkedPr,
                        CommentRef = commentRef
                    }
                });

            writer.WriteLine($"Review comment posted for {executionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string ResolveBodyPath(string repoRoot, string rawPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPath);

        return Path.IsPathRooted(rawPath)
            ? Path.GetFullPath(rawPath)
            : Path.GetFullPath(Path.Combine(repoRoot, rawPath));
    }

    private static void PersistTransition(CliContext context, QueueTransitionResult result)
    {
        var queueStatePath = context.GetQueueStatePath();
        File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(result.UpdatedState));

        var runLogPath = context.GetRunLogPath();
        var runLogDirectory = Path.GetDirectoryName(runLogPath)
            ?? throw new InvalidOperationException("Run log path did not contain a directory.");
        Directory.CreateDirectory(runLogDirectory);
        File.AppendAllText(
            runLogPath,
            RunLogSerializer.SerializeLine(result.Event) + Environment.NewLine);
    }
}
