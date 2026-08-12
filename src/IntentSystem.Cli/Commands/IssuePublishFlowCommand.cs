using IntentSystem.Supervisor;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G245: <c>intent-cli issue publish-flow</c> command. Performs the
/// deterministic issue create / durable-publish-boundary handoff for an
/// existing packet without prompt-specific label mutation knowledge.
///
/// Validates the packet's <c>github-body.md</c> for required Child Issue
/// Contract sections before any GitHub mutation. With <c>--write</c>,
/// creates the GitHub issue WITHOUT <c>intent-target</c>, then atomically
/// reflects the creation in parent durable artifacts (G278): the
/// matching <c>queue-state.json</c> item gets a populated
/// <c>LinkedIssue</c>, <c>publish.yaml</c> advances to
/// <c>issue-created</c> with the GitHub URL/number, and an
/// <c>issue-created</c> event is appended to <c>runs.jsonl</c>. Re-running
/// after a successful publish is idempotent: the <c>publish.yaml</c>
/// issue-created marker short-circuits both the GitHub call and the
/// durable-state writes. The command never applies <c>intent-target</c>
/// directly. Never launches an AI provider.
/// </summary>
internal static class IssuePublishFlowCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    public const string PublishStatusIssueCreated = "issue-created";

    public const string IssueCreatedEventName = "issue-created";

    public const string IssueCreatedEventBy = "issue-publish-flow";

    private static readonly Regex IssueUrlNumberRegex = new(
        @"/issues/(?<number>\d+)(?:[/#?].*)?$",
        RegexOptions.Compiled);

    private const string UsageLine =
        "Usage: intent-cli issue publish-flow <execution-unit> --repo <owner/repo> [--domain <name>] [--write] [--format json|markdown]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

    /// <summary>Test seam — replaces the default <c>gh issue create</c> shell out.</summary>
    public static Func<IIssueCreator>? CreatorFactory { get; set; }

    /// <summary>
    /// G536 review repair: test seam — replaces the default
    /// <c>gh issue list --search</c> shell out used to corroborate that no
    /// GitHub issue already exists before falling through to
    /// <c>gh issue create</c> when all three local durable artifacts are
    /// missing.
    /// </summary>
    public static Func<IGitHubExistingIssueChecker>? ExistingIssueCheckerFactory { get; set; }

    /// <summary>G278: test seam for the durable-state event timestamp.</summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

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

        if (!TryParseArguments(args, out var executionUnit, out var repo, out var domainOverride, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G245'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit!);
        var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
        var publishYamlPath = Path.Combine(context.RepoRoot, IssuePublishArtifactPathResolver.Resolve(executionUnit!));

        if (!Directory.Exists(packetDirectory))
        {
            var earlyResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: false,
                githubBodyPresent: false,
                missingSections: PacketDraftCommand.RequiredContractSections,
                title: null,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"packet directory not found: {packetDirectory}");
            EmitResult(writer, earlyResult, format);
            return 1;
        }

        var githubBodyPresent = File.Exists(githubBodyPath);
        var githubBody = githubBodyPresent ? File.ReadAllText(githubBodyPath) : null;
        // G670: this is the exact publish-gate readiness judgment consumed by
        // next-slice and stalled-work. Keep the validator and its named cause
        // in one shared result so no consumer can drift into a parallel
        // placeholder heuristic.
        var publishGateReadiness = NextSliceReadinessEvaluator.EvaluatePublishGate(
            executionUnit!, githubBodyPath, githubBody);
        var missing = publishGateReadiness.MissingContractSections;

        // G290: prefer `packet.yaml` `title:` over the body H1 fallback so a
        // packet with valid metadata never publishes as `<id> (untitled)`.
        // Body H1 remains a fallback for older packets / Sekiban-style
        // bodies that start at `## Goal`. Title source is reported on the
        // result so the operator can audit which path resolved the title.
        // G298: format the resolved title as `<execution-unit> <title>` when
        // the title doesn't already begin with the execution-unit token, so
        // `gh issue create` always carries a scan-friendly identifier in the
        // GitHub issue list. Already-prefixed titles (and the deterministic
        // `<id> (untitled)` fallback) stay verbatim.
        string? title = null;
        string? titleSource = null;
        if (githubBodyPresent)
        {
            (title, titleSource) = ResolveTitleWithSource(executionUnit!, packetDirectory, githubBodyPath);
            title = FormatIssueTitle(executionUnit!, title);
        }

        // G449: gate publish on the SHARED NextSliceReadinessEvaluator's
        // contract-completeness verdict so a candidate publish-flow rejects is
        // exactly one that next-slice / packet-draft / diagnostics will not
        // report issue-cut-ready. ContractComplete requires the body present AND
        if (!publishGateReadiness.Judgment.IssueCutReady)
        {
            var validationResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: githubBodyPresent,
                missingSections: missing,
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: githubBodyPresent
                    ? "Child Issue Contract is incomplete; the existing publish gate rejected headings or placeholder-only Related Links."
                    : "github-body.md is missing in the packet directory.",
                titleSource: titleSource);
            EmitResult(writer, validationResult, format);
            return 1;
        }

        // G669: a lane-declaring packet is publishable only after the
        // independent design proposal and orchestration confirmation are both
        // recorded in the CLI-owned decision store. Legacy packets have no
        // lane declaration and deliberately pass this gate unchanged.
        var laneDecisionGate = BranchLaneDecisionGate.Evaluate(context.RepoRoot, executionUnit!);
        if (!laneDecisionGate.Passed)
        {
            var laneGateResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: githubBodyPresent,
                missingSections: missing,
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: laneDecisionGate.Error
                    ?? "lane decision records are incomplete; both propose and confirm records are required.",
                titleSource: titleSource);
            EmitResult(writer, laneGateResult, format);
            return 1;
        }

        var queueStatePathForIdempotency = context.GetQueueStatePath();
        var runLogPathForIdempotency = context.GetRunLogPath();

        // G536 review repair: ONE shared, fail-closed analyzer independently
        // parses all three durable artifacts (queue-state linked_issue,
        // publish.yaml issue-created record, runs.jsonl issue-created
        // event(s) — classified zero/exactly-one/duplicate-identical/
        // conflicting) and resolves a single canonical issue identity, or
        // fails closed on malformed data or a cross-artifact contradiction.
        // `AutomationPublishRecoveryCommand` consults the SAME analyzer so
        // the two surfaces can never disagree about what is missing.
        //
        // Field incidents (2026-07-19, G530/#1164 and #1166): a concurrent
        // host-main advance forced a stash+ff-sync mid-publish. The
        // pre-G536 short-circuit trusted whichever ONE signal it found
        // first (publish.yaml or queue-state) and unconditionally reported
        // durable_state_synced:true without ever checking runs.jsonl —
        // and a unit whose ONLY surviving signal was runs.jsonl fell
        // through to the create path entirely, risking a SECOND GitHub
        // issue for the same execution unit.
        var analysis = PublishDurableArtifactAnalyzer.Analyze(
            executionUnit!, repo!, queueStatePathForIdempotency, publishYamlPath, runLogPathForIdempotency,
            context, domain);

        if (analysis.IsInvalid)
        {
            var invalidResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"durable-state analysis for '{executionUnit}' failed closed ({analysis.InvalidReason}): "
                    + $"{analysis.InvalidDetail} Refusing to create, restore, or otherwise guess at this unit's "
                    + $"state — inspect {analysis.InvalidArtifactPath} manually, then re-run "
                    + $"`intent-cli issue publish-flow {executionUnit} --repo {repo} --write` once resolved.",
                titleSource: titleSource);
            EmitResult(writer, invalidResult, format);
            return 1;
        }

        if (!write)
        {
            // Dry-run: PLAN only, never write. When the analyzer finds an
            // existing issue, report the idempotent-rerun plan (canonical
            // identity plus any artifacts that would be restored) purely
            // from the read-only analysis — no restoration helper is ever
            // invoked here.
            var dryRunResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: analysis.HasExistingIssue,
                durableStateSynced: analysis.IsFullySynced,
                issueUrl: analysis.CanonicalIssueUrl,
                issueNumber: analysis.CanonicalIssueNumber,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: null,
                titleSource: titleSource,
                wouldRestore: analysis.HasExistingIssue ? analysis.Gaps : null,
                extraWarnings: analysis.Warnings);
            EmitResult(writer, dryRunResult, format);
            return 0;
        }

        if (analysis.HasExistingIssue)
        {
            return ExecuteIdempotentRerun(
                writer,
                format,
                executionUnit!,
                domain,
                repo!,
                packetDirectory,
                githubBodyPath,
                publishYamlPath,
                queueStatePathForIdempotency,
                runLogPathForIdempotency,
                title,
                titleSource,
                analysis,
                context);
        }

        // G536 review repair: the analyzer found NO existing-issue identity
        // across all three LOCAL artifacts — before falling through to
        // create, corroborate against GitHub itself. If a matching issue
        // already exists there (e.g. every local artifact was reset/lost
        // but the GitHub issue itself was never re-created), creating here
        // would produce a genuine duplicate.
        IGitHubExistingIssueChecker existingIssueChecker;
        try
        {
            existingIssueChecker = ExistingIssueCheckerFactory?.Invoke() ?? new GhCliExistingIssueChecker();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            var checkerErrorResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"failed to initialize GitHub existing-issue check: {exception.Message}",
                titleSource: titleSource);
            EmitResult(writer, checkerErrorResult, format);
            return 1;
        }

        GitHubExistingIssueLookupResult lookup;
        try
        {
            lookup = existingIssueChecker.FindExistingIssue(repo!, executionUnit!, title!, File.ReadAllText(githubBodyPath));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Fail closed: an unresolvable corroboration check (a search
            // failure) must never be treated as "no existing issue."
            var checkerFailedResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"could not verify with GitHub whether an issue for '{executionUnit}' already exists "
                    + $"({exception.Message}); refusing to create without that corroboration. Retry once GitHub "
                    + "is reachable.",
                titleSource: titleSource);
            EmitResult(writer, checkerFailedResult, format);
            return 1;
        }

        if (lookup.Classification == GitHubExistingIssueClassification.Multiple)
        {
            var ambiguousResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"multiple issues on {repo} match both the exact expected title and body for '{executionUnit}'; "
                    + "cannot deterministically pick which one is canonical. Refusing to create or restore around an "
                    + "ambiguous match — reconcile the duplicate GitHub issues manually, then re-run.",
                titleSource: titleSource);
            EmitResult(writer, ambiguousResult, format);
            return 1;
        }

        if (lookup.Classification == GitHubExistingIssueClassification.Unique)
        {
            // G536 round-4 review repair: a unique, exact title+body match
            // already exists on GitHub even though every local artifact was
            // lost — feed that identity into the SAME shared analyzer/
            // restoration path used for a local-signal rerun, so all three
            // local artifacts are restored WITHOUT ever calling `gh issue
            // create` (which would produce a genuine duplicate).
            var githubSourcedAnalysis = PublishDurableArtifactAnalysis.ExistingIssue(
                lookup.IssueNumber,
                lookup.IssueUrl!,
                new[]
                {
                    PublishDurableArtifactAnalyzer.GapQueueLinkedIssueMissing,
                    PublishDurableArtifactAnalyzer.GapPublishYamlMissing,
                    PublishDurableArtifactAnalyzer.GapRunsEventMissing,
                });
            return ExecuteIdempotentRerun(
                writer,
                format,
                executionUnit!,
                domain,
                repo!,
                packetDirectory,
                githubBodyPath,
                publishYamlPath,
                queueStatePathForIdempotency,
                runLogPathForIdempotency,
                title,
                titleSource,
                githubSourcedAnalysis,
                context);
        }

        // G363 (PR #830 review repair): atomic-seed gate. The
        // execution-unit MUST be present in queue-state.json BEFORE
        // any GitHub mutation. Without this guard, `gh issue create`
        // could succeed for a unit the queue can't link, leaving an
        // orphan GitHub issue that closeout will never reconcile.
        // The recommended recovery surface is the new
        // `automation queue-seed-from-packet --write` command (G363)
        // — that's the safe deterministic path to populate the
        // missing item from a validated prepared packet directory.
        if (!QueueStateContainsExecutionUnit(queueStatePathForIdempotency, executionUnit!))
        {
            var missingQueueItemResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"queue-state has no item with execution_unit `{executionUnit}`; "
                    + "refusing to create the GitHub issue (atomic-seed gate, G363). "
                    + $"Seed first: `intent-cli automation queue-seed-from-packet --execution-unit {executionUnit} --target-repo {repo} --write`, "
                    + "then re-run `issue publish-flow --write`.",
                titleSource: titleSource);
            EmitResult(writer, missingQueueItemResult, format);
            return 1;
        }

        IIssueCreator creator;
        try
        {
            creator = CreatorFactory?.Invoke() ?? new GhCliIssueCreator();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            var creatorErrorResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"failed to initialize GitHub issue creator: {exception.Message}",
                titleSource: titleSource);
            EmitResult(writer, creatorErrorResult, format);
            return 1;
        }

        IssueCreateOutcome outcome;
        try
        {
            outcome = creator.CreateIssue(repo!, title!, githubBodyPath);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            var createErrorResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: null,
                issueNumber: null,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"gh issue create failed: {exception.Message}",
                titleSource: titleSource);
            EmitResult(writer, createErrorResult, format);
            return 1;
        }

        var issueNumber = ParseIssueNumber(outcome.IssueUrl);
        var queueStatePath = context.GetQueueStatePath();
        var runLogPath = context.GetRunLogPath();
        var publishedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

        bool queueStatePatched;
        bool publishYamlPatched;
        bool runsAppended;
        try
        {
            queueStatePatched = TryPatchQueueStateLinkedIssue(
                queueStatePath,
                executionUnit!,
                repo!,
                issueNumber,
                outcome.IssueUrl,
                publishedAt);

            publishYamlPatched = WritePublishArtifact(
                publishYamlPath,
                packetDirectory,
                githubBodyPath,
                executionUnit!,
                issueNumber,
                outcome.IssueUrl);

            runsAppended = AppendIssueCreatedRunEvent(
                runLogPath,
                executionUnit!,
                repo!,
                issueNumber,
                outcome.IssueUrl,
                publishedAt);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            var partialResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                // G278: do not claim created:true while local durable artifacts remain unmodified.
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: outcome.IssueUrl,
                issueNumber: issueNumber,
                queueStatePatched: false,
                publishYamlPatched: false,
                runsAppended: false,
                error: $"GitHub issue {outcome.IssueUrl} was created but parent durable state could not be updated: {exception.Message}. Reconcile via 'intent-cli automation reconcile' before re-running.",
                titleSource: titleSource);
            EmitResult(writer, partialResult, format);
            return 1;
        }

        // G278 follow-up (PR #660 review): all three durable artifacts must be in
        // sync for success. If any required artifact was not patched (most
        // commonly the queue-state item, e.g. queue-state.json missing or the
        // execution unit absent), refuse to claim created:true.
        if (!queueStatePatched || !publishYamlPatched || !runsAppended)
        {
            var unsynchronizedReasons = new List<string>();
            if (!queueStatePatched)
            {
                unsynchronizedReasons.Add(
                    File.Exists(queueStatePath)
                        ? $"queue-state.json has no item with execution_unit '{executionUnit}'"
                        : $"queue-state.json not found at {queueStatePath}");
            }
            if (!publishYamlPatched)
            {
                unsynchronizedReasons.Add($"publish.yaml at {publishYamlPath} was not written");
            }
            if (!runsAppended)
            {
                unsynchronizedReasons.Add($"runs.jsonl at {runLogPath} did not receive the issue-created event");
            }

            var unsynchronizedResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
                packetExists: true,
                githubBodyPresent: true,
                missingSections: Array.Empty<string>(),
                title: title,
                created: false,
                idempotent: false,
                durableStateSynced: false,
                issueUrl: outcome.IssueUrl,
                issueNumber: issueNumber,
                queueStatePatched: queueStatePatched,
                publishYamlPatched: publishYamlPatched,
                runsAppended: runsAppended,
                titleSource: titleSource,
                error: $"GitHub issue {outcome.IssueUrl} was created but parent durable state is not fully synchronized: {string.Join("; ", unsynchronizedReasons)}. Reconcile via 'intent-cli automation reconcile' or seed the missing parent artifact, then re-run.");
            EmitResult(writer, unsynchronizedResult, format);
            return 1;
        }

        var successResult = NewResult(executionUnit!, domain, repo!, packetDirectory, githubBodyPath, publishYamlPath, write,
            packetExists: true,
            githubBodyPresent: true,
            missingSections: Array.Empty<string>(),
            title: title,
            created: true,
            idempotent: false,
            durableStateSynced: true,
            issueUrl: outcome.IssueUrl,
            issueNumber: issueNumber,
            queueStatePatched: queueStatePatched,
            publishYamlPatched: publishYamlPatched,
            runsAppended: runsAppended,
            error: null,
            titleSource: titleSource,
            extraWarnings: analysis.Warnings);
        EmitResult(writer, successResult, format);
        return 0;
    }

    /// <summary>
    /// G536 review repair: the idempotent-rerun path. The shared
    /// <see cref="PublishDurableArtifactAnalyzer"/> already proved a
    /// canonical issue identity exists and named exactly which of the three
    /// durable artifacts (queue-state linked_issue, publish.yaml,
    /// runs.jsonl issue-created event) are genuinely missing — this method
    /// restores ONLY those, never touching an artifact the analyzer found
    /// present (whether correct or, on a prior contradiction, already
    /// rejected upstream). After attempting restoration it NEVER trusts the
    /// write helpers' boolean return values alone: it re-invokes the same
    /// analyzer against the freshly re-read files and reports
    /// <c>durable_state_synced: true</c> only when that second, independent
    /// read confirms zero remaining gaps.
    /// </summary>
    private static int ExecuteIdempotentRerun(
        TextWriter writer,
        string format,
        string executionUnit,
        string domain,
        string repo,
        string packetDirectory,
        string githubBodyPath,
        string publishYamlPath,
        string queueStatePath,
        string runLogPath,
        string? title,
        string? titleSource,
        PublishDurableArtifactAnalysis analysis,
        CliContext context)
    {
        var canonicalIssueNumber = analysis.CanonicalIssueNumber;
        var canonicalIssueUrl = analysis.CanonicalIssueUrl!;
        var restoredAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var restoredArtifacts = new HashSet<string>(StringComparer.Ordinal);
        var writeProblems = new List<string>();

        foreach (var gap in analysis.Gaps)
        {
            switch (gap)
            {
                case PublishDurableArtifactAnalyzer.GapQueueLinkedIssueMissing:
                    try
                    {
                        if (TryPatchQueueStateLinkedIssue(
                                queueStatePath, executionUnit, repo, canonicalIssueNumber, canonicalIssueUrl, restoredAt))
                        {
                            restoredArtifacts.Add("queue_state");
                        }
                        else
                        {
                            writeProblems.Add(
                                File.Exists(queueStatePath)
                                    ? $"queue-state.json has no item with execution_unit '{executionUnit}' to restore linked_issue onto"
                                    : $"queue-state.json not found at {queueStatePath}");
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        writeProblems.Add($"queue-state.json linked_issue restoration failed: {exception.Message}");
                    }
                    break;

                case PublishDurableArtifactAnalyzer.GapPublishYamlMissing:
                    try
                    {
                        if (WritePublishArtifact(
                                publishYamlPath, packetDirectory, githubBodyPath, executionUnit, canonicalIssueNumber, canonicalIssueUrl))
                        {
                            restoredArtifacts.Add("publish_yaml");
                        }
                        else
                        {
                            writeProblems.Add($"publish.yaml at {publishYamlPath} was not written");
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        writeProblems.Add($"publish.yaml restoration failed: {exception.Message}");
                    }
                    break;

                case PublishDurableArtifactAnalyzer.GapRunsEventMissing:
                    try
                    {
                        if (AppendIssueCreatedRunEvent(
                                runLogPath, executionUnit, repo, canonicalIssueNumber, canonicalIssueUrl, restoredAt))
                        {
                            restoredArtifacts.Add("runs");
                        }
                        else
                        {
                            writeProblems.Add($"runs.jsonl at {runLogPath} did not receive the issue-created event");
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException)
                    {
                        writeProblems.Add($"runs.jsonl issue-created event restoration failed: {exception.Message}");
                    }
                    break;
            }
        }

        // Never trust the write helpers' boolean returns alone: re-read and
        // re-analyze all three artifacts from disk before ever reporting
        // durable_state_synced:true. A concurrent change between the write
        // above and this re-read (or a write that silently no-opped) is
        // caught here rather than masked.
        var reAnalysis = PublishDurableArtifactAnalyzer.Analyze(
            executionUnit, repo, queueStatePath, publishYamlPath, runLogPath, context, domain);
        var fullySynced = reAnalysis.HasExistingIssue && !reAnalysis.IsInvalid && reAnalysis.Gaps.Count == 0;

        string? error = null;
        if (!fullySynced)
        {
            var remaining = new List<string>();
            if (reAnalysis.IsInvalid)
            {
                remaining.Add($"post-restoration re-analysis failed closed ({reAnalysis.InvalidReason}): {reAnalysis.InvalidDetail}");
            }
            else if (reAnalysis.Gaps.Count > 0)
            {
                remaining.Add($"still missing: {string.Join(", ", reAnalysis.Gaps)}");
            }
            if (writeProblems.Count > 0)
            {
                remaining.Add(string.Join("; ", writeProblems));
            }

            error = $"GitHub issue {canonicalIssueUrl} already exists but parent durable state could not be fully "
                + $"restored: {string.Join("; ", remaining)}. Recovery: re-run "
                + $"`intent-cli issue publish-flow {executionUnit} --repo {repo} --write` after ensuring these "
                + "paths are writable, or run `intent-cli automation publish-recovery "
                + $"--repo {repo} --write` to reconcile queue-state linkage.";
        }

        var result = NewResult(executionUnit, domain, repo, packetDirectory, githubBodyPath, publishYamlPath, write: true,
            packetExists: true,
            githubBodyPresent: true,
            missingSections: Array.Empty<string>(),
            title: title,
            created: false,
            idempotent: true,
            durableStateSynced: fullySynced,
            issueUrl: reAnalysis.CanonicalIssueUrl ?? canonicalIssueUrl,
            issueNumber: reAnalysis.CanonicalIssueNumber ?? canonicalIssueNumber,
            queueStatePatched: restoredArtifacts.Contains("queue_state"),
            publishYamlPatched: restoredArtifacts.Contains("publish_yaml"),
            runsAppended: restoredArtifacts.Contains("runs"),
            error: error,
            titleSource: titleSource,
            extraWarnings: analysis.Warnings.Concat(reAnalysis.Warnings).Distinct(StringComparer.Ordinal).ToArray());
        EmitResult(writer, result, format);
        return fullySynced ? 0 : 1;
    }

    /// <summary>
    /// G363 (PR #830 review repair): returns true when
    /// <c>queue-state.json</c> contains an item for the
    /// <paramref name="executionUnit"/>. Used to gate
    /// <c>issue publish-flow --write</c> BEFORE any GitHub
    /// mutation. Treats a missing or unparseable queue-state file
    /// as "not present" so the caller fails closed and routes the
    /// operator to <c>automation queue-seed-from-packet</c> rather
    /// than creating an orphan GitHub issue.
    ///
    /// PR #830 review repair: also catches
    /// <see cref="System.Text.Json.JsonException"/> so a malformed
    /// <c>queue-state.json</c> (truncated write, hand-edit typo,
    /// stale partial commit, etc.) cannot crash
    /// <c>issue publish-flow --write</c> via an unhandled exception.
    /// Malformed input falls through to "not present" → atomic-seed
    /// gate trips → operator gets the structured stop with the
    /// recommended <c>automation queue-seed-from-packet</c> recovery
    /// command, matching the existing fail-closed handling.
    /// </summary>
    private static bool QueueStateContainsExecutionUnit(string queueStatePath, string executionUnit)
    {
        if (!File.Exists(queueStatePath))
        {
            return false;
        }
        try
        {
            var queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            foreach (var item in queueState.Items)
            {
                if (string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or System.Text.Json.JsonException)
        {
            // Treat as not present — fall through to fail-closed.
            // PR #830 review repair: JsonException added to handle
            // malformed queue-state.json without crashing.
        }
        return false;
    }

    private static int? ParseIssueNumber(string issueUrl)
    {
        if (string.IsNullOrWhiteSpace(issueUrl))
        {
            return null;
        }

        var match = IssueUrlNumberRegex.Match(issueUrl.Trim());
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(
            match.Groups["number"].Value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var number)
            ? number
            : null;
    }

    private static bool TryPatchQueueStateLinkedIssue(
        string queueStatePath,
        string executionUnit,
        string repo,
        int? issueNumber,
        string issueUrl,
        DateTimeOffset publishedAt)
    {
        if (!File.Exists(queueStatePath))
        {
            return false;
        }

        var raw = File.ReadAllText(queueStatePath);
        var queueState = QueueStateSerializer.Deserialize(raw);

        var matchedIndex = -1;
        for (var index = 0; index < queueState.Items.Count; index++)
        {
            if (string.Equals(queueState.Items[index].ExecutionUnit, executionUnit, StringComparison.Ordinal))
            {
                matchedIndex = index;
                break;
            }
        }

        if (matchedIndex < 0)
        {
            return false;
        }

        var existingItem = queueState.Items[matchedIndex];
        var updatedItem = existingItem with
        {
            LinkedIssue = new LinkedIssue
            {
                Repo = repo,
                Number = issueNumber,
                Url = issueUrl,
            }
        };

        var newItems = queueState.Items.ToArray();
        newItems[matchedIndex] = updatedItem;

        var updatedState = queueState with
        {
            Items = newItems,
            UpdatedAt = publishedAt,
        };

        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(queueStatePath, queueState, updatedState);
        return true;
    }

    private static bool WritePublishArtifact(
        string publishYamlPath,
        string packetDirectory,
        string githubBodyPath,
        string executionUnit,
        int? issueNumber,
        string issueUrl)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(publishYamlPath)!);

        var artifact = new IssuePublishArtifact
        {
            ExecutionUnit = executionUnit,
            PublishStatus = PublishStatusIssueCreated,
            PacketPath = packetDirectory,
            IssueBodyPath = githubBodyPath,
            CreatedIssueNumber = issueNumber,
            CreatedIssueUrl = issueUrl,
            PublishedLabelName = null,
        };

        File.WriteAllText(publishYamlPath, IssuePublishArtifactYaml.Serialize(artifact));
        return true;
    }

    private static bool AppendIssueCreatedRunEvent(
        string runLogPath,
        string executionUnit,
        string repo,
        int? issueNumber,
        string issueUrl,
        DateTimeOffset publishedAt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);

        var linkedIssueDescriptor = issueNumber.HasValue
            ? $"{repo}#{issueNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : issueUrl;

        var runEvent = new RunEvent
        {
            Ts = publishedAt,
            ExecutionUnit = executionUnit,
            Event = IssueCreatedEventName,
            By = IssueCreatedEventBy,
            LinkedIssue = linkedIssueDescriptor,
            Reason = issueUrl,
        };

        var line = RunLogSerializer.SerializeLine(runEvent);

        if (File.Exists(runLogPath))
        {
            var existing = File.ReadAllText(runLogPath);
            if (!existing.EndsWith("\n", StringComparison.Ordinal) && existing.Length > 0)
            {
                File.AppendAllText(runLogPath, "\n");
            }
        }

        File.AppendAllText(runLogPath, line + "\n");
        return true;
    }

    private static IssuePublishFlowResult NewResult(
        string executionUnit,
        string domain,
        string repo,
        string packetDirectory,
        string githubBodyPath,
        string publishYamlPath,
        bool write,
        bool packetExists,
        bool githubBodyPresent,
        IReadOnlyList<string> missingSections,
        string? title,
        bool created,
        bool idempotent,
        bool durableStateSynced,
        string? issueUrl,
        int? issueNumber,
        bool queueStatePatched,
        bool publishYamlPatched,
        bool runsAppended,
        string? error,
        string? titleSource = null,
        IReadOnlyList<string>? wouldRestore = null,
        IReadOnlyList<string>? extraWarnings = null)
    {
        var nextSteps = new List<string>();
        if (created)
        {
            nextSteps.Add("Commit and push the parent durable state for this execution unit (queue-state, runs, packet files).");
            nextSteps.Add($"Then apply the publish boundary with: intent-cli automation issue-publish --repo {repo} --issue {(issueNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<issue-number>")} --write --format json");
        }
        else if (idempotent)
        {
            nextSteps.Add("Issue already created; durable state is already in sync. No GitHub call was made.");
            if (issueNumber.HasValue)
            {
                nextSteps.Add($"Apply the publish boundary if needed with: intent-cli automation issue-publish --repo {repo} --issue {issueNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} --write --format json");
            }
        }

        return new IssuePublishFlowResult
        {
            ExecutionUnit = executionUnit,
            Domain = domain,
            Repo = repo,
            PacketDirectory = packetDirectory,
            GithubBodyPath = githubBodyPath,
            PublishYamlPath = publishYamlPath,
            PacketExists = packetExists,
            GithubBodyPresent = githubBodyPresent,
            MissingContractSections = missingSections,
            Mode = write ? ModeWrite : ModeDryRun,
            Title = title,
            Created = created,
            Idempotent = idempotent,
            DurableStateSynced = durableStateSynced,
            IssueUrl = issueUrl,
            IssueNumber = issueNumber,
            QueueStatePatched = queueStatePatched,
            PublishYamlPatched = publishYamlPatched,
            RunsAppended = runsAppended,
            IntentTargetApplied = false,
            NextSteps = nextSteps,
            // G290: surface the structured title source so the operator can
            // audit `packet-yaml` vs `github-body-h1` vs `fallback-untitled`.
            // The fallback case adds a `title-fallback` warning so a fallback
            // publish never fails silently.
            TitleSource = titleSource,
            // G542: cross-domain malformed runs.jsonl rows surface here too
            // (naming `runs-audit`) alongside the pre-existing title-fallback
            // warning — domain-scoped analysis narrows the blast radius of a
            // legacy row, it does not silence the finding entirely.
            Warnings = (string.Equals(titleSource, TitleSourceFallbackUntitled, StringComparison.Ordinal)
                    ? new[] { "title-fallback" }
                    : Array.Empty<string>())
                .Concat(extraWarnings ?? Array.Empty<string>())
                .ToArray(),
            WouldRestore = wouldRestore,
            Error = error
        };
    }

    private static void EmitResult(TextWriter writer, IssuePublishFlowResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, IssuePublishFlowResult result)
    {
        writer.WriteLine($"# Issue publish-flow — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- repo: {result.Repo}");
        writer.WriteLine($"- packet directory: {result.PacketDirectory}");
        writer.WriteLine($"- publish.yaml: {result.PublishYamlPath}");
        writer.WriteLine($"- packet exists: {(result.PacketExists ? "yes" : "no")}");
        writer.WriteLine($"- github-body.md present: {(result.GithubBodyPresent ? "yes" : "no")}");
        writer.WriteLine($"- mode: {result.Mode}");
        if (!string.IsNullOrWhiteSpace(result.Title))
        {
            writer.WriteLine($"- title: {result.Title}");
        }

        if (!string.IsNullOrWhiteSpace(result.TitleSource))
        {
            writer.WriteLine($"- title source: {result.TitleSource}");
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine($"- warnings: {string.Join(", ", result.Warnings)}");
        }

        writer.WriteLine();

        writer.WriteLine("## Contract validation");
        if (result.MissingContractSections.Count == 0)
        {
            writer.WriteLine("- missing sections: none");
        }
        else
        {
            writer.WriteLine("- missing sections:");
            foreach (var section in result.MissingContractSections)
            {
                writer.WriteLine($"  - {section}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Outcome");
        writer.WriteLine($"- created: {(result.Created ? "yes" : "no")}");
        writer.WriteLine($"- idempotent: {(result.Idempotent ? "yes" : "no")}");
        writer.WriteLine($"- durable_state_synced: {(result.DurableStateSynced ? "yes" : "no")}");
        if (result.WouldRestore is { Count: > 0 } wouldRestore)
        {
            writer.WriteLine($"- would_restore: {string.Join(", ", wouldRestore)}");
        }
        if (result.IssueNumber is { } issueNumber)
        {
            writer.WriteLine($"- issue number: {issueNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }
        if (!string.IsNullOrWhiteSpace(result.IssueUrl))
        {
            writer.WriteLine($"- issue URL: {result.IssueUrl}");
        }
        writer.WriteLine($"- queue_state_patched: {(result.QueueStatePatched ? "yes" : "no")}");
        writer.WriteLine($"- publish_yaml_patched: {(result.PublishYamlPatched ? "yes" : "no")}");
        writer.WriteLine($"- runs_appended: {(result.RunsAppended ? "yes" : "no")}");
        writer.WriteLine($"- intent-target applied: {(result.IntentTargetApplied ? "yes" : "no — apply only at the explicit publish boundary after parent durable state is pushed")}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine($"- error: {result.Error}");
        }
        writer.WriteLine();

        if (result.NextSteps.Count > 0)
        {
            writer.WriteLine("## Next steps");
            foreach (var step in result.NextSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    /// <summary>
    /// G290: title source constants reported on the publish result so the
    /// operator can audit which path resolved the title.
    /// </summary>
    public const string TitleSourcePacketYaml = "packet-yaml";
    public const string TitleSourceGithubBodyH1 = "github-body-h1";
    public const string TitleSourceFallbackUntitled = "fallback-untitled";

    /// <summary>
    /// G290: resolves the title in priority order: `packet.yaml` `title:` →
    /// body H1 (`# Title`) → fallback `<execution-unit> (untitled)`. Returns
    /// both the title and a structured source string so the caller can
    /// report which path resolved it (and emit a warning when the fallback
    /// fired).
    /// </summary>
    internal static (string Title, string Source) ResolveTitleWithSource(
        string executionUnit,
        string packetDirectory,
        string githubBodyPath)
    {
        // (1) Prefer packet.yaml `title:` when present and non-empty.
        var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");
        if (File.Exists(packetYamlPath))
        {
            var packetTitle = TryReadPacketTitle(packetYamlPath);
            if (!string.IsNullOrWhiteSpace(packetTitle))
            {
                return (packetTitle!, TitleSourcePacketYaml);
            }
        }

        // (2) Fall back to body H1 for packets that don't carry the title in
        // metadata (older packets, hand-authored bodies).
        if (File.Exists(githubBodyPath))
        {
            var lines = File.ReadAllLines(githubBodyPath);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (line.StartsWith("# ", StringComparison.Ordinal))
                {
                    return (line[2..].Trim(), TitleSourceGithubBodyH1);
                }

                break;
            }
        }

        // (3) Last-resort deterministic fallback. The caller surfaces this
        // as a warning so the operator can repair packet metadata.
        return ($"{executionUnit} (untitled)", TitleSourceFallbackUntitled);
    }

    /// <summary>
    /// G298: prepend the execution-unit token to the resolved title when the
    /// title does not already start with it. The operator scans the GitHub
    /// issue list by execution unit (G294, SKS-G190, etc.), so a title like
    /// <c>Add host branch policy ...</c> stored without the token would hide
    /// the issue's correlation key. Already-prefixed titles and the
    /// deterministic <c>&lt;id&gt; (untitled)</c> fallback are returned
    /// verbatim — we only insert the prefix when missing. The check is
    /// case-sensitive because execution units are uppercase identifiers.
    /// </summary>
    internal static string FormatIssueTitle(string executionUnit, string? resolvedTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);

        if (string.IsNullOrWhiteSpace(resolvedTitle))
        {
            return $"{executionUnit} (untitled)";
        }

        var trimmed = resolvedTitle.Trim();
        if (trimmed.StartsWith(executionUnit + " ", StringComparison.Ordinal)
            || string.Equals(trimmed, executionUnit, StringComparison.Ordinal)
            || trimmed.StartsWith(executionUnit + ":", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"{executionUnit} {trimmed}";
    }

    /// <summary>
    /// G290: reads the top-level <c>title:</c> scalar from packet.yaml. The
    /// packet schema places the field at the root (older packets) or under
    /// `implementation_issue_packet:` (Sekiban-style); we accept either by
    /// scanning for the first `title:` line that has a non-empty value, with
    /// optional surrounding quotes stripped.
    /// </summary>
    private static string? TryReadPacketTitle(string packetYamlPath)
    {
        try
        {
            using var reader = new StreamReader(packetYamlPath);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = trimmed["title:".Length..].Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (value.Length >= 2
                    && ((value[0] == '"' && value[^1] == '"')
                        || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (IOException)
        {
            // Unreadable packet.yaml — fall through to body H1 / untitled.
        }
        catch (UnauthorizedAccessException)
        {
            // Permission denied — fall through.
        }

        return null;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? executionUnit,
        out string? repo,
        out string? domainOverride,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = null;
        repo = null;
        domainOverride = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--write":
                    write = true;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                    {
                        error = $"Unknown argument '{argument}'.";
                        return false;
                    }

                    if (executionUnit is not null)
                    {
                        error = $"Only one execution-unit id is allowed (got '{executionUnit}' and '{argument}').";
                        return false;
                    }

                    executionUnit = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "An execution-unit id is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("issue publish-flow");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Validates the packet, creates the GitHub issue without intent-target, syncs parent durable state (queue-state, publish.yaml, runs.jsonl), and reports the publish boundary as a next step.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal interface IIssueCreator
{
    IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath);
}

internal sealed record IssueCreateOutcome(string IssueUrl);

/// <summary>
/// G536 round-4 review repair: corroborates against GitHub itself, before
/// the first-create path, whether an issue for this execution unit already
/// exists — classified zero / exactly-one / multiple, never a bare bool.
/// Matching requires BOTH the exact expected issue title (not a prefix —
/// "G53" must never match "G536 ...") AND the candidate's body matching
/// the exact local packet body that would have been (or was) posted to
/// GitHub, so a merely similarly-titled unrelated issue can never match.
/// This check never GUESSES or reconstructs identity from a fuzzy match —
/// zero classifies as "safe to create," exactly-one feeds the confirmed
/// GitHub identity into the same <see cref="PublishDurableArtifactAnalyzer"/>-
/// backed restoration path (never a `gh issue create`), and multiple fails
/// closed, non-mutating.
/// </summary>
internal interface IGitHubExistingIssueChecker
{
    GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody);
}

internal enum GitHubExistingIssueClassification
{
    None,
    Unique,
    Multiple,
}

internal sealed record GitHubExistingIssueLookupResult
{
    public required GitHubExistingIssueClassification Classification { get; init; }
    public int? IssueNumber { get; init; }
    public string? IssueUrl { get; init; }
}

/// <summary>
/// G536 round-5 review repair: enumerates ALL matching issues (open and
/// closed — <c>state=all</c>, matching the previous rounds' contract) via
/// GraphQL cursor pagination rather than a fixed <c>--limit</c>, so a real
/// duplicate can never be silently dropped by a result-count cap. Any page
/// that reports <c>hasNextPage: true</c> without an <c>endCursor</c>, or a
/// result set that does not terminate within <see cref="MaxPages"/> pages,
/// fails loud rather than silently truncating.
/// </summary>
internal sealed class GhCliExistingIssueChecker : IGitHubExistingIssueChecker
{
    // Safety ceiling, not a silent cap: if genuinely exceeded, FetchAllCandidates
    // throws (fails loud) rather than returning a truncated, incomplete result.
    private const int MaxPages = 50;

    private const string GraphQlQuery = """
        query($searchQuery: String!, $cursor: String) {
          search(query: $searchQuery, type: ISSUE, first: 100, after: $cursor) {
            pageInfo { hasNextPage endCursor }
            nodes {
              ... on Issue { number title url body }
            }
          }
        }
        """;

    /// <summary>
    /// Test seam: replaces the real <c>gh api graphql</c> shell-out for a
    /// single page. Production default shells to <c>gh</c>; tests inject a
    /// fake returning canned GraphQL JSON so the real pagination loop,
    /// truncation guard, and candidate classification/normalization below
    /// are exercised end-to-end without spawning a process.
    /// </summary>
    internal Func<IReadOnlyList<string>, string>? PageFetcherOverride { get; init; }

    public GitHubExistingIssueLookupResult FindExistingIssue(string repo, string executionUnit, string expectedTitle, string expectedBody)
    {
        var allCandidates = FetchAllCandidates(repo, executionUnit);
        return ClassifyCandidates(allCandidates, expectedTitle, expectedBody);
    }

    internal IReadOnlyList<GhIssueListEntry> FetchAllCandidates(string repo, string executionUnit)
    {
        var fetchPage = PageFetcherOverride ?? RunGh;
        // G536 round-6 review repair: `is:issue` — GraphQL search
        // `type: ISSUE` covers BOTH issues and pull requests; without this
        // qualifier a matching PR would come back as a node with no Issue
        // fields (deserializing to an empty/default entry) instead of
        // being excluded. `state:` is deliberately never added — omitting
        // it is what keeps both open and closed issues in scope.
        var searchQuery = $"repo:{repo} {executionUnit} in:title is:issue";
        var results = new List<GhIssueListEntry>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 0; ; page++)
        {
            if (page >= MaxPages)
            {
                throw new InvalidOperationException(
                    $"gh api graphql issue search for '{executionUnit}' on {repo} did not complete within "
                    + $"{MaxPages} pages ({MaxPages * 100} candidates); refusing to silently truncate the "
                    + "candidate set. Narrow the search or investigate an unexpectedly large result set.");
            }

            var arguments = new List<string> { "api", "graphql", "-f", $"query={GraphQlQuery}", "-f", $"searchQuery={searchQuery}" };
            if (cursor is not null)
            {
                arguments.Add("-f");
                arguments.Add($"cursor={cursor}");
            }

            var stdout = fetchPage(arguments);

            GraphQlResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<GraphQlResponse>(stdout);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"gh api graphql returned unparseable JSON: {exception.Message}");
            }

            // G536 round-6 review repair: a GraphQL response can carry a
            // non-empty `errors` array alongside partial `data` (the spec
            // permits both simultaneously) — that partial data must never
            // be treated as authoritative. Checked BEFORE touching `data`
            // at all.
            if (response?.Errors is { Count: > 0 } errors)
            {
                var messages = string.Join("; ", errors.Select(error => error.Message ?? "(no message)"));
                throw new InvalidOperationException(
                    $"gh api graphql returned error(s) for the issue-existence check on '{executionUnit}' ({repo}): {messages}");
            }

            var search = response?.Data?.Search
                ?? throw new InvalidOperationException("gh api graphql returned no search data for the issue-existence check.");

            // G536 round-7 review repair: a JSON `null` for a "non-nullable"
            // shape (pageInfo / nodes / an individual node) deserializes
            // silently rather than throwing — never dereference it without
            // an explicit check first, or an incomplete authoritative
            // response degrades into an incidental NullReferenceException
            // instead of an intentional provider diagnostic.
            if (search.PageInfo is null)
            {
                throw new InvalidOperationException("gh api graphql returned a search result with no pageInfo.");
            }

            if (search.Nodes is null)
            {
                throw new InvalidOperationException("gh api graphql returned a search result with no nodes array.");
            }

            // G536 round-6/7 review repair: validate every candidate BEFORE
            // accumulating it — a malformed/incomplete authoritative
            // response must fail loud here, not be silently carried
            // forward to discover only after classification (or worse,
            // after a restoration write) that the fetched identity was
            // never trustworthy.
            foreach (var node in search.Nodes)
            {
                if (node is null)
                {
                    throw new InvalidOperationException(
                        "gh api graphql returned a null node in the search results; refusing to process an "
                        + "incomplete authoritative response.");
                }

                results.Add(ValidateCandidate(node, repo));
            }

            if (!search.PageInfo.HasNextPage)
            {
                break;
            }

            // G536 round-7 review repair: null, empty, AND whitespace-only
            // are all a missing cursor — a bare `?? throw` only caught the
            // null case, letting an empty-string endCursor be sent back as
            // `cursor=` on the next request and recorded into seenCursors
            // as if it were a real value.
            var nextCursor = search.PageInfo.EndCursor;
            if (string.IsNullOrWhiteSpace(nextCursor))
            {
                throw new InvalidOperationException(
                    "gh api graphql reported hasNextPage=true with a missing endCursor (null, empty, or "
                    + "whitespace-only); refusing to silently stop pagination short of the real result set.");
            }

            if (!seenCursors.Add(nextCursor))
            {
                throw new InvalidOperationException(
                    $"gh api graphql returned repeated cursor '{nextCursor}'; the pagination loop would re-fetch "
                    + "the same page indefinitely. Refusing to loop until the safety cap — investigate the "
                    + "GraphQL search API response.");
            }

            cursor = nextCursor;
        }

        return results;
    }

    /// <summary>
    /// G536 round-6 review repair: a candidate must be a complete,
    /// self-consistent authoritative record before it is ever accumulated
    /// — a positive issue number, a non-null/non-empty title, a non-null
    /// body (required to prove content linkage later; a null body is an
    /// invalid provider response, never silently treated as empty text),
    /// and a URL matching the canonical
    /// <c>https://github.com/&lt;requested repo&gt;/issues/&lt;number&gt;</c>
    /// shape EXACTLY for the repo this check was scoped to.
    /// </summary>
    internal static GhIssueListEntry ValidateCandidate(GhIssueListEntry entry, string repo)
    {
        if (entry.Number <= 0)
        {
            throw new InvalidOperationException(
                $"gh api graphql returned a candidate with a non-positive issue number ({entry.Number}) for repo '{repo}'.");
        }

        if (string.IsNullOrEmpty(entry.Title))
        {
            throw new InvalidOperationException(
                $"gh api graphql returned candidate #{entry.Number} on '{repo}' with a null/empty title.");
        }

        if (entry.Body is null)
        {
            throw new InvalidOperationException(
                $"gh api graphql returned candidate #{entry.Number} on '{repo}' with a null body; cannot verify content linkage.");
        }

        var expectedUrl = $"https://github.com/{repo}/issues/{entry.Number}";
        if (!string.Equals(entry.Url, expectedUrl, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"gh api graphql returned candidate #{entry.Number} with url '{entry.Url ?? "(null)"}'; expected exactly '{expectedUrl}'.");
        }

        return entry;
    }

    private static string RunGh(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"gh api graphql exit {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }

    internal static GitHubExistingIssueLookupResult ClassifyCandidates(
        IReadOnlyList<GhIssueListEntry> allCandidates, string expectedTitle, string expectedBody)
    {
        var normalizedExpectedBody = NormalizeBody(expectedBody);
        var candidates = allCandidates
            .Where(entry => string.Equals(entry.Title, expectedTitle, StringComparison.Ordinal))
            .Where(entry => string.Equals(NormalizeBody(entry.Body ?? string.Empty), normalizedExpectedBody, StringComparison.Ordinal))
            .ToArray();

        return candidates.Length switch
        {
            0 => new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.None },
            1 => new GitHubExistingIssueLookupResult
            {
                Classification = GitHubExistingIssueClassification.Unique,
                IssueNumber = candidates[0].Number,
                IssueUrl = candidates[0].Url,
            },
            _ => new GitHubExistingIssueLookupResult { Classification = GitHubExistingIssueClassification.Multiple },
        };
    }

    /// <summary>
    /// G536 round-5 review repair: the only permitted normalization is
    /// line-ending conversion (CRLF/CR → LF) and equating "ends with
    /// exactly one trailing newline" with "no trailing newline" (GitHub's
    /// own storage/rendering convention). Unlike a blanket <c>Trim()</c>,
    /// this NEVER touches leading whitespace/indentation or any other
    /// interior/trailing whitespace — an indented Markdown code block (or
    /// any other authored whitespace) is semantically significant, so a
    /// body that differs only in a single trailing newline is treated as
    /// identical, but any other whitespace drift is treated as a genuine
    /// difference (not the same issue).
    /// </summary>
    internal static string NormalizeBody(string body)
    {
        var normalized = body.Replace("\r\n", "\n").Replace("\r", "\n");
        if (normalized.EndsWith('\n'))
        {
            normalized = normalized[..^1];
        }
        return normalized;
    }

    internal sealed record GhIssueListEntry(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("body")] string? Body);

    private sealed record GraphQlResponse(
        [property: JsonPropertyName("data")] GraphQlData? Data,
        [property: JsonPropertyName("errors")] List<GraphQlError>? Errors);

    private sealed record GraphQlError([property: JsonPropertyName("message")] string? Message);

    private sealed record GraphQlData([property: JsonPropertyName("search")] GraphQlSearch? Search);

    private sealed record GraphQlSearch(
        [property: JsonPropertyName("pageInfo")] GraphQlPageInfo? PageInfo,
        [property: JsonPropertyName("nodes")] List<GhIssueListEntry?>? Nodes);

    private sealed record GraphQlPageInfo(
        [property: JsonPropertyName("hasNextPage")] bool HasNextPage,
        [property: JsonPropertyName("endCursor")] string? EndCursor);
}

internal sealed class GhCliIssueCreator : IIssueCreator
{
    public IssueCreateOutcome CreateIssue(string repo, string title, string bodyFilePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom
        };
        startInfo.ArgumentList.Add("issue");
        startInfo.ArgumentList.Add("create");
        startInfo.ArgumentList.Add("--repo");
        startInfo.ArgumentList.Add(repo);
        startInfo.ArgumentList.Add("--title");
        startInfo.ArgumentList.Add(title);
        startInfo.ArgumentList.Add("--body-file");
        startInfo.ArgumentList.Add(bodyFilePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"gh issue create exit {process.ExitCode}: {stderr.Trim()}");
        }

        var url = stdout.Trim().Split('\n').LastOrDefault(line => line.StartsWith("https://", StringComparison.Ordinal))
            ?? stdout.Trim();
        return new IssueCreateOutcome(url);
    }
}

internal sealed record IssuePublishFlowResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("github_body_path")]
    public required string GithubBodyPath { get; init; }

    [JsonPropertyName("publish_yaml_path")]
    public required string PublishYamlPath { get; init; }

    [JsonPropertyName("packet_exists")]
    public required bool PacketExists { get; init; }

    [JsonPropertyName("github_body_present")]
    public required bool GithubBodyPresent { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>
    /// G298: explicit alias for <see cref="Title"/> emphasizing that this
    /// is the final title sent to <c>gh issue create</c>. Same value; the
    /// distinct field name lets operators and downstream tooling read
    /// "issue_title" without re-inferring it from <c>title</c> when audit
    /// trails care about the exact GitHub-side string.
    /// </summary>
    [JsonPropertyName("issue_title")]
    public string? IssueTitle => Title;

    /// <summary>
    /// G290: which path resolved <see cref="Title"/>. One of
    /// <see cref="IssuePublishFlowCommand.TitleSourcePacketYaml"/>,
    /// <see cref="IssuePublishFlowCommand.TitleSourceGithubBodyH1"/>, or
    /// <see cref="IssuePublishFlowCommand.TitleSourceFallbackUntitled"/>.
    /// Null for early-exit cases where the title was never resolved.
    /// </summary>
    [JsonPropertyName("title_source")]
    public string? TitleSource { get; init; }

    /// <summary>
    /// G290: structured warnings, e.g. <c>title-fallback</c> when the title
    /// resolved to <c>&lt;execution-unit&gt; (untitled)</c>.
    /// </summary>
    [JsonPropertyName("warnings")]
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    [JsonPropertyName("created")]
    public required bool Created { get; init; }

    [JsonPropertyName("idempotent")]
    public required bool Idempotent { get; init; }

    [JsonPropertyName("durable_state_synced")]
    public required bool DurableStateSynced { get; init; }

    [JsonPropertyName("issue_url")]
    public string? IssueUrl { get; init; }

    [JsonPropertyName("issue_number")]
    public int? IssueNumber { get; init; }

    [JsonPropertyName("queue_state_patched")]
    public required bool QueueStatePatched { get; init; }

    [JsonPropertyName("publish_yaml_patched")]
    public required bool PublishYamlPatched { get; init; }

    [JsonPropertyName("runs_appended")]
    public required bool RunsAppended { get; init; }

    /// <summary>
    /// G536: dry-run-only plan of durable artifacts that a subsequent
    /// <c>--write</c> rerun would restore, taken verbatim from
    /// <see cref="PublishDurableArtifactAnalysis.Gaps"/>. Null unless an
    /// existing issue identity was found in dry-run mode.
    /// </summary>
    [JsonPropertyName("would_restore")]
    public IReadOnlyList<string>? WouldRestore { get; init; }

    [JsonPropertyName("intent_target_applied")]
    public required bool IntentTargetApplied { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
