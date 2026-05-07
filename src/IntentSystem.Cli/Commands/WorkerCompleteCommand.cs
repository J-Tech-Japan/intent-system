using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: <c>intent-cli worker complete</c> command. Applies (or in
/// dry-run mode, describes) the completion label transition for a
/// selected issue/PR target after a worker run finished. The
/// transition mirrors the recommended_label_actions emitted by
/// <see cref="WorkerResultSummaryAnalyzer"/>, with stale-state
/// pre-checks added at this writer boundary.
///
/// No-mutation invariants (verified by tests):
/// - dry-run mode (default) NEVER calls
///   <see cref="IGitHubLabelMutator.ApplyLabelTransitions"/>;
/// - this command and its analyzer file contain no
///   <c>Process.Start</c>, no <c>gh</c> mutation literals, and never
///   invoke <see cref="NestedProviderLauncher"/>;
/// - whole-workspace byte-snapshot before/after asserts no file is
///   created or modified;
/// - the only file in the worker claim/complete surface that calls
///   <c>Process.Start</c> is the mutator adapter
///   <see cref="GhCliGitHubLabelMutator"/>, by design.
/// </summary>
internal static class WorkerCompleteCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubLabelMutator"/>
    /// here so no real GitHub network call is made.
    /// </summary>
    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all command paths.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(
                args,
                out var repo,
                out var kind,
                out var number,
                out var outcome,
                out var prNumber,
                out var mode,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G283: issue-to-pr success requires the created PR number so the
        // command can publish it to host review (PR-side intent-target) and
        // sync parent linked_pr metadata. Refuse rather than silently
        // succeed without publishing the PR.
        var isPrCreatedOutcome = string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
            && string.Equals(outcome, WorkerResultSummaryConstants.Outcomes.PrCreated, StringComparison.Ordinal);
        if (isPrCreatedOutcome && prNumber is null)
        {
            writer.WriteLine("--pr is required when --kind issue --outcome pr-created (created PR number publishes intent-target to host review).");
            return 1;
        }

        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
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
            currentLabels = mutator.ReadLabels(repo!, kind!, number);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(
                $"failed to read current labels for {kind} #{number} in {repo}: {exception.Message}");
            return 1;
        }

        var currentNames = LabelNames(currentLabels);
        var decision = WorkerCompleteAnalyzer.Analyze(kind!, outcome!, currentNames);

        var applied = false;
        bool? prTargetApplied = null;
        bool? linkedPrSynced = null;
        var publishWarnings = new List<string>();
        var publishErrors = new List<string>();
        if (decision.Proceed
            && string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!, kind!, number, decision.AddLabels, decision.RemoveLabels);
                applied = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine(
                    $"failed to apply complete transition on {kind} #{number} in {repo}: {exception.Message}");
                return 1;
            }

            // G283: pr-created on an issue must also publish PR-side intent-target
            // and sync parent queue-state linked_pr so host review automation can
            // pick up the PR. The created-PR write happens only after the
            // issue-side mutation succeeded; if the PR write fails, surface
            // an error and refuse to claim full completion.
            if (isPrCreatedOutcome && prNumber is { } prNumberValue)
            {
                try
                {
                    mutator.ApplyLabelTransitions(
                        repo!,
                        GhCliGitHubLabelMutator.Kinds.Pr,
                        prNumberValue,
                        new[] { WorkerNextActionConstants.Labels.IntentTarget },
                        Array.Empty<string>());
                    prTargetApplied = true;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or IOException)
                {
                    prTargetApplied = false;
                    publishErrors.Add(
                        $"failed to apply '{WorkerNextActionConstants.Labels.IntentTarget}' to PR #{prNumberValue} in {repo}: {exception.Message}. Source issue completion was applied but PR review publication is incomplete; reconcile via 'intent-cli automation reconcile' before re-running.");
                }

                if (prTargetApplied == true)
                {
                    var (synced, warning) = TryPatchQueueStateLinkedPr(context, number, repo!, prNumberValue);
                    linkedPrSynced = synced;
                    if (!string.IsNullOrWhiteSpace(warning))
                    {
                        publishWarnings.Add(warning);
                    }
                }
            }
        }

        var summary = decision.Summary;
        var proceed = decision.Proceed;
        var aggregatedErrors = decision.Errors.Concat(publishErrors).ToArray();
        var aggregatedWarnings = decision.Warnings.Concat(publishWarnings).ToArray();
        if (publishErrors.Count > 0)
        {
            proceed = false;
            applied = false;
            summary = $"Issue-to-PR completion partially applied: source issue updated, but PR review publication failed for PR #{prNumber}.";
        }

        var result = new WorkerCompleteResult
        {
            Kind = kind!,
            Repo = repo!,
            Number = number,
            Outcome = outcome!,
            Mode = mode,
            Proceed = proceed,
            Applied = applied,
            AddLabels = decision.AddLabels,
            RemoveLabels = decision.RemoveLabels,
            CurrentLabels = currentNames,
            Errors = aggregatedErrors,
            Warnings = aggregatedWarnings,
            Summary = summary,
            PrNumber = prNumber,
            PrTargetApplied = prTargetApplied,
            LinkedPrSynced = linkedPrSynced,
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

    /// <summary>
    /// G283: sync the matching execution-unit's <c>linked_pr</c> in
    /// <c>.intent-cli/queue-state.json</c> when an issue→PR completion
    /// can be deterministically resolved (queue item's
    /// <c>linked_issue.number</c> matches the source issue number).
    /// Returns <c>(synced, warning)</c>; <c>warning</c> is non-empty when
    /// the file is missing or no item matches, so the operator can tell
    /// PR-side label success apart from queue-state drift.
    /// </summary>
    private static (bool synced, string? warning) TryPatchQueueStateLinkedPr(
        CliContext context,
        int sourceIssueNumber,
        string repo,
        int prNumber)
    {
        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            return (false, $"queue-state.json not found at {queueStatePath}; PR-side intent-target was applied but linked_pr could not be synced. Reconcile via 'intent-cli automation reconcile' if this host owns the parent durable state.");
        }

        try
        {
            var raw = File.ReadAllText(queueStatePath);
            var queueState = QueueStateSerializer.Deserialize(raw);
            var matchedIndex = -1;
            for (var index = 0; index < queueState.Items.Count; index++)
            {
                var linkedIssue = queueState.Items[index].LinkedIssue;
                if (linkedIssue is { Number: { } linkedNumber } && linkedNumber == sourceIssueNumber)
                {
                    matchedIndex = index;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                return (false, $"queue-state.json has no item with linked_issue.number={sourceIssueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}; PR-side intent-target was applied but linked_pr could not be synced.");
            }

            var existingItem = queueState.Items[matchedIndex];
            var prUrl = $"https://github.com/{repo}/pull/{prNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (string.Equals(existingItem.LinkedPr, prUrl, StringComparison.Ordinal))
            {
                return (true, null);
            }

            var updatedItem = existingItem with { LinkedPr = prUrl };
            var newItems = queueState.Items.ToArray();
            newItems[matchedIndex] = updatedItem;
            var updatedState = queueState with
            {
                Items = newItems,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));
            return (true, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return (false, $"failed to sync linked_pr for issue #{sourceIssueNumber} in queue-state.json: {exception.Message}");
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

    private static void WriteText(TextWriter writer, WorkerCompleteResult result)
    {
        writer.WriteLine($"# Worker complete: {result.Kind} #{result.Number} ({result.Repo})");
        writer.WriteLine();
        writer.WriteLine($"- outcome: {result.Outcome}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- proceed: {result.Proceed}");
        writer.WriteLine($"- applied: {result.Applied}");
        writer.WriteLine($"- add: {(result.AddLabels.Count == 0 ? "(none)" : string.Join(", ", result.AddLabels))}");
        writer.WriteLine($"- remove: {(result.RemoveLabels.Count == 0 ? "(none)" : string.Join(", ", result.RemoveLabels))}");
        writer.WriteLine($"- summary: {result.Summary}");
        writer.WriteLine();

        writer.WriteLine("## Errors");
        if (result.Errors.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var item in result.Errors)
            {
                writer.WriteLine($"- {item}");
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
            foreach (var item in result.Warnings)
            {
                writer.WriteLine($"- {item}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? kind,
        out int number,
        out string? outcome,
        out int? prNumber,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        kind = null;
        number = 0;
        outcome = null;
        prNumber = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsedPr)
                        || parsedPr <= 0)
                    {
                        error = "--pr requires a positive integer.";
                        return false;
                    }
                    prNumber = parsedPr;
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value ('issue' or 'pr').";
                        return false;
                    }
                    kind = args[index + 1];
                    index++;
                    break;

                case "--number":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out number)
                        || number <= 0)
                    {
                        error = "--number requires a positive integer.";
                        return false;
                    }
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
                    error =
                        $"Unknown argument '{argument}'. Supported: --repo <owner/repo> --kind <issue|pr> --number <N> --outcome <outcome> [--pr <N>] [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required ('issue' or 'pr').";
            return false;
        }
        if (!string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal))
        {
            error = $"--kind must be 'issue' or 'pr' (got '{kind}').";
            return false;
        }
        if (number <= 0)
        {
            error = "--number is required and must be a positive integer.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(outcome))
        {
            error = "--outcome is required.";
            return false;
        }

        return true;
    }
}
