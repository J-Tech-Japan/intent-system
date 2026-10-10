using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G858: a report-local, read-only index for the start-window applied to
/// knowledge and guide closeout debt. This deliberately has no provider,
/// process, network, or durable-write surface.
/// </summary>
internal sealed class StalledWorkAdoptionWindow
{
    private static readonly Regex RepoIssuePattern = new("^([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)#([1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex GitHubIssueUrlPattern = new("https?://(?:www\\.)?github\\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/(issues|pull)/([1-9][0-9]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GitHubIssueUrlExactPattern = new("^https?://(?:www\\.)?github\\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/(issues|pull)/([1-9][0-9]*)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GitHubPullUrlPattern = new("^https?://(?:www\\.)?github\\.com/([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)/pull/([1-9][0-9]*)/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly string repoRoot;
    private readonly string domain;
    private readonly string repo;
    private readonly string? requestedTeam;
    private readonly DateTimeOffset now;
    private readonly TeamModeResolution modeResolution;
    private readonly StalledWorkDebtWindowPolicy policy;
    private readonly DateTimeOffset? cutoff;
    private readonly string? unavailableReason;
    private readonly string legacyRunLogPath;
    private readonly string modePath;
    private readonly Dictionary<string, UnitEvidence> evidenceByUnit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StalledWorkAdoptionDecision> decisionCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StalledWorkAdoptionDecision> reportedDecisions = new(StringComparer.Ordinal);
    private readonly HashSet<string> emittedDiagnostics = new(StringComparer.Ordinal);
    private string? runIndexError;
    private string? claimsIndexError;

    private StalledWorkAdoptionWindow(
        string repoRoot,
        string domain,
        string repo,
        string? requestedTeam,
        DateTimeOffset now,
        TeamModeResolution modeResolution,
        StalledWorkDebtWindowPolicy policy,
        DateTimeOffset? cutoff,
        string? unavailableReason,
        string legacyRunLogPath)
    {
        this.repoRoot = Path.GetFullPath(repoRoot);
        this.domain = domain;
        this.repo = repo;
        this.requestedTeam = requestedTeam;
        this.now = now.ToUniversalTime();
        this.modeResolution = modeResolution;
        this.policy = policy;
        this.cutoff = cutoff?.ToUniversalTime();
        this.unavailableReason = unavailableReason;
        this.legacyRunLogPath = Path.GetFullPath(legacyRunLogPath);
        modePath = TeamModeStore.ResolvePath(this.repoRoot);
        BuildIndex();
    }

    public static StalledWorkAdoptionWindow? Create(
        string repoRoot,
        string domain,
        string repo,
        string? requestedTeam,
        string legacyRunLogPath,
        TeamModeResolution modeResolution,
        DateTimeOffset? explicitSince,
        DateTimeOffset now,
        List<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(modeResolution);
        ArgumentNullException.ThrowIfNull(warnings);

        if (explicitSince is null && !modeResolution.IsSoloConductor)
        {
            return null;
        }

        DateTimeOffset? cutoff = explicitSince;
        string? unavailableReason = null;
        var policy = explicitSince is null
            ? StalledWorkDebtWindowPolicy.SoloFirstAdoption
            : StalledWorkDebtWindowPolicy.ExplicitSince;

        if (explicitSince is null)
        {
            var firstSoloTransition = modeResolution.Entry?.Transitions
                .Where(transition => string.Equals(transition.To, TeamMode.SoloConductor, StringComparison.Ordinal))
                .Select(transition => (DateTimeOffset?)transition.At.ToUniversalTime())
                .FirstOrDefault();
            if (firstSoloTransition is null)
            {
                unavailableReason = "solo-adoption-transition-missing";
            }
            else if (firstSoloTransition.Value > now.ToUniversalTime())
            {
                unavailableReason = "solo-adoption-transition-in-future";
            }
            else
            {
                cutoff = firstSoloTransition;
            }
        }

        var window = new StalledWorkAdoptionWindow(
            repoRoot,
            domain,
            repo,
            requestedTeam,
            now,
            modeResolution,
            policy,
            cutoff,
            unavailableReason,
            legacyRunLogPath);
        if (window.runIndexError is not null)
        {
            warnings.Add($"debt window run-log index is incomplete at `{window.legacyRunLogPath}`: {window.runIndexError}");
        }
        if (window.claimsIndexError is not null)
        {
            warnings.Add($"debt window claim index is incomplete at `{Path.Combine(window.repoRoot, ".intent-cli", "claims")}`: {window.claimsIndexError}");
        }
        return window;
    }

    public DateTimeOffset? Cutoff => cutoff;

    public StalledWorkAdoptionDecision Decide(string executionUnit)
    {
        if (decisionCache.TryGetValue(executionUnit, out var prior))
        {
            return prior;
        }

        var decision = DecideCore(executionUnit);
        decisionCache[executionUnit] = decision;
        return decision;
    }

    public bool ReportDecision(
        string executionUnit,
        string laneKind,
        List<StalledWorkExcluded> excluded,
        List<string> warnings)
    {
        var decision = Decide(executionUnit);
        reportedDecisions.TryAdd(executionUnit, decision);
        if (decision.Status == StalledWorkAdoptionDecisionStatus.Historical)
        {
            excluded.Add(new StalledWorkExcluded
            {
                Kind = laneKind,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                Reason = "debt-window-historical",
                Detail = $"`{executionUnit}` is outside the requested debt window: {decision.Detail}",
                DebtWindowStatus = decision.Status,
                DebtWindowEvidenceKind = decision.EvidenceKind,
                DebtWindowEvidencePath = decision.EvidencePath,
                DebtWindowEvidenceAt = decision.EvidenceAt,
                DebtWindowCutoff = decision.Cutoff,
            });
            return false;
        }

        if (decision.Status is StalledWorkAdoptionDecisionStatus.Unknown
            or StalledWorkAdoptionDecisionStatus.ForeignTeam
            or StalledWorkAdoptionDecisionStatus.Unavailable)
        {
            var reason = decision.Status switch
            {
                StalledWorkAdoptionDecisionStatus.ForeignTeam => "debt-window-foreign-team",
                StalledWorkAdoptionDecisionStatus.Unavailable => "debt-window-unavailable",
                _ => "debt-window-start-unknown",
            };
            excluded.Add(new StalledWorkExcluded
            {
                Kind = laneKind,
                ExecutionUnit = executionUnit,
                Issue = null,
                Pr = null,
                Reason = reason,
                Detail = $"`{executionUnit}` remains in the existing debt scan because its start-window decision is {decision.Reason}: {decision.Detail}",
                DebtWindowStatus = decision.Status,
                DebtWindowEvidenceKind = decision.EvidenceKind,
                DebtWindowEvidencePath = decision.EvidencePath,
                DebtWindowEvidenceAt = decision.EvidenceAt,
                DebtWindowCutoff = decision.Cutoff,
            });

            var warningKey = executionUnit + "\0" + reason + "\0" + decision.Reason;
            if (emittedDiagnostics.Add(warningKey))
            {
                warnings.Add($"debt window retained `{executionUnit}` ({reason}; {decision.Reason}): {decision.Detail}");
            }
        }

        return true;
    }

    public StalledWorkDebtWindowSummary BuildSummary(
        DateTimeOffset? knowledgeCloseoutCutoff,
        DateTimeOffset? guideCloseoutCutoff)
    {
        var counts = reportedDecisions.Values.ToArray();
        return new StalledWorkDebtWindowSummary
        {
            Policy = policy == StalledWorkDebtWindowPolicy.ExplicitSince ? "explicit-since" : "solo-first-adoption",
            Cutoff = cutoff,
            UnavailableReason = unavailableReason,
            ResolvedDomain = modeResolution.Entry?.Domain ?? domain,
            ResolvedTeam = modeResolution.Entry?.Team,
            RequestedTeam = requestedTeam,
            ResolvedScope = modeResolution.Entry?.Team is null
                ? (modeResolution.Source == TeamModeSource.Recorded ? "domain-wide" : "default")
                : modeResolution.UsedUniqueTeamFallback ? "unique-team-fallback" : "team-specific",
            TeamMode = modeResolution.Mode,
            ModeSource = modeResolution.Source == TeamModeSource.Recorded ? "recorded" : "default",
            ModePath = modePath,
            LegacyRunLogPath = legacyRunLogPath,
            KnowledgeWritebackCloseoutCutoff = knowledgeCloseoutCutoff,
            GuideReachabilityCloseoutCutoff = guideCloseoutCutoff,
            DecisionCounts = new StalledWorkDebtWindowDecisionCounts
            {
                CandidateUnits = counts.Length,
                IncludedUnits = counts.Count(decision => decision.Status == StalledWorkAdoptionDecisionStatus.Included),
                HistoricalUnits = counts.Count(decision => decision.Status == StalledWorkAdoptionDecisionStatus.Historical),
                UnknownUnits = counts.Count(decision => decision.Status is StalledWorkAdoptionDecisionStatus.Unknown or StalledWorkAdoptionDecisionStatus.Unavailable),
                ForeignTeamUnits = counts.Count(decision => decision.Status == StalledWorkAdoptionDecisionStatus.ForeignTeam),
            },
        };
    }

    private StalledWorkAdoptionDecision DecideCore(string executionUnit)
    {
        if (cutoff is null)
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unavailable,
                unavailableReason ?? "debt-window-cutoff-unavailable", "no usable start cutoff is available.", null, null, null);
        }

