namespace IntentSystem.Cli.Commands;

internal static class CommandRouter
{
    private delegate int CommandHandler(CliContext context, string[] args, TextWriter writer);

    private static readonly string[] CommandGroups =
    [
        "project",
        "projection",
        "queue",
        "issue",
        "bug",
        "review",
        "interview",
        "clarify",
        "clarification",
        "status",
        "context",
        "next-slice",
        "automation",
        "safety",
        "tasking",
        "task",
        "worker",
        "metadata",
        "guide",
        "intent",
        "packet",
        "closeout",
        "migrate",
        "skill",
        // G570: session-layer transport selection (agmsg | herdr-only).
        "session-layer",
        // G578: transport-neutral role notification surface.
        "notify",
        // G596: explicit durable lifecycle for obligations requiring a human operator.
        "operator-attention"
    ];

    /// <summary>
    /// G379: the chat-first subset shown by the default <c>intent-cli --help</c>.
    /// These are the routine implementation / review / next-slice surfaces from
    /// the issue's Accepted Baseline. Every entry must also appear in
    /// <see cref="CommandGroups"/> (asserted by tests).
    /// </summary>
    internal static readonly IReadOnlyList<string> PrimaryCommandGroups = new[]
    {
        "guide",
        "automation",
        "notify",
        "worker",
        "review",
        "issue",
        "packet",
        "intent",
        "closeout"
    };

