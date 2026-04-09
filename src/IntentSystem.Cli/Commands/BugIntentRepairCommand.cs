namespace IntentSystem.Cli.Commands;

internal static class BugIntentRepairCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugIntentRepairRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugIntentRepairCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug intent-repair command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var reportRef = $".intent-cli/bugs/{bugId}.report.yaml";
        var triageRef = $".intent-cli/bugs/{bugId}.triage.yaml";
        var executionRef = $".intent-cli/bugs/{bugId}.plan.yaml";

        var reportPath = ResolveExistingArtifactPath(context.RepoRoot, reportRef, "Bug report artifact");
        var triagePath = ResolveExistingArtifactPath(context.RepoRoot, triageRef, "Bug triage artifact");
        var executionPath = ResolveExistingArtifactPath(context.RepoRoot, executionRef, "Bug plan artifact");

        var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
        var triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));
        var execution = BugExecutionArtifactYaml.Deserialize(File.ReadAllText(executionPath));

        ValidateBugId("report", bugId, report.BugId);
        ValidateBugId("triage", bugId, triage.BugId);
        ValidateBugId("execution", bugId, execution.BugId);

        var intentTaskCandidates = DistinctOrdered(execution.IntentTaskCandidates);
        var readyToIssueCut = !triage.ClarificationRequired
            && !string.Equals(triage.DownstreamAction, "implementation-only", StringComparison.Ordinal)
            && intentTaskCandidates.Length > 0;

        var parentRepairTargets = readyToIssueCut
            ? NormalizeParentRepairTargets(intentTaskCandidates)
            : [];

        var artifact = new BugIntentRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = executionRef,
            IntentTaskCandidates = intentTaskCandidates,
            ParentRepairTargets = parentRepairTargets,
            SuggestedIssueTitle = $"Intent repair: {report.Title} ({bugId})",
            SuggestedGoal = BuildSuggestedGoal(report, bugId, parentRepairTargets, executionRef, readyToIssueCut),
            ReadyToIssueCut = readyToIssueCut
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugIntentRepairCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    private static string ResolveExistingArtifactPath(string repoRoot, string relativePath, string artifactLabel)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(absolutePath))
        {
            throw new InvalidOperationException($"{artifactLabel} was not found at {absolutePath}");
        }

        return absolutePath;
    }

    private static void ValidateBugId(string source, string requestedBugId, string artifactBugId)
    {
        if (!string.Equals(requestedBugId, artifactBugId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bug {source} artifact bug id '{artifactBugId}' does not match requested bug id '{requestedBugId}'.");
        }
    }

    private static string[] NormalizeParentRepairTargets(IEnumerable<string> intentTaskCandidates)
    {
        return intentTaskCandidates
            .Select(candidate =>
            {
                var targetType = candidate.Contains("/specs/", StringComparison.Ordinal)
                    || candidate.Contains("/rules/", StringComparison.Ordinal)
                    ? "rule-spec"
                    : "intent";

                return $"{targetType}:{candidate}";
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string BuildSuggestedGoal(
        BugReportArtifact report,
        string bugId,
        IReadOnlyList<string> parentRepairTargets,
        string executionRef,
        bool readyToIssueCut)
    {
        if (!readyToIssueCut)
        {
            return $"Prepare parent intent repair for '{report.Title}' ({bugId}) once issue-cut blockers are cleared from {executionRef}.";
        }

        return $"Repair parent intent targets for '{report.Title}' ({bugId}) using {executionRef}: {string.Join(", ", parentRepairTargets)}";
    }

    private static string WriteArtifact(string repoRoot, BugIntentRepairArtifact artifact)
    {
        var relativePath = BugIntentRepairArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug intent-repair artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugIntentRepairArtifactYaml.Serialize(artifact));

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
