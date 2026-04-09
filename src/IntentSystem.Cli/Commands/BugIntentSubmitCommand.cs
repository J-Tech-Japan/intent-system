namespace IntentSystem.Cli.Commands;

internal static class BugIntentSubmitCommand
{
    public static Func<CliContext, string, RunSubmitResult> RunSubmitExecutor { get; set; } =
        (context, executionUnit) => RunSubmitCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentSubmitRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentSubmitCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-submit command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var relativeArtifactPath = BugIntentSubmitArtifactPathResolver.Resolve(bugId);
        var artifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Bug intent-submit artifact already exists at {artifactPath}");
        }

        var intentStartRef = $".intent-cli/bugs/{bugId}.intent-start.yaml";
        var intentStartPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, intentStartRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(intentStartPath))
        {
            throw new InvalidOperationException($"Bug intent-start artifact was not found at {intentStartPath}");
        }

        var intentStart = BugIntentStartArtifactYaml.Deserialize(File.ReadAllText(intentStartPath));
        if (!string.Equals(intentStart.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-start artifact bug id '{intentStart.BugId}' does not match requested bug id '{bugId}'.");
        }

        if (!intentStart.ReadyToStart || string.IsNullOrWhiteSpace(intentStart.StartedExecutionUnit))
        {
            var notReadyArtifact = new BugIntentSubmitArtifact
            {
                BugId = bugId,
                IntentStartRef = intentStartRef,
                SubmittedExecutionUnit = null,
                LinkedPrUrl = null,
                LinkedPrNumber = null,
                ReadyToSubmit = false
            };

            return new BugIntentSubmitCommandResult
            {
                Artifact = notReadyArtifact,
                ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, notReadyArtifact)
            };
        }

        var submitResult = RunSubmitExecutor(context, intentStart.StartedExecutionUnit);
        var artifact = new BugIntentSubmitArtifact
        {
            BugId = bugId,
            IntentStartRef = intentStartRef,
            SubmittedExecutionUnit = submitResult.ExecutionUnit,
            LinkedPrUrl = submitResult.LinkedPr,
            LinkedPrNumber = ResolvePullRequestNumber(submitResult.LinkedPr),
            ReadyToSubmit = true
        };

        return new BugIntentSubmitCommandResult
        {
            Artifact = artifact,
            ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, artifact)
        };
    }

    private static string WriteArtifact(string absolutePath, string relativePath, BugIntentSubmitArtifact artifact)
    {
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-submit artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentSubmitArtifactYaml.Serialize(artifact));
        return relativePath;
    }

    private static int ResolvePullRequestNumber(string linkedPrUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkedPrUrl);

        var normalized = linkedPrUrl.TrimEnd('/');
        var segment = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (int.TryParse(segment, out var number))
        {
            return number;
        }

        throw new InvalidOperationException(
            $"Linked PR URL '{linkedPrUrl}' must end with a numeric pull request number.");
    }
}
