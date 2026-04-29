namespace IntentSystem.Cli.Commands;

/// <summary>
/// G189: pure planner logic for <c>intent-cli issue plan-candidate</c>. Reads
/// the supplied input paths (relative-to-RepoRoot when relative) and builds
/// the deterministic <see cref="IssuePlanCandidateArtifact"/>. Performs no
/// GitHub network calls, applies no labels, and never touches
/// <c>.intent-cli/queue-state.json</c> or <c>.intent-cli/runs.jsonl</c>. The
/// only filesystem reads are the input paths themselves (existence + size +
/// sha256 of bytes); a missing path is recorded with <c>exists: false</c>
/// rather than treated as an error, because planning a candidate may
/// reference work that has not yet landed.
/// </summary>
internal static class IssuePlanCandidateAnalyzer
{
    public static IssuePlanCandidateArtifact Build(
        string repoRoot,
        string executionUnit,
        string title,
        IReadOnlyList<string> contextPaths,
        string? packetPath,
        string? reviewContextPath,
        string generatedAtUtc,
        string resolvedArtifactPath)
    {
        ArgumentNullException.ThrowIfNull(repoRoot);
        ArgumentNullException.ThrowIfNull(executionUnit);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(contextPaths);
        ArgumentNullException.ThrowIfNull(generatedAtUtc);
        ArgumentNullException.ThrowIfNull(resolvedArtifactPath);

        var contextRefs = new List<CandidateSourceReference>(contextPaths.Count);
        foreach (var path in contextPaths)
        {
            contextRefs.Add(BuildReference(repoRoot, path));
        }

        var packetRef = string.IsNullOrEmpty(packetPath)
            ? null
            : BuildReference(repoRoot, packetPath);
        var reviewRef = string.IsNullOrEmpty(reviewContextPath)
            ? null
            : BuildReference(repoRoot, reviewContextPath);

        var summaryLine =
            $"Candidate issue spec for {executionUnit} ({title}) — UNPUBLISHED; not automation-visible.";

        return new IssuePlanCandidateArtifact
        {
            ExecutionUnit = executionUnit,
            Title = title,
            IsPublished = false,
            IsAutomationVisible = false,
            CandidateSpecStatus = IssuePlanCandidateConstants.UnpublishedCandidateStatus,
            ContextPaths = contextRefs,
            PacketPath = packetRef,
            ReviewContextPath = reviewRef,
            GeneratedAtUtc = generatedAtUtc,
            ArtifactPath = resolvedArtifactPath,
            SummaryLine = summaryLine
        };
    }

    internal static CandidateSourceReference BuildReference(string repoRoot, string asPassedPath)
    {
        var resolved = ResolveRelativeToRepoRoot(repoRoot, asPassedPath);
        if (!File.Exists(resolved))
        {
            return new CandidateSourceReference
            {
                Path = asPassedPath,
                Exists = false,
                SizeBytes = null,
                Sha256 = null
            };
        }

        try
        {
            // Reuse existing helpers: hex-of-sha256 from IssuePrepareCommand.
            var bytes = File.ReadAllBytes(resolved);
            var sha = IssuePrepareCommand.ComputeSha256Hex(bytes);
            return new CandidateSourceReference
            {
                Path = asPassedPath,
                Exists = true,
                SizeBytes = bytes.LongLength,
                Sha256 = sha
            };
        }
        catch (IOException)
        {
            return new CandidateSourceReference
            {
                Path = asPassedPath,
                Exists = false,
                SizeBytes = null,
                Sha256 = null
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new CandidateSourceReference
            {
                Path = asPassedPath,
                Exists = false,
                SizeBytes = null,
                Sha256 = null
            };
        }
    }

    private static string ResolveRelativeToRepoRoot(string repoRoot, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        return Path.Combine(repoRoot, path);
    }
}
