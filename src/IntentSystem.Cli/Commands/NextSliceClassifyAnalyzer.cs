using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Read-only continuation classifier for the AI tasking thread (G185). Inspects
/// local <c>.intent-cli</c> state plus parent-host clarifications to decide which
/// of <see cref="NextSliceClassification"/> applies. Never mutates queue state,
/// runs, GitHub, packet files, or source. Reuses <see cref="StatusBriefAnalyzer"/>
/// for the canonical WIP predicate and <see cref="ClarificationOpenDetector"/>
/// for the structured open-blocker parser.
/// </summary>
internal static class NextSliceClassifyAnalyzer
{
    private const int ClarificationSummaryCharLimit = 240;

    public static NextSliceClassifyResult Analyze(CliContext context, string? domainOverride)
    {
        ArgumentNullException.ThrowIfNull(context);

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride;

        // (1) Required-but-corrupted artifact precedence.
        var queueStatePath = context.GetQueueStatePath();
        QueueState? queueState = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (Exception exception) when (
                exception is JsonException
                or InvalidOperationException
                or FormatException)
            {
                return new NextSliceClassifyResult
                {
                    Domain = domain,
                    Classification = NextSliceClassification.InspectManually,
                    Rationale = $"queue-state.json could not be parsed: {exception.Message}",
                    WipRefs = Array.Empty<string>(),
                    ClarificationSummary = null,
                    CandidateExecutionUnit = null,
                    CandidatePacketPath = null,
                    RecommendedNextAction = "Inspect .intent-cli/queue-state.json manually before continuing.",
                    Reason = $"corrupted artifact: {queueStatePath}"
                };
            }
        }

        // (2) WIP precedence — reuse StatusBriefAnalyzer's canonical predicate.
        var wipRefs = new List<string>();
        if (queueState is not null)
        {
            StatusBriefAnalyzer.CollectWipUnits(queueState, wipRefs, reviewUnits: null);
        }