        if (!TryValidateUnit(executionUnit, out var unitError))
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "execution-unit-invalid",
                unitError, "unit-identity", legacyRunLogPath, null);
        }

        var packetPath = ResolvePacketPath(executionUnit);
        if (!TryReadPacketIdentity(packetPath, executionUnit, out var identity, out var identityReason))
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, identityReason,
                $"packet identity at `{packetPath}` could not corroborate the requested domain/repository.", "packet-identity", packetPath, null);
        }

        if (!string.Equals(identity.Domain, domain, StringComparison.OrdinalIgnoreCase))
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "packet-domain-mismatch",
                $"packet declares domain `{identity.Domain}` instead of `{domain}`.", "packet-identity", packetPath, null);
        }

        if (!string.Equals(identity.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "packet-repository-mismatch",
                $"packet declares repository `{identity.Repo}` instead of `{repo}`.", "packet-identity", packetPath, null);
        }

        if (runIndexError is not null)
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "run-log-index-unavailable",
                runIndexError, "run-log", legacyRunLogPath, null);
        }

        if (claimsIndexError is not null)
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "claim-index-unavailable",
                claimsIndexError, "claim-index", Path.Combine(repoRoot, ".intent-cli", "claims"), null);
        }

        evidenceByUnit.TryGetValue(executionUnit, out var evidence);
        evidence ??= new UnitEvidence();

        if (evidence.Problems.Count > 0)
        {
            var problem = evidence.Problems[0];
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, problem.Reason,
                problem.Detail, problem.Kind, problem.Path, problem.At);
        }

        if (policy == StalledWorkDebtWindowPolicy.SoloFirstAdoption
            && modeResolution.Entry?.Team is { } resolvedTeam)
        {
            var claimTeams = evidence.ClaimTeams
                .Where(team => !string.IsNullOrWhiteSpace(team.Team))
                .Select(team => team.Team!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (evidence.ClaimTeamMissing || claimTeams.Length == 0)
            {
                return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "claim-team-provenance-missing",
                    "a team-specific adoption window requires an active claim team or displaced claim team for this unit.",
                    "claim-team", evidence.ClaimTeamPaths.FirstOrDefault() ?? legacyRunLogPath, null);
            }
            if (claimTeams.Length > 1)
            {
                return Decision(StalledWorkAdoptionDecisionStatus.Unknown, "claim-team-provenance-conflict",
                    $"claim acquisitions for this unit name conflicting teams: {string.Join(", ", claimTeams.OrderBy(value => value, StringComparer.Ordinal))}.",
                    "claim-team", evidence.ClaimTeamPaths.FirstOrDefault() ?? legacyRunLogPath, null);
            }
            if (!string.Equals(claimTeams[0], resolvedTeam, StringComparison.Ordinal))
            {
                return Decision(StalledWorkAdoptionDecisionStatus.ForeignTeam, "foreign-team",
                    $"the unit's claim provenance names team `{claimTeams[0]}`, outside resolved adoption team `{resolvedTeam}`.",
                    "claim-team", evidence.ClaimTeamPaths.FirstOrDefault() ?? legacyRunLogPath, null);
            }
        }

        var historicalCloseout = evidence.Closeouts
            .Where(value => string.Equals(value.Repo, repo, StringComparison.OrdinalIgnoreCase)
                && value.At < cutoff.Value)
            .OrderBy(value => value.At)
            .FirstOrDefault();
        if (historicalCloseout is not null)
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Historical, "historical-existence",
                "a corroborated closeout before the cutoff proves the unit already existed; it does not assert an exact start time.",
                "historical-existence", historicalCloseout.Path, historicalCloseout.At);
        }

        var matchingStarts = evidence.Starts
            .Where(value => string.Equals(value.Repo, repo, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value.At)
            .ToArray();
        if (matchingStarts.Length == 0)
        {
            var reason = evidence.Starts.Count > 0 ? "start-evidence-for-another-repository" : "start-evidence-missing";
            var detail = evidence.Starts.Count > 0
                ? "the unit has start events, but none corroborate the requested repository."
                : "no supported issue-created, issue-published, or claim-acquisition timestamp was found; a later closeout cannot establish a new start.";
            return Decision(StalledWorkAdoptionDecisionStatus.Unknown, reason, detail,
                evidence.Starts.Count > 0 ? "issue-start" : "start-evidence", evidence.Starts.FirstOrDefault()?.Path ?? legacyRunLogPath, null);
        }

        var earliestStart = matchingStarts[0];
        if (earliestStart.At < cutoff.Value)
        {
            return Decision(StalledWorkAdoptionDecisionStatus.Historical, "supported-start-before-cutoff",
                "the earliest corroborated durable start predates the cutoff.", earliestStart.Kind, earliestStart.Path, earliestStart.At);
        }

        return Decision(StalledWorkAdoptionDecisionStatus.Included, "start-at-or-after-cutoff",
            "the earliest corroborated durable start is at or after the inclusive cutoff.", earliestStart.Kind, earliestStart.Path, earliestStart.At);
    }

    private StalledWorkAdoptionDecision Decision(
        string status,
        string reason,
        string detail,
        string? evidenceKind,
        string? evidencePath,
        DateTimeOffset? evidenceAt) => new()
    {
        Status = status,
        Reason = reason,
        Detail = detail,
        EvidenceKind = evidenceKind,
        EvidencePath = evidencePath,
        EvidenceAt = evidenceAt,
        Cutoff = cutoff,
    };

    private void BuildIndex()
    {
        BuildRunIndex();
        BuildClaimIndex();
    }

    private void BuildRunIndex()
    {
        if (!TryProbePath(legacyRunLogPath, repoRoot, out var exists, out var isDirectory, out var pathError))
        {
            runIndexError = pathError;
            return;
        }
        if (!exists)
        {
            return;
        }
        if (isDirectory)
        {
            runIndexError = $"legacy run log path `{legacyRunLogPath}` is a directory, not a regular evidence file.";
            return;
        }

        if (!TryReadRegularFile(legacyRunLogPath, repoRoot, out var content, out var readError))
        {
            runIndexError = readError;
            return;
        }

        IReadOnlyList<RunEvent> events;
        try
        {
            events = RunLogSerializer.DeserializeAll(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or NotSupportedException)
        {
            runIndexError = $"legacy run log `{legacyRunLogPath}` could not be indexed: {exception.Message}";
            return;
        }

        foreach (var runEvent in events)
        {
            if (runEvent.Event is not ("issue-created" or "issue-published" or "closeout-recorded"))
            {
                continue;
            }

            if (!TryValidateUnit(runEvent.ExecutionUnit, out var unitError))
            {
                // A supported start/closeout event with an unscopable unit
                // can belong to any candidate. Do not build a path from it,
                // and do not let other rows in this partial index authorize
                // an exclusion.
                runIndexError = $"legacy run log `{legacyRunLogPath}` contains a supported event with an unsafe or missing execution-unit: {unitError}; exclusions are disabled for the incomplete index.";
                return;
            }

            var unitEvidence = GetEvidence(runEvent.ExecutionUnit!);
            if (runEvent.Event is "issue-created" or "issue-published")
            {
                var parsed = ParseIssueIdentity(runEvent);
                if (parsed.Problem is not null)
                {
                    unitEvidence.Problems.Add(new EvidenceProblem(
                        "issue-start-identity-invalid",
                        $"`{runEvent.Event}` in `{legacyRunLogPath}` has conflicting or unsupported GitHub issue identity: {parsed.Problem}",
                        "issue-start",
                        legacyRunLogPath,
                        runEvent.Ts.ToUniversalTime()));
                    continue;
                }

                if (parsed.Repo is null || parsed.Number is null)
                {
                    unitEvidence.Problems.Add(new EvidenceProblem(
                        "issue-start-identity-missing",
                        $"`{runEvent.Event}` in `{legacyRunLogPath}` has no repository-bound GitHub issue identity.",
                        "issue-start",
                        legacyRunLogPath,
                        null));
                    continue;
                }

                if (!string.Equals(parsed.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    // A coherent start event for another repository is not
                    // evidence for this report and must not affect its window.
                    continue;
                }

                if (runEvent.Ts == default || runEvent.Ts.ToUniversalTime() > now)
                {
                    unitEvidence.Problems.Add(new EvidenceProblem(
                        "run-event-timestamp-invalid",
                        $"`{runEvent.Event}` in `{legacyRunLogPath}` has a missing, default, or future timestamp.",
                        "run-event",
                        legacyRunLogPath,
                        runEvent.Ts == default ? null : runEvent.Ts));
                    continue;
                }

                if (parsed.Repo is not null)
                {
                    unitEvidence.Starts.Add(new WindowEvidence(
                        runEvent.Ts.ToUniversalTime(),
                        parsed.Repo,
                        runEvent.Event,
                        legacyRunLogPath));
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(runEvent.Repo) && !IsCanonicalRepo(runEvent.Repo))
            {
                unitEvidence.Problems.Add(new EvidenceProblem(
                    "closeout-identity-invalid",
                    $"`closeout-recorded` in `{legacyRunLogPath}` has a malformed repository binding.",
                    "closeout",
                    legacyRunLogPath,
                    runEvent.Ts.ToUniversalTime()));
                continue;
            }

            string? closeoutRepo = string.IsNullOrWhiteSpace(runEvent.Repo) ? null : runEvent.Repo;
            var closeoutPr = runEvent.Pr;
            if (!string.IsNullOrWhiteSpace(runEvent.LinkedPr))
            {
                var linkedPrValue = runEvent.LinkedPr.Trim();
                Match? pullMatch = null;
                int? linkedPrNumber = null;
                if (Regex.IsMatch(linkedPrValue, "^(?:#)?[1-9][0-9]*$", RegexOptions.CultureInvariant))
                {
                    var numberText = linkedPrValue.TrimStart('#');
                    if (int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var barePr))
                    {
                        linkedPrNumber = barePr;
                    }
                }
                else
                {
                    pullMatch = GitHubPullUrlPattern.Match(linkedPrValue);
                    if (pullMatch.Success
                        && int.TryParse(pullMatch.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var urlPr))
                    {
                        linkedPrNumber = urlPr;
                    }
                }

                if (linkedPrNumber is null)
                {
                    if (string.Equals(runEvent.Repo, repo, StringComparison.OrdinalIgnoreCase)
                        || linkedPrValue.Contains($"github.com/{repo}/pull/", StringComparison.OrdinalIgnoreCase))
                    {
                        unitEvidence.Problems.Add(new EvidenceProblem(
                            "closeout-linked-pr-invalid",
                            $"`closeout-recorded` in `{legacyRunLogPath}` has an unsupported linked pull-request identity.",
                            "closeout",
                            legacyRunLogPath,
                            runEvent.Ts == default ? null : runEvent.Ts));
                    }
                    continue;
                }
                var linkedRepo = pullMatch is { Success: true }
                    ? $"{pullMatch.Groups[1].Value}/{pullMatch.Groups[2].Value}"
                    : closeoutRepo;
                if ((linkedRepo is not null && closeoutRepo is not null && !string.Equals(linkedRepo, closeoutRepo, StringComparison.OrdinalIgnoreCase))
                    || (closeoutPr is not null && closeoutPr.Value != linkedPrNumber.Value))
                {
                    if (string.Equals(closeoutRepo, repo, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
                    {
                        unitEvidence.Problems.Add(new EvidenceProblem(
                            "closeout-identity-conflict",
                            $"`closeout-recorded` repository/PR fields in `{legacyRunLogPath}` contradict the linked pull-request URL.",
                            "closeout",
                            legacyRunLogPath,
                            runEvent.Ts == default ? null : runEvent.Ts));
                    }
                    continue;
                }
                closeoutRepo ??= linkedRepo;
                closeoutPr ??= linkedPrNumber.Value;
            }

            if (closeoutRepo is null || !string.Equals(closeoutRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (runEvent.Ts == default || runEvent.Ts.ToUniversalTime() > now)
            {
                unitEvidence.Problems.Add(new EvidenceProblem(
                    "run-event-timestamp-invalid",
                    $"`closeout-recorded` in `{legacyRunLogPath}` has a missing, default, or future timestamp.",
                    "closeout",
                    legacyRunLogPath,
                    runEvent.Ts == default ? null : runEvent.Ts));
                continue;
            }

            if (closeoutPr is null or <= 0)
            {
                unitEvidence.Problems.Add(new EvidenceProblem(
                    "closeout-identity-incomplete",
                    $"`closeout-recorded` in `{legacyRunLogPath}` names repository `{closeoutRepo}` without a positive PR number.",
                    "closeout",
                    legacyRunLogPath,
                    runEvent.Ts.ToUniversalTime()));
                continue;
            }

            unitEvidence.Closeouts.Add(new WindowEvidence(
                runEvent.Ts.ToUniversalTime(),
                closeoutRepo,
                "closeout-recorded",
                legacyRunLogPath));
        }
    }

    private IssueIdentityParse ParseIssueIdentity(RunEvent runEvent)
    {
        var repositories = new List<string>();
        var numbers = new List<int>();
        var linked = ParseIssueReference(runEvent.LinkedIssue, allowReasonText: false);
        if (linked.Problem is not null)
        {
            return new IssueIdentityParse(null, linked.Problem);
        }
        if (linked.Repo is not null)
        {
            repositories.Add(linked.Repo);
        }
        if (linked.Number is not null)
        {
            numbers.Add(linked.Number.Value);
        }

        if (!string.IsNullOrWhiteSpace(runEvent.Reason))
        {
            var urls = GitHubIssueUrlPattern.Matches(runEvent.Reason);
            foreach (Match match in urls)
            {
                if (!int.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    return new IssueIdentityParse(null, "the issue URL number is invalid.");
                }
                if (!string.Equals(match.Groups[3].Value, "issues", StringComparison.OrdinalIgnoreCase))
                {
                    return new IssueIdentityParse(null, "the reason URL points to a pull request, not an issue.");
                }
                repositories.Add($"{match.Groups[1].Value}/{match.Groups[2].Value}");
                numbers.Add(number);
            }
        }

        if (!string.IsNullOrWhiteSpace(runEvent.Repo))
        {
            if (!IsCanonicalRepo(runEvent.Repo))
            {
                return new IssueIdentityParse(null, "the event repository is malformed.");
            }
            repositories.Add(runEvent.Repo);
        }

        var distinctRepositories = repositories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var distinctNumbers = numbers.Distinct().ToArray();
        if (distinctRepositories.Length > 1 || distinctNumbers.Length > 1)
        {
            return new IssueIdentityParse(null, "independent issue/repository assertions disagree.");
        }

        return new IssueIdentityParse(
            distinctRepositories.SingleOrDefault(),
            null,
            distinctNumbers.Length == 1 ? distinctNumbers[0] : null);
    }

    private static IssueIdentityParse ParseIssueReference(string? value, bool allowReasonText)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new IssueIdentityParse(null, null);
        }

        var exact = RepoIssuePattern.Match(value.Trim());
        if (exact.Success
            && int.TryParse(exact.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return new IssueIdentityParse($"{exact.Groups[1].Value}/{exact.Groups[2].Value}", null, number);
        }

        var linkedValue = value.Trim();
        var exactUrl = GitHubIssueUrlExactPattern.Match(linkedValue);
        if (exactUrl.Success)
        {
            if (!string.Equals(exactUrl.Groups[3].Value, "issues", StringComparison.OrdinalIgnoreCase))
            {
                return new IssueIdentityParse(null, "linked issue URL points to a pull request.");
            }
            if (int.TryParse(exactUrl.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                return new IssueIdentityParse($"{exactUrl.Groups[1].Value}/{exactUrl.Groups[2].Value}", null, number);
            }
        }

        if (Regex.IsMatch(value.Trim(), "^(?:#)?[1-9][0-9]*$", RegexOptions.CultureInvariant))
        {
            // A bare number has no repository binding. A separate supported
            // Reason URL may still bind the writer record.
            var numberOnly = value.Trim().TrimStart('#');
            return int.TryParse(numberOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var bareNumber)
                ? new IssueIdentityParse(null, null, bareNumber)
                : new IssueIdentityParse(null, "the bare issue number is invalid.");
        }

        return new IssueIdentityParse(null, allowReasonText ? null : "linked issue is not a supported owner/repo#N or GitHub issue URL.");
    }

    private void BuildClaimIndex()
    {
        var claimsRoot = Path.Combine(repoRoot, ".intent-cli", "claims");
        var historyRoot = Path.Combine(claimsRoot, "history");
        if (!TryProbePath(claimsRoot, repoRoot, out var claimsRootExists, out var claimsRootIsDirectory, out var claimsPathError))
        {
            claimsIndexError = claimsPathError;
            return;
        }
        if (!claimsRootExists)
        {
            return;
        }
        if (!claimsRootIsDirectory)
        {
            claimsIndexError = $"claim root `{claimsRoot}` is not a directory; exclusions are disabled for the incomplete index.";
            return;
        }

        if (!TryEnumerateDirectory(claimsRoot, repoRoot, out var activeFiles, out var activeError))
        {
            claimsIndexError = activeError;
            return;
        }

        foreach (var path in activeFiles.Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryReadJsonFile(path, repoRoot, out var document, out var parseError))
            {
                claimsIndexError = parseError;
                return;
            }
            using (document)
            {
                if (!TryReadScope(document.RootElement, out var unit, out var irrelevant, out var scopeError))
                {
                    claimsIndexError = scopeError;
                    return;
                }
                if (irrelevant)
                {
                    continue;
                }

                var evidence = GetEvidence(unit!);
                if (!TryReadDate(document.RootElement, "claimed_at", out var claimedAt))
                {
                    evidence.Problems.Add(new EvidenceProblem(
                        "claim-start-timestamp-invalid",
                        $"active claim `{path}` has no valid `claimed_at` timestamp.",
                        "active-claim",
                        path,
                        null));
                    continue;
                }
                if (claimedAt > now)
                {
                    evidence.Problems.Add(new EvidenceProblem(
                        "claim-start-timestamp-future",
                        $"active claim `{path}` has a future `claimed_at` timestamp.",
                        "active-claim",
                        path,
                        claimedAt));
                    continue;
                }

                if (!TryReadNullableString(document.RootElement, "team", out var team, out var malformedTeam))
                {
                    evidence.Problems.Add(new EvidenceProblem(
                        "claim-team-field-malformed",
                        $"active claim `{path}` has a non-string `team` field.",
                        "active-claim",
                        path,
                        claimedAt));
                    continue;
                }
                evidence.Starts.Add(new WindowEvidence(claimedAt, repo, "active-claim", path));
                evidence.ClaimTeams.Add(new ClaimTeamEvidence(team, path));
                evidence.ClaimTeamPaths.Add(path);
                evidence.ClaimTeamMissing |= malformedTeam || string.IsNullOrWhiteSpace(team);
            }
        }

        if (!TryProbePath(historyRoot, repoRoot, out var historyRootExists, out var historyRootIsDirectory, out var historyPathError))
        {
            claimsIndexError = historyPathError;
            return;
        }
        if (!historyRootExists)
        {
            return;
        }
        if (!historyRootIsDirectory)
        {
            claimsIndexError = $"claim history root `{historyRoot}` is not a directory; exclusions are disabled for the incomplete index.";
            return;
        }
        if (!TryEnumerateDirectory(historyRoot, repoRoot, out var buckets, out var historyError))
        {
            claimsIndexError = historyError;
            return;
        }

        foreach (var bucket in buckets)
        {
            if (!TryProbePath(bucket, repoRoot, out var bucketExists, out var bucketIsDirectory, out historyError))
            {
                claimsIndexError = historyError;
                return;
            }
            if (!bucketExists || !bucketIsDirectory)
            {
                claimsIndexError = $"claim history entry `{bucket}` is missing or is not a regular bucket directory; exclusions are disabled for the incomplete index.";
                return;
            }
            if (!TryEnumerateDirectory(bucket, repoRoot, out var historyFiles, out historyError))
            {
                claimsIndexError = historyError;
                return;
            }
            foreach (var path in historyFiles.Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                if (!TryReadJsonFile(path, repoRoot, out var document, out var parseError))
                {
                    claimsIndexError = parseError;
                    return;
                }
                using (document)
                {
                    if (!TryReadScope(document.RootElement, out var unit, out var irrelevant, out var scopeError))
                    {
                        claimsIndexError = scopeError;
                        return;
                    }
                    if (irrelevant)
                    {
                        continue;
                    }

                    var evidence = GetEvidence(unit!);
                    if (!TryReadDate(document.RootElement, "displaced_claimed_at", out var displacedClaimedAt))
                    {
                        evidence.Problems.Add(new EvidenceProblem(
                            "claim-history-start-timestamp-invalid",
                            $"claim history `{path}` has no valid `displaced_claimed_at` timestamp.",
                            "claim-history",
                            path,
                            null));
                        continue;
                    }
                    if (displacedClaimedAt > now)
                    {
                        evidence.Problems.Add(new EvidenceProblem(
                            "claim-history-start-timestamp-future",
                            $"claim history `{path}` has a future `displaced_claimed_at` timestamp.",
                            "claim-history",
                            path,
                            displacedClaimedAt));
                        continue;
                    }

                    if (!TryReadNullableString(document.RootElement, "displaced_team", out var displacedTeam, out var malformedTeam))
                    {
                        evidence.Problems.Add(new EvidenceProblem(
                            "claim-history-team-field-malformed",
                            $"claim history `{path}` has a non-string `displaced_team` field.",
                            "claim-history",
                            path,
                            displacedClaimedAt));
                        continue;
                    }
                    evidence.Starts.Add(new WindowEvidence(displacedClaimedAt, repo, "claim-history", path));
                    evidence.ClaimTeams.Add(new ClaimTeamEvidence(displacedTeam, path));
                    evidence.ClaimTeamPaths.Add(path);
                    evidence.ClaimTeamMissing |= malformedTeam || string.IsNullOrWhiteSpace(displacedTeam);
                }
            }
        }
    }

    private static bool TryReadScope(JsonElement root, out string? unit, out bool irrelevant, out string error)
    {
        unit = null;
        irrelevant = false;
        error = string.Empty;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("scope", out var scopeValue)
            || scopeValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(scopeValue.GetString()))
        {
            error = "claim index contains a record without a readable scope; exclusions are disabled for the incomplete index.";
            return false;
        }

        var scope = scopeValue.GetString()!;
        if (!scope.StartsWith("execution-unit:", StringComparison.Ordinal))
        {
            if (!ClaimCommand.TryValidateScope(scope, out _))
            {
                error = $"claim index contains unsupported scope `{scope}`; exclusions are disabled for the incomplete index.";
                return false;
            }
            irrelevant = true;
            return true;
        }

        var candidate = scope["execution-unit:".Length..];
        if (!TryValidateUnit(candidate, out var unitError))
        {
            error = $"claim index contains unsafe execution-unit scope `{scope}`: {unitError}; exclusions are disabled for the incomplete index.";
            return false;
        }

        unit = candidate;
        return true;
    }

    private bool TryReadPacketIdentity(string path, string executionUnit, out PacketIdentity identity, out string reason)
    {
        identity = new PacketIdentity(string.Empty, string.Empty);
        reason = "packet-identity-unavailable";
        if (!TryReadRegularFile(path, repoRoot, out var yaml, out _))
        {
            return false;
        }

        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            reason = "packet-identity-malformed";
            return false;
        }
        if (root is null)
        {
            reason = "packet-identity-malformed";
            return false;
        }

        var nestedMappings = FindValues(root, "implementation_issue_packet").ToArray();
        if (nestedMappings.Any(node => node is not YamlMappingNode))
        {
            reason = "packet-identity-malformed";
            return false;
        }

        if (!TryReadCanonicalScalar(root, nestedMappings.OfType<YamlMappingNode>(), "domain", out var packetDomain)
            || !TryReadCanonicalScalar(root, nestedMappings.OfType<YamlMappingNode>(), "target_repo", out var packetRepo)
            || !TryReadCanonicalScalar(root, nestedMappings.OfType<YamlMappingNode>(), "source_execution_unit", out var assertedUnit, allowMissing: true))
        {
            reason = "packet-identity-conflict-or-malformed";
            return false;
        }

        if (string.IsNullOrWhiteSpace(packetDomain) || string.IsNullOrWhiteSpace(packetRepo))
        {
            reason = "packet-identity-incomplete";
            return false;
        }
        if (!IsCanonicalRepo(packetRepo))
        {
            reason = "packet-repository-invalid";
            return false;
        }
        if (assertedUnit is not null && !string.Equals(assertedUnit, executionUnit, StringComparison.Ordinal))
        {
            reason = "packet-execution-unit-conflict";
            return false;
        }

        identity = new PacketIdentity(packetDomain, packetRepo);
        reason = string.Empty;
        return true;
    }

    private static bool TryReadCanonicalScalar(
        YamlMappingNode root,
        IEnumerable<YamlMappingNode> nested,
        string key,
        out string? value,
        bool allowMissing = false)
    {
        var values = FindValues(root, key).Concat(nested.SelectMany(mapping => FindValues(mapping, key))).ToArray();
        value = null;
        if (values.Length == 0)
        {
            return allowMissing;
        }
        if (values.Any(node => node is not YamlScalarNode scalar || scalar.Value is null))
        {
            return false;
        }
        var candidates = values.Cast<YamlScalarNode>().Select(scalar => scalar.Value!.Trim()).ToArray();
        if (candidates.Any(string.IsNullOrWhiteSpace)
            || candidates.Distinct(StringComparer.Ordinal).Count() != 1)
        {
            return false;
        }
        value = candidates[0];
        return true;
    }

    private static IEnumerable<YamlNode> FindValues(YamlMappingNode mapping, string key) =>
        mapping.Children
            .Where(pair => pair.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            .Select(pair => pair.Value);

    private string ResolvePacketPath(string executionUnit) =>
        Path.Combine(repoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");

    private UnitEvidence GetEvidence(string executionUnit)
    {
        if (!evidenceByUnit.TryGetValue(executionUnit, out var evidence))
        {
            evidence = new UnitEvidence();
            evidenceByUnit[executionUnit] = evidence;
        }
        return evidence;
    }

    private static bool TryValidateUnit(string? executionUnit, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "execution-unit is missing.";
            return false;
        }
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out error))
        {
            error = string.IsNullOrWhiteSpace(error) ? "execution-unit is not a safe canonical identifier." : error;
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsCanonicalRepo(string value) =>
        Regex.IsMatch(value, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant);

    private static bool TryReadNullableString(JsonElement root, string property, out string? result, out bool wasMissingOrNull)
    {
        result = null;
        wasMissingOrNull = true;
        if (!root.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        result = value.GetString();
        wasMissingOrNull = false;
        return true;
    }

    private static bool TryReadDate(JsonElement root, string property, out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(property, out var dateValue)
            && dateValue.ValueKind == JsonValueKind.String
            && dateValue.TryGetDateTimeOffset(out value)
            && value != default;
    }

    private static bool TryReadJsonFile(string path, string root, out JsonDocument document, out string error)
    {
        document = null!;
        if (!TryReadRegularFile(path, root, out var json, out error))
        {
            return false;
        }
        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException exception)
        {
            error = $"claim index record `{path}` is malformed: {exception.Message}; exclusions are disabled for the incomplete index.";
            return false;
        }
    }

    private static bool TryEnumerateDirectory(string path, string root, out string[] files, out string error)
    {
        files = [];
        if (!TryValidateEvidencePath(path, root, expectDirectory: true, out error))
        {
            return false;
        }
        try
        {
            files = Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(entry => entry, StringComparer.Ordinal)
                .ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"claim evidence directory `{path}` could not be enumerated: {exception.Message}; exclusions are disabled for the incomplete index.";
            return false;
        }
    }

    private static bool TryProbePath(
        string path,
        string root,
        out bool exists,
        out bool isDirectory,
        out string error)
    {
        exists = false;
        isDirectory = false;
        error = string.Empty;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            error = $"evidence path `{fullPath}` escapes its local evidence root.";
            return false;
        }

        var current = fullRoot;
        try
        {
            var rootAttributes = File.GetAttributes(current);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0 || (rootAttributes & FileAttributes.Directory) == 0)
            {
                error = $"evidence root `{current}` is not a regular local directory.";
                return false;
            }

            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                exists = true;
                isDirectory = true;
                return true;
            }

            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                var linkTarget = new FileInfo(current).LinkTarget ?? new DirectoryInfo(current).LinkTarget;
                if (linkTarget is not null)
                {
                    error = $"evidence path `{current}` is a symlink or reparse point; it cannot authorize historical exclusion.";
                    return false;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(current);
                }
                catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
                {
                    return true;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = $"evidence path `{current}` is a symlink or reparse point; it cannot authorize historical exclusion.";
                    return false;
                }
                var currentIsDirectory = (attributes & FileAttributes.Directory) != 0;
                var mustBeDirectory = index < parts.Length - 1;
                if (mustBeDirectory && !currentIsDirectory)
                {
                    error = $"evidence ancestor `{current}` is not a directory.";
                    return false;
                }
                if (index == parts.Length - 1)
                {
                    exists = true;
                    isDirectory = currentIsDirectory;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = $"evidence path `{fullPath}` could not be inspected: {exception.Message}";
            return false;
        }
    }

    private static bool TryReadRegularFile(string path, string root, out string content, out string error)
    {
        content = string.Empty;
        if (!TryValidateEvidencePath(path, root, expectDirectory: false, out error))
        {
            return false;
        }
        if (!CrossRuntimeReviewFileMode.TryReadRegularFileBytes(path, out var bytes, out var failure, out var readError))
        {
            error = $"evidence file `{path}` could not be read safely ({failure}): {readError}; it cannot authorize historical exclusion.";
            return false;
        }

        // Run logs and claim records use UTF-8. Remove a possible UTF-8 BOM,
        // matching File.ReadAllText while keeping the metadata-before-open
        // check that refuses empty files and Unix FIFOs.
        content = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        return true;
    }

    private static bool TryValidateEvidencePath(string path, string root, bool expectDirectory, out string error)
    {
        error = string.Empty;
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            error = $"evidence path `{fullPath}` escapes its local evidence root; it cannot authorize historical exclusion.";
            return false;
        }

        var current = fullRoot;
        try
        {
            var rootAttributes = File.GetAttributes(current);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0 || (rootAttributes & FileAttributes.Directory) == 0)
            {
                error = $"evidence root `{current}` is not a regular local directory; it cannot authorize historical exclusion.";
                return false;
            }

            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < parts.Length; index++)
            {
                current = Path.Combine(current, parts[index]);
                var attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    error = $"evidence path `{current}` is a symlink or reparse point; it cannot authorize historical exclusion.";
                    return false;
                }
                var shouldBeDirectory = index < parts.Length - 1 || expectDirectory;
                if (shouldBeDirectory != ((attributes & FileAttributes.Directory) != 0))
                {
                    error = $"evidence path `{current}` is not a regular {(shouldBeDirectory ? "directory" : "file")}; it cannot authorize historical exclusion.";
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            error = $"evidence path `{fullPath}` is inaccessible or missing: {exception.Message}";
            return false;
        }
    }

    private sealed class UnitEvidence
    {
        public List<WindowEvidence> Starts { get; } = [];
        public List<WindowEvidence> Closeouts { get; } = [];
        public List<ClaimTeamEvidence> ClaimTeams { get; } = [];
        public List<string> ClaimTeamPaths { get; } = [];
        public List<EvidenceProblem> Problems { get; } = [];
        public bool ClaimTeamMissing { get; set; }
    }

    private sealed record WindowEvidence(DateTimeOffset At, string Repo, string Kind, string Path);
    private sealed record ClaimTeamEvidence(string? Team, string Path);
    private sealed record EvidenceProblem(string Reason, string Detail, string Kind, string Path, DateTimeOffset? At);
    private sealed record PacketIdentity(string Domain, string Repo);
    private sealed record IssueIdentityParse(string? Repo, string? Problem, int? Number = null);
}

