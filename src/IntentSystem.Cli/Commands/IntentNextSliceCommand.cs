using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G243: Read-only <c>intent-cli intent next-slice --dry-run</c> command.
/// Reports the deterministic planning facts an AI agent needs before
/// drafting next-slice packet content: queue-state WIP, open clarification
/// blockers, preloaded packet candidates, and Child Issue Contract
/// missing-field findings. Never mutates state. Never creates GitHub
/// issues. Never applies labels. Never launches an AI provider.
/// </summary>
internal static class IntentNextSliceCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string OutcomeIssueCutReady = "issue-cut-ready";
    private const string OutcomeSkipDueToWip = "skip-next-slice-due-to-wip";
    private const string OutcomeClarificationRequired = "clarification-required";
    private const string OutcomeNoActionableItem = "no-actionable-item";
    /// <summary>
    /// G328: emitted when no prepared packet exists and runtime
    /// creation is NOT permitted (the operator did not pass
    /// <c>--runtime-creation-allowed</c>). Signals to the host loop
    /// that the situation is not truly idle — the next move is a
    /// design-side packet draft, not a wait-and-retry. The
    /// host-loop-next-action analyzer maps this onto its own
    /// <c>design-needed</c> classification (G328).
    /// </summary>
    internal const string OutcomeDesignNeeded = "design-needed";

    /// <summary>
    /// G354: emitted when the packet candidate has missing contract sections
    /// but ALL of them are <c>mechanical-repairable</c> (e.g.
    /// <c>Verification</c>, <c>Related Links</c>). The result carries
    /// per-section repair guidance so the host agent can append the
    /// missing boilerplate without operator input and immediately
    /// re-run to reach <c>issue-cut-ready</c>.
    /// </summary>
    internal const string OutcomePacketGapMechanicalRepairable = "packet-gap-mechanical-repairable";

    // G285: surfaced in `warnings` when the clarification file has stale
    // front-matter (`intent_state: open`) but the body explicitly records no
    // current blockers / open questions. The candidate may still publish.
    internal const string WarningStaleClarificationMetadata = "stale-clarification-metadata";

    private const string UsageLine =
        "Usage: intent-cli intent next-slice --dry-run [--domain <name>] [--target-repo <owner/repo>] [--runtime-creation-allowed] [--format json|markdown]";

    // G433: use the same required section list as publish-flow so readiness
    // and publish validation are always consistent.
    private static IReadOnlyList<string> RequiredContractSections =>
        PacketDraftCommand.RequiredContractSections;

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

        if (!TryParseArguments(args, out var dryRun, out var domainOverride, out var targetRepo, out var runtimeCreationAllowed, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!dryRun)
        {
            writer.WriteLine("--dry-run is required. Mutating modes are not implemented in this command.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Analyze(context, domainOverride, targetRepo, runtimeCreationAllowed);

        if (string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
        {
            WriteMarkdown(writer, result);
        }
        else
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }

        return 0;
    }

    internal static IntentNextSliceResult Analyze(CliContext context, string? domainOverride, string? targetRepo)
    {
        return Analyze(context, domainOverride, targetRepo, runtimeCreationAllowed: false);
    }

    /// <summary>
    /// G328 overload — accepts an explicit <paramref name="runtimeCreationAllowed"/>
    /// flag. When false (the safe default), absence of a prepared
    /// packet causes the recommendation to surface
    /// <see cref="OutcomeDesignNeeded"/> rather than
    /// <see cref="OutcomeNoActionableItem"/>, so the host loop can
    /// tell the difference between "truly idle" and "needs design
    /// work before any next slice can be cut".
    /// </summary>
    internal static IntentNextSliceResult Analyze(
        CliContext context,
        string? domainOverride,
        string? targetRepo,
        bool runtimeCreationAllowed)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        // G332: when the caller names a target repo, route the
        // queue-state read through the runtime-scoped resolver so the
        // WIP gate sees the scoped state for the (domain, target-repo)
        // pair rather than unrelated root state. Pre-migration hosts
        // with only the legacy root file fall through automatically
        // (StateLocationKind.Legacy). When no target repo is supplied
        // the read keeps its pre-G332 root behavior byte-identically.
        string queueStatePath;
        string? stateLayout = null;
        if (!string.IsNullOrWhiteSpace(targetRepo))
        {
            var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
                context.RepoRoot, domain, targetRepo!);
            queueStatePath = location.Path;
            stateLayout = location.Kind switch
            {
                StateLocationKind.Scoped => "scoped",
                StateLocationKind.Legacy => "legacy-fallback",
                _ => "scoped"
            };
        }
        else
        {
            queueStatePath = context.GetQueueStatePath();
        }
        QueueState? queueState = null;
        var notes = new List<string>();

        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                notes.Add($"queue-state JSON could not be parsed: {jsonException.Message}");
            }
            catch (InvalidOperationException invalidOperation)
            {
                notes.Add($"queue-state payload was invalid: {invalidOperation.Message}");
            }
        }
        else
        {
            notes.Add($"no queue-state file at {queueStatePath}");
        }

        var packetRoot = Path.Combine(context.RepoRoot, ".intent-cli", "issues");

        // G359 (PR #822 review repair): load the domain's
        // execution_unit_regex BEFORE the WIP pass so the same scope
        // check applied to packet directories also applies to queue
        // items.
        //
        // PR #824 review repair #3: use the structured Resolve()
        // outcome instead of TryLoad so callers can distinguish
        // "no domain configured" (legacy fallback) from "domain
        // configured but bindings.md missing" and "bindings present
        // but pattern invalid".
        //
        // G439: only fail closed on InvalidPattern (an explicitly
        // broken regex the operator must fix). When bindings.md is
        // absent/missing the namespace constraint simply isn't
        // configured yet — treat it like a null regex (open filter,
        // same as MatchesExecutionUnitRegex(null, …) = true) so a
        // complete queued packet isn't blocked solely because
        // bindings.md hasn't been authored. Domain/repo protection
        // continues via MatchesDomainAndRepoFilter regardless.
        var bindingsResolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, domain);
        var executionUnitRegex = bindingsResolution.Regex;
        var bindingsFailedClosed = !string.IsNullOrWhiteSpace(domainOverride)
            && bindingsResolution.Kind == ExecutionUnitRegexResolutionKind.InvalidPattern;
        if (bindingsFailedClosed
            || (!string.IsNullOrWhiteSpace(domainOverride)
                && bindingsResolution.Kind == ExecutionUnitRegexResolutionKind.MissingOrAbsent))
        {
            // Add a structured note so the operator can see the gap
            // (missing bindings.md, missing field, or invalid pattern).
            var kindLabel = bindingsResolution.Kind switch
            {
                ExecutionUnitRegexResolutionKind.MissingOrAbsent => "missing-domain-bindings",
                ExecutionUnitRegexResolutionKind.InvalidPattern => "invalid-domain-bindings",
                _ => bindingsResolution.Kind.ToString().ToLowerInvariant(),
            };
            notes.Add(
                $"domain `{domain}` bindings.md `{bindingsResolution.BindingsPath ?? "(unresolved)"}` "
                + $"reported `{kindLabel}` "
                + (bindingsResolution.Detail is { Length: > 0 } ? $"({bindingsResolution.Detail}); " : "; ")
                + (bindingsFailedClosed
                    ? "next-slice refuses to surface a candidate without verifying the active "
                      + "domain boundary (PR #824 review repair #3)."
                    : "next-slice will surface candidates without an execution-unit namespace check; "
                      + "add execution_unit_regex to bindings.md to restrict the domain boundary (G439)."));
        }

        var wip = new List<string>();
        var queued = new List<string>();
        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (queueState is not null)
        {
            foreach (var item in queueState.Items)
            {
                switch (item.State)
                {
                    case QueueItemState.Active:
                    case QueueItemState.Review:
                    case QueueItemState.Fixing:
                        // G275: filter WIP by the same domain/repo predicates as candidate selection
                        // so cross-domain in-flight items do not block the requested lane.
                        // G359 (PR #822 review repair): also enforce the
                        // domain bindings execution_unit_regex on queue
                        // items so a misnamed cross-namespace WIP item
                        // cannot block the requested domain lane.
                        if (MatchesDomainAndRepoFilter(domain, targetRepo, queueState, item.ExecutionUnit, Path.Combine(packetRoot, item.ExecutionUnit))
                            && MatchesExecutionUnitRegex(executionUnitRegex, item.ExecutionUnit))
                        {
                            wip.Add(item.ExecutionUnit);
                        }

                        break;

                    case QueueItemState.Queued:
                        queued.Add(item.ExecutionUnit);
                        break;

                    case QueueItemState.Completed:
                        completed.Add(item.ExecutionUnit);
                        break;
                }
            }
        }

        var clarificationPath = Path.Combine(context.RepoRoot, "intents", domain, "clarifications", "open.md");
        var clarificationFilePresent = File.Exists(clarificationPath);
        ClarificationStateAnalysis? clarificationAnalysis = null;
        if (clarificationFilePresent)
        {
            clarificationAnalysis = ClarificationOpenDetector.Analyze(File.ReadAllText(clarificationPath));
        }

        var clarificationOpen = clarificationAnalysis?.HasOpenBlocker ?? false;
        var staleClarificationMetadata = clarificationAnalysis?.StaleClarificationMetadata ?? false;

        // G302: structured clarifications under
        // `intents/<domain>/clarifications/*.toml` block next-slice the
        // same way the legacy markdown blocker does.
        try
        {
            if (StructuredClarificationsDirectory.HasOpenBlocker(context.RepoRoot, domain))
            {
                clarificationOpen = true;
            }
        }
        catch (InvalidOperationException)
        {
            // Treat parse errors as non-blocking here; the dedicated
            // `clarification status` surface returns the structured error
            // when the operator audits it directly.
        }
        // G359: `executionUnitRegex` is loaded above (before the WIP
        // pass) so both queue items and packet directories are scoped
        // to the requested domain's namespace via the same regex.
        // Missing bindings file / missing field / invalid regex still
        // degrades to "no name filter" so pre-G359 hosts continue to
        // behave byte-identically.

        IntentNextSliceCandidate? candidate = null;
        // PR #824 review repair #3 / G439: only block candidate selection
        // when bindings have an INVALID pattern (operator must fix the
        // regex). When bindings.md is simply absent, domain/repo
        // protection from MatchesDomainAndRepoFilter is still applied and
        // the note added above informs the operator. This aligns
        // next-slice with host-review-diagnostics which never gates on the
        // execution_unit_regex.
        if (Directory.Exists(packetRoot) && !bindingsFailedClosed)
        {
            // Pick the first queued execution_unit in queue-state order whose packet
            // directory exists AND whose domain/target-repo matches the filter.
            // Falls back to the alphabetically-first packet directory not in completed
            // if no queued match exists.
            //
            // G275: When --domain and/or --target-repo are specified, packets are
            // filtered by those values. Domain is derived from the queue item's
            // clarification_return_path (shape: intents/<domain>/clarifications/open.md).
            // Target-repo is read from the packet.yaml file inside the packet directory.
            // G359: Additionally, when the domain's bindings.md declares an
            // execution_unit_regex, only packet directory names matching that
            // regex are considered. This stops a shared packet root from
            // surfacing a wrong-namespace candidate (e.g. SKS-G365 leaking
            // into --domain intent-cli).
            foreach (var executionUnit in queued)
            {
                var directory = Path.Combine(packetRoot, executionUnit);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                if (!MatchesExecutionUnitRegex(executionUnitRegex, executionUnit))
                {
                    continue;
                }

                if (!MatchesDomainAndRepoFilter(domain, targetRepo, queueState, executionUnit, directory))
                {
                    continue;
                }

                candidate = BuildCandidate(executionUnit, directory);
                break;
            }

            if (candidate is null)
            {
                foreach (var directory in Directory
                    .EnumerateDirectories(packetRoot)
                    .OrderBy(path => path, StringComparer.Ordinal))
                {
                    var executionUnit = Path.GetFileName(directory)!;
                    if (completed.Contains(executionUnit))
                    {
                        continue;
                    }

                    if (!MatchesExecutionUnitRegex(executionUnitRegex, executionUnit))
                    {
                        continue;
                    }

                    if (!MatchesDomainAndRepoFilter(domain, targetRepo, queueState, executionUnit, directory))
                    {
                        continue;
                    }

                    candidate = BuildCandidate(executionUnit, directory);
                    break;
                }
            }
        }

        var recommendedOutcome = ComputeRecommendedOutcome(
            clarificationOpen,
            wip.Count > 0,
            candidate,
            runtimeCreationAllowed);

        var warnings = new List<string>();
        if (staleClarificationMetadata)
        {
            // G285: surface stale front-matter so the host can repair the file
            // later. The boolean `clarificationOpen` is already false because
            // the body explicitly records no current blockers / questions, so
            // a complete candidate still proceeds to `issue-cut-ready`.
            warnings.Add(WarningStaleClarificationMetadata);
        }

        return new IntentNextSliceResult
        {
            Domain = domain,
            TargetRepo = targetRepo,
            DryRun = true,
            QueueStatePath = queueStatePath,
            QueueStatePresent = queueState is not null,
            Wip = wip,
            ClarificationPath = clarificationPath,
            ClarificationFilePresent = clarificationFilePresent,
            ClarificationOpen = clarificationOpen,
            StaleClarificationMetadata = staleClarificationMetadata,
            PacketRoot = packetRoot,
            Candidate = candidate,
            RecommendedOutcome = recommendedOutcome,
            RuntimeCreationAllowed = runtimeCreationAllowed,
            StateLayout = stateLayout,
            Warnings = warnings,
            Notes = notes
        };
    }

    private static IntentNextSliceCandidate BuildCandidate(string executionUnit, string directory)
    {
        var githubBodyPath = Path.Combine(directory, "github-body.md");
        var githubBodyPresent = File.Exists(githubBodyPath);
        var missing = new List<string>();

        if (githubBodyPresent)
        {
            var content = File.ReadAllText(githubBodyPath);
            foreach (var section in RequiredContractSections)
            {
                if (!ContainsSectionHeading(content, section))
                {
                    missing.Add(section);
                }
            }
        }
        else
        {
            missing.AddRange(RequiredContractSections);
        }

        // G328: surface packet provenance so downstream tooling (host
        // loop / packet publish / closeout) can audit which role
        // authored the packet. Pre-G328 packets fall back to
        // `default-design` per `NextSlicePacketProvenanceReader`.
        var provenance = NextSlicePacketProvenanceReader.Read(directory);

        // G354: classify missing sections as mechanical-repairable vs
        // product-clarification-required so the host agent knows whether
        // it can apply boilerplate templates autonomously or must wait
        // for an operator/product decision.
        var gapAnalysis = missing.Count > 0
            ? PacketContractGapAnalyzer.Analyze(missing, directory)
            : null;

        return new IntentNextSliceCandidate
        {
            ExecutionUnit = executionUnit,
            PacketDirectory = directory,
            GithubBodyPresent = githubBodyPresent,
            MissingContractSections = missing,
            GapAnalysis = gapAnalysis,
            Provenance = provenance
        };
    }

    private static bool ContainsSectionHeading(string content, string section)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line.TrimStart('#').Trim();
            if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeRecommendedOutcome(
        bool clarificationOpen,
        bool wipPresent,
        IntentNextSliceCandidate? candidate,
        bool runtimeCreationAllowed)
    {
        if (clarificationOpen)
        {
            return OutcomeClarificationRequired;
        }

        if (wipPresent)
        {
            return OutcomeSkipDueToWip;
        }

        if (candidate is null)
        {
            // G328: when no prepared packet exists, the recommendation
            // splits on whether runtime creation is permitted. The
            // safe default (runtime creation NOT allowed) surfaces
            // `design-needed` so the host loop never reports
            // true-idle while a design-side packet draft is the
            // actual next move. Operators / review-runtime workspaces
            // that have been granted runtime-creation authority can
            // pass `--runtime-creation-allowed` to fall through to
            // the pre-G328 `no-actionable-item` semantics.
            return runtimeCreationAllowed
                ? OutcomeNoActionableItem
                : OutcomeDesignNeeded;
        }

        if (candidate.MissingContractSections.Count > 0)
        {
            // G354: when every missing section is mechanical-repairable
            // (Verification, Related Links), surface the specific repair
            // outcome so the host agent can apply the boilerplate templates
            // autonomously — no operator clarification needed.
            if (candidate.GapAnalysis is not null
                && candidate.GapAnalysis.OverallClassification ==
                    PacketContractGapAnalyzer.ClassificationMechanicalRepairable)
            {
                return OutcomePacketGapMechanicalRepairable;
            }

            return OutcomeClarificationRequired;
        }

        return OutcomeIssueCutReady;
    }

    /// <summary>
    /// G359: Returns true when the given <paramref name="executionUnit"/>
    /// matches the domain's bindings.md <c>execution_unit_regex</c>. When
    /// <paramref name="regex"/> is null (bindings file missing, field
    /// missing, or invalid pattern) the filter is treated as "open" —
    /// every unit passes so pre-G359 hosts and misconfigured bindings
    /// never silently block all candidates.
    /// </summary>
    private static bool MatchesExecutionUnitRegex(Regex? regex, string executionUnit)
    {
        if (regex is null)
        {
            return true;
        }

        if (string.IsNullOrEmpty(executionUnit))
        {
            return false;
        }

        try
        {
            return regex.IsMatch(executionUnit);
        }
        catch (RegexMatchTimeoutException)
        {
            // Treat catastrophic regex evaluation as no-match so a
            // pathological binding cannot keep a stale candidate alive.
            return false;
        }
    }

    /// <summary>
    /// G275: Returns true when the given execution unit / packet directory matches
    /// the requested domain and target-repo filters.
    ///
    /// Domain is inferred from the queue item's <c>clarification_return_path</c>
    /// field (shape: <c>intents/&lt;domain&gt;/clarifications/open.md</c>).
    /// When no queue item exists for the unit, domain filtering is skipped for
    /// that unit (it remains a candidate).
    ///
    /// Target-repo is read from <c>packet.yaml</c> in the packet directory.
    /// When the file is absent or unreadable, target-repo filtering is skipped
    /// (the unit remains a candidate).
    /// </summary>
    private static bool MatchesDomainAndRepoFilter(
        string domain,
        string? targetRepo,
        QueueState? queueState,
        string executionUnit,
        string directory)
    {
        // Domain filter: derive domain from queue item's clarification_return_path.
        // Path shape: intents/<domain>/clarifications/open.md
        if (queueState is not null)
        {
            QueueItem? item = null;
            foreach (var candidate in queueState.Items)
            {
                if (string.Equals(candidate.ExecutionUnit, executionUnit, StringComparison.Ordinal))
                {
                    item = candidate;
                    break;
                }
            }

            if (item is not null)
            {
                var itemDomain = ExtractDomainFromReturnPath(item.ClarificationReturnPath);
                if (itemDomain is not null
                    && !string.Equals(itemDomain, domain, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        // Target-repo filter: read packet.yaml to get target_repo field.
        //
        // PR #824 review repair #3: parse packet.yaml structurally
        // (via the strict PreparedPacketYamlScalarParser) instead of
        // the legacy line-scanner. Malformed YAML now fails the
        // filter so a broken packet.yaml can never reach
        // issue-cut-ready. Lazy import of the strict parser keeps
        // this command from gaining a hard dependency on the G361
        // helper.
        if (!string.IsNullOrWhiteSpace(targetRepo))
        {
            var packetYamlPath = Path.Combine(directory, "packet.yaml");
            if (File.Exists(packetYamlPath))
            {
                var packetTargetRepo = TryReadPacketTargetRepoStrict(packetYamlPath);
                // Strict outcomes:
                //  - matching value         → keep (passes filter).
                //  - mismatched value       → filter out (return false).
                //  - missing field          → keep (legacy behavior).
                //  - malformed YAML / I/O   → filter out (return false): the
                //    operator must fix the packet before host-loop selection
                //    can trust it (PR #824 review repair #3).
                if (packetTargetRepo.Outcome == PacketTargetRepoOutcomeKind.Mismatch
                    || packetTargetRepo.Outcome == PacketTargetRepoOutcomeKind.MalformedYaml)
                {
                    return false;
                }
                if (packetTargetRepo.Outcome == PacketTargetRepoOutcomeKind.Present
                    && !string.Equals(packetTargetRepo.Value, targetRepo, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// PR #824 review repair #3: structured outcome distinguishing a
    /// present matching target_repo, a present mismatching target_repo,
    /// a missing field (legacy lenience), and a malformed packet.yaml
    /// (fail closed).
    /// </summary>
    private enum PacketTargetRepoOutcomeKind
    {
        Present,
        MissingField,
        Mismatch,
        MalformedYaml,
    }

    private readonly record struct PacketTargetRepoOutcome(
        PacketTargetRepoOutcomeKind Outcome,
        string? Value);

    private static PacketTargetRepoOutcome TryReadPacketTargetRepoStrict(string packetYamlPath)
    {
        string content;
        try
        {
            content = File.ReadAllText(packetYamlPath);
        }
        catch (IOException)
        {
            return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.MalformedYaml, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.MalformedYaml, null);
        }

        IReadOnlyDictionary<string, string> fields;
        try
        {
            fields = PreparedPacketYamlScalarParser.Parse(content);
        }
        catch (FormatException)
        {
            // Malformed YAML — fail closed so the broken packet does
            // not slip through the next-slice candidate filter.
            return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.MalformedYaml, null);
        }

        if (fields.TryGetValue("implementation_issue_packet.target_repo", out var nested)
            && !string.IsNullOrWhiteSpace(nested))
        {
            return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.Present, nested);
        }
        if (fields.TryGetValue("target_repo", out var flat)
            && !string.IsNullOrWhiteSpace(flat))
        {
            return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.Present, flat);
        }
        return new PacketTargetRepoOutcome(PacketTargetRepoOutcomeKind.MissingField, null);
    }

    /// <summary>
    /// Extracts the domain segment from a clarification return path of the form
    /// <c>intents/&lt;domain&gt;/clarifications/open.md</c>.
    /// Returns null when the path does not match the expected shape.
    /// </summary>
    private static string? ExtractDomainFromReturnPath(string returnPath)
    {
        if (string.IsNullOrWhiteSpace(returnPath))
        {
            return null;
        }

        // Normalise path separators so this works on all platforms.
        var normalised = returnPath.Replace('\\', '/');
        var parts = normalised.Split('/');

        // Expected: ["intents", "<domain>", "clarifications", "open.md"]
        if (parts.Length < 4)
        {
            return null;
        }

        if (!string.Equals(parts[0], "intents", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1];
    }

    // PR #824 review repair #3: legacy line-scanner
    // `TryReadPacketTargetRepo` removed; the strict
    // `TryReadPacketTargetRepoStrict` (above) replaces it and routes
    // malformed packet.yaml through the fail-closed lane so the
    // next-slice filter never silently trusts a broken packet.

    private static void WriteMarkdown(TextWriter writer, IntentNextSliceResult result)
    {
        writer.WriteLine($"# Intent next-slice dry-run — {result.Domain}");
        writer.WriteLine();
        writer.WriteLine($"- target repo: {(result.TargetRepo ?? "(unspecified)")}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- queue-state present: {(result.QueueStatePresent ? "yes" : "no")}");
        writer.WriteLine($"- recommended outcome: {result.RecommendedOutcome}");
        writer.WriteLine();

        writer.WriteLine("## WIP (in-flight)");
        if (result.Wip.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var unit in result.Wip)
            {
                writer.WriteLine($"- {unit}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Open clarifications");
        writer.WriteLine($"- file: {result.ClarificationPath}");
        writer.WriteLine($"- file present: {(result.ClarificationFilePresent ? "yes" : "no")}");
        writer.WriteLine($"- has open blocker: {(result.ClarificationOpen ? "yes" : "no")}");
        writer.WriteLine($"- stale clarification metadata: {(result.StaleClarificationMetadata ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Candidate");
        if (result.Candidate is null)
        {
            writer.WriteLine("- none");
        }
        else
        {
            writer.WriteLine($"- execution unit: {result.Candidate.ExecutionUnit}");
            writer.WriteLine($"- packet directory: {result.Candidate.PacketDirectory}");
            writer.WriteLine($"- github-body.md present: {(result.Candidate.GithubBodyPresent ? "yes" : "no")}");
            if (result.Candidate.MissingContractSections.Count == 0)
            {
                writer.WriteLine("- missing contract sections: none");
            }
            else
            {
                writer.WriteLine("- missing contract sections:");
                foreach (var section in result.Candidate.MissingContractSections)
                {
                    writer.WriteLine($"  - {section}");
                }
            }
        }
        writer.WriteLine();

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }

            writer.WriteLine();
        }

        if (result.Notes.Count > 0)
        {
            writer.WriteLine("## Notes");
            foreach (var note in result.Notes)
            {
                writer.WriteLine($"- {note}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out bool dryRun,
        out string? domainOverride,
        out string? targetRepo,
        out bool runtimeCreationAllowed,
        out string format,
        out string error)
    {
        dryRun = false;
        domainOverride = null;
        targetRepo = null;
        runtimeCreationAllowed = false;
        format = FormatJson;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--dry-run":
                    dryRun = true;
                    break;

                case "--runtime-creation-allowed":
                    runtimeCreationAllowed = true;
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

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (json or markdown).";
                        return false;
                    }

                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'json' or 'markdown' (got '{requested}').";
                        return false;
                    }

                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent next-slice");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only next-slice planning facts. Reports WIP, clarification blockers, candidate packets, and missing contract fields without mutating state.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record IntentNextSliceResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("dry_run")]
    public required bool DryRun { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("queue_state_present")]
    public required bool QueueStatePresent { get; init; }

    [JsonPropertyName("wip")]
    public required IReadOnlyList<string> Wip { get; init; }

    [JsonPropertyName("clarification_path")]
    public required string ClarificationPath { get; init; }

    [JsonPropertyName("clarification_file_present")]
    public required bool ClarificationFilePresent { get; init; }

    [JsonPropertyName("clarification_open")]
    public required bool ClarificationOpen { get; init; }

    [JsonPropertyName("stale_clarification_metadata")]
    public required bool StaleClarificationMetadata { get; init; }

    [JsonPropertyName("packet_root")]
    public required string PacketRoot { get; init; }

    [JsonPropertyName("candidate")]
    public IntentNextSliceCandidate? Candidate { get; init; }

    [JsonPropertyName("recommended_outcome")]
    public required string RecommendedOutcome { get; init; }

    /// <summary>
    /// G328: did the caller pass <c>--runtime-creation-allowed</c>?
    /// Surfaced in the result so downstream tooling (host-loop-next-action)
    /// can verify the policy gate that produced the recommendation
    /// without re-parsing the original CLI argv.
    /// </summary>
    [JsonPropertyName("runtime_creation_allowed")]
    public required bool RuntimeCreationAllowed { get; init; }

    /// <summary>
    /// G332: which on-disk layout the runtime-scoped resolver chose
    /// for the queue-state read when the caller supplied
    /// <c>--target-repo</c>. <c>scoped</c> when
    /// `.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/queue-state.json`
    /// exists, <c>legacy-fallback</c> when only the legacy root path
    /// exists (pre-migration), null when no target repo was supplied
    /// (the read used the pre-G332 root path byte-identically).
    /// </summary>
    [JsonPropertyName("state_layout")]
    public string? StateLayout { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("notes")]
    public required IReadOnlyList<string> Notes { get; init; }
}

internal sealed record IntentNextSliceCandidate
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("github_body_present")]
    public required bool GithubBodyPresent { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    /// <summary>
    /// G354: per-section gap classification and repair guidance.
    /// Non-null only when <see cref="MissingContractSections"/> is non-empty.
    /// When <see cref="PacketContractGapAnalysisResult.OverallClassification"/>
    /// is <c>mechanical-repairable</c>, <see cref="PacketContractGapAnalysisResult.RepairGuidance"/>
    /// contains actionable instructions the host agent can apply immediately.
    /// </summary>
    [JsonPropertyName("gap_analysis")]
    public PacketContractGapAnalysisResult? GapAnalysis { get; init; }

    /// <summary>
    /// G328: packet provenance — which role authored the packet
    /// (<c>design</c> vs <c>review-runtime</c>), which host workspace
    /// produced it, and (for runtime-created packets) the source PR.
    /// Pre-G328 packets fall back to the default-design record so
    /// the field is never null.
    /// </summary>
    [JsonPropertyName("provenance")]
    public required NextSlicePacketProvenance Provenance { get; init; }
}
