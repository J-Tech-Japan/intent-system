using System.Text.Json;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G857: reads the complete solo-conductor release proof from one already
/// refreshed canonical checkout. This type has no process or provider access;
/// ClaimCommand owns the isolated Git transaction and supplies its snapshot.
/// </summary>
internal static class SoloConductorClaimReleaseGate
{
    private const string PrMergedEvent = "pr-merged";
    private const string CloseoutRecordedEvent = "closeout-recorded";
    private const string KnowledgeRoot = ".intent-cli/knowledge-writebacks";
    private const string GuideRoot = ".intent-cli/guide-reachability";

    public static SoloConductorClaimReleaseGateResult Evaluate(
        string snapshotRoot,
        string executionUnit,
        string team,
        string snapshotOid,
        string canonicalTargetRef)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotRoot);
        ArgumentNullException.ThrowIfNull(executionUnit);
        ArgumentException.ThrowIfNullOrWhiteSpace(team);

        var root = Path.GetFullPath(snapshotRoot);
        var modePath = TeamModeStore.ResolvePath(root);
        var modePathState = InspectPath(root, modePath, expectDirectory: false);
        if (modePathState.Kind == PathKind.Unavailable)
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "team-mode-unavailable", modePathState.Detail!, modePath);
        }

        TeamModeState? modeState;
        try
        {
            modeState = TeamModeStore.TryRead(root);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "team-mode-unavailable", exception.Message, modePath);
        }

        if (modeState is null || !modeState.Entries.Any(entry => TeamMode.IsSoloConductor(entry.Mode)))
        {
            return SoloConductorClaimReleaseGateResult.NotApplicable();
        }

        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(executionUnit, out var unitError))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "execution-unit-unavailable", unitError, modePath);
        }

        var packetRelative = GuideReachabilityRecord.ResolvePacketPath(root, executionUnit);
        var packetPathState = InspectPath(root, packetRelative, expectDirectory: false);
        if (packetPathState.Kind != PathKind.Present)
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                packetPathState.Kind == PathKind.Missing ? "packet-domain-missing" : "packet-unavailable",
                packetPathState.Detail ?? "Canonical packet.yaml is missing; a solo mode entry exists, so domain cannot be guessed.",
                packetRelative);
        }

        string packetYaml;
        PacketYamlDocument? packet;
        try
        {
            packetYaml = GuardedFileRead.ReadAllText(packetRelative);
            if (!PacketYamlDocument.TryParse(packetYaml, out packet, out var parseError))
            {
                return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                    "packet-unavailable", parseError, packetRelative);
            }
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-unavailable", exception.Message, packetRelative);
        }

        if (!TryReadPacketIdentityAssertions(packetYaml, out var packetIdentity, out var identityParseError))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-unavailable", identityParseError!, packetRelative);
        }
        if (packetIdentity!.Domain.Error is not null)
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-identity-conflict", packetIdentity.Domain.Error, packetRelative);
        }
        var domain = packetIdentity.Domain.Value;
        if (!IsSafeIdentifier(domain))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-domain-missing", "The canonical packet has no valid explicit domain; invoking defaults are not authority.",
                packetRelative);
        }

        if (packetIdentity.SourceExecutionUnit.Error is not null)
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-identity-conflict", packetIdentity.SourceExecutionUnit.Error, packetRelative, domain);
        }
        if (packetIdentity.SourceExecutionUnit.Present
            && !string.Equals(packetIdentity.SourceExecutionUnit.Value, executionUnit, StringComparison.Ordinal))
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "packet-identity-conflict",
                $"Packet source_execution_unit '{packetIdentity.SourceExecutionUnit.Value}' does not match held scope unit '{executionUnit}'.",
                packetRelative, domain);
        }

        TeamModeResolution resolution;
        try
        {
            resolution = TeamModeStore.Resolve(modeState, domain!, team);
        }
        catch (TeamModeResolutionException exception)
        {
            return ApplicabilityRefusal(root, executionUnit, team, snapshotOid, canonicalTargetRef,
                "team-mode-ambiguous", exception.Message, modePath, domain);
        }

        if (!resolution.IsSoloConductor || resolution.Source != TeamModeSource.Recorded)
        {
            return SoloConductorClaimReleaseGateResult.NotApplicable();
        }

        var modeSource = resolution.Entry?.Team is null ? "domain" : "team";
        if (packetIdentity.TargetRepo.Error is not null)
        {
            var identityDuty = ApplicabilityDuty(root, "target-repo", "target-repo-identity-conflict",
                packetIdentity.TargetRepo.Error, packetRelative, canonicalTargetRef,
                "The packet contains malformed or conflicting actual target_repo assertions; correct the canonical packet before release.");
            return ApplicableResult(executionUnit, team, domain!, snapshotOid, canonicalTargetRef,
                "recorded-solo-conductor", "The current held team resolves to a recorded solo-conductor mode.",
                resolution.Entry?.Team is null ? "domain" : "team", [Relative(root, modePath), Relative(root, packetRelative)], [identityDuty], null, null);
        }
        var targetRepo = packetIdentity.TargetRepo.Value;
        if (!IsSafeRepo(targetRepo))
        {
            var duty = ApplicabilityDuty(root, "target-repo", "target-repo-unavailable",
                "Solo-conductor applicability is recorded, but packet target_repo is missing or invalid.",
                packetRelative, canonicalTargetRef,
                repairUnavailable: "The packet does not name one safe owner/repo identity; repair the canonical packet declaration before release.");
            return ApplicableResult(executionUnit, team, domain!, snapshotOid, canonicalTargetRef,
                "recorded-solo-conductor", "The current held team resolves to a recorded solo-conductor mode.",
                modeSource, [Relative(root, modePath), Relative(root, packetRelative)], [duty], targetRepo, null);
        }

        return EvaluateDuties(root, executionUnit, team, domain!, targetRepo!, snapshotOid,
            canonicalTargetRef, modeSource, modePath, packetRelative, packetYaml);
    }

    private static SoloConductorClaimReleaseGateResult EvaluateDuties(
        string root,
        string unit,
        string team,
        string domain,
        string targetRepo,
        string snapshotOid,
        string targetRef,
        string modeSource,
        string modePath,
        string packetPath,
        string packetYaml)
    {
        var duties = new List<SoloConductorClaimReleaseDuty>();
        var evidencePaths = new HashSet<string>(StringComparer.Ordinal)
        {
            Relative(root, modePath),
            Relative(root, packetPath),
        };
        var target = targetRef;
        var scopedQueuePath = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, domain, targetRepo);
        var scopedQueueInspection = InspectPath(root, scopedQueuePath, expectDirectory: false);
        StateLocation queueLocation;
        if (scopedQueueInspection.Kind != PathKind.Missing)
        {
            queueLocation = new StateLocation(scopedQueuePath, StateLocationKind.Scoped);
        }
        else
        {
            var legacyQueuePath = RuntimeScopedStateResolver.GetLegacyQueueStatePath(root);
            var legacyQueueInspection = InspectPath(root, legacyQueuePath, expectDirectory: false);
            queueLocation = legacyQueueInspection.Kind == PathKind.Missing
                ? new StateLocation(scopedQueuePath, StateLocationKind.MissingPreferScoped)
                : new StateLocation(legacyQueuePath, StateLocationKind.Legacy);
        }
        var queuePath = Path.GetFullPath(queueLocation.Path);
        var runsPath = queueLocation.Kind == StateLocationKind.Legacy
            ? RuntimeScopedStateResolver.GetLegacyRunLogPath(root)
            : RuntimeScopedStateResolver.GetScopedRunLogPath(root, domain, targetRepo);
        runsPath = Path.GetFullPath(runsPath);
        var queueResult = ReadQueue(root, unit, targetRepo, queuePath);
        evidencePaths.Add(Relative(root, queuePath));

        int? currentPr = queueResult.PullRequest;
        var completedIdentityMissing = queueResult.Cause == "linked-pr-missing"
            && queueResult.Item?.State == QueueItemState.Completed;
        var canDoctorRepair = completedIdentityMissing && queueLocation.Kind == StateLocationKind.Legacy;
        var canOfferCloseout = queueResult.State == "missing" && !completedIdentityMissing;
        var queueCloseoutPr = currentPr?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<actual-merged-pr>";
        var closeoutCommand = canOfferCloseout
            ? $"intent-cli closeout pr --pr {queueCloseoutPr} --repo {targetRepo} --domain {domain} --pr-merged true --write --format json"
            : null;
        var doctorBaseCommand = $"intent-cli automation state-doctor --workdir <canonical-host-checkout> --repo {targetRepo} --domain {domain} --team {team}";
        IReadOnlyList<string> doctorCommands =
        [
            doctorBaseCommand + " --read-only --format json",
            doctorBaseCommand + " --write --format json",
        ];
        var stateDoctorPublicationDetail = $"The state-doctor is host-wide, not unit-scoped, and may propose repairs for other queue rows too. Review the full read-only preview and confirm this item has unique merged-PR closing-issue evidence before --write. If no unique finding appears, resolve ambiguity through the responsible host/architect workflow. After a successful write, stage only the exact owned .intent-cli/queue-state.json and .intent-cli/runs.jsonl changes, publish them to {target}, then retry release.";
        var queueDetail = canDoctorRepair
            ? queueResult.Detail + " " + stateDoctorPublicationDetail
            : canOfferCloseout
                ? queueResult.Detail + " Run the command only after the normal host workflow independently confirms the actual PR is merged."
                : queueResult.Detail;
        var queueRepairUnavailableReason = queueResult.State == "unavailable"
            ? BuildQueueRepairUnavailableReason(root, queuePath, queueResult.RepairUnavailableReason ?? queueResult.Detail, target)
            : completedIdentityMissing && !canDoctorRepair
                ? BuildScopedCompletedQueueIdentityRepairUnavailableReason(root, queuePath, queueResult.Detail, target)
                : queueResult.RepairUnavailableReason;
        IReadOnlyList<string> queueRecoveryCommands = canDoctorRepair
            ? doctorCommands
            : closeoutCommand is not null ? [closeoutCommand] : [];
        var queuePublicationStep = canDoctorRepair || closeoutCommand is not null
            ? PublicationStep(target)
            : null;
        duties.Add(BuildDuty(
            "closeout-queue",
            queueResult.State,
            queueResult.Cause,
            queueDetail,
            queueResult.Path is null ? [] : [Evidence(root, queueResult.Path, queueResult.Item)],
            queueRecoveryCommands,
            queuePublicationStep,
            queueRepairUnavailableReason));

        var runsRead = ReadRuns(root, unit, targetRepo, currentPr, runsPath);
        evidencePaths.Add(Relative(root, runsPath));
        foreach (var eventName in new[] { PrMergedEvent, CloseoutRecordedEvent })
        {
            var found = runsRead.Events?.Any(item => item.Event == eventName) == true;
            var state = runsRead.State == "unavailable"
                ? "unavailable"
                : currentPr is null
                    ? "unavailable"
                    : found ? "satisfied" : "missing";
            var cause = runsRead.State == "unavailable"
                ? runsRead.Cause
                : currentPr is null
                    ? "current-pr-unavailable"
                    : found ? "canonical-run-receipt-present" : "current-closeout-run-missing";
            var detail = runsRead.State == "unavailable"
                ? runsRead.Detail
                : currentPr is null
                    ? canDoctorRepair
                        ? $"A current linked PR identity is required before matching run receipts can be evaluated. {stateDoctorPublicationDetail}"
                        : completedIdentityMissing
                            ? $"A current linked PR identity cannot be established because the completed canonical queue item lacks linked_pr. {queueRepairUnavailableReason}"
                            : $"A current linked PR identity is required before matching run receipts can be evaluated; resolve it through the closeout-queue recovery command and publish the canonical queue/runs artifacts to {target}."
                    : found
                        ? $"Canonical {eventName} event matches unit '{unit}', repo '{targetRepo}', and PR #{currentPr}."
                        : $"No canonical {eventName} event matches unit '{unit}', repo '{targetRepo}', and PR #{currentPr}.";
            var repairFlag = queueResult.State == "satisfied" ? " --repair-runs" : string.Empty;
            var command = currentPr is null ? null
                : $"intent-cli closeout pr --pr {currentPr} --repo {targetRepo} --domain {domain} --pr-merged true{repairFlag} --write --format json";
            var runRepairUnavailableReason = runsRead.State == "unavailable"
                ? BuildRunLogRepairUnavailableReason(root, runsPath, target)
                : queueResult.State == "unavailable"
                    ? BuildQueueRepairUnavailableReason(root, queuePath, queueResult.RepairUnavailableReason ?? queueResult.Detail, target)
                : completedIdentityMissing && !canDoctorRepair
                    ? queueRepairUnavailableReason
                : canDoctorRepair
                    ? null
                : currentPr is null
                    ? $"Run evidence cannot be matched until the queue owner resolves the actual PR identity. Use the closeout-queue recovery command with the actual merged PR, publish the canonical queue/runs artifacts to {target}, then retry; do not infer a PR from missing evidence."
                    : null;
            IReadOnlyList<string> runRecoveryCommands = runsRead.State != "unavailable" && currentPr is null && canDoctorRepair
                ? doctorCommands
                : command is not null && state == "missing" ? [command] : [];
            var runPublicationStep = runsRead.State != "unavailable" && currentPr is null && canDoctorRepair
                ? PublicationStep(target)
                : command is not null && state == "missing" ? PublicationStep(target) : null;
            duties.Add(BuildDuty(
                eventName,
                state,
                cause,
                detail,
                runsRead.Path is null ? [] : [Evidence(root, runsRead.Path)],
                runRecoveryCommands,
                runPublicationStep,
                runRepairUnavailableReason));
        }

        KnowledgeWriteBackDeclaration? knowledge = null;
        GuideReachabilityDeclaration? guide = null;
        string? knowledgeError = null;
        string? guideError = null;
        try
        {
            ValidateKnowledgeDeclarationShapes(packetYaml);
            knowledge = KnowledgeWriteBackDeclaration.Read(packetYaml);
            knowledgeError = null;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            knowledgeError = exception.Message;
        }
        try
        {
            ValidateGuideDeclarationShapes(packetYaml);
            guide = GuideReachabilityDeclaration.Read(packetYaml);
            guideError = null;
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            guideError = exception.Message;
        }

        var explicitKnowledge = HasExplicitKnowledgeDeclaration(packetYaml);
        if (knowledgeError is not null)
        {
            duties.Add(BuildDuty("knowledge-declaration", "unavailable", "knowledge-declaration-unavailable",
                knowledgeError, [Evidence(root, packetPath)], [], null,
                "The packet declaration is malformed or unreadable; correct it through the architect packet workflow."));
        }
        else if (knowledge!.IsRequired)
        {

            var read = ReadKnowledgeReceipts(root, unit);
            foreach (var role in new[] { CloseoutRecordRole.Architect, CloseoutRecordRole.Orchestrator })
            {
                var receipt = read.Records.FirstOrDefault(row => row.Record.Role == role)?.Record;
                var state = read.State == "unavailable" ? "unavailable"
                    : read.DuplicateRoles?.Contains(role, StringComparer.Ordinal) == true ? "unavailable"
                    : receipt is null ? "missing" : "satisfied";
                var cause = read.State == "unavailable" ? "knowledge-record-unavailable"
                    : read.DuplicateRoles?.Contains(role, StringComparer.Ordinal) == true ? "duplicate-role-record"
                    : receipt is null ? "attributed-knowledge-record-missing" : "attributed-knowledge-record-present";
                var detail = state switch
                {
                    "satisfied" => $"One canonical knowledge record is attributed to {role}; targets remain optional.",
                    "unavailable" => read.Detail ?? $"Multiple canonical knowledge records normalize to role '{role}'.",
                    _ => $"No canonical knowledge record is attributed to {role}; legacy null attribution does not satisfy this duty.",
                };
                var command = $"intent-cli automation knowledge-writeback-record --execution-unit {unit} --role {role} --commit <actual-host-commit> --write --format json";
                duties.Add(BuildDuty(
                    $"knowledge-{role}", state, cause, detail,
                    read.Records.Select(row => Evidence(root, row.Path, row.Record))
                        .Concat(read.Evidence.Where(path => read.Records.All(row => !string.Equals(row.Path, path, StringComparison.Ordinal)))
                            .Select(path => Evidence(root, path)))
                        .ToArray(),
                    state == "missing" ? [command] : [],
                    state == "missing" ? PublicationStep(target) : null,
                    state == "unavailable" ? read.RepairUnavailableReason ?? "Existing immutable or duplicate evidence cannot be overwritten by the canonical writer." : null,
                    knowledge.RequiredFacets,
                    knowledge.DeclaredTargets));
            }
        }
        else if (explicitKnowledge)
        {
            duties.Add(NotApplicableDuty("knowledge-writeback", "explicit-no-required-duty",
                "Packet explicitly declares no required knowledge write-back duty.", Evidence(root, packetPath),
                knowledge!.RequiredFacets, knowledge.DeclaredTargets));
        }
        else
        {
            duties.Add(BuildDuty("knowledge-declaration", "missing", "knowledge-declaration-missing",
                "Solo-conductor packets require an explicit knowledge declaration; an absent declaration is not a no-op.",
                [Evidence(root, packetPath)], [], null,
                "Return to architect packet authoring and validation to make an explicit required or not-required declaration."));
        }

        if (guideError is not null)
        {
            duties.Add(BuildDuty("guide-declaration", "unavailable", "guide-declaration-unavailable",
                guideError, [Evidence(root, packetPath)], [], null,
                "The packet declaration is malformed or unreadable; correct it through the architect packet workflow."));
        }
        else if (!guide!.IsDeclared)
        {
            duties.Add(BuildDuty("guide-declaration", "missing", "guide-declaration-missing",
                "Solo-conductor packets require an explicit guide route or explicit no-role-facing-surface declaration.",
                [Evidence(root, packetPath)], [], null,
                "Return to architect packet authoring and validation to declare guide routes or no_role_facing_surface: true."));
        }
        else if (guide.NoRoleFacingSurface)
        {
            duties.Add(NotApplicableDuty("guide-reachability", "explicit-no-role-facing-surface",
                "The packet explicitly declares no role-facing guide surface; the canonical writer has no-op behavior.",
                Evidence(root, packetPath), null, null));
        }
        else
        {

            var read = ReadGuideReceipts(root, unit);
            var receipt = read.Records.FirstOrDefault(row => row.Record.Role == CloseoutRecordRole.Architect)?.Record;
        var state = read.State == "unavailable" ? "unavailable"
            : read.DuplicateArchitect ? "unavailable"
            : receipt is null ? "missing" : "satisfied";
            var cause = read.State == "unavailable" ? "guide-record-unavailable"
                : read.DuplicateArchitect ? "duplicate-role-record"
                : receipt is null ? "architect-guide-record-missing" : "architect-guide-record-present";
            var detail = state switch
            {
                "satisfied" => "One canonical guide-reachability record is attributed to architect; recipient roles are provenance only.",
                "unavailable" => read.Detail ?? "Canonical guide evidence is unreadable or duplicated.",
                _ => "No canonical guide-reachability record is attributed to architect; record.roles does not substitute for recorder attribution.",
            };
            var routeSummaries = guide.Routes.Select(route => new SoloConductorClaimReleaseRoute
            {
                GuideSurface = route.GuideSurface,
                Role = route.Role,
                TargetSurface = route.TargetSurface,
            }).ToArray();
            var command = $"intent-cli automation guide-reachability-record --execution-unit {unit} --role architect --commit <actual-host-commit> --write --format json";
            duties.Add(BuildDuty("guide-reachability", state, cause, detail,
                read.Records.Select(row => Evidence(root, row.Path, row.Record))
                    .Concat(read.Evidence.Where(path => read.Records.All(row => !string.Equals(row.Path, path, StringComparison.Ordinal)))
                        .Select(path => Evidence(root, path)))
                    .ToArray(),
                state == "missing" ? [command] : [],
                state == "missing" ? PublicationStep(target) : null,
                state == "unavailable" ? read.RepairUnavailableReason ?? "Existing immutable or duplicate evidence cannot be overwritten by the canonical writer." : null,
                declaredRoutes: routeSummaries));
        }

        foreach (var duty in duties)
        {
            foreach (var artifact in duty.Evidence)
            {
                evidencePaths.Add(artifact.Path);
            }
        }

        var decision = duties.All(duty => duty.State is "satisfied" or "not-applicable") ? "satisfied" : "refused";
        var completion = new SoloConductorClaimReleaseCompletion
        {
            Decision = decision,
            ExecutionUnit = unit,
            Domain = domain,
            Team = team,
            TargetRepo = targetRepo,
            LinkedPr = currentPr,
            CanonicalSnapshotOid = snapshotOid,
            CanonicalTargetRef = target,
            Applicability = new SoloConductorClaimReleaseApplicability
            {
                State = "applicable",
                Cause = "recorded-solo-conductor",
                Detail = "The current held team resolves to a recorded solo-conductor mode for the packet domain.",
                Mode = TeamMode.SoloConductor,
                ModeSource = modeSource,
                EvidencePaths = evidencePaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            },
            Duties = duties,
        };
        return new SoloConductorClaimReleaseGateResult(true, completion)
        {
            RunLogRelativePath = Relative(root, runsPath),
        };
    }

    private static QueueReadResult ReadQueue(string root, string unit, string repo, string path)
    {
        var inspect = InspectPath(root, path, expectDirectory: false);
        if (inspect.Kind == PathKind.Missing)
        {
            return new QueueReadResult("missing", "queue-item-missing", "No canonical queue item exists for this execution unit.", path, null, null, null);
        }
        if (inspect.Kind == PathKind.Unavailable)
        {
            return new QueueReadResult("unavailable", "queue-unavailable", inspect.Detail!, path, null, null,
                "Canonical queue state is unreadable or follows a symlink; repair the canonical queue through its supported queue command.");
        }

        QueueState state;
        string queueJson;
        try
        {
            queueJson = GuardedFileRead.ReadAllText(path);
            state = QueueStateSerializer.Deserialize(queueJson);
            if (state.Items is null || state.Items.Any(item => item is null))
                throw new InvalidOperationException("Queue state items is null or contains a null entry.");
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new QueueReadResult("unavailable", "queue-unavailable", exception.Message, path, null, null,
                "Canonical queue state is malformed or unreadable; repair it through the supported queue workflow.");
        }

        var rows = state.Items.Where(item => string.Equals(item.ExecutionUnit, unit, StringComparison.Ordinal)).ToArray();
        if (rows.Length == 0)
        {
            return new QueueReadResult("missing", "queue-item-missing", $"Canonical queue state has no item for '{unit}'.", path, null, null, null);
        }
        if (rows.Length != 1)
        {
            return new QueueReadResult("unavailable", "duplicate-queue-item", $"Canonical queue state has {rows.Length} entries for '{unit}'; exactly one is required.", path, null, null,
                "Duplicate canonical queue identity cannot be resolved safely by release; repair through the queue owner workflow.");
        }

        var item = rows[0];
        if (!ValidateRawLinkedPrObject(queueJson, unit, repo, item, out var rawPrError))
        {
            return new QueueReadResult("unavailable", "queue-pr-identity-conflict", rawPrError!, path, item, null,
                "The structured linked_pr object contains contradictory repository or PR fields; correct it through the supported queue/closeout workflow.");
        }
        if (item.LinkedIssue is { Repo.Length: > 0 } issue
            && !string.Equals(issue.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            return new QueueReadResult("unavailable", "queue-repo-conflict",
                $"Queue linked_issue repo '{issue.Repo}' conflicts with packet target_repo '{repo}'.", path, item, null,
                "The canonical queue contains contradictory linked issue ownership; the responsible host/queue owner must correct this artifact.");
        }
        if (item.LinkedIssue?.Url is { Length: > 0 } issueUrl)
        {
            if (!TryReadGitHubIdentity(issueUrl, "issues", out var issueRepo, out var issueNumber))
            {
                return new QueueReadResult("unavailable", "queue-issue-identity-invalid",
                    $"Queue linked_issue URL '{issueUrl}' is not a canonical GitHub issue identity.", path, item, null,
                    "The canonical queue contains a malformed linked issue identity; the responsible host/queue owner must correct this artifact.");
            }
            if (!string.Equals(issueRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                return new QueueReadResult("unavailable", "queue-issue-repo-conflict",
                    $"Queue linked_issue URL repo '{issueRepo}' conflicts with packet target_repo '{repo}'.", path, item, null,
                    "The canonical queue contains contradictory repository ownership; the responsible host/queue owner must correct this artifact.");
            }
            if (item.LinkedIssue.Number is not null && item.LinkedIssue.Number != issueNumber)
            {
                return new QueueReadResult("unavailable", "queue-issue-identity-conflict",
                    $"Queue linked_issue number {item.LinkedIssue.Number} conflicts with URL issue #{issueNumber}.", path, item, null,
                    "The canonical queue contains contradictory linked issue numbers; the responsible host/queue owner must correct this artifact.");
            }
        }

        var pr = ParseQueuePr(item, repo, out var prError);
        if (prError is not null)
        {
            return new QueueReadResult("unavailable", "queue-pr-identity-conflict", prError, path, item, null,
                "The queue PR identity is malformed or contradictory; correct it through the supported queue/closeout workflow.");
        }

        if (item.State != QueueItemState.Completed)
        {
            return new QueueReadResult("missing", "queue-not-completed",
                $"Canonical queue item state is '{item.State}', not Completed.", path, item, pr, null);
        }
        if (pr is null)
        {
            return new QueueReadResult("missing", "linked-pr-missing",
                "The completed queue item does not identify one PR for the packet target repository.", path, item, null, null);
        }
        return new QueueReadResult("satisfied", "completed-queue-item-present",
            $"Exactly one completed queue item identifies '{unit}' and PR #{pr} in '{repo}'.", path, item, pr, null);
    }

    private static int? ParseQueuePr(QueueItem item, string repo, out string? error)
    {
        error = null;
        var raw = item.LinkedPr?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            if (!TryReadGitHubIdentity(raw, "pull", out var linkedRepo, out var number))
            {
                error = $"Queue linked_pr '{raw}' is not a canonical GitHub pull-request URL.";
                return null;
            }
            if (!string.Equals(linkedRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Queue linked_pr repo '{linkedRepo}' conflicts with packet target_repo '{repo}'.";
                return null;
            }
            return number;
        }

        if (!int.TryParse(raw, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var legacyNumber) || legacyNumber <= 0)
        {
            error = $"Queue linked_pr '{raw}' is neither a GitHub pull-request URL nor a positive legacy number.";
            return null;
        }
        if (item.LinkedIssue is not { Repo.Length: > 0 } linkedIssue
            || !string.Equals(linkedIssue.Repo, repo, StringComparison.OrdinalIgnoreCase))
        {
            error = "Legacy numeric linked_pr is ambiguous without a linked_issue naming the packet target repo.";
            return null;
        }
        return legacyNumber;
    }

    private static bool ValidateRawLinkedPrObject(string json, string unit, string targetRepo, QueueItem item, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return true;
            var rawItem = items.EnumerateArray().FirstOrDefault(element =>
                element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("execution_unit", out var executionUnit)
                && executionUnit.ValueKind == JsonValueKind.String
                && string.Equals(executionUnit.GetString(), unit, StringComparison.Ordinal));
            if (rawItem.ValueKind != JsonValueKind.Object
                || !rawItem.TryGetProperty("linked_pr", out var linkedPr)
                || linkedPr.ValueKind != JsonValueKind.Object)
                return true;

            string? objectRepo = null;
            int? objectNumber = null;
            string? objectUrl = null;
            if (linkedPr.TryGetProperty("repo", out var repoElement) && repoElement.ValueKind != JsonValueKind.Null)
            {
                if (repoElement.ValueKind != JsonValueKind.String || !IsSafeRepo(repoElement.GetString()))
                {
                    error = "Structured linked_pr.repo is not a safe owner/repo identity.";
                    return false;
                }
                objectRepo = repoElement.GetString();
                if (!string.Equals(objectRepo, targetRepo, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Structured linked_pr.repo '{objectRepo}' conflicts with packet target_repo '{targetRepo}'.";
                    return false;
                }
            }
            if (linkedPr.TryGetProperty("number", out var numberElement) && numberElement.ValueKind != JsonValueKind.Null)
            {
                if (numberElement.ValueKind != JsonValueKind.Number
                    || !numberElement.TryGetInt32(out var parsedNumber)
                    || parsedNumber <= 0)
                {
                    error = "Structured linked_pr.number is not a positive integer.";
                    return false;
                }
                objectNumber = parsedNumber;
            }
            if (linkedPr.TryGetProperty("url", out var urlElement) && urlElement.ValueKind != JsonValueKind.Null)
            {
                if (urlElement.ValueKind != JsonValueKind.String)
                {
                    error = "Structured linked_pr.url is not a string.";
                    return false;
                }
                objectUrl = urlElement.GetString();
            }
            if (!string.IsNullOrWhiteSpace(objectUrl))
            {
                if (!TryReadGitHubIdentity(objectUrl, "pull", out var urlRepo, out var urlNumber))
                {
                    error = $"Structured linked_pr.url '{objectUrl}' is not a canonical GitHub pull-request identity.";
                    return false;
                }
                if (objectRepo is not null && !string.Equals(objectRepo, urlRepo, StringComparison.OrdinalIgnoreCase)
                    || objectNumber is not null && objectNumber != urlNumber)
                {
                    error = "Structured linked_pr.repo/number conflicts with its URL identity.";
                    return false;
                }
            }
            else if (objectRepo is not null || objectNumber is not null)
            {
                error = "Structured linked_pr has repo/number fields but no URL; the canonical queue serializer cannot retain a complete PR identity.";
                return false;
            }
            return true;
        }
        catch (JsonException exception)
        {
            error = "Queue state JSON could not be parsed while checking structured PR identity: " + exception.Message;
            return false;
        }
    }

    private static RunsReadResult ReadRuns(string root, string unit, string repo, int? pr, string path)
    {
        var inspect = InspectPath(root, path, expectDirectory: false);
        if (inspect.Kind == PathKind.Missing)
        {
            return new RunsReadResult("missing", "run-log-missing", "Canonical runs log is absent.", path, []);
        }
        if (inspect.Kind == PathKind.Unavailable)
        {
            return new RunsReadResult("unavailable", "run-log-unavailable", inspect.Detail!, path, null);
        }

        IReadOnlyList<RunEvent> events;
        try { events = RunLogSerializer.DeserializeAll(GuardedFileRead.ReadAllText(path)); }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new RunsReadResult("unavailable", "run-log-unavailable", exception.Message, path, null);
        }

        if (pr is null)
        {
            return new RunsReadResult("missing", "current-pr-unavailable", "Cannot match run receipts without a current queue PR identity.", path, []);
        }
        var targetEvents = events.Where(run => string.Equals(run.ExecutionUnit, unit, StringComparison.Ordinal)
            && (run.Event == PrMergedEvent || run.Event == CloseoutRecordedEvent)).ToArray();
        foreach (var run in targetEvents)
        {
            if (TryRunIdentity(run, out var runRepo, out var runPr, out var identityError)
                && identityError is null)
            {
                continue;
            }
            if (identityError is not null)
            {
                return new RunsReadResult("unavailable", "run-identity-conflict", identityError, path, null);
            }
        }

        var matching = targetEvents.Where(run =>
            TryRunIdentity(run, out var runRepo, out var runPr, out var error)
            && error is null
            && string.Equals(runRepo, repo, StringComparison.OrdinalIgnoreCase)
            && runPr == pr).ToArray();
        return new RunsReadResult("present", "run-log-readable",
            "Canonical runs log was parsed; matching current-PR events were selected by unit/repo/PR.", path, matching);
    }

    private static bool TryRunIdentity(RunEvent run, out string? repo, out int? pr, out string? error)
    {
        repo = null;
        pr = null;
        error = null;
        if (!string.IsNullOrWhiteSpace(run.Repo))
        {
            if (!IsSafeRepo(run.Repo)) error = $"Run event '{run.Event}' carries invalid repo '{run.Repo}'.";
            else repo = run.Repo;
        }
        if (run.Pr is not null)
        {
            if (run.Pr <= 0) error = $"Run event '{run.Event}' carries invalid PR number '{run.Pr}'.";
            else pr = run.Pr;
        }
        if (!string.IsNullOrWhiteSpace(run.LinkedPr))
        {
            if (!TryReadRunReference(run.LinkedPr, "pull", out var linkedRepo, out var linkedPr))
            {
                error = $"Run event '{run.Event}' carries invalid linked_pr '{run.LinkedPr}'.";
                return false;
            }
            if (linkedRepo is not null && repo is not null
                && !string.Equals(repo, linkedRepo, StringComparison.OrdinalIgnoreCase)
                || pr is not null && pr != linkedPr)
            {
                error = $"Run event '{run.Event}' has contradictory repo/PR identity fields.";
                return false;
            }
            repo ??= linkedRepo;
            pr ??= linkedPr;
        }
        if (!string.IsNullOrWhiteSpace(run.LinkedIssue))
        {
            if (!TryReadRunReference(run.LinkedIssue, "issues", out var issueRepo, out _))
            {
                error = $"Run event '{run.Event}' carries invalid linked_issue '{run.LinkedIssue}'.";
                return false;
            }
            if (issueRepo is not null && repo is not null
                && !string.Equals(repo, issueRepo, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Run event '{run.Event}' has contradictory repo and linked_issue identity fields.";
                return false;
            }
            repo ??= issueRepo;
            // An issue number is independent of a pull-request number.
        }
        return repo is not null && pr is not null;
    }

    private static bool TryReadRunReference(string value, string kind, out string? repo, out int number)
    {
        if (TryReadGitHubIdentity(value, kind, out var urlRepo, out number))
        {
            repo = urlRepo;
            return true;
        }

        repo = null;
        return TryReadLegacyRunNumber(value, out number);
    }

    private static bool TryReadLegacyRunNumber(string value, out int number)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('#')) trimmed = trimmed[1..];
        return int.TryParse(trimmed, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static KnowledgeReadResult ReadKnowledgeReceipts(string root, string unit)
    {
        var result = new List<KnowledgeReceipt>();
        var evidence = new List<string>();
        var seenRoles = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var baseDir = Path.GetFullPath(Path.Combine(root, KnowledgeRoot, unit));
            var directoryState = InspectPath(root, baseDir, expectDirectory: true);
            if (directoryState.Kind == PathKind.Unavailable)
                return new KnowledgeReadResult("unavailable", directoryState.Detail, [], [baseDir], []);
            var roleDirectory = Path.GetFullPath(Path.Combine(baseDir, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName));
            var roleDirectoryState = InspectPath(root, roleDirectory, expectDirectory: true);
            if (roleDirectoryState.Kind == PathKind.Unavailable)
                return new KnowledgeReadResult("unavailable", roleDirectoryState.Detail, [], [roleDirectory], []);
            var legacyPath = Path.Combine(baseDir, "record.json");
            var legacyState = InspectPath(root, legacyPath, expectDirectory: false);
            if (legacyState.Kind == PathKind.Unavailable)
                return new KnowledgeReadResult("unavailable", legacyState.Detail, [], [legacyPath], []);
            var roleEntries = InspectRecordEntries(root, roleDirectory);
            if (roleEntries.Kind == PathKind.Unavailable)
                return new KnowledgeReadResult("unavailable", roleEntries.Detail, [], [roleDirectory], []);
            var paths = RoleScopedCloseoutRecordStore.EnumerateExistingPaths(root, KnowledgeRoot, unit);
            foreach (var path in paths)
            {
                evidence.Add(Path.GetFullPath(path));
                var inspect = InspectPath(root, path, expectDirectory: false);
                if (inspect.Kind != PathKind.Present)
                    return new KnowledgeReadResult("unavailable", inspect.Detail ?? "Enumerated knowledge evidence path is unavailable.", [], evidence, []);
                var record = KnowledgeWriteBackRecord.Deserialize(GuardedFileRead.ReadAllText(path), unit);
                result.Add(new KnowledgeReceipt(Path.GetFullPath(path), record));
                if (record.Role is not null && !seenRoles.Add(record.Role))
                    duplicates.Add(record.Role);
            }
            return new KnowledgeReadResult("read", null, result, evidence, duplicates.ToArray());
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new KnowledgeReadResult("unavailable", exception.Message, [], evidence, []);
        }
    }

    private static GuideReadResult ReadGuideReceipts(string root, string unit)
    {
        var result = new List<GuideReceipt>();
        var evidence = new List<string>();
        var architectCount = 0;
        try
        {
            var baseDir = Path.GetFullPath(Path.Combine(root, GuideRoot, unit));
            var directoryState = InspectPath(root, baseDir, expectDirectory: true);
            if (directoryState.Kind == PathKind.Unavailable)
                return new GuideReadResult("unavailable", directoryState.Detail, [], [baseDir], false);
            var roleDirectory = Path.GetFullPath(Path.Combine(baseDir, RoleScopedCloseoutRecordStore.RoleRecordsDirectoryName));
            var roleDirectoryState = InspectPath(root, roleDirectory, expectDirectory: true);
            if (roleDirectoryState.Kind == PathKind.Unavailable)
                return new GuideReadResult("unavailable", roleDirectoryState.Detail, [], [roleDirectory], false);
            var legacyPath = Path.Combine(baseDir, "record.json");
            var legacyState = InspectPath(root, legacyPath, expectDirectory: false);
            if (legacyState.Kind == PathKind.Unavailable)
                return new GuideReadResult("unavailable", legacyState.Detail, [], [legacyPath], false);
            var roleEntries = InspectRecordEntries(root, roleDirectory);
            if (roleEntries.Kind == PathKind.Unavailable)
                return new GuideReadResult("unavailable", roleEntries.Detail, [], [roleDirectory], false);
            var paths = RoleScopedCloseoutRecordStore.EnumerateExistingPaths(root, GuideRoot, unit);
            foreach (var path in paths)
            {
                evidence.Add(Path.GetFullPath(path));
                var inspect = InspectPath(root, path, expectDirectory: false);
                if (inspect.Kind != PathKind.Present)
                    return new GuideReadResult("unavailable", inspect.Detail ?? "Enumerated guide evidence path is unavailable.", [], evidence, false);
                var record = GuideReachabilityRecord.Deserialize(GuardedFileRead.ReadAllText(path), unit);
                result.Add(new GuideReceipt(Path.GetFullPath(path), record));
                if (record.Role == CloseoutRecordRole.Architect) architectCount++;
            }
            return new GuideReadResult("read", null, result, evidence, architectCount > 1);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new GuideReadResult("unavailable", exception.Message, [], evidence, false);
        }
    }

    private static PathInspection InspectRecordEntries(string root, string roleDirectory)
    {
        var directory = InspectPath(root, roleDirectory, expectDirectory: true);
        if (directory.Kind != PathKind.Present) return directory;
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(roleDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                var inspection = InspectPath(root, path, expectDirectory: false);
                if (inspection.Kind != PathKind.Present) return inspection;
            }
            return new PathInspection(PathKind.Present, null);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new PathInspection(PathKind.Unavailable, exception.Message);
        }
    }

    private static bool HasExplicitKnowledgeDeclaration(string yaml)
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
        catch (YamlDotNet.Core.YamlException) { return false; }
    }

    private static YamlMappingNode ReadDeclarationRoot(string yaml)
    {
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            return stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode root
                ? root
                : throw new InvalidOperationException("Packet declaration root must be a mapping.");
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            throw new InvalidOperationException("Packet declaration YAML cannot be read: " + exception.Message);
        }
    }

    private static YamlMappingNode? OptionalDeclarationMapping(YamlMappingNode parent, string key, string path)
    {
        if (!parent.Children.TryGetValue(new YamlScalarNode(key), out var node)) return null;
        return node as YamlMappingNode
            ?? throw new InvalidOperationException($"Packet field '{path}' must be a mapping when present.");
    }

    private static void ValidatePresentDeclarationBoolean(YamlMappingNode mapping, string key, string path)
    {
        if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var node)) return;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value)
            || !bool.TryParse(scalar.Value.Trim(), out _))
        {
            throw new InvalidOperationException($"Packet field '{path}' must be a nonempty boolean scalar when present.");
        }
    }

    private static void ValidateKnowledgeDeclarationShapes(string yaml)
    {
        var root = ReadDeclarationRoot(yaml);
        var updates = OptionalDeclarationMapping(root, "knowledge_updates", "knowledge_updates");
        if (updates is not null)
        {
            foreach (var facet in new[] { "intent_tree", "adr", "diagram", "docs" })
            {
                OptionalDeclarationMapping(updates, facet, $"knowledge_updates.{facet}");
            }

            foreach (var entry in updates.Children)
            {
                if (entry.Value is not YamlMappingNode facetMapping) continue;
                var facetName = (entry.Key as YamlScalarNode)?.Value ?? "<facet>";
                ValidatePresentDeclarationBoolean(facetMapping, "required", $"knowledge_updates.{facetName}.required");
            }
        }

        var learning = OptionalDeclarationMapping(root, "closeout_learning", "closeout_learning");
        if (learning is not null)
        {
            ValidatePresentDeclarationBoolean(learning, "write_back_required", "closeout_learning.write_back_required");
        }
    }

    private static void ValidateGuideDeclarationShapes(string yaml)
    {
        var root = ReadDeclarationRoot(yaml);
        if (!root.Children.TryGetValue(new YamlScalarNode("guide_reachability"), out var node)) return;
        if (node is YamlSequenceNode) return;
        if (node is not YamlMappingNode mapping)
        {
            throw new InvalidOperationException("Packet field 'guide_reachability' must be a mapping or nonempty route sequence.");
        }

        foreach (var key in new[] { "no_role_facing_surface", "no_role_facing", "no_surface", "none",
                     "not_applicable", "declared_no_surface", "required" })
        {
            ValidatePresentDeclarationBoolean(mapping, key, $"guide_reachability.{key}");
        }

        foreach (var key in new[] { "routes", "guide_routes", "entries", "declarations", "surfaces" })
        {
            if (mapping.Children.TryGetValue(new YamlScalarNode(key), out var routes) && routes is not YamlSequenceNode)
            {
                throw new InvalidOperationException($"Packet field 'guide_reachability.{key}' must be a route sequence when present.");
            }
        }
    }

    private static bool TryReadPacketIdentityAssertions(
        string yaml,
        out PacketIdentityAssertions? assertions,
        out string? error)
    {
        assertions = null;
        error = null;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                error = "Packet YAML has no mapping root for identity validation.";
                return false;
            }

            YamlMappingNode? implementation = null;
            if (root.Children.TryGetValue(new YamlScalarNode("implementation_issue_packet"), out var implementationNode))
            {
                if (implementationNode is not YamlMappingNode mapping)
                {
                    error = "Packet implementation_issue_packet identity container must be a mapping.";
                    return false;
                }
                implementation = mapping;
            }

            assertions = new PacketIdentityAssertions(
                ReadPacketIdentityField(root, implementation, "domain"),
                ReadPacketIdentityField(root, implementation, "source_execution_unit"),
                ReadPacketIdentityField(root, implementation, "target_repo"));
            return true;
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            error = "Packet identity assertions could not be parsed: " + exception.Message;
            return false;
        }
    }

    private static PacketIdentityField ReadPacketIdentityField(
        YamlMappingNode root,
        YamlMappingNode? implementation,
        string name)
    {
        var rootPath = name;
        var nestedPath = $"implementation_issue_packet.{name}";
        var rootPresent = root.Children.TryGetValue(new YamlScalarNode(name), out var rootNode);
        YamlNode? nestedNode = null;
        var nestedPresent = implementation is not null
            && implementation.Children.TryGetValue(new YamlScalarNode(name), out nestedNode);

        string? rootValue = null;
        string? nestedValue = null;
        if (rootPresent && !TryGetPacketIdentityScalar(rootNode!, rootPath, out rootValue, out var rootError))
        {
            return new PacketIdentityField(true, null, rootError);
        }
        if (nestedPresent && !TryGetPacketIdentityScalar(nestedNode!, nestedPath, out nestedValue, out var nestedError))
        {
            return new PacketIdentityField(true, null, nestedError);
        }
        if (rootPresent && nestedPresent
            && !string.Equals(rootValue, nestedValue, StringComparison.Ordinal))
        {
            return new PacketIdentityField(true, null,
                $"Packet has conflicting actual '{rootPath}' and '{nestedPath}' identity values.");
        }

        return new PacketIdentityField(rootPresent || nestedPresent, nestedValue ?? rootValue, null);
    }

    private static bool TryGetPacketIdentityScalar(YamlNode node, string path, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value)
            || scalar.Style == YamlDotNet.Core.ScalarStyle.Plain
                && scalar.Value.Trim() is "~" or "null" or "Null" or "NULL")
        {
            error = $"Packet '{path}' must be a nonempty scalar identity when present.";
            return false;
        }

        value = scalar.Value.Trim();
        return true;
    }

    private static bool IsSafeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value[0] == '.' || value.Contains("..", StringComparison.Ordinal)) return false;
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    }

    private static bool IsSafeRepo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(part => part.Length is > 0 and <= 100
            && part is not "." and not ".."
            && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'));
    }

    private static bool TryReadGitHubIdentity(string value, string kind, out string repo, out int number)
    {
        repo = string.Empty;
        number = 0;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !string.Equals(parts[2], kind, StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[3], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out number) || number <= 0) return false;
        repo = parts[0] + "/" + parts[1];
        return IsSafeRepo(repo);
    }

    private static PathInspection InspectPath(string root, string path, bool expectDirectory)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(fullRoot, fullPath, StringComparison.Ordinal)
            && !fullPath.StartsWith(rootPrefix, PathComparison))
        {
            return new PathInspection(PathKind.Unavailable, "Canonical evidence path escapes the snapshot root.");
        }

        var relative = Path.GetRelativePath(fullRoot, fullPath);
        var current = fullRoot;
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { return MissingOrDanglingLink(current); }
            catch (DirectoryNotFoundException) { return MissingOrDanglingLink(current); }
            catch (Exception exception) when (IsReadFailure(exception))
            {
                return new PathInspection(PathKind.Unavailable, exception.Message);
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return new PathInspection(PathKind.Unavailable, $"Canonical evidence path '{Path.GetRelativePath(fullRoot, current)}' is a symlink/reparse point.");
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var isLast = index == segments.Length - 1;
            if (!isLast && !isDirectory)
                return new PathInspection(PathKind.Unavailable, $"Canonical evidence path parent '{Path.GetRelativePath(fullRoot, current)}' is not a directory.");
            if (isLast && isDirectory != expectDirectory)
                return new PathInspection(PathKind.Unavailable, expectDirectory
                    ? $"Expected directory at '{Path.GetRelativePath(fullRoot, current)}'."
                    : $"Expected regular file at '{Path.GetRelativePath(fullRoot, current)}'.");
        }
        return new PathInspection(PathKind.Present, null);
    }

    private static PathInspection MissingOrDanglingLink(string path)
    {
        try
        {
            if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
            {
                return new PathInspection(PathKind.Unavailable, $"Canonical evidence path '{path}' is a dangling symlink/reparse point.");
            }
            return new PathInspection(PathKind.Missing, null);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            return new PathInspection(PathKind.Unavailable, exception.Message);
        }
    }

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static SoloConductorClaimReleaseGateResult ApplicabilityRefusal(
        string root,
        string unit,
        string team,
        string snapshotOid,
        string targetRef,
        string cause,
        string detail,
        string evidencePath,
        string? domain = null)
    {
        var evidence = Path.IsPathRooted(evidencePath) ? Relative(root, evidencePath) : evidencePath.Replace(Path.DirectorySeparatorChar, '/');
        return new SoloConductorClaimReleaseGateResult(true, new SoloConductorClaimReleaseCompletion
        {
            Decision = "refused",
            ExecutionUnit = unit,
            Domain = domain,
            Team = team,
            CanonicalSnapshotOid = snapshotOid,
            CanonicalTargetRef = targetRef,
            Applicability = new SoloConductorClaimReleaseApplicability
            {
                State = "unavailable",
                Cause = cause,
                Detail = detail,
                EvidencePaths = [evidence],
            },
            Duties = [ApplicabilityDuty(root, "applicability", cause, detail, evidence, targetRef,
                repairUnavailable: "Applicability cannot be established from canonical snapshot evidence; correct the named canonical artifact, publish it, then retry release.")],
        });
    }

    private static SoloConductorClaimReleaseGateResult ApplicableResult(
        string unit, string team, string? domain, string snapshotOid, string targetRef,
        string cause, string detail, string modeSource, IReadOnlyList<string> paths,
        IReadOnlyList<SoloConductorClaimReleaseDuty> duties, string? targetRepo, int? linkedPr)
    {
        return new SoloConductorClaimReleaseGateResult(true, new SoloConductorClaimReleaseCompletion
        {
            Decision = "refused",
            ExecutionUnit = unit,
            Domain = domain,
            Team = team,
            TargetRepo = targetRepo,
            LinkedPr = linkedPr,
            CanonicalSnapshotOid = snapshotOid,
            CanonicalTargetRef = targetRef,
            Applicability = new SoloConductorClaimReleaseApplicability
            {
                State = "applicable",
                Cause = cause,
                Detail = detail,
                Mode = TeamMode.SoloConductor,
                ModeSource = modeSource,
                EvidencePaths = paths,
            },
            Duties = duties,
        });
    }

    private static SoloConductorClaimReleaseDuty ApplicabilityDuty(
        string root, string id, string cause, string detail, string path, string targetRef, string? repairUnavailable) => BuildDuty(
            id, "unavailable", cause, detail,
            [new SoloConductorClaimReleaseEvidence { Path = Path.IsPathRooted(path) ? Relative(root, path) : path }], [], null, repairUnavailable);

    private static SoloConductorClaimReleaseDuty BuildDuty(
        string id,
        string state,
        string cause,
        string detail,
        IReadOnlyList<SoloConductorClaimReleaseEvidence> evidence,
        IReadOnlyList<string> recoveryCommands,
        string? publicationStep,
        string? repairUnavailableReason,
        IReadOnlyList<string>? declaredFacets = null,
        IReadOnlyList<string>? declaredTargets = null,
        IReadOnlyList<SoloConductorClaimReleaseRoute>? declaredRoutes = null) => new()
        {
            Id = id,
            State = state,
            Cause = cause,
            Detail = detail,
            Evidence = evidence,
            RecoveryCommands = recoveryCommands,
            PublicationStep = publicationStep,
            RepairUnavailableReason = repairUnavailableReason,
            DeclaredFacets = declaredFacets,
            DeclaredTargets = declaredTargets,
            DeclaredRoutes = declaredRoutes,
        };

    private static SoloConductorClaimReleaseDuty NotApplicableDuty(
        string id, string cause, string detail, SoloConductorClaimReleaseEvidence evidence,
        IReadOnlyList<string>? facets, IReadOnlyList<string>? targets) => BuildDuty(
            id, "not-applicable", cause, detail, [evidence], [], null, null, facets, targets);

    private static SoloConductorClaimReleaseEvidence Evidence(string root, string path, object? record = null)
    {
        var evidence = new SoloConductorClaimReleaseEvidence { Path = Relative(root, path) };
        return record switch
        {
            KnowledgeWriteBackRecord knowledge => evidence with
            {
                ArtifactKind = knowledge.ArtifactKind,
                ExecutionUnit = knowledge.ExecutionUnit,
                Role = knowledge.Role,
                HostCommit = knowledge.HostCommit,
                RecordedAt = knowledge.RecordedAt,
                Targets = knowledge.Targets,
            },
            GuideReachabilityRecord guide => evidence with
            {
                ArtifactKind = guide.ArtifactKind,
                ExecutionUnit = guide.ExecutionUnit,
                Role = guide.Role,
                HostCommit = guide.HostCommit,
                RecordedAt = guide.RecordedAt,
                GuideSurfaces = guide.GuideSurfaces,
                RoutingRoles = guide.Roles,
            },
            QueueItem item => evidence with
            {
                ExecutionUnit = item.ExecutionUnit,
                QueueState = item.State.ToString(),
                Repo = item.LinkedIssue?.Repo,
                Pr = ParseQueuePr(item, item.LinkedIssue?.Repo ?? string.Empty, out _),
            },
            _ => evidence,
        };
    }

    private static string PublicationStep(string targetRef) =>
        $"After recording locally, stage only exact owned evidence paths, commit, and plain-push to {targetRef}; then retry claim release. Example: git -C <canonical-host-checkout> add -- <exact-owned-artifact-paths> && git -C <canonical-host-checkout> commit -m <receipt-publication-message> && git -C <canonical-host-checkout> push origin HEAD:{targetRef}. Never stage the whole dirty repository; if already published, verify canonical visibility without an empty commit.";

    private static string BuildQueueRepairUnavailableReason(string root, string path, string detail, string targetRef) =>
        $"Canonical queue artifact '{Relative(root, path)}' is unavailable: {detail} Return the exact owned path to the responsible host/queue-owner workflow for correction, publish that change to {targetRef}, then retry claim release.";

    private static string BuildScopedCompletedQueueIdentityRepairUnavailableReason(string root, string path, string detail, string targetRef) =>
        $"The selected scoped queue artifact '{Relative(root, path)}' is missing linked_pr: {detail} `intent-cli automation state-doctor` reads and writes only the legacy queue layout and cannot repair this scoped file. Ordinary closeout does not rewrite a completed item, and --repair-runs only appends run receipts. Return the exact artifact to the responsible host/architect workflow for a supported scoped correction, publish it to {targetRef}, then retry claim release.";

    private static string BuildRunLogRepairUnavailableReason(string root, string path, string targetRef) =>
        $"Canonical run log '{Relative(root, path)}' is malformed, contradictory, or unreadable. `intent-cli closeout pr --repair-runs` only appends missing receipts and cannot correct this artifact. Return the exact path to the responsible host/architect workflow for correction, publish the owned change to {targetRef}, then retry claim release.";

    private static string PublicationStep(string root, string targetRef) => PublicationStep(targetRef);

    private static bool IsReadFailure(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or JsonException
        or InvalidOperationException
        or ArgumentException
        or NullReferenceException
        or IndexOutOfRangeException
        or NotSupportedException;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private enum PathKind { Missing, Present, Unavailable }
    private readonly record struct PathInspection(PathKind Kind, string? Detail);
    private sealed record PacketIdentityField(bool Present, string? Value, string? Error);
    private sealed record PacketIdentityAssertions(
        PacketIdentityField Domain,
        PacketIdentityField SourceExecutionUnit,
        PacketIdentityField TargetRepo);
    private sealed record QueueReadResult(string State, string Cause, string Detail, string? Path, QueueItem? Item, int? PullRequest, string? RepairUnavailableReason);
    private sealed record RunsReadResult(string State, string Cause, string Detail, string? Path, IReadOnlyList<RunEvent>? Events);
    private sealed record KnowledgeReceipt(string Path, KnowledgeWriteBackRecord Record);
    private sealed record GuideReceipt(string Path, GuideReachabilityRecord Record);
    private sealed record KnowledgeReadResult(string State, string? Detail, IReadOnlyList<KnowledgeReceipt> Records, IReadOnlyList<string> Evidence, IReadOnlyList<string> DuplicateRoles)
    {
        public string? RepairUnavailableReason => State == "unavailable" ? "Existing knowledge evidence is unreadable or immutable; the supported writer cannot promise to overwrite it." : null;
    }
    private sealed record GuideReadResult(string State, string? Detail, IReadOnlyList<GuideReceipt> Records, IReadOnlyList<string> Evidence, bool DuplicateArchitect)
    {
        public string? RepairUnavailableReason => State == "unavailable" || DuplicateArchitect ? "Existing guide evidence is unreadable or duplicated; the supported writer cannot promise to overwrite it." : null;
    }
}
