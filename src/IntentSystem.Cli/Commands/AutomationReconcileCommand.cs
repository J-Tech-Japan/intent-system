using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G277: Host-side safe reconcile lane. Runs only on host loops and only
/// before declaring idle / hard-clarification. Produces a dry-run plan
/// describing mechanically provable label drift in the target repo, with
/// evidence and confidence; <c>--write</c> applies high-confidence repairs
/// through <see cref="IGitHubLabelMutator.ApplyReconcileTransitions"/>.
/// Lower-confidence advisory entries are not applied — they carry a
/// <c>requires_followup_command</c> that points the operator at the existing
/// host-owned surface (closeout / packet / next-slice) responsible for the
/// underlying mutation. The command itself never reads <c>intents/rules/**</c>
/// and never launches an AI provider. Calling it from a child implementation
/// loop is rejected via <c>--child-loop-context</c> so the prohibition is
/// testable; child-loop guidance never references this command in the first
/// place.
/// </summary>
internal static class AutomationReconcileCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private const string OptLane = "--lane";
    private const string OptRepo = "--repo";
    private const string OptWorkdir = "--workdir";
    private const string OptDryRun = "--dry-run";
    private const string OptWrite = "--write";
    private const string OptFormat = "--format";
    private const string OptChildLoopContext = "--child-loop-context";
    private const string OptNextSliceClarificationRequired = "--next-slice-clarification-required";
    private const string OptClarificationsAllResolved = "--clarifications-all-resolved";
    private const string OptHelp = "--help";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

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

        if (args.Length == 1 && string.Equals(args[0], OptHelp, StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(
                args,
                out var lane,
                out var repo,
                out var workdir,
                out var mode,
                out var format,
                out var childLoopContext,
                out var nextSliceClarificationRequired,
                out var clarificationsAllResolved,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (childLoopContext)
        {
            var rejected = new AutomationReconcileResult
            {
                Lane = lane,
                Repo = repo ?? string.Empty,
                Mode = mode,
                HostOnly = true,
                SafeRepairs = Array.Empty<AutomationReconcileRepair>(),
                UnsafeStops =
                [
                    new AutomationReconcileUnsafeStop
                    {
                        Kind = AutomationReconcileUnsafeStopKinds.ChildLoopProhibited,
                        TargetKind = null,
                        TargetNumber = null,
                        TargetUrl = null,
                        Reason = "automation reconcile is host-only; child implementation loops must not invoke it.",
                        MissingEvidence = Array.Empty<string>(),
                    }
                ],
                Warnings = Array.Empty<string>(),
                Summary = "child-loop prohibited: automation reconcile is host-only.",
            };

            Emit(writer, rejected, format);
            return 2;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // Host-only reconcile depends on the same installed CLI surface as the
        // host review preflight so high-confidence repairs are applied through
        // the canonical automation surface, not raw gh fallbacks.
        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        if (!surfaceReport.Available)
        {
            var stale = new AutomationReconcileResult
            {
                Lane = lane,
                Repo = repo!,
                Mode = mode,
                HostOnly = true,
                SafeRepairs = Array.Empty<AutomationReconcileRepair>(),
                UnsafeStops =
                [
                    new AutomationReconcileUnsafeStop
                    {
                        Kind = "stale-host-cli",
                        TargetKind = null,
                        TargetNumber = null,
                        TargetUrl = null,
                        Reason = $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation command surfaces; refresh the installed CLI before running reconcile.",
                        MissingEvidence = Array.Empty<string>(),
                    }
                ],
                Warnings = Array.Empty<string>(),
                Summary = "stale-host-cli: installed automation surface is incomplete.",
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

        var includeHostReview = !string.Equals(lane, AutomationReconcileLanes.NextSlice, StringComparison.Ordinal);
        var includeNextSlice = !string.Equals(lane, AutomationReconcileLanes.HostReview, StringComparison.Ordinal);

        AutomationReconcileAnalysis hostReviewAnalysis = new(
            Array.Empty<AutomationReconcileRepair>(),
            Array.Empty<AutomationReconcileUnsafeStop>());
        AutomationReconcileAnalysis nextSliceAnalysis = hostReviewAnalysis;

        if (includeHostReview)
        {
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
                writer.WriteLine($"failed to list reconcile candidates for {repo}: {exception.Message}");
                return 1;
            }

            hostReviewAnalysis = AutomationReconcileAnalyzer.AnalyzeHostReview(
                repo!,
                openPrs,
                publishedIssues);
        }

        if (includeNextSlice)
        {
            nextSliceAnalysis = AutomationReconcileAnalyzer.AnalyzeNextSlice(
                nextSliceClarificationRequired,
                clarificationsAllResolved);
        }

        var combined = AutomationReconcileAnalyzer.Combine(hostReviewAnalysis, nextSliceAnalysis);

        var appliedRepairs = combined.SafeRepairs;
        var warnings = new List<string>();
        if (string.Equals(mode, AutomationReconcileModes.Write, StringComparison.Ordinal))
        {
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
                writer.WriteLine($"failed to initialize reconcile mutator: {exception.Message}");
                return 1;
            }

            appliedRepairs = ApplyHighConfidenceRepairs(repo!, mutator, combined.SafeRepairs, warnings);
        }

        var result = new AutomationReconcileResult
        {
            Lane = lane,
            Repo = repo!,
            Mode = mode,
            HostOnly = true,
            SafeRepairs = appliedRepairs,
            UnsafeStops = combined.UnsafeStops,
            Warnings = warnings,
            Summary = BuildSummary(combined.SafeRepairs, combined.UnsafeStops, mode),
        };

        Emit(writer, result, format);
        return 0;
    }

    private static IReadOnlyList<AutomationReconcileRepair> ApplyHighConfidenceRepairs(
        string repo,
        IGitHubLabelMutator mutator,
        IReadOnlyList<AutomationReconcileRepair> repairs,
        List<string> warnings)
    {
        var applied = new List<AutomationReconcileRepair>(repairs.Count);
        foreach (var repair in repairs)
        {
            if (!string.Equals(repair.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal)
                || repair.TargetNumber is null
                || (string.Equals(repair.TargetKind, GhCliGitHubLabelMutator.Kinds.Pr, StringComparison.Ordinal) == false
                    && string.Equals(repair.TargetKind, GhCliGitHubLabelMutator.Kinds.Issue, StringComparison.Ordinal) == false))
            {
                applied.Add(repair);
                continue;
            }

            try
            {
                mutator.ApplyReconcileTransitions(
                    repo,
                    repair.TargetKind,
                    repair.TargetNumber.Value,
                    repair.AddLabels,
                    repair.RemoveLabels);
                applied.Add(repair with { Applied = true });
            }
            catch (InvalidOperationException exception)
            {
                warnings.Add($"failed to apply repair '{repair.Type}' on {repair.TargetKind} #{repair.TargetNumber}: {exception.Message}");
                applied.Add(repair);
            }
        }

        return applied;
    }

    private static string BuildSummary(
        IReadOnlyList<AutomationReconcileRepair> safeRepairs,
        IReadOnlyList<AutomationReconcileUnsafeStop> unsafeStops,
        string mode)
    {
        var highCount = safeRepairs.Count(r => string.Equals(r.Confidence, AutomationReconcileConfidence.High, StringComparison.Ordinal));
        var advisoryCount = safeRepairs.Count(r => string.Equals(r.Confidence, AutomationReconcileConfidence.Advisory, StringComparison.Ordinal));
        var unsafeCount = unsafeStops.Count;

        if (highCount == 0 && advisoryCount == 0 && unsafeCount == 0)
        {
            return $"no reconcile drift detected; {mode} mode produced no plan entries.";
        }

        return $"reconcile {mode}: {highCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} high-confidence repair(s), {advisoryCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} advisory entry(ies), {unsafeCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} unsafe stop(s).";
    }

    private static bool TryParseArguments(
        string[] args,
        out string lane,
        out string? repo,
        out string? workdir,
        out string mode,
        out string format,
        out bool childLoopContext,
        out bool nextSliceClarificationRequired,
        out bool clarificationsAllResolved,
        out string error)
    {
        lane = AutomationReconcileLanes.All;
        repo = null;
        workdir = null;
        mode = AutomationReconcileModes.DryRun;
        format = FormatText;
        childLoopContext = false;
        nextSliceClarificationRequired = false;
        clarificationsAllResolved = false;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case OptLane:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--lane requires a value (host-review, next-slice, or all).";
                        return false;
                    }
                    var requestedLane = args[index + 1].Trim();
                    if (!string.Equals(requestedLane, AutomationReconcileLanes.HostReview, StringComparison.Ordinal)
                        && !string.Equals(requestedLane, AutomationReconcileLanes.NextSlice, StringComparison.Ordinal)
                        && !string.Equals(requestedLane, AutomationReconcileLanes.All, StringComparison.Ordinal))
                    {
                        error = $"--lane must be 'host-review', 'next-slice', or 'all' (got '{requestedLane}').";
                        return false;
                    }
                    lane = requestedLane;
                    index++;
                    break;

                case OptRepo:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1].Trim();
                    index++;
                    break;

                case OptWorkdir:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case OptDryRun:
                    mode = AutomationReconcileModes.DryRun;
                    break;

                case OptWrite:
                    mode = AutomationReconcileModes.Write;
                    break;

                case OptFormat:
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

                case OptChildLoopContext:
                    childLoopContext = true;
                    break;

                case OptNextSliceClarificationRequired:
                    nextSliceClarificationRequired = true;
                    break;

                case OptClarificationsAllResolved:
                    clarificationsAllResolved = true;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: [--lane host-review|next-slice|all] [--repo <owner/repo>] [--workdir <path>] [--dry-run] [--write] [--next-slice-clarification-required] [--clarifications-all-resolved] [--child-loop-context] [--format text|json].";
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

    private static void Emit(TextWriter writer, AutomationReconcileResult result, string format)
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

    private static void WriteText(TextWriter writer, AutomationReconcileResult result)
    {
        writer.WriteLine($"# Automation reconcile ({result.Lane}) for {result.Repo}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- host_only: {result.HostOnly.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- {result.Summary}");
        writer.WriteLine();
        writer.WriteLine("## Safe repairs");
        if (result.SafeRepairs.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        foreach (var repair in result.SafeRepairs)
        {
            writer.WriteLine($"- {repair.Type} ({repair.Confidence}, applied={repair.Applied.ToString().ToLowerInvariant()})");
            writer.WriteLine($"  target: {repair.TargetKind} {(repair.TargetNumber is { } n ? "#" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) : "(local)")}");
            if (repair.AddLabels.Count > 0)
            {
                writer.WriteLine($"  add: {string.Join(", ", repair.AddLabels)}");
            }
            if (repair.RemoveLabels.Count > 0)
            {
                writer.WriteLine($"  remove: {string.Join(", ", repair.RemoveLabels)}");
            }
            writer.WriteLine($"  summary: {repair.Summary}");
            foreach (var line in repair.Evidence)
            {
                writer.WriteLine($"  evidence: {line}");
            }
            if (!string.IsNullOrWhiteSpace(repair.RequiresFollowupCommand))
            {
                writer.WriteLine($"  followup: {repair.RequiresFollowupCommand}");
            }
        }
        writer.WriteLine();
        writer.WriteLine("## Unsafe stops");
        if (result.UnsafeStops.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        foreach (var stop in result.UnsafeStops)
        {
            writer.WriteLine($"- {stop.Kind}: {stop.Reason}");
            foreach (var line in stop.MissingEvidence)
            {
                writer.WriteLine($"  missing_evidence: {line}");
            }
        }
        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var w in result.Warnings)
            {
                writer.WriteLine($"- {w}");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation reconcile");
        writer.WriteLine("Usage: intent-cli automation reconcile [--lane host-review|next-slice|all] [--repo <owner/repo>] [--workdir <path>] [--dry-run] [--write] [--next-slice-clarification-required] [--clarifications-all-resolved] [--format text|json]");
        writer.WriteLine("Host-only safe reconcile lane for mechanically provable intent-cli metadata drift.");
        writer.WriteLine("Dry-run by default. --write applies high-confidence label repairs through the host-owned reconcile mutator.");
        writer.WriteLine("Child implementation loops must not invoke this command. --child-loop-context exits with code 2 for testable prohibition.");
    }
}
