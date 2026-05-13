namespace IntentSystem.Cli.Commands;

/// <summary>
/// G312: pure analyzer that classifies a bundle of dirty
/// host-durable-state paths and per-path delta results into one of
/// three terminal classifications:
/// <list type="bullet">
/// <item><c>verified-commit-ready</c> — every dirty durable-state path is one of the deterministic forward-only kinds: <c>.intent-cli/queue-state.json</c> with a forward-only delta (linked_pr / linked_issue added) and/or <c>.intent-cli/runs.jsonl</c> with an append-only delta. Nothing else dirty. Auto-commit safe.</item>
/// <item><c>needs-operator-review</c> — at least one durable-state path has a non-forward delta (in-place edit, removal, replacement) but the data is structurally valid. Operator must inspect.</item>
/// <item><c>unsafe-durable-state</c> — at least one path has corrupt/invalid content (unparseable JSON / JSONL) OR touches a region where the host loop must never auto-commit (<c>intents/**</c>, <c>.intent-cli/issues/**</c>, host binding/AGENTS/CLAUDE markdown), OR involves a deletion of durable state, OR the change set is mixed with packet body / clarification edits. Hard stop.</item>
/// </list>
///
/// Pure data in / pure data out: caller passes pre-classified
/// per-path inputs (the file delta results from the per-format
/// analyzers + structural flags) and gets back the bundle verdict
/// with the verified file list and a recommended commit message.
/// </summary>
internal static class DurableStatePreflightAnalyzer
{
    public const string ClassificationVerifiedCommitReady = "verified-commit-ready";
    public const string ClassificationNeedsOperatorReview = "needs-operator-review";
    public const string ClassificationUnsafe = "unsafe-durable-state";

    public const string PathQueueStateJson = ".intent-cli/queue-state.json";
    public const string PathRunsJsonl = ".intent-cli/runs.jsonl";

    /// <summary>
    /// G343: file segment under <c>.intent-cli/issues/&lt;unit&gt;/</c>
    /// that this analyzer recognizes as a deterministic publish artifact.
    /// Any other path under that directory (body.md, clarifications, etc.)
    /// remains operator-owned and unsafe for host-loop auto-commit.
    /// </summary>
    public const string IssuesDirectorySegment = ".intent-cli/issues/";
    public const string PublishYamlFileName = "publish.yaml";

    /// <summary>
    /// Path prefixes that a forward-only durable-state auto-commit
    /// must NEVER touch. Any dirty path matching one of these
    /// downgrades the bundle to <c>unsafe-durable-state</c>.
    /// </summary>
    /// <remarks>
    /// G343: <c>.intent-cli/issues/</c> is no longer a blanket-unsafe
    /// prefix — canonical <c>publish.yaml</c> within that tree is routed
    /// through <see cref="ApplyPublishYamlResult"/> and accepted as a
    /// forward-only durable-state write when its content parses as a
    /// canonical <see cref="IssuePublishArtifact"/>. Non-publish.yaml
    /// files under that tree are still rejected via the default lane in
    /// <see cref="Analyze"/>.
    /// </remarks>
    private static readonly string[] AlwaysUnsafePrefixes =
    [
        "intents/",                 // intent text / packets / clarifications
        "AGENTS.md",
        "CLAUDE.md",
        ".intent-cli/host-binding.toml",
        ".intent-cli/config.toml",
    ];

    public static DurableStatePreflightResult Analyze(DurableStatePreflightInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var verified = new List<DurableStateVerifiedPath>();
        var review = new List<DurableStateReviewPath>();
        var unsafePaths = new List<DurableStateUnsafePath>();

        foreach (var entry in input.DirtyPaths)
        {
            var path = NormalizePath(entry.Path);

            // Deletions of durable state are always unsafe.
            if (entry.IsDeleted)
            {
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = $"`{path}` is deleted; host-loop auto-commit must never delete durable state.",
                });
                continue;
            }