    private static readonly IReadOnlyList<string> AutomationCommandHelp =
    [
        "automation base-branch-check --repo <r> --pr <n> --actual-base <branch> [--policy direct-main|main-ai]",
        "automation check",
        "automation clarification-stop",
        "automation complete",
        "automation doctor",
        "automation durable-state-preflight [--format markdown|json]",
        "automation heartbeat --domain <d> --repo <r> [--stale-minutes <m, default 45>] [--format json|markdown]",
        "automation host-loop-next-action --repo <r> [--sync-classification <c>] [--publish-recovery-repairs <N>] [--next-slice-issue-cut-ready] [--publish-next-execution-unit <u>]",
        "automation host-loop-wake --repo <r> [--domain <d>] [--write] [--format json|markdown]",
        "automation host-queue-item-recovery --repo <r> [--unit <u>] [--issue <n>] [--pr <m>] [--write]",
        "automation host-review-preflight",
        "automation host-review-diagnostics",
        "automation host-sync-preflight",
        "automation intent-target-gap-recovery --repo <r> [--write]",
        "automation issue-publish --issue <n> --write",
        "automation queue-dependency-reconcile [--execution-unit <u>] [--dry-run|--write]",
        "automation knowledge-writeback-record --execution-unit <u> --commit <host-sha> [--target <path>]... [--dry-run|--write]",
        "automation pr-transition --transition review-start --write",
        "automation pr-transition --transition request-update --write",
        "automation pr-transition --transition approved --write",
        "automation publish-lifecycle-repair --repo <r> [--write]",
        "automation publish-recovery --repo <r> [--write]",
        "automation reconcile [--lane host-review|next-slice|all] [--write]",
        "automation runs-audit [--repo <r>] [--domain <d>] [--write] [--apply-inferred] [--format json|markdown]",
        "automation state-doctor [--repo <r>] [--read-only] [--write]",
        "automation summary",
        "automation workspace-guard --mode plan|begin|end [--write]"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, CommandHandler>> ImplementedCommands =
        new Dictionary<string, IReadOnlyDictionary<string, CommandHandler>>(StringComparer.Ordinal)
        {
            ["project"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["status"] = ProjectStatusCommand.Execute
            },
            ["projection"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["generate"] = ProjectionGenerateCommand.Generate,
                ["regenerate"] = ProjectionGenerateCommand.Regenerate
            },
            ["review"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["closeout-plan"] = ReviewCloseoutPlanCommand.Execute,
                // G374: host-side structured worker-signal collection / convergence.
                ["collect-signals"] = ReviewCollectSignalsCommand.Execute,
                ["signal-handled"] = ReviewSignalHandledCommand.Execute
            },
            ["interview"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["start"] = InterviewStartCommand.Execute,
                ["answer"] = InterviewAnswerCommand.Execute,
                ["resume"] = InterviewResumeCommand.Execute,
                ["next-question"] = InterviewNextQuestionCommand.Execute,
                ["record-answer"] = InterviewRecordAnswerCommand.Execute,
                ["compile"] = InterviewCompileCommand.Execute
            },
            // G570/G592/G601: mode, canonical delivery topology, and generated visibility markers.
            ["session-layer"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["show"] = SessionLayerCommand.ExecuteShow,
                ["set"] = SessionLayerCommand.ExecuteSet,
                ["topology"] = SessionLayerTopologyCommand.Execute,
                ["marker"] = SessionLayerMarkerCommand.Execute,
            },
            // G578: one workflow contract over agmsg and herdr-only transports.
            ["notify"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["delegate"] = NotifyCommand.ExecuteDelegate,
                ["report"] = NotifyCommand.ExecuteReport,
                ["escalate"] = NotifyCommand.ExecuteEscalate
            },
            ["operator-attention"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["open"] = OperatorAttentionCommand.ExecuteOpen,
                ["resolve"] = OperatorAttentionCommand.ExecuteResolve,
                ["supersede"] = OperatorAttentionCommand.ExecuteSupersede,
                ["query"] = OperatorAttentionCommand.ExecuteQuery
            },
            // G559: cross-platform agent skill install surface.
            ["skill"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["list"] = SkillCommand.ExecuteList,
                ["install"] = SkillCommand.ExecuteInstall,
                ["diff"] = SkillCommand.ExecuteDiff
            },
            ["clarify"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["open"] = ClarifyOpenCommand.Execute,
                ["list"] = ClarifyListCommand.Execute,
                ["answer"] = ClarifyAnswerCommand.Execute,
                ["draft"] = ClarifyDraftCommand.Execute,
                ["record"] = ClarifyRecordCommand.Execute
            },
            ["clarification"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["status"] = ClarificationCommand.ExecuteStatus,
                ["next"] = ClarificationCommand.ExecuteNext,
                ["answer"] = ClarificationCommand.ExecuteAnswer
            },
            ["queue"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["list"] = QueueListCommand.Execute,
                ["show"] = QueueShowCommand.Execute,
                ["next"] = QueueNextCommand.Execute,
                ["enqueue"] = QueueEnqueueCommand.Execute,
                ["dispatch"] = QueueDispatchCommand.Execute,
                ["transition"] = QueueTransitionCommand.Execute,
                ["reprioritize"] = QueueReprioritizeCommand.Execute,
                ["priority-drift"] = QueuePriorityDriftCommand.Execute
            },
            ["issue"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["draft"] = IssueDraftCommand.Execute,
                ["create"] = IssueCreateCommand.Execute,
                ["publish"] = IssuePublishCommand.Execute,
                ["status"] = IssueStatusCommand.Execute,
                ["validate-body"] = IssueValidateBodyCommand.Execute,
                ["prepare"] = IssuePrepareCommand.Execute,
                ["publish-reviewed"] = IssuePublishReviewedCommand.Execute,
                ["plan-candidate"] = IssuePlanCandidateCommand.Execute,
                ["publish-flow"] = IssuePublishFlowCommand.Execute
            },
            ["bug"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["report"] = BugReportCommand.Execute,
                ["triage"] = BugTriageCommand.Execute,
                ["plan"] = BugExecutionCommand.Execute,
                ["implementation-repair"] = BugImplementationRepairCommand.Execute,
                ["implementation-issue"] = BugImplementationIssueCommand.Execute
            },
            ["status"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["brief"] = StatusBriefCommand.Execute
            },
            ["context"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["collect"] = ContextCollectCommand.Execute
            },
            ["next-slice"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["classify"] = NextSliceClassifyCommand.Execute
            },
            ["automation"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["base-branch-check"] = AutomationBaseBranchCheckCommand.Execute,
                ["check"] = AutomationCheckCommand.Execute,
                ["clarification-stop"] = AutomationClarificationStopCommand.Execute,
                ["closeout-drift-check"] = AutomationCloseoutDriftCheckCommand.Execute,
                ["complete"] = AutomationCompleteCommand.Execute,
                ["doctor"] = AutomationDoctorCommand.Execute,
                ["durable-state-preflight"] = AutomationDurableStatePreflightCommand.Execute,
                ["heartbeat"] = AutomationHeartbeatCommand.Execute,
                ["host-review-preflight"] = AutomationHostReviewPreflightCommand.Execute,
                ["host-review-diagnostics"] = AutomationHostReviewDiagnosticsCommand.Execute,
                ["host-loop-next-action"] = AutomationHostLoopNextActionCommand.Execute,
                ["host-loop-wake"] = AutomationHostLoopWakeCommand.Execute,
                ["host-queue-item-recovery"] = AutomationHostQueueItemRecoveryCommand.Execute,
                ["host-sync-preflight"] = AutomationHostSyncPreflightCommand.Execute,
                ["intent-target-gap-recovery"] = AutomationIntentTargetGapRecoveryCommand.Execute,
                ["issue-block"] = AutomationIssueBlockCommand.Execute,
                ["issue-publish"] = AutomationIssuePublishCommand.Execute,
                ["issue-release"] = AutomationIssueReleaseCommand.Execute,
                ["issue-retire"] = AutomationIssueRetireCommand.Execute,
                // G564: records a performed intent-tree/ADR/diagram/docs write-back.
                ["knowledge-writeback-record"] = AutomationKnowledgeWriteBackRecordCommand.Execute,
                ["label-palette-audit"] = AutomationLabelPaletteAuditCommand.Execute,
                ["label-palette-sync"] = AutomationLabelPaletteSyncCommand.Execute,
                ["pr-transition"] = AutomationPrTransitionCommand.Execute,
                ["publish-lifecycle-repair"] = AutomationPublishLifecycleRepairCommand.Execute,
                ["publish-recovery"] = AutomationPublishRecoveryCommand.Execute,
                // G568: diagnose/repair packet-vs-queue dependency drift.
                ["queue-dependency-reconcile"] = AutomationQueueDependencyReconcileCommand.Execute,
                ["queue-seed-from-packet"] = AutomationQueueSeedFromPacketCommand.Execute,
                ["reconcile"] = AutomationReconcileCommand.Execute,
                ["runs-audit"] = AutomationRunsAuditCommand.Execute,
                ["same-repo-metadata-preflight"] = AutomationSameRepoMetadataPreflightCommand.Execute,
                ["stalled-work"] = AutomationStalledWorkCommand.Execute,
                ["state-doctor"] = AutomationStateDoctorCommand.Execute,
                ["summary"] = AutomationSummaryCommand.Execute,
                ["workspace-guard"] = AutomationWorkspaceGuardCommand.Execute
            },
            ["safety"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["nested-provider-handoff"] = SafetyNestedProviderHandoffCommand.Execute
            },
            ["worker"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["issue-preflight"] = WorkerIssuePreflightCommand.Execute,
                ["pr-review-preflight"] = WorkerPrReviewPreflightCommand.Execute,
                ["pr-comment-preflight"] = WorkerPrCommentPreflightCommand.Execute,
                ["result-summary"] = WorkerResultSummaryCommand.Execute,
                ["next-action"] = WorkerNextActionCommand.Execute,
                ["claim"] = WorkerClaimCommand.Execute,
                ["complete"] = WorkerCompleteCommand.Execute,
                // G374: child-side structured worker-signal raising.
                ["signal"] = WorkerSignalCommand.Execute
            },
            ["metadata"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["validate"] = MetadataValidateCommand.Execute,
                ["update"] = MetadataUpdateCommand.Execute
            },
            ["migrate"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["host-state"] = (ctx, args, w) => MigrateHostStateCommand.Execute(ctx, ["host-state", .. args], w)
            },
            ["task"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["issue-to-pr"] = (ctx, args, w) => TaskCommand.Execute(ctx, ["issue-to-pr", .. args], w),
                ["review-pr"] = (ctx, args, w) => TaskCommand.Execute(ctx, ["review-pr", .. args], w),
                ["fix-pr-comments"] = (ctx, args, w) => TaskCommand.Execute(ctx, ["fix-pr-comments", .. args], w),
                ["publish-next-issue"] = (ctx, args, w) => TaskCommand.Execute(ctx, ["publish-next-issue", .. args], w)
            },
            ["tasking"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["handoff"] = TaskingHandoffCommand.Execute,
                ["task-packet"] = TaskingTaskPacketCommand.Execute,
                ["task-packet-preview"] = TaskingTaskPacketPreviewCommand.Execute,
                ["task-packet-checklist"] = TaskingTaskPacketChecklistCommand.Execute,
                ["handoff-bundle"] = TaskingHandoffBundleCommand.Execute,
                ["handoff-bundle-inspect"] = TaskingHandoffBundleInspectCommand.Execute,
                ["handoff-bundle-verify"] = TaskingHandoffBundleVerifyCommand.Execute,
                ["handoff-bundle-import-dry-run"] = TaskingHandoffBundleImportDryRunCommand.Execute,
                ["publish-reviewed-bridge"] = TaskingPublishReviewedBridgeCommand.Execute,
                ["handoff-bundle-history"] = TaskingHandoffBundleHistoryCommand.Execute,
                ["ai-thread-summary-attach"] = TaskingAiThreadSummaryAttachCommand.Execute
            },
            ["guide"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                // G393: guide-first entrypoint — the single obvious command an
                // agent runs before intent/packet/issue/review/loop work.
                ["start"] = GuideStartCommand.Execute,
                ["oneshot"] = GuideOneshotCommand.Execute,
                ["automation"] = GuideAutomationCommand.Execute,
                ["review"] = GuideReviewCommand.Execute,
                ["collaborate"] = GuideCollaborateCommand.Execute,
                ["rules"] = GuideRulesCommand.Execute,
                ["workflow"] = GuideWorkflowCommand.Execute,
                ["model"] = GuideModelCommand.Execute,
                ["improve"] = GuideImproveCommand.Execute,
                ["grill"] = GuideGrillCommand.Execute,
                ["stack"] = GuideStackCommand.Execute,
                ["next"] = GuideNextCommand.Execute,
                ["inspect"] = GuideInspectCommand.Execute,
                ["commands"] = GuideCommandsCommand.Execute,
                ["onboarding"] = GuideOnboardingCommand.Execute,
                ["intent-work"] = GuideIntentWorkCommand.Execute,
                ["worker"] = GuideWorkerCommand.Execute,
                ["closeout"] = GuideCloseoutCommand.Execute,
                ["prompt-matrix"] = GuidePromptMatrixCommand.Execute,
                ["prompt-template"] = GuidePromptTemplateCommand.Execute,
                // G380: direct clarification-question-style guide surface.
                ["question-style"] = GuideQuestionStyleCommand.Execute,
                // G381: persistent goal-seeking interview-mode guide surface.
                ["interview-mode"] = GuideInterviewModeCommand.Execute,
                // G382: interview readiness checklist + classification.
                ["interview-readiness"] = GuideInterviewReadinessCommand.Execute,
                // G383: host review visible-verification policy classification.
                ["review-verification-policy"] = GuideReviewVerificationPolicyCommand.Execute,
                // G326: role-scoped host ownership model.
                ["host-ownership"] = GuideHostOwnershipCommand.Execute,
                // G334: self-discovery help surface for external users.
                ["help"] = GuideHelpCommand.Execute,
                // G438: external artifact intake guidance (external issue / PR review / PR adopt).
                ["artifact-intake"] = GuideArtifactIntakeCommand.Execute,
                // G487: optional agmsg-backed orchestrator-thread guide surface.
                ["orchestrator-thread"] = GuideOrchestratorThreadCommand.Execute,
                // G488: thin, portable agent skill pack bootstrap (ADR-013 / spec-27).
                ["skill-pack"] = GuideSkillPackCommand.Execute
            },
            ["intent"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["host-check"] = IntentHostCheckCommand.Execute,
                ["init"] = IntentInitCommand.Execute,
                ["init-tree"] = IntentInitTreeCommand.Execute,
                ["add-feature"] = IntentAddFeatureCommand.Execute,
                ["analyze-tree"] = IntentAnalyzeTreeCommand.Execute,
                ["lint-layout"] = IntentLintLayoutCommand.Execute,
                ["status"] = IntentStatusCommand.Execute,
                ["search"] = IntentSearchCommand.Execute,
                ["explain"] = IntentExplainCommand.Execute,
                ["next-slice"] = IntentNextSliceCommand.Execute,
                ["draft-from-interview"] = IntentDraftFromInterviewCommand.Execute,
                ["facet-check"] = IntentFacetCheckCommand.Execute
            },
            ["packet"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["draft"] = PacketDraftCommand.Execute,
                ["retire"] = PacketRetireCommand.Execute
            },
            ["closeout"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["pr"] = CloseoutPrCommand.Execute
            }
        };

    public static int Execute(string[] args, CliContext context, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length > 0 && string.Equals(args[0], "guide", StringComparison.Ordinal))
        {
            return GuideSkillUpdateNudge.Execute(
                context,
                args,
                writer,
                guideWriter => ExecuteCore(args, context, guideWriter));
        }

        return ExecuteCore(args, context, writer);
    }

