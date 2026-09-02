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

    /// <summary>
    /// G462: test seam / optional issue-body source for the host-only publish
    /// gate. When set (or in production, the default <c>gh</c>-backed lookup),
    /// the command reads the issue body before applying <c>intent-target</c> and
    /// refuses to publish a host-only packet (all target paths host-owned) as a
    /// child implementation issue unless <c>--allow-host-only-override</c> is
    /// passed. Tests inject a fake to avoid touching GitHub.
    /// </summary>
    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }

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
                out var executionUnit,
                out var mode,
                out var format,
                out var allowHostOnlyOverride,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = WorkdirResolver.Resolve(context, workdir);
        if (!string.IsNullOrWhiteSpace(executionUnit))
        {
            if (!TryResolveIssueFromExecutionUnit(resolvedWorkdir, executionUnit, out var resolvedIssue, out error))
            {
                writer.WriteLine(error);
                return 1;
            }

            if (issue.HasValue && issue.Value != resolvedIssue)
            {
                writer.WriteLine($"--issue {issue.Value} disagrees with --execution-unit '{executionUnit}': "
                    + $"publish.yaml records created_issue_number {resolvedIssue}.");
                return 1;
            }

            issue = resolvedIssue;
        }

        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G669: the host-owned label transition is also a publish boundary.
        // A lane-declaring packet must carry both independent decision records
        // before any issue is promoted; legacy packets remain unaffected.
        if (!string.IsNullOrWhiteSpace(executionUnit))
        {
            var laneDecisionGate = BranchLaneDecisionGate.Evaluate(resolvedWorkdir, executionUnit!);
            if (!laneDecisionGate.Passed)
            {
                writer.WriteLine(laneDecisionGate.Error
                    ?? "lane decision records are incomplete; both propose and confirm records are required.");
                return 1;
            }
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

        // G462: host-only publish gate. Read the issue body and refuse to
        // publish a host-only packet (every declared target path is host-owned:
        // intents/**, .intent-cli/**) as a child intent-target issue, unless the
        // operator explicitly overrides. This stops the G458 / issue #1018
        // mis-routing class at the publish boundary. The body fetch is fail-soft:
        // a lookup error simply skips the gate (the existing behavior).
        HostOnlyPacketVerdict? hostOnlyVerdict = null;
        try
        {
            var lookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup();
            var issuePayload = lookup.Lookup(repo!, issue!.Value);
            hostOnlyVerdict = HostOnlyPacketClassifier.Classify(issuePayload.Body);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            hostOnlyVerdict = null;
        }

        if (hostOnlyVerdict is { IsHostOnly: true } && !allowHostOnlyOverride)
        {
            var refusal = new AutomationIssuePublishResult
            {
                Repo = repo!,
                Issue = issue!.Value,
                IssueUrl = BuildIssueUrl(repo!, issue.Value),
                Mode = mode,
                Applied = false,
                Refused = true,
                RefusalReason = "host-only packet: every declared target path is host/design-owned ("
                    + string.Join(", ", hostOnlyVerdict.HostOwnedPaths)
                    + "); a GitHub-contract-only child loop cannot edit host metadata, so this packet must not be published as a child intent-target issue. Retarget it to child-owned paths (src/**, tests/**, docs/**, README.md) or keep it in the host/design workflow. Pass --allow-host-only-override only if intent-target is genuinely intended here.",
                AppliedLabel = AppliedLabel,
                AddLabels = Array.Empty<string>(),
                RemoveLabels = Array.Empty<string>(),
                CurrentLabels = currentLabels,
                PublishedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Summary = $"Refused to publish issue #{issue.Value}: host-only packet (host-owned target paths only).",
            };

            if (string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                writer.WriteLine(JsonSerializer.Serialize(refusal, JsonOptions));
            }
            else
            {
                writer.WriteLine(refusal.Summary);
                writer.WriteLine($"refusal_reason: {refusal.RefusalReason}");
            }

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
            Refused = false,
            RefusalReason = null,
            AppliedLabel = AppliedLabel,
            AddLabels = [AppliedLabel],
            RemoveLabels = Array.Empty<string>(),
            CurrentLabels = currentLabels,
            PublishedAt = publishedAt,
            Summary = BuildSummary(issue.Value, AppliedLabel, mode),
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
        out string? executionUnit,
        out string mode,
        out string format,
        out bool allowHostOnlyOverride,
        out string error)
    {
        repo = null;
        workdir = null;
        issue = null;
        executionUnit = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        format = FormatText;
        allowHostOnlyOverride = false;
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

                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }
                    executionUnit = args[index + 1].Trim();
                    index++;
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
                    break;

                case "--allow-host-only-override":
                    allowHostOnlyOverride = true;
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] (--issue <n>|--execution-unit <id>) [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (issue is null && string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--issue or --execution-unit is required.";
            return false;
        }

        return true;
    }

    private static bool TryResolveIssueFromExecutionUnit(
        string workdir,
        string executionUnit,
        out int issue,
        out string error)
    {
        issue = 0;
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out error))
        {
            return false;
        }

        var relativeArtifactPath = IssuePublishArtifactPathResolver.Resolve(executionUnit);
        var artifactPath = Path.Combine(workdir, relativeArtifactPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(artifactPath))
        {
            error = $"--execution-unit '{executionUnit}' has no publish.yaml at '{relativeArtifactPath}'.";
            return false;
        }

        IssuePublishArtifact artifact;
        try
        {
            artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            error = $"--execution-unit '{executionUnit}' publish.yaml could not be read: {exception.Message}";
            return false;
        }

        if (!string.Equals(artifact.ExecutionUnit, executionUnit, StringComparison.Ordinal))
        {
            error = $"--execution-unit '{executionUnit}' does not match publish.yaml execution_unit "
                + $"'{artifact.ExecutionUnit}'.";
            return false;
        }

        if (!artifact.CreatedIssueNumber.HasValue || artifact.CreatedIssueNumber.Value <= 0)
        {
            error = $"--execution-unit '{executionUnit}' publish.yaml is missing created_issue_number.";
            return false;
        }

        issue = artifact.CreatedIssueNumber.Value;
        error = string.Empty;
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

    private static string BuildIssueUrl(string repo, int issue) =>
        $"https://github.com/{repo}/issues/{issue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private static string BuildSummary(int issue, string label, string mode)
    {
        var issueText = issue.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal)
            ? $"Published issue #{issueText} by adding {label}."
            : $"Would publish issue #{issueText} by adding {label}.";
    }

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
        writer.WriteLine("Usage: intent-cli automation issue-publish --repo <owner/repo> (--issue <n>|--execution-unit <id>) [--write] [--dry-run] [--format text|json]");
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

    /// <summary>G462: true when publishing was refused because the issue is a host-only packet.</summary>
    [JsonPropertyName("refused")]
    public bool Refused { get; init; }

    [JsonPropertyName("refusal_reason")]
    public string? RefusalReason { get; init; }

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