internal enum StalledWorkDebtWindowPolicy
{
    SoloFirstAdoption,
    ExplicitSince,
}

internal static class StalledWorkAdoptionDecisionStatus
{
    public const string Included = "included";
    public const string Historical = "historical";
    public const string Unknown = "retained-unknown";
    public const string ForeignTeam = "foreign-team";
    public const string Unavailable = "unavailable";
}

internal sealed record StalledWorkAdoptionDecision
{
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public required string Detail { get; init; }
    public string? EvidenceKind { get; init; }
    public string? EvidencePath { get; init; }
    public DateTimeOffset? EvidenceAt { get; init; }
    public DateTimeOffset? Cutoff { get; init; }
}

internal sealed record StalledWorkDebtWindowSummary
{
    [JsonPropertyName("policy")] public required string Policy { get; init; }
    [JsonPropertyName("cutoff")] public DateTimeOffset? Cutoff { get; init; }
    [JsonPropertyName("unavailable_reason")] public string? UnavailableReason { get; init; }
    [JsonPropertyName("resolved_domain")] public required string ResolvedDomain { get; init; }
    [JsonPropertyName("resolved_team")] public string? ResolvedTeam { get; init; }
    [JsonPropertyName("requested_team")] public string? RequestedTeam { get; init; }
    [JsonPropertyName("resolved_scope")] public required string ResolvedScope { get; init; }
    [JsonPropertyName("team_mode")] public required string TeamMode { get; init; }
    [JsonPropertyName("mode_source")] public required string ModeSource { get; init; }
    [JsonPropertyName("mode_path")] public required string ModePath { get; init; }
    [JsonPropertyName("legacy_run_log_path")] public required string LegacyRunLogPath { get; init; }
    [JsonPropertyName("knowledge_writeback_closeout_cutoff")] public DateTimeOffset? KnowledgeWritebackCloseoutCutoff { get; init; }
    [JsonPropertyName("guide_reachability_closeout_cutoff")] public DateTimeOffset? GuideReachabilityCloseoutCutoff { get; init; }
    [JsonPropertyName("decision_counts")] public required StalledWorkDebtWindowDecisionCounts DecisionCounts { get; init; }
}

internal sealed record StalledWorkDebtWindowDecisionCounts
{
    [JsonPropertyName("candidate_units")] public required int CandidateUnits { get; init; }
    [JsonPropertyName("included_units")] public required int IncludedUnits { get; init; }
    [JsonPropertyName("historical_units")] public required int HistoricalUnits { get; init; }
    [JsonPropertyName("unknown_units")] public required int UnknownUnits { get; init; }
    [JsonPropertyName("foreign_team_units")] public required int ForeignTeamUnits { get; init; }
}
