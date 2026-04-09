namespace IntentSystem.Cli.Commands;

internal static class BugIntentReviewCommand
{
    public static Func<CliContext, string, ReviewRunResult> ReviewRunExecutor { get; set; } =
        (context, executionUnit) => ReviewRunCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentReviewRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentReviewCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-review command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var relativeArtifactPath = BugIntentReviewArtifactPathResolver.Resolve(bugId);
        var artifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Bug intent-review artifact already exists at {artifactPath}");
        }

        var intentSubmitRef = $".intent-cli/bugs/{bugId}.intent-submit.yaml";
        var intentSubmitPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, intentSubmitRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(intentSubmitPath))
        {
            throw new InvalidOperationException($"Bug intent-submit artifact was not found at {intentSubmitPath}");
        }

        var intentSubmit = BugIntentSubmitArtifactYaml.Deserialize(File.ReadAllText(intentSubmitPath));
        if (!string.Equals(intentSubmit.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-submit artifact bug id '{intentSubmit.BugId}' does not match requested bug id '{bugId}'.");
        }

        if (!intentSubmit.ReadyToSubmit || string.IsNullOrWhiteSpace(intentSubmit.SubmittedExecutionUnit))
        {
            var notReadyArtifact = new BugIntentReviewArtifact
            {
                BugId = bugId,
                IntentSubmitRef = intentSubmitRef,
                ReviewedExecutionUnit = null,
                ReviewRequestRef = null,
                ReadyToReview = false
            };

            return new BugIntentReviewCommandResult
            {
                Artifact = notReadyArtifact,
                ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, notReadyArtifact)
            };
        }

        var reviewRunResult = ReviewRunExecutor(context, intentSubmit.SubmittedExecutionUnit);
        var artifact = new BugIntentReviewArtifact
        {
            BugId = bugId,
            IntentSubmitRef = intentSubmitRef,
            ReviewedExecutionUnit = reviewRunResult.ExecutionUnit,
            ReviewRequestRef = reviewRunResult.ArtifactPath,
            ReadyToReview = true
        };

        return new BugIntentReviewCommandResult
        {
            Artifact = artifact,
            ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, artifact)
        };
    }

    private static string WriteArtifact(string absolutePath, string relativePath, BugIntentReviewArtifact artifact)
    {
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-review artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentReviewArtifactYaml.Serialize(artifact));
        return relativePath;
    }
}
