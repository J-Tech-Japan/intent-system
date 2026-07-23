using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G545: the canonical issue-level mirror of queue-state's <c>state=blocked</c>
/// — <c>intent-cli automation issue-block --repo &lt;owner/repo&gt; --issue &lt;n&gt;
/// --reason &lt;text&gt; [--write] [--dry-run] [--format text|json]</c> applies
/// <see cref="WorkerNextActionConstants.Labels.IntentIssueBlocked"/>, recording
/// the queue's own <c>blocked_by</c> reason text; the counterpart <c>--clear</c>
/// removes it once the unit is unblocked. Coexists with
/// <see cref="WorkerNextActionConstants.Labels.IntentIssueInProgress"/> rather
/// than replacing it — the worker still owns the issue, it just cannot
/// currently proceed. Dry-run by default; WITHOUT raw <c>gh ... edit
/// --add-label</c>/<c>--remove-label</c>, mutating labels only through the
/// installed <see cref="IGitHubLabelMutator"/>, exactly like the existing
/// <see cref="AutomationIssueReleaseCommand"/>. Never edits queue-state or
/// runs.jsonl, and never launches an AI provider — this command only makes
/// GitHub agree with a `blocked` transition that already happened via
/// <c>queue transition &lt;unit&gt; blocked --reason &lt;text&gt;</c>.
/// </summary>
internal static class AutomationIssueBlockCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string BlockedLabel = WorkerNextActionConstants.Labels.IntentIssueBlocked;

    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var repo, out var issue, out var reason, out var clear, out var mode, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<string> currentLabels;
        try
        {
            currentLabels = mutator
                .ReadLabels(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value)
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to read current labels for issue #{issue} in {repo}: {exception.Message}");
            return 1;
        }

        var hasBlockedLabel = currentLabels.Contains(BlockedLabel, StringComparer.Ordinal);
        var applied = false;
        var transitionAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var isWrite = string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal);

        if (clear)
        {
            if (isWrite && hasBlockedLabel)
            {
                try
                {
                    mutator.ApplyLabelTransitions(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value, Array.Empty<string>(), [BlockedLabel]);
                    applied = true;
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    writer.WriteLine($"failed to clear the blocked transition on issue #{issue} in {repo}: {exception.Message}");
                    return 1;
                }
            }
        }
        else if (isWrite && !hasBlockedLabel)
        {
            try
            {
                mutator.ApplyLabelTransitions(repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value, [BlockedLabel], Array.Empty<string>());
                applied = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine($"failed to apply the blocked transition on issue #{issue} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var result = new AutomationIssueBlockResult
        {
            Repo = repo!,
            Issue = issue!.Value,
            IssueUrl = BuildIssueUrl(repo!, issue.Value),
            Mode = mode,
            Clear = clear,
            Applied = applied,
            BlockedLabel = BlockedLabel,
            HadBlockedLabel = hasBlockedLabel,
            Reason = reason,
            AddLabels = clear ? Array.Empty<string>() : [BlockedLabel],
            RemoveLabels = clear ? [BlockedLabel] : Array.Empty<string>(),
            CurrentLabels = currentLabels,
            TransitionedAt = transitionAt,
            Summary = BuildSummary(issue.Value, clear, hasBlockedLabel, reason),
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static string BuildSummary(int issue, bool clear, bool hasBlockedLabel, string? reason)
    {
        if (clear)
        {
            return hasBlockedLabel
                ? $"Would clear the blocked transition on issue #{issue} by removing {BlockedLabel}."
                : $"Issue #{issue} does not carry {BlockedLabel}; nothing to clear.";
        }

        return hasBlockedLabel
            ? $"Issue #{issue} already carries {BlockedLabel}; nothing to apply."
            : $"Would apply the blocked transition on issue #{issue} by adding {BlockedLabel}"
                + (string.IsNullOrWhiteSpace(reason) ? "." : $" (reason: {reason}).");
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? issue,
        out string? reason,
        out bool clear,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        issue = null;
        reason = null;
        clear = false;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--repo requires a value (e.g. owner/repo)."; return false; }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueNumber)
                        || issueNumber <= 0)
                    {
                        error = "--issue requires a positive integer.";
                        return false;
                    }
                    issue = issueNumber;
                    index++;
                    break;
                case "--reason":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--reason requires a value."; return false; }
                    reason = args[index + 1].Trim();
                    index++;
                    break;
                case "--clear":
                    clear = true;
                    break;
                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;
                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--format requires a value (text or json)."; return false; }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;
                default:
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> --issue <n> [--reason <text>] [--clear] [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        if (issue is null)
        {
            error = "--issue is required.";
            return false;
        }

        if (clear && !string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is only supported when applying the blocked transition, not with --clear.";
            return false;
        }

        if (!clear && string.IsNullOrWhiteSpace(reason))
        {
            error = "--reason is required unless --clear is given — a blocked transition without a recorded reason is not permitted.";
            return false;
        }

        return true;
    }

    private static string BuildIssueUrl(string repo, int issue) =>
        $"https://github.com/{repo}/issues/{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void WriteText(TextWriter writer, AutomationIssueBlockResult result)
    {
        writer.WriteLine(result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"issue: {result.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"issue_url: {result.IssueUrl}");
        writer.WriteLine($"clear: {result.Clear.ToString().ToLowerInvariant()}");
        writer.WriteLine($"blocked_label: {result.BlockedLabel}");
        writer.WriteLine($"had_blocked_label: {result.HadBlockedLabel.ToString().ToLowerInvariant()}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            writer.WriteLine($"reason: {result.Reason}");
        }
        writer.WriteLine($"transitioned_at: {result.TransitionedAt:O}");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation issue-block");
        writer.WriteLine("Usage: intent-cli automation issue-block --repo <owner/repo> --issue <n> --reason <text> [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("       intent-cli automation issue-block --repo <owner/repo> --issue <n> --clear [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Applies (or, with --clear, removes) the issue-level intent-issue-blocked label (G545) so GitHub");
        writer.WriteLine("agrees with a queue-state `blocked` transition. Coexists with intent-issue-in-progress; never");
        writer.WriteLine("uses raw gh label mutation.");
    }
}

internal sealed record AutomationIssueBlockResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("issue_url")]
    public required string IssueUrl { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("clear")]
    public required bool Clear { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("blocked_label")]
    public required string BlockedLabel { get; init; }

    [JsonPropertyName("had_blocked_label")]
    public required bool HadBlockedLabel { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("transitioned_at")]
    public required DateTimeOffset TransitionedAt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
