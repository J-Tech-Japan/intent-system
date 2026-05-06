using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Installed host-side PR label transition surface for review-loop state
/// changes that previously required raw <c>gh pr edit</c> fallback blocks.
/// </summary>
internal static class AutomationPrTransitionCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string TransitionReviewStart = "review-start";
    private const string TransitionRequestUpdate = "request-update";
    private const string TransitionApproved = "approved";

    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

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
                out var pr,
                out var transition,
                out var mode,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            WriteHelp(writer);
            return 1;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var plan = PlanTransition(transition!);
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
                .ReadLabels(repo!, GhCliGitHubLabelMutator.Kinds.Pr, pr!.Value)
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to read current labels for PR #{pr} in {repo}: {exception.Message}");
            return 1;
        }

        var removeLabels = ResolveRemoveLabelsForMode(transition!, mode, plan.RemoveLabels, currentLabels);

        var applied = false;
        if (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            try
            {
                mutator.ApplyLabelTransitions(
                    repo!,
                    GhCliGitHubLabelMutator.Kinds.Pr,
                    pr!.Value,
                    plan.AddLabels,
                    removeLabels);
                applied = true;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException
                or IOException)
            {
                writer.WriteLine($"failed to apply PR transition on PR #{pr} in {repo}: {exception.Message}");
                return 1;
            }
        }

        var result = new AutomationPrTransitionResult
        {
            Repo = repo!,
            Pr = pr!.Value,
            Transition = transition!,
            Mode = mode,
            Applied = applied,
            AddLabels = plan.AddLabels,
            RemoveLabels = removeLabels,
            CurrentLabels = currentLabels,
            Summary = BuildSummary(transition!, plan.AddLabels, removeLabels),
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

    private static TransitionPlan PlanTransition(string transition) =>
        transition switch
        {
            TransitionReviewStart => new TransitionPlan(
                AddLabels:
                [
                    WorkerNextActionConstants.Labels.IntentTarget,
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                ],
                RemoveLabels:
                [
                    WorkerNextActionConstants.Labels.IntentPrRereviewReady,
                    "rereview-ready",
                ]),
            TransitionApproved => new TransitionPlan(
                AddLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrApproved,
                ],
                RemoveLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                ]),
            TransitionRequestUpdate => new TransitionPlan(
                AddLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate,
                ],
                RemoveLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, "Unsupported PR transition."),
        };

    private static IReadOnlyList<string> ResolveRemoveLabelsForMode(
        string transition,
        string mode,
        IReadOnlyList<string> plannedRemoveLabels,
        IReadOnlyList<string> currentLabels)
    {
        if (!string.Equals(transition, TransitionReviewStart, StringComparison.Ordinal)
            || !string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            return plannedRemoveLabels;
        }

        var currentLabelSet = new HashSet<string>(currentLabels, StringComparer.Ordinal);
        return plannedRemoveLabels
            .Where(label => currentLabelSet.Contains(label))
            .ToArray();
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out int? pr,
        out string? transition,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        pr = null;
        transition = null;
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

                case "--pr":
                    if (!TryReadPositiveInt(args, index, "--pr", out var prNumber, out error))
                    {
                        return false;
                    }
                    pr = prNumber;
                    index++;
                    break;

                case "--transition":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--transition requires a value (review-start, request-update, or approved).";
                        return false;
                    }
                    transition = args[index + 1].Trim();
                    if (!string.Equals(transition, TransitionReviewStart, StringComparison.Ordinal)
                        && !string.Equals(transition, TransitionRequestUpdate, StringComparison.Ordinal)
                        && !string.Equals(transition, TransitionApproved, StringComparison.Ordinal))
                    {
                        error = $"--transition must be '{TransitionReviewStart}', '{TransitionRequestUpdate}', or '{TransitionApproved}' (got '{transition}').";
                        return false;
                    }
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] --pr <n> --transition <review-start|request-update|approved> [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(transition))
        {
            error = "--transition is required (review-start, request-update, or approved).";
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

    private static string BuildSummary(
        string transition,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        var addPart = addLabels.Count == 0 ? "(none)" : string.Join(", ", addLabels);
        var removePart = removeLabels.Count == 0 ? "(none)" : string.Join(", ", removeLabels);
        return $"Would apply host PR transition '{transition}': add {addPart}; remove {removePart}.";
    }

    private static void WriteText(TextWriter writer, AutomationPrTransitionResult result)
    {
        writer.WriteLine(result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"pr: {result.Pr.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation pr-transition");
        writer.WriteLine("Usage: intent-cli automation pr-transition --repo <owner/repo> --pr <n> --transition <review-start|request-update|approved> [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Supported transitions:");
        writer.WriteLine("- review-start");
        writer.WriteLine("- request-update");
        writer.WriteLine("- approved");
    }

    private sealed record TransitionPlan(
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);
}

internal sealed record AutomationPrTransitionResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("transition")]
    public required string Transition { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

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

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
