using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Read-only host review/next-slice diagnostic. Differentiates true
/// idle from stale-CLI, stuck review label, missing target on a PR,
/// conflicting review-side labels, WIP-cap blockage, and
/// clarification-required so an operator running the host loop can tell
/// why the loop did not advance. Never mutates GitHub, never applies
/// labels, never touches durable parent state, and never launches an AI
/// provider. Pairs with <c>automation host-review-preflight</c> (which
/// selects a target) and <c>automation reconcile</c> (which actually
/// repairs label drift); this command only classifies and recommends.
/// </summary>
internal static class AutomationHostReviewDiagnosticsCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

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
                out var candidate,
                out var clarificationRequired,
                out var staleClarificationMetadata,
                out var reconcileUnsafeStopKinds,
                out var reconcileRepairsAvailable,
                out var publishRecoveryRepairsAvailable,
                out var allowWipCapOverride,
                out var prDraft,
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

        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        if (!surfaceReport.Available)
        {
            var missingSurfaces = surfaceReport.Checks
                .Where(check => !check.Available)
                .Select(check => string.IsNullOrWhiteSpace(check.Transition)
                    ? check.Command
                    : $"{check.Command} --transition {check.Transition}")
                .ToArray();
            var stale = new AutomationHostReviewDiagnosticsResult
            {
                Repo = repo!,
                Classification = AutomationHostReviewDiagnosticsClassifications.StaleHostCli,
                Summary = $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation command surfaces ({string.Join("; ", missingSurfaces)}); refresh the installed CLI before running the host loop.",
                ReadOnly = true,
                RecommendedNextCommand = "intent-cli automation doctor --format json",
                StructuredClarification = null,
                Details =
                [
                    new AutomationHostReviewDiagnosticsDetail
                    {
                        Kind = AutomationHostReviewDiagnosticsClassifications.StaleHostCli,
                        TargetKind = null,
                        TargetNumber = null,
                        TargetUrl = null,
                        Description = $"installed_cli_path: {surfaceReport.InstalledCliPath}; missing_surfaces: {string.Join(", ", missingSurfaces)}",
                    }
                ],
                Warnings = staleClarificationMetadata
                    ? new[] { "stale-clarification-metadata" }
                    : Array.Empty<string>(),
            };
            Emit(writer, stale, format);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIssues;
        try
        {
            openPrs = lister.ListPullRequests(repo!, Array.Empty<string>());
            publishedIssues = lister.ListIssues(
                repo!,
                [WorkerNextActionConstants.Labels.IntentTarget]);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to list diagnostic candidates for {repo}: {exception.Message}");
            return 1;
        }

        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo!,
            openPrs,
            publishedIssues,
            clarificationRequired,
            candidate,
            staleClarificationMetadata,
            reconcileUnsafeStopKinds,
            reconcileRepairsAvailable,
            allowWipCapOverride,
            prDraft,
            publishRecoveryRepairsAvailable);

        Emit(writer, result, format);
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string? candidate,
        out bool clarificationRequired,
        out bool staleClarificationMetadata,
        out IReadOnlyList<string> reconcileUnsafeStopKinds,
        out int reconcileRepairsAvailable,
        out int publishRecoveryRepairsAvailable,
        out bool allowWipCapOverride,
        out bool? prDraft,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        candidate = null;
        clarificationRequired = false;
        staleClarificationMetadata = false;
        var unsafeStops = new List<string>();
        reconcileUnsafeStopKinds = unsafeStops;
        reconcileRepairsAvailable = 0;
        publishRecoveryRepairsAvailable = 0;
        allowWipCapOverride = false;
        prDraft = null;
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
                case "--candidate":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--candidate requires an execution unit value.";
                        return false;
                    }
                    candidate = args[index + 1].Trim();
                    index++;
                    break;
                case "--clarification-required":
                    clarificationRequired = true;
                    break;
                case "--stale-clarification-metadata":
                    staleClarificationMetadata = true;
                    break;
                case "--reconcile-unsafe-stop":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--reconcile-unsafe-stop requires a value (one stop kind per flag; repeat for multiple).";
                        return false;
                    }
                    unsafeStops.Add(args[index + 1].Trim());
                    index++;
                    break;
                case "--reconcile-repairs-available":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var repairCount)
                        || repairCount < 0)
                    {
                        error = "--reconcile-repairs-available requires a non-negative integer value.";
                        return false;
                    }
                    reconcileRepairsAvailable = repairCount;
                    index++;
                    break;
                case "--publish-recovery-repairs-available":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var publishRecoveryRepairCount)
                        || publishRecoveryRepairCount < 0)
                    {
                        error = "--publish-recovery-repairs-available requires a non-negative integer value.";
                        return false;
                    }
                    publishRecoveryRepairsAvailable = publishRecoveryRepairCount;
                    index++;
                    break;
                case "--allow-wip-cap-override":
                    allowWipCapOverride = true;
                    break;
                case "--pr-draft":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-draft requires a value (true or false).";
                        return false;
                    }
                    var rawDraft = args[index + 1].Trim().ToLowerInvariant();
                    if (string.Equals(rawDraft, "true", StringComparison.Ordinal))
                    {
                        prDraft = true;
                    }
                    else if (string.Equals(rawDraft, "false", StringComparison.Ordinal))
                    {
                        prDraft = false;
                    }
                    else
                    {
                        error = $"--pr-draft must be 'true' or 'false' (got '{args[index + 1]}').";
                        return false;
                    }
                    index++;
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--stale-clarification-metadata] [--reconcile-unsafe-stop <kind>] [--reconcile-repairs-available <N>] [--publish-recovery-repairs-available <N>] [--allow-wip-cap-override] [--pr-draft true|false] [--format text|json].";
                    return false;
            }
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

    private static void Emit(TextWriter writer, AutomationHostReviewDiagnosticsResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }
    }

    private static void WriteText(TextWriter writer, AutomationHostReviewDiagnosticsResult result)
    {
        writer.WriteLine($"# Host review diagnostics for {result.Repo}");
        writer.WriteLine($"- classification: {result.Classification}");
        writer.WriteLine($"- read_only: {result.ReadOnly.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- {result.Summary}");
        if (!string.IsNullOrWhiteSpace(result.RecommendedNextCommand))
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended next command");
            writer.WriteLine($"- {result.RecommendedNextCommand}");
        }
        if (result.StructuredClarification is { } clarification)
        {
            writer.WriteLine();
            writer.WriteLine("## Structured clarification");
            writer.WriteLine($"- background: {clarification.Background}");
            writer.WriteLine($"- question: {clarification.Question}");
            foreach (var option in clarification.Options)
            {
                writer.WriteLine($"  - option: {option}");
            }
        }
        if (result.Details.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Details");
            foreach (var detail in result.Details)
            {
                writer.WriteLine($"- {detail.Kind}: {detail.Description}");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation host-review-diagnostics");
        writer.WriteLine("Usage: intent-cli automation host-review-diagnostics [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--stale-clarification-metadata] [--reconcile-unsafe-stop <kind> ...] [--reconcile-repairs-available <N>] [--allow-wip-cap-override] [--pr-draft true|false] [--format text|json]");
        writer.WriteLine("Read-only host-loop convergence diagnostic. Classifies stuck-reviewing, missing-target-on-pr, request-update-rereview-conflict, wip-cap-blocked, clarification-required, stale-host-cli, review-pr-actionable, issue-publish-ready, unsafe-metadata, repaired-and-retry, draft-merge-blocked (G297), and true-idle (G286). Stale clarification metadata surfaces in `warnings` without flipping the terminal class. With `--allow-wip-cap-override` (G288) and a complete candidate, an in-flight intent-target item is bypassed for one publish; the override surfaces as `wip-cap-overridden` in `warnings`. With `--pr-draft true` and a selected review PR (G297), the diagnostic returns `draft-merge-blocked` so the host loop can release the review lease and surface the gap. Never mutates GitHub or local state.");
    }
}
