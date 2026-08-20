using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G244: <c>intent-cli packet draft</c> command. Scaffolds and validates
/// the canonical packet directory for a child execution unit
/// (<c>.intent-cli/issues/&lt;id&gt;/</c>). Writes only skeleton files
/// (<c>packet.yaml</c>, <c>implementation.md</c>, <c>review-context.md</c>,
/// <c>github-body.md</c>) and never overwrites existing content. Produces
/// deterministic output, supports a read-only <c>--dry-run</c> preview,
/// and never launches an AI provider.
/// </summary>
internal static class PacketDraftCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string FileCreated = "created";
    private const string FileSkipped = "skipped";
    private const string FilePlanned = "planned";

    /// <summary>
    /// G530 review repair: review-context.md's Facet context block is
    /// selectively regenerated (not merely created-once-or-skipped like the
    /// other three scaffold files) — this status distinguishes "the
    /// existing file's generated block was refreshed" from a fresh
    /// <see cref="FileCreated"/> or an untouched <see cref="FileSkipped"/>.
    /// </summary>
    private const string FileUpdated = "updated";

    /// <summary>
    /// G530 review repair: distinct from <see cref="FileSkipped"/> (which
    /// also covers the genuinely healthy "no markers at all, a legacy file"
    /// case). This status means the file HAS marker text but not in the one
    /// safe shape (exactly one begin, exactly one end, begin before end) —
    /// fail-closed: the file is never mutated, but the caller must be able
    /// to tell "untouched because healthy" apart from "untouched because
    /// something is wrong with the markers" (see <see cref="PacketDraftFile.Detail"/>).
    /// </summary>
    private const string FileMarkersMalformed = "markers-malformed";

    /// <summary>
    /// G530 review repair: delimits the machine-owned Facet context block
    /// inside review-context.md. Content between these markers is fully
    /// regenerated on every `packet draft` run (never hand-edited content —
    /// treat it as codegen output); content OUTSIDE the markers — including
    /// everything before/after them in the file — is NEVER touched. A
    /// review-context.md that predates this feature (or had the markers
    /// manually removed) has neither marker, so it is left alone entirely,
    /// exactly like the other three scaffold files' plain skip-if-exists
    /// behavior — the markers are never retroactively injected into
    /// hand-owned content.
    /// </summary>
    private const string FacetContextBeginMarker = "<!-- BEGIN GENERATED FACET CONTEXT (G530) -->";
    private const string FacetContextEndMarker = "<!-- END GENERATED FACET CONTEXT (G530) -->";

    private const string UsageLine =
        "Usage: intent-cli packet draft --execution-unit <id> [--domain <name>] [--target-repo <owner/repo>] [--team <team>] [--lane <id>] [--dry-run] [--format markdown|json]";

    private static readonly Regex ExecutionUnitPattern = new(
        @"^[A-Za-z][A-Za-z0-9-]*$",
        RegexOptions.Compiled);

    // G482: the required publish-gate sections now live in the single shared
    // source of truth so the scaffold's draft check and the publish-body
    // validator can never drift apart.
    internal static IReadOnlyList<string> RequiredContractSections => PublishContractSections.Required;

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

        if (!TryParseArguments(args, out var executionUnit, out var domainOverride, out var targetRepo, out var team, out var laneOverride, out var dryRun, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!ExecutionUnitPattern.IsMatch(executionUnit!))
        {
            writer.WriteLine($"Invalid execution-unit id '{executionUnit}'. Expected an alphanumeric token like 'G244'.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var claimVerification = ClaimOwnershipVerifier.Verify(
            context.RepoRoot, $"execution-unit:{executionUnit}", team);
        if (!claimVerification.Passed)
        {
            ClaimVerificationCommand.Write(writer, format, claimVerification);
            return 1;
        }

        var result = Draft(context, executionUnit!, domainOverride, targetRepo, dryRun, laneOverride);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    internal static PacketDraftResult Draft(
        CliContext context,
        string executionUnit,
        string? domainOverride,
        string? targetRepo,
        bool dryRun,
        string? laneOverride = null)
    {
        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        var mode = dryRun ? ModeDryRun : ModeWrite;
        // G668: validate the named-lane choice before creating a packet
        // directory so an unknown/unsupported selection has no artifact side
        // effect.
        var laneSelection = BranchLaneResolver.ResolveForDraft(
            context.Config.Project,
            domain,
            laneOverride);

        if (!dryRun)
        {
            Directory.CreateDirectory(packetDirectory);
        }

        // G347: resolve base branch policy from host config so the published
        // contract section carries the project-specific expected base branch.
        var baseBranchPolicy = context.Config.Project.BaseBranchPolicy;
        if (string.IsNullOrWhiteSpace(baseBranchPolicy))
        {
            baseBranchPolicy = CliRuntimeContracts.DefaultBaseBranchPolicy;
        }
        var policyDefaultBaseBranch = BaseBranchPolicyContract.IsKnownPolicy(baseBranchPolicy)
            ? BaseBranchPolicyContract.ResolveExpectedBaseBranch(baseBranchPolicy)
            : CliRuntimeContracts.DirectMainBaseBranch;
        // G668: a configured named lane owns both the packet start branch and
        // the expected PR base branch. Registry-less projects continue through
        // the G667 shared effective-branch judgment below.
        // G667: use the shared effective-branch judgment so a configured
        // implementation_base_branch is carried into newly drafted issue
        // bodies, while the policy default remains byte-identical when no
        // implementation branch is configured.
        var branchDecision = ImplementationBaseBranchResolver.Resolve(
            explicitBranch: null,
            configuredBranch: context.Config.Project.ImplementationBaseBranch,
            sameRepoTopologyBranch: null,
            policyDefaultBranch: policyDefaultBaseBranch);
        var expectedBaseBranch = laneSelection?.Snapshot.PrBaseBranch ?? branchDecision.Branch;
        if (laneSelection is not null)
        {
            baseBranchPolicy = "named-lane";
        }

        // G530: review-context.md's generated "Facet context" block scopes
        // to whatever `intent_references` an EXISTING packet.yaml on disk
        // already declares — never the freshly-templated `[]` this same
        // call may be about to write for packet.yaml itself (that write
        // happens below, after this read). This is what makes
        // "regenerating an existing packet" meaningful: packet.yaml can
        // already carry hand-edited references (added after an earlier
        // `packet draft` run), and every subsequent `packet draft` run
        // refreshes the generated block to match current references —
        // never the surrounding hand-owned content.
        var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");
        var intentReferences = ReadIntentReferences(packetYamlPath);
        var facetDomainRoot = ResolveFacetDomainRoot(context, domain);
        var facetSelection = FacetContextSelector.Select(facetDomainRoot, domain, intentReferences, facetFilter: null);

        var planned = new[]
        {
            ("packet.yaml", BuildPacketYaml(executionUnit, domain, targetRepo, laneSelection)),
            ("implementation.md", BuildImplementationMd(executionUnit)),
            ("github-body.md", BuildGithubBodyMd(executionUnit, baseBranchPolicy, expectedBaseBranch, laneSelection))
        };

        var files = new List<PacketDraftFile>();
        foreach (var (name, content) in planned)
        {
            var path = Path.Combine(packetDirectory, name);
            var alreadyExists = File.Exists(path);

            string status;
            if (dryRun)
            {
                status = alreadyExists ? FileSkipped : FilePlanned;
            }
            else if (alreadyExists)
            {
                status = FileSkipped;
            }
            else
            {
                File.WriteAllText(path, content);
                status = FileCreated;
            }

            files.Add(new PacketDraftFile
            {
                Name = name,
                Path = path,
                Status = status
            });
        }

        // Inserted at its original position (between implementation.md and
        // github-body.md) so the reported file order matches the canonical
        // four-file layout regardless of the dedicated handling below.
        files.Insert(2, WriteOrUpdateReviewContext(packetDirectory, executionUnit, facetSelection, dryRun));

        // G587: readiness describes what exists NOW, not the valid skeleton a
        // dry-run could create. Feed the same four on-disk files, domain binding,
        // target repo, and publish-section source into the analyzer used by
        // queue-seed-from-packet. This eliminates the old vacuous green where an
        // absent github-body.md yielded zero missing sections merely because the
        // planned scaffold would contain them.
        string? ReadPacketFile(string name)
        {
            var path = Path.Combine(packetDirectory, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        var regexResolution = NextSliceDomainBindingsExecutionUnitRegex.Resolve(context, domain);
        var readiness = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = executionUnit,
            PacketYaml = ReadPacketFile(PreparedPacketCommitReadyAnalyzer.FileNamePacketYaml),
            ImplementationMarkdown = ReadPacketFile(PreparedPacketCommitReadyAnalyzer.FileNameImplementationMarkdown),
            ReviewContextMarkdown = ReadPacketFile(PreparedPacketCommitReadyAnalyzer.FileNameReviewContextMarkdown),
            GithubBodyMarkdown = ReadPacketFile(PreparedPacketCommitReadyAnalyzer.FileNameGithubBodyMarkdown),
            ExecutionUnitRegex = regexResolution.Pattern,
            RequestedTargetRepo = targetRepo,
            RequireDomainBinding = true,
        });

        IReadOnlyList<string> recommendedActions = readiness.RecommendedActions;
        if (readiness.Classification != PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady)
        {
            var rerun = $"intent-cli packet draft --execution-unit {executionUnit} --domain {domain}"
                + (string.IsNullOrWhiteSpace(targetRepo) ? string.Empty : $" --target-repo {targetRepo}")
                + " --dry-run --format json";
            recommendedActions = [.. recommendedActions, $"After repairing every reported item, re-run `{rerun}` and proceed only when `contract_publishable` is true."];
        }

        return new PacketDraftResult
        {
            ExecutionUnit = executionUnit,
            Domain = domain,
            TargetRepo = targetRepo,
            PacketDirectory = packetDirectory,
            Mode = mode,
            Files = files,
            MissingCanonicalFiles = readiness.MissingFiles,
            MissingContractSections = readiness.MissingContractSections,
            RefusalReasons = readiness.RefusalReasons,
            RecommendedActions = recommendedActions,
            ContractPublishable = readiness.Classification == PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady,
            BranchLane = laneSelection?.Snapshot.LaneId,
            BranchLaneSource = laneSelection?.Source,
            RoutingSnapshot = laneSelection?.Snapshot,
        };
    }

    private static string BuildPacketYaml(
        string executionUnit,
        string domain,
        string? targetRepo,
        BranchLaneSelection? laneSelection)
    {
        var repoLine = targetRepo ?? "<owner/repo>";
        var routingFields = laneSelection is null
            ? string.Empty
            : "\n" + BranchLaneRoutingYaml.RenderFields(laneSelection);
        return $"""
            implementation_issue_packet:
              issue_title: "{executionUnit} TODO short title"
              issue_kind: feature
              source_execution_unit: {executionUnit}
              domain: {domain}{routingFields}
              target_repo: {repoLine}
              target_path: <comma- or space-separated paths>
              target_part: "<one-line target description>"
              dependencies: []
              technical_baseline: []
              intent_references: []
              acceptance_criteria:
                - "TODO: at least one acceptance criterion"

            # G461: optional packet-time intent-maintenance metadata. OPTIONAL and
            # backward-compatible — packets that omit this whole block stay valid.
            # Fill it in or explicitly decline each part while the design context is
            # fresh; `improve` (G456 / G460) is the later safety net, not a substitute.
            intent_placement:
              primary_intent: <intents/{domain}/intent-tree/...>
              supporting_intents: []
              new_intent_needed: false
              placement_rationale: ""
            knowledge_updates:
              intent_tree:
                required: false
                target_paths: []
                summary: ""
              adr:
                required: false
                target_paths: []
                decision_title: ""
              diagram:
                required: false
                target_paths: []
                diagram_type: none
              docs:
                required: false
                target_paths: []
                summary: ""
            closeout_learning:
              expected: ""
              write_back_required: false
              write_back_targets: []
            # G645/G661: guide reachability is explicit and checked at closeout.
            # Uncomment and complete EXACTLY ONE accepted form. Leaving this
            # commented is visibly undeclared; the scaffold never guesses that
            # a new slice has no role-facing surface.
            #
            # Route form:
            # guide_reachability:
            #   no_role_facing_surface: false
            #   routes:
            #     - guide_surface: guide workflow task implementation-loop
            #       role: implementation
            #       target_surface: <role-facing-surface>
            #
            # Explicit no-surface form:
            # guide_reachability:
            #   no_role_facing_surface: true
            #   routes: []
            """;
    }

    private static string BuildImplementationMd(string executionUnit)
    {
        return $"""
            # {executionUnit} Implementation Packet

            ## Goal

            TODO: one-paragraph statement of what this slice changes.

            ## Why

            TODO: why this slice exists now.

            ## Scope

            - TODO

            ## Out of scope

            - TODO

            ## Verification

            TODO: focused tests and `git diff --check`.

            ## Knowledge Maintenance (G461, optional)

            Captured while the design context is fresh. Answer or explicitly decline:

            - Intent placement: TODO which intent node this supports / whether a new node is needed.
            - ADR candidate: TODO ADR-worthy decision + path, or decline.
            - Diagram candidate: TODO concept/workflow/topology/state diagram update, or decline.
            - Docs update: TODO user-facing docs to change, or decline.
            - Closeout learning: TODO knowledge to write back after landing + whether `write_back_required`.

            - Guide reachability (G645): for every role-facing surface, name the guide surface, routing role,
              and target surface; if none is added, explicitly set `no_role_facing_surface: true`. A blank
              declaration is not a decision. `stalled-work` reports a declared route until the host records it.

            `improve` (G456 / G460) is the later safety net; packet-time maintenance is the normal path.
            """;
    }

    /// <summary>
    /// G530 review repair: the review-context.md scaffold, once created,
    /// gets its Facet context block selectively refreshed on every later
    /// `packet draft` run rather than the whole file being locked at
    /// creation time — see <see cref="WriteOrUpdateReviewContext"/>. Content
    /// is only ever written by that method; this helper produces the FULL
    /// file for the fresh-create case.
    /// </summary>
    private static string BuildReviewContextMd(string executionUnit, FacetContextSelection facetSelection)
    {
        return $"""
            # {executionUnit} Review Context

            Review that this slice moves operation toward the documented intent without widening scope.

            Flag findings if the implementation:

            - widens scope beyond the issue contract;
            - launches AI providers from `intent-cli`;
            - mutates GitHub or parent state when the issue is read-only;
            - skips required contract sections.

            ## Facet context

            {FacetContextBeginMarker}
            {BuildFacetContextBlockContent(facetSelection)}
            {FacetContextEndMarker}

            ## Knowledge Writeback Expectation (G461)

            If the packet's `closeout_learning.write_back_required` is `true`, confirm the
            expected intent-tree / ADR / diagram / docs writeback landed in this PR or was
            captured as a follow-up packet. If the packet declined all knowledge maintenance,
            that is acceptable — note it rather than blocking.
            """;
    }

    /// <summary>
    /// G530 (review-repaired): the four G529 semantic-facet nodes
    /// (vocabulary/invariant/decider/acceptance-property) overlapping this
    /// packet's own declared `intent_references` — the semantic core the
    /// implementation must respect, surfaced directly in the review
    /// contract rather than left for a reviewer to reconstruct by hand.
    /// This is exactly the content written BETWEEN the generated-block
    /// markers — see <see cref="WriteOrUpdateReviewContext"/> — so it is
    /// entirely machine-owned and safe to regenerate on every run. Malformed
    /// or unknown-valued `facets:` declarations are never silently dropped:
    /// they surface as trailing warning lines naming the excluded path and
    /// reason.
    /// </summary>
    private static string BuildFacetContextBlockContent(FacetContextSelection facetSelection)
    {
        var lines = new List<string>();
        if (!facetSelection.DomainHasAnyFacetNodes)
        {
            lines.Add("No facet-annotated nodes found for this domain — facets (G529) are optional and this is the norm before a tree adopts them.");
        }
        else
        {
            foreach (var group in facetSelection.Groups)
            {
                lines.Add($"### {group.Facet}");
                if (group.Nodes.Count == 0)
                {
                    lines.Add("- (none overlapping this packet's intent_references)");
                    continue;
                }

                foreach (var node in group.Nodes)
                {
                    lines.Add($"- `{node.Id}` [{string.Join(", ", node.Facets)}] {node.Summary} — `{node.Path}`");
                }
            }
        }

        if (facetSelection.Warnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Warnings (excluded from the facet context above):");
            foreach (var warning in facetSelection.Warnings)
            {
                lines.Add($"- `{warning.Path}`: {warning.Reason}");
            }
        }

        // G530 review repair: an intent_references entry that could not be
        // resolved to a domain-relative path must never look like "this
        // packet simply references nothing overlapping" — it is reported
        // exactly like context collect's rejected --scope hints.
        if (facetSelection.ScopeWarnings.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(
                facetSelection.AllScopeHintsRejected
                    ? "Scope warnings (ALL of this packet's intent_references were rejected — nothing was scoped in):"
                    : "Scope warnings (these intent_references entries were rejected; other valid entries were still applied):");
            foreach (var warning in facetSelection.ScopeWarnings)
            {
                lines.Add($"- `{warning.Hint}`: {warning.Reason}");
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// G530 review repair: creates review-context.md fresh (matching the
    /// other three scaffold files' first-write behavior) when it does not
    /// yet exist. When it DOES exist, the file as a whole is never
    /// overwritten — but if it carries EXACTLY ONE correctly-ordered pair of
    /// generated-block markers (see <see cref="FacetContextBeginMarker"/>),
    /// the content strictly BETWEEN them is replaced with a
    /// freshly-computed <see cref="FacetContextSelection"/> while
    /// everything before/after the markers (all hand-owned content) is
    /// preserved byte-for-byte, using the FILE'S OWN existing newline style
    /// (CRLF or LF) rather than hardcoding one. A review-context.md with NO
    /// markers at all (predates this feature, or an operator removed them)
    /// is left completely untouched — <see cref="FileSkipped"/>, the
    /// markers are never retroactively injected. A review-context.md with
    /// markers in any OTHER shape (duplicates, reversed order, only a begin
    /// or only an end) is ALSO left completely untouched — but reported
    /// distinctly as <see cref="FileMarkersMalformed"/> with a diagnostic
    /// <see cref="PacketDraftFile.Detail"/>, so that state is never
    /// indistinguishable from the genuinely healthy no-markers-at-all case.
    /// </summary>
    private static PacketDraftFile WriteOrUpdateReviewContext(
        string packetDirectory, string executionUnit, FacetContextSelection facetSelection, bool dryRun)
    {
        const string name = "review-context.md";
        var path = Path.Combine(packetDirectory, name);

        if (!File.Exists(path))
        {
            var status = dryRun ? FilePlanned : FileCreated;
            if (!dryRun)
            {
                File.WriteAllText(path, BuildReviewContextMd(executionUnit, facetSelection));
            }
            return new PacketDraftFile { Name = name, Path = path, Status = status };
        }

        var existing = File.ReadAllText(path);
        var markerShape = ClassifyGeneratedBlockMarkers(existing);

        if (markerShape.Kind == GeneratedBlockMarkerKind.None)
        {
            return new PacketDraftFile { Name = name, Path = path, Status = FileSkipped };
        }

        if (markerShape.Kind == GeneratedBlockMarkerKind.Malformed)
        {
            return new PacketDraftFile
            {
                Name = name,
                Path = path,
                Status = FileMarkersMalformed,
                Detail = markerShape.Diagnostic,
            };
        }

        // Preserve the FILE'S OWN newline convention for both the newly
        // inserted separators and the block content's internal newlines —
        // never hardcode "\n", which would mix line-ending styles into an
        // existing CRLF file.
        var usesCrlf = existing.Contains("\r\n", StringComparison.Ordinal);
        var newline = usesCrlf ? "\r\n" : "\n";
        var blockContent = BuildFacetContextBlockContent(facetSelection);
        if (usesCrlf)
        {
            blockContent = blockContent.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        var beforeAndBeginMarker = existing[..(markerShape.BeginIndex + FacetContextBeginMarker.Length)];
        var endMarkerOnward = existing[markerShape.EndIndex..];
        var updatedContent = $"{beforeAndBeginMarker}{newline}{blockContent}{newline}{endMarkerOnward}";

        if (string.Equals(updatedContent, existing, StringComparison.Ordinal))
        {
            return new PacketDraftFile { Name = name, Path = path, Status = FileSkipped };
        }

        var updateStatus = dryRun ? FilePlanned : FileUpdated;
        if (!dryRun)
        {
            File.WriteAllText(path, updatedContent);
        }
        return new PacketDraftFile { Name = name, Path = path, Status = updateStatus };
    }

    private enum GeneratedBlockMarkerKind
    {
        /// <summary>No begin marker and no end marker anywhere — a healthy legacy file (or one never scaffolded by this feature).</summary>
        None,

        /// <summary>Exactly one begin marker, exactly one end marker, begin strictly before end — safe to regenerate.</summary>
        ValidPair,

        /// <summary>Any other shape: duplicates, reversed order, or only one of the two markers present.</summary>
        Malformed,
    }

    private readonly record struct GeneratedBlockMarkerShape(
        GeneratedBlockMarkerKind Kind, int BeginIndex, int EndIndex, string? Diagnostic);

    /// <summary>
    /// G530 review repair: classifies review-context.md's marker state
    /// before any mutation is attempted — fail-closed. Only the single
    /// unambiguous "exactly one begin, exactly one end, begin before end"
    /// shape is safe to regenerate; every other shape (duplicate markers of
    /// either kind, an end appearing before its begin, or only one of the
    /// two present) is reported as <see cref="GeneratedBlockMarkerKind.Malformed"/>
    /// with a human-readable diagnostic rather than silently doing nothing
    /// OR silently picking an arbitrary pair.
    /// </summary>
    private static GeneratedBlockMarkerShape ClassifyGeneratedBlockMarkers(string content)
    {
        var beginPositions = AllIndicesOf(content, FacetContextBeginMarker);
        var endPositions = AllIndicesOf(content, FacetContextEndMarker);

        if (beginPositions.Count == 0 && endPositions.Count == 0)
        {
            return new GeneratedBlockMarkerShape(GeneratedBlockMarkerKind.None, -1, -1, null);
        }

        if (beginPositions.Count == 1 && endPositions.Count == 1 && endPositions[0] > beginPositions[0])
        {
            return new GeneratedBlockMarkerShape(GeneratedBlockMarkerKind.ValidPair, beginPositions[0], endPositions[0], null);
        }

        string diagnostic;
        if (beginPositions.Count == 0)
        {
            diagnostic = $"found {endPositions.Count} end marker(s) but no begin marker — expected exactly one of each.";
        }
        else if (endPositions.Count == 0)
        {
            diagnostic = $"found {beginPositions.Count} begin marker(s) but no end marker — expected exactly one of each.";
        }
        else if (beginPositions.Count > 1 || endPositions.Count > 1)
        {
            diagnostic = $"found {beginPositions.Count} begin marker(s) and {endPositions.Count} end marker(s) — expected exactly one of each.";
        }
        else
        {
            diagnostic = "the end marker appears before the begin marker.";
        }

        return new GeneratedBlockMarkerShape(GeneratedBlockMarkerKind.Malformed, -1, -1, diagnostic);
    }

    private static List<int> AllIndicesOf(string content, string marker)
    {
        var indices = new List<int>();
        var searchStart = 0;
        while (true)
        {
            var index = content.IndexOf(marker, searchStart, StringComparison.Ordinal);
            if (index < 0)
            {
                return indices;
            }
            indices.Add(index);
            searchStart = index + marker.Length;
        }
    }

    /// <summary>
    /// G530: reads the `implementation_issue_packet.intent_references` list
    /// from an EXISTING packet.yaml on disk (never the in-memory template
    /// this same invocation may be about to write). Tolerant — a missing
    /// file, missing section, or malformed YAML all degrade to an empty
    /// list rather than an error, consistent with the rest of this
    /// scaffolding command's never-fail posture.
    /// </summary>
    private static IReadOnlyList<string> ReadIntentReferences(string packetYamlPath)
    {
        if (!File.Exists(packetYamlPath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var yaml = new YamlDotNet.RepresentationModel.YamlStream();
            using var reader = new StringReader(File.ReadAllText(packetYamlPath));
            yaml.Load(reader);

            if (yaml.Documents.Count == 0
                || yaml.Documents[0].RootNode is not YamlDotNet.RepresentationModel.YamlMappingNode root
                || !root.Children.TryGetValue(
                    new YamlDotNet.RepresentationModel.YamlScalarNode("implementation_issue_packet"), out var implementationNode)
                || implementationNode is not YamlDotNet.RepresentationModel.YamlMappingNode implementationMapping
                || !implementationMapping.Children.TryGetValue(
                    new YamlDotNet.RepresentationModel.YamlScalarNode("intent_references"), out var referencesNode)
                || referencesNode is not YamlDotNet.RepresentationModel.YamlSequenceNode referencesSequence)
            {
                return Array.Empty<string>();
            }

            return referencesSequence.Children
                .OfType<YamlDotNet.RepresentationModel.YamlScalarNode>()
                .Select(scalar => scalar.Value ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToArray();
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// G530: same parent-host-aware domain root resolution
    /// <c>context collect</c> already uses for clarification/automation-
    /// bindings paths — a child implementation workspace's intent tree
    /// (and therefore its facet nodes) lives under the PARENT host root
    /// when one is configured, else the local repo.
    /// </summary>
    private static string ResolveFacetDomainRoot(CliContext context, string domain)
    {
        var parentRoot = context.ResolveParentIntentRepoRootPath();
        var baseRoot = string.IsNullOrWhiteSpace(parentRoot) ? context.RepoRoot : parentRoot;
        return Path.Combine(baseRoot!, "intents", domain);
    }

    private static string BuildGithubBodyMd(
        string executionUnit,
        string baseBranchPolicy,
        string expectedBaseBranch,
        BranchLaneSelection? laneSelection)
    {
        // G347: derive policy-specific branch instruction for the mandatory contract section.
        var branchInstruction = laneSelection is not null
            ? $"Open all child PRs against `{expectedBaseBranch}`; the immutable routing snapshot records lane `{laneSelection.Snapshot.LaneId}` with landing mode `{laneSelection.Snapshot.LandingMode}`."
            : string.Equals(baseBranchPolicy, CliRuntimeContracts.MainAiBaseBranchPolicy, StringComparison.Ordinal)
            ? $"Open all child PRs against `{expectedBaseBranch}`. Do NOT target `main` directly — the human operator periodically merges `{expectedBaseBranch}` → `main`."
            : $"Open all child PRs against `{expectedBaseBranch}` directly.";
        var routingSnapshotSection = laneSelection is null
            ? $"Policy: `{baseBranchPolicy}`\nExpected PR base branch: `{expectedBaseBranch}`"
            : $"Policy: `{baseBranchPolicy}`\nLane: `{laneSelection.Snapshot.LaneId}`\nLane membership: `{laneSelection.Source}`\nRegistry definition revision: `{laneSelection.Snapshot.DefinitionRevision}`\nStart branch: `{laneSelection.Snapshot.StartBranch}`\nLanding mode: `{laneSelection.Snapshot.LandingMode}`\nExpected PR base branch: `{laneSelection.Snapshot.PrBaseBranch}`\nRouting snapshot: immutable for this execution unit; later registry edits do not retarget it.";

        return $"""
            ## Goal

            TODO: state what this slice will change.

            ## Why This Slice Exists Now

            TODO: explain why this is the next step.

            ## Current Observed State

            TODO: describe current behavior or repro.

            ## Accepted Baseline You May Assume

            - TODO

            ## Target Repo / Path / Part

            Repository: `<owner/repo>`

            - Target paths: `<comma- or space-separated paths>`

            Target part: `<one-line target description>`

            ## In Scope

            - TODO

            ## Out Of Scope

            - TODO

            ## Standalone Child Issue Contract

            TODO: one-paragraph restatement of exactly what the child PR must deliver, readable on its own without the surrounding design thread.

            ## Acceptance Criteria

            - TODO

            ## Verification

            TODO: focused tests and `git diff --check`.

            ## Related Links

            - TODO

            ## Knowledge Maintenance

            Optional (G461). Tells the implementer/reviewer whether intent / ADR / diagram / docs
            writeback is expected for this slice. Answer or explicitly decline:

            - Intent placement: TODO / none
            - ADR candidate: TODO / none
            - Diagram candidate: TODO / none
            - Docs update: TODO / none
            - Closeout writeback expected: no

            ## Guide Reachability (G645)

            While the author still knows the answer, name the guide surface and role that route to every
            role-facing surface this slice adds, or explicitly say that no role-facing surface is added. A
            blank answer is not treated as no-surface. The closeout record is a debt check, not a merge gate.

            ## Base Branch Policy

            {routingSnapshotSection}

            {branchInstruction}
            """;
    }

    private static void WriteMarkdown(TextWriter writer, PacketDraftResult result)
    {
        writer.WriteLine($"# Packet draft — {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- target repo: {(result.TargetRepo ?? "(unspecified)")}");
        writer.WriteLine($"- packet directory: {result.PacketDirectory}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine();

        writer.WriteLine("## Files");
        foreach (var file in result.Files)
        {
            writer.WriteLine($"- {file.Name}: {file.Status}");
            if (file.Detail is not null)
            {
                writer.WriteLine($"  - {file.Detail}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Contract validation");
        writer.WriteLine($"- contract publishable: {(result.ContractPublishable ? "yes" : "no")}");
        if (result.MissingCanonicalFiles.Count == 0)
        {
            writer.WriteLine("- missing canonical files: none");
        }
        else
        {
            writer.WriteLine("- missing canonical files:");
            foreach (var file in result.MissingCanonicalFiles)
            {
                writer.WriteLine($"  - {file}");
            }
        }
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
        if (result.RefusalReasons.Count > 0)
        {
            writer.WriteLine("- refusal reasons:");
            foreach (var reason in result.RefusalReasons)
            {
                writer.WriteLine($"  - {reason}");
            }
        }
        if (result.RecommendedActions.Count > 0)
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
        out string? executionUnit,
        out string? domainOverride,
        out string? targetRepo,
        out string? team,
        out string? laneOverride,
        out bool dryRun,
        out string format,
        out string error)
    {
        executionUnit = null;
        domainOverride = null;
        targetRepo = null;
        team = null;
        laneOverride = null;
        dryRun = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value.";
                        return false;
                    }

                    executionUnit = args[index + 1];
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

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
                    index++;
                    break;

                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }

                    team = args[index + 1];
                    index++;
                    break;

                case "--lane":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--lane requires a value.";
                        return false;
                    }

                    laneOverride = args[index + 1];
                    index++;
                    break;

                case "--dry-run":
                    dryRun = true;
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
                    error = $"Unknown argument '{argument}'.";
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

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("packet draft");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Scaffolds packet.yaml, implementation.md, review-context.md, github-body.md for an execution unit. Existing files are never overwritten.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record PacketDraftResult
{
    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("packet_directory")]
    public required string PacketDirectory { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<PacketDraftFile> Files { get; init; }

    [JsonPropertyName("missing_canonical_files")]
    public required IReadOnlyList<string> MissingCanonicalFiles { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    [JsonPropertyName("refusal_reasons")]
    public required IReadOnlyList<string> RefusalReasons { get; init; }

    [JsonPropertyName("recommended_actions")]
    public required IReadOnlyList<string> RecommendedActions { get; init; }

    /// <summary>
    /// G587: true only when the same complete packet readiness analyzer used by
    /// queue-seed-from-packet finds no missing files, sections, binding mismatch,
    /// malformed packet.yaml, or other publication refusal.
    /// </summary>
    [JsonPropertyName("contract_publishable")]
    public bool ContractPublishable { get; init; }

    [JsonPropertyName("branch_lane")]
    public string? BranchLane { get; init; }

    [JsonPropertyName("branch_lane_source")]
    public string? BranchLaneSource { get; init; }

    [JsonPropertyName("routing_snapshot")]
    public BranchRoutingSnapshot? RoutingSnapshot { get; init; }
}

internal sealed record PacketDraftFile
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// G530 review repair: set only for review-context.md's <c>markers-malformed</c>
    /// status — a human-readable diagnostic explaining exactly what shape
    /// the generated-block markers were found in (duplicates, reversed
    /// order, one-sided), so the fail-closed "left untouched" outcome is
    /// actionable rather than a bare status string.
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}