    private static int ExecuteCore(string[] args, CliContext context, TextWriter writer)
    {
        // G379: `intent-cli --help --all` (and the `--help-all` synonym) shows
        // every command group, including the advanced/legacy/provider-runtime
        // surfaces hidden from the chat-first default. Checked before the
        // plain-`--help` branch so the `--all` modifier is honored.
        if ((args.Length == 2
                && string.Equals(args[0], "--help", StringComparison.Ordinal)
                && string.Equals(args[1], "--all", StringComparison.Ordinal))
            || (args.Length == 1 && string.Equals(args[0], "--help-all", StringComparison.Ordinal)))
        {
            WriteHelp(writer, includeAll: true);
            return 0;
        }

        if (args.Length == 0
            || (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal)))
        {
            WriteHelp(writer, includeAll: false);
            return 0;
        }

        // G317 review-fix: `intent-cli task` (no subcommand) and
        // `intent-cli task --help` must reach the task help surface.
        // Without this special-case, `task` falls through to the
        // "group and subcommand required" branch and `task --help`
        // looks up "--help" in the per-group dictionary and fails as
        // "not yet implemented", which makes the task surface
        // undiscoverable from the installed CLI entry path.
        if (string.Equals(args[0], "task", StringComparison.Ordinal)
            && (args.Length == 1
                || (args.Length == 2 && string.Equals(args[1], "--help", StringComparison.Ordinal))))
        {
            return TaskCommand.Execute(context, args[1..], writer);
        }

