namespace IntentSystem.Cli.Commands;

internal static class BugIntentStartCommand
{
    public static Func<CliContext, string, RunStartResult> RunStartExecutor { get; set; } =
        (context, executionUnit) => RunStartCommand.ExecuteCore(context, executionUnit);

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentStartRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentStartCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-start command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var relativeArtifactPath = BugIntentStartArtifactPathResolver.Resolve(bugId);
        var artifactPath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(artifactPath))
        {
            throw new InvalidOperationException($"Bug intent-start artifact already exists at {artifactPath}");
        }

        var intentEnqueueRef = $".intent-cli/bugs/{bugId}.intent-enqueue.yaml";
        var intentEnqueuePath = Path.GetFullPath(
            Path.Combine(context.RepoRoot, intentEnqueueRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(intentEnqueuePath))
        {
            throw new InvalidOperationException($"Bug intent-enqueue artifact was not found at {intentEnqueuePath}");
        }

        var intentEnqueue = BugIntentEnqueueArtifactYaml.Deserialize(File.ReadAllText(intentEnqueuePath));
        if (!string.Equals(intentEnqueue.BugId, bugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug intent-enqueue artifact bug id '{intentEnqueue.BugId}' does not match requested bug id '{bugId}'.");
        }

        if (!intentEnqueue.ReadyToEnqueue || string.IsNullOrWhiteSpace(intentEnqueue.AllocatedExecutionUnit))
        {
            var notReadyArtifact = new BugIntentStartArtifact
            {
                BugId = bugId,
                IntentEnqueueRef = intentEnqueueRef,
                AllocatedExecutionUnit = intentEnqueue.AllocatedExecutionUnit,
                WorktreePath = null,
                BranchName = null,
                ReadyToStart = false
            };

            return new BugIntentStartCommandResult
            {
                Artifact = notReadyArtifact,
                ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, notReadyArtifact)
            };
        }

        var startResult = RunStartExecutor(context, intentEnqueue.AllocatedExecutionUnit);
        var artifact = new BugIntentStartArtifact
        {
            BugId = bugId,
            IntentEnqueueRef = intentEnqueueRef,
            AllocatedExecutionUnit = startResult.ExecutionUnit,
            WorktreePath = startResult.WorktreePath,
            BranchName = startResult.BranchName,
            ReadyToStart = true
        };

        return new BugIntentStartCommandResult
        {
            Artifact = artifact,
            ArtifactPath = WriteArtifact(artifactPath, relativeArtifactPath, artifact)
        };
    }

    private static string WriteArtifact(string absolutePath, string relativePath, BugIntentStartArtifact artifact)
    {
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-start artifact path did not contain a directory.");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentStartArtifactYaml.Serialize(artifact));
        return relativePath;
    }
}
