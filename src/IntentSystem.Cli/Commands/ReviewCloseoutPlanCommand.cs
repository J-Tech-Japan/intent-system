using IntentSystem.Supervisor;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G329: read-only seam for fetching the closing-issue references of a
/// PR. The default production implementation reuses
/// <see cref="IGitHubPrLookup"/> (which already requests
/// <c>closingIssuesReferences</c>); tests inject a deterministic fake
/// to avoid touching GitHub. Returning an empty list means "no
/// closing references" so the closeout planner falls through to its
/// pre-G329 host-metadata-blocked path (no guessing).
/// </summary>
internal interface IPrClosingIssuesFetcher
{
    IReadOnlyList<int> Fetch(string repo, int prNumber);
}

/// <summary>
/// G329: default closing-issues fetcher backed by the existing
/// <see cref="GhCliGitHubPrLookup"/> shell adapter. Any GitHub error is
/// fail-soft — the fetcher returns an empty list so the planner falls
/// through to its existing recovery path (publish-recovery / reconcile)
/// rather than crashing the wake.
/// </summary>
internal sealed class GhCliPrClosingIssuesFetcher : IPrClosingIssuesFetcher
{
    public IReadOnlyList<int> Fetch(string repo, int prNumber)
    {
        try
        {
            var lookup = new GhCliGitHubPrLookup();
            var view = lookup.Lookup(repo, prNumber);
            return view.ClosingIssuesReferences
                .Where(r => r.Number > 0)
                .Select(r => r.Number)
                .ToArray();
        }
        catch (InvalidOperationException)
        {
            return Array.Empty<int>();
        }
    }
}

/// <summary>
/// G455: read-only seam for fetching a PR's body so linkage recovery can fall
/// back to a canonical <c>Closes #N</c> reference when GitHub's
/// <c>closingIssuesReferences</c> API is empty (non-default-base PRs). The
/// default production implementation reuses <see cref="GhCliGitHubPrLookup"/>;
/// tests inject a deterministic fake. Returns <c>null</c> on any failure so the
/// planner falls through to its existing host-metadata-blocked path.
/// </summary>
internal interface IPrBodyFetcher
{
    string? Fetch(string repo, int prNumber);
}

