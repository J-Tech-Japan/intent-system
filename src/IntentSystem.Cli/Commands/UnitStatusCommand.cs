using System.Globalization;
using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

internal static class UnitStatusCommand
{
    private const string Usage = "Usage: intent-cli unit status --execution-unit <unit> [--domain <domain>] [--team <team>] --format json|markdown";
    private static readonly string[] FactIds =
    [
        "design-claim-acquired", "bug-chain-or-ruling", "packet-current-files", "packet-current-validation", "guide-declaration",
        "queue-seed", "publication-artifact", "issue-published-run", "design-claim-release", "implementation-claim-acquired",
        "issue-completion-marker", "host-pr-linkage", "worker-completion-receipt", "recorded-review", "posted-review",
        "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged", "pr-merged-run",
        "closeout-recorded-run", "architect-knowledge-writeback", "orchestrator-knowledge-writeback", "guide-reachability",
        "implementation-claim-release",
    ];

    public static int Execute(CliContext context, string[] args, TextWriter writer) =>
        ExecuteCore(context, args, writer, new UnitStatusReadAdapter());

    internal static int ExecuteCore(CliContext context, string[] args, TextWriter writer, IUnitStatusSnapshotReader reader)
    {
        if (IsHelp(args)) { WriteHelp(writer); return 0; }
        if (!TryParse(args, out var unit, out var requestedDomain, out var requestedTeam, out var format, out var error))
            return Emit(writer, Empty(unit ?? "(unresolved)", "invalid-request", error, UnitStatusStates.InvalidRequest), format);
        return Emit(writer, Capture(context, unit!, requestedDomain, requestedTeam, reader), format);
    }

    internal static bool IsStatusCommand(string[] args) => args.Length >= 2 && args[0] == "unit" && args[1] == "status";
    internal static bool IsHelpRequest(string[] args) => IsStatusCommand(args) && args.Length == 3 && args[2] is "--help" or "help";

    internal static int ExecuteMetadataFree(string[] args, string cwd, TextWriter writer)
    {
        if (IsHelpRequest(args)) { WriteHelp(writer); return 0; }
        if (!TryParse(args[2..], out var unit, out var domain, out var team, out var format, out var error))
            return Emit(writer, Empty(unit ?? "(unresolved)", "invalid-request", error, UnitStatusStates.InvalidRequest), format);
        return Emit(writer, Empty(unit!, "host-context-unavailable",
            "No configured host context was found from '" + cwd + "'; bootstrap defaults were not used and no GitHub calls were made.",
            UnitStatusStates.ApplicabilityUnresolved, domain, team), format);
    }

    internal static int ExecuteHostRefusal(string[] args, string detail, TextWriter writer)
    {
        if (!TryParse(args[2..], out var unit, out var domain, out var team, out var format, out var error))
            return Emit(writer, Empty(unit ?? "(unresolved)", "invalid-request", error, UnitStatusStates.InvalidRequest), format);
        return Emit(writer, Empty(unit!, "host-config-unreadable", detail, UnitStatusStates.ReadFailure, domain, team), format);
    }

    private static UnitStatusEvidenceSnapshot Capture(CliContext context, string unit, string? askedDomain, string? askedTeam, IUnitStatusSnapshotReader reader)
    {
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(unit, out var unitError))
            return Empty(unit, "unit-invalid", unitError, UnitStatusStates.InvalidRequest);
        var observedAt = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath(context.RepoRoot);
        var packetDir = Path.GetFullPath(Path.Combine(root, ".intent-cli", "issues", unit));
        var packetRoot = Path.GetFullPath(Path.Combine(root, ".intent-cli", "issues"));
        if (!Contained(packetRoot, packetDir)) return Empty(unit, "unit-invalid", "Unit path escaped packet root.", UnitStatusStates.InvalidRequest);
        var packetYaml = ReadText(GuideReachabilityRecord.ResolvePacketPath(root, unit), out var packetError);
        PacketYamlDocument? packet = null;
        if (packetYaml is not null && !PacketYamlDocument.TryParse(packetYaml, out packet, out var parseError)) packetError = parseError;
        var declaredDomain = packet?.Fields.GetValueOrDefault("domain");
        var packetRepo = packet?.Fields.GetValueOrDefault("implementation_issue_packet.target_repo") ?? packet?.Fields.GetValueOrDefault("target_repo");

        var publishPath = Path.Combine(packetDir, "publish.yaml");
        IssuePublishArtifact? publish = null;
        string? publishError = null;
        var publishIdentityInvalid = false;
        if (File.Exists(publishPath) || Directory.Exists(publishPath))
        {
            try
            {
                publish = IssuePublishArtifactYaml.Deserialize(File.ReadAllText(publishPath));
                if (publish.ExecutionUnit != unit)
                {
                    publishIdentityInvalid = true;
                    throw new InvalidOperationException("Publish artifact execution unit mismatches the requested unit.");
                }
            }
            catch (Exception exception) when (IsReadException(exception)) { publishError = exception.Message; publish = null; }
        }

