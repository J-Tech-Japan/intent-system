using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G214: <c>intent-cli automation complete</c> is the automation-level
/// post-run entrypoint. It normalizes a worker outcome and, with explicit
/// <c>--write</c>, applies only the supported completion transition already
/// modeled by <c>worker complete</c>.
/// </summary>
internal static class AutomationCompleteCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>Test seam shared with worker complete's GitHub label mutator.</summary>
    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    /// <summary>Test sentinel: must never be invoked by this command.</summary>
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

        if (!TryParseArguments(
                args,
                out var workflowKind,
                out var repoOverride,
                out var workdir,
                out var issue,
                out var pr,
                out var outcome,
                out var mode,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        var repo = repoOverride;
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (!TryMapCompletionTarget(workflowKind!, issue, pr, out var targetKind, out var targetNumber, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (RequiresCreatedPrTarget(workflowKind!, outcome!) && pr is null)
        {
            writer.WriteLine("--pr is required when --kind is issue-to-pr and --outcome is pr-created.");
            return 1;
        }

        WorkerResultSummaryResult summary;
        try
        {
            summary = WorkerResultSummaryAnalyzer.Analyze(
                workflowKind!,
                outcome!,
                repo!,
                issue,
                pr);
        }
        catch (ArgumentException exception)
        {
            writer.WriteLine(exception.Message);
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke()
                ?? WorkerCompleteCommand.MutatorFactory?.Invoke()
                ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationLabel> currentLabels;
        try
        {
            currentLabels = mutator.ReadLabels(repo!, targetKind, targetNumber);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(
                $"failed to read current labels for {targetKind} #{targetNumber} in {repo}: {exception.Message}");
            return 1;
        }

        var currentNames = LabelNames(currentLabels);
        var decision = WorkerCompleteAnalyzer.Analyze(targetKind, outcome!, currentNames);
        var issueLabelActions = BuildPlannedActions(targetKind, decision);

        IReadOnlyList<string> createdPrCurrentLabels = Array.Empty<string>();
        var createdPrDecision = CompleteDecisionForPrTarget.None;
        if (RequiresCreatedPrTarget(workflowKind!, outcome!))
        {
            IReadOnlyList<GitHubAutomationLabel> createdPrLabels;
            try
            {
                createdPrLabels = mutator.ReadLabels(repo!, GhCliGitHubLabelMutator.Kinds.Pr, pr!.Value);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine(
                    $"failed to read current labels for created PR #{pr} in {repo}: {exception.Message}");
                return 1;
            }

            createdPrCurrentLabels = LabelNames(createdPrLabels);
            createdPrDecision = PlanCreatedPrTarget(createdPrCurrentLabels);
        }

        var applied = false;
        if (decision.Proceed
            && createdPrDecision.Proceed
            && string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!, targetKind, targetNumber, decision.AddLabels, decision.RemoveLabels);
                if (createdPrDecision.Proceed
                    && createdPrDecision.AddLabels.Count > 0
                    && pr is not null)
                {
                    mutator.ApplyLabelTransitions(
                        repo!,
                        GhCliGitHubLabelMutator.Kinds.Pr,
                        pr.Value,
                        createdPrDecision.AddLabels,
                        Array.Empty<string>());
                }
                applied = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine(
                    $"failed to apply complete transition on {targetKind} #{targetNumber} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var issueAndPrLabelActions = issueLabelActions
            .Concat(createdPrDecision.LabelActions)
            .ToArray();
        var warnings = summary.Warnings
            .Concat(decision.Warnings)
            .Concat(createdPrDecision.Warnings)
            .ToArray();
        var errors = decision.Errors
            .Concat(createdPrDecision.Errors)
            .ToArray();
        var proceed = decision.Proceed && createdPrDecision.Proceed;
        var result = new AutomationCompleteResult
        {
            Kind = workflowKind!,
            Repo = repo!,
            Issue = issue,
            Pr = pr,
            Outcome = outcome!,
            Status = summary.Status,
            Mode = mode,
            TargetKind = targetKind,
            TargetNumber = targetNumber,
            Proceed = proceed,
            Applied = applied,
            RecommendedLabelActions = summary.RecommendedLabelActions,
            PlannedLabelActions = issueAndPrLabelActions,
            AppliedLabelActions = applied ? issueAndPrLabelActions : Array.Empty<WorkerResultSummaryLabelAction>(),
            SourceIssueLabelActions = string.Equals(targetKind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
                ? issueLabelActions
                : Array.Empty<WorkerResultSummaryLabelAction>(),
            CreatedPrLabelActions = createdPrDecision.LabelActions,
            CurrentLabels = currentNames,
            CreatedPrCurrentLabels = createdPrCurrentLabels,
            Errors = errors,
            Warnings = warnings,
            Summary = $"{summary.Summary} {decision.Summary}{createdPrDecision.SummarySuffix}",
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return proceed ? 0 : 2;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? repo,
        out string? workdir,
        out int? issue,
        out int? pr,
        out string? outcome,
        out string mode,
        out string format,
        out string error)
    {
        kind = null;
        repo = null;
        workdir = null;
        issue = null;
        pr = null;
        outcome = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value (issue-to-pr or pr-comment-fix).";
                        return false;
                    }
                    kind = args[index + 1];
                    index++;
                    break;

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

                case "--pr":
                    if (!TryReadPositiveInt(args, index, "--pr", out var prNumber, out error))
                    {
                        return false;
                    }
                    pr = prNumber;
                    index++;
                    break;

                case "--outcome":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--outcome requires a value.";
                        return false;
                    }
                    outcome = args[index + 1];
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

                default:
                    error = $"Unknown argument '{argument}'. Supported: --kind <issue-to-pr|pr-comment-fix> [--repo <owner/repo>] [--workdir <path>] [--issue <n>] [--pr <n>] --outcome <outcome> [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required (issue-to-pr or pr-comment-fix).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outcome))
        {
            error = "--outcome is required.";
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

        return Path.GetFullPath(workdir);
    }

    private static bool TryMapCompletionTarget(
        string workflowKind,
        int? issue,
        int? pr,
        out string targetKind,
        out int targetNumber,
        out string error)
    {
        targetKind = string.Empty;
        targetNumber = 0;
        error = string.Empty;

        switch (workflowKind)
        {
            case WorkerResultSummaryConstants.Kinds.IssueToPr:
                if (issue is null)
                {
                    error = "--issue is required when --kind is issue-to-pr.";
                    return false;
                }
                targetKind = GhCliGitHubLabelMutator.Kinds.Issue;
                targetNumber = issue.Value;
                return true;

            case WorkerResultSummaryConstants.Kinds.PrCommentFix:
                if (pr is null)
                {
                    error = "--pr is required when --kind is pr-comment-fix.";
                    return false;
                }
                targetKind = GhCliGitHubLabelMutator.Kinds.Pr;
                targetNumber = pr.Value;
                return true;

            default:
                error = $"--kind must be 'issue-to-pr' or 'pr-comment-fix' (got '{workflowKind}').";
                return false;
        }
    }

    private static IReadOnlyList<string> LabelNames(IReadOnlyList<GitHubAutomationLabel>? labels)
    {
        if (labels is null || labels.Count == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            if (!string.IsNullOrEmpty(label.Name))
            {
                names.Add(label.Name);
            }
        }
        return names;
    }

    private static IReadOnlyList<WorkerResultSummaryLabelAction> BuildPlannedActions(
        string targetKind,
        WorkerCompleteAnalyzer.CompleteDecision decision)
    {
        if (!decision.Proceed)
        {
            return Array.Empty<WorkerResultSummaryLabelAction>();
        }

        var target = string.Equals(targetKind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
            ? WorkerResultSummaryConstants.LabelActionTargets.Issue
            : WorkerResultSummaryConstants.LabelActionTargets.Pr;
        var actions = new List<WorkerResultSummaryLabelAction>();
        foreach (var label in decision.RemoveLabels)
        {
            actions.Add(new WorkerResultSummaryLabelAction
            {
                Action = WorkerResultSummaryConstants.LabelActionVerbs.Remove,
                Target = target,
                Label = label,
            });
        }
        foreach (var label in decision.AddLabels)
        {
            actions.Add(new WorkerResultSummaryLabelAction
            {
                Action = WorkerResultSummaryConstants.LabelActionVerbs.Add,
                Target = target,
                Label = label,
            });
        }
        return actions;
    }

    private static bool RequiresCreatedPrTarget(string workflowKind, string outcome)
    {
        return string.Equals(workflowKind, WorkerResultSummaryConstants.Kinds.IssueToPr, StringComparison.Ordinal)
            && string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.PrCreated, StringComparison.Ordinal);
    }

    private static CompleteDecisionForPrTarget PlanCreatedPrTarget(IReadOnlyList<string> currentLabels)
    {
        if (currentLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
        {
            return new CompleteDecisionForPrTarget
            {
                Proceed = false,
                AddLabels = Array.Empty<string>(),
                LabelActions = Array.Empty<WorkerResultSummaryLabelAction>(),
                Errors = new[]
                {
                    $"label policy violation: PR carries '{WorkerNextActionConstants.Labels.IntentPrCreated}', which belongs on the source issue."
                },
                Warnings = Array.Empty<string>(),
                SummarySuffix = " Refusing to mark created PR as review target because it carries an issue-only completion label.",
            };
        }

        if (currentLabels.Contains(WorkerNextActionConstants.Labels.IntentTarget, StringComparer.Ordinal))
        {
            return new CompleteDecisionForPrTarget
            {
                Proceed = true,
                AddLabels = Array.Empty<string>(),
                LabelActions = Array.Empty<WorkerResultSummaryLabelAction>(),
                Errors = Array.Empty<string>(),
                Warnings = new[] { "created PR already carries 'intent-target'; no PR label add needed." },
                SummarySuffix = " Created PR already carries intent-target.",
            };
        }

        var actions = new[]
        {
            new WorkerResultSummaryLabelAction
            {
                Action = WorkerResultSummaryConstants.LabelActionVerbs.Add,
                Target = WorkerResultSummaryConstants.LabelActionTargets.Pr,
                Label = WorkerNextActionConstants.Labels.IntentTarget,
            }
        };
        return new CompleteDecisionForPrTarget
        {
            Proceed = true,
            AddLabels = new[] { WorkerNextActionConstants.Labels.IntentTarget },
            LabelActions = actions,
            Errors = Array.Empty<string>(),
            Warnings = Array.Empty<string>(),
            SummarySuffix = " Would add: intent-target on the created PR.",
        };
    }

    private static void WriteText(TextWriter writer, AutomationCompleteResult result)
    {
        writer.WriteLine($"# Automation complete for {result.Repo} ({result.Kind})");
        writer.WriteLine();
        writer.WriteLine($"- outcome: {result.Outcome}");
        writer.WriteLine($"- status: {result.Status}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- target: {result.TargetKind} #{result.TargetNumber}");
        writer.WriteLine($"- proceed: {result.Proceed}");
        writer.WriteLine($"- applied: {result.Applied}");
        writer.WriteLine();
        writer.WriteLine("## Planned label actions");
        if (result.PlannedLabelActions.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var action in result.PlannedLabelActions)
            {
                writer.WriteLine($"- {action.Action} {action.Label} on {action.Target}");
            }
        }
        writer.WriteLine();
        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
    }
}

internal sealed record AutomationCompleteResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public int? Issue { get; init; }

    [JsonPropertyName("pr")]
    public int? Pr { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("target_kind")]
    public required string TargetKind { get; init; }

    [JsonPropertyName("targetKind")]
    public string TargetKindCamelCase => TargetKind;

    [JsonPropertyName("target_number")]
    public required int TargetNumber { get; init; }

    [JsonPropertyName("targetNumber")]
    public int TargetNumberCamelCase => TargetNumber;

    [JsonPropertyName("proceed")]
    public required bool Proceed { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("recommended_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> RecommendedLabelActions { get; init; }

    [JsonPropertyName("recommendedLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> RecommendedLabelActionsCamelCase => RecommendedLabelActions;

    [JsonPropertyName("planned_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> PlannedLabelActions { get; init; }

    [JsonPropertyName("plannedLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> PlannedLabelActionsCamelCase => PlannedLabelActions;

    [JsonPropertyName("applied_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> AppliedLabelActions { get; init; }

    [JsonPropertyName("appliedLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> AppliedLabelActionsCamelCase => AppliedLabelActions;

    [JsonPropertyName("source_issue_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> SourceIssueLabelActions { get; init; }

    [JsonPropertyName("sourceIssueLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> SourceIssueLabelActionsCamelCase => SourceIssueLabelActions;

    [JsonPropertyName("created_pr_label_actions")]
    public required IReadOnlyList<WorkerResultSummaryLabelAction> CreatedPrLabelActions { get; init; }

    [JsonPropertyName("createdPrLabelActions")]
    public IReadOnlyList<WorkerResultSummaryLabelAction> CreatedPrLabelActionsCamelCase => CreatedPrLabelActions;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamelCase => CurrentLabels;

    [JsonPropertyName("created_pr_current_labels")]
    public required IReadOnlyList<string> CreatedPrCurrentLabels { get; init; }

    [JsonPropertyName("createdPrCurrentLabels")]
    public IReadOnlyList<string> CreatedPrCurrentLabelsCamelCase => CreatedPrCurrentLabels;

    [JsonPropertyName("errors")]
    public required IReadOnlyList<string> Errors { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}

internal sealed record CompleteDecisionForPrTarget
{
    public static CompleteDecisionForPrTarget None { get; } = new()
    {
        Proceed = true,
        AddLabels = Array.Empty<string>(),
        LabelActions = Array.Empty<WorkerResultSummaryLabelAction>(),
        Errors = Array.Empty<string>(),
        Warnings = Array.Empty<string>(),
        SummarySuffix = string.Empty,
    };

    public required bool Proceed { get; init; }

    public required IReadOnlyList<string> AddLabels { get; init; }

    public required IReadOnlyList<WorkerResultSummaryLabelAction> LabelActions { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public required string SummarySuffix { get; init; }
}