/// <summary>
/// G455: default PR-body fetcher backed by the existing
/// <see cref="GhCliGitHubPrLookup"/> shell adapter. Fail-soft: any GitHub error
/// returns <c>null</c>.
/// </summary>
internal sealed class GhCliPrBodyFetcher : IPrBodyFetcher
{
    public string? Fetch(string repo, int prNumber)
    {
        try
        {
            var lookup = new GhCliGitHubPrLookup();
            return lookup.Lookup(repo, prNumber).Body;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// G247: Read-only <c>intent-cli review closeout-plan</c> command. Reports
/// what a review-pass closeout would mutate before any merge or parent
/// state write. Resolves the queue item via <c>linked_pr</c>, lists the
/// packet directory contents, validates Child Issue Contract sections,
/// derives the expected submodule path, and emits the deterministic
/// validation/closeout steps the operator must run separately. Reports
/// blocking ambiguities as actionable gaps. Never mutates state. Never
/// launches an AI provider.
///
/// G287: Each gap is now classified into one of two terminal blocker
/// classes so the host loop can route the gap correctly. Gaps that
/// describe parent-host metadata drift (missing <c>linked_pr</c>,
/// missing/invalid queue-state, missing <c>linked_issue</c>) are
/// classified as <c>host-metadata-blocked</c>; the host loop must
/// run <c>automation reconcile</c> rather than turn the gap into a
/// PR repair comment / <c>request-update</c> transition. Gaps that
/// describe packet content the implementer can repair (missing
/// github-body, missing contract sections) are classified as
/// <c>implementation-review-finding</c>. The aggregate
/// <c>blocker_classification</c> field surfaces the dominant class
/// for the wake.
/// </summary>
internal static class ReviewCloseoutPlanCommand
{
    /// <summary>G287: aggregate blocker classifications surfaced on the result.</summary>
    public const string BlockerClassificationReady = "ready";
    public const string BlockerClassificationHostMetadataBlocked = "host-metadata-blocked";
    public const string BlockerClassificationImplementationReviewFinding = "implementation-review-finding";

    /// <summary>G287: per-gap classification kinds.</summary>
    public const string GapClassificationHostMetadata = "host-metadata";
    public const string GapClassificationImplementationReview = "implementation-review";
    /// <summary>
    /// G329: structured ambiguity gap when GitHub closing-issue
    /// reconstruction finds more than one matching queue item.
    /// Aggregates to <c>host-metadata-blocked</c> (no guessing).
    /// </summary>
    public const string GapClassificationLinkageAmbiguous = "linkage-ambiguous";

    /// <summary>
    /// G329: test seam for the closing-issues fetcher (used when
    /// <c>--closing-issues</c> is not supplied; auto-fetch via gh).
    /// Tests inject a deterministic fake so the planner never shells
    /// out to live GitHub during unit tests. Production callers leave
    /// this null and the planner uses
    /// <see cref="GhCliPrClosingIssuesFetcher"/>.
    /// </summary>
    public static Func<IPrClosingIssuesFetcher>? PrClosingIssuesFetcherFactory { get; set; }

    /// <summary>
    /// G455: test seam for the PR-body fetcher used by the canonical
    /// closing-reference fallback (when GitHub <c>closingIssuesReferences</c>
    /// is empty for a non-default-base PR). Production callers leave this null
    /// and the planner uses <see cref="GhCliPrBodyFetcher"/>.
    /// </summary>
    public static Func<IPrBodyFetcher>? PrBodyFetcherFactory { get; set; }

    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli review closeout-plan --pr <n> --repo <owner/repo> [--domain <name>] [--closing-issues <n>[,<n>...]] [--write-recovered-linkage] [--format json|markdown]";

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

        if (!TryParseArguments(args, out var pr, out var repo, out var domainOverride, out var closingIssues, out var writeRecoveredLinkage, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        // G332: route queue-state read through the runtime-scoped
        // resolver. When `.intent-cli/runtime/<domain>/<owner>__<repo>/queue-state.json`
        // exists on disk it wins; only-legacy hosts (pre-migration)
        // continue to read the root file (fallback explicitly surfaced
        // via `state_layout`). The packet lookup under
        // `.intent-cli/issues/<execution-unit>/` is unchanged (G300).
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            context.RepoRoot, domain, repo!);
        var queueStatePath = queueStateLocation.Path;
        var stateLayout = queueStateLocation.Kind switch
        {
            StateLocationKind.Scoped => "scoped",
            StateLocationKind.Legacy => "legacy-fallback",
            _ => "scoped"
        };
        var gaps = new List<ReviewCloseoutPlanGap>();

        QueueState? queueState = null;
        if (!File.Exists(queueStatePath))
        {
            // G287: parent-host owned durable state.
            gaps.Add(HostMetadataGap($"queue-state file not found: {queueStatePath}"));
        }
        else
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (JsonException jsonException)
            {
                gaps.Add(HostMetadataGap($"queue-state JSON could not be parsed: {jsonException.Message}"));
            }
            catch (InvalidOperationException invalidOperation)
            {
                gaps.Add(HostMetadataGap($"queue-state payload was invalid: {invalidOperation.Message}"));
            }
        }

        QueueItem? matchedItem = null;
        RecoveredLinkagePayload? recoveredLinkage = null;
        AmbiguousLinkagePayload? ambiguousLinkage = null;
        var closingIssuesSource = closingIssues.Count > 0 ? "operator-flag" : (string?)null;
        if (queueState is not null)
        {
            var prToken = pr!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item, repo!, prToken));
            if (matchedItem is null)
            {
                // G329 review fix: auto-fetch closing issues from
                // `gh pr view --json closingIssuesReferences` when the
                // operator did not pass `--closing-issues`. The host
                // review path must not have to plumb the flag manually —
                // closing-issue recovery should be the default when
                // GitHub facts are available. Fetcher is a test seam;
                // production uses GhCliPrClosingIssuesFetcher which
                // returns empty on any gh failure (fail-soft).
                if (closingIssues.Count == 0)
                {
                    var fetcher = PrClosingIssuesFetcherFactory?.Invoke()
                        ?? new GhCliPrClosingIssuesFetcher();
                    var fetched = fetcher.Fetch(repo!, pr!.Value);
                    if (fetched.Count > 0)
                    {
                        closingIssues = fetched;
                        closingIssuesSource = "github-auto-fetch";
                    }
                }

                // G455: GitHub `closingIssuesReferences` comes back EMPTY for
                // PRs targeting a non-default base branch (e.g. `develop-v2`),
                // even when the PR body carries a deterministic `Closes #N`.
                // Fall back to the canonical body reference: exactly ONE
                // same-repo Close/Fix/Resolve reference recovers the closing
                // issue; multiple distinct references surface as a structured
                // ambiguity gap (no guessing). Only consult the body when the
                // operator did not pass `--closing-issues` and GitHub provided
                // none.
                var bodyFallbackAmbiguous = false;
                if (closingIssues.Count == 0)
                {
                    var bodyFetcher = PrBodyFetcherFactory?.Invoke() ?? new GhCliPrBodyFetcher();
                    var prBody = bodyFetcher.Fetch(repo!, pr!.Value);
                    var fallback = PrBodyClosingReferenceFallbackAnalyzer.Resolve(closingIssues, prBody, repo!);
                    if (fallback.ResolvedClosingIssues.Count > 0)
                    {
                        closingIssues = fallback.ResolvedClosingIssues;
                        closingIssuesSource = fallback.Source;
                    }
                    else if (fallback.Ambiguous)
                    {
                        bodyFallbackAmbiguous = true;
                        gaps.Add(LinkageAmbiguousGap(
                            $"linkage-ambiguous: {fallback.Reason}"));
                    }
                }

                // G329: before classifying this as host-metadata-blocked,
                // try to reconstruct the linkage from GitHub closing-issue
                // facts. If exactly one queue item matches a closing issue,
                // recover the linkage and treat that queue item as matched
                // for the rest of planning. Multiple matches surface as a
                // structured `linkage-ambiguous` gap (no guessing per G329
                // out-of-scope). When the G455 body fallback already surfaced
                // an ambiguity gap, skip reconstruction entirely so the wake
                // reports a single structured ambiguity rather than a
                // redundant host-metadata gap.
                if (!bodyFallbackAmbiguous)
                {
                    var reconstruction = GitHubLinkageReconstructor.Reconstruct(closingIssues, queueState, repo);
                    switch (reconstruction.Kind)
                    {
                        case LinkageReconstructionKind.Deterministic:
                            var candidate = reconstruction.Candidates[0];
                            matchedItem = queueState.Items.First(item =>
                                string.Equals(item.ExecutionUnit, candidate.ExecutionUnit, StringComparison.Ordinal));
                            recoveredLinkage = new RecoveredLinkagePayload
                            {
                                ExecutionUnit = candidate.ExecutionUnit,
                                LinkedPrNumber = pr!.Value,
                                LinkedPrRepo = repo!,
                                LinkedPrUrl = $"https://github.com/{repo}/pull/{pr}",
                                LinkedIssueNumber = candidate.LinkedIssueNumber,
                                LinkedIssueRepo = candidate.LinkedIssueRepo,
                                LinkedIssueUrl = candidate.LinkedIssueUrl,
                                RecoverySource = LinkageReconstructionConstants.RecoverySourceGitHubClosingReference
                            };
                            break;

                        case LinkageReconstructionKind.Ambiguous:
                            ambiguousLinkage = new AmbiguousLinkagePayload
                            {
                                Candidates = reconstruction.Candidates,
                                Reason = $"PR #{pr} closes {closingIssues.Count} issue(s) matching {reconstruction.Candidates.Count} queue items; cannot deterministically recover linkage."
                            };
                            gaps.Add(LinkageAmbiguousGap(
                                $"linkage-ambiguous: PR #{pr} closing-issues match {reconstruction.Candidates.Count} queue items "
                                + $"({string.Join(", ", reconstruction.Candidates.Select(c => c.ExecutionUnit))}); refusing to guess."));
                            break;

                        case LinkageReconstructionKind.NoClosingReferences:
                        case LinkageReconstructionKind.NoMatch:
                        default:
                            // G287: this is the PR #670-shaped gap — host metadata drift,
                            // not an implementation defect. The host loop must run
                            // `automation reconcile`, not request a child PR repair.
                            gaps.Add(HostMetadataGap($"no queue item found with linked_pr matching #{pr}."));
                            break;
                    }
                }
            }
        }

        string? packetDirectory = null;
        IReadOnlyList<string> packetFiles = Array.Empty<string>();
        IReadOnlyList<string> missingSections = Array.Empty<string>();
        ReviewCloseoutLinkedIssue? linkedIssue = null;
        string? packetDeclaredDomain = null;
        if (matchedItem is not null)
        {
            packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", matchedItem.ExecutionUnit);
            if (Directory.Exists(packetDirectory))
            {
                packetFiles = Directory.EnumerateFiles(packetDirectory)
                    .Select(file => Path.GetFileName(file))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                // G522: the packet's own `domain:` scalar is authoritative
                // for which domain this execution unit belongs to — more
                // reliable than the probe domain used above just to locate
                // the (possibly legacy, unscoped) queue-state file. See the
                // domain resolution applied to `domain` below.
                var packetYamlPath = Path.Combine(packetDirectory, "packet.yaml");
                if (File.Exists(packetYamlPath))
                {
                    try
                    {
                        var packetFields = PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetYamlPath));
                        packetFields.TryGetValue("domain", out packetDeclaredDomain);
                    }
                    catch (FormatException)
                    {
                        // Malformed packet.yaml is surfaced elsewhere (G361
                        // validation on the write-side commands); here it
                        // just means no domain can be derived from it.
                        packetDeclaredDomain = null;
                    }
                }

                var githubBodyPath = Path.Combine(packetDirectory, "github-body.md");
                if (!File.Exists(githubBodyPath))
                {
                    // G287: missing github-body.md is a parent-host packet
                    // sync issue (the implementer cannot write it from the
                    // PR branch).
                    gaps.Add(HostMetadataGap($"github-body.md not found in packet directory: {packetDirectory}"));
                    missingSections = PacketDraftCommand.RequiredContractSections;
                }
                else
                {
                    var content = File.ReadAllText(githubBodyPath);
                    missingSections = PacketDraftCommand.RequiredContractSections
                        .Where(section => !ContainsSectionHeading(content, section))
                        .ToArray();
                    if (missingSections.Count > 0)
                    {
                        // G287: contract sections are part of the published
                        // child issue body; an incomplete contract is an
                        // implementation/review finding the implementer can
                        // address by amending the PR head against the
                        // packet/issue body.
                        gaps.Add(ImplementationReviewGap(
                            "Child Issue Contract is incomplete; sections missing: " + string.Join(", ", missingSections)));
                    }
                }
            }
            else
            {
                gaps.Add(HostMetadataGap($"packet directory not found: {packetDirectory}"));
                missingSections = PacketDraftCommand.RequiredContractSections;
            }