        // G457: top-level `intent-cli improve` alias for the design-thread
        // improve / realignment process. Without a first-class top-level
        // surface, an agent given the short request "intent-cli で improve
        // プロセスを実行してください" searches the CLI, fails to find an
        // obvious improve command, and misroutes to bug-to-intent-repair /
        // host-loop recovery / state-doctor. `improve` takes only flags
        // (`--domain` / `--format` / `--help`), so it is intercepted here
        // before group/subcommand dispatch (which would treat `--domain` as
        // a subcommand) and delegated verbatim to the same guidance surface
        // as `guide improve`.
        if (string.Equals(args[0], "improve", StringComparison.Ordinal))
        {
            return GuideImproveCommand.Execute(context, args[1..], writer);
        }

        // G463: top-level `intent-cli grill` alias for persistent interview
        // mode. Like `improve`, it takes only flags (`--domain` / `--format` /
        // `--help`), so it is intercepted here before group/subcommand dispatch
        // and delegated verbatim to the same guidance surface as `guide grill`.
        // A first-class top-level surface keeps an agent asked to "grill a
        // topic" from misrouting to clarification or improve.
        if (string.Equals(args[0], "grill", StringComparison.Ordinal))
        {
            return GuideGrillCommand.Execute(context, args[1..], writer);
        }