        if (wipRefs.Count > 0)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.SkipDueToWip,
                Rationale = $"queue-state.json shows in-flight WIP units: {string.Join(", ", wipRefs)}.",
                WipRefs = wipRefs,
                ClarificationSummary = null,
                CandidateExecutionUnit = null,
                CandidatePacketPath = null,
                RecommendedNextAction = "Wait for in-flight units to clear before cutting another slice.",
                Reason = null
            };
        }

        // (3) Clarification precedence — reuse ClarificationOpenDetector.
        var clarificationPath = ResolveClarificationPath(context, domain);
        string? clarificationSummary = null;
        var clarificationOpen = false;
        if (clarificationPath is not null && File.Exists(clarificationPath))
        {
            var content = File.ReadAllText(clarificationPath);
            clarificationOpen = ClarificationOpenDetector.HasOpenBlocker(content);
            if (clarificationOpen)
            {
                clarificationSummary = ExtractFirstOpenBlockerSummary(content);
            }
        }

        if (clarificationOpen)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.ClarificationRequired,
                Rationale = "open clarification blocker detected under '## Current Open Blockers'.",
                WipRefs = Array.Empty<string>(),
                ClarificationSummary = clarificationSummary,
                CandidateExecutionUnit = null,
                CandidatePacketPath = null,
                RecommendedNextAction = "Resolve or record clarification before cutting another slice.",
                Reason = null
            };
        }

        // (4) Issue-cut-ready precedence — find a deterministic packet candidate.
        // A complete packet requires BOTH github-body.md AND review-context.md
        // (the canonical packet shape produced by the projection pipeline; see
        // PacketPathResolver / IssueDraftCommand / QueueEnqueueCommand). Packets
        // missing review-context.md are NOT publishable from intent-cli's
        // existing issue prepare/publish-reviewed flow without operator
        // intervention, so they degrade to `inspect-manually` rather than
        // `issue-cut-ready`.
        var candidate = FindIssueCutCandidate(context, queueState);
        if (candidate.Kind == IssueCutCandidateKind.Ambiguous)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.InspectManually,
                Rationale = "multiple unlinked issue packets visible; cannot deterministically pick one.",
                WipRefs = Array.Empty<string>(),
                ClarificationSummary = null,
                CandidateExecutionUnit = null,
                CandidatePacketPath = null,
                RecommendedNextAction = "Inspect .intent-cli/issues/ to disambiguate the next candidate.",
                Reason = "ambiguous candidate set under .intent-cli/issues/"
            };
        }

        if (candidate.Kind == IssueCutCandidateKind.Incomplete)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.InspectManually,
                Rationale = $"unlinked issue packet for {candidate.Unit} is incomplete (missing review-context.md).",
                WipRefs = Array.Empty<string>(),
                ClarificationSummary = null,
                CandidateExecutionUnit = null,
                CandidatePacketPath = null,
                RecommendedNextAction = $"Complete the packet under .intent-cli/issues/{candidate.Unit}/ before cutting.",
                Reason = $"incomplete packet at .intent-cli/issues/{candidate.Unit}/ (missing review-context.md)"
            };
        }

        if (candidate.Kind == IssueCutCandidateKind.Ready
            && candidate.Unit is not null
            && candidate.Path is not null)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.IssueCutReady,
                Rationale = $"issue packet for {candidate.Unit} is present and not yet linked in queue-state.",
                WipRefs = Array.Empty<string>(),
                ClarificationSummary = null,
                CandidateExecutionUnit = candidate.Unit,
                CandidatePacketPath = candidate.Path,
                RecommendedNextAction = $"Publish reviewed issue for {candidate.Unit}.",
                Reason = null
            };
        }

        // (5) Default fallthrough.
        return new NextSliceClassifyResult
        {
            Domain = domain,
            Classification = NextSliceClassification.NoActionableItem,
            Rationale = "no WIP, no open clarification, no unlinked issue packet candidate.",
            WipRefs = Array.Empty<string>(),
            ClarificationSummary = null,
            CandidateExecutionUnit = null,
            CandidatePacketPath = null,
            RecommendedNextAction = "No deterministic next action; revisit parent intents to plan a new slice.",
            Reason = null
        };
    }

    private static string? ResolveClarificationPath(CliContext context, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return null;
        }

        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot)
            ? context.RepoRoot
            : parentRoot;

        return Path.Combine(baseRoot!, "intents", domain, "clarifications", "open.md");
    }

    private static string? ExtractFirstOpenBlockerSummary(string content)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var heading = line[3..].Trim();
                inSection = string.Equals(heading, "Current Open Blockers", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal)
                && !trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                continue;
            }

            var item = trimmed[2..].Trim();
            if (item.Length == 0)
            {
                continue;
            }

            // Skip the no-blocker sentinel(s) — same shape as ClarificationOpenDetector
            // but we deliberately re-use HasOpenBlocker for the boolean decision and
            // only inspect items here for surface-level summary text.
            if (item.Contains("現時点で child issue cut を要する root blocker はない", StringComparison.Ordinal)
                || item.Contains("no root blocker requiring child issue cut", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return item.Length > ClarificationSummaryCharLimit
                ? item[..ClarificationSummaryCharLimit] + "…"
                : item;
        }

        return null;
    }

    internal enum IssueCutCandidateKind
    {
        None,
        Ready,
        Ambiguous,
        Incomplete
    }

    internal readonly record struct IssueCutCandidate(
        IssueCutCandidateKind Kind,
        string? Unit,
        string? Path);

    private static IssueCutCandidate FindIssueCutCandidate(
        CliContext context,
        QueueState? queueState)
    {
        var issuesRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesRoot))
        {
            return new IssueCutCandidate(IssueCutCandidateKind.None, null, null);
        }

        var linkedUnits = BuildLinkedUnitSet(queueState);

        var complete = new List<(string Unit, string Path)>();
        var incomplete = new List<string>();
        foreach (var unitDir in Directory.EnumerateDirectories(issuesRoot))
        {
            var unit = Path.GetFileName(unitDir);
            if (string.IsNullOrEmpty(unit))
            {
                continue;
            }

            var bodyPath = Path.Combine(unitDir, "github-body.md");
            if (!File.Exists(bodyPath))
            {
                // Not a packet directory at all — skip silently.
                continue;
            }

            if (linkedUnits.Contains(unit))
            {
                continue;
            }

            // A complete packet requires the canonical companion artifact
            // review-context.md (PacketPathResolver / IssueDraftCommand /
                // QueueEnqueueCommand). Without it, the packet is not in a
                // shape the existing issue prepare/publish-reviewed boundary
                // can deterministically consume, so we degrade to Incomplete
                // rather than reporting issue-cut-ready.
            var reviewContextPath = Path.Combine(unitDir, "review-context.md");
            if (!File.Exists(reviewContextPath))
            {
                incomplete.Add(unit);
                continue;
            }

            complete.Add((unit, bodyPath));
        }

        if (complete.Count > 1)
        {
            return new IssueCutCandidate(IssueCutCandidateKind.Ambiguous, null, null);
        }

        if (complete.Count == 1)
        {
            // Prefer a complete unlinked candidate even if other directories
            // happen to be incomplete — those are noise relative to the
            // deterministic next slice.
            var only = complete[0];
            return new IssueCutCandidate(IssueCutCandidateKind.Ready, only.Unit, only.Path);
        }

        if (incomplete.Count > 0)
        {
            // Sort for deterministic ordering across filesystems.
            incomplete.Sort(StringComparer.Ordinal);
            return new IssueCutCandidate(IssueCutCandidateKind.Incomplete, incomplete[0], null);
        }

        return new IssueCutCandidate(IssueCutCandidateKind.None, null, null);
    }

    private static HashSet<string> BuildLinkedUnitSet(QueueState? queueState)
    {
        var linked = new HashSet<string>(StringComparer.Ordinal);
        if (queueState is null)
        {
            return linked;
        }

        foreach (var item in queueState.Items)
        {
            // A queue item is treated as "already linked" when it carries either a
            // GitHub issue reference or a PR reference. Anything else (queued only,
            // no link yet) keeps its packet a candidate.
            if (item.LinkedIssue is not null
                && (item.LinkedIssue.Number is not null
                    || !string.IsNullOrWhiteSpace(item.LinkedIssue.Url)))
            {
                linked.Add(item.ExecutionUnit);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.LinkedPr))
            {
                linked.Add(item.ExecutionUnit);
            }
        }

        return linked;
    }
}
