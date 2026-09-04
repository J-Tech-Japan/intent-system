using IntentSystem.Supervisor;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G363: <c>intent-cli automation queue-seed-from-packet
/// --execution-unit &lt;unit&gt; [--target-repo &lt;owner/repo&gt;]
/// [--domain &lt;name&gt;] [--write] [--format markdown|json]</c> —
/// seed <c>.intent-cli/queue-state.json</c> with a queued item for
/// a validated prepared packet directory so downstream
/// <c>issue publish-flow</c> and closeout can find the execution
/// unit.
///
/// The seed is gated by
/// <see cref="PreparedPacketCommitReadyAnalyzer"/>: the packet must
/// have the four canonical files, packet.yaml must parse, the
/// directory-derived execution-unit must match the active domain
/// binding regex (when configured), and the declared
/// <c>target_repo</c> must match the requested one. Anything else
/// is a structured unsafe stop — the seed is REFUSED rather than
/// silently inserting a wrong-domain / malformed item.
///
/// Without <c>--write</c> the command emits the planned seed
/// (dry-run); with <c>--write</c> it inserts the item, persists
/// <c>queue-state.json</c>, and appends a
/// <c>queue_seeded_from_packet</c> event to
/// <c>.intent-cli/runs.jsonl</c>. Existing items are left
/// untouched — re-running on an already-seeded unit returns
/// <c>already-seeded</c>.
/// </summary>
internal static class AutomationQueueSeedFromPacketCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string ClassificationReady = "queue-seed-ready";
    public const string ClassificationAlreadySeeded = "already-seeded";
    public const string ClassificationApplied = "queue-seed-applied";
    public const string ClassificationUnsafe = "unsafe-prepared-packet";
    public const string ClassificationPacketDirectoryMissing = "packet-directory-missing";
    public const string ReasonDomainResolutionFailed = "domain-resolution-failed";
    public const string ReasonRoutingSnapshotInvalid = "routing-snapshot-invalid";

    public const string SeedEventName = "queue_seeded_from_packet";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var executionUnit, out var domain, out var targetRepo, out var team,
                out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var claimVerification = ClaimOwnershipVerifier.Verify(
            context.RepoRoot, $"execution-unit:{executionUnit}", team);
        if (!claimVerification.Passed)
        {
            ClaimVerificationCommand.Write(writer, format, claimVerification);
            return 1;
        }

        var packetDirectoryRelative = $".intent-cli/issues/{executionUnit}/";
        var packetDirectoryAbsolute = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        if (!Directory.Exists(packetDirectoryAbsolute))
        {
            var missingFiles = PreparedPacketCommitReadyAnalyzer.CanonicalFileNames
                .Select(name => packetDirectoryRelative + name)
                .ToArray();
            var missing = new QueueSeedFromPacketResult
            {
                Classification = ClassificationPacketDirectoryMissing,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                ContractPublishable = false,
                UnsafeReason = PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile,
                RefusalReasons =
                [
                    PreparedPacketCommitReadyAnalyzer.ReasonMissingCanonicalFile,
                    PreparedPacketCommitReadyAnalyzer.ReasonGithubBodyMissingSection,
                ],
                MissingCanonicalFiles = missingFiles,
                MissingContractSections = PreparedPacketCommitReadyAnalyzer.RequiredGithubBodySections,
                RecommendedActions =
                [
                    $"Create `{packetDirectoryRelative}` and author all four canonical packet files: {string.Join(", ", missingFiles)}.",
                    BuildReadinessRerun(executionUnit, domain, targetRepo),
                ],
                Summary = $"prepared packet directory `{packetDirectoryRelative}` does not exist; all four canonical files and every required github-body.md section are missing.",
            };
            EmitResult(writer, format, missing);
            return 1;
        }

        // G522: domain resolution order is explicit `--domain` > the
        // domain declared by the packet's own `domain:` scalar > fail
        // loud. The previous fallback to the host config's
        // `Project.Domain` silently resolved wrong-domain packets
        // against the WRONG domain's binding regex on multi-domain
        // hosts (the packet's own declared domain is authoritative —
        // see G522 issue). `--domain` remains optional on the CLI, but
        // the underlying domain-binding regex check MUST ALWAYS RUN —
        // otherwise a wrong-domain packet could be seeded as long as
        // `target_repo` matches (fail-open).
        // G567: the WHOLE document is parsed before anything reads a field from
        // it, and a packet that is not valid YAML fails closed here — in
        // dry-run and in write alike, with the parse error named and before any
        // queue-state or runs.jsonl mutation is even planned.
        //
        // Until now this lane read fields with the G361 regex scalar reader,
        // which never parses the document, so a packet that the schema and
        // projection surfaces both reject could still classify
        // `queue-seed-ready` and seed the queue. That is the same
        // acceptance-surface disagreement G565 closed for projection, one
        // surface upstream and on a mutation path — where the cost is a
        // malformed unit sitting in the queue and failing later at publish or
        // preflight, far from its cause.
        var packetYaml = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml));
        var implementationMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown));
        var reviewContextMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown));
        var githubBodyMarkdown = TryReadFile(Path.Combine(packetDirectoryAbsolute, PreparedPacketCommitReadyAnalyzer.FileNameGithubBodyMarkdown));

        PacketYamlDocument? packetDocument = null;
        var packetDeclaredDomain = packetYaml is not null
            && PacketYamlDocument.TryParse(packetYaml, out packetDocument, out _)
                ? LookupScalar(packetDocument!.Fields, "domain")
                : null;
        var domainResolution = PacketDomainResolution.Resolve(
            domain,
            packetDeclaredDomain,
            DomainCandidateScanner.Scan(context),
            $"intent-cli automation queue-seed-from-packet --execution-unit {executionUnit} --domain <name>"
                + (string.IsNullOrWhiteSpace(targetRepo) ? string.Empty : $" --target-repo {targetRepo}"));
        if (domainResolution.IsError)
        {
            var contentValidation = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
            {
                ExecutionUnit = executionUnit,
                PacketYaml = packetYaml,
                ImplementationMarkdown = implementationMarkdown,
                ReviewContextMarkdown = reviewContextMarkdown,
                GithubBodyMarkdown = githubBodyMarkdown,
                RequestedTargetRepo = targetRepo,
                RequireDomainBinding = false,
            });
            var refusalReasons = contentValidation.RefusalReasons
                .Append(ReasonDomainResolutionFailed)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var domainFailure = new QueueSeedFromPacketResult
            {
                Classification = ClassificationUnsafe,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                ContractPublishable = false,
                UnsafeReason = contentValidation.Reason ?? ReasonDomainResolutionFailed,
                RefusalReasons = refusalReasons,
                MissingCanonicalFiles = contentValidation.MissingFiles,
                MissingContractSections = contentValidation.MissingContractSections,
                RecommendedActions =
                [
                    .. contentValidation.RecommendedActions,
                    domainResolution.ErrorMessage ?? "Resolve the packet domain explicitly, then re-run readiness.",
                    BuildReadinessRerun(executionUnit, domain, targetRepo),
                ],
                Summary = $"refusing to seed queue-state from `{packetDirectoryRelative}`: {contentValidation.Summary} "
                    + (domainResolution.ErrorMessage ?? "The packet domain could not be resolved."),
            };
            EmitResult(writer, format, domainFailure);
            return 1;
        }
        var effectiveDomain = domainResolution.Domain!;

        // G485: resolve the domain-binding `execution_unit_regex` through the
        // SAME shared resolver the host loop and `automation summary` use
        // (NextSliceDomainBindingsExecutionUnitRegex), instead of a duplicate
        // local parser that could disagree with summary on the active domain's
        // regex. The structured outcome also lets the diagnostic distinguish a
        // missing bindings file from a present-but-empty regex field.
        var regexResolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, effectiveDomain);

        // Validate via PreparedPacketCommitReadyAnalyzer (G361). The
        // probe reads the four canonical files and feeds them to the
        // pure analyzer.
        var validation = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = executionUnit,
            PacketYaml = packetYaml,
            ImplementationMarkdown = implementationMarkdown,
            ReviewContextMarkdown = reviewContextMarkdown,
            GithubBodyMarkdown = githubBodyMarkdown,
            ExecutionUnitRegex = regexResolution.Pattern,
            RequestedTargetRepo = targetRepo,
            // Always require a domain binding now that `effectiveDomain`
            // is always populated (via G522's `--domain` > packet-declared
            // > fail-loud order). Closes the fail-open path where omitting
            // `--domain` skipped the regex check.
            RequireDomainBinding = true,
        });

        if (validation.Classification != PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady)
        {
            var unsafeResult = new QueueSeedFromPacketResult
            {
                Classification = ClassificationUnsafe,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                ContractPublishable = false,
                UnsafeReason = validation.Reason,
                RefusalReasons = validation.RefusalReasons,
                MissingCanonicalFiles = validation.MissingFiles,
                MissingContractSections = validation.MissingContractSections,
                RecommendedActions =
                [
                    .. validation.RecommendedActions,
                    BuildReadinessRerun(executionUnit, domain, targetRepo),
                ],
                Summary = $"refusing to seed queue-state from `{packetDirectoryRelative}`: "
                    + validation.Summary
                    + DescribeBindingResolution(
                        validation.RefusalReasons.Contains(
                            PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex,
                            StringComparer.Ordinal)
                            ? PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex
                            : null,
                        effectiveDomain,
                        regexResolution),
            };
            EmitResult(writer, format, unsafeResult);
            return 1;
        }

        BranchRoutingSnapshot? routingSnapshot;
        try
        {
            // G668: queue seeding carries the already-materialised packet
            // snapshot forward for reviewers. It never resolves against the
            // current registry, and a partial snapshot fails closed.
            routingSnapshot = BranchLaneResolver.TryReadSnapshot(packetDocument!.Fields);
        }
        catch (InvalidOperationException exception)
        {
            var invalidSnapshot = new QueueSeedFromPacketResult
            {
                Classification = ClassificationUnsafe,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                ContractPublishable = false,
                UnsafeReason = ReasonRoutingSnapshotInvalid,
                RefusalReasons = [ReasonRoutingSnapshotInvalid],
                RecommendedActions =
                [
                    exception.Message,
                    "Repair the packet routing_snapshot and re-run queue readiness before seeding.",
                    BuildReadinessRerun(executionUnit, domain, targetRepo),
                ],
                Summary = $"refusing to seed queue-state from `{packetDirectoryRelative}`: {exception.Message}",
            };
            EmitResult(writer, format, invalidSnapshot);
            return 1;
        }

        // PR #830 review repair #2: resolve the canonical clarification
        // return path for the host domain so packets that omit the
        // `clarification_return_path` field still seed with a usable
        // route. The convention used across the codebase (see
        // BugIntentEnqueueCommand, ProjectionPacketRuntimeReader,
        // AutomationHostQueueItemRecoveryCommand) is
        // `intents/<domain>/clarifications/open.md`. Falling back to
        // `string.Empty` here would silently break the packet ↔
        // queue-item clarification path contract enforced by
        // ClarifyOpenCommand and MetadataValidateAnalyzer.
        // `effectiveDomain` is already resolved above (G522: --domain >
        // packet-declared domain > fail-loud) so the clarification path
        // computation reuses the same value rather than re-deriving it.
        var packetFields = packetDocument!.Fields;
        var defaultClarificationReturnPath = $"intents/{effectiveDomain}/clarifications/open.md";

        // PR #830 review repair #3 (08:27 comment): align role /
        // priority fallbacks with the established
        // `QueueEnqueueCommand` contract so seeded queue items look
        // the same as packets enqueued through the standard path.
        // - WorkerRole / ReviewRole default to the host's configured
        //   logical roles (`context.Config.Roles.WorkerRoleForQueue` /
        //   `ReviewRoleForQueue`), which canonicalize aliases and keep
        //   historical vendor values out of newly seeded records.
        // - Priority defaults to "high" to match
        //   `QueueEnqueueCommand.DefaultPriority`.
        // Packets that explicitly declare any of these still win via
        // `LookupScalar` precedence inside BuildSeedItem.
        var defaultWorkerRole = context.Config.Roles.WorkerRoleForQueue;
        var defaultReviewRole = context.Config.Roles.ReviewRoleForQueue;
        const string defaultPriority = "high";

        var seed = BuildSeedItem(
            executionUnit,
            packetDocument,
            defaultClarificationReturnPath,
            defaultWorkerRole,
            defaultReviewRole,
            defaultPriority,
            routingSnapshot);

        // Read current queue-state (if present). Missing file is OK —
        // we'll create one with this seed as the sole item.
        var queueStatePath = context.GetQueueStatePath();
        QueueState? existing = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                existing = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException exception)
            {
                writer.WriteLine($"queue-state.json at `{queueStatePath}` is unparseable; refusing to seed. {exception.Message}");
                return 1;
            }
        }

        // Already-seeded check is keyed on execution_unit (the
        // canonical identifier). Operators re-running the command on
        // a unit that's already in the queue get a no-op signal so
        // they can move on rather than treating the run as failure.
        var existingItem = existing?.Items.FirstOrDefault(
            item => string.Equals(item.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        if (existingItem is not null)
        {
            var already = new QueueSeedFromPacketResult
            {
                Classification = ClassificationAlreadySeeded,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = write,
                ContractPublishable = true,
                Summary = $"queue-state already contains an entry for `{executionUnit}`; nothing to seed.",
                SeededItem = existingItem,
                RoutingSnapshot = existingItem.RoutingSnapshot is null
                    ? null
                    : BranchLaneResolver.FromQueueProjection(existingItem.RoutingSnapshot),
            };
            EmitResult(writer, format, already);
            return 0;
        }

        if (!write)
        {
            var readyDryRun = new QueueSeedFromPacketResult
            {
                Classification = ClassificationReady,
                ExecutionUnit = executionUnit,
                PacketDirectory = packetDirectoryRelative,
                Write = false,
                ContractPublishable = true,
                SeededItem = seed,
                RoutingSnapshot = routingSnapshot,
                Summary = $"prepared packet `{packetDirectoryRelative}` validated; queue-state would be seeded with a new queued item for `{executionUnit}`. "
                    + "Re-run with `--write` to persist.",
                RecommendedActions = new[]
                {
                    $"intent-cli automation queue-seed-from-packet --execution-unit {executionUnit}"
                        + (string.IsNullOrWhiteSpace(targetRepo) ? string.Empty : $" --target-repo {targetRepo}")
                        + (string.IsNullOrWhiteSpace(domain) ? string.Empty : $" --domain {domain}")
                        + " --write",
                },
            };
            EmitResult(writer, format, readyDryRun);
            return 0;
        }

        // --write: insert seed, persist queue-state, append runs.jsonl event.
        var newItems = new List<QueueItem>(existing?.Items ?? Array.Empty<QueueItem>()) { seed };
        var updated = new QueueState
        {
            SchemaVersion = existing?.SchemaVersion ?? "1",
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = newItems,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(
            queueStatePath,
            existing ?? new QueueState { SchemaVersion = "1", UpdatedAt = updated.UpdatedAt, Items = Array.Empty<QueueItem>() },
            updated);

        var runsPath = Path.Combine(context.RepoRoot, ".intent-cli", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        var runEvent = new RunEvent
        {
            Ts = DateTimeOffset.UtcNow,
            ExecutionUnit = executionUnit,
            Event = SeedEventName,
            By = "automation queue-seed-from-packet (G363)",
            PacketRef = packetDirectoryRelative,
        };
        File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");

        var applied = new QueueSeedFromPacketResult
        {
            Classification = ClassificationApplied,
            ExecutionUnit = executionUnit,
            PacketDirectory = packetDirectoryRelative,
            Write = true,
            ContractPublishable = true,
            SeededItem = seed,
            RoutingSnapshot = routingSnapshot,
            Summary = $"seeded queue-state with a new queued item for `{executionUnit}` from validated packet `{packetDirectoryRelative}`. "
                + $"Appended `{SeedEventName}` event to `.intent-cli/runs.jsonl`.",
        };
        EmitResult(writer, format, applied);
        return 0;
    }

    /// <summary>
    /// Build the queued <see cref="QueueItem"/> for a validated
    /// packet. Fields that are not present in packet.yaml are filled
    /// with deterministic defaults so the seed has a complete shape;
    /// the operator can override later via metadata-update if
    /// needed.
    /// </summary>
    internal static QueueItem BuildSeedItem(
        string executionUnit,
        PacketYamlDocument packet,
        string defaultClarificationReturnPath,
        string defaultWorkerRole,
        string defaultReviewRole,
        string defaultPriority,
        BranchRoutingSnapshot? routingSnapshot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executionUnit);
        ArgumentNullException.ThrowIfNull(packet);
        var packetFields = packet.Fields;
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultClarificationReturnPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultWorkerRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultReviewRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultPriority);

        var packetDir = $".intent-cli/issues/{executionUnit}/";
        var title = LookupScalar(packetFields,
            "implementation_issue_packet.issue_title",
            "implementation_issue.issue_title",
            "issue_title",
            "title")
            ?? executionUnit;

        var targetRepo = LookupScalar(packetFields,
            "implementation_issue_packet.target_repo",
            "implementation_issue.target_repo",
            "target_repo");
        // PR #830 review repair #2: ClarificationReturnPath
        // convention is `intents/<domain>/clarifications/open.md`. If
        // the packet declares one, honor it; otherwise fall back to
        // the caller-provided per-domain default so downstream
        // consumers (ClarifyOpenCommand, MetadataValidateAnalyzer)
        // see a real path. An empty path would violate the packet ↔
        // queue-item clarification path contract.
        var clarificationReturnPath = LookupScalar(packetFields,
            "clarification_return_path",
            "implementation_issue_packet.clarification_return_path")
            ?? defaultClarificationReturnPath;
        // PR #830 review repair #3: align role / priority fallbacks
        // with the established `QueueEnqueueCommand` contract
        // (config-driven roles, priority "high"). Hardcoded
        // "coder" / "reviewer" / "normal" diverged from that
        // contract, so packets enqueued via this lane looked
        // different from packets enqueued via the standard path.
        // Packets that DO declare any field still win via
        // LookupScalar.
        var workerRole = LogicalRoleNormalizer.NormalizeOrPreserveLegacy(
            LookupScalar(packetFields,
            "worker_role",
            "implementation_issue_packet.worker_role"),
            defaultWorkerRole);
        var reviewRole = LogicalRoleNormalizer.NormalizeOrPreserveLegacy(
            LookupScalar(packetFields,
            "review_role",
            "implementation_issue_packet.review_role"),
            defaultReviewRole);
        var priority = LookupScalar(packetFields,
            "priority",
            "implementation_issue_packet.priority")
            ?? defaultPriority;

        // PR #830 review repair: preserve packet.yaml dependency /
        // blocked_by data when the packet declares them. Previously
        // these fields were hardcoded empty, which silently dropped
        // dependency metadata the operator already authored into
        // the prepared packet.
        //
        // G568: both come off the parsed document's structured
        // sequences, so a FLOW (`[G1, G2]`) and a BLOCK (`- G1`)
        // declaration seed identically. Until now a flow sequence
        // survived as bracket text that had to be re-split here, and
        // a block sequence was dropped entirely — which silently
        // un-gated dependency-aware selection for exactly the units
        // that declared their dependencies in the more common style.
        // Empty stays the safe default when the packet truly carries
        // none — absence is never guessed into content.
        var dependencies = packet.LookupSequence(
            "implementation_issue_packet.dependencies",
            "implementation_issue.dependencies",
            "dependencies");
        var blockedBy = packet.LookupSequence(
            "implementation_issue_packet.blocked_by",
            "implementation_issue.blocked_by",
            "blocked_by");

        var item = new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = title,
            State = QueueItemState.Queued,
            Dependencies = dependencies,
            BlockedBy = blockedBy,
            ClarificationReturnPath = clarificationReturnPath,
            PacketPaths = new PacketPaths
            {
                Implementation = packetDir + PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown,
                ReviewContext = packetDir + PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown,
                Yaml = packetDir + PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml,
            },
            RoutingSnapshot = routingSnapshot is null
                ? null
                : BranchLaneResolver.ToQueueProjection(routingSnapshot),
            WorkerRole = workerRole,
            ReviewRole = reviewRole,
            Priority = priority,
        };
        return item;
    }

    private static string? LookupScalar(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string? TryReadFile(string absolutePath)
    {
        if (!File.Exists(absolutePath))
        {
            return null;
        }
        try
        {
            return File.ReadAllText(absolutePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string BuildReadinessRerun(
        string executionUnit,
        string? domain,
        string? targetRepo)
    {
        var command = $"intent-cli automation queue-seed-from-packet --execution-unit {executionUnit}"
            + (string.IsNullOrWhiteSpace(domain) ? " --domain <name>" : $" --domain {domain}")
            + (string.IsNullOrWhiteSpace(targetRepo) ? " --target-repo <owner/repo>" : $" --target-repo {targetRepo}")
            + " --format json";
        return $"After repairing every reported item, re-run `{command}` and proceed only when `contract_publishable` is true.";
    }

    /// <summary>
    /// G485: turn the shared binding resolution outcome into a precise,
    /// appended diagnostic so the operator can tell apart a missing bindings
    /// file, a present-but-empty <c>execution_unit_regex</c> field, and an
    /// invalid pattern — but only when the refusal is actually about the
    /// domain binding (<c>missing-domain-binding-regex</c>). Other refusal
    /// reasons (missing contract sections, wrong target repo, etc.) keep the
    /// analyzer's own summary unchanged.
    /// </summary>
    private static string DescribeBindingResolution(
        string? reason,
        string domain,
        ExecutionUnitRegexResolution resolution)
    {
        if (!string.Equals(reason, PreparedPacketCommitReadyAnalyzer.ReasonMissingDomainBindingRegex, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // A `missing-domain-binding-regex` refusal always corresponds to a
        // MissingOrAbsent resolution (a present pattern compiles to commit-ready
        // or to the distinct `invalid-domain-binding-regex` reason the analyzer
        // owns), so point the operator at the exact bindings source that was
        // consulted — the same one `automation summary` reads.
        return resolution.Kind == ExecutionUnitRegexResolutionKind.MissingOrAbsent
            ? $" (domain `{domain}` binding: no `execution_unit_regex` resolved from `{resolution.BindingsPath}`"
                + " — confirm the bindings file exists for this domain and declares a top-level `execution_unit_regex`;"
                + $" `intent-cli automation summary --domain {domain} --format json` reads the same source.)"
            : string.Empty;
    }

    private static void EmitResult(TextWriter writer, string format, QueueSeedFromPacketResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
            return;
        }
        writer.WriteLine($"# automation queue-seed-from-packet (G363) — `{result.ExecutionUnit}`");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- packet directory: `{result.PacketDirectory}`");
        writer.WriteLine($"- write: {(result.Write ? "yes" : "no (dry-run)")}");
        writer.WriteLine($"- contract publishable: {(result.ContractPublishable ? "yes" : "no")}");
        if (!string.IsNullOrWhiteSpace(result.UnsafeReason))
        {
            writer.WriteLine($"- unsafe reason: `{result.UnsafeReason}`");
        }
        if (result.RefusalReasons.Count > 0)
        {
            writer.WriteLine("- refusal reasons:");
            foreach (var reason in result.RefusalReasons)
            {
                writer.WriteLine($"  - {reason}");
            }
        }
        if (result.MissingCanonicalFiles.Count > 0)
        {
            writer.WriteLine("- missing canonical files:");
            foreach (var file in result.MissingCanonicalFiles)
            {
                writer.WriteLine($"  - {file}");
            }
        }
        if (result.MissingContractSections.Count > 0)
        {
            writer.WriteLine("- missing contract sections:");
            foreach (var section in result.MissingContractSections)
            {
                writer.WriteLine($"  - {section}");
            }
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.RecommendedActions is { Count: > 0 })
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended actions");
            foreach (var action in result.RecommendedActions)
            {
                writer.WriteLine($"- {action}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string executionUnit,
        out string? domain,
        out string? targetRepo,
        out string? team,
        out bool write,
        out string format,
        out string error)
    {
        executionUnit = string.Empty;
        domain = null;
        targetRepo = null;
        team = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }
                    executionUnit = args[++index].Trim();
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value (owner/repo).";
                        return false;
                    }
                    targetRepo = args[++index].Trim();
                    break;
                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }
                    team = args[++index].Trim();
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
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required.";
            return false;
        }
        return true;
    }
}

internal sealed record QueueSeedFromPacketResult
{
    public required string Classification { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string PacketDirectory { get; init; }
    public required bool Write { get; init; }
    public required bool ContractPublishable { get; init; }
    public required string Summary { get; init; }
    public string? UnsafeReason { get; init; }
    public IReadOnlyList<string> RefusalReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingCanonicalFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingContractSections { get; init; } = Array.Empty<string>();
    public QueueItem? SeededItem { get; init; }
    [JsonPropertyName("routing_snapshot")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BranchRoutingSnapshot? RoutingSnapshot { get; init; }
    public IReadOnlyList<string> RecommendedActions { get; init; } = Array.Empty<string>();
}
