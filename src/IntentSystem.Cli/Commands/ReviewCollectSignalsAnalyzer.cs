namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: pure classifier for <c>intent-cli review collect-signals</c>.
/// Given the normalized issue/PR candidates (each already filtered to
/// carry <c>intent-signal-sent</c> by the gh label query, but tolerant
/// of any label set so it stays unit-testable), it parses the latest
/// structured marker comment per item and partitions them into:
/// - pending  : has intent-signal-sent + a parseable marker, not handled
/// - skipped  : intent-signal-handled present (already processed)
/// - unmarked : labelled intent-signal-sent but no marker comment found
/// No GitHub or filesystem I/O — the command layer feeds it data read
/// through the lister + gateway seams.
/// </summary>
internal static class ReviewCollectSignalsAnalyzer
{
    public static ReviewCollectSignalsResult Analyze(
        string repo,
        IReadOnlyList<SignalCandidateInput> candidates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(candidates);

        var pending = new List<PendingSignal>();
        var warnings = new List<string>();
        var handledSkipped = 0;
        var unmarked = 0;

        foreach (var candidate in candidates)
        {
            var hasSent = candidate.Labels.Contains(WorkerSignalContract.Labels.SignalSent, StringComparer.Ordinal);
            var hasHandled = candidate.Labels.Contains(WorkerSignalContract.Labels.SignalHandled, StringComparer.Ordinal);

            // Already-processed items: handled marker present and the pending
            // marker cleared. These must never be reprocessed.
            if (hasHandled && !hasSent)
            {
                handledSkipped++;
                continue;
            }

            // Not actually a pending signal (defensive — production filters
            // on the label, but a fed candidate may not carry it).
            if (!hasSent)
            {
                continue;
            }

            if (hasHandled)
            {
                warnings.Add(
                    $"{candidate.Target} #{candidate.Number} carries both '{WorkerSignalContract.Labels.SignalSent}' and '{WorkerSignalContract.Labels.SignalHandled}'; treating as pending — run `intent-cli review signal-handled` to converge.");
            }

            if (!TryResolveLatestSignalComment(candidate.Comments, out var signalKind, out var commentRef, out var createdAt))
            {
                unmarked++;
                warnings.Add(
                    $"{candidate.Target} #{candidate.Number} is labelled '{WorkerSignalContract.Labels.SignalSent}' but has no parseable structured signal marker comment.");
                continue;
            }

            pending.Add(new PendingSignal
            {
                Target = candidate.Target,
                Number = candidate.Number,
                SignalKind = signalKind,
                Title = candidate.Title,
                Url = candidate.Url,
                CommentRef = commentRef,
                CommentCreatedAt = createdAt,
            });
        }

        return new ReviewCollectSignalsResult
        {
            Repo = repo,
            PendingSignals = pending,
            HandledSkippedCount = handledSkipped,
            UnmarkedCount = unmarked,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// G374: among an item's comments, pick the most recent one that is a
    /// structured signal marker (ISO-8601 UTC timestamps sort
    /// lexicographically; comments with no timestamp fall back to source
    /// order). Returns its kind, url, and created-at.
    /// </summary>
    private static bool TryResolveLatestSignalComment(
        IReadOnlyList<GitHubSignalComment> comments,
        out string signalKind,
        out string commentRef,
        out string createdAt)
    {
        signalKind = string.Empty;
        commentRef = string.Empty;
        createdAt = string.Empty;

        GitHubSignalComment? latest = null;
        var latestKind = string.Empty;
        foreach (var comment in comments)
        {
            if (!WorkerSignalContract.TryParseSignalKind(comment.Body, out var kind))
            {
                continue;
            }

            if (latest is null
                || string.Compare(comment.CreatedAt, latest.CreatedAt, StringComparison.Ordinal) >= 0)
            {
                latest = comment;
                latestKind = kind;
            }
        }

        if (latest is null)
        {
            return false;
        }

        signalKind = latestKind;
        commentRef = latest.Url;
        createdAt = latest.CreatedAt;
        return true;
    }
}

/// <summary>
/// G374: normalized input row for <see cref="ReviewCollectSignalsAnalyzer"/>.
/// The command builds these from the candidate lister (labels/title/url)
/// and the comment gateway (comments).
/// </summary>
internal sealed record SignalCandidateInput
{
    public required string Target { get; init; }
    public required int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public required IReadOnlyList<string> Labels { get; init; }
    public required IReadOnlyList<GitHubSignalComment> Comments { get; init; }
}
