using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Host-owned issue publish boundary transition. Dry-run by default; with
/// explicit <c>--write</c>, applies <c>intent-target</c> to the specified issue.
/// </summary>
internal static class AutomationIssuePublishCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string AppliedLabel = WorkerNextActionConstants.Labels.IntentTarget;

    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    public static Func<bool>? NestedProviderLauncher { get; set; }

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
            mutator = MutatorFactory?.Invoke()
                ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
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
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to read current labels for issue #{issue} in {repo}: {exception.Message}");
            return 1;
        }

        var applied = false;
        var publishedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow)
            .ToUniversalTime();

        if (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!,
                    GhCliGitHubLabelMutator.Kinds.Issue,
                    issue!.Value,
                    [AppliedLabel],
                    Array.Empty<string>());
                applied = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine($"failed to apply issue publish transition on issue #{issue} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var result = new AutomationIssuePublishResult
        {
            Repo = repo!,
            Issue = issue!.Value,
            IssueUrl = BuildIssueUrl(repo!, issue.Value),
            Mode = mode,
            Applied = applied,
            AppliedLabel = AppliedLabel,
            AddLabels = [AppliedLabel],
            RemoveLabels = Array.Empty<string>(),
            CurrentLabels = currentLabels,
            PublishedAt = publishedAt,
            Summary = BuildSummary(issue.Value, AppliedLabel),
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
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        issue = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1].Trim();
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case "--issue":
                    if (!TryReadPositiveInt(args, index, "--issue", out var issueNumber, out error))
                    {
                        return false;
                    }
                    issue = issueNumber;
                    index++;
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
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

                case "--pr":
                    error = "--pr is not supported by automation issue-publish; issue publish only targets issues.";
                    return false;

                default:
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] --issue <n> [--write] [--dry-run] [--format text|json].";
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

    private static bool TryReadPositiveInt(
        string[] args,
        int index,
        string option,
        out int value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (index + 1 >= args.Length
            || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || value <= 0)
        {
            error = $"{option} requires a positive integer.";
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

    private static string BuildSummary(int issue, string label) =>
        $"Would publish issue #{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)} by adding {label}.";

    private static void WriteText(TextWriter writer, AutomationIssuePublishResult result)
    {
        writer.WriteLine(result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"issue: {result.Issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"issue_url: {result.IssueUrl}");
        writer.WriteLine($"applied_label: {result.AppliedLabel}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"published_at: {result.PublishedAt:O}");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation issue-publish");
        writer.WriteLine("Usage: intent-cli automation issue-publish --repo <owner/repo> --issue <n> [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Publishes a child issue by applying the host-owned intent-target transition.");
    }
}

internal sealed record AutomationIssuePublishResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("issue_url")]
    public required string IssueUrl { get; init; }

    [JsonPropertyName("issueUrl")]
    public string IssueUrlCamel => IssueUrl;

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("applied_label")]
    public required string AppliedLabel { get; init; }

    [JsonPropertyName("appliedLabel")]
    public string AppliedLabelCamel => AppliedLabel;

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamel => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamel => RemoveLabels;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamel => CurrentLabels;

    [JsonPropertyName("published_at")]
    public required DateTimeOffset PublishedAt { get; init; }

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAtCamel => PublishedAt;

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
