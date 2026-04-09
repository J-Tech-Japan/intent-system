namespace IntentSystem.Cli.Commands;

internal static class BugExecutionCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugExecutionRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugExecutionCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug plan command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var reportRef = $".intent-cli/bugs/{bugId}.report.yaml";
        var triageRef = $".intent-cli/bugs/{bugId}.triage.yaml";

        var reportPath = Path.GetFullPath(Path.Combine(context.RepoRoot, reportRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException($"Bug report artifact was not found at {reportPath}");
        }

        var triagePath = Path.GetFullPath(Path.Combine(context.RepoRoot, triageRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(triagePath))
        {
            throw new InvalidOperationException($"Bug triage artifact was not found at {triagePath}");
        }

        BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
        var triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));

        var resolvedImplementationRefs = DistinctOrdered(triage.ResolvedImplementationRefs);
        var resolvedReviewContextRefs = DistinctOrdered(triage.ResolvedReviewContextRefs);
        var resolvedPacketRefs = DistinctOrdered(triage.ResolvedPacketRefs);
        string[] implementationTaskCandidates;
        string[] intentTaskCandidates;
        var clarificationRequired = triage.ClarificationRequired;

        if (clarificationRequired || string.Equals(triage.DownstreamAction, "clarification-first", StringComparison.Ordinal))
        {
            implementationTaskCandidates = [];
            intentTaskCandidates = [];
        }
        else
        {
            implementationTaskCandidates = DistinctOrdered(triage.ImplementationRepairCandidates);
            intentTaskCandidates = DistinctOrdered(triage.IntentRepairCandidates);
        }

        var readyToLaunch = !clarificationRequired
            && !string.Equals(triage.DownstreamAction, "clarification-first", StringComparison.Ordinal)
            && (implementationTaskCandidates.Length > 0 || intentTaskCandidates.Length > 0);

        var artifact = new BugExecutionArtifact
        {
            BugId = bugId,
            ReportRef = reportRef,
            TriageRef = triageRef,
            DownstreamAction = triage.DownstreamAction,
            ResolvedImplementationRefs = resolvedImplementationRefs,
            ResolvedReviewContextRefs = resolvedReviewContextRefs,
            ResolvedPacketRefs = resolvedPacketRefs,
            ImplementationTaskCandidates = implementationTaskCandidates,
            IntentTaskCandidates = intentTaskCandidates,
            ClarificationRequired = clarificationRequired,
            ReadyToLaunch = readyToLaunch
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugExecutionCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    private static string WriteArtifact(string repoRoot, BugExecutionArtifact artifact)
    {
        var relativePath = BugExecutionArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug plan artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugExecutionArtifactYaml.Serialize(artifact));

        return relativePath;
    }

    private static string[] DistinctOrdered(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