        // G464: top-level `intent-cli stack` alias for the packet-backlog
        // creation process. Like `improve` / `grill`, it takes only flags
        // (`--domain` / `--target-repo` / `--format` / `--help`), so it is
        // intercepted here before group/subcommand dispatch and delegated
        // verbatim to the same guidance surface as `guide stack`. A
        // first-class top-level surface keeps an agent asked to "create the
        // available packets and publish the first issue" from improvising an
        // ad-hoc multi-issue publish.
        if (string.Equals(args[0], "stack", StringComparison.Ordinal))
        {
            return GuideStackCommand.Execute(context, args[1..], writer);
        }

        // G465: top-level `intent-cli next` alias for the design-side action
        // advisor. Like `improve` / `grill` / `stack`, it takes only flags
        // (`--domain` / `--target-repo` / `--format` / `--help`), so it is
        // intercepted here before group/subcommand dispatch and delegated
        // verbatim to the same guidance surface as `guide next`. A first-class
        // top-level surface is the answer to "intent-cli に聞いて、次に何を
        // したらいいか教えてください。" without the agent guessing a process.
        if (string.Equals(args[0], "next", StringComparison.Ordinal))
        {
            return GuideNextCommand.Execute(context, args[1..], writer);
        }

        // G466: top-level `intent-cli inspect` alias for the evidence-backed
        // observation process. Like `improve` / `grill` / `stack` / `next`, it
        // takes only flags (`--domain` / `--target-repo` / `--format` /
        // `--help`), so it is intercepted here before group/subcommand dispatch
        // and delegated verbatim to the same guidance surface as
        // `guide inspect`.
        if (string.Equals(args[0], "inspect", StringComparison.Ordinal))
        {
            return GuideInspectCommand.Execute(context, args[1..], writer);
        }

        // G334: per-group help routing. `intent-cli <group> --help` and
        // `intent-cli <group> help` must reach a useful surface before
        // we fall through to "group and subcommand required" / "not
        // yet implemented". External users should be able to ask any
        // top-level group for its help without already knowing the
        // subcommand names. The router prints a per-group descriptor
        // (subcommands + one-line purpose + workflow guide pointer)
        // for every group in CommandGroups, including groups whose
        // subcommands are not yet wired into ImplementedCommands —
        // those are marked "unavailable in this build" so command
        // discovery cannot silently fail.
        if (args.Length == 1
            && CommandGroups.Contains(args[0], StringComparer.Ordinal))
        {
            WriteGroupHelp(args[0], writer);
            return 0;
        }
        if (args.Length == 2
            && CommandGroups.Contains(args[0], StringComparer.Ordinal)
            && (string.Equals(args[1], "--help", StringComparison.Ordinal)
                || string.Equals(args[1], "help", StringComparison.Ordinal)))
        {
            // The guide group has a real `help` subcommand that returns
            // a richer external-user surface; defer to it so JSON
            // output stays available.
            if (string.Equals(args[0], "guide", StringComparison.Ordinal)
                && string.Equals(args[1], "help", StringComparison.Ordinal))
            {
                return GuideHelpCommand.Execute(context, Array.Empty<string>(), writer);
            }
            WriteGroupHelp(args[0], writer);
            return 0;
        }

        if (args.Length < 2)
        {
            writer.WriteLine("A command group and subcommand are required.");
            WriteHelp(writer);
            return 1;
        }

        var group = args[0];
        var subcommand = args[1];

