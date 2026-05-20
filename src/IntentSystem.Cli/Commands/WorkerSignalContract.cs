namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: pure contract for the GitHub-label-backed structured worker
/// signal protocol. A child implementation worker uses it to hand a
/// blocker / follow-up / scope-warning finding back to host
/// review/design automation using only GitHub issue/PR comments plus
/// <c>intent-cli</c> label transitions — never by reading or mutating
/// host <c>.intent-cli</c> / <c>intents/**</c> metadata.
///
/// The protocol has two halves:
/// - <b>send</b> (child): post a marker-wrapped structured comment on
///   the assigned Issue/PR and add <see cref="Labels.SignalSent"/>.
/// - <b>collect / handled</b> (host): scan items carrying
///   <see cref="Labels.SignalSent"/>, parse the latest marker comment,
///   and once processed add <see cref="Labels.SignalHandled"/> while
///   removing <see cref="Labels.SignalSent"/> so later scans skip it.
///
/// This file is intentionally pure (no <c>Process.Start</c>, no
/// filesystem, no GitHub I/O) so the marker format and label-transition
/// planning are unit-testable in isolation.
/// </summary>
internal static class WorkerSignalContract
{
    /// <summary>G374: canonical signal labels (also in the workflow palette).</summary>
    public static class Labels
    {
        public const string SignalSent = "intent-signal-sent";
        public const string SignalHandled = "intent-signal-handled";
    }

    /// <summary>G374: the structured-signal kinds a worker may raise.</summary>
    public static class Kinds
    {
        /// <summary>Issue cannot be safely implemented; decline before implementation.</summary>
        public const string Blocker = "blocker";

        /// <summary>Implementation can proceed, but a follow-up defect/design gap was found.</summary>
        public const string FollowUp = "follow-up";

        /// <summary>Finding belongs to host intent/packet metadata or widens scope.</summary>
        public const string ScopeWarning = "scope-warning";
    }

    /// <summary>G374: GitHub target kinds a signal can be attached to.</summary>
    public static class Targets
    {
        public const string Issue = "issue";
        public const string Pr = "pr";
    }

    /// <summary>G374: marker schema version (bumped if the marker grammar changes).</summary>
    public const int MarkerVersion = 1;

    /// <summary>
    /// G374: HTML-comment marker prefix that identifies a structured
    /// worker-signal comment. Hidden from rendered Markdown so the
    /// comment reads cleanly while still being machine-detectable.
    /// </summary>
    public const string MarkerPrefix = "<!-- intent-signal";

    /// <summary>G374: canonical kind order for help/template/audit output.</summary>
    public static readonly IReadOnlyList<string> AllKinds =
    [
        Kinds.Blocker,
        Kinds.FollowUp,
        Kinds.ScopeWarning,
    ];

    public static bool IsKnownKind(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && (string.Equals(kind, Kinds.Blocker, StringComparison.Ordinal)
            || string.Equals(kind, Kinds.FollowUp, StringComparison.Ordinal)
            || string.Equals(kind, Kinds.ScopeWarning, StringComparison.Ordinal));

    public static bool IsKnownTarget(string? target) =>
        !string.IsNullOrWhiteSpace(target)
        && (string.Equals(target, Targets.Issue, StringComparison.Ordinal)
            || string.Equals(target, Targets.Pr, StringComparison.Ordinal));

    /// <summary>
    /// G374: which GitHub target kinds a given signal kind may be posted
    /// on. blocker is issue-only (decline before implementation),
    /// follow-up is pr-only (a defect found while a PR is open), and
    /// scope-warning can attach to either.
    /// </summary>
    public static IReadOnlyList<string> AllowedTargets(string kind) => kind switch
    {
        Kinds.Blocker => [Targets.Issue],
        Kinds.FollowUp => [Targets.Pr],
        Kinds.ScopeWarning => [Targets.Issue, Targets.Pr],
        _ => Array.Empty<string>(),
    };

    public static bool IsTargetAllowed(string kind, string target) =>
        AllowedTargets(kind).Contains(target, StringComparer.Ordinal);

    /// <summary>
    /// G374: build the single-line machine marker embedded as the first
    /// line of a structured-signal comment, e.g.
    /// <c>&lt;!-- intent-signal v=1 kind=blocker target=issue#851 --&gt;</c>.
    /// </summary>
    public static string BuildMarker(string kind, string target, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "target number must be positive.");
        }

