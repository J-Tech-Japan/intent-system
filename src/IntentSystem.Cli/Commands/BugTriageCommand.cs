namespace IntentSystem.Cli.Commands;

internal static class BugTriageCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        try
        {
            var result = ExecuteCore(context, args);
            BugTriageRenderer.WriteSummary(writer, result.Artifact, result.ArtifactPath);
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }
    }

    internal static BugTriageCommandResult ExecuteCore(CliContext context, string[] args)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            throw new InvalidOperationException("Bug triage command requires '<bug-id>'.");
        }

        var bugId = args[0].Trim();
        var reportRef = $".intent-cli/bugs/{bugId}.report.yaml";
        var reportPath = Path.GetFullPath(Path.Combine(context.RepoRoot, reportRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(reportPath))
        {
            throw new InvalidOperationException($"Bug report artifact was not found at {reportPath}");
        }

        var report = BugReportArtifactYaml.Deserialize(File.ReadAllText(reportPath));
        var originalInstructionRootRefs = DistinctOrdered(
            report.OriginalInstructionRefs
                .Concat(report.AffectedIntentRefs)
                .Concat(report.AffectedRuleSpecRefs));

        var linkedReviewRefs = DistinctOrdered(report.LinkedReviewRefs);
        var resolvedExecutionUnits = new List<string>();
        var resolvedImplementationRefs = new List<string>();
        var resolvedReviewContextRefs = new List<string>();
        var resolvedPacketRefs = new List<string>();
        var unresolvedExecutionUnits = new List<string>();

        foreach (var executionUnit in DistinctOrdered(report.LinkedExecutionUnits))
        {
            var implementationRef = $".intent-cli/issues/{executionUnit}/implementation.md";
            var reviewContextRef = $".intent-cli/issues/{executionUnit}/review-context.md";
            var packetRef = $".intent-cli/issues/{executionUnit}/packet.yaml";

            var implementationPath = Path.GetFullPath(Path.Combine(context.RepoRoot, implementationRef.Replace('/', Path.DirectorySeparatorChar)));
            var reviewContextPath = Path.GetFullPath(Path.Combine(context.RepoRoot, reviewContextRef.Replace('/', Path.DirectorySeparatorChar)));
            var packetPath = Path.GetFullPath(Path.Combine(context.RepoRoot, packetRef.Replace('/', Path.DirectorySeparatorChar)));

            if (File.Exists(implementationPath) && File.Exists(reviewContextPath) && File.Exists(packetPath))
            {
                resolvedExecutionUnits.Add(executionUnit);
                resolvedImplementationRefs.Add(implementationRef);
                resolvedReviewContextRefs.Add(reviewContextRef);
                resolvedPacketRefs.Add(packetRef);
                continue;
            }

            unresolvedExecutionUnits.Add(executionUnit);
        }

        var clarificationReasons = new List<string>();
        if (unresolvedExecutionUnits.Count > 0)
        {
            clarificationReasons.Add(
                $"execution unit roots could not be fully resolved for: {string.Join(", ", unresolvedExecutionUnits)}");
        }

        var reconstructableRootRefs = originalInstructionRootRefs
            .Concat(resolvedPacketRefs)
            .Concat(linkedReviewRefs)
            .ToArray();
        var canReconstructOriginalInstructionRoot = reconstructableRootRefs.Length > 0;
        if (!canReconstructOriginalInstructionRoot)
        {
            clarificationReasons.Add(
                "original instruction root could not be reconstructed from current bug report artifact and linked packet/review refs.");
        }

        var implementationRepairCandidates = resolvedExecutionUnits.ToArray();
        var intentRepairCandidates = DistinctOrdered(report.AffectedIntentRefs.Concat(report.AffectedRuleSpecRefs));

        var hasIntentCandidates = report.AffectedIntentRefs.Count > 0;
        var hasRuleCandidates = report.AffectedRuleSpecRefs.Count > 0;
        var hasImplementationCandidates = implementationRepairCandidates.Length > 0;

        var classification = !canReconstructOriginalInstructionRoot
            ? "unknown"
            : unresolvedExecutionUnits.Count > 0
                ? "packet-gap"
                : hasImplementationCandidates
                    ? "implementation-mismatch"
                    : hasRuleCandidates
                        ? "rule-gap"
                        : hasIntentCandidates
                            ? "intent-gap"
                            : linkedReviewRefs.Length > 0
                                ? "edge-case-gap"
                                : "unknown";

        var downstreamAction = !canReconstructOriginalInstructionRoot
            ? "clarification-first"
            : hasImplementationCandidates && (hasIntentCandidates || hasRuleCandidates)
                ? "dual-track"
                : hasImplementationCandidates
                    ? "implementation-only"
                    : hasIntentCandidates || hasRuleCandidates
                        ? "intent-only"
                        : "clarification-first";

        var artifact = new BugTriageArtifact
        {
            BugId = bugId,
            ReportRef = reportRef,
            Classification = classification,
            DownstreamAction = downstreamAction,
            ClarificationRequired = clarificationReasons.Count > 0,
            ClarificationReasons = clarificationReasons,
            OriginalInstructionRootRefs = originalInstructionRootRefs,
            LinkedReviewRefs = linkedReviewRefs,
            ResolvedExecutionUnits = resolvedExecutionUnits,
            ResolvedImplementationRefs = resolvedImplementationRefs,
            ResolvedReviewContextRefs = resolvedReviewContextRefs,
            ResolvedPacketRefs = resolvedPacketRefs,
            UnresolvedExecutionUnits = unresolvedExecutionUnits,
            ImplementationRepairCandidates = implementationRepairCandidates,
            IntentRepairCandidates = intentRepairCandidates
        };

        var artifactPath = WriteArtifact(context.RepoRoot, artifact);
        return new BugTriageCommandResult
        {
            Artifact = artifact,
            ArtifactPath = artifactPath
        };
    }

    private static string[] DistinctOrdered(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string WriteArtifact(string repoRoot, BugTriageArtifact artifact)
    {
        var relativePath = BugTriageArtifactPathResolver.Resolve(artifact.BugId);
        var absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var directoryPath = Path.GetDirectoryName(absolutePath)
            ?? throw new InvalidOperationException("Bug triage artifact path did not contain a directory.");

        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(absolutePath, BugTriageArtifactYaml.Serialize(artifact));

        return relativePath;
    }
}