            if (IsAlwaysUnsafe(path))
            {
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = $"`{path}` is operator-owned durable content (intents/packets/clarifications/host bindings); host-loop auto-commit is never allowed for this path.",
                });
                continue;
            }

            switch (path)
            {
                case PathQueueStateJson:
                    ApplyQueueStateResult(path, entry, verified, review, unsafePaths);
                    break;

                case PathRunsJsonl:
                    ApplyRunsJsonlResult(path, entry, verified, review, unsafePaths);
                    break;

                default:
                    if (IsCanonicalPublishYamlCandidate(path))
                    {
                        // G343: route .intent-cli/issues/<unit>/publish.yaml
                        // through the canonical-content lane.
                        ApplyPublishYamlResult(path, entry, verified, review, unsafePaths);
                        break;
                    }
                    if (path.StartsWith(IssuesDirectorySegment, StringComparison.Ordinal))
                    {
                        // G343: any non-publish.yaml file under
                        // .intent-cli/issues/ (body.md, clarifications/*,
                        // ad-hoc operator notes) stays operator-owned.
                        unsafePaths.Add(new DurableStateUnsafePath
                        {
                            Path = path,
                            Reason = $"`{path}` is dirty inside `{IssuesDirectorySegment}` but is not a recognized canonical publish artifact; host-loop auto-commit must not touch operator-owned issue metadata.",
                        });
                        break;
                    }
                    // Untracked durable-state path that isn't queue-state or runs.jsonl
                    // and isn't in the always-unsafe list. Refuse auto-commit; let
                    // the operator decide.
                    review.Add(new DurableStateReviewPath
                    {
                        Path = path,
                        Reason = $"`{path}` is dirty but is not a recognized forward-only durable-state target (`{PathQueueStateJson}` / `{PathRunsJsonl}`); operator review required.",
                    });
                    break;
            }
        }

        var classification = ChooseTerminalClassification(verified.Count, review.Count, unsafePaths.Count);
        var commitMessage = classification == ClassificationVerifiedCommitReady
            ? BuildCommitMessage(verified)
            : null;

        return new DurableStatePreflightResult
        {
            Classification = classification,
            Summary = BuildSummary(classification, verified, review, unsafePaths),
            VerifiedPaths = verified,
            ReviewPaths = review,
            UnsafePaths = unsafePaths,
            RecommendedCommitMessage = commitMessage,
        };
    }

    private static void ApplyQueueStateResult(
        string path,
        DurableStateDirtyPath entry,
        List<DurableStateVerifiedPath> verified,
        List<DurableStateReviewPath> review,
        List<DurableStateUnsafePath> unsafePaths)
    {
        if (entry.QueueStateDelta is null)
        {
            review.Add(new DurableStateReviewPath
            {
                Path = path,
                Reason = $"`{path}` is dirty but no queue-state delta was supplied; operator review required.",
            });
            return;
        }

        switch (entry.QueueStateDelta.Classification)
        {
            case QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly:
                verified.Add(new DurableStateVerifiedPath
                {
                    Path = path,
                    Summary = entry.QueueStateDelta.Summary,
                });
                break;

            case QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview:
                review.Add(new DurableStateReviewPath
                {
                    Path = path,
                    Reason = entry.QueueStateDelta.Summary,
                });
                break;

            case QueueStateForwardDeltaAnalyzer.ClassificationInvalid:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = entry.QueueStateDelta.Summary,
                });
                break;

            default:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = $"unrecognized queue-state delta classification `{entry.QueueStateDelta.Classification}` for `{path}`.",
                });
                break;
        }
    }

    private static void ApplyRunsJsonlResult(
        string path,
        DurableStateDirtyPath entry,
        List<DurableStateVerifiedPath> verified,
        List<DurableStateReviewPath> review,
        List<DurableStateUnsafePath> unsafePaths)
    {
        if (entry.RunsJsonlDelta is null)
        {
            review.Add(new DurableStateReviewPath
            {
                Path = path,
                Reason = $"`{path}` is dirty but no runs.jsonl delta was supplied; operator review required.",
            });
            return;
        }

        switch (entry.RunsJsonlDelta.Classification)
        {
            case RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly:
                verified.Add(new DurableStateVerifiedPath
                {
                    Path = path,
                    Summary = entry.RunsJsonlDelta.Summary,
                });
                break;

            case RunsJsonlAppendOnlyAnalyzer.ClassificationNeedsOperatorReview:
                review.Add(new DurableStateReviewPath
                {
                    Path = path,
                    Reason = entry.RunsJsonlDelta.Summary,
                });
                break;

            case RunsJsonlAppendOnlyAnalyzer.ClassificationInvalid:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = entry.RunsJsonlDelta.Summary,
                });
                break;

            default:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = $"unrecognized runs.jsonl delta classification `{entry.RunsJsonlDelta.Classification}` for `{path}`.",
                });
                break;
        }
    }

    /// <summary>
    /// G343: <c>publish.yaml</c> under
    /// <c>.intent-cli/issues/&lt;execution-unit&gt;/</c> is the only file
    /// in that tree the host loop is allowed to auto-commit, and only
    /// when its content is classified canonical. The directory must have
    /// the exact three-segment shape — anything deeper (e.g. nested
    /// clarifications) stays operator-owned.
    /// </summary>
    private static bool IsCanonicalPublishYamlCandidate(string normalizedPath)
    {
        if (!normalizedPath.StartsWith(IssuesDirectorySegment, StringComparison.Ordinal))
        {
            return false;
        }
        var remainder = normalizedPath[IssuesDirectorySegment.Length..];
        var slashIndex = remainder.IndexOf('/');
        if (slashIndex <= 0)
        {
            return false;
        }
        var fileSegment = remainder[(slashIndex + 1)..];
        return fileSegment.Equals(PublishYamlFileName, StringComparison.Ordinal);
    }

    private static void ApplyPublishYamlResult(
        string path,
        DurableStateDirtyPath entry,
        List<DurableStateVerifiedPath> verified,
        List<DurableStateReviewPath> review,
        List<DurableStateUnsafePath> unsafePaths)
    {
        if (entry.PublishYamlDelta is null)
        {
            // No content delta supplied — the caller did not (or could
            // not) read the working-tree YAML. Without canonical proof we
            // refuse to auto-commit. Mirrors the queue-state / runs.jsonl
            // "no delta supplied" lane but stays in the unsafe bucket
            // because publish.yaml is operator-owned by default until
            // proven canonical (G343).
            unsafePaths.Add(new DurableStateUnsafePath
            {
                Path = path,
                Reason = $"`{path}` is dirty but no publish.yaml canonical-content delta was supplied; cannot prove the change is a deterministic host-loop write. Run `{PublishYamlCanonicalAnalyzer.RecoveryCommand}` to regenerate canonical content.",
            });
            return;
        }

        switch (entry.PublishYamlDelta.Classification)
        {
            case PublishYamlCanonicalAnalyzer.ClassificationCanonical:
                verified.Add(new DurableStateVerifiedPath
                {
                    Path = path,
                    Summary = entry.PublishYamlDelta.Summary,
                });
                break;

            case PublishYamlCanonicalAnalyzer.ClassificationNonCanonical:
            case PublishYamlCanonicalAnalyzer.ClassificationInvalid:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = entry.PublishYamlDelta.Summary,
                });
                break;

            default:
                unsafePaths.Add(new DurableStateUnsafePath
                {
                    Path = path,
                    Reason = $"unrecognized publish.yaml canonical-content classification `{entry.PublishYamlDelta.Classification}` for `{path}`.",
                });
                break;
        }
    }

    private static string ChooseTerminalClassification(int verified, int review, int unsafeCount)
    {
        if (unsafeCount > 0)
        {
            return ClassificationUnsafe;
        }
        if (review > 0)
        {
            return ClassificationNeedsOperatorReview;
        }
        return verified > 0
            ? ClassificationVerifiedCommitReady
            : ClassificationNeedsOperatorReview;
    }

    private static bool IsAlwaysUnsafe(string normalizedPath)
    {
        foreach (var prefix in AlwaysUnsafePrefixes)
        {
            if (normalizedPath.Equals(prefix, StringComparison.Ordinal))
            {
                return true;
            }
            if (prefix.EndsWith('/') && normalizedPath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string BuildSummary(
        string classification,
        IReadOnlyList<DurableStateVerifiedPath> verified,
        IReadOnlyList<DurableStateReviewPath> review,
        IReadOnlyList<DurableStateUnsafePath> unsafePaths) =>
        classification switch
        {
            ClassificationVerifiedCommitReady =>
                $"All {verified.Count} dirty durable-state path(s) are deterministic forward-only updates; safe to auto-commit (G312).",
            ClassificationNeedsOperatorReview =>
                $"{review.Count} dirty durable-state path(s) need operator review before commit (G312); auto-commit refused.",
            ClassificationUnsafe =>
                $"{unsafePaths.Count} dirty path(s) are unsafe for host-loop auto-commit (intents/packets/clarifications/host-bindings, deletions, or invalid JSON/JSONL); hard stop (G312).",
            _ => $"unrecognized durable-state classification `{classification}`.",
        };

    private static string BuildCommitMessage(IReadOnlyList<DurableStateVerifiedPath> verified)
    {
        // The recommended commit message names the host-loop autoflag
        // explicitly so reviewers can quickly identify that the commit
        // came from the verified durable-state lane and not from a
        // human edit. Keeping the body terse — operators inspect the
        // diff itself for detail.
        var summaries = verified.Select(v => $"- {v.Path}: {v.Summary}");
        return $"chore(host-loop): auto-commit verified durable-state forward updates (G312)\n\n{string.Join("\n", summaries)}";
    }
}

internal sealed record DurableStateDirtyPath
{
    public required string Path { get; init; }
    public required bool IsDeleted { get; init; }
    public QueueStateForwardDeltaResult? QueueStateDelta { get; init; }
    public RunsJsonlAppendOnlyResult? RunsJsonlDelta { get; init; }
    /// <summary>
    /// G343: optional canonical-content classification for dirty
    /// <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c> paths. The
    /// caller computes this by reading the working-tree file and
    /// passing the content + directory-derived execution-unit to
    /// <see cref="PublishYamlCanonicalAnalyzer"/>. Null on any other
    /// path or when the file could not be read.
    /// </summary>
    public PublishYamlCanonicalResult? PublishYamlDelta { get; init; }
}

internal sealed record DurableStatePreflightInput
{
    public required IReadOnlyList<DurableStateDirtyPath> DirtyPaths { get; init; }
}

internal sealed record DurableStateVerifiedPath
{
    public required string Path { get; init; }
    public required string Summary { get; init; }
}

internal sealed record DurableStateReviewPath
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
}

internal sealed record DurableStateUnsafePath
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
}

internal sealed record DurableStatePreflightResult
{
    public required string Classification { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<DurableStateVerifiedPath> VerifiedPaths { get; init; }
    public required IReadOnlyList<DurableStateReviewPath> ReviewPaths { get; init; }
    public required IReadOnlyList<DurableStateUnsafePath> UnsafePaths { get; init; }
    public string? RecommendedCommitMessage { get; init; }
}
