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

        try
        {
            var result = ExecuteCore(context, args[0], args[2]);
            writer.WriteLine($"Review comment posted for {result.ExecutionUnit}.");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static ReviewCommentResult ExecuteCore(CliContext context, string executionUnit, string bodyPathArg)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPathArg);

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

        var reviewRequestRef = ReviewArtifactPathResolver.Resolve(executionUnit);
        var reviewRequestPath = Path.Combine(
            context.RepoRoot,
            reviewRequestRef.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(reviewRequestPath))
        {
            throw new InvalidOperationException($"Review request artifact was not found at {reviewRequestPath}");
        }

        var bodyPath = ResolveBodyPath(context.RepoRoot, bodyPathArg);
        if (!File.Exists(bodyPath))
        {
            throw new InvalidOperationException($"Review comment body file was not found at {bodyPath}");
        }

        var body = File.ReadAllText(bodyPath);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("Review comment body file must not be empty.");
        }

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

        var commentRef = ResolveCommentRef(context, executionUnit, request.LinkedPr, body);
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

        return new ReviewCommentResult
        {
            ExecutionUnit = executionUnit,
            ArtifactPath = Review.ReviewCommentArtifactPathResolver.Resolve(executionUnit),
            CommentRef = commentRef
        };
    }

    private static string ResolveCommentRef(CliContext context, string executionUnit, string linkedPr, string body)
    {
        if (TryResolveCurrentSessionCommentRef(context, executionUnit, linkedPr, out var existingCommentRef))
        {
            return existingCommentRef;
        }

        return PublisherFactory().PostComment(linkedPr, body);
    }

    private static bool TryResolveCurrentSessionCommentRef(
        CliContext context,
        string executionUnit,
        string linkedPr,
        out string commentRef)
    {
        commentRef = string.Empty;

        var resultArtifactPath = Path.Combine(context.ResolveDirectRunArtifactRootPath(), $"{executionUnit}.result.json");
        if (!File.Exists(resultArtifactPath))
        {
            return false;
        }

        var resultArtifact = DirectRunResultArtifactJson.Deserialize(File.ReadAllText(resultArtifactPath));
        if (!string.Equals(resultArtifact.EntryKind, "review", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(resultArtifact.ReviewCommentRef)
            && resultArtifact.ReviewCommentRef.StartsWith($"{linkedPr}#issuecomment-", StringComparison.OrdinalIgnoreCase))
        {
            commentRef = resultArtifact.ReviewCommentRef;
            return true;
        }

        var providerEventLogPath = Path.IsPathRooted(resultArtifact.RawLogRef)
            ? Path.GetFullPath(resultArtifact.RawLogRef)
            : Path.GetFullPath(Path.Combine(context.RepoRoot, resultArtifact.RawLogRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(providerEventLogPath))
        {
            return false;
        }

        DateTimeOffset? launchedAt = null;
        var requestArtifactPath = Path.Combine(context.ResolveDirectRunArtifactRootPath(), $"{executionUnit}.request.json");
        if (File.Exists(requestArtifactPath))
        {
            var requestArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(requestArtifactPath));
            if (string.Equals(requestArtifact.EntryKind, "review", StringComparison.Ordinal)
                && DirectRunSessionBoundary.TryParseLaunchedAt(requestArtifact.LaunchedAt, out var parsedLaunchedAt))
            {
                launchedAt = parsedLaunchedAt;
            }
        }

        var providerEvents = DirectRunProviderEventJsonl.DeserializeAll(File.ReadAllText(providerEventLogPath));
        var currentSessionEvents = DirectRunSessionBoundary.SelectEvents(providerEvents, resultArtifact.SessionId, launchedAt);
        if (!DirectRunReviewOutcomeSupport.TryResolvePublishedReviewCommentRef(currentSessionEvents, linkedPr, out commentRef))
        {
            return false;
        }

        if (!string.Equals(resultArtifact.ReviewCommentRef, commentRef, StringComparison.Ordinal))
        {
            File.WriteAllText(
                resultArtifactPath,
                DirectRunResultArtifactJson.Serialize(resultArtifact with
                {
                    ReviewCommentRef = commentRef
                }));
        }

        return true;
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