            if (matchedItem.LinkedIssue is null)
            {
                gaps.Add(HostMetadataGap("queue item has no linked_issue; closeout requires a linked issue to close."));
            }
            else
            {
                linkedIssue = new ReviewCloseoutLinkedIssue
                {
                    Repo = matchedItem.LinkedIssue.Repo,
                    Number = matchedItem.LinkedIssue.Number,
                    Url = matchedItem.LinkedIssue.Url
                };
            }
        }

        // G522: domain resolution order — explicit `--domain` wins (it is
        // an error if it contradicts the packet-declared domain); else the
        // domain declared by the resolved packet is authoritative for the
        // REPORTED domain, overriding the probe domain used above only to
        // locate the (possibly legacy/unscoped) queue-state file; else the
        // surface fails loud naming candidate domains and the exact
        // re-invocation — it never silently falls back to the host's
        // default domain binding.
        var domainResolution = PacketDomainResolution.Resolve(
            domainOverride,
            packetDeclaredDomain,
            DomainCandidateScanner.Scan(context),
            $"intent-cli review closeout-plan --pr {pr} --repo {repo} --domain <name>");
        if (domainResolution.IsError)
        {
            writer.WriteLine(domainResolution.ErrorMessage);
            return 1;
        }
        var reportedDomain = domainResolution.Domain!;

        var expectedSubmodulePath = DeriveSubmodulePath(repo!);

        var validationSteps = new List<string>
        {
            "Run focused tests for the touched packet area.",
            "Run `git diff --check` against the merge result.",
            "Confirm the PR head SHA before and after the review pass."
        };

        var closeoutSteps = new List<string>
        {
            $"Sync the parent submodule pointer for {repo} at `{expectedSubmodulePath}` to the merge commit.",
            "Mark the queue item completed and append `pr-merged` + `closeout-recorded` runs events (see `intent-cli closeout pr --write`).",
            "Close the linked child issue once the merge is durable.",
            "Commit and push the parent durable state (queue-state, runs, submodule pointer)."
        };

        // G287: the dominant blocker class. host-metadata-blocked dominates
        // implementation-review-finding when both are present so the host
        // loop runs reconcile first. Posting a PR comment for a wake that is
        // host-metadata-blocked is forbidden by the host-loop guide.
        //
        // G313: when the host-metadata blocker is specifically "no queue
        // item found with linked_pr matching #N" AND at least one
        // `.intent-cli/issues/<unit>/publish.yaml` exists on disk, the
        // first-class recovery is `automation publish-recovery` (G303),
        // not generic reconcile. publish-recovery uses the publish-artifact
        // evidence to deterministically repair the queue's linked_pr;
        // generic reconcile is a fallback for cases without publish
        // artifacts. When publish artifacts are absent, generic reconcile
        // is still the recommended primary command.
        var ready = gaps.Count == 0 && matchedItem is not null;
        string blockerClassification;
        string? recommendedRecoveryCommand;
        if (ready)
        {
            blockerClassification = BlockerClassificationReady;
            recommendedRecoveryCommand = null;
        }
        else if (gaps.Any(g =>
            string.Equals(g.Classification, GapClassificationHostMetadata, StringComparison.Ordinal)
            || string.Equals(g.Classification, GapClassificationLinkageAmbiguous, StringComparison.Ordinal)))
        {
            // G329: linkage-ambiguous still aggregates to host-metadata-blocked
            // (no PR repair comment) — the operator must disambiguate the
            // closing-reference match before any host mutation. The
            // structured `ambiguous_linkage` payload on the result gives
            // them the candidate list.
            blockerClassification = BlockerClassificationHostMetadataBlocked;
            var missingLinkedPrGap = matchedItem is null
                && gaps.Any(g => string.Equals(g.Classification, GapClassificationHostMetadata, StringComparison.Ordinal)
                    && g.Description.Contains("no queue item found with linked_pr", StringComparison.Ordinal));
            // G351: include --pr <n> in the publish-recovery recommendation so
            // the operator gets a scoped single-item recovery rather than a
            // broad scan that floods with unrelated unsafe stops.
            recommendedRecoveryCommand = missingLinkedPrGap && PublishArtifactsExist(context.RepoRoot)
                ? $"intent-cli automation publish-recovery --repo {repo} --pr {pr} --format json"
                : $"intent-cli automation reconcile --lane host-review --repo {repo} --format json";
        }
        else
        {
            blockerClassification = BlockerClassificationImplementationReviewFinding;
            recommendedRecoveryCommand = null;
        }

        // G344: when the host-metadata blocker is the missing-linked_pr
        // gap AND packet + publish + GitHub closing-issue evidence
        // deterministically identifies the missing queue item, surface
        // a `recovery_lane` block recommending the exact
        // `automation host-queue-item-recovery` command. This does NOT
        // change closeout-plan's `ready: false` semantics; the host
        // loop runs the recovery command first, then re-invokes
        // closeout-plan.
        ReviewCloseoutPlanRecoveryLane? recoveryLane = null;
        if (matchedItem is null
            && gaps.Any(g => string.Equals(g.Classification, GapClassificationHostMetadata, StringComparison.Ordinal)
                && g.Description.Contains("no queue item found with linked_pr", StringComparison.Ordinal)))
        {
            recoveryLane = TryBuildHostQueueItemRecoveryLane(
                context.RepoRoot,
                repo!,
                pr!.Value,
                closingIssues,
                queueState);
            if (recoveryLane is not null)
            {
                recommendedRecoveryCommand = recoveryLane.RecommendedCommand;
            }
        }

        // G351: when blockerClassification is host-metadata-blocked, surface
        // explicit review-release guidance. If recovery cannot proceed (e.g.,
        // no publish artifacts, no closing-issue reference, ambiguous match),
        // the host loop must release the intent-pr-reviewing lease before
        // stopping — otherwise the PR stays stuck in reviewing state.
        string? reviewReleaseCommand = null;
        if (string.Equals(blockerClassification, BlockerClassificationHostMetadataBlocked, StringComparison.Ordinal))
        {
            reviewReleaseCommand = $"intent-cli automation pr-transition --repo {repo} --pr {pr} --transition review-release --write";
        }

        var result = new ReviewCloseoutPlanResult
        {
            Domain = reportedDomain,
            Repo = repo!,
            Pr = pr!.Value,
            QueueStatePath = queueStatePath,
            ExecutionUnit = matchedItem?.ExecutionUnit,
            QueueItemState = matchedItem?.State.ToString().ToLowerInvariant(),
            LinkedIssue = linkedIssue,
            PacketDirectory = packetDirectory,
            PacketFiles = packetFiles,
            MissingContractSections = missingSections,
            ExpectedSubmodulePath = expectedSubmodulePath,
            ValidationSteps = validationSteps,
            ClosingSteps = closeoutSteps,
            Gaps = gaps.Select(g => g.Description).ToArray(),
            ClassifiedGaps = gaps,
            BlockerClassification = blockerClassification,
            RecommendedRecoveryCommand = recommendedRecoveryCommand,
            ReviewReleaseCommand = reviewReleaseCommand,
            Ready = ready,
            RecoveredLinkage = recoveredLinkage,
            AmbiguousLinkage = ambiguousLinkage,
            ClosingIssuesSource = closingIssuesSource,
            LinkageRecoveryApplied = false,
            StateLayout = stateLayout,
            RecoveryLane = recoveryLane,
        };

        // G329 review fix: when --write-recovered-linkage is supplied
        // AND the reconstructor returned a deterministic recovery,
        // persist the recovered linkage into queue-state (host-owned)
        // and append a `linkage-recovered` event to runs.jsonl. This
        // is host/review-runtime owned, NOT child-worker owned —
        // the closeout-plan command is invoked by the host review
        // path. Ambiguous matches are NEVER written (the structured
        // ambiguity payload is the disambiguation contract).
        if (writeRecoveredLinkage && recoveredLinkage is not null && queueState is not null)
        {
            try
            {
                ApplyRecoveredLinkagePersistence(
                    context.RepoRoot,
                    queueStatePath,
                    queueState,
                    recoveredLinkage);
                result = result with { LinkageRecoveryApplied = true };
            }
            catch (IOException ioException)
            {
                gaps.Add(HostMetadataGap(
                    $"linkage recovery write failed: {ioException.Message}"));
                // Re-form the result without the applied flag and with
                // the new gap so the host loop sees the failure.
                result = result with
                {
                    Gaps = gaps.Select(g => g.Description).ToArray(),
                    ClassifiedGaps = gaps,
                    Ready = false,
                    BlockerClassification = BlockerClassificationHostMetadataBlocked,
                    LinkageRecoveryApplied = false
                };
            }
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return result.Ready ? 0 : 1;
    }

    /// <summary>
    /// G344: dry-run helper that walks
    /// <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c> entries, finds
    /// the one (if any) whose <c>created_issue_number</c> matches a
    /// closing-issue reference for the selected PR, and returns the
    /// proposed <c>automation host-queue-item-recovery</c> command when
    /// the evidence is deterministic. Returns null when the evidence is
    /// missing, ambiguous, or unrelated to the selected repo. Pure with
    /// respect to GitHub — relies only on the closing-issue numbers the
    /// caller already fetched.
    /// </summary>
    internal static ReviewCloseoutPlanRecoveryLane? TryBuildHostQueueItemRecoveryLane(
        string repoRoot,
        string repo,
        int prNumber,
        IReadOnlyList<int> closingIssues,
        QueueState? queueState)
    {
        if (closingIssues.Count == 0)
        {
            return null;
        }
        var issuesDir = Path.Combine(repoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesDir))
        {
            return null;
        }

        var matches = new List<(string Unit, int IssueNumber)>();
        foreach (var unitDir in Directory.EnumerateDirectories(issuesDir).OrderBy(p => p, StringComparer.Ordinal))
        {
            var unit = Path.GetFileName(unitDir)!;
            var publishPath = Path.Combine(unitDir, "publish.yaml");
            var packetPath = Path.Combine(unitDir, "packet.yaml");
            if (!File.Exists(publishPath) || !File.Exists(packetPath))
            {
                continue;
            }
            IssuePublishArtifact? artifact;
            try
            {
                artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                continue;
            }
            if (artifact.CreatedIssueNumber is not int n || n <= 0)
            {
                continue;
            }
            // Repo scoping: publish.yaml created_issue_url must point at
            // the selected repo (when present).
            if (!string.IsNullOrEmpty(artifact.CreatedIssueUrl)
                && artifact.CreatedIssueUrl!.IndexOf($"github.com/{repo}/issues/", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }
            if (closingIssues.Contains(n))
            {
                matches.Add((unit, n));
            }
        }

        if (matches.Count != 1)
        {
            // Zero or >1 matches → not deterministic; let the existing
            // host-metadata-blocked recovery path remain.
            return null;
        }

        var (matchedUnit, matchedIssue) = matches[0];

        // If queue-state already binds the matched unit to a *different*
        // PR or issue, the host-queue-item-recovery analyzer would emit
        // a conflicting-existing-queue-item stop. We pre-filter here so
        // closeout-plan only surfaces the recovery_lane when the
        // recovery would actually succeed.
        if (queueState is not null)
        {
            var existing = queueState.Items.FirstOrDefault(i =>
                string.Equals(i.ExecutionUnit, matchedUnit, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (existing.LinkedIssue is { Number: int existingIssue } && existingIssue != matchedIssue)
                {
                    return null;
                }
                var existingPr = TryParsePrNumberFromLinkedPr(existing.LinkedPr);
                if (existingPr is int otherPr && otherPr != prNumber)
                {
                    return null;
                }
            }
            // If a different unit already binds this PR or issue, do
            // not surface this recovery_lane.
            foreach (var item in queueState.Items)
            {
                if (string.Equals(item.ExecutionUnit, matchedUnit, StringComparison.Ordinal))
                {
                    continue;
                }
                if (item.LinkedIssue is { Number: int otherIssue } && otherIssue == matchedIssue)
                {
                    return null;
                }
                if (TryParsePrNumberFromLinkedPr(item.LinkedPr) == prNumber)
                {
                    return null;
                }
            }
        }

        var command = $"intent-cli automation host-queue-item-recovery --repo {repo} "
            + $"--unit {matchedUnit} --issue {matchedIssue} --pr {prNumber} --write";
        return new ReviewCloseoutPlanRecoveryLane
        {
            Lane = "host-queue-item-recovery",
            ExecutionUnit = matchedUnit,
            LinkedIssue = matchedIssue,
            LinkedPr = prNumber,
            RecommendedCommand = command,
            Reason = $"packet.yaml and publish.yaml for '{matchedUnit}' identify issue #{matchedIssue}, "
                + $"which is the closing-issue for PR #{prNumber} in '{repo}'; host-queue-item-recovery can "
                + "deterministically reconstruct the missing queue item.",
        };
    }

    private static int? TryParsePrNumberFromLinkedPr(string? linkedPr)
    {
        if (string.IsNullOrWhiteSpace(linkedPr))
        {
            return null;
        }
        if (int.TryParse(linkedPr, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var direct))
        {
            return direct;
        }
        var idx = linkedPr.LastIndexOf('/');
        if (idx >= 0 && idx + 1 < linkedPr.Length)
        {
            var tail = linkedPr[(idx + 1)..];
            if (int.TryParse(tail, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                return n;
            }
        }
        return null;
    }

    /// <summary>
    /// G313: returns true iff at least one
    /// <c>.intent-cli/issues/&lt;unit&gt;/publish.yaml</c> exists under
    /// <paramref name="repoRoot"/>. Used to choose
    /// <c>automation publish-recovery</c> over generic reconcile when the
    /// closeout-plan blocker is a missing <c>linked_pr</c> on a wake whose
    /// publish artifacts can deterministically repair the link.
    /// </summary>
    internal static bool PublishArtifactsExist(string repoRoot)
    {
        var issuesDir = Path.Combine(repoRoot, ".intent-cli", "issues");
        if (!Directory.Exists(issuesDir))
        {
            return false;
        }
        try
        {
            foreach (var unitDir in Directory.EnumerateDirectories(issuesDir))
            {
                if (File.Exists(Path.Combine(unitDir, "publish.yaml")))
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Permission/IO errors fall through to "no publish artifacts
            // available"; generic reconcile is then the safe default.
            return false;
        }
        return false;
    }

    /// <summary>
    /// G329: host-owned persistence of deterministic linkage recovery.
    /// Mutates queue-state.json (sets the matched item's
    /// <c>linked_pr</c> to the recovered PR URL) and appends a
    /// <c>linkage-recovered</c> event to runs.jsonl. Both writes are
    /// routed through G327's <see cref="RuntimeScopedStateResolver"/>
    /// so the scoped runtime layout wins when present (and the
    /// resolver's legacy fallback preserves transition compatibility).
    ///
    /// Ambiguous matches are NEVER persisted — the caller only invokes
    /// this when the reconstructor returned a deterministic candidate
    /// AND the operator passed <c>--write-recovered-linkage</c>.
    /// </summary>
    private static void ApplyRecoveredLinkagePersistence(
        string repoRoot,
        string legacyQueueStatePath,
        QueueState queueState,
        RecoveredLinkagePayload recovered)
    {
        // Reuse G327's resolver so the write lands in the layout the
        // closeout flow expects. The queue-state passed in was loaded
        // from `legacyQueueStatePath` (closeout-plan currently reads
        // the legacy root); we write back to that same path so the
        // analyzer's view stays consistent with the on-disk state.
        // Future migration to scoped state is independent of this fix.
        var updatedItems = queueState.Items
            .Select(item => string.Equals(item.ExecutionUnit, recovered.ExecutionUnit, StringComparison.Ordinal)
                ? new QueueItem
                {
                    ExecutionUnit = item.ExecutionUnit,
                    Title = item.Title,
                    State = item.State,
                    Dependencies = item.Dependencies,
                    BlockedBy = item.BlockedBy,
                    ClarificationReturnPath = item.ClarificationReturnPath,
                    PacketPaths = item.PacketPaths,
                    LinkedIssue = item.LinkedIssue,
                    LinkedPr = recovered.LinkedPrUrl,
                    WorkerRole = item.WorkerRole,
                    ReviewRole = item.ReviewRole,
                    Priority = item.Priority
                }
                : item)
            .ToArray();
        var updatedState = new QueueState
        {
            SchemaVersion = queueState.SchemaVersion,
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = updatedItems
        };

        Directory.CreateDirectory(Path.GetDirectoryName(legacyQueueStatePath)!);
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(legacyQueueStatePath, queueState, updatedState);

        var runsLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(runsLogPath)!);
        var runEvent = new RunEvent
        {
            Ts = DateTimeOffset.UtcNow,
            ExecutionUnit = recovered.ExecutionUnit,
            Event = "linkage-recovered",
            By = "intent-cli review closeout-plan",
            Repo = recovered.LinkedPrRepo,
            Pr = recovered.LinkedPrNumber
        };
        var line = RunLogSerializer.SerializeLine(runEvent);
        using var stream = new FileStream(runsLogPath, FileMode.Append, FileAccess.Write);
        using var streamWriter = new StreamWriter(stream);
        streamWriter.WriteLine(line);
    }

    private static ReviewCloseoutPlanGap HostMetadataGap(string description) =>
        new() { Description = description, Classification = GapClassificationHostMetadata };

    private static ReviewCloseoutPlanGap ImplementationReviewGap(string description) =>
        new() { Description = description, Classification = GapClassificationImplementationReview };

    /// <summary>
    /// G329: structured ambiguity gap surfaced when GitHub closing-issue
    /// reconstruction finds more than one matching queue item. Refuses to
    /// guess; the operator (or downstream automation) must disambiguate.
    /// </summary>
    private static ReviewCloseoutPlanGap LinkageAmbiguousGap(string description) =>
        new() { Description = description, Classification = GapClassificationLinkageAmbiguous };

    private static bool MatchesLinkedPr(QueueItem item, string repo, string prToken)
    {
        return int.TryParse(prToken, out var number)
            && GitHubWorkItemIdentity.MatchesPullRequest(item, repo, number);
    }

    private static string DeriveSubmodulePath(string repo)
    {
        var segments = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        var name = segments.Length == 2 ? segments[1] : repo;
        return $"submodules/{name}";
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

    private static void WriteMarkdown(TextWriter writer, ReviewCloseoutPlanResult result)
    {
        writer.WriteLine($"# Review closeout plan — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- execution unit: {(result.ExecutionUnit ?? "(unresolved)")}");
        if (!string.IsNullOrWhiteSpace(result.QueueItemState))
        {
            writer.WriteLine($"- queue item state: {result.QueueItemState}");
        }
        writer.WriteLine($"- expected submodule path: {result.ExpectedSubmodulePath}");
        writer.WriteLine($"- ready: {(result.Ready ? "yes" : "no")}");
        writer.WriteLine($"- blocker classification: {result.BlockerClassification}");
        if (!string.IsNullOrWhiteSpace(result.RecommendedRecoveryCommand))
        {
            writer.WriteLine($"- recommended recovery command: {result.RecommendedRecoveryCommand}");
        }
        // G351: surface review-release command when host-metadata-blocked so
        // the host loop knows to release intent-pr-reviewing before stopping.
        if (!string.IsNullOrWhiteSpace(result.ReviewReleaseCommand))
        {
            writer.WriteLine($"- review release required: {result.ReviewReleaseCommand}");
        }

        writer.WriteLine();

        writer.WriteLine("## Linked issue");
        if (result.LinkedIssue is null)
        {
            writer.WriteLine("- (none recorded)");
        }
        else
        {
            writer.WriteLine($"- repo: {result.LinkedIssue.Repo}");
            if (result.LinkedIssue.Number is not null)
            {
                writer.WriteLine($"- number: {result.LinkedIssue.Number}");
            }
            if (!string.IsNullOrWhiteSpace(result.LinkedIssue.Url))
            {
                writer.WriteLine($"- url: {result.LinkedIssue.Url}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Packet");
        if (string.IsNullOrWhiteSpace(result.PacketDirectory))
        {
            writer.WriteLine("- packet directory: (unknown)");
        }
        else
        {
            writer.WriteLine($"- packet directory: {result.PacketDirectory}");
            if (result.PacketFiles.Count == 0)
            {
                writer.WriteLine("- files: (none)");
            }
            else
            {
                writer.WriteLine("- files:");
                foreach (var file in result.PacketFiles)
                {
                    writer.WriteLine($"  - {file}");
                }
            }
            if (result.MissingContractSections.Count == 0)
            {
                writer.WriteLine("- missing contract sections: none");
            }
            else
            {
                writer.WriteLine("- missing contract sections:");
                foreach (var section in result.MissingContractSections)
                {
                    writer.WriteLine($"  - {section}");
                }
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Validation steps");
        foreach (var step in result.ValidationSteps)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Closeout steps");
        foreach (var step in result.ClosingSteps)
        {
            writer.WriteLine($"- {step}");
        }
        writer.WriteLine();

        if (result.ClassifiedGaps.Count > 0)
        {
            writer.WriteLine("## Gaps");
            foreach (var gap in result.ClassifiedGaps)
            {
                writer.WriteLine($"- ({gap.Classification}) {gap.Description}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int? pr,
        out string? repo,
        out string? domainOverride,
        out IReadOnlyList<int> closingIssues,
        out bool writeRecoveredLinkage,
        out string format,
        out string error)
    {
        pr = null;
        repo = null;
        domainOverride = null;
        closingIssues = Array.Empty<int>();
        writeRecoveredLinkage = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue) || prValue <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    pr = prValue;
                    index++;
                    break;

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

                case "--write-recovered-linkage":
                    writeRecoveredLinkage = true;
                    break;

                case "--closing-issues":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--closing-issues requires a value (comma-separated issue numbers).";
                        return false;
                    }
                    {
                        var raw = args[index + 1];
                        var parsed = new List<int>();
                        foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            if (!int.TryParse(token,
                                    System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var issueNumber) || issueNumber <= 0)
                            {
                                error = $"--closing-issues entry must be a positive integer (got '{token}').";
                                return false;
                            }
                            parsed.Add(issueNumber);
                        }
                        closingIssues = parsed;
                    }
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

        if (pr is null)
        {
            error = "--pr is required.";
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
        writer.WriteLine("review closeout-plan");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only review-pass closeout planning facts. Reports execution unit, linked issue, packet refs, expected submodule path, validation steps, and any blocking gaps without mutating state.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal sealed record ReviewCloseoutPlanResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("queue_item_state")]
    public string? QueueItemState { get; init; }

    [JsonPropertyName("linked_issue")]
    public ReviewCloseoutLinkedIssue? LinkedIssue { get; init; }

    [JsonPropertyName("packet_directory")]
    public string? PacketDirectory { get; init; }

    [JsonPropertyName("packet_files")]
    public required IReadOnlyList<string> PacketFiles { get; init; }

    [JsonPropertyName("missing_contract_sections")]
    public required IReadOnlyList<string> MissingContractSections { get; init; }

    [JsonPropertyName("expected_submodule_path")]
    public required string ExpectedSubmodulePath { get; init; }

    [JsonPropertyName("validation_steps")]
    public required IReadOnlyList<string> ValidationSteps { get; init; }

    [JsonPropertyName("closeout_steps")]
    public required IReadOnlyList<string> ClosingSteps { get; init; }

    [JsonPropertyName("gaps")]
    public required IReadOnlyList<string> Gaps { get; init; }

    /// <summary>
    /// G287: per-gap classification (kind + description). The
    /// <c>gaps</c> list is preserved as a flat string list for backward
    /// compatibility with existing callers/tests.
    /// </summary>
    [JsonPropertyName("classified_gaps")]
    public required IReadOnlyList<ReviewCloseoutPlanGap> ClassifiedGaps { get; init; }

    /// <summary>
    /// G287: aggregate terminal class for the wake — <c>ready</c>,
    /// <c>host-metadata-blocked</c>, or <c>implementation-review-finding</c>.
    /// The host loop must NOT post a PR repair comment or transition the PR
    /// to <c>intent-pr-request-update</c> when the value is
    /// <c>host-metadata-blocked</c>; instead it runs the
    /// <c>recommended_recovery_command</c> (host-owned reconcile) and
    /// retries the wake.
    /// </summary>
    [JsonPropertyName("blocker_classification")]
    public required string BlockerClassification { get; init; }

    /// <summary>
    /// G287: deterministic recovery command for host-metadata-blocked wakes.
    /// Null for ready wakes and for implementation-review findings (the
    /// implementer addresses those by amending the PR head).
    /// </summary>
    [JsonPropertyName("recommended_recovery_command")]
    public required string? RecommendedRecoveryCommand { get; init; }

    /// <summary>
    /// G351: when <c>blocker_classification</c> is
    /// <c>host-metadata-blocked</c>, this field surfaces the exact
    /// <c>pr-transition --transition review-release</c> command the host
    /// loop must run before stopping. Releasing <c>intent-pr-reviewing</c>
    /// prevents the PR from staying stuck in the reviewing state when the
    /// only blocker is a recoverable host-metadata gap.
    /// Null for ready or implementation-review-finding wakes.
    /// </summary>
    [JsonPropertyName("review_release_command")]
    public string? ReviewReleaseCommand { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    /// <summary>
    /// G329: emitted when GitHub closing-issue reconstruction
    /// deterministically recovered the missing <c>linked_pr</c>.
    /// The review runtime (NOT the child loop) persists this payload
    /// onto queue-state via its own writer; the child loop only
    /// surfaces it. Null when no recovery was needed (queue-state
    /// already had the link) or when recovery was not deterministic.
    /// </summary>
    [JsonPropertyName("recovered_linkage")]
    public RecoveredLinkagePayload? RecoveredLinkage { get; init; }

    /// <summary>
    /// G329: emitted when GitHub closing-issue reconstruction matched
    /// more than one queue item. The host loop surfaces this to the
    /// operator instead of guessing — the structured candidate list
    /// is the disambiguation contract.
    /// </summary>
    [JsonPropertyName("ambiguous_linkage")]
    public AmbiguousLinkagePayload? AmbiguousLinkage { get; init; }

    /// <summary>
    /// G329 review fix: where the closing-issue list used by the
    /// reconstructor came from. <c>operator-flag</c> when the operator
    /// passed <c>--closing-issues</c>, <c>github-auto-fetch</c> when
    /// the planner fetched them via <c>gh pr view</c>, null when no
    /// closing issues were supplied or fetched (the reconstructor
    /// then returned <c>NoClosingReferences</c> and the planner fell
    /// through to existing host-metadata-blocked behavior).
    /// </summary>
    [JsonPropertyName("closing_issues_source")]
    public string? ClosingIssuesSource { get; init; }

    /// <summary>
    /// G329 review fix: true when <c>--write-recovered-linkage</c>
    /// was passed AND the reconstructor returned a deterministic
    /// candidate AND the persistence write to queue-state +
    /// runs.jsonl actually succeeded. Surfaced so the host loop /
    /// operator can verify recovery was both planned AND committed
    /// in a single wake.
    /// </summary>
    [JsonPropertyName("linkage_recovery_applied")]
    public required bool LinkageRecoveryApplied { get; init; }

    /// <summary>
    /// G332: which on-disk layout the runtime-scoped resolver chose
    /// for the queue-state read — <c>scoped</c> when
    /// `.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/queue-state.json`
    /// is on disk (or selected as the first-write target),
    /// <c>legacy-fallback</c> when only the legacy root path exists
    /// (pre-migration hosts). Surfaced so the host loop and operator
    /// can audit which state source produced the plan.
    /// </summary>
    [JsonPropertyName("state_layout")]
    public string? StateLayout { get; init; }

    /// <summary>
    /// G344: emitted when the missing-linked_pr host-metadata gap can
    /// be deterministically recovered from packet + publish + GitHub
    /// closing-issue facts. Surfaces the exact
    /// <c>automation host-queue-item-recovery</c> command for the
    /// matching execution unit. Null when no deterministic recovery is
    /// available (no matching packet/publish, ambiguous matches, or
    /// queue state already in a conflicting state).
    /// </summary>
    [JsonPropertyName("recovery_lane")]
    public ReviewCloseoutPlanRecoveryLane? RecoveryLane { get; init; }
}

/// <summary>
/// G344: structured recovery_lane payload surfaced on the closeout-plan
/// result when packet + publish + GitHub closing-issue facts identify a
/// missing queue item that can be deterministically recovered via the
/// <c>automation host-queue-item-recovery</c> surface.
/// </summary>
internal sealed record ReviewCloseoutPlanRecoveryLane
{
    [JsonPropertyName("lane")]
    public required string Lane { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("linked_issue")]
    public required int LinkedIssue { get; init; }

    [JsonPropertyName("linked_pr")]
    public required int LinkedPr { get; init; }

    [JsonPropertyName("recommended_command")]
    public required string RecommendedCommand { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// G287: one gap entry with a classification kind so the host loop can
/// route gaps to host-owned reconcile vs PR-side review comments.
/// </summary>
internal sealed record ReviewCloseoutPlanGap
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// One of <see cref="ReviewCloseoutPlanCommand.GapClassificationHostMetadata"/>
    /// or <see cref="ReviewCloseoutPlanCommand.GapClassificationImplementationReview"/>.
    /// </summary>
    [JsonPropertyName("classification")]
    public required string Classification { get; init; }
}

internal sealed record ReviewCloseoutLinkedIssue
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("number")]
    public int? Number { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