        return $"{MarkerPrefix} v={MarkerVersion} kind={kind} target={target}#{number} -->";
    }

    /// <summary>
    /// G374: wrap an operator-authored signal body in the canonical
    /// comment shape: the hidden marker line, a human-readable heading,
    /// then the body. The result is what gets posted to GitHub.
    /// </summary>
    public static string BuildCommentBody(string kind, string target, int number, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var marker = BuildMarker(kind, target, number);
        var heading = kind switch
        {
            Kinds.Blocker => "Worker signal — blocker (decline before implementation)",
            Kinds.FollowUp => "Worker signal — follow-up finding",
            Kinds.ScopeWarning => "Worker signal — scope warning",
            _ => "Worker signal",
        };

        return string.Join(
            "\n",
            marker,
            string.Empty,
            $"### {heading}",
            string.Empty,
            body.TrimEnd(),
            string.Empty,
            "_Raised by `intent-cli worker signal`. Host review/design automation collects this via "
                + "`intent-cli review collect-signals` and marks it handled with `intent-cli review signal-handled`._");
    }

    /// <summary>
    /// G374: detect whether a comment body is a structured worker signal
    /// and, if so, extract its <c>kind</c>. Tolerant of leading
    /// whitespace and additional marker tokens.
    /// </summary>
    public static bool TryParseSignalKind(string? commentBody, out string kind)
    {
        kind = string.Empty;
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return false;
        }

        using var reader = new StringReader(commentBody);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(MarkerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var token in trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!token.StartsWith("kind=", StringComparison.Ordinal))
                {
                    continue;
                }

                var value = token["kind=".Length..].TrimEnd('-', '>').Trim();
                if (IsKnownKind(value))
                {
                    kind = value;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsSignalComment(string? commentBody) =>
        TryParseSignalKind(commentBody, out _);

    /// <summary>
    /// G374: plan the label transition for SENDING a signal. The fresh
    /// signal becomes pending, so add <see cref="Labels.SignalSent"/>
    /// (when absent) and clear a stale <see cref="Labels.SignalHandled"/>
    /// — a previously-handled item with a new signal is pending again.
    /// </summary>
    public static SignalLabelPlan PlanSentTransition(IReadOnlyCollection<string> currentLabels)
    {
        ArgumentNullException.ThrowIfNull(currentLabels);

        var add = new List<string>();
        var remove = new List<string>();

        if (!currentLabels.Contains(Labels.SignalSent, StringComparer.Ordinal))
        {
            add.Add(Labels.SignalSent);
        }
        if (currentLabels.Contains(Labels.SignalHandled, StringComparer.Ordinal))
        {
            remove.Add(Labels.SignalHandled);
        }

        return new SignalLabelPlan { AddLabels = add, RemoveLabels = remove };
    }

    /// <summary>
    /// G374: plan the label transition for MARKING a signal handled. Add
    /// <see cref="Labels.SignalHandled"/> (when absent) and remove
    /// <see cref="Labels.SignalSent"/> (when present) so later
    /// collection scans skip the already-processed item.
    /// </summary>
    public static SignalLabelPlan PlanHandledTransition(IReadOnlyCollection<string> currentLabels)
    {
        ArgumentNullException.ThrowIfNull(currentLabels);

        var add = new List<string>();
        var remove = new List<string>();

        if (!currentLabels.Contains(Labels.SignalHandled, StringComparer.Ordinal))
        {
            add.Add(Labels.SignalHandled);
        }
        if (currentLabels.Contains(Labels.SignalSent, StringComparer.Ordinal))
        {
            remove.Add(Labels.SignalSent);
        }

        return new SignalLabelPlan { AddLabels = add, RemoveLabels = remove };
    }
}

/// <summary>
/// G374: a planned add/remove label transition for a signal operation.
/// </summary>
internal sealed record SignalLabelPlan
{
    public required IReadOnlyList<string> AddLabels { get; init; }
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    public bool HasChanges => AddLabels.Count > 0 || RemoveLabels.Count > 0;
}
