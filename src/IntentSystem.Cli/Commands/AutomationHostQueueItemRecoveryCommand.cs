using IntentSystem.Supervisor;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G344: <c>intent-cli automation host-queue-item-recovery
/// --repo &lt;owner/repo&gt; [--unit &lt;u&gt;] [--issue &lt;n&gt;] [--pr &lt;m&gt;]
/// [--write] [--format markdown|json]</c> — host-side recovery lane
/// that detects when packet + publish + GitHub issue + GitHub PR facts
/// for an execution unit deterministically identify a missing or
/// stale queue-state item, and reports (or applies with
/// <c>--write</c>) the high-confidence repair. Strictly scopes scans
/// to the requested target repo so unrelated domains do not surface
/// as primary blockers.
///
/// All deterministic decisions live in the pure
/// <see cref="HostQueueItemRecoveryAnalyzer"/>; this command only
/// collects the inputs (filesystem + gh) and translates the result
/// into an exit code / optionally mutates <c>queue-state.json</c>.
/// </summary>
internal static class AutomationHostQueueItemRecoveryCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string ModeDryRun = "dry-run";
    private const string ModeWrite = "write";

    /// <summary>
    /// G344 second-round repair: emitted by the command (not the
    /// analyzer) when the GitHub issue lookup fails — the PR's closing
    /// reference points at issue #N but we cannot retrieve that issue
    /// from GitHub. Previously we silently fabricated an open issue with
    /// empty labels; the recovery contract now requires real label
    /// evidence, so a lookup failure becomes a structured unsafe stop.
    /// </summary>
    public const string ReasonIssueLookupFailed = "issue-lookup-failed";

    /// <summary>
    /// G344 second-round repair: emitted when packet.yaml carries
    /// conflicting <c>implementation_issue</c> and
    /// <c>implementation_issue_packet</c> sections that disagree on
    /// <c>target_repo</c>.
    /// </summary>
    public const string ReasonPacketTargetRepoConflict = "packet-target-repo-conflict";

    /// <summary>Test seam — replaces the gh-backed PR lookup.</summary>
    public static Func<IGitHubPrLookup>? PrLookupFactory { get; set; }

    /// <summary>Test seam — replaces the gh-backed issue lookup.</summary>
    public static Func<IGitHubAutomationIssueLookup>? IssueLookupFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var unit, out var issue, out var pr, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var domain = context.Config.Project.Domain;
        var issuesDir = Path.Combine(context.RepoRoot, ".intent-cli", "issues");

        // Load queue-state (may be missing — that's still a valid input
        // because the whole point of this lane is "queue item is
        // missing or stale").
        var queueLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            context.RepoRoot, domain, repo!);
        var queueStatePath = queueLocation.Path;
        QueueState? queueState = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"failed to parse queue-state at {queueStatePath}: {exception.Message}");
                return 1;
            }
        }

        // Collect publish artifacts under .intent-cli/issues/. When
        // --unit is supplied we restrict to that subdirectory; otherwise
        // we walk every unit. We always filter strictly by --repo, both
        // via publish.yaml created_issue_url and (when available) packet
        // target_repo.
        var publishArtifacts = new List<(string Unit, string Path, IssuePublishArtifact? Artifact)>();
        if (Directory.Exists(issuesDir))
        {
            IEnumerable<string> unitDirs;
            if (!string.IsNullOrWhiteSpace(unit))
            {
                var specific = Path.Combine(issuesDir, unit!);
                unitDirs = Directory.Exists(specific) ? new[] { specific } : Array.Empty<string>();
            }
            else
            {
                unitDirs = Directory.EnumerateDirectories(issuesDir).OrderBy(p => p, StringComparer.Ordinal);
            }

            foreach (var unitDir in unitDirs)
            {
                var u = Path.GetFileName(unitDir)!;
                var artifactPath = Path.Combine(unitDir, "publish.yaml");
                if (!File.Exists(artifactPath))
                {
                    continue;
                }
                IssuePublishArtifact? artifact = null;
                try
                {
                    artifact = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    artifact = null;
                }
                publishArtifacts.Add((u, artifactPath, artifact));
            }
        }

        // Repo-scope filter: drop any artifact whose created_issue_url
        // points at a different repo. The analyzer would catch this but
        // we want unrelated repos to be EXCLUDED from the output rather
        // than surfaced as blockers.
        var scopedArtifacts = publishArtifacts
            .Where(a => a.Artifact is null || ArtifactBelongsToRepo(a.Artifact, repo!))
            .ToList();

        // Build a map of issue-number → units that claim it (after repo
        // filter), so the analyzer can flag multiple-matching-packets.
        var issueToUnits = new Dictionary<int, List<string>>();
        foreach (var (u, _, artifact) in scopedArtifacts)
        {
            if (artifact?.CreatedIssueNumber is int n && n > 0)
            {
                if (!issueToUnits.TryGetValue(n, out var list))
                {
                    list = new List<string>();
                    issueToUnits[n] = list;
                }
                list.Add(u);
            }
        }

        // Existing queue items (scoped to the requested repo via
        // linked_issue.repo / linked_pr URL).
        var existingItemsForRepo = (queueState?.Items ?? Array.Empty<QueueItem>())
            .Where(item => QueueItemBelongsToRepo(item, repo!))
            .ToList();

        var prLookup = PrLookupFactory?.Invoke() ?? new GhCliGitHubPrLookup();
        var issueLookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubAutomationIssueLookup();

        // Cache GitHub PR lookups so a sweep over many units doesn't
        // re-fetch the same PR per artifact.
        var prCache = new Dictionary<int, GitHubPrLookupResult?>();
        var issueCache = new Dictionary<int, GitHubAutomationIssueCandidate?>();

        var perCandidate = new List<HostQueueItemRecoveryRecord>();
        foreach (var (u, artifactPath, artifact) in scopedArtifacts)
        {
            if (artifact is null)
            {
                continue;
            }

            // Skip artifacts that don't carry a created issue — they
            // can't be recovered into a queue item without an issue
            // anchor.
            if (artifact.CreatedIssueNumber is not int issueNumber || issueNumber <= 0)
            {
                continue;
            }

            // If --issue was supplied, restrict to artifacts matching
            // that issue.
            if (issue is int requestedIssue && requestedIssue != issueNumber)
            {
                continue;
            }

            // Resolve the PR for this artifact. If --pr is set, use it;
            // otherwise we need to find a PR that closes the issue.
            int? prNumber = pr;
            GitHubPrLookupResult? prView = null;
            if (prNumber is int prn)
            {
                prView = LookupPr(prLookup, prCache, repo!, prn);
            }
            else
            {
                // No --pr — try to discover the PR via the issue's PR
                // listings indirectly. For G344's recovery lane we
                // require the operator to supply --pr or --unit so the
                // analyzer has a single target. If neither is supplied
                // and we don't know the PR, we skip this artifact (it
                // remains visible in the result as `skipped-no-pr`).
                perCandidate.Add(new HostQueueItemRecoveryRecord
                {
                    Unit = u,
                    PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                    Result = "skipped",
                    ReasonCode = "no-pr-supplied",
                    Explanation = $"no --pr supplied; cannot deterministically attach queue item recovery for '{u}'. Pass --pr <n> or --unit <u>.",
                });
                continue;
            }

            var resolution = ResolveClosingIssue(prView, issueLookup, issueCache, repo!, issueNumber);
            if (resolution.Kind == ClosingIssueResolutionKind.LookupFailed)
            {
                // G344 second-round repair: issue lookup failed. Do NOT
                // fabricate empty-label issue evidence. Surface as a
                // structured unsafe stop so the operator sees the real
                // blocker (and cannot accidentally recover queue state
                // from an unverified GitHub issue).
                perCandidate.Add(new HostQueueItemRecoveryRecord
                {
                    Unit = u,
                    PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                    Result = HostQueueItemRecoveryAnalyzer.ResultUnsafeStop,
                    ReasonCode = ReasonIssueLookupFailed,
                    Explanation = $"PR #{prNumber.Value} in '{repo}' closes issue #{resolution.FailedIssueNumber} "
                        + "but the issue lookup failed (HTTP error, parse failure, or not found); "
                        + "refusing to recover queue state without verifying the issue's labels.",
                });
                continue;
            }
            var closingIssue = resolution.Issue;

            // Other units (post repo-filter) claiming the same issue.
            var otherUnits = issueToUnits.TryGetValue(issueNumber, out var us)
                ? us.Where(x => !string.Equals(x, u, StringComparison.Ordinal)).ToArray()
                : Array.Empty<string>();

            // Project existing queue items down to the analyzer shape.
            // Repo-scoped: only items already linked to the requested
            // repo are passed to the analyzer, so a same-issue or same-PR
            // number on an unrelated repo cannot trigger
            // `conflicting-existing-queue-item`.
            var existingProjected = existingItemsForRepo
                .Select(ToExisting)
                .Where(i => i is not null)
                .Cast<HostQueueItemRecoveryExistingItem>()
                .ToArray();

            var packetPath = Path.Combine(issuesDir, u, "packet.yaml");
            var packetTargetRepo = TryReadPacketTargetRepo(packetPath, out var packetTargetRepoConflict);
            var packetExists = File.Exists(packetPath);
            if (packetTargetRepoConflict is { Length: > 0 })
            {
                // G344 second-round repair: conflicting target_repo
                // across packet schemas — refuse to silently pick one.
                perCandidate.Add(new HostQueueItemRecoveryRecord
                {
                    Unit = u,
                    PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                    Result = HostQueueItemRecoveryAnalyzer.ResultUnsafeStop,
                    ReasonCode = ReasonPacketTargetRepoConflict,
                    Explanation = packetTargetRepoConflict,
                });
                continue;
            }

            var candidate = new HostQueueItemRecoveryCandidate
            {
                TargetRepo = repo!,
                ExecutionUnit = u,
                PacketArtifactExists = packetExists,
                PacketTargetRepo = packetTargetRepo,
                Publish = artifact,
                PrNumber = prNumber.Value,
                PrUrl = prView is not null ? $"https://github.com/{repo}/pull/{prNumber.Value}" : null,
                PrRepo = repo,
                PrState = prView?.State,
                PrIsDraft = prView?.IsDraft ?? false,
                ClosingIssue = closingIssue,
                ExistingQueueItems = existingProjected,
                OtherUnitsClaimingSameIssue = otherUnits,
            };

            var analysis = HostQueueItemRecoveryAnalyzer.Analyze(candidate);
            perCandidate.Add(new HostQueueItemRecoveryRecord
            {
                Unit = u,
                PublishArtifactPath = ToRepoRelative(context.RepoRoot, artifactPath),
                Result = analysis.Result,
                ReasonCode = analysis.ReasonCode,
                Explanation = analysis.Explanation,
                ProposedQueueItem = analysis.ProposedQueueItem,
                RecommendedCommand = analysis.RecommendedCommand,
                Evidence = analysis.Evidence,
            });
        }

        // Apply repairs if --write.
        var appliedUnits = new List<string>();
        var warnings = new List<string>();
        if (write)
        {
            var recoverable = perCandidate
                .Where(r => string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultRecoverable, StringComparison.Ordinal))
                .ToList();
            foreach (var record in recoverable)
            {
                try
                {
                    queueState = ApplyRepair(context, repo!, domain, queueLocation, queueState, record.ProposedQueueItem!);
                    appliedUnits.Add(record.Unit);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    warnings.Add($"{record.Unit}: write failed: {exception.Message}");
                }
            }
        }

        var safeRepairCount = perCandidate.Count(r =>
            string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultRecoverable, StringComparison.Ordinal));
        var unsafeStopCount = perCandidate.Count(r =>
            string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, StringComparison.Ordinal));

        var commandResult = new HostQueueItemRecoveryCommandResult
        {
            Repo = repo!,
            Mode = write ? ModeWrite : ModeDryRun,
            RequestedUnit = unit,
            RequestedIssue = issue,
            RequestedPr = pr,
            SafeRepairCount = safeRepairCount,
            UnsafeStopCount = unsafeStopCount,
            AppliedCount = appliedUnits.Count,
            AppliedUnits = appliedUnits,
            Records = perCandidate,
            Warnings = warnings,
            Summary = BuildSummary(perCandidate, appliedUnits.Count, warnings.Count, write),
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(commandResult, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, commandResult);
        }

        // Non-zero exit when there are unsafe stops and no safe repairs
        // (operator must act), or when --write produced no applications
        // and there were unsafe stops. Pure dry-run with safe repairs
        // exits 0 so host-loop can detect the plan and re-invoke with
        // --write.
        if (warnings.Count > 0)
        {
            return 1;
        }
        if (unsafeStopCount > 0 && safeRepairCount == 0)
        {
            return 1;
        }
        return 0;
    }

    private static string BuildSummary(
        IReadOnlyList<HostQueueItemRecoveryRecord> records,
        int applied,
        int failures,
        bool write)
    {
        var safe = records.Count(r => string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultRecoverable, StringComparison.Ordinal));
        var unsafeCount = records.Count(r => string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, StringComparison.Ordinal));
        if (write)
        {
            return $"host-queue-item-recovery: applied {applied} repair(s); {safe} safe repair(s) detected, {unsafeCount} unsafe stop(s), {failures} failure(s).";
        }
        return $"host-queue-item-recovery (dry-run): {safe} safe repair(s); {unsafeCount} unsafe stop(s).";
    }

    private static QueueState ApplyRepair(
        CliContext context,
        string repo,
        string domain,
        StateLocation queueLocation,
        QueueState? currentState,
        HostQueueItemRecoveryProposedItem proposed)
    {
        // Decide write target — when queue-state already exists we
        // write back to its path (preserving scoped/legacy layout). When
        // missing, we create a new scoped file.
        string writePath = queueLocation.Path;
        if (queueLocation.Kind == StateLocationKind.MissingPreferScoped)
        {
            writePath = queueLocation.Path;
        }

        var items = currentState?.Items?.ToList() ?? new List<QueueItem>();
        var idx = items.FindIndex(i => string.Equals(i.ExecutionUnit, proposed.ExecutionUnit, StringComparison.Ordinal));
        var newLinkedIssue = new LinkedIssue
        {
            Repo = proposed.LinkedIssueRepo,
            Number = proposed.LinkedIssueNumber,
            Url = proposed.LinkedIssueUrl,
        };

        if (idx >= 0)
        {
            var current = items[idx];
            items[idx] = new QueueItem
            {
                ExecutionUnit = current.ExecutionUnit,
                Title = string.IsNullOrWhiteSpace(current.Title)
                    ? (proposed.Title ?? proposed.ExecutionUnit)
                    : current.Title,
                State = current.State == QueueItemState.Queued ? QueueItemState.Review : current.State,
                Dependencies = current.Dependencies,
                BlockedBy = current.BlockedBy,
                ClarificationReturnPath = current.ClarificationReturnPath,
                PacketPaths = current.PacketPaths,
                LinkedIssue = newLinkedIssue,
                LinkedPr = proposed.LinkedPrUrl,
                WorkerRole = current.WorkerRole,
                ReviewRole = current.ReviewRole,
                Priority = current.Priority,
            };
        }
        else
        {
            items.Add(new QueueItem
            {
                ExecutionUnit = proposed.ExecutionUnit,
                Title = proposed.Title ?? proposed.ExecutionUnit,
                State = QueueItemState.Review,
                Dependencies = Array.Empty<string>(),
                BlockedBy = Array.Empty<string>(),
                ClarificationReturnPath = $"intents/{domain}/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Yaml = $".intent-cli/issues/{proposed.ExecutionUnit}/packet.yaml",
                    Implementation = $".intent-cli/issues/{proposed.ExecutionUnit}/implementation.md",
                    ReviewContext = $".intent-cli/issues/{proposed.ExecutionUnit}/review-context.md",
                },
                LinkedIssue = newLinkedIssue,
                LinkedPr = proposed.LinkedPrUrl,
                WorkerRole = "coder",
                ReviewRole = "reviewer",
                Priority = "normal",
            });
        }

        var newState = new QueueState
        {
            SchemaVersion = currentState?.SchemaVersion ?? "1",
            UpdatedAt = DateTimeOffset.UtcNow,
            Items = items,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        // G548: guarded write (no-item-loss + stale-base re-application).
        QueueStatePersistence.Persist(
            writePath,
            currentState ?? new QueueState { SchemaVersion = "1", UpdatedAt = newState.UpdatedAt, Items = Array.Empty<QueueItem>() },
            newState);
        return newState;
    }

    private static GitHubPrLookupResult? LookupPr(
        IGitHubPrLookup prLookup,
        Dictionary<int, GitHubPrLookupResult?> cache,
        string repo,
        int prNumber)
    {
        if (cache.TryGetValue(prNumber, out var cached))
        {
            return cached;
        }

        try
        {
            var view = prLookup.Lookup(repo, prNumber);
            cache[prNumber] = view;
            return view;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            cache[prNumber] = null;
            return null;
        }
    }

    /// <summary>
    /// G344 second-round repair: resolves the GitHub closing issue for
    /// a recovery candidate, distinguishing three outcomes:
    /// <list type="bullet">
    /// <item><description><c>PrDoesNotCloseIssue</c>: PR has no closing
    /// reference for the requested issue number — analyzer will report
    /// <c>pr-does-not-close-issue</c>.</description></item>
    /// <item><description><c>LookupFailed</c>: PR closes the issue but
    /// the issue itself could not be fetched (HTTP error, parse failure,
    /// not found). Command surfaces a structured unsafe stop with
    /// <c>issue-lookup-failed</c>; the analyzer is not consulted.
    /// Previously this path fabricated an open issue with empty labels —
    /// that fabricated evidence is unsafe under the published-issue
    /// label contract.</description></item>
    /// <item><description><c>Resolved</c>: full <see cref="HostQueueItemRecoveryClosingIssue"/>.</description></item>
    /// </list>
    /// </summary>
    private static ClosingIssueResolution ResolveClosingIssue(
        GitHubPrLookupResult? prView,
        IGitHubAutomationIssueLookup issueLookup,
        Dictionary<int, GitHubAutomationIssueCandidate?> issueCache,
        string repo,
        int issueNumber)
    {
        // First: prefer the structured closing-issue references on the
        // PR view. The PR must explicitly close this issue (i.e. the
        // GitHub "linked issues" relation) for the recovery to be
        // deterministic.
        var closesIssue = prView?.ClosingIssuesReferences?.Any(r => r.Number == issueNumber) == true;
        if (prView is null || !closesIssue)
        {
            return ClosingIssueResolution.PrDoesNotCloseIssue();
        }

        if (!issueCache.TryGetValue(issueNumber, out var issueCandidate))
        {
            try
            {
                issueCandidate = issueLookup.GetIssue(repo, issueNumber);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                issueCandidate = null;
            }
            issueCache[issueNumber] = issueCandidate;
        }

        if (issueCandidate is null)
        {
            // GitHub knows the PR closes this issue but we couldn't
            // fetch the issue itself. Do NOT fabricate label evidence —
            // the recovery contract requires real labels (intent-target
            // + intent-pr-created). Surface a structured lookup failure.
            return ClosingIssueResolution.LookupFailed(issueNumber, repo);
        }

        return ClosingIssueResolution.Resolved(new HostQueueItemRecoveryClosingIssue
        {
            Number = issueCandidate.Number,
            Repo = null,
            State = string.IsNullOrEmpty(issueCandidate.State) ? "OPEN" : issueCandidate.State,
            Labels = issueCandidate.Labels.Select(l => l.Name).ToArray(),
            Url = issueCandidate.Url,
        });
    }

    private readonly record struct ClosingIssueResolution(
        ClosingIssueResolutionKind Kind,
        HostQueueItemRecoveryClosingIssue? Issue,
        int FailedIssueNumber,
        string? FailedIssueRepo)
    {
        public static ClosingIssueResolution PrDoesNotCloseIssue() =>
            new(ClosingIssueResolutionKind.PrDoesNotCloseIssue, null, 0, null);
        public static ClosingIssueResolution LookupFailed(int issueNumber, string repo) =>
            new(ClosingIssueResolutionKind.LookupFailed, null, issueNumber, repo);
        public static ClosingIssueResolution Resolved(HostQueueItemRecoveryClosingIssue issue) =>
            new(ClosingIssueResolutionKind.Resolved, issue, 0, null);
    }

    private enum ClosingIssueResolutionKind
    {
        PrDoesNotCloseIssue,
        LookupFailed,
        Resolved,
    }

    private static HostQueueItemRecoveryExistingItem? ToExisting(QueueItem item)
    {
        var prNumber = TryParsePrNumberFromUrl(item.LinkedPr);
        var issueNumber = item.LinkedIssue?.Number;
        return new HostQueueItemRecoveryExistingItem
        {
            ExecutionUnit = item.ExecutionUnit,
            LinkedPrNumber = prNumber,
            LinkedIssueNumber = issueNumber,
            Title = item.Title,
        };
    }

    private static int? TryParsePrNumberFromUrl(string? linkedPr)
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

    private static bool ArtifactBelongsToRepo(IssuePublishArtifact artifact, string repo)
    {
        if (string.IsNullOrEmpty(artifact.CreatedIssueUrl))
        {
            // No url to compare against — keep the artifact; the
            // analyzer will check via packet target_repo and reject any
            // explicit mismatch.
            return true;
        }
        var marker = $"github.com/{repo}/issues/";
        return artifact.CreatedIssueUrl!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool QueueItemBelongsToRepo(QueueItem item, string repo)
    {
        if (item.LinkedIssue is { Repo: { Length: > 0 } repoName }
            && string.Equals(repoName, repo, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(item.LinkedPr)
            && item.LinkedPr!.IndexOf($"github.com/{repo}/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        // Items without a recorded repo link can't be definitively
        // scoped; we keep them OUT of the repo-scoped existing list so
        // unrelated-repo items can't accidentally block a recovery.
        return false;
    }

    /// <summary>
    /// G344 second-round repair: lightweight scanner for the
    /// <c>target_repo</c> scalar in packet.yaml. Parses BOTH the
    /// canonical <c>implementation_issue:</c> schema section (used by
    /// real published packets — see <c>IssuePublishCommandTests</c>) AND
    /// the legacy <c>implementation_issue_packet:</c> section, so a
    /// packet whose <c>target_repo</c> disagrees with the requested
    /// repo is detected regardless of which key the packet author used.
    /// </summary>
    /// <remarks>
    /// If both sections are present and disagree on <c>target_repo</c>,
    /// the conflict is surfaced via <paramref name="conflict"/> so the
    /// caller can emit a structured unsafe stop
    /// (<see cref="ReasonPacketTargetRepoConflict"/>).
    /// </remarks>
    private static string? TryReadPacketTargetRepo(string packetYamlPath)
        => TryReadPacketTargetRepo(packetYamlPath, out _);

    private static string? TryReadPacketTargetRepo(string packetYamlPath, out string? conflict)
    {
        conflict = null;
        if (!File.Exists(packetYamlPath))
        {
            return null;
        }
        string? implementationIssueTargetRepo = null;
        string? implementationIssuePacketTargetRepo = null;
        try
        {
            using var reader = new StringReader(File.ReadAllText(packetYamlPath));
            string? line;
            string currentSection = string.Empty;
            while ((line = reader.ReadLine()) is not null)
            {
                var trimmed = line.TrimEnd();
                if (trimmed.Length > 0
                    && !char.IsWhiteSpace(trimmed[0])
                    && trimmed.EndsWith(":", StringComparison.Ordinal))
                {
                    currentSection = trimmed[..^1];
                    continue;
                }
                if (!string.Equals(currentSection, "implementation_issue", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(currentSection, "implementation_issue_packet", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!trimmed.StartsWith("  target_repo:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                var value = trimmed["  target_repo:".Length..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1];
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                if (string.Equals(currentSection, "implementation_issue", StringComparison.OrdinalIgnoreCase))
                {
                    implementationIssueTargetRepo ??= value;
                }
                else
                {
                    implementationIssuePacketTargetRepo ??= value;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }

        // Both sections present with disagreeing target_repo → conflict.
        if (implementationIssueTargetRepo is { Length: > 0 }
            && implementationIssuePacketTargetRepo is { Length: > 0 }
            && !string.Equals(implementationIssueTargetRepo, implementationIssuePacketTargetRepo, StringComparison.OrdinalIgnoreCase))
        {
            conflict = $"packet.yaml has conflicting target_repo: implementation_issue.target_repo='{implementationIssueTargetRepo}' "
                + $"vs implementation_issue_packet.target_repo='{implementationIssuePacketTargetRepo}'.";
            // Still return the canonical (implementation_issue) value so
            // downstream repo-mismatch checks can run; the caller will
            // surface the conflict separately.
            return implementationIssueTargetRepo;
        }

        // Canonical schema wins when present; otherwise legacy.
        return implementationIssueTargetRepo ?? implementationIssuePacketTargetRepo;
    }

    private static string ToRepoRelative(string repoRoot, string fullPath)
    {
        if (string.IsNullOrEmpty(repoRoot)) return fullPath;
        var normalizedRoot = repoRoot.EndsWith(Path.DirectorySeparatorChar) ? repoRoot : repoRoot + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            return fullPath[normalizedRoot.Length..].Replace('\\', '/');
        }
        return fullPath.Replace('\\', '/');
    }

    private static void WriteMarkdown(TextWriter writer, HostQueueItemRecoveryCommandResult result)
    {
        writer.WriteLine($"# automation host-queue-item-recovery (G344)");
        writer.WriteLine();
        writer.WriteLine($"- repo: `{result.Repo}`");
        writer.WriteLine($"- mode: `{result.Mode}`");
        writer.WriteLine($"- safe repairs: {result.SafeRepairCount}");
        writer.WriteLine($"- unsafe stops: {result.UnsafeStopCount}");
        writer.WriteLine($"- applied: {result.AppliedCount}");
        writer.WriteLine();
        writer.WriteLine(result.Summary);

        var recoverable = result.Records.Where(r => string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultRecoverable, StringComparison.Ordinal)).ToArray();
        if (recoverable.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Safe repairs");
            foreach (var r in recoverable)
            {
                writer.WriteLine($"- `{r.Unit}`: {r.Explanation}");
                if (!string.IsNullOrWhiteSpace(r.RecommendedCommand))
                {
                    writer.WriteLine($"  - recovery: `{r.RecommendedCommand}`");
                }
            }
        }

        var stops = result.Records.Where(r => string.Equals(r.Result, HostQueueItemRecoveryAnalyzer.ResultUnsafeStop, StringComparison.Ordinal)).ToArray();
        if (stops.Length > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Unsafe stops");
            foreach (var s in stops)
            {
                writer.WriteLine($"- `{s.Unit}` ({s.ReasonCode}): {s.Explanation}");
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

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? unit,
        out int? issue,
        out int? pr,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        unit = null;
        issue = null;
        pr = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--repo":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[++i].Trim();
                    break;
                case "--unit":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--unit requires a value.";
                        return false;
                    }
                    unit = args[++i].Trim();
                    break;
                case "--issue":
                    if (i + 1 >= args.Length
                        || !int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueValue)
                        || issueValue <= 0)
                    {
                        error = "--issue requires a positive integer.";
                        return false;
                    }
                    issue = issueValue;
                    i++;
                    break;
                case "--pr":
                    if (i + 1 >= args.Length
                        || !int.TryParse(args[i + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue)
                        || prValue <= 0)
                    {
                        error = "--pr requires a positive integer.";
                        return false;
                    }
                    pr = prValue;
                    i++;
                    break;
                case "--write":
                    write = true;
                    break;
                case "--dry-run":
                    write = false;
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[++i].Trim();
                    if (!string.Equals(requested, FormatJson, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatMarkdown, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[i]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        return true;
    }
}

internal sealed record HostQueueItemRecoveryRecord
{
    [JsonPropertyName("unit_id")]
    public required string Unit { get; init; }

    [JsonPropertyName("publish_artifact_path")]
    public required string PublishArtifactPath { get; init; }

    [JsonPropertyName("result")]
    public required string Result { get; init; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; init; }

    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("proposed_queue_item")]
    public HostQueueItemRecoveryProposedItem? ProposedQueueItem { get; init; }

    [JsonPropertyName("recommended_command")]
    public string? RecommendedCommand { get; init; }

    [JsonPropertyName("evidence")]
    public IReadOnlyList<string>? Evidence { get; init; }
}

internal sealed record HostQueueItemRecoveryCommandResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("requested_unit")]
    public string? RequestedUnit { get; init; }

    [JsonPropertyName("requested_issue")]
    public int? RequestedIssue { get; init; }

    [JsonPropertyName("requested_pr")]
    public int? RequestedPr { get; init; }

    [JsonPropertyName("safe_repair_count")]
    public required int SafeRepairCount { get; init; }

    [JsonPropertyName("unsafe_stop_count")]
    public required int UnsafeStopCount { get; init; }

    [JsonPropertyName("applied_count")]
    public required int AppliedCount { get; init; }

    [JsonPropertyName("applied_units")]
    public required IReadOnlyList<string> AppliedUnits { get; init; }

    [JsonPropertyName("records")]
    public required IReadOnlyList<HostQueueItemRecoveryRecord> Records { get; init; }

    [JsonPropertyName("warnings")]
    public required IReadOnlyList<string> Warnings { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
