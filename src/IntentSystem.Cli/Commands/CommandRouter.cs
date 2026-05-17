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
        "task",
        "worker",
        "metadata",
        "guide",
        "intent",
        "packet",
        "closeout",
        "migrate"
    ];

    private static readonly IReadOnlyList<string> AutomationCommandHelp =
    [
        "automation base-branch-check --repo <r> --pr <n> --actual-base <branch> [--policy direct-main|main-ai]",
        "automation check",
        "automation clarification-stop",
        "automation complete",
        "automation doctor",
        "automation durable-state-preflight [--format markdown|json]",
        "automation host-loop-next-action --repo <r> [--sync-classification <c>] [--publish-recovery-repairs <N>] [--next-slice-issue-cut-ready] [--publish-next-execution-unit <u>]",
        "automation host-queue-item-recovery --repo <r> [--unit <u>] [--issue <n>] [--pr <m>] [--write]",
        "automation host-review-preflight",
        "automation host-review-diagnostics",
        "automation host-sync-preflight",
        "automation intent-target-gap-recovery --repo <r> [--write]",
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
                ["closeout-drift-check"] = AutomationCloseoutDriftCheckCommand.Execute,
                ["complete"] = AutomationCompleteCommand.Execute,
                ["doctor"] = AutomationDoctorCommand.Execute,
                ["durable-state-preflight"] = AutomationDurableStatePreflightCommand.Execute,
                ["host-review-preflight"] = AutomationHostReviewPreflightCommand.Execute,
                ["host-review-diagnostics"] = AutomationHostReviewDiagnosticsCommand.Execute,
                ["host-loop-next-action"] = AutomationHostLoopNextActionCommand.Execute,
                ["host-queue-item-recovery"] = AutomationHostQueueItemRecoveryCommand.Execute,
                ["host-sync-preflight"] = AutomationHostSyncPreflightCommand.Execute,
                ["intent-target-gap-recovery"] = AutomationIntentTargetGapRecoveryCommand.Execute,
                ["issue-publish"] = AutomationIssuePublishCommand.Execute,
                ["pr-transition"] = AutomationPrTransitionCommand.Execute,
                ["publish-lifecycle-repair"] = AutomationPublishLifecycleRepairCommand.Execute,
                ["publish-recovery"] = AutomationPublishRecoveryCommand.Execute,
                ["queue-seed-from-packet"] = AutomationQueueSeedFromPacketCommand.Execute,
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
                ["prompt-matrix"] = GuidePromptMatrixCommand.Execute,
                // G326: role-scoped host ownership model.
                ["host-ownership"] = GuideHostOwnershipCommand.Execute,
                // G334: self-discovery help surface for external users.
                ["help"] = GuideHelpCommand.Execute
            },
            ["intent"] = new Dictionary<string, CommandHandler>(StringComparer.Ordinal)
            {
                ["host-check"] = IntentHostCheckCommand.Execute,
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
        writer.WriteLine("- Child implementation loops MUST NOT inspect or mutate parent host queue-state, runs logs, packet directories, intent tree, review-runtime state, local rules, or local skills (G300 / G330 / G333).");
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
            ["intake"] = "Older concept-intake surface; prefer `guide collaborate` + `interview record-answer` for new flows.",
            ["task"] = "`intent-cli task issue-to-pr --repo <r> --issue <n> --workdir <path>` (controller-driven planners).",
            ["tasking"] = "`intent-cli tasking handoff` / `task-packet` / `handoff-bundle` (cross-thread tasking; experimental).",
            ["safety"] = "`intent-cli safety nested-provider-handoff` (artifact-only nested-provider handoffs).",
            ["projection"] = "`intent-cli projection generate` / `regenerate` (internal tooling).",
            ["project"] = "`intent-cli project status` (older surface; prefer `intent status`).",
            ["run"] = "`intent-cli run ...` is integration smoke / replay / dogfooding — NOT the primary chat-first path."
        };

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Available command groups:");
        foreach (var group in CommandGroups)
        {
            writer.WriteLine($"- {group}");
        }

        writer.WriteLine("Additional top-level commands:");
        writer.WriteLine($"- {GenerateFromCurrentCommandName}");

        // G334: top-level help must point an external agent at the
        // canonical workflow entries (init / interview / packet /
        // issue / automation / bug repair) so self-discovery does not
        // depend on local rules or copied skill prompts.
        writer.WriteLine();
        writer.WriteLine("Workflow guides:");
        foreach (var line in WorkflowGuidePointersHelp)
        {
            writer.WriteLine($"- {line}");
        }

        writer.WriteLine();
        writer.WriteLine("Per-group help:");
        writer.WriteLine("- `intent-cli <group> --help` (e.g. `intent-cli guide --help`, `intent-cli metadata --help`, `intent-cli migrate --help`) — list the group's subcommands.");
        writer.WriteLine("- `intent-cli guide help [--format markdown|json]` — external-user self-discovery surface with examples + workflow guide pointers.");
        writer.WriteLine("- `intent-cli guide commands list [--format markdown|json]` — full catalog with primary/support/advanced/experimental classification.");

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
        "bug-to-intent-repair — `intent-cli guide workflow task bug-to-intent-repair --format json` (report → triage → plan → intent-repair → implementation-repair chain; classifies implementation-mismatch / intent-gap / packet-gap / rule-gap / metadata-workflow-gap; recommends packet creation when the bug is in intent-cli rules/guidance, G339)."
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
