using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G462: host-owned issue RELEASE transition — the safe counterpart to
/// <see cref="AutomationIssuePublishCommand"/>. Removes <c>intent-target</c>
/// from an issue that was mistakenly published as a child implementation target
/// (e.g. a host-only packet, the G458 / issue #1018 regression), and records
/// why, WITHOUT raw <c>gh ... edit --remove-label</c>. Dry-run by default; with
/// explicit <c>--write</c>, removes <c>intent-target</c> via the installed label
/// mutator. Never adds labels, never edits parent durable state, never launches
/// an AI provider.
/// </summary>
internal static class AutomationIssueReleaseCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string ReleasedLabel = WorkerNextActionConstants.Labels.IntentTarget;

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

        if (!TryParseArguments(
                args,
                out var repo,
                out var workdir,
                out var issue,
                out var reason,
                out var mode,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
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

        var hasTarget = currentLabels.Contains(ReleasedLabel, StringComparer.Ordinal);
        var applied = false;
        var releasedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

        if (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal) && hasTarget)
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!,
                    GhCliGitHubLabelMutator.Kinds.Issue,
                    issue!.Value,
                    Array.Empty<string>(),
                    [ReleasedLabel]);
                applied = true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                writer.WriteLine($"failed to apply issue release transition on issue #{issue} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var result = new AutomationIssueReleaseResult
        {
            Repo = repo!,
            Issue = issue!.Value,
            IssueUrl = BuildIssueUrl(repo!, issue.Value),
            Mode = mode,
            Applied = applied,
            ReleasedLabel = ReleasedLabel,
            HadTargetLabel = hasTarget,
            Reason = reason,
            AddLabels = Array.Empty<string>(),
            RemoveLabels = [ReleasedLabel],
            CurrentLabels = currentLabels,
            ReleasedAt = releasedAt,
            Summary = hasTarget
                ? $"Would release issue #{issue.Value} by removing {ReleasedLabel}"
                    + (string.IsNullOrWhiteSpace(reason) ? "." : $" (reason: {reason}).")
                : $"Issue #{issue.Value} does not carry {ReleasedLabel}; nothing to release.",
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

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out int? issue,
        out string? reason,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        issue = null;
        reason = null;
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
                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { error = "--workdir requires a value."; return false; }
                    workdir = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] --issue <n> [--reason <text>] [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (issue is null)
        {
            error = "--issue is required.";
            return false;
        }

        return true;
    }

    private static string ResolveWorkdir(CliContext context, string? workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
        {
            return context.RepoRoot;
        }
        return Path.IsPathRooted(workdir)
            ? workdir
            : Path.GetFullPath(Path.Combine(context.RepoRoot, workdir));
    }

    private static string BuildIssueUrl(string repo, int issue) =>
        $"https://github.com/{repo}/issues/{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static void WriteText(TextWriter writer, AutomationIssueReleaseResult result)
    {
        writer.WriteLine(result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"issue: {result.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"issue_url: {result.IssueUrl}");
        writer.WriteLine($"released_label: {result.ReleasedLabel}");
        writer.WriteLine($"had_target_label: {result.HadTargetLabel.ToString().ToLowerInvariant()}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
        if (!string.IsNullOrWhiteSpace(result.Reason))
        {
            writer.WriteLine($"reason: {result.Reason}");
        }
        writer.WriteLine($"released_at: {result.ReleasedAt:O}");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation issue-release");
        writer.WriteLine("Usage: intent-cli automation issue-release --repo <owner/repo> --issue <n> [--reason <text>] [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Releases an issue that was mistakenly published as a child target by removing the host-owned intent-target label (G462). The safe counterpart to automation issue-publish; never uses raw gh label mutation.");
    }
}

internal sealed record AutomationIssueReleaseResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("issue_url")]
    public required string IssueUrl { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("released_label")]
    public required string ReleasedLabel { get; init; }

    [JsonPropertyName("had_target_label")]
    public required bool HadTargetLabel { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("released_at")]
    public required DateTimeOffset ReleasedAt { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
