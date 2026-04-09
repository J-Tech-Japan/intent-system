namespace IntentSystem.Cli.Commands;

internal static class BugIntentCommentCommand
{
    public static Func<CliContext, string, string, ReviewCommentResult> ReviewCommentExecutor { get; set; } =
        (context, executionUnit, bodyPath) => ReviewCommentCommand.ExecuteCore(context, executionUnit, bodyPath);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentCommentRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentCommentCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 3
            || string.IsNullOrWhiteSpace(args[0])
            || !string.Equals(args[1], "--from-file", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[2]))
        {
            throw new InvalidOperationException("Bug intent-comment command requires '<bug-id> --from-file <path>'.");
        }

        var bugId = args[0].Trim();
        var bodyPath = args[2];
        var relativeArtifactPath = BugIntentCommentArtifactPathResolver.Resolve(bugId);
        var artifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Bug intent-comment artifact already exists at {artifactPath}");
        }

        var intentReviewRef = BugIntentReviewArtifactPathResolver.Resolve(bugId);
        var intentReviewPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, intentReviewRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(intentReviewPath))
        {
            throw new InvalidOperationException($"Bug intent-review artifact was not found at {intentReviewPath}");
        }

        var intentReview = BugIntentReviewArtifactYaml.Deserialize(File.ReadAllText(intentReviewPath));
        if (!string.Equals(intentReview.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-review artifact bug id '{intentReview.BugId}' does not match requested bug id '{bugId}'.");
        }

        if (!intentReview.ReadyToReview || string.IsNullOrWhiteSpace(intentReview.ReviewedExecutionUnit))
        {
            var notReadyArtifact = new BugIntentCommentArtifact
            {
                BugId = bugId,
                IntentReviewRef = intentReviewRef,
                CommentedExecutionUnit = null,
                ReviewCommentRef = null,
                CommentRef = null,
                LinkedPrUrl = intentReview.LinkedPrUrl,
                ReadyToComment = false
            };

            return new BugIntentCommentCommandResult
            {
                Artifact = notReadyArtifact,
                ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, notReadyArtifact)
            };
        }

        var reviewCommentResult = ReviewCommentExecutor(context, intentReview.ReviewedExecutionUnit, bodyPath);
        var artifact = new BugIntentCommentArtifact
        {
            BugId = bugId,
            IntentReviewRef = intentReviewRef,
            CommentedExecutionUnit = reviewCommentResult.ExecutionUnit,
            ReviewCommentRef = reviewCommentResult.ArtifactPath,
            CommentRef = reviewCommentResult.CommentRef,
            LinkedPrUrl = intentReview.LinkedPrUrl,
            ReadyToComment = true
        };

        return new BugIntentCommentCommandResult
        {
            Artifact = artifact,
            ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, artifact)
        };
    }

    private static string WriteArtifact(string absolutePath, string relativePath, BugIntentCommentArtifact artifact)
    {
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-comment artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentCommentArtifactYaml.Serialize(artifact));
        return relativePath;
    }
}