        var queueIdentity = ReadQueueIdentityCandidates(root, unit, out var queueScanError);
        var runIdentity = ReadRunIdentityCandidates(root, unit, out var runScanError);
        var domain = askedDomain?.Trim();
        if (domain is null && !string.IsNullOrWhiteSpace(declaredDomain)) domain = declaredDomain.Trim();
        var scopedDomains = queueIdentity.Select(candidate => candidate.Domain).Concat(runIdentity.Select(candidate => candidate.Domain))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        if (domain is null && scopedDomains.Length == 1) domain = scopedDomains[0];
        string? publishIssueRepo = null;
        int? publishIssueFromUrl = null;
        string? publishPrRepo = null;
        int? publishPrFromUrl = null;
        if (publish?.CreatedIssueUrl is { } issueUrl)
        {
            if (TryParseGithubUrl(issueUrl, "issues", out var parsedRepo, out var parsedIssue))
            { publishIssueRepo = parsedRepo; publishIssueFromUrl = parsedIssue; }
            else publishIdentityInvalid = true;
        }
        if (publish?.CreatedIssueNumber is > 0 and { } createdNumber && publishIssueFromUrl is { } fromUrl && createdNumber != fromUrl)
            publishIdentityInvalid = true;
        if (publish?.LinkedPrUrl is { } linkedPrUrl)
        {
            if (TryParseGithubUrl(linkedPrUrl, "pull", out var parsedRepo, out var parsedPr))
            { publishPrRepo = parsedRepo; publishPrFromUrl = parsedPr; }
            else publishIdentityInvalid = true;
        }
        if (publish?.LinkedPrNumber is > 0 and { } linkedPrNumber && publishPrFromUrl is { } linkedPrFromUrl && linkedPrNumber != linkedPrFromUrl)
            publishIdentityInvalid = true;
        var queueIdentityInvalid = queueIdentity.Any(candidate =>
            candidate.Item.LinkedIssue is { } linkedIssue && (!SafeRepo(linkedIssue.Repo) || linkedIssue.Number <= 0)
            || candidate.Item.LinkedPr is { } linkedPr && !TryParseGithubUrl(linkedPr, "pull", out _, out _));
        var repoCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(packetRepo)) repoCandidates.Add(packetRepo.Trim());
        if (publishIssueRepo is not null) repoCandidates.Add(publishIssueRepo);
        if (publishPrRepo is not null) repoCandidates.Add(publishPrRepo);
        repoCandidates.AddRange(queueIdentity.Select(candidate => candidate.Item.LinkedIssue?.Repo).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>());
        foreach (var candidate in queueIdentity)
            if (candidate.Item.LinkedPr is { } linked && TryParseGithubUrl(linked, "pull", out var linkedRepo, out _)) repoCandidates.Add(linkedRepo);
        repoCandidates.AddRange(runIdentity.SelectMany(candidate => candidate.Repos));
        var repoValues = repoCandidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var repo = repoValues.Length == 1 ? repoValues[0] : null;
        var issueCandidates = new List<int>();
        if (publish?.CreatedIssueNumber is > 0 and { } issueNumber) issueCandidates.Add(issueNumber);
        if (publishIssueFromUrl is { } urlIssueNumber) issueCandidates.Add(urlIssueNumber);
        issueCandidates.AddRange(queueIdentity.Select(candidate => candidate.Item.LinkedIssue?.Number).Where(value => value is > 0).Select(value => value!.Value));
        issueCandidates.AddRange(runIdentity.SelectMany(candidate => candidate.Issues));
        var issueValues = issueCandidates.Distinct().ToArray();
        var issue = issueValues.Length == 1 ? issueValues[0] : (int?)null;
        var prCandidates = new List<int>();
        if (publish?.LinkedPrNumber is > 0 and { } artifactPr) prCandidates.Add(artifactPr);
        if (publishPrFromUrl is { } urlPrNumber) prCandidates.Add(urlPrNumber);
        foreach (var candidate in queueIdentity)
            if (candidate.Item.LinkedPr is { } linked && TryParseGithubUrl(linked, "pull", out _, out var number)) prCandidates.Add(number);
        prCandidates.AddRange(runIdentity.SelectMany(candidate => candidate.PullRequests));
        var prValues = prCandidates.Distinct().ToArray();
        var pr = prValues.Length == 1 ? prValues[0] : (int?)null;

        var claims = reader.ReadClaimSnapshot(context, unit);
        var claimedTeams = claims.History.Select(item => item.Team).Append(claims.ActiveClaim?.Team)
            .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var claimedTeam = claims.ActiveClaim?.Team
            ?? (claimedTeams.Length == 1 ? claimedTeams[0] : null);
        var team = string.IsNullOrWhiteSpace(askedTeam) ? claimedTeam : askedTeam.Trim();
        var packetDomainInvalid = !string.IsNullOrWhiteSpace(declaredDomain) && !SafeScope(declaredDomain.Trim());
        var domainScopeInvalid = domain is not null && !SafeScope(domain) || scopedDomains.Any(candidate => !SafeScope(candidate));
        var teamScopeInvalid = team is not null && !SafeScope(team);
        var packetUnitInvalid = packet?.Fields.GetValueOrDefault("execution_unit") is { } packetUnitValue
            && !string.Equals(packetUnitValue, unit, StringComparison.Ordinal);
        var conflict = repoValues.Length > 1 || issueValues.Length > 1 || prValues.Length > 1 || scopedDomains.Length > 1
            || queueIdentity.Count > 1
            || runIdentity.Select(candidate => candidate.Path).Distinct(StringComparer.Ordinal).Count() > 1
            || runIdentity.Any(candidate => candidate.InvalidIdentity)
            || publishIdentityInvalid || packetUnitInvalid || queueIdentityInvalid
            || packetRepo is not null && !SafeRepo(packetRepo)
            || domainScopeInvalid || teamScopeInvalid
            || claims.ActiveClaim is null && claimedTeams.Length > 1
            || queueScanError is not null || runScanError is not null
            || domain is not null && scopedDomains.Any(candidate => candidate != domain)
            || askedDomain is not null && !string.IsNullOrWhiteSpace(declaredDomain) && askedDomain != declaredDomain
            || askedTeam is not null && !string.IsNullOrWhiteSpace(claimedTeam) && askedTeam != claimedTeam;

        var sources = new List<string>();
        if (packetYaml is not null) sources.Add("packet.yaml");
        if (publish is not null || publishError is not null) sources.Add("publish.yaml");
        if (queueIdentity.Count > 0) sources.Add("queue-state.json");
        if (runIdentity.Count > 0) sources.Add("runs.jsonl");
        if (claims.ActiveClaim is not null || claims.History.Count > 0) sources.Add("claim snapshot");
        var localSources = new List<string> { "packet", "publish artifact", "local claim snapshot" };
        var facts = new List<UnitStatusFact>();
        if (claims.State == UnitStatusStates.Unavailable) AddClaimFacts(facts, claims, team, unit);
        if (conflict)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable,
                queueScanError is not null || runScanError is not null ? "identity-candidates-unreadable"
                    : packetDomainInvalid ? "packet-domain-invalid"
                    : domainScopeInvalid ? "domain-scope-invalid"
                    : teamScopeInvalid ? "team-scope-invalid"
                    : "identity-conflict",
                queueScanError ?? runScanError ?? (packetDomainInvalid ? "Packet domain is not a safe path scope."
                    : domainScopeInvalid ? "A packet, queue, or run domain is not a safe path scope."
                    : teamScopeInvalid ? "Resolved team is not a safe path scope."
                    : "Packet, publish, queue, runs, claim, or explicit identity assertions disagree."),
                queueScanError is not null || runScanError is not null ? UnitStatusStates.ReadFailure : UnitStatusStates.IdentityConflict);
        if (claims.State != UnitStatusStates.Unavailable && team is not null) AddClaimFacts(facts, claims, team, unit);
        if (packetError is not null)
        {
            var pointer = EvidenceFile("packet", Path.Combine(".intent-cli", "issues", unit, "packet.yaml").Replace('\\', '/'), unit,
                "packet-identity-read-failure");
            facts.Add(Unavailable("packet-current-validation", "packet-identity-unreadable", packetError, UnitStatusStates.ReadFailure)
                with { Evidence = [pointer] });
            facts.Add(Unavailable("bug-chain-or-ruling", "packet-unreadable", packetError, UnitStatusStates.ReadFailure)
                with { Evidence = [pointer] });
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "packet-identity-unreadable", packetError, UnitStatusStates.ReadFailure);
        }
        if (publishError is not null)
        {
            var pointer = EvidenceFile("publish-artifact", Path.Combine(".intent-cli", "issues", unit, "publish.yaml").Replace('\\', '/'), unit,
                "required-publish-identity-read-failure");
            facts.Add(Unavailable("publication-artifact", "publication-artifact-unreadable", publishError, UnitStatusStates.ReadFailure)
                with { Evidence = [pointer] });
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "publication-artifact-unreadable", publishError, UnitStatusStates.ReadFailure);
        }
        if (domain is null)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "domain-unresolved", "Domain could not be derived from the packet or an explicit assertion.", UnitStatusStates.ApplicabilityUnresolved);
        if (team is null)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "team-unresolved", "Team could not be derived from claim identity or --team; no unique-mode fallback was used.", UnitStatusStates.ApplicabilityUnresolved);

        TeamModeEntry? mode;
        try
        {
            var state = TeamModeStore.TryRead(root);
            mode = state?.Entries.SingleOrDefault(entry => entry.Domain == domain && entry.Team == team);
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "team-mode-unreadable", exception.Message, UnitStatusStates.ApplicabilityUnresolved);
        }
        if (mode is null)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "team-mode-unrecorded", "No current recorded mode exists for this exact domain/team entry.", UnitStatusStates.ApplicabilityUnresolved);
        var packetUnit = packet?.Fields.GetValueOrDefault("execution_unit");
        var hasExactUnitSource = string.Equals(packetUnit, unit, StringComparison.Ordinal)
            || publish?.ExecutionUnit == unit
            || queueIdentity.Any(candidate => candidate.Item.ExecutionUnit == unit)
            || runIdentity.Count > 0
            || claims.ActiveClaim?.Scope == "execution-unit:" + unit
            || claims.History.Count > 0;
        if (!hasExactUnitSource)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "unit-identity-unestablished", "The requested unit is not established by a matching packet, queue, publish, run, or claim record.", UnitStatusStates.ApplicabilityUnresolved);
        var claimRefProvenanceOnly = claims.State == UnitStatusStates.Unavailable
            && claims.Cause == "local-claim-ref-unavailable"
            && claims.UnavailableClass == UnitStatusStates.ProvenanceLimit;
        if (claims.State == UnitStatusStates.Unavailable && !claimRefProvenanceOnly)
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "claim-snapshot-unavailable", claims.Detail ?? "The configured claim snapshot could not be read.", claims.UnavailableClass ?? UnitStatusStates.ReadFailure);
        if (claimRefProvenanceOnly
            && (string.IsNullOrWhiteSpace(askedDomain)
                || string.IsNullOrWhiteSpace(askedTeam)
                || !(string.Equals(packetUnit, unit, StringComparison.Ordinal) || publish?.ExecutionUnit == unit)))
        {
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
                UnitStatusStates.Unavailable, "unit-identity-unestablished",
                "A local claim reference is unconfigured and packet/publish identity with explicit --domain and --team is required to continue.",
                UnitStatusStates.ApplicabilityUnresolved);
        }
        if (mode.Mode != TeamMode.SoloConductor)
        {
            var na = FactIds.Select(id => NotApplicable(id, "recorded-non-solo-mode",
                "Recorded mode '" + mode.Mode + "' is not solo-conductor; optional sources and GitHub were skipped.")).ToArray();
            return Build(unit, domain, team, repo, issue, pr, observedAt, claims, na, sources, localSources,
                UnitStatusStates.NotApplicable, "recorded-non-solo-mode", "All solo-conductor phases are not applicable.", null, mode);
        }

        AddPacketFacts(facts, unit, root, packetDir, packetYaml, packetError, packet);
        AddQueueAndPublishFacts(facts, root, unit, domain, repo, issue, pr, publish, publishError,
            out var queueItem, out var queuePath, out var runEvents, out var runsPath, out var queueReadError, out var runsReadError);
        if (queueScanError is not null)
        {
            facts.RemoveAll(fact => fact.Id == "queue-seed");
            facts.Add(Unavailable("queue-seed", "queue-scan-unreadable", queueScanError, UnitStatusStates.ReadFailure));
        }
        if (queueReadError is not null || runsReadError is not null) localSources.Add("runtime state");
        UnitStatusRemoteSnapshot? remote = null;
        if (repo is not null && issue is > 0)
            remote = pr is > 0
                ? reader.ObserveGitHub(context, repo, issue.Value, pr.Value, unit, domain, team)
                : reader.ObserveGitHubIssue(context, repo, issue.Value, unit);
        var stableHead = remote is not null
            && remote.HeadBefore is not null
            && string.Equals(remote.HeadBefore, remote.HeadAfter, StringComparison.OrdinalIgnoreCase)
                ? remote.HeadSha
                : null;
        var localReviews = AddCloseoutFacts(facts, root, unit, repo, pr, domain, team, packetYaml,
            runEvents, runsPath, runsReadError, stableHead);
        facts.Add(new UnitStatusFact
        {
            Id = "worker-completion-receipt", State = UnitStatusStates.Unavailable,
            Cause = "worker-completion-receipt-not-recorded",
            Detail = "No dedicated durable worker-completion receipt exists; labels and PR linkage are separate observations.",
            UnavailableClass = UnitStatusStates.ProvenanceLimit,
            RepairUnavailableReason = "no-supported-historical-worker-completion-receipt-writer",
        });

        if (remote is not null)
        {
            facts.AddRange(remote.Facts);
            if (pr is null)
            {
                foreach (var id in new[] { "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged" })
                    facts.Add(Missing(id, "pr-not-linked", "No pull request is linked to this successfully resolved issue identity yet."));
            }
        }
        else if (pr is null && repo is not null && issue is > 0)
        {
            facts.Add(Unavailable("issue-completion-marker", "github-read-failed", "The issue labels could not be observed.", UnitStatusStates.ReadFailure));
            foreach (var id in new[] { "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged" })
                facts.Add(Missing(id, "pr-not-linked", "No pull request is linked to this successfully resolved issue identity yet."));
        }
        else if (repo is not null && issue is null && pr is null)
        {
            foreach (var id in new[] { "issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged", "recorded-review" })
                facts.RemoveAll(fact => fact.Id == id);
            foreach (var id in new[] { "issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged", "recorded-review" })
                facts.Add(Missing(id, "issue-not-published", "No issue has been published for this locally resolved unit identity."));
        }
        else
        {
            foreach (var id in new[] { "issue-completion-marker", "posted-review", "delta-review", "observed-ci", "approved-marker", "approval-head-receipt", "pr-merged" })
                facts.Add(Unavailable(id, "identity-unresolved", "A corroborated repo, issue, and PR identity is required for GitHub observation.", UnitStatusStates.IdentityConflict));
        }

        return Build(unit, domain, team, repo, issue, pr, observedAt, claims, facts, sources, localSources,
            UnitStatusStates.Done, "solo-conductor-applicable", "Current exact domain/team entry records solo-conductor mode.", null, mode, remote,
            localReviews.Concat(remote?.Reviews ?? []).ToArray());
    }

    private static IReadOnlyList<(QueueItem Item, string? Domain, string Path)> ReadQueueIdentityCandidates(string root, string unit, out string? failure)
    {
        var result = new List<(QueueItem, string?, string)>();
        var intentRoot = Path.Combine(root, ".intent-cli");
        var runtimeRoot = Path.Combine(intentRoot, RuntimeScopedStateResolver.RuntimeDirectoryName);
        var paths = new List<string>();
        try
        {
            if (Directory.Exists(runtimeRoot))
                paths.AddRange(Directory.EnumerateFiles(runtimeRoot, CliRuntimeContracts.QueueStateFileName, SearchOption.AllDirectories).Take(501));
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            failure = exception.Message;
            return [];
        }
        var overflow = paths.Count > 500;
        if (overflow) paths = paths.Take(500).ToList();
        var legacy = RuntimeScopedStateResolver.GetLegacyQueueStatePath(root);
        if (File.Exists(legacy)) paths.Add(legacy);
        failure = overflow ? "Queue identity scan exceeded the 500-file bound." : null;
        var legacyFullPath = Path.GetFullPath(legacy);
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var isLegacy = string.Equals(fullPath, legacyFullPath, PathComparison);
                if (!isLegacy && !Contained(runtimeRoot, fullPath))
                {
                    failure ??= "Queue identity scan found a path outside the runtime-state root.";
                    continue;
                }

                var state = QueueStateSerializer.Deserialize(File.ReadAllText(path));
                var relative = Path.GetRelativePath(runtimeRoot, path);
                var domain = relative.StartsWith("..", StringComparison.Ordinal) ? null : relative.Split(Path.DirectorySeparatorChar)[0];
                foreach (var item in state.Items.Where(item => item.ExecutionUnit == unit)) result.Add((item, domain, path));
            }
            catch (Exception exception) when (IsReadException(exception))
            {
                failure ??= "Queue file '" + Path.GetRelativePath(root, path) + "' could not be read: " + exception.Message;
            }
        }
        return result.Select(row => (row.Item1, row.Item2, row.Item3)).ToArray();
    }

    private static IReadOnlyList<RunIdentityCandidate> ReadRunIdentityCandidates(string root, string unit, out string? failure)
    {
        var result = new List<RunIdentityCandidate>();
        var runtimeRoot = Path.Combine(root, ".intent-cli", RuntimeScopedStateResolver.RuntimeDirectoryName);
        var paths = new List<string>();
        try
        {
            if (Directory.Exists(runtimeRoot))
                paths.AddRange(Directory.EnumerateFiles(runtimeRoot, CliRuntimeContracts.RunLogFileName, SearchOption.AllDirectories).Take(501));
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            failure = exception.Message;
            return [];
        }

        var overflow = paths.Count > 500;
        if (overflow) paths = paths.Take(500).ToList();
        var legacy = RuntimeScopedStateResolver.GetLegacyRunLogPath(root);
        if (File.Exists(legacy)) paths.Add(legacy);
        failure = overflow ? "Run-log identity scan exceeded the 500-file bound." : null;
        var legacyFullPath = Path.GetFullPath(legacy);
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var isLegacy = string.Equals(fullPath, legacyFullPath, PathComparison);
                if (!isLegacy && !Contained(runtimeRoot, fullPath))
                {
                    failure ??= "Run-log identity scan found a path outside the runtime-state root.";
                    continue;
                }

                var relative = Path.GetRelativePath(runtimeRoot, path);
                var domain = relative.StartsWith("..", StringComparison.Ordinal) ? null : relative.Split(Path.DirectorySeparatorChar)[0];
                foreach (var run in RunLogSerializer.DeserializeAll(File.ReadAllText(path)).Where(item => item.ExecutionUnit == unit))
                {
                    var repos = new List<string>();
                    var issues = new List<int>();
                    var prs = new List<int>();
                    var invalid = false;
                    if (!string.IsNullOrWhiteSpace(run.Repo))
                    {
                        if (SafeRepo(run.Repo)) repos.Add(run.Repo);
                        else invalid = true;
                    }
                    if (run.Pr is > 0) prs.Add(run.Pr.Value);
                    if (!string.IsNullOrWhiteSpace(run.LinkedIssue))
                    {
                        if (TryParseGithubUrl(run.LinkedIssue, "issues", out var linkedRepo, out var linkedIssue))
                        { repos.Add(linkedRepo); issues.Add(linkedIssue); }
                        else invalid = true;
                    }
                    if (!string.IsNullOrWhiteSpace(run.LinkedPr))
                    {
                        if (TryParseGithubUrl(run.LinkedPr, "pull", out var linkedRepo, out var linkedPr))
                        { repos.Add(linkedRepo); prs.Add(linkedPr); }
                        else invalid = true;
                    }
                    result.Add(new RunIdentityCandidate(
                        run,
                        domain,
                        path,
                        repos.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        issues.Distinct().ToArray(),
                        prs.Distinct().ToArray(),
                        invalid));
                }
            }
            catch (Exception exception) when (IsReadException(exception))
            {
                failure ??= "Run log '" + Path.GetRelativePath(root, path) + "' could not be read: " + exception.Message;
            }
        }
        return result;
    }

    private sealed record RunIdentityCandidate(
        RunEvent Event,
        string? Domain,
        string Path,
        IReadOnlyList<string> Repos,
        IReadOnlyList<int> Issues,
        IReadOnlyList<int> PullRequests,
        bool InvalidIdentity);

    private static void AddPacketFacts(ICollection<UnitStatusFact> facts, string unit, string root, string directory, string? yaml, string? readError, PacketYamlDocument? packet)
    {
        var packetPath = Path.Combine(".intent-cli", "issues", unit, "packet.yaml").Replace('\\', '/');
        var implementationPath = Path.Combine(directory, "implementation.md");
        var reviewContextPath = Path.Combine(directory, "review-context.md");
        var bodyPath = Path.Combine(directory, "github-body.md");
        var implementation = ReadText(implementationPath, out var implementationError);
        var reviewContext = ReadText(reviewContextPath, out var reviewContextError);
        var body = ReadText(bodyPath, out var bodyError);
        var unreadable = new[]
            {
                readError is null ? null : "packet.yaml",
                implementationError is null ? null : "implementation.md",
                reviewContextError is null ? null : "review-context.md",
                bodyError is null ? null : "github-body.md",
            }
            .Where(name => name is not null).Cast<string>().ToArray();
        var missing = new[]
            {
                yaml is null && readError is null ? "packet.yaml" : null,
                implementation is null && implementationError is null ? "implementation.md" : null,
                reviewContext is null && reviewContextError is null ? "review-context.md" : null,
                body is null && bodyError is null ? "github-body.md" : null,
            }
            .Where(name => name is not null).Cast<string>().ToArray();
        var packetEvidence = new[]
            {
                EvidenceFile("packet", packetPath, unit, "current-packet-file"),
                implementation is null ? null : EvidenceFile("packet", PacketRelativePath(unit, "implementation.md"), unit, "current-packet-file"),
                reviewContext is null ? null : EvidenceFile("packet", PacketRelativePath(unit, "review-context.md"), unit, "current-packet-file"),
                body is null ? null : EvidenceFile("packet", PacketRelativePath(unit, "github-body.md"), unit, "current-packet-file"),
            }
            .Where(item => item is not null).Cast<UnitStatusEvidencePointer>().ToArray();
        facts.Add(unreadable.Length > 0
            ? Unavailable("packet-current-files", "packet-file-unreadable", "Canonical packet files could not be read: " + string.Join(", ", unreadable), UnitStatusStates.ReadFailure) with { Evidence = packetEvidence }
            : missing.Length == 0
                ? Done("packet-current-files", "packet-current-files-present", "All four canonical packet files are present.") with { Evidence = packetEvidence }
                : Missing("packet-current-files", "packet-file-missing", "Missing canonical packet files: " + string.Join(", ", missing)) with { Evidence = packetEvidence });
        if (unreadable.Length > 0)
        {
            facts.Add(Unavailable("packet-current-validation", "packet-file-unreadable", "Current packet validation could not read: " + string.Join(", ", unreadable), UnitStatusStates.ReadFailure)
                with { Evidence = packetEvidence });
            facts.Add(Unavailable("guide-declaration", "guide-declaration-unreadable", "Current packet declaration cannot be read.", UnitStatusStates.ReadFailure)
                with { Evidence = [EvidenceFile("packet", packetPath, unit, "current-packet-file")] });
            AddSourceArtifactFact(facts, root, unit, packetPath, packet);
            return;
        }
        if (yaml is null)
        {
            facts.Add(readError is null ? Missing("packet-current-validation", "packet-yaml-missing", "Current packet validation needs packet.yaml.")
                : Unavailable("packet-current-validation", "packet-yaml-unreadable", readError, UnitStatusStates.ReadFailure));
            facts.Add(Unavailable("guide-declaration", "guide-declaration-unreadable", "Current packet declaration cannot be read.", UnitStatusStates.ReadFailure));
            facts.Add(readError is null
                ? Missing("bug-chain-or-ruling", "source-links-not-recorded", "No packet was present to declare a source or ruling reference.")
                : Unavailable("bug-chain-or-ruling", "packet-unreadable", readError, UnitStatusStates.ReadFailure));
            return;
        }
        var validation = PreparedPacketCommitReadyAnalyzer.Analyze(new PreparedPacketCommitReadyInput
        {
            ExecutionUnit = unit, PacketYaml = yaml,
            ImplementationMarkdown = implementation,
            ReviewContextMarkdown = reviewContext,
            GithubBodyMarkdown = body,
            RequestedTargetRepo = packet?.Fields.GetValueOrDefault("implementation_issue_packet.target_repo") ?? packet?.Fields.GetValueOrDefault("target_repo"),
        });
        facts.Add(validation.Classification == PreparedPacketCommitReadyAnalyzer.ClassificationCommitReady
            ? Done("packet-current-validation", "packet-current-validation-passed", "Existing readiness analysis was applied to current packet contents, not historical command execution.")
                with { Evidence = [EvidenceFile("packet", packetPath, unit, "current-packet-revalidation-not-historical-proof")] }
            : validation.RefusalReasons.Contains(PreparedPacketCommitReadyAnalyzer.ReasonPacketYamlUnparseable, StringComparer.Ordinal)
                ? Unavailable("packet-current-validation", validation.Reason ?? "packet-yaml-unparseable", validation.Summary, UnitStatusStates.ReadFailure)
                    with { Evidence = [EvidenceFile("packet", packetPath, unit, "current-packet-revalidation-not-historical-proof")] }
                : Missing("packet-current-validation", validation.Reason ?? "packet-current-validation-failed", validation.Summary)
                    with { Evidence = [EvidenceFile("packet", packetPath, unit, "current-packet-revalidation-not-historical-proof")] });
        try
        {
            var declaration = GuideReachabilityDeclaration.Read(yaml);
            var evidence = new[] { EvidenceFile("packet", packetPath, unit, "packet-guide-reachability-declaration") };
            facts.Add(!declaration.IsDeclared ? Missing("guide-declaration", "guide-declaration-absent", "Packet has no guide_reachability declaration.") with { Evidence = evidence }
                : declaration.NoRoleFacingSurface ? NotApplicable("guide-declaration", "explicit-no-role-facing-surface", "Packet explicitly declares no role-facing surface.") with { Evidence = evidence }
                : Done("guide-declaration", "guide-declaration-present", "Packet declares role-facing guide routes.") with { Evidence = evidence });
        }
        catch (Exception exception) when (IsReadException(exception))
        { facts.Add(Unavailable("guide-declaration", "guide-declaration-unreadable", exception.Message, UnitStatusStates.ReadFailure)); }
        AddSourceArtifactFact(facts, root, unit, packetPath, packet);
    }

    private static void AddSourceArtifactFact(ICollection<UnitStatusFact> facts, string root, string unit,
        string packetPath, PacketYamlDocument? packet)
    {
        if (packet is null)
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "packet-unparseable", "The packet source declaration could not be read from an unparseable packet.", UnitStatusStates.ReadFailure));
            return;
        }
        var sourceReference = packet?.Fields.GetValueOrDefault("implementation_issue_packet.source_artifact")
            ?? packet?.Fields.GetValueOrDefault("source_artifact");
        var rulingReference = packet?.Fields.GetValueOrDefault("implementation_issue_packet.ruling_artifact")
            ?? packet?.Fields.GetValueOrDefault("ruling_artifact");
        if (string.IsNullOrWhiteSpace(sourceReference) && string.IsNullOrWhiteSpace(rulingReference))
        {
            facts.Add(Missing("bug-chain-or-ruling", "source-links-not-recorded", "A successfully read packet has no explicit supported source or ruling reference."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(rulingReference))
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "ruling-reference-unsupported",
                "The packet declares a ruling artifact, but this status reader has no canonical read-only ruling-artifact parser.", UnitStatusStates.ProvenanceLimit)
                with { RepairUnavailableReason = "no-supported-ruling-artifact-reader" });
            return;
        }

        var source = sourceReference!.Trim();
        string? explicitReportPath = null;
        string? sourceUrl = null;
        if (TryParseGithubUrl(source, "issues", out var sourceRepo, out var sourceIssue))
        {
            sourceUrl = $"https://github.com/{sourceRepo}/issues/{sourceIssue}";
        }
        else if (source.StartsWith(".intent-cli/bugs/", StringComparison.Ordinal)
            && source.EndsWith(".report.yaml", StringComparison.Ordinal)
            && !source.Contains("..", StringComparison.Ordinal)
            && !source.Contains('\\'))
        {
            var absolute = Path.GetFullPath(Path.Combine(root, source.Replace('/', Path.DirectorySeparatorChar)));
            var bugsRoot = Path.GetFullPath(Path.Combine(root, ".intent-cli", "bugs"));
            if (!Contained(bugsRoot, absolute) || !string.Equals(Path.GetDirectoryName(absolute), bugsRoot, PathComparison))
            {
                facts.Add(Unavailable("bug-chain-or-ruling", "source-reference-identity-conflict", "The packet source report path escapes the canonical bug-artifact directory.", UnitStatusStates.IdentityConflict));
                return;
            }
            explicitReportPath = absolute;
        }
        else
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-reference-unsupported",
                "The packet source reference is freeform or uses an unsupported source format.", UnitStatusStates.ProvenanceLimit)
                with { RepairUnavailableReason = "no-supported-source-reference-reader" });
            return;
        }

        var bugsRootPath = Path.GetFullPath(Path.Combine(root, ".intent-cli", "bugs"));
        if (explicitReportPath is null && !Directory.Exists(bugsRootPath))
        {
            facts.Add(Missing("bug-chain-or-ruling", "source-chain-not-recorded", "No canonical source bug report exists for the explicit source reference."));
            return;
        }

        string[] reportPaths;
        if (explicitReportPath is not null)
        {
            // An explicit local path asserts a concrete artifact. Missing or
            // unreadable evidence is a read failure, unlike a URL inventory
            // that was read successfully and contained no match.
            reportPaths = [explicitReportPath];
        }
        else try
        {
            reportPaths = Directory.EnumerateFiles(bugsRootPath, "*.report.yaml", SearchOption.TopDirectoryOnly)
                .Take(501)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-inventory-unreadable", exception.Message, UnitStatusStates.ReadFailure));
            return;
        }
        if (reportPaths.Length > 500)
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-inventory-truncated", "Canonical bug report inventory exceeded the 500-file bound.", UnitStatusStates.ReadFailure));
            return;
        }

        var candidates = new List<(BugReportArtifact Report, UnitStatusEvidencePointer Evidence)>();
        var conflictingSourceEvidence = new List<UnitStatusEvidencePointer>();
        foreach (var path in reportPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!Contained(bugsRootPath, fullPath) || !string.Equals(Path.GetDirectoryName(fullPath), bugsRootPath, PathComparison))
            {
                facts.Add(Unavailable("bug-chain-or-ruling", "source-inventory-identity-conflict", "Canonical bug report inventory contains a path outside its root.", UnitStatusStates.IdentityConflict));
                return;
            }

            var yaml = ReadText(fullPath, out var readError);
            if (yaml is null)
            {
                facts.Add(Unavailable("bug-chain-or-ruling", "source-report-unreadable", readError ?? "A canonical bug report disappeared during observation.", UnitStatusStates.ReadFailure));
                return;
            }

            BugReportArtifact report;
            try { report = BugReportArtifactYaml.Deserialize(yaml); }
            catch (Exception exception) when (IsReadException(exception))
            {
                facts.Add(Unavailable("bug-chain-or-ruling", "source-report-unreadable", exception.Message, UnitStatusStates.ReadFailure));
                return;
            }
            var canonicalReportRef = BugReportArtifactPathResolver.Resolve(report.BugId);
            var canonicalReportPath = Path.GetFullPath(Path.Combine(root, canonicalReportRef.Replace('/', Path.DirectorySeparatorChar)));
            if (!string.Equals(canonicalReportPath, fullPath, PathComparison))
            {
                facts.Add(Unavailable("bug-chain-or-ruling", "source-report-identity-conflict", "A report artifact filename disagrees with its embedded bug id.", UnitStatusStates.IdentityConflict));
                return;
            }
            var linkedIssueMatches = sourceUrl is not null && report.LinkedIssueRefs.Any(reference =>
                TryParseGithubUrl(reference, "issues", out var linkedRepo, out var linkedIssue)
                    && string.Equals(linkedRepo, sourceRepo, StringComparison.OrdinalIgnoreCase)
                    && linkedIssue == sourceIssue);
            var foreignRepoIssueMatches = sourceUrl is not null
                && report.LinkedExecutionUnits.Contains(unit, StringComparer.Ordinal)
                && report.LinkedIssueRefs.Any(reference =>
                    TryParseGithubUrl(reference, "issues", out var linkedRepo, out var linkedIssue)
                        && linkedIssue == sourceIssue
                        && !string.Equals(linkedRepo, sourceRepo, StringComparison.OrdinalIgnoreCase));
            var directPathMatches = explicitReportPath is not null && string.Equals(fullPath, explicitReportPath, PathComparison);
            var pointer = new UnitStatusEvidencePointer
            {
                Kind = "bug-report-artifact",
                Path = Path.GetRelativePath(root, fullPath).Replace('\\', '/'),
                RecordId = report.BugId,
                Url = sourceUrl,
                ExecutionUnit = unit,
                Provenance = "canonical-local-bug-report-linked-by-explicit-packet-source-reference",
            };
            if (foreignRepoIssueMatches)
                conflictingSourceEvidence.Add(pointer);
            if (linkedIssueMatches || directPathMatches)
                candidates.Add((report, pointer));
        }

        if (conflictingSourceEvidence.Count > 0)
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-chain-identity-conflict",
                "A canonical report linked to this unit points at the packet's issue number in a different repository.", UnitStatusStates.IdentityConflict)
                with { Evidence = conflictingSourceEvidence });
            return;
        }

        if (candidates.Count == 0)
        {
            facts.Add(Missing("bug-chain-or-ruling", "source-chain-not-recorded", "No canonical bug report is linked to the packet's explicit source reference."));
            return;
        }
        if (candidates.Count != 1)
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-chain-identity-conflict", "Multiple canonical bug reports match the explicit packet source reference.", UnitStatusStates.IdentityConflict)
                with { Evidence = candidates.Select(candidate => candidate.Evidence).ToArray() });
            return;
        }

        var selected = candidates[0];
        var triageRef = BugTriageArtifactPathResolver.Resolve(selected.Report.BugId);
        var planRef = BugExecutionArtifactPathResolver.Resolve(selected.Report.BugId);
        var triagePath = Path.GetFullPath(Path.Combine(root, triageRef.Replace('/', Path.DirectorySeparatorChar)));
        var planPath = Path.GetFullPath(Path.Combine(root, planRef.Replace('/', Path.DirectorySeparatorChar)));
        if (!Contained(root, triagePath) || !Contained(root, planPath))
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-chain-path-conflict", "Canonical triage or plan path escapes the repository root.", UnitStatusStates.IdentityConflict));
            return;
        }
        if (!File.Exists(triagePath) || !File.Exists(planPath))
        {
            facts.Add(Missing("bug-chain-or-ruling", "source-triage-plan-not-recorded", "The source report has no complete canonical triage and plan chain.")
                with { Evidence = [selected.Evidence] });
            return;
        }

        BugTriageArtifact triage;
        BugExecutionArtifact plan;
        try
        {
            triage = BugTriageArtifactYaml.Deserialize(File.ReadAllText(triagePath));
            plan = BugExecutionArtifactYaml.Deserialize(File.ReadAllText(planPath));
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-triage-plan-unreadable", exception.Message, UnitStatusStates.ReadFailure)
                with { Evidence = [selected.Evidence] });
            return;
        }

        var packetRelative = Path.GetRelativePath(root, packetPath).Replace('\\', '/');
        var reportHasContradictoryUnits = selected.Report.LinkedExecutionUnits.Count > 0
            && !selected.Report.LinkedExecutionUnits.Contains(unit, StringComparer.Ordinal);
        var triageHasContradictoryUnits = triage.ResolvedExecutionUnits.Count > 0
            && !triage.ResolvedExecutionUnits.Contains(unit, StringComparer.Ordinal);
        var triageHasContradictoryPackets = triage.ResolvedPacketRefs.Count > 0
            && !triage.ResolvedPacketRefs.Contains(packetRelative, StringComparer.Ordinal);
        var planHasContradictoryPackets = plan.ResolvedPacketRefs.Count > 0
            && !plan.ResolvedPacketRefs.Contains(packetRelative, StringComparer.Ordinal);
        if (reportHasContradictoryUnits
            || triage.BugId != selected.Report.BugId || triage.ReportRef != BugReportArtifactPathResolver.Resolve(selected.Report.BugId)
            || triageHasContradictoryUnits || triageHasContradictoryPackets
            || plan.BugId != selected.Report.BugId || plan.ReportRef != BugReportArtifactPathResolver.Resolve(selected.Report.BugId)
            || plan.TriageRef != triageRef || planHasContradictoryPackets)
        {
            facts.Add(Unavailable("bug-chain-or-ruling", "source-chain-identity-conflict", "Canonical report, triage, plan, and packet references do not agree on bug and execution-unit identity.", UnitStatusStates.IdentityConflict)
                with { Evidence = [selected.Evidence] });
            return;
        }

        var chainEvidence = new[]
        {
            selected.Evidence,
            new UnitStatusEvidencePointer { Kind = "bug-triage-artifact", Path = triageRef, RecordId = triage.BugId, ExecutionUnit = unit, Provenance = "canonical-local-bug-triage-artifact" },
            new UnitStatusEvidencePointer { Kind = "bug-plan-artifact", Path = planRef, RecordId = plan.BugId, ExecutionUnit = unit, Provenance = "canonical-local-bug-plan-artifact" },
            EvidenceFile("packet", packetPath, unit, "packet-source-artifact-declaration"),
        };
        facts.Add(Done("bug-chain-or-ruling", "canonical-source-chain-recorded", "The packet's explicit source issue resolves to one identity-matched canonical report, triage, and plan chain.")
            with { Evidence = chainEvidence });
    }

    private static bool HasExplicitKnowledgeWriteBackDeclaration(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) return false;
            if (root.Children.TryGetValue(new YamlScalarNode("closeout_learning"), out var closeoutNode)
                && closeoutNode is YamlMappingNode closeout
                && closeout.Children.ContainsKey(new YamlScalarNode("write_back_required"))) return true;
            if (!root.Children.TryGetValue(new YamlScalarNode("knowledge_updates"), out var updatesNode)
                || updatesNode is not YamlMappingNode updates) return false;
            return updates.Children.Values.OfType<YamlMappingNode>()
                .Any(facet => facet.Children.ContainsKey(new YamlScalarNode("required")));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return false;
        }
    }

    private static void AddQueueAndPublishFacts(ICollection<UnitStatusFact> facts, string root, string unit, string domain, string? repo, int? issue, int? pr,
        IssuePublishArtifact? publish, string? publishError, out QueueItem? queueItem, out string queuePath,
        out IReadOnlyList<RunEvent> runs, out string runsPath, out string? queueError, out string? runsError)
    {
        queueItem = null; runs = []; queueError = null; runsError = null;
        queuePath = repo is null ? RuntimeScopedStateResolver.GetLegacyQueueStatePath(root)
            : RuntimeScopedStateResolver.ResolveQueueStatePathForRead(root, domain, repo).Path;
        if (File.Exists(queuePath) || Directory.Exists(queuePath))
        {
            try
            {
                var queue = QueueStateSerializer.Deserialize(File.ReadAllText(queuePath));
                var matches = queue.Items.Where(item => item.ExecutionUnit == unit).ToArray();
                if (matches.Length > 1) queueError = "Queue state contains duplicate execution-unit entries.";
                else queueItem = matches.SingleOrDefault();
            }
            catch (Exception exception) when (IsReadException(exception)) { queueError = exception.Message; }
        }
        var queueEvidence = LocalFileEvidence(root, queuePath, "queue-state", unit, "queue-state-read");
        facts.Add(queueError is not null
            ? Unavailable("queue-seed", "queue-state-unreadable", queueError, UnitStatusStates.ReadFailure) with { Evidence = queueEvidence is null ? [] : [queueEvidence] }
            : queueItem is null
                ? Missing("queue-seed", "queue-item-absent", "No queue item matches this execution unit.") with { Evidence = queueEvidence is null ? [] : [queueEvidence] }
                : Done("queue-seed", "queue-item-present", "A matching queue item was read.") with { Evidence = queueEvidence is null ? [] : [queueEvidence with { RecordId = unit }] });
        var publishPath = Path.Combine(root, ".intent-cli", "issues", unit, "publish.yaml");
        var publishEvidence = LocalFileEvidence(root, publishPath, "publish-artifact", unit, "issue-publish-artifact");
        UnitStatusEvidencePointer[] publishPointers = publishEvidence is null
            ? []
            : [publishEvidence with { RecordId = publish?.CreatedIssueNumber?.ToString(CultureInfo.InvariantCulture) }];
        var issueUrlIsExact = publish?.CreatedIssueNumber is > 0
            && publish.CreatedIssueUrl is { } createdUrl
            && repo is not null
            && issue is > 0
            && TryParseGithubUrl(createdUrl, "issues", out var createdRepo, out var createdIssue)
            && string.Equals(createdRepo, repo, StringComparison.OrdinalIgnoreCase)
            && createdIssue == issue;
        var lifecycleContradictsStatus = publish?.LifecycleState is { } lifecycle
            && (publish.PublishStatus == "published" && IssuePublishLifecycle.Rank(lifecycle) < IssuePublishLifecycle.Rank(IssuePublishLifecycle.Published)
                || publish.PublishStatus == "issue-created" && IssuePublishLifecycle.Rank(lifecycle) > IssuePublishLifecycle.Rank(IssuePublishLifecycle.IssueCreated)
                || publish.PublishStatus == "drafted" && (publish.CreatedIssueNumber is not null || publish.CreatedIssueUrl is not null));
        var publicationFact = publishError is not null
            ? Unavailable("publication-artifact", "publication-artifact-unreadable", publishError, UnitStatusStates.ReadFailure)
            : publish is null
                ? Missing("publication-artifact", "publication-artifact-absent", "No publish artifact exists for this unit.")
            : lifecycleContradictsStatus || publish.PublishStatus is not ("drafted" or "issue-created" or "published")
                ? Unavailable("publication-artifact", "publication-status-identity-conflict", "Publish status and lifecycle identity are contradictory or unsupported.", UnitStatusStates.IdentityConflict)
            : publish.PublishStatus == "published" && issueUrlIsExact
                ? Done("publication-artifact", "publication-issue-published", "The publish artifact records a published issue with matching repository and issue identity.")
            : publish.PublishStatus == "published"
                ? Unavailable("publication-artifact", "publication-identity-unavailable", "Published status lacks a valid created issue URL and number matching the resolved repository and issue.", UnitStatusStates.IdentityConflict)
            : Missing("publication-artifact", "publication-not-published", "Publish artifact exists, but its status does not record a published issue.");
        facts.Add(publicationFact with { Evidence = publishPointers });
        runsPath = repo is null ? RuntimeScopedStateResolver.GetLegacyRunLogPath(root) : RuntimeScopedStateResolver.ResolveRunLogPathForRead(root, domain, repo).Path;
        if (File.Exists(runsPath) || Directory.Exists(runsPath))
        {
            try { runs = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath)).Where(item => item.ExecutionUnit == unit).ToArray(); }
            catch (Exception exception) when (IsReadException(exception)) { runsError = exception.Message; }
        }
        var runsEvidence = LocalFileEvidence(root, runsPath, "runs-jsonl", unit, "runs-log-read");
        var issueEvents = runs.Where(item => item.Event == "issue-published").ToArray();
        var matchingIssueEvent = issue is > 0 && repo is not null
            ? issueEvents.FirstOrDefault(item => MatchesPublishedIssue(item, repo, issue.Value))
            : null;
        var issueFact = runsError is not null
            ? Unavailable("issue-published-run", "runs-unreadable", runsError, UnitStatusStates.ReadFailure)
            : issueEvents.Length == 0
                ? Missing("issue-published-run", "issue-published-run-absent", "No matching issue-published run event exists.")
                : matchingIssueEvent is not null
                    ? Done("issue-published-run", "issue-published-run-recorded", "A matching issue-published run event exists.")
                    : issueEvents.Any(item => string.IsNullOrWhiteSpace(item.LinkedIssue))
                        ? Unavailable("issue-published-run", "run-identity-unrecorded", "An issue-published event lacks the issue URL needed for exact identity.", UnitStatusStates.ProvenanceLimit)
                        : Unavailable("issue-published-run", "run-identity-conflict", "Issue-published event identity disagrees with the resolved issue/repository.", UnitStatusStates.IdentityConflict);
        if (runsEvidence is not null)
        {
            issueFact = issueFact with
            {
                Evidence = issueEvents.Length > 0
                    ? issueEvents.Select(item => runsEvidence with { RecordId = item.Event, RecordedAt = item.Ts, Repo = item.Repo }).ToArray()
                    : [runsEvidence],
            };
        }
        facts.Add(issueFact);
        var queuePrLinked = queueItem?.LinkedPr is { } queuePr
            && repo is not null
            && pr is > 0
            && TryParseGithubUrl(queuePr, "pull", out var queuePrRepo, out var queuePrNumber)
            && string.Equals(queuePrRepo, repo, StringComparison.OrdinalIgnoreCase)
            && queuePrNumber == pr;
        var linkEvidence = new List<UnitStatusEvidencePointer>();
        if (queuePrLinked && queueEvidence is not null && queueItem?.LinkedPr is not null) linkEvidence.Add(queueEvidence with { RecordId = unit, Url = queueItem.LinkedPr });
        var possibleLinkEvidence = new[] { publishEvidence, queueEvidence }.OfType<UnitStatusEvidencePointer>().ToArray();
        facts.Add(queuePrLinked
            ? Done("host-pr-linkage", "host-pr-linkage-present", "Queue state links this unit to the resolved repository, issue, and PR.") with { Evidence = linkEvidence }
            : Missing("host-pr-linkage", "queue-pr-link-absent", "No matching queue linked_pr record was read.")
                with { Evidence = possibleLinkEvidence });
    }

    private static void AddClaimFacts(ICollection<UnitStatusFact> facts, UnitStatusClaimSnapshot claims, string? team, string unit)
    {
        UnitStatusEvidencePointer SnapshotEvidence() => new()
        {
            Kind = "local-claim-snapshot",
            Path = null,
            RecordId = claims.MetadataOid,
            ExecutionUnit = unit,
            ClaimTeam = team,
            Provenance = (claims.Cause == "local-claim-ref-unavailable" && claims.MetadataRef is null
                ? "local-claim-snapshot-ref-unconfigured:scope=execution-unit:" + unit + "; "
                : "configured-local-claim-snapshot:scope=execution-unit:" + unit + "; ")
                + "ref=" + (claims.MetadataRef ?? "(unresolved)") + "; oid=" + (claims.MetadataOid ?? "(unresolved)"),
        };

        if (claims.State == UnitStatusStates.Unavailable)
        {
            foreach (var id in new[] { "design-claim-acquired", "design-claim-release", "implementation-claim-acquired", "implementation-claim-release" })
                facts.Add(Unavailable(id, claims.Cause ?? "claim-snapshot-unavailable", claims.Detail ?? "Configured local claim ref is unavailable.", claims.UnavailableClass ?? UnitStatusStates.ReadFailure)
                    with { Evidence = [SnapshotEvidence()] });
            return;
        }
        var rows = claims.History.OrderBy(row => row.RecordedAt).ThenBy(row => row.Path, StringComparer.Ordinal).ToArray();
        bool Role(string? actor, string role) => LogicalRoleNormalizer.TryNormalize(actor, out var normalized, out _) && normalized == role;
        string Disposition(string operation, string role) => operation == "takeover"
            ? "takeover"
            : role == LogicalRoleNormalizer.Architect ? "design-handoff" : "implementation-release";
        UnitStatusEvidencePointer ClaimEvidence(UnitStatusClaimRecordFact row, string role, DateTimeOffset? epochClaimedAt = null) => new()
        {
            Kind = "claim-record",
            Path = row.Path,
            RecordId = row.Operation ?? "active-claim",
            Role = role,
            ExecutionUnit = unit,
            RecordedAt = row.RecordedAt ?? row.ClaimedAt,
            ClaimEpochClaimedAt = epochClaimedAt ?? row.DisplacedClaimedAt ?? row.ClaimedAt,
            ClaimOperation = row.Operation ?? "acquire",
            ClaimDisposition = row.Operation is null ? "active" : Disposition(row.Operation, role),
            ClaimActor = row.Actor,
            ClaimTeam = row.Team,
            DisplacedClaimedAt = row.DisplacedClaimedAt,
            Provenance = "configured-local-claim-snapshot:" + (claims.MetadataRef ?? "(unresolved)")
                + "@" + (claims.MetadataOid ?? "(unresolved)"),
        };

        void AddRoleFacts(string role, string acquiredId, string releaseId)
        {
            var active = claims.ActiveClaim is { } current && current.Team == team && Role(current.Actor, role) ? current : null;
            var acquisitionEpochs = new List<(DateTimeOffset At, UnitStatusEvidencePointer Evidence, bool Active)>();
            var releaseEpochs = new List<(DateTimeOffset At, UnitStatusEvidencePointer Evidence)>();
            var invalidDetail = (string?)null;
            if (active is not null)
            {
                if (active.ClaimedAt is null || active.ClaimedAt == default)
                    invalidDetail = "Active claim has no usable claimed_at value for its epoch.";
                else
                    acquisitionEpochs.Add((active.ClaimedAt.Value, ClaimEvidence(active, role, active.ClaimedAt), true));
            }

            foreach (var row in rows)
            {
                var actorMatches = Role(row.Actor, role) && row.Team == team;
                var displacedMatches = Role(row.DisplacedHolder, role) && row.DisplacedTeam == team;
                if (row.Operation == "release" && (actorMatches || displacedMatches))
                {
                    if (!actorMatches || !displacedMatches || row.DisplacedClaimedAt is null || row.DisplacedClaimedAt == default)
                    {
                        invalidDetail = "Release history has inconsistent holder/team/claim-epoch identity.";
                        continue;
                    }

                    var pointer = ClaimEvidence(row, role, row.DisplacedClaimedAt);
                    acquisitionEpochs.Add((row.DisplacedClaimedAt.Value, pointer, false));
                    releaseEpochs.Add((row.DisplacedClaimedAt.Value, pointer));
                }
                else if (row.Operation == "takeover")
                {
                    if (actorMatches)
                    {
                        if (row.RecordedAt is null || row.RecordedAt == default)
                            invalidDetail = "Takeover history has no usable recorded_at value for the incoming claim epoch.";
                        else
                            acquisitionEpochs.Add((row.RecordedAt.Value,
                                ClaimEvidence(row, role, row.RecordedAt) with { Provenance = "configured-local-claim-snapshot:takeover-recorded-at:" + (claims.MetadataRef ?? "(unresolved)") + "@" + (claims.MetadataOid ?? "(unresolved)") },
                                false));
                    }

                    if (displacedMatches)
                    {
                        if (row.DisplacedClaimedAt is null || row.DisplacedClaimedAt == default)
                        {
                            invalidDetail = "Takeover history has no usable displaced_claimed_at value for the outgoing claim epoch.";
                            continue;
                        }

                        var pointer = ClaimEvidence(row, role, row.DisplacedClaimedAt);
                        acquisitionEpochs.Add((row.DisplacedClaimedAt.Value, pointer, false));
                        releaseEpochs.Add((row.DisplacedClaimedAt.Value, pointer));
                    }
                }
            }

            var acquiredEvidence = acquisitionEpochs.Select(item => item.Evidence).ToArray();
            if (invalidDetail is not null)
            {
                facts.Add(Unavailable(acquiredId, "claim-epoch-identity-conflict", invalidDetail, UnitStatusStates.IdentityConflict)
                    with { Evidence = acquiredEvidence.Append(SnapshotEvidence()).ToArray() });
                facts.Add(Unavailable(releaseId, "claim-epoch-identity-conflict", invalidDetail, UnitStatusStates.IdentityConflict)
                    with { Evidence = releaseEpochs.Select(item => item.Evidence).Append(SnapshotEvidence()).ToArray() });
                return;
            }

            facts.Add(acquisitionEpochs.Count > 0
                ? Done(acquiredId, "claim-epoch-acquisition-observed", "Claim history or the active claim establishes one or more matching role/team claim epochs.")
                    with { Evidence = acquiredEvidence }
                : Missing(acquiredId, role == LogicalRoleNormalizer.Architect ? "design-claim-not-observed" : "implementation-claim-not-observed",
                    "No matching role/team claim acquisition evidence exists.") with { Evidence = [SnapshotEvidence()] });

            if (acquisitionEpochs.Count == 0)
            {
                facts.Add(Missing(releaseId, role == LogicalRoleNormalizer.Architect ? "design-claim-release-not-observed" : "implementation-claim-release-not-observed",
                    "No matching role/team claim epoch was observed to release.") with { Evidence = [SnapshotEvidence()] });
                return;
            }

            var latestAt = acquisitionEpochs.Max(item => item.At);
            var latestEpochs = acquisitionEpochs.Where(item => item.At == latestAt).ToArray();
            var activeAtLatest = latestEpochs.Any(item => item.Active);
            var releaseAtLatest = releaseEpochs.Where(item => item.At == latestAt).ToArray();
            if (activeAtLatest && releaseAtLatest.Length > 0)
            {
                facts.Add(Unavailable(releaseId, "claim-epoch-identity-conflict", "Claim epoch timestamps tie across incompatible active/release evidence; no history row was selected by order.", UnitStatusStates.IdentityConflict)
                    with { Evidence = releaseEpochs.Select(item => item.Evidence).Concat(latestEpochs.Select(item => item.Evidence)).Distinct().ToArray() });
                return;
            }

            if (releaseAtLatest.Length > 0 && !activeAtLatest)
            {
                facts.Add(Done(releaseId, "claim-epoch-release-observed", "The latest matching role/team claim epoch has a release or displacement record.")
                    with { Evidence = releaseAtLatest.Select(item => item.Evidence).Distinct().ToArray() });
                return;
            }

            var oldReleaseEvidence = releaseEpochs.Select(item => item.Evidence).ToArray();
            facts.Add(Missing(releaseId,
                activeAtLatest || releaseEpochs.Count > 0 ? "claim-release-superseded-by-new-epoch" : "claim-release-not-observed",
                activeAtLatest
                    ? "A later matching claim acquisition is active; earlier release evidence cannot satisfy this epoch."
                    : "No release evidence matches the latest observed claim epoch.")
                with { Evidence = oldReleaseEvidence.Length == 0 ? [SnapshotEvidence()] : oldReleaseEvidence });
        }

        AddRoleFacts(LogicalRoleNormalizer.Architect, "design-claim-acquired", "design-claim-release");
        AddRoleFacts(LogicalRoleNormalizer.Builder, "implementation-claim-acquired", "implementation-claim-release");
    }

    private static IReadOnlyList<UnitStatusObservedReview> AddCloseoutFacts(ICollection<UnitStatusFact> facts, string root, string unit, string? repo, int? pr,
        string domain, string team, string? yaml, IReadOnlyList<RunEvent> runs, string runsPath, string? runsReadError, string? currentHead)
    {
        CrossRuntimeReviewReadResult? review = repo is not null && pr is > 0 ? CrossRuntimeReviewStore.Read(root, repo, pr.Value) : null;
        var matching = review?.Records.Where(item => item.Record.ExecutionUnit == unit && item.Record.Domain == domain && item.Record.Team == team
            && item.Record.Kind == CrossRuntimeReviewRecord.KindImplementation).ToArray() ?? [];
        var localReviews = matching.Select(item => new UnitStatusObservedReview
        {
            Source = "local-cross-runtime-record", HeadSha = item.Record.HeadSha, Verdict = item.Record.Verdict,
            Runtime = item.Record.Runtime, Relation = item.Record.Relation, At = item.Record.RecordedAt,
            RecordId = item.RelativePath, CitedRecordPath = item.Record.RawVerdictFile,
        }).ToArray();
        var localReviewOrderingValid = UnitEvidenceEvaluator.TryOrderReviewRows(
            localReviews, out var orderedLocalReviews, out var localReviewOrderingError);
        var reviewEvidence = matching.Select(item => new UnitStatusEvidencePointer
        {
            Kind = "cross-runtime-review-record",
            Path = item.RelativePath,
            RecordId = item.RelativePath,
            ExecutionUnit = unit,
            Repo = item.Record.Repo,
            Pr = item.Record.Pr,
            HeadSha = item.Record.HeadSha,
            RecordedAt = item.Record.RecordedAt,
            ReviewVerdict = item.Record.Verdict,
            ReviewState = null,
            ReviewDisposition = "local-record-not-github",
            Provenance = "digest-validated-local-cross-runtime-review-record",
        }).ToArray();
        facts.Add((pr is null ? Missing("recorded-review", "pr-not-linked", "A local PR review record cannot be selected until the unit has a linked pull request.")
            : review?.Unreadable.Count > 0 ? Unavailable("recorded-review", "recorded-review-unreadable", string.Join("; ", review.Unreadable.Select(item => item.Error)), UnitStatusStates.ReadFailure)
            : !localReviewOrderingValid ? Unavailable("recorded-review", "recorded-review-identity-conflict", localReviewOrderingError, UnitStatusStates.IdentityConflict)
            : currentHead is null ? Unavailable("recorded-review", "current-pr-head-unavailable", "Digest-validated records cannot be compared with a current PR head.", UnitStatusStates.ReadFailure)
            : matching.Any(item => string.Equals(item.Record.HeadSha, currentHead, StringComparison.OrdinalIgnoreCase))
                ? Done("recorded-review", "recorded-review-current-head", "Digest-validated local implementation review evidence matches the observed PR head.")
                : matching.Length > 0 ? Missing("recorded-review", "recorded-review-head-stale", "Local review records exist, but none matches the current PR head.")
                : Missing("recorded-review", "recorded-review-absent", "No local implementation review record matches this identity and head."))
            with { Evidence = reviewEvidence });
        if (runsReadError is not null)
        {
            facts.Add(Unavailable("pr-merged-run", "runs-unreadable", runsReadError, UnitStatusStates.ReadFailure));
            facts.Add(Unavailable("closeout-recorded-run", "runs-unreadable", runsReadError, UnitStatusStates.ReadFailure));
        }
        else
        {
            AddRunFact(facts, "pr-merged-run", "pr-merged", runs, runsPath, root, unit, repo, pr);
            AddRunFact(facts, "closeout-recorded-run", "closeout-recorded", runs, runsPath, root, unit, repo, pr);
        }
        if (yaml is null)
        {
            foreach (var id in new[] { "architect-knowledge-writeback", "orchestrator-knowledge-writeback", "guide-reachability" })
                facts.Add(Unavailable(id, "packet-unavailable", "Packet declaration is unavailable.", UnitStatusStates.ReadFailure));
            return orderedLocalReviews;
        }
        KnowledgeWriteBackDeclaration declaration;
        try { declaration = KnowledgeWriteBackDeclaration.Read(yaml); }
        catch (Exception exception) when (IsReadException(exception))
        {
            foreach (var id in new[] { "architect-knowledge-writeback", "orchestrator-knowledge-writeback", "guide-reachability" })
                facts.Add(Unavailable(id, "packet-declaration-unreadable", exception.Message, UnitStatusStates.ReadFailure));
            return orderedLocalReviews;
        }
        if (!declaration.IsRequired && !HasExplicitKnowledgeWriteBackDeclaration(yaml))
        {
            facts.Add(Missing("architect-knowledge-writeback", "knowledge-writeback-declaration-absent", "Legacy packet has no knowledge write-back declaration."));
            facts.Add(Missing("orchestrator-knowledge-writeback", "knowledge-writeback-declaration-absent", "Legacy packet has no knowledge write-back declaration."));
        }
        else if (!declaration.IsRequired)
        {
            facts.Add(NotApplicable("architect-knowledge-writeback", "knowledge-writeback-not-required", "Packet declares no required knowledge write-back."));
            facts.Add(NotApplicable("orchestrator-knowledge-writeback", "knowledge-writeback-not-required", "Packet declares no required knowledge write-back."));
        }
        else
        {
            AddWritebackFact(facts, root, unit, LogicalRoleNormalizer.Architect, "architect-knowledge-writeback");
            AddWritebackFact(facts, root, unit, LogicalRoleNormalizer.Orchestrator, "orchestrator-knowledge-writeback");
        }
        AddGuideFact(facts, root, unit, yaml);
        return orderedLocalReviews;
    }

    private static void AddRunFact(ICollection<UnitStatusFact> facts, string id, string eventName, IReadOnlyList<RunEvent> runs,
        string runsPath, string root, string unit, string? repo, int? pr)
    {
        var candidates = runs.Where(run => run.Event == eventName).ToArray();
        var exact = candidates.Where(run => repo is not null && pr is > 0
            && string.Equals(run.Repo, repo, StringComparison.OrdinalIgnoreCase)
            && run.Pr == pr).ToArray();
        var evidence = LocalFileEvidence(root, runsPath, "runs-jsonl", unit, "runs-log-read");
        var pointers = exact.Select(run => evidence is null ? null : evidence with
            {
                RecordId = run.Event,
                RecordedAt = run.Ts,
                Repo = run.Repo,
                Pr = run.Pr,
            })
            .Where(item => item is not null).Cast<UnitStatusEvidencePointer>().ToArray();
        var fact = candidates.Length == 0
            ? Missing(id, eventName + "-run-absent", "No matching " + eventName + " run receipt exists.")
            : exact.Length > 0
                ? Done(id, eventName + "-run-recorded", "Matching run receipt exists.")
                : repo is null || pr is null
                    ? Unavailable(id, "run-identity-unresolved", "Run receipt exists, but exact repository/PR identity is unavailable.", UnitStatusStates.IdentityConflict)
                    : candidates.Any(run => run.Repo is null || run.Pr is null)
                        ? Unavailable(id, "run-identity-unrecorded", "Run receipt lacks the repository/PR fields required for exact attribution.", UnitStatusStates.ProvenanceLimit)
                        : Unavailable(id, "run-identity-conflict", "Run receipt repository/PR identity disagrees with the resolved unit.", UnitStatusStates.IdentityConflict);
        facts.Add(fact with { Evidence = pointers.Length > 0 ? pointers : evidence is null ? [] : [evidence] });
    }

    internal static (IReadOnlyList<T> Rows, IReadOnlyList<string> Errors) ReadRoleRecordRows<T>(
        Func<IEnumerable<string>> enumeratePaths,
        Func<string, T> readRecord)
    {
        var rows = new List<T>();
        var errors = new List<string>();
        try
        {
            foreach (var path in enumeratePaths())
            {
                try { rows.Add(readRecord(path)); }
                catch (Exception exception) when (IsReadException(exception)) { errors.Add(exception.Message); }
            }
        }
        catch (Exception exception) when (IsReadException(exception))
        {
            errors.Add(exception.Message);
        }

        return (rows, errors);
    }

    private static void AddWritebackFact(ICollection<UnitStatusFact> facts, string root, string unit, string role, string id)
    {
        var read = ReadRoleRecordRows(
            () => RoleScopedCloseoutRecordStore.EnumerateExistingPaths(root, KnowledgeWriteBackRecord.RecordRootRelativePath, unit),
            path => (Record: KnowledgeWriteBackRecord.Deserialize(File.ReadAllText(path), unit), Path: path));
        var rows = read.Rows;
        var errors = read.Errors;
        UnitStatusEvidencePointer Pointer((KnowledgeWriteBackRecord Record, string Path) row)
        {
            var qualifies = row.Record.Role is not null
                && CloseoutRecordRole.TryNormalize(row.Record.Role, out var normalizedRole, out _)
                && normalizedRole == role;
            var provenance = row.Record.Role is null
                ? "legacy-role-unattributed; required-role=" + role + "; qualifies=false"
                : "observed-role-record; required-role=" + role + "; qualifies=" + (qualifies ? "true" : "false");
            return LocalFileEvidence(root, row.Path, "knowledge-writeback-record", unit, provenance,
                row.Record.RecordedAt.ToString("O", CultureInfo.InvariantCulture), recordedAt: row.Record.RecordedAt)!
                with { Role = row.Record.Role };
        }
        var matching = rows.Where(row => row.Record.Role is not null
            && CloseoutRecordRole.TryNormalize(row.Record.Role, out var normalizedRole, out _)
            && normalizedRole == role).ToArray();
        var legacy = rows.Where(row => row.Record.Role is null).ToArray();
        var evidence = rows.Select(Pointer).ToArray();
        if (errors.Count > 0) facts.Add(Unavailable(id, "writeback-unreadable", string.Join("; ", errors), UnitStatusStates.ReadFailure)
            with { Evidence = evidence });
        else if (matching.Length > 1) facts.Add(Unavailable(id, "duplicate-closeout-role-record", "Duplicate records exist for this role duty.", UnitStatusStates.IdentityConflict)
            with { Evidence = evidence });
        else if (matching.Length == 1) facts.Add(Done(id, "role-attributed-writeback-recorded", "A write-back record names recorder role " + role + ".")
            with { Evidence = evidence });
        else if (legacy.Length > 0) facts.Add(Unavailable(id, "legacy-closeout-role-unattributed", "An unattributed legacy record cannot satisfy a role-specific duty.", UnitStatusStates.ProvenanceLimit)
            with { Evidence = evidence });
        else facts.Add(Missing(id, "role-writeback-absent", "No record attributed to " + role + " was found.")
            with { Evidence = evidence });
    }

    private static void AddGuideFact(ICollection<UnitStatusFact> facts, string root, string unit, string yaml)
    {
        GuideReachabilityDeclaration declaration;
        try { declaration = GuideReachabilityDeclaration.Read(yaml); }
        catch (Exception exception) when (IsReadException(exception))
        { facts.Add(Unavailable("guide-reachability", "guide-declaration-unreadable", exception.Message, UnitStatusStates.ReadFailure)); return; }
        if (!declaration.IsDeclared) { facts.Add(Missing("guide-reachability", "guide-declaration-absent", "Legacy packet has no guide declaration.")); return; }
        if (declaration.NoRoleFacingSurface) { facts.Add(NotApplicable("guide-reachability", "explicit-no-role-facing-surface", "Packet explicitly declares no role-facing surface.")); return; }
        var read = ReadRoleRecordRows(
            () => RoleScopedCloseoutRecordStore.EnumerateExistingPaths(root, GuideReachabilityRecord.RecordRootRelativePath, unit),
            path => (Record: GuideReachabilityRecord.Deserialize(File.ReadAllText(path), unit), Path: path));
        var rows = read.Rows;
        var errors = read.Errors;
        UnitStatusEvidencePointer Pointer((GuideReachabilityRecord Record, string Path) row)
        {
            var qualifies = row.Record.Role == LogicalRoleNormalizer.Architect;
            var provenance = row.Record.Role is null
                ? "legacy-role-unattributed; required-role=architect; qualifies=false"
                : "observed-role-record; required-role=architect; qualifies=" + (qualifies ? "true" : "false");
            return LocalFileEvidence(root, row.Path, "guide-reachability-record", unit, provenance,
                recordedAt: row.Record.RecordedAt)!
                with { Role = row.Record.Role };
        }
        var matching = rows.Where(row => row.Record.Role == LogicalRoleNormalizer.Architect).ToArray();
        var legacy = rows.Where(row => row.Record.Role is null).ToArray();
        var evidence = rows.Select(Pointer).ToArray();
        if (errors.Count > 0) facts.Add(Unavailable("guide-reachability", "guide-record-unreadable", string.Join("; ", errors), UnitStatusStates.ReadFailure)
            with { Evidence = evidence });
        else if (matching.Length > 1) facts.Add(Unavailable("guide-reachability", "duplicate-closeout-role-record", "Duplicate architect records exist.", UnitStatusStates.IdentityConflict)
            with { Evidence = evidence });
        else if (matching.Length == 1) facts.Add(Done("guide-reachability", "guide-reachability-recorded", "An architect-attributed guide record exists.")
            with { Evidence = evidence });
        else if (legacy.Length > 0) facts.Add(Unavailable("guide-reachability", "legacy-closeout-role-unattributed", "An unattributed legacy record cannot satisfy the architect duty.", UnitStatusStates.ProvenanceLimit)
            with { Evidence = evidence });
        else facts.Add(Missing("guide-reachability", "guide-record-absent", "No architect-attributed guide record exists.")
            with { Evidence = evidence });
    }

    private static UnitStatusEvidenceSnapshot Build(string unit, string? domain, string? team, string? repo, int? issue, int? pr,
        DateTimeOffset observedAt, UnitStatusClaimSnapshot claims, IReadOnlyList<UnitStatusFact> facts,
        IReadOnlyList<string> identitySources, IReadOnlyList<string> localSources, string state, string cause, string detail, string? unavailableClass,
        TeamModeEntry? mode = null, UnitStatusRemoteSnapshot? remote = null,
        IReadOnlyList<UnitStatusObservedReview>? reviews = null) => new()
    {
        ExecutionUnit = unit, Domain = domain, Team = team, Repo = repo, Issue = issue, Pr = pr, HeadSha = remote?.HeadSha,
        TeamMode = mode?.Mode, ModeBasis = mode is null ? null : "current-recorded-entry", ModeEntryPath = mode is null ? null : TeamModeStore.RelativePath,
        ModeEntryUpdatedAt = mode?.UpdatedAt.ToString("O", CultureInfo.InvariantCulture), ObservedAt = observedAt,
        LocalHeadSha = claims.LocalHeadSha, LocalHeadRef = claims.LocalHeadRef, ClaimMetadataRef = claims.MetadataRef, ClaimMetadataOid = claims.MetadataOid,
        ApplicabilityState = state, ApplicabilityCause = cause, ApplicabilityDetail = detail, UnavailableClass = unavailableClass,
        IdentitySources = identitySources, LocalSources = localSources,
        Facts = CompleteFacts(facts, state, cause, detail, unavailableClass),
        Reviews = reviews ?? remote?.Reviews ?? [], Checks = remote?.Checks ?? [], GitHubSnapshotState = remote?.State ?? "not-observed",
        GitHubSnapshotCause = remote?.Cause, GitHubSnapshotDetail = remote?.Detail, GitHubHeadBefore = remote?.HeadBefore, GitHubHeadAfter = remote?.HeadAfter,
        MergeCommitSha = remote?.MergeCommitSha, PullRequestMerged = remote?.Merged, IssueLabels = remote?.IssueLabels ?? [], PullRequestLabels = remote?.PullRequestLabels ?? [],
        Warnings = claims.Warnings.Concat(remote?.Warnings ?? []).Distinct(StringComparer.Ordinal).ToArray(),
    };

    private static IReadOnlyList<UnitStatusFact> CompleteFacts(IReadOnlyList<UnitStatusFact> facts,
        string state, string cause, string detail, string? unavailableClass)
    {
        var known = facts.ToDictionary(fact => fact.Id, StringComparer.Ordinal);
        return FactIds.Select(id => known.TryGetValue(id, out var fact)
            ? fact
            : state == UnitStatusStates.Unavailable
                ? Unavailable(id, cause, detail, unavailableClass ?? UnitStatusStates.ApplicabilityUnresolved)
                : Missing(id, "evidence-not-observed", "Observation stopped before this evidence was read.")
        ).ToArray();
    }

    private static UnitStatusEvidenceSnapshot Empty(string unit, string cause, string detail, string unavailableClass, string? domain = null, string? team = null) => new()
    {
        ExecutionUnit = unit, Domain = domain, Team = team, ObservedAt = DateTimeOffset.UtcNow,
        ApplicabilityState = UnitStatusStates.Unavailable, ApplicabilityCause = cause, ApplicabilityDetail = detail,
        UnavailableClass = unavailableClass, GitHubSnapshotState = "not-observed", Warnings = [detail],
        Facts = FactIds.Select(id => Unavailable(id, cause, detail, unavailableClass)).ToArray(),
    };

    private static int Emit(TextWriter writer, UnitStatusEvidenceSnapshot snapshot, string format)
    {
        var report = UnitEvidenceEvaluator.Evaluate(snapshot);
        UnitStatusRenderer.Write(writer, report, format);
        return report.Summary.ObservationExitCode;
    }

    private static UnitStatusFact Done(string id, string cause, string detail) => new() { Id = id, State = UnitStatusStates.Done, Cause = cause, Detail = detail };
    private static UnitStatusFact Missing(string id, string cause, string detail) => new() { Id = id, State = UnitStatusStates.Missing, Cause = cause, Detail = detail };
    private static UnitStatusFact NotApplicable(string id, string cause, string detail) => new() { Id = id, State = UnitStatusStates.NotApplicable, Cause = cause, Detail = detail };
    private static UnitStatusFact Unavailable(string id, string cause, string detail, string unavailableClass) => new() { Id = id, State = UnitStatusStates.Unavailable, Cause = cause, Detail = detail, UnavailableClass = unavailableClass };

    private static string? ReadText(string path, out string? error)
    {
        error = null;
        try
        {
            if (Directory.Exists(path))
            {
                error = "Expected a file, but the path is a directory.";
                return null;
            }

            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception exception) when (IsReadException(exception)) { error = exception.Message; return null; }
    }

    private static bool TryParseGithubUrl(string? value, string kind, out string repo, out int number)
    {
        repo = ""; number = 0;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 4
            || uri.AbsolutePath != "/" + string.Join('/', parts)
            || parts[2] != kind
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out number)
            || number <= 0) return false;
        repo = parts[0] + "/" + parts[1]; return true;
    }

    private static bool SafeRepo(string value) => value.Split('/') is { Length: 2 } parts
        && parts.All(part => part.Length > 0 && !part.Contains("..", StringComparison.Ordinal)
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool Contained(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison)
            && !Path.IsPathRooted(relative);
    }
    private static bool IsReadException(Exception exception) => exception is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException or FormatException or NotSupportedException;

    private static bool TryParse(IReadOnlyList<string> args, out string? unit, out string? domain, out string? team, out string format, out string error)
    {
        unit = null; domain = null; team = null; format = "json"; error = "";
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["--execution-unit", "--domain", "--team", "--format"], StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var flag = args[index];
            if (!allowed.Contains(flag) || index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            { error = "Invalid or incomplete argument '" + flag + "'. " + Usage; return false; }
            if (!values.TryAdd(flag, args[++index].Trim())) { error = "Argument '" + flag + "' may only appear once. " + Usage; return false; }
        }
        if (!values.TryGetValue("--execution-unit", out unit) || string.IsNullOrWhiteSpace(unit))
        { error = "--execution-unit is required. " + Usage; return false; }
        domain = values.GetValueOrDefault("--domain"); team = values.GetValueOrDefault("--team");
        if (!values.TryGetValue("--format", out var suppliedFormat) || string.IsNullOrWhiteSpace(suppliedFormat))
        { error = "--format is required. " + Usage; return false; }
        format = suppliedFormat;
        if (values.Any(pair => (pair.Key is "--domain" or "--team") && string.IsNullOrWhiteSpace(pair.Value)))
        { error = "--domain and --team must not be empty."; return false; }
        if (format is not "json" and not "markdown") { format = "json"; error = "--format must be json or markdown. " + Usage; return false; }
        if ((!string.IsNullOrWhiteSpace(domain) && !SafeScope(domain)) || (!string.IsNullOrWhiteSpace(team) && !SafeScope(team)))
        { error = "--domain and --team must be path-safe identifiers."; return false; }
        return true;
    }

    private static bool SafeScope(string value) => value.Length is > 0 and <= 128
        && value is not "." and not ".."
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    private static bool IsHelp(IReadOnlyList<string> args) => args.Count == 1 && args[0] is "--help" or "help";
    private static UnitStatusEvidencePointer EvidenceFile(string kind, string path, string unit, string provenance) => new()
    {
        Kind = kind,
        Path = path,
        ExecutionUnit = unit,
        Provenance = provenance,
    };

    private static UnitStatusEvidencePointer? LocalFileEvidence(
        string root,
        string path,
        string kind,
        string unit,
        string provenance,
        string? recordId = null,
        string? repo = null,
        int? pr = null,
        DateTimeOffset? recordedAt = null)
    {
        var fullRoot = Path.GetFullPath(root);
        var relative = Path.GetRelativePath(fullRoot, Path.GetFullPath(path));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        return new UnitStatusEvidencePointer
        {
            Kind = kind,
            Path = relative.Replace('\\', '/'),
            RecordId = recordId,
            ExecutionUnit = unit,
            Repo = repo,
            Pr = pr,
            RecordedAt = recordedAt,
            Provenance = provenance,
        };
    }

    private static bool MatchesPublishedIssue(RunEvent run, string repo, int issue)
    {
        if (!TryParseGithubUrl(run.LinkedIssue, "issues", out var linkedRepo, out var linkedIssue)
            || !string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase)
            || linkedIssue != issue)
        {
            return false;
        }

        return run.Repo is null || string.Equals(run.Repo, repo, StringComparison.OrdinalIgnoreCase);
    }

    private static string PacketRelativePath(string unit, string fileName) =>
        Path.Combine(".intent-cli", "issues", unit, fileName).Replace('\\', '/');

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("unit status"); writer.WriteLine(Usage);
        writer.WriteLine("Read-only lifecycle evidence observation. Missing evidence is separate from unreadable or unsupported provenance.");
        writer.WriteLine("Exit 0 means observation completed or recorded non-solo mode applies; it does not mean complete or merge-ready.");
        writer.WriteLine("Stable provenance-limit findings are not retry or re-approval instructions.");
    }
}
