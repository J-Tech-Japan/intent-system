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
        var (candidateUnit, candidatePath, ambiguous) = FindIssueCutCandidate(context, queueState);
        if (ambiguous)
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

        if (candidateUnit is not null && candidatePath is not null)
        {
            return new NextSliceClassifyResult
            {
                Domain = domain,
                Classification = NextSliceClassification.IssueCutReady,
                Rationale = $"issue packet for {candidateUnit} is present and not yet linked in queue-state.",
                WipRefs = Array.Empty<string>(),
                ClarificationSummary = null,
                CandidateExecutionUnit = candidateUnit,
                CandidatePacketPath = candidatePath,
                RecommendedNextAction = $"Publish reviewed issue for {candidateUnit}.",
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

    private static (string? Unit, string? Path, bool Ambiguous) FindIssueCutCandidate(
        CliContext context,
        QueueState? queueState)
    {
        var issuesRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesRoot))
        {
            return (null, null, false);
        }

        var linkedUnits = BuildLinkedUnitSet(queueState);

        var candidates = new List<(string Unit, string Path)>();
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
                continue;
            }

            if (linkedUnits.Contains(unit))
            {
                continue;
            }

            candidates.Add((unit, bodyPath));
        }

        if (candidates.Count == 0)
        {
            return (null, null, false);
        }

        if (candidates.Count > 1)
        {
            return (null, null, true);
        }

        var only = candidates[0];
        return (only.Unit, only.Path, false);
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
