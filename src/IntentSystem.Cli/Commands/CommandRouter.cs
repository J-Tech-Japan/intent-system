namespace IntentSystem.Cli.Commands;

internal static class CommandRouter
{
    private delegate int CommandHandler(CliContext context, string[] args, TextWriter writer);
    private const string GenerateFromCurrentCommandName = "generate-from-current";

    private static readonly string[] CommandGroups =
    [
        "project",
        "projection",
        "queue",
        "issue",
        "bug",
        "run",
        "review",
        "interview",
        "clarify",
        "clarification",
        "intake",
        "status",
        "context",
        "next-slice",
        "automation",
        "safety",
        "tasking",
        "worker",
        "metadata",
        "guide",
        "intent",
        "packet",
        "closeout"
    ];

    private static readonly IReadOnlyList<string> AutomationCommandHelp =
    [
        "automation base-branch-check --repo <r> --pr <n> --actual-base <branch> [--policy direct-main|main-ai]",
        "automation check",
        "automation clarification-stop",
        "automation complete",
        "automation doctor",
        "automation host-loop-next-action --repo <r> [--sync-classification <c>] [--publish-recovery-repairs <N>] [--next-slice-issue-cut-ready] [--publish-next-execution-unit <u>]",
        "automation host-review-preflight",
        "automation host-review-diagnostics",
        "automation host-sync-preflight",
        "automation issue-publish --issue <n> --write",
        "automation pr-transition --transition review-start --write",
        "automation pr-transition --transition request-update --write",
        "automation pr-transition --transition approved --write",
        "automation publish-lifecycle-repair --repo <r> [--write]",
        "automation publish-recovery --repo <r> [--write]",
        "automation reconcile [--lane host-review|next-slice|all] [--write]",
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
            ["run"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["start"] = RunStartCommand.Execute,
                ["supervise"] = RunSuperviseCommand.Execute,
                ["submit"] = RunSubmitCommand.Execute,
                ["resubmit"] = RunResubmitCommand.Execute,
                ["rereview"] = RunRereviewCommand.Execute,
                ["resume"] = RunResumeCommand.Execute,
                ["log"] = RunLogCommand.Execute,
                ["implement"] = RunImplementCommand.Execute,
                ["fix"] = RunFixCommand.Execute
            },
            ["review"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["run"] = ReviewRunCommand.Execute,
                ["comment"] = ReviewCommentCommand.Execute,
                ["accept"] = ReviewAcceptCommand.Execute,
                ["closeout-plan"] = ReviewCloseoutPlanCommand.Execute
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
                ["transition"] = QueueTransitionCommand.Execute
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
                ["intent-repair"] = BugIntentRepairCommand.Execute,
                ["intent-issue"] = BugIntentIssueCommand.Execute,
                ["intent-enqueue"] = BugIntentEnqueueCommand.Execute,
                ["intent-start"] = BugIntentStartCommand.Execute,
                ["intent-submit"] = BugIntentSubmitCommand.Execute,
                ["intent-review"] = BugIntentReviewCommand.Execute,
                ["intent-comment"] = BugIntentCommentCommand.Execute,
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
                ["complete"] = AutomationCompleteCommand.Execute,
                ["doctor"] = AutomationDoctorCommand.Execute,
                ["host-review-preflight"] = AutomationHostReviewPreflightCommand.Execute,
                ["host-review-diagnostics"] = AutomationHostReviewDiagnosticsCommand.Execute,
                ["host-loop-next-action"] = AutomationHostLoopNextActionCommand.Execute,
                ["host-sync-preflight"] = AutomationHostSyncPreflightCommand.Execute,
                ["issue-publish"] = AutomationIssuePublishCommand.Execute,
                ["pr-transition"] = AutomationPrTransitionCommand.Execute,
                ["publish-lifecycle-repair"] = AutomationPublishLifecycleRepairCommand.Execute,
                ["publish-recovery"] = AutomationPublishRecoveryCommand.Execute,
                ["reconcile"] = AutomationReconcileCommand.Execute,
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
                ["complete"] = WorkerCompleteCommand.Execute
            },
            ["metadata"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["validate"] = MetadataValidateCommand.Execute,
                ["update"] = MetadataUpdateCommand.Execute
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
            ["intake"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["init"] = IntakeInitCommand.Execute,
                ["concept"] = IntakeConceptCommand.Execute,
                ["interview"] = IntakeInterviewCommand.Execute,
                ["compile"] = IntakeCompileCommand.Execute,
                ["foldin"] = IntakeFoldinCommand.Execute,
                ["patch"] = IntakePatchCommand.Execute,
                ["apply"] = IntakeApplyCommand.Execute,
                ["execution"] = IntakeExecutionCommand.Execute,
                ["advance"] = IntakeAdvanceCommand.Execute,
                ["activate"] = IntakeActivateCommand.Execute,
                ["issue"] = IntakeIssueCommand.Execute,
                ["enqueue"] = IntakeEnqueueCommand.Execute,
                ["autostart"] = IntakeAutostartCommand.Execute,
                ["launch"] = IntakeLaunchCommand.Execute,
                ["start"] = IntakeStartCommand.Execute
            },
            ["guide"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["oneshot"] = GuideOneshotCommand.Execute,
                ["automation"] = GuideAutomationCommand.Execute,
                ["review"] = GuideReviewCommand.Execute,
                ["collaborate"] = GuideCollaborateCommand.Execute,
                ["rules"] = GuideRulesCommand.Execute,
                ["workflow"] = GuideWorkflowCommand.Execute,
                ["model"] = GuideModelCommand.Execute,
                ["commands"] = GuideCommandsCommand.Execute,
                ["onboarding"] = GuideOnboardingCommand.Execute,
                ["intent-work"] = GuideIntentWorkCommand.Execute,
                ["worker"] = GuideWorkerCommand.Execute,
                ["closeout"] = GuideCloseoutCommand.Execute,
                ["prompt-matrix"] = GuidePromptMatrixCommand.Execute
            },
            ["intent"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["init"] = IntentInitCommand.Execute,
                ["status"] = IntentStatusCommand.Execute,
                ["search"] = IntentSearchCommand.Execute,
                ["explain"] = IntentExplainCommand.Execute,
                ["next-slice"] = IntentNextSliceCommand.Execute,
                ["draft-from-interview"] = IntentDraftFromInterviewCommand.Execute
            },
            ["packet"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["draft"] = PacketDraftCommand.Execute
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

        if (args.Length == 0
            || (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal)))
        {
            WriteHelp(writer);
            return 0;
        }

        if (string.Equals(args[0], GenerateFromCurrentCommandName, StringComparison.Ordinal))
        {
            return GenerateFromCurrentCommand.Execute(context, args[1..], writer);
        }

        if (args.Length == 1 && string.Equals(args[0], "run", StringComparison.Ordinal))
        {
            return RunCommand.Execute(context, [], writer);
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

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Available command groups:");
        foreach (var group in CommandGroups)
        {
            writer.WriteLine($"- {group}");
        }

        writer.WriteLine("Additional top-level commands:");
        writer.WriteLine($"- {GenerateFromCurrentCommandName}");

        writer.WriteLine();
        writer.WriteLine("Automation commands:");
        foreach (var command in AutomationCommandHelp)
        {
            writer.WriteLine($"- {command}");
        }

        writer.WriteLine();
        foreach (var line in RunRoleNote)
        {
            writer.WriteLine(line);
        }
    }

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
