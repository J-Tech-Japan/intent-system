using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G800's task-kind contract.  Research is deliberately a contract and
/// measurement surface, not a readiness gate: a judgement seat may always
/// read for itself through the ordinary notify paths.
/// </summary>
internal static class ResearchDelegationContract
{
    public const string TaskKind = "research";

    public static bool IsResearch(string? taskKind) =>
        string.Equals(taskKind, TaskKind, StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizePair(
        string? fromRole,
        string? toRole,
        out string? canonicalFrom,
        out string? canonicalTo,
        out string error)
    {
        canonicalFrom = null;
        canonicalTo = null;
        if (!LogicalRoleNormalizer.TryNormalize(fromRole, out canonicalFrom, out error)
            || canonicalFrom is null)
        {
            return false;
        }

        if (canonicalFrom is not (LogicalRoleNormalizer.Architect or LogicalRoleNormalizer.Reviewer))
        {
            error = $"research delegation sender must be Architect or Reviewer, not '{fromRole}'.";
            return false;
        }

        if (!LogicalRoleNormalizer.TryNormalize(toRole, out canonicalTo, out error)
            || canonicalTo is null)
        {
            return false;
        }

        if (canonicalTo is not (LogicalRoleNormalizer.Orchestrator or LogicalRoleNormalizer.Steward))
        {
            error = $"research delegation recipient must be Orchestrator or Steward, not '{toRole}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryBuildFindings(
        IReadOnlyList<string> findings,
        IReadOnlyList<string> sources,
        out IReadOnlyList<NotifyResearchFinding> sourcedFindings,
        out string error)
    {
        sourcedFindings = [];
        if (findings.Count == 0)
        {
            error = "research report requires at least one finding with a matching source; sourced findings are required.";
            return false;
        }

        if (findings.Count != sources.Count)
        {
            var firstUnpairedIndex = Math.Min(findings.Count, sources.Count) + 1;
            error = findings.Count > sources.Count
                ? $"research finding {firstUnpairedIndex} has no matching source (findings={findings.Count}, sources={sources.Count})."
                : $"research source {firstUnpairedIndex} has no matching finding (findings={findings.Count}, sources={sources.Count}).";
            return false;
        }

        var result = new List<NotifyResearchFinding>(findings.Count);
        for (var index = 0; index < findings.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(findings[index]) || string.IsNullOrWhiteSpace(sources[index]))
            {
                error = $"research finding {index + 1} must carry a source with file+symbol, command+output, or URL.";
                return false;
            }

            if (!IsSourceBearing(sources[index]))
            {
                error = $"research finding {index + 1} source is not source-bearing; use file+symbol, command+output, or URL.";
                return false;
            }

            result.Add(new NotifyResearchFinding
            {
                Finding = findings[index].Trim(),
                Source = sources[index].Trim(),
            });
        }

        sourcedFindings = result;
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Keep the source requirement structural rather than treating arbitrary
    /// prose as evidence.  The accepted forms deliberately mirror the
    /// contract: an absolute HTTP(S) URL, labelled file/symbol values, or
    /// labelled command/output values.  Labels make a pasted source
    /// attributable without coupling the contract to a particular tool.
    /// </summary>
    private static bool IsSourceBearing(string source)
    {
        var value = source.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri is not null
            && (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return true;
        }

        if (value.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            var url = value[4..].Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out var labelledUri)
                && labelledUri is not null
                && (string.Equals(labelledUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(labelledUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(labelledUri.Host))
            {
                return true;
            }
        }

        return HasLabelWithValue(value, "file")
            && HasLabelWithValue(value, "symbol")
            || HasLabelWithValue(value, "command")
            && HasLabelWithValue(value, "output");
    }

    private static bool HasLabelWithValue(string value, string label)
    {
        var offset = 0;
        while (offset < value.Length)
        {
            var index = value.IndexOf(label, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var beforeIsBoundary = index == 0
                || !char.IsLetterOrDigit(value[index - 1]) && value[index - 1] is not '_' and not '-';
            var afterLabel = index + label.Length;
            var afterIsBoundary = afterLabel >= value.Length
                || !char.IsLetterOrDigit(value[afterLabel]) && value[afterLabel] is not '_' and not '-';
            if (beforeIsBoundary && afterIsBoundary)
            {
                var cursor = afterLabel;
                while (cursor < value.Length && char.IsWhiteSpace(value[cursor])) cursor++;
                if (cursor < value.Length && value[cursor] is ':' or '=') cursor++;
                while (cursor < value.Length && char.IsWhiteSpace(value[cursor])) cursor++;
                if (cursor < value.Length && value[cursor] is not ';' and not ',')
                {
                    return true;
                }
            }

            offset = afterLabel;
        }

        return false;
    }

    public static bool TryValidateReport(
        IReadOnlyList<string> findings,
        IReadOnlyList<string> sources,
        string? rulingPayload,
        string? rulingOrigin,
        string? rulingDigest,
        string? judgementSeat,
        out IReadOnlyList<NotifyResearchFinding> sourcedFindings,
        out string error)
    {
        sourcedFindings = [];
        if (rulingPayload is not null || rulingOrigin is not null || rulingDigest is not null)
        {
            var seat = DisplayJudgementSeat(judgementSeat);
            error = $"research-ruling-refused: a research report may carry findings only; the {seat} must supply the ruling.";
            return false;
        }

        return TryBuildFindings(findings, sources, out sourcedFindings, out error);
    }

    public static string DisplayJudgementSeat(string? role) =>
        LogicalRoleNormalizer.TryNormalize(role, out var canonical, out _)
            && canonical is not null
            ? canonical == LogicalRoleNormalizer.Reviewer ? "Reviewer" : "Architect"
            : "Architect or Reviewer";

    public static ResearchDelegationGuidance CreateGuidance() => new()
    {
        TaskKind = TaskKind,
        SenderRoles = [LogicalRoleNormalizer.Architect, LogicalRoleNormalizer.Reviewer],
        RecipientRoles = [LogicalRoleNormalizer.Orchestrator, LogicalRoleNormalizer.Steward],
        WhatGoesDown = "The research question, the expected artifact, and the requested context go down as a task-kind research delegation.",
        WhoReceives = "An Architect or Reviewer may send it to the Orchestrator or Steward; all four sender/recipient pairs are routable.",
        WhatStays = "The judgement seat keeps the ruling responsibility; the recipient returns findings, each paired with its source.",
        SourcedFindingRule = "Every finding names a source such as a file and symbol, a command and output, or a URL.",
        NoRulingBoundary = "A research report carrying the ruling payload is refused and names the Architect or Reviewer that must rule.",
        DirectResearchRule = "Direct Architect and Reviewer research remains an ordinary, successful path; it is never refused or turned into a failure warning.",
        VisibilityRule = "The visibility surface reports counts of issued research delegations and judgement-seat turns without a delegation; counts are descriptive, not a grade.",
        NoSizeRule = "No count of files, call sites, commands, or tokens changes the behavior; the seat chooses by kind and example.",
        Examples =
        [
            "architect -> orchestrator: inventory the affected symbols; expected artifact: sourced inventory.",
            "architect -> steward: check the recorded surface; expected artifact: sourced check notes.",
            "reviewer -> orchestrator: gather the regression evidence; expected artifact: sourced evidence table.",
            "reviewer -> steward: collect the compatibility facts; expected artifact: sourced compatibility notes.",
        ],
    };

    public static NotifyResearchMetrics Measure(
        string routingRoot,
        string domain,
        string team,
        out string? error)
    {
        var pending = NotifyPendingDelegationStore.ReadAll(routingRoot, domain, team, out error);
        if (error is not null)
        {
            return new NotifyResearchMetrics();
        }

        var outbox = NotifyReportOutboxStore.ReadAll(routingRoot, domain, team, out var outboxError);
        if (outboxError is not null)
        {
            error = outboxError;
            return new NotifyResearchMetrics();
        }

        var issued = pending.Count(record => IsResearch(record.TaskKind) && record.DirectResearch != true);
        var directTaskIds = pending
            .Where(record => record.DirectResearch == true)
            .Select(record => record.TaskId)
            .ToHashSet(StringComparer.Ordinal);
        directTaskIds.UnionWith(outbox
            .Where(entry => entry.DirectResearch == true)
            .Select(entry => entry.TaskId));

        error = null;
        return new NotifyResearchMetrics
        {
            ResearchDelegationsIssued = issued,
            JudgementSeatTurnsWithoutDelegation = directTaskIds.Count,
        };
    }
}

internal sealed record NotifyResearchFinding
{
    [JsonPropertyName("finding")] public required string Finding { get; init; }
    [JsonPropertyName("source")] public required string Source { get; init; }
}

internal sealed record ResearchDelegationGuidance
{
    [JsonPropertyName("task_kind")] public required string TaskKind { get; init; }
    [JsonPropertyName("sender_roles")] public required IReadOnlyList<string> SenderRoles { get; init; }
    [JsonPropertyName("recipient_roles")] public required IReadOnlyList<string> RecipientRoles { get; init; }
    [JsonPropertyName("what_goes_down")] public required string WhatGoesDown { get; init; }
    [JsonPropertyName("who_receives")] public required string WhoReceives { get; init; }
    [JsonPropertyName("what_stays")] public required string WhatStays { get; init; }
    [JsonPropertyName("sourced_finding_rule")] public required string SourcedFindingRule { get; init; }
    [JsonPropertyName("no_ruling_boundary")] public required string NoRulingBoundary { get; init; }
    [JsonPropertyName("direct_research_rule")] public required string DirectResearchRule { get; init; }
    [JsonPropertyName("visibility_rule")] public required string VisibilityRule { get; init; }
    [JsonPropertyName("no_size_rule")] public required string NoSizeRule { get; init; }
    [JsonPropertyName("examples")] public required IReadOnlyList<string> Examples { get; init; }
}

internal sealed record NotifyResearchMetrics
{
    [JsonPropertyName("research_delegations_issued")] public int ResearchDelegationsIssued { get; init; }
    [JsonPropertyName("judgement_seat_turns_without_delegation")] public int JudgementSeatTurnsWithoutDelegation { get; init; }
}

internal sealed record NotifyResearchStatusResult
{
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("routing_root")] public required string RoutingRoot { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("research_delegations_issued")] public int ResearchDelegationsIssued { get; init; }
    [JsonPropertyName("judgement_seat_turns_without_delegation")] public int JudgementSeatTurnsWithoutDelegation { get; init; }
    [JsonPropertyName("cause")] public string? Cause { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}
