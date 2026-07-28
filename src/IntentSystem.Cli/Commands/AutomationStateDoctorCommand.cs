using IntentSystem.Supervisor;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G448: Unified, OSS-safe host-metadata state doctor. Read-only by default —
/// it surveys queue-state, publish artifacts, and GitHub PRs (open + merged)
/// and reports deterministic drift categories (missing linked_issue, missing
/// linked_pr, merged-PR-not-completed) with evidence and confidence, plus
/// ambiguous cases as fail-closed unsafe findings. <c>--write</c> applies ONLY
/// high-confidence, forward-only queue-state repairs and appends an
/// append-only <c>runs.jsonl</c> event per applied repair; it never clears,
/// rewrites, or downgrades existing host data, and never migrates old hosts.
///
/// Host-only by policy: child implementation loops must not invoke it
/// (<c>--child-loop-context</c> exits with code 2 for a testable prohibition).
/// The command never reads <c>intents/rules/**</c> and never launches an AI
/// provider.
/// </summary>
internal static class AutomationStateDoctorCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private const string OptRepo = "--repo";
    private const string OptWorkdir = "--workdir";
    private const string OptReadOnly = "--read-only";
    private const string OptWrite = "--write";
    private const string OptFormat = "--format";
    private const string OptChildLoopContext = "--child-loop-context";
    private const string OptHelp = "--help";

    private const string RunEventName = "state-doctor-repair";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var workdir, out var mode, out var format, out var childLoopContext, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (childLoopContext)
        {
            var rejected = BuildSingleUnsafe(
                repo ?? string.Empty,
                mode,
                AutomationStateDoctorUnsafeKinds.ChildLoopProhibited,
                "automation state-doctor is host-only; child implementation loops must not invoke it.",
                "child-loop prohibited: automation state-doctor is host-only.");
            Emit(writer, rejected, format);
            return 2;
        }

        var resolvedWorkdir = ResolveWorkdir(context, workdir);

        // G448 (review fix): --workdir must consistently drive the HOST context
        // used for every read/write — queue-state, publish artifacts, and the
        // run log — not just repo inference. Otherwise
        // `state-doctor --workdir /path/to/host --repo other/repo --write` could
        // mutate the caller cwd's `.intent-cli` state while inferring/reporting a
        // different target host, risking corruption of the wrong host metadata.
        // Rebind the context to the resolved workdir so all I/O targets that one
        // host root (fail-closed host-data safety).
        var hostContext = string.Equals(resolvedWorkdir, context.RepoRoot, StringComparison.Ordinal)
            ? context
            : context with { RepoRoot = resolvedWorkdir };

        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(hostContext);
        if (!surfaceReport.Available)
        {
            var stale = BuildSingleUnsafe(
                repo!,
                mode,
                AutomationStateDoctorUnsafeKinds.StaleHostCli,
                $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation surfaces; refresh the installed CLI before running state-doctor.",
                "stale-host-cli: installed automation surface is incomplete.");
            Emit(writer, stale, format);
            return 1;
        }

        // ---- gather queue-state (forward-compat: missing/old files are fine) ----
        QueueState? queueState = TryReadQueueState(hostContext);
        var queueItems = ProjectQueueItems(queueState);
        var publishEvidence = ProjectPublishEvidence(hostContext, queueState, repo!);

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<StateDoctorPr> pullRequests;
        try
        {
            pullRequests = ProjectPullRequests(repo!, lister);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            writer.WriteLine($"failed to list state-doctor candidates for {repo}: {exception.Message}");
            return 1;
        }

        var analysis = AutomationStateDoctorAnalyzer.Analyze(repo!, queueItems, publishEvidence, pullRequests);

        var findings = analysis.Findings;
        var warnings = new List<string>();
        if (string.Equals(mode, AutomationStateDoctorModes.Write, StringComparison.Ordinal))
        {
            findings = ApplyHighConfidenceRepairs(hostContext, findings, warnings);
        }

        var result = new AutomationStateDoctorResult
        {
            Repo = repo!,
            Mode = mode,
            HostOnly = true,
            Findings = findings,
            UnsafeFindings = analysis.UnsafeFindings,
            Warnings = warnings,
            Summary = BuildSummary(analysis.Findings, analysis.UnsafeFindings, mode),
        };

        Emit(writer, result, format);
        return 0;
    }

    private static IReadOnlyList<AutomationStateDoctorFinding> ApplyHighConfidenceRepairs(
        CliContext context,
        IReadOnlyList<AutomationStateDoctorFinding> findings,
        List<string> warnings)
    {
        var highConfidence = findings
            .Where(f => string.Equals(f.Confidence, AutomationStateDoctorConfidence.High, StringComparison.Ordinal))
            .ToArray();
        if (highConfidence.Length == 0)
        {
            return findings;
        }

        var queueStatePath = context.GetQueueStatePath();
        if (!File.Exists(queueStatePath))
        {
            warnings.Add($"queue-state.json not found at {queueStatePath}; no forward-only repairs applied.");
            return findings;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            warnings.Add($"failed to read queue-state.json for repair: {exception.Message}");
            return findings;
        }

        var items = queueState.Items.ToArray();
        var appliedById = new Dictionary<string, AutomationStateDoctorFinding>(StringComparer.Ordinal);
        var appliedEvents = new List<RunEvent>();
        var mutated = false;

        foreach (var finding in highConfidence)
        {
            var index = Array.FindIndex(items, item => string.Equals(item.ExecutionUnit, finding.ExecutionUnit, StringComparison.Ordinal));
            if (index < 0)
            {
                warnings.Add($"queue-state has no item with execution_unit '{finding.ExecutionUnit}'; skipped {finding.Category}.");
                continue;
            }

            var existing = items[index];
            QueueItem updated = existing;
            string? linkedIssueForEvent = null;
            string? linkedPrForEvent = null;

            switch (finding.RepairKind)
            {
                case AutomationStateDoctorRepairKinds.SetQueueLinkedIssue:
                    // Forward-only: only fill when currently empty.
                    if (existing.LinkedIssue is { Number: { } }) { continue; }
                    updated = existing with
                    {
                        LinkedIssue = new LinkedIssue
                        {
                            Repo = finding.IssueRepo ?? string.Empty,
                            Number = finding.IssueNumber,
                            Url = finding.IssueUrl,
                        },
                    };
                    linkedIssueForEvent = finding.IssueUrl
                        ?? (finding.IssueNumber is { } n ? $"https://github.com/{finding.IssueRepo}/issues/{n.ToString(CultureInfo.InvariantCulture)}" : null);
                    break;

                case AutomationStateDoctorRepairKinds.SetQueueLinkedPr:
                    if (!string.IsNullOrWhiteSpace(existing.LinkedPr)) { continue; }
                    updated = existing with { LinkedPr = finding.PrUrl };
                    linkedPrForEvent = finding.PrUrl;
                    break;

                case AutomationStateDoctorRepairKinds.MarkQueueCompleted:
                    if (existing.State == QueueItemState.Completed) { continue; }
                    updated = existing with { State = QueueItemState.Completed };
                    linkedPrForEvent = finding.PrUrl;
                    break;

                default:
                    continue;
            }

            items[index] = updated;
            mutated = true;
            appliedById[finding.ExecutionUnit + "|" + finding.Category] = finding;
            appliedEvents.Add(new RunEvent
            {
                Ts = DateTimeOffset.UtcNow,
                ExecutionUnit = finding.ExecutionUnit,
                Event = RunEventName,
                By = "automation state-doctor (G448)",
                LinkedIssue = linkedIssueForEvent,
                LinkedPr = linkedPrForEvent,
                Reason = finding.Summary,
                Repo = finding.IssueRepo,
                Pr = finding.PrNumber,
            });
        }

        if (mutated)
        {
            try
            {
                var updatedState = queueState with { Items = items, UpdatedAt = DateTimeOffset.UtcNow };
                // G548: guarded write (no-item-loss + stale-base re-application).
                QueueStatePersistence.Persist(queueStatePath, queueState, updatedState);
                AppendRunEvents(context, appliedEvents);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                warnings.Add($"failed to persist queue-state repairs: {exception.Message}");
                return findings;
            }
        }

        return findings
            .Select(f => appliedById.ContainsKey(f.ExecutionUnit + "|" + f.Category) ? f with { Applied = true } : f)
            .ToArray();
    }

    private static void AppendRunEvents(CliContext context, IReadOnlyList<RunEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        var runsPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        var builder = new System.Text.StringBuilder();
        foreach (var runEvent in events)
        {
            builder.Append(RunLogSerializer.SerializeLine(runEvent)).Append('\n');
        }
        File.AppendAllText(runsPath, builder.ToString());
    }

    private static QueueState? TryReadQueueState(CliContext context)
    {
        var path = context.GetQueueStatePath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return QueueStateSerializer.Deserialize(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<StateDoctorQueueItem> ProjectQueueItems(QueueState? queueState)
    {
        if (queueState is null)
        {
            return Array.Empty<StateDoctorQueueItem>();
        }

        return queueState.Items
            .Select(item => new StateDoctorQueueItem
            {
                ExecutionUnit = item.ExecutionUnit,
                LinkedIssueRepo = item.LinkedIssue?.Repo,
                LinkedIssueNumber = item.LinkedIssue?.Number,
                LinkedIssueUrl = item.LinkedIssue?.Url,
                LinkedPrUrl = item.LinkedPr,
                Completed = item.State == QueueItemState.Completed,
            })
            .ToArray();
    }

    private static IReadOnlyList<StateDoctorPublishEvidence> ProjectPublishEvidence(
        CliContext context,
        QueueState? queueState,
        string repo)
    {
        if (queueState is null)
        {
            return Array.Empty<StateDoctorPublishEvidence>();
        }

        var evidence = new List<StateDoctorPublishEvidence>();
        foreach (var item in queueState.Items)
        {
            var publishPath = Path.Combine(
                context.RepoRoot,
                IssuePublishArtifactPathResolver.Resolve(item.ExecutionUnit).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(publishPath))
            {
                continue;
            }

            IssuePublishArtifact artifact;
            try
            {
                artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                continue;
            }

            if (artifact.CreatedIssueNumber is not { } issueNumber)
            {
                continue;
            }

            var issueRepo = TryParseRepoFromIssueUrl(artifact.CreatedIssueUrl) ?? repo;
            evidence.Add(new StateDoctorPublishEvidence
            {
                ExecutionUnit = item.ExecutionUnit,
                IssueRepo = issueRepo,
                IssueNumber = issueNumber,
                IssueUrl = artifact.CreatedIssueUrl,
            });
        }
        return evidence;
    }

    private static string? TryParseRepoFromIssueUrl(string? url)
    {
        // https://github.com/<owner>/<repo>/issues/<n>
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length >= 2 ? $"{segments[0]}/{segments[1]}" : null;
    }

    private static IReadOnlyList<StateDoctorPr> ProjectPullRequests(string repo, IGitHubAutomationCandidateLister lister)
    {
        var byNumber = new Dictionary<int, StateDoctorPr>();

        void Ingest(IReadOnlyList<GitHubAutomationPrCandidate> candidates, bool forceMerged)
        {
            foreach (var candidate in candidates)
            {
                var closing = candidate.ClosingIssuesReferences
                    .Where(reference => reference.Number > 0 && ReferenceMatchesRepo(reference, repo))
                    .Select(reference => reference.Number)
                    .Distinct()
                    .ToArray();
                var merged = forceMerged
                    || string.Equals(candidate.State, "MERGED", StringComparison.OrdinalIgnoreCase);
                byNumber[candidate.Number] = new StateDoctorPr
                {
                    Number = candidate.Number,
                    Url = candidate.Url,
                    Merged = merged,
                    ClosingIssueNumbers = closing,
                };
            }
        }

        Ingest(lister.ListPullRequests(repo, Array.Empty<string>()), forceMerged: false);
        Ingest(lister.ListMergedPullRequests(repo, Array.Empty<string>()), forceMerged: true);

        return byNumber.Values.ToArray();
    }

    private static bool ReferenceMatchesRepo(GitHubPrClosingIssueReference reference, string repo)
    {
        if (reference.Repository is { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
        {
            return string.Equals($"{repository.Owner.Login}/{repository.Name}", repo, StringComparison.OrdinalIgnoreCase);
        }

        // No repository descriptor → assume same-repo reference (gh omits the
        // descriptor for same-repo closing references).
        return true;
    }

    private static AutomationStateDoctorResult BuildSingleUnsafe(
        string repo,
        string mode,
        string kind,
        string reason,
        string summary) =>
        new()
        {
            Repo = repo,
            Mode = mode,
            HostOnly = true,
            Findings = Array.Empty<AutomationStateDoctorFinding>(),
            UnsafeFindings =
            [
                new AutomationStateDoctorUnsafe
                {
                    Kind = kind,
                    ExecutionUnit = null,
                    IssueNumber = null,
                    Reason = reason,
                    MissingEvidence = Array.Empty<string>(),
                }
            ],
            Warnings = Array.Empty<string>(),
            Summary = summary,
        };

    private static string BuildSummary(
        IReadOnlyList<AutomationStateDoctorFinding> findings,
        IReadOnlyList<AutomationStateDoctorUnsafe> unsafeFindings,
        string mode)
    {
        var high = findings.Count(f => string.Equals(f.Confidence, AutomationStateDoctorConfidence.High, StringComparison.Ordinal));
        var advisory = findings.Count - high;
        if (high == 0 && advisory == 0 && unsafeFindings.Count == 0)
        {
            return $"no host-metadata drift detected; {mode} mode produced no findings.";
        }

        return $"state-doctor {mode}: {high.ToString(CultureInfo.InvariantCulture)} high-confidence repair(s), {advisory.ToString(CultureInfo.InvariantCulture)} advisory finding(s), {unsafeFindings.Count.ToString(CultureInfo.InvariantCulture)} unsafe finding(s).";
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string mode,
        out string format,
        out bool childLoopContext,
        out string error)
    {
        repo = null;
        workdir = null;
        mode = AutomationStateDoctorModes.ReadOnly;
        format = FormatText;
        childLoopContext = false;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
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

                case OptReadOnly:
                    mode = AutomationStateDoctorModes.ReadOnly;
                    break;

                case OptWrite:
                    mode = AutomationStateDoctorModes.Write;
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

                default:
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] [--read-only] [--write] [--child-loop-context] [--format text|json].";
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

    private static void Emit(TextWriter writer, AutomationStateDoctorResult result, string format)
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

    private static void WriteText(TextWriter writer, AutomationStateDoctorResult result)
    {
        writer.WriteLine($"# Automation state-doctor for {result.Repo}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- host_only: {result.HostOnly.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- {result.Summary}");
        writer.WriteLine();
        writer.WriteLine("## Findings");
        if (result.Findings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        foreach (var finding in result.Findings)
        {
            writer.WriteLine($"- {finding.Category} ({finding.Confidence}, applied={finding.Applied.ToString().ToLowerInvariant()})");
            writer.WriteLine($"  execution_unit: {finding.ExecutionUnit}");
            writer.WriteLine($"  repair_kind: {finding.RepairKind}");
            writer.WriteLine($"  summary: {finding.Summary}");
            foreach (var line in finding.Evidence)
            {
                writer.WriteLine($"  evidence: {line}");
            }
        }
        writer.WriteLine();
        writer.WriteLine("## Unsafe findings");
        if (result.UnsafeFindings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        foreach (var entry in result.UnsafeFindings)
        {
            writer.WriteLine($"- {entry.Kind}: {entry.Reason}");
            foreach (var line in entry.MissingEvidence)
            {
                writer.WriteLine($"  missing_evidence: {line}");
            }
        }
        if (result.Warnings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation state-doctor");
        writer.WriteLine("Usage: intent-cli automation state-doctor [--repo <owner/repo>] [--workdir <path>] [--read-only] [--write] [--format text|json]");
        writer.WriteLine("Unified, OSS-safe diagnostic for host-metadata drift (queue-state / publish artifacts / GitHub PRs).");
        writer.WriteLine("Read-only by default. --write applies ONLY high-confidence, forward-only queue-state repairs and appends runs.jsonl events.");
        writer.WriteLine("Ambiguous drift is reported as unsafe findings and never mutated (fail-closed). Existing/old hosts are never migrated.");
        writer.WriteLine("Host-only: child implementation loops must not invoke this command. --child-loop-context exits with code 2.");
    }
}
