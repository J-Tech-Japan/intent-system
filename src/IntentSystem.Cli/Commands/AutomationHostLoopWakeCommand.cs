using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G450: <c>intent-cli automation host-loop-wake --repo &lt;owner/repo&gt;
/// [--domain &lt;d&gt;] [--write] [--format json|markdown]</c>.
///
/// One safe-wake orchestration command. It keeps the host loop's safety
/// ordering in code so an agent can invoke a single command and report the
/// structured result instead of choreographing the full preflight / sync /
/// review / closeout / publish / diagnostics sequence by hand. It:
///   1. gates on the installed-CLI surface probe (stale-cli ⇒ blocker, no
///      guessing);
///   2. delegates the highest-priority decision to the existing
///      <see cref="AutomationHostLoopNextActionCommand"/> (reusing ALL its
///      read-only probes and GitHub gathering);
///   3. maps the classification into one of four agent-facing wake actions —
///      <c>true-idle</c>, <c>review</c>, <c>publish</c>, <c>blocker</c> — and
///      enforces the at-most-one-PR / at-most-one-issue-publish invariant.
///
/// Read-only by default. Under <c>--write</c> it executes the documented SAFE,
/// no-judgement mutation lanes through existing surfaces:
/// <list type="bullet">
///   <item>deterministic host-metadata repair — <c>automation publish-recovery</c>
///         / <c>closeout-drift-check --write</c>;</item>
///   <item>next-slice publish — the deterministic chain <c>packet draft</c> →
///         <c>issue publish-flow --write</c> → <c>automation issue-publish
///         --write</c> (the candidate is already issue-cut-ready with an empty
///         WIP cap, so no expert judgement is involved).</item>
/// </list>
/// The one-PR / one-publish invariant is preserved. It performs no destructive
/// git operation and never auto-approves review. Review approval / request-update
/// transitions REQUIRE expert judgement and stay fail-closed: the single safe
/// command is surfaced as <c>pending_command</c> for the host to run after
/// review through the existing detailed commands. Any safe-lane step that fails
/// stays fail-closed (no further step runs, no unearned mutation claimed) and
/// records a warning.
/// </summary>
internal static class AutomationHostLoopWakeCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string OptRepo = "--repo";
    private const string OptDomain = "--domain";
    private const string OptWrite = "--write";
    private const string OptFormat = "--format";
    private const string OptHelp = "--help";

    /// <summary>G450: testability seam — the inner host-loop-next-action JSON
    /// producer. Production delegates to
    /// <see cref="AutomationHostLoopNextActionCommand.Execute"/>; tests inject a
    /// fake to model each classification without GitHub access.</summary>
    public static Func<CliContext, string[], TextWriter, int>? NextActionDelegate { get; set; }

    /// <summary>G450 (review fix): testability seam for the safe-mutation lane
    /// executor. Production delegates to the existing
    /// <see cref="AutomationPublishRecoveryCommand.Execute"/>; tests inject a
    /// fake to assert <c>--write</c> actually drives the existing surface.</summary>
    public static Func<CliContext, string[], TextWriter, int>? PublishRecoveryDelegate { get; set; }

    /// <summary>G450 (review fix): testability seam for the closeout-drift-check
    /// safe-mutation lane. Production delegates to
    /// <see cref="AutomationCloseoutDriftCheckCommand.Execute"/>.</summary>
    public static Func<CliContext, string[], TextWriter, int>? CloseoutDriftCheckDelegate { get; set; }

    /// <summary>G450 (review fix 2): testability seams for the deterministic
    /// next-slice publish chain (packet draft → issue publish-flow --write →
    /// automation issue-publish --write). Production delegates to the existing
    /// commands; tests inject fakes to assert the chain runs without real
    /// GitHub mutation.</summary>
    public static Func<CliContext, string[], TextWriter, int>? PacketDraftDelegate { get; set; }

    public static Func<CliContext, string[], TextWriter, int>? IssuePublishFlowDelegate { get; set; }

    public static Func<CliContext, string[], TextWriter, int>? IssuePublishDelegate { get; set; }

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

        if (!TryParseArguments(args, out var repo, out var domain, out var mode, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            writer.WriteLine("automation host-loop-wake requires '--repo <owner/repo>'.");
            return 1;
        }

        // 1. Installed-CLI surface gate — never guess past a stale/missing surface.
        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        if (!surfaceReport.Available)
        {
            var blocked = new AutomationHostLoopWakeResult
            {
                Repo = repo!,
                Domain = domain,
                Mode = mode,
                HostOnly = true,
                WakeAction = AutomationHostLoopWakeActions.Blocker,
                Classification = HostLoopNextActionAnalyzer.ClassificationStaleCli,
                MutationAllowed = false,
                RecommendedCommand = "intent-cli automation doctor --format json",
                CandidateExecutionUnit = null,
                ProcessedPrCount = 0,
                ProcessedIssuePublishCount = 0,
                Mutations = Array.Empty<string>(),
                Validations = [$"installed-cli-surface: unavailable ({surfaceReport.InstalledCliPath})"],
                WriteExecuted = false,
                PendingCommand = null,
                StopReason = "stale-host-cli: installed automation surface is missing/stale; refresh before any wake.",
                Evidence = ["automation doctor surface probe reported the installed CLI is missing or stale."],
                Summary = "blocker: stale-host-cli — refresh the installed CLI before the next host loop wake.",
            };
            Emit(writer, blocked, format);
            return 1;
        }

        // 2. Delegate the highest-priority decision to host-loop-next-action.
        var nextActionArgs = domain is null
            ? new[] { OptRepo, repo!, "--format", FormatJson }
            : new[] { OptRepo, repo!, OptDomain, domain, "--format", FormatJson };

        using var buffer = new StringWriter();
        int innerExit;
        try
        {
            var del = NextActionDelegate ?? AutomationHostLoopNextActionCommand.Execute;
            innerExit = del(context, nextActionArgs, buffer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            writer.WriteLine($"host-loop-wake failed to evaluate next action for {repo}: {exception.Message}");
            return 1;
        }

        if (innerExit != 0)
        {
            writer.WriteLine($"host-loop-wake: next-action evaluation failed (exit {innerExit.ToString(System.Globalization.CultureInfo.InvariantCulture)}).");
            return 1;
        }

        if (!TryParseNextAction(buffer.ToString(), out var inner, out var parseError))
        {
            writer.WriteLine($"host-loop-wake: could not parse next-action result: {parseError}");
            return 1;
        }

        var result = BuildResult(repo!, domain, mode, inner, surfaceReport.InstalledCliPath);

        // G450 (review fix): under --write, actually execute the documented safe
        // mutation lanes through existing surfaces, preserving the one-PR /
        // one-publish invariant. Review approval / request-update transitions
        // remain JUDGEMENT-gated (out of scope to auto-approve) and stay
        // fail-closed with pending_command surfaced.
        if (string.Equals(mode, AutomationHostLoopWakeModes.Write, StringComparison.Ordinal))
        {
            if (string.Equals(result.Classification, HostLoopNextActionAnalyzer.ClassificationRepairHostMetadata, StringComparison.Ordinal))
            {
                // Deterministic host-metadata repair.
                result = ExecuteSafeRepairLane(context, repo!, inner, result);
            }
            else if (string.Equals(result.WakeAction, AutomationHostLoopWakeActions.Publish, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(inner.CandidateExecutionUnit))
            {
                // G450 (review fix 2): the next-slice publish lane is
                // deterministic and requires NO expert judgement (the candidate
                // is already issue-cut-ready with an empty WIP cap), so --write
                // runs the documented publish chain through existing surfaces:
                // packet draft → issue publish-flow --write → automation
                // issue-publish --write.
                result = ExecutePublishChain(context, repo!, inner.CandidateExecutionUnit!, result);
            }
        }

        Emit(writer, result, format);
        return 0;
    }

    /// <summary>
    /// G450 (review fix 2): execute the deterministic next-slice publish chain
    /// through the existing surfaces. Fail-closed: if any step returns non-zero
    /// or cannot resolve the created issue number, no further step runs, no
    /// mutation is claimed beyond what actually succeeded, and a warning is
    /// recorded. Preserves the one-issue-publish invariant.
    /// </summary>
    private static AutomationHostLoopWakeResult ExecutePublishChain(
        CliContext context,
        string repo,
        string unit,
        AutomationHostLoopWakeResult result)
    {
        var packetDraft = PacketDraftDelegate ?? PacketDraftCommand.Execute;
        var publishFlow = IssuePublishFlowDelegate ?? IssuePublishFlowCommand.Execute;
        var issuePublish = IssuePublishDelegate ?? AutomationIssuePublishCommand.Execute;
        var mutations = new List<string>();

        // Step 1: packet draft (scaffold/validate the packet directory).
        using (var draftBuffer = new StringWriter())
        {
            int exit;
            try
            {
                exit = packetDraft(context, ["--execution-unit", unit, "--target-repo", repo, "--format", "json"], draftBuffer);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                return FailClosedPublish(result, $"packet draft for '{unit}' failed: {exception.Message}");
            }
            if (exit != 0)
            {
                return FailClosedPublish(result, $"packet draft for '{unit}' returned exit {exit.ToString(System.Globalization.CultureInfo.InvariantCulture)}; publish chain not started.");
            }
            mutations.Add($"packet draft --execution-unit {unit}");
        }

        // Step 2: issue publish-flow --write (creates the GitHub issue + durable state).
        int? issueNumber;
        using (var flowBuffer = new StringWriter())
        {
            int exit;
            try
            {
                exit = publishFlow(context, [unit, "--repo", repo, "--write", "--format", "json"], flowBuffer);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                return FailClosedPublish(result, $"issue publish-flow for '{unit}' failed: {exception.Message}");
            }
            if (exit != 0)
            {
                return FailClosedPublish(result, $"issue publish-flow for '{unit}' returned exit {exit.ToString(System.Globalization.CultureInfo.InvariantCulture)}; no issue-publish performed.");
            }
            issueNumber = ExtractIssueNumber(flowBuffer.ToString());
            mutations.Add($"issue publish-flow {unit} --write" + (issueNumber is { } n ? $" (#{n.ToString(System.Globalization.CultureInfo.InvariantCulture)})" : string.Empty));
        }

        if (issueNumber is not { } createdIssue)
        {
            // Issue created but number not resolvable — do NOT guess; surface for
            // the host to apply intent-target via the existing surface.
            return result with
            {
                WriteExecuted = true,
                Mutations = mutations,
                PendingCommand = $"intent-cli automation issue-publish --repo {repo} --issue <n> --write",
                StopReason = "publish chain created the issue but the issue number was not resolvable; apply intent-target via automation issue-publish.",
                Summary = $"host-loop-wake: published '{unit}' (issue-publish label step deferred — number unresolved).",
                Warnings = [.. result.Warnings, "issue publish-flow did not surface an issue_number; intent-target not applied automatically."],
            };
        }

        // Step 3: automation issue-publish --write (applies intent-target).
        using (var publishBuffer = new StringWriter())
        {
            int exit;
            try
            {
                exit = issuePublish(context, ["--repo", repo, "--issue", createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture), "--write", "--format", "json"], publishBuffer);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
            {
                return result with
                {
                    WriteExecuted = true,
                    Mutations = mutations,
                    Warnings = [.. result.Warnings, $"automation issue-publish for #{createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture)} failed: {exception.Message}"],
                };
            }
            if (exit != 0)
            {
                return result with
                {
                    WriteExecuted = true,
                    Mutations = mutations,
                    Warnings = [.. result.Warnings, $"automation issue-publish for #{createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture)} returned exit {exit.ToString(System.Globalization.CultureInfo.InvariantCulture)}."],
                };
            }
            mutations.Add($"automation issue-publish --issue {createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture)} --write");
        }

        return result with
        {
            WriteExecuted = true,
            PendingCommand = null,
            Mutations = mutations,
            StopReason = $"published next-slice issue #{createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture)} for '{unit}' via the existing publish chain.",
            Summary = $"host-loop-wake: published next-slice issue #{createdIssue.ToString(System.Globalization.CultureInfo.InvariantCulture)} for '{unit}'.",
        };
    }

    private static AutomationHostLoopWakeResult FailClosedPublish(AutomationHostLoopWakeResult result, string warning) =>
        result with { Warnings = [.. result.Warnings, warning] };

    private static int? ExtractIssueNumber(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("issue_number", out var n)
                && n.ValueKind == JsonValueKind.Number
                ? n.GetInt32()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// G450 (review fix): execute the safe host-metadata repair lane through the
    /// existing <c>automation publish-recovery --write</c> / <c>closeout-drift-check
    /// --write</c> surface. These are deterministic, forward-only queue-state
    /// repairs (each gated by its own command's fail-closed logic), so running
    /// them from the one-wake command is safe. Returns an updated result with
    /// <c>write_executed</c> / <c>mutations</c> populated on success; on failure
    /// it stays fail-closed (no mutation claimed) and records a warning.
    /// </summary>
    private static AutomationHostLoopWakeResult ExecuteSafeRepairLane(
        CliContext context,
        string repo,
        NextActionProjection inner,
        AutomationHostLoopWakeResult result)
    {
        var recommended = inner.RecommendedCommand ?? string.Empty;
        string command;
        Func<CliContext, string[], TextWriter, int> executor;
        if (recommended.Contains("publish-recovery", StringComparison.Ordinal))
        {
            command = "automation publish-recovery";
            executor = PublishRecoveryDelegate ?? AutomationPublishRecoveryCommand.Execute;
        }
        else if (recommended.Contains("closeout-drift-check", StringComparison.Ordinal))
        {
            command = "automation closeout-drift-check";
            executor = CloseoutDriftCheckDelegate ?? AutomationCloseoutDriftCheckCommand.Execute;
        }
        else
        {
            // Unknown repair command — stay fail-closed and keep pending_command.
            return result with
            {
                Warnings = [.. result.Warnings, $"repair lane recommended an unrecognized command; not auto-executed: {recommended}"],
            };
        }

        using var buffer = new StringWriter();
        int exit;
        try
        {
            exit = executor(context, ["--repo", repo, "--write", "--format", "json"], buffer);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            return result with
            {
                Warnings = [.. result.Warnings, $"safe repair lane failed to execute `{command} --write`: {exception.Message}"],
            };
        }

        if (exit != 0)
        {
            return result with
            {
                Warnings = [.. result.Warnings, $"`{command} --write` returned exit {exit.ToString(System.Globalization.CultureInfo.InvariantCulture)}; no mutation claimed (fail-closed)."],
            };
        }

        var detail = ExtractSummary(buffer.ToString()) ?? $"{command} --write completed.";
        return result with
        {
            WriteExecuted = true,
            PendingCommand = null,
            Mutations = [$"{command} --write: {detail}"],
            StopReason = $"safe repair executed via existing surface ({command} --write).",
            Summary = $"host-loop-wake: executed safe host-metadata repair ({command} --write).",
        };
    }

    private static string? ExtractSummary(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("summary", out var s)
                && s.ValueKind == JsonValueKind.String
                ? s.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AutomationHostLoopWakeResult BuildResult(
        string repo,
        string? domain,
        string mode,
        NextActionProjection inner,
        string installedCliPath)
    {
        var wakeAction = MapWakeAction(inner.Classification);
        var processedPr = wakeAction == AutomationHostLoopWakeActions.Review ? 1 : 0;
        var processedPublish = wakeAction == AutomationHostLoopWakeActions.Publish ? 1 : 0;

        var isWrite = string.Equals(mode, AutomationHostLoopWakeModes.Write, StringComparison.Ordinal);
        // Fail-closed: this slice never performs a mutation itself. When a
        // mutation lane is active under --write, surface the single safe command
        // the host must run through the existing detailed surface.
        var pendingCommand = isWrite && inner.MutationAllowed ? inner.RecommendedCommand : null;

        var stopReason = wakeAction switch
        {
            AutomationHostLoopWakeActions.TrueIdle =>
                "true-idle: no actionable host review, publish, or repair this wake.",
            AutomationHostLoopWakeActions.Review =>
                $"review action selected ({inner.Classification}); run the recommended review command.",
            AutomationHostLoopWakeActions.Publish =>
                $"publish action selected for candidate '{inner.CandidateExecutionUnit ?? "<unit>"}'.",
            _ => $"blocker ({inner.Classification}): {inner.Summary}",
        };

        var evidence = new List<string>(inner.Evidence)
        {
            isWrite
                ? "--write executes the safe no-judgement lanes (host-metadata repair, next-slice publish chain) through existing surfaces; review approval/request-update stay judgement-gated and surface pending_command."
                : "read-only wake: no mutation performed; the recommended command is advisory.",
        };

        return new AutomationHostLoopWakeResult
        {
            Repo = repo,
            Domain = domain,
            Mode = mode,
            HostOnly = true,
            WakeAction = wakeAction,
            Classification = inner.Classification,
            MutationAllowed = inner.MutationAllowed,
            RecommendedCommand = inner.RecommendedCommand,
            CandidateExecutionUnit = inner.CandidateExecutionUnit,
            ProcessedPrCount = processedPr,
            ProcessedIssuePublishCount = processedPublish,
            Mutations = Array.Empty<string>(),
            Validations = [$"installed-cli-surface: available ({installedCliPath})"],
            WriteExecuted = false,
            PendingCommand = pendingCommand,
            StopReason = stopReason,
            Evidence = evidence,
            Summary = BuildSummary(wakeAction, inner),
            GithubApiStatus = inner.GithubApiStatus,
            Degraded = inner.Degraded,
            Cause = inner.Cause,
            DegradedState = inner.DegradedState,
        };
    }

    private static string BuildSummary(string wakeAction, NextActionProjection inner) => wakeAction switch
    {
        AutomationHostLoopWakeActions.TrueIdle => "host-loop-wake: true-idle — nothing to do this wake.",
        AutomationHostLoopWakeActions.Review => $"host-loop-wake: review action ({inner.Classification}).",
        AutomationHostLoopWakeActions.Publish => $"host-loop-wake: publish action for '{inner.CandidateExecutionUnit ?? "<unit>"}'.",
        _ => $"host-loop-wake: blocker ({inner.Classification}).",
    };

    private static string MapWakeAction(string classification) => classification switch
    {
        HostLoopNextActionAnalyzer.ClassificationTrueIdle => AutomationHostLoopWakeActions.TrueIdle,
        HostLoopNextActionAnalyzer.ClassificationReviewPr => AutomationHostLoopWakeActions.Review,
        HostLoopNextActionAnalyzer.ClassificationApprovedPrMergeCloseout => AutomationHostLoopWakeActions.Review,
        HostLoopNextActionAnalyzer.ClassificationApprovedPrDraftBlocked => AutomationHostLoopWakeActions.Review,
        HostLoopNextActionAnalyzer.ClassificationApprovedPrMergeBlocked => AutomationHostLoopWakeActions.Review,
        HostLoopNextActionAnalyzer.ClassificationApprovedPrMetadataBlocked => AutomationHostLoopWakeActions.Review,
        HostLoopNextActionAnalyzer.ClassificationPublishNextIssue => AutomationHostLoopWakeActions.Publish,
        _ => AutomationHostLoopWakeActions.Blocker,
    };

    private static bool TryParseNextAction(string json, out NextActionProjection projection, out string error)
    {
        projection = default!;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "empty next-action output";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("classification", out var classificationElement)
                || classificationElement.ValueKind != JsonValueKind.String)
            {
                error = "missing 'classification' field";
                return false;
            }

            var evidence = new List<string>();
            if (root.TryGetProperty("evidence", out var evidenceElement)
                && evidenceElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in evidenceElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            evidence.Add(value);
                        }
                    }
                }
            }

            projection = new NextActionProjection
            {
                Classification = classificationElement.GetString()!,
                MutationAllowed = root.TryGetProperty("mutation_allowed", out var ma)
                    && ma.ValueKind == JsonValueKind.True,
                RecommendedCommand = ReadString(root, "recommended_command"),
                CandidateExecutionUnit = ReadString(root, "candidate_execution_unit"),
                Summary = ReadString(root, "summary") ?? string.Empty,
                Evidence = evidence,
                GithubApiStatus = ReadString(root, "github_api_status")
                    ?? (root.TryGetProperty("degraded", out var statusDegraded)
                        && statusDegraded.ValueKind == JsonValueKind.True
                        ? GitHubApiQuotaConstants.Degraded
                        : GitHubApiQuotaConstants.Healthy),
                Degraded = root.TryGetProperty("degraded", out var degraded)
                    && degraded.ValueKind == JsonValueKind.True,
                Cause = ReadString(root, "cause"),
                DegradedState = root.TryGetProperty("degraded_state", out var degradedState)
                    && degradedState.ValueKind == JsonValueKind.Object
                    ? JsonSerializer.Deserialize<GitHubApiDegradedState>(degradedState.GetRawText())
                    : null,
            };
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? domain,
        out string mode,
        out string format,
        out string error)
    {
        repo = null;
        domain = null;
        mode = AutomationHostLoopWakeModes.ReadOnly;
        format = FormatJson;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case OptRepo:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case OptDomain:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case OptWrite:
                    mode = AutomationHostLoopWakeModes.Write;
                    break;
                case OptFormat:
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (json or markdown).";
                        return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'json' or 'markdown' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'. Supported: --repo <owner/repo> [--domain <d>] [--write] [--format json|markdown].";
                    return false;
            }
        }

        return true;
    }

    private static void Emit(TextWriter writer, AutomationHostLoopWakeResult result, string format)
    {
        if (string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
        {
            WriteMarkdown(writer, result);
        }
        else
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
    }

    private static void WriteMarkdown(TextWriter writer, AutomationHostLoopWakeResult result)
    {
        writer.WriteLine($"# automation host-loop-wake (G450) — `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- wake_action: **{result.WakeAction}**");
        writer.WriteLine($"- classification: {result.Classification}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- mutation_allowed: {(result.MutationAllowed ? "yes" : "no")}");
        writer.WriteLine($"- processed_pr_count: {result.ProcessedPrCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"- processed_issue_publish_count: {result.ProcessedIssuePublishCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrEmpty(result.RecommendedCommand))
        {
            writer.WriteLine($"- recommended_command: `{result.RecommendedCommand}`");
        }
        if (!string.IsNullOrEmpty(result.PendingCommand))
        {
            writer.WriteLine($"- pending_command: `{result.PendingCommand}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        writer.WriteLine();
        writer.WriteLine($"stop_reason: {result.StopReason}");
        if (result.Evidence.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Evidence");
            foreach (var line in result.Evidence)
            {
                writer.WriteLine($"- {line}");
            }
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation host-loop-wake");
        writer.WriteLine("Usage: intent-cli automation host-loop-wake --repo <owner/repo> [--domain <d>] [--write] [--format json|markdown]");
        writer.WriteLine("One safe-wake orchestration: gates on installed-CLI surface, reuses host-loop-next-action, and reports one of true-idle / review / publish / blocker.");
        writer.WriteLine("Read-only by default. --write runs the safe no-judgement lanes (host-metadata repair; next-slice publish chain packet draft -> issue publish-flow --write -> automation issue-publish --write) via existing surfaces; review approval/request-update stay judgement-gated and surface pending_command.");
        writer.WriteLine("Processes at most one PR review/closeout and one issue publish per wake. Existing detailed commands remain available.");
    }

    private sealed record NextActionProjection
    {
        public required string Classification { get; init; }
        public required bool MutationAllowed { get; init; }
        public string? RecommendedCommand { get; init; }
        public string? CandidateExecutionUnit { get; init; }
        public required string Summary { get; init; }
        public required IReadOnlyList<string> Evidence { get; init; }
        public string GithubApiStatus { get; init; } = GitHubApiQuotaConstants.Healthy;
        public bool Degraded { get; init; }
        public string? Cause { get; init; }
        public GitHubApiDegradedState? DegradedState { get; init; }
    }
}