        if (!CommandGroups.Contains(group, StringComparer.Ordinal))
        {
            writer.WriteLine($"Unknown command group '{group}'.");
            WriteHelp(writer);
            return 1;
        }

        if (ImplementedCommands.TryGetValue(group, out var subcommands)
            && subcommands.TryGetValue(subcommand, out var handler))
        {
            return handler(context, args[2..], writer);
        }

        writer.WriteLine($"Command '{group} {subcommand}' is not yet implemented.");
        return 1;
    }

    /// <summary>
    /// G334: emit a per-group help block. Lists the subcommands the
    /// router can dispatch (or "unavailable in this build" when the
    /// group is reserved in <see cref="CommandGroups"/> but not wired
    /// into <see cref="ImplementedCommands"/>), names the canonical
    /// workflow-guide pointer, and reminds the caller that hand-editing
    /// metadata is forbidden when an intent-cli command exists. The
    /// output is intentionally Markdown-friendly so external users see
    /// the same shape whether they pipe to a chat or to a file.
    /// </summary>
    private static void WriteGroupHelp(string group, TextWriter writer)
    {
        writer.WriteLine($"intent-cli {group} — group help");
        writer.WriteLine($"Usage: intent-cli {group} <subcommand> [--help]");
        writer.WriteLine();

        if (ImplementedCommands.TryGetValue(group, out var subcommands)
            && subcommands.Count > 0)
        {
            writer.WriteLine("Subcommands (run with --help for details):");
            foreach (var name in subcommands.Keys.OrderBy(s => s, StringComparer.Ordinal))
            {
                writer.WriteLine($"- {name}");
            }
        }
        else
        {
            writer.WriteLine("Subcommands: unavailable in this build (group reserved but not wired).");
        }
        writer.WriteLine();

        if (GroupHelpHints.TryGetValue(group, out var hint))
        {
            writer.WriteLine($"See also: {hint}");
            writer.WriteLine();
        }

        writer.WriteLine("Prefer intent-cli-backed metadata mutation over hand-editing:");
        writer.WriteLine("- Ask `intent-cli guide commands list --format json` (or `intent-cli automation summary --domain <d> --format json`) which command performs the transition.");
        writer.WriteLine("- Do not directly edit queue-state, runs logs, publish artifacts, workflow labels, or runtime metadata by hand when a supported intent-cli command exists.");
        writer.WriteLine("- Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333). " + DispatcherSkillCarveOut.BoundaryClause);
    }

    /// <summary>
    /// G334: canonical "next call" hint per top-level group. Used by
    /// <see cref="WriteGroupHelp"/> so an external user always knows
    /// the next intent-cli command to read. Tests assert each entry
    /// names a real intent-cli surface (no dangling references).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> GroupHelpHints =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["guide"] = "`intent-cli guide help --format json` (workflow guides + examples).",
            ["intent"] = "`intent-cli intent init --domain <name> --target-repo <r> --write` (bootstrap), `intent-cli intent next-slice --dry-run --domain <d> --format json` (plan).",
            ["interview"] = "`intent-cli interview next-question --domain <d> --format json` then `intent-cli interview record-answer ...`.",
            ["packet"] = "`intent-cli packet draft --execution-unit <id> --target-repo <r> --format markdown`.",
            ["issue"] = "`intent-cli issue publish-flow <id> --repo <r> --write --format json` then `intent-cli automation issue-publish --write`.",
            ["automation"] = "`intent-cli automation summary --domain <d> --format json` (capability JSON), `intent-cli automation doctor --format json` (CLI freshness).",
            ["session-layer"] = "`intent-cli session-layer show --domain <d> [--team <t>]` (which transport is in force), `session-layer set` to change it, and `session-layer topology record|show|validate --team <t>` for the delivery mapping.",
            ["notify"] = "`intent-cli notify delegate|report|escalate --domain <d> --team <t> ...` (canonical role workflow; transport follows the recorded session-layer mode).",
            ["operator-attention"] = "`intent-cli operator-attention query --domain <d> [--team <t>] --format json` (durable human-operator obligations); use `open|resolve|supersede --write` for explicit lifecycle transitions.",
            ["bug"] = "`intent-cli guide worker pr-comment-fix --format json` (repair guidance), `intent-cli bug report`/`triage` for new-bug intake.",
            ["worker"] = "`intent-cli worker next-action --repo <r> --workdir <child> --format json` then claim / result-summary / complete.",
            ["metadata"] = "`intent-cli metadata validate --format json` (read-only); use `intent-cli metadata update --mode completed-closeout --write` for the bounded controlled writer instead of hand-editing.",
            ["migrate"] = "`intent-cli migrate host-state --from <legacy-path> --to <new-path> --write` (one-way migration; runs read-only without --write).",
            ["review"] = "`intent-cli guide review --pr <n> --repo <r> --domain <d> --format json` (G316 review surface).",
            ["closeout"] = "`intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json`.",
            ["queue"] = "`intent-cli queue list --format json`, `intent-cli queue show <id> --format json`.",
            ["status"] = "`intent-cli status brief --format json` (tasking thread brief).",
            ["context"] = "`intent-cli context collect ...` (tasking-thread context).",
            ["next-slice"] = "Prefer `intent-cli intent next-slice --dry-run` for collaborative shaping.",
            ["clarify"] = "`intent-cli clarify open ...` / `list` / `answer` / `draft` / `record`.",
            ["clarification"] = "`intent-cli clarification status`, `intent-cli clarification next`, `intent-cli clarification answer`.",
            ["task"] = "`intent-cli task issue-to-pr --repo <r> --issue <n> --workdir <path>` (controller-driven planners).",
            ["tasking"] = "`intent-cli tasking handoff` / `task-packet` / `handoff-bundle` (cross-thread tasking; experimental).",
            ["safety"] = "`intent-cli safety nested-provider-handoff` (artifact-only nested-provider handoffs).",
            ["projection"] = "`intent-cli projection generate` / `regenerate` (internal tooling).",
            ["project"] = "`intent-cli project status` (older surface; prefer `intent status`).",
            ["skill"] = "`intent-cli skill list` / `install --target claude|codex|copilot|all [--scope user|repo] [--force]` / `diff` (installs the embedded SKILL.md into each platform's skill location)."
        };

    private static void WriteHelp(TextWriter writer) => WriteHelp(writer, includeAll: false);

    /// <summary>
    /// G379: the default <c>intent-cli --help</c> is chat-first. It leads with
    /// the workflow guides and lists only the primary command groups
    /// (<see cref="PrimaryCommandGroups"/>) a coding agent reaches for in
    /// routine implementation / review / next-slice work. With
    /// <paramref name="includeAll"/> the full group list and the
    /// <see cref="RunRoleNote"/> are emitted.
    /// </summary>
    private static void WriteHelp(TextWriter writer, bool includeAll)
    {
        writer.WriteLine("intent-cli — chat-first intent workflow CLI.");
        writer.WriteLine("Routine model: human <-> coding agent (Claude / Codex / Copilot) <-> intent-cli calls <-> GitHub / host metadata.");
        writer.WriteLine("Ask `intent-cli guide workflow task <task> --format json` or `intent-cli guide prompt-template ...` for current paste-ready instructions.");
        writer.WriteLine();

        // G334: top-level help must point an external agent at the
        // canonical workflow entries (init / interview / packet /
        // issue / automation / bug repair) so self-discovery does not
        // depend on local rules or copied skill prompts. G379 keeps these
        // first so the chat-first discovery path is the prominent default.
        writer.WriteLine("Workflow guides:");
        foreach (var line in WorkflowGuidePointersHelp)
        {
            writer.WriteLine($"- {line}");
        }
        writer.WriteLine();

        if (includeAll)
        {
            writer.WriteLine("All command groups:");
            foreach (var group in CommandGroups)
            {
                writer.WriteLine($"- {group}");
            }
        }
        else
        {
            writer.WriteLine("Primary command groups (chat-first workflow):");
            foreach (var group in PrimaryCommandGroups)
            {
                writer.WriteLine($"- {group}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("Per-group help:");
        writer.WriteLine("- `intent-cli <group> --help` (e.g. `intent-cli guide --help`, `intent-cli worker --help`) — list the group's subcommands.");
        writer.WriteLine("- `intent-cli guide help [--format markdown|json]` — external-user self-discovery surface with examples + workflow guide pointers.");
        writer.WriteLine("- `intent-cli guide commands list [--format markdown|json]` — classified catalog (primary / support).");

        writer.WriteLine();
        writer.WriteLine("Automation commands:");
        foreach (var command in AutomationCommandHelp)
        {
            writer.WriteLine($"- {command}");
        }

        if (includeAll)
        {
            writer.WriteLine();
            foreach (var line in RunRoleNote)
            {
                writer.WriteLine(line);
            }
        }
    }

    /// <summary>
    /// G334: canonical one-line pointers for the six workflow phases the
    /// issue names (init / interview / packet / issue / automation /
    /// bug repair), the G338 loop-setup pair (implementation-loop /
    /// review-next-slice-loop), and the G339 bug-to-intent-repair
    /// chain. Tests assert each phase appears in the top-level help
    /// output so external agents can find the workflow entry without
    /// reading local rules.
    /// </summary>
    internal static readonly IReadOnlyList<string> WorkflowGuidePointersHelp = new[]
    {
        "init — `intent-cli guide workflow task init-host --format json` (pick design / review-runtime / child-implementation role + scaffold plan, G335) then `intent-cli intent init --domain <name> --target-repo <r> --write` for host roles.",
        "interview — `intent-cli guide workflow task intent-interview --format json` (product-owner interview / clarification loop guide, G336) then `intent-cli interview next-question --domain <d> --format json` for the durable Q/A artifact.",
        "packet — `intent-cli guide workflow task packet-draft --format json` (packet files + standalone issue contract, G337) then `intent-cli packet draft --execution-unit <id> --target-repo <r> --format markdown` to scaffold.",
        "issue — `intent-cli guide workflow task issue-publish --format json` (draft/create/publish-flow/intent-target boundary guide, G337) then `intent-cli issue publish-flow <id> --repo <r> --write --format json` and `automation issue-publish --write`.",
        "automation — `intent-cli automation summary --domain <d> --format json` (label-driven capability JSON) + `intent-cli automation doctor` (CLI freshness).",
        "bug repair — `intent-cli guide worker pr-comment-fix --format json` (narrow PR-comment repair guidance).",
        "implementation-loop — `intent-cli guide workflow task implementation-loop --target-repo <r> --agent claude --frequency 5m --format markdown` (paste-ready child implementation-loop prompt with current label/claim/complete rules, G338).",
        "review-next-slice-loop — `intent-cli guide workflow task review-next-slice-loop --domain <d> --target-repo <r> --agent claude --frequency 20m --format markdown` (paste-ready host review / next-slice-loop prompt with current host-sync preflight + packet/issue lifecycle rules, G338).",
        "bug-to-intent-repair — `intent-cli guide workflow task bug-to-intent-repair --format json` (report → triage → plan → intent-repair → implementation-repair chain; classifies implementation-mismatch / intent-gap / packet-gap / rule-gap / metadata-workflow-gap; recommends packet creation when the bug is in intent-cli rules/guidance, G339).",
        "improve (design-thread reflection) — `intent-cli improve --domain <d> --format markdown` (alias of `intent-cli guide improve`): periodic MVV / ADR / intent-tree / packet-history / clarification-history realignment review, G456/G457. NOT bug-to-intent-repair, host-loop recovery, state-doctor, or dirty-state repair.",
        "grill (persistent interview mode) — `intent-cli grill --domain <d> --format markdown` (alias of `intent-cli guide grill`): once the user asks to grill a topic, stay in grill mode — generate an open-question backlog from current intents/packets/ADRs/docs, ask one question at a time, and continue after each answer until a stop condition holds, G463. Built on the interview artifacts; NOT clarification (blocker resolution) and NOT improve (retrospective realignment).",
        "stack (packet backlog creation) — `intent-cli stack --domain <d> --target-repo <r> --format markdown` (alias of `intent-cli guide stack`): forward planning — create an ordered packet backlog from the current intents (often ~10), commit/push durable state, and publish AT MOST the first GitHub issue by default, G464. Distinct from improve (retrospective realignment), grill (open-question interview), clarification (blocker resolution), and runtime queue transitions.",
        "next (design-side action advisor) — `intent-cli next --domain <d> --target-repo <r> --format markdown` (alias of `intent-cli guide next`): answers \"what should I do next?\" by recommending ONE design-side process among grill / stack / improve / inspect / issue-publish / review / recovery / idle, with a paste-ready suggested prompt, G465. Read-only by default; never auto-executes.",
        "inspect (evidence-backed observation) — `intent-cli inspect --domain <d> --target-repo <r> --format markdown` (alias of `intent-cli guide inspect`): observe the real app / CLI / UI / log / test behavior, separate observed evidence from inference, compare against expected intent, and turn gaps into packet candidates, G466. First pass read-only; routes to stack / grill / improve / recovery / no-action. Distinct from grill, stack, and improve."
    };

    /// <summary>
    /// G188 — clarifies the accepted role of <c>intent-cli run</c>. The command
    /// family is for integration smoke, deterministic replay, and local
    /// dogfooding; production automation uses the host-side review/next-slice
    /// loop, provider-neutral GitHub labels, durable parent state, and
    /// explicit handoff artifacts (see <c>automation summary</c> and
    /// <c>safety nested-provider-handoff</c>). Exposed as <c>internal</c> so
    /// focused help-surface tests can assert against the canonical wording
    /// without re-deriving it.
    /// </summary>
    internal static readonly IReadOnlyList<string> RunRoleNote =
    [
        "Notes:",
        "- `intent-cli run` is for integration smoke, deterministic replay, and local dogfooding;",
        "  it is not the primary production orchestrator.",
        "- Production automation lives in the host-side review/next-slice loop with",
        "  provider-neutral GitHub labels, durable parent state, and explicit handoff",
        "  artifacts. See `automation summary` for the label-driven contract and",
        "  `safety nested-provider-handoff` for artifact-only nested-provider handoffs."
    ];
}
