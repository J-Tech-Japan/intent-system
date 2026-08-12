using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G465: Read-only <c>intent-cli next</c> / <c>intent-cli guide next</c>.
/// The design-side <em>action advisor</em>: answers "what should I do next?" by
/// laying out the catalog of design-side processes (grill, stack, improve,
/// inspect, issue-publish, review, recovery, idle), the evidence to check
/// before choosing, and the recommendation output shape the agent fills in.
///
/// The intended user experience is a single natural-language ask:
/// <c>intent-cli に聞いて、次に何をしたらいいか教えてください。</c>
///
/// next is READ-ONLY by default: it recommends a process, it does not secretly
/// mutate packets, issues, labels, or queue state, and it never launches an AI
/// provider. Host-state-free by default: when a domain and team are supplied,
/// it also reads the recorded supervision cycle for that team without
/// mutating state.
/// </summary>
internal static class GuideNextCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli next [--domain <name>] [--team <name>] [--target-repo <owner/repo>] [--role <role>] [--format markdown|json]  (alias: intent-cli guide next)";

    /// <summary>The shortest natural-language ask that triggers the advisor.</summary>
    public const string ShortPrompt = "intent-cli に聞いて、次に何をしたらいいか教えてください。";

    // Design-side actions in the decision set.
    public const string ActionGrill = "grill";
    public const string ActionStack = "stack";
    public const string ActionImprove = "improve";
    public const string ActionInspect = "inspect";
    public const string ActionIssuePublish = "issue-publish";
    public const string ActionReview = "review";
    public const string ActionRecovery = "recovery";
    public const string ActionIdle = "idle";
    public const string ActionSupervisionSetup = "supervision-setup";
    public const string ActionRealignment = "realignment";
    public const string ActionBootstrapResume = "bootstrap-resume";

    internal static Func<DateTimeOffset> UtcNowFactory { get; set; } = () => DateTimeOffset.UtcNow;

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

        if (!TryParseArguments(args, out var domain, out var team, out var targetRepo, out var role, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = BuildResult(context, domain, team, targetRepo, role ?? "design");
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

    internal static GuideNextResult BuildResult(string? domain, string? targetRepo)
        => BuildResult(context: null, domain: domain, team: null, targetRepo: targetRepo, invokingRole: "design");

    internal static GuideNextResult BuildResult(
        CliContext? context,
        string? domain,
        string? team,
        string? targetRepo,
        string? invokingRole = "design")
    {
        var domainArg = string.IsNullOrWhiteSpace(domain) ? "<domain>" : domain!;
        var teamArg = string.IsNullOrWhiteSpace(team) ? "<team>" : team!;
        var repoArg = string.IsNullOrWhiteSpace(targetRepo) ? "<owner/repo>" : targetRepo!;
        var supervision = ReadSupervisionStatus(context, domain, team);
        var realignment = ReadRealignmentStatus(context, domain);
        var bootstrap = ReadBootstrapStatus(context, domain, team);
        var normalizedRole = GuideRoleContractGuidance.Normalize(invokingRole);
        var roleContractFirst = GuideRoleContractGuidance.Resolve(invokingRole);

        var prompt =
$@"Advise the design thread on what to do next for `{domainArg}` ({repoArg}). This is READ-ONLY: recommend ONE design-side process, do not mutate packets / issues / labels / queue state, and never launch an AI provider.

1. Check the evidence (below) — current intents, open questions, packet backlog, open PRs / review state, and CLI / queue health — before recommending.
2. Use `{GuideDesignThreadCommand.CommandName}` as the design-role operating contract. Its four-outcome wake rule governs whether this wake has an outcome at all.
3. Before starting a named execution unit, acquire `execution-unit:<EU>` with `intent-cli claim acquire`; before authoring release preparation, acquire `release-prep:{repoArg}:<version>`. Start only when the command returns `status=acquired` and `push_succeeded=true`; a local file or commit is not ownership.
4. When a domain and team are supplied, inspect recorded topology plus the supervision cycle. A recorded topology without a completed cycle/handoff includes `bootstrap-resume` first; absent topology is silent because bootstrap has not started; a completed cycle clears the recommendation.
5. Inspect the recorded supervision cycle independently and include `supervision-setup` when no cycle is recorded.
6. When a domain is supplied, inspect the independently declared realignment window and latest durable improve-run record. If no run falls within that window, include `realignment`; judge timestamp recency only, never review quality. With no window declaration, do not invent a cadence.
7. Match the situation to exactly one action in the decision set (bootstrap-resume for a half-done bootstrap, supervision-setup when its check is missing, then realignment when its declared window is lapsed, then grill / stack / improve / inspect / issue-publish / review / recovery / idle).
8. Return the recommendation output shape: the recommended action, the reason tied to the evidence you actually checked, the evidence checked, a paste-ready suggested prompt for that action, and the safety boundary.
9. Stop there — the user decides whether to run the suggested prompt. next never auto-executes the chosen action.";

        var claimBeforeStart = new[]
        {
            "Claim before start (G679, preview-through-1.x): the design thread starts a named execution unit or release preparation only after the remote accepted the claim's plain push; a local file or commit is never ownership.",
            $"Execution unit: `intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team {teamArg} --write --format json`.",
            $"Release preparation: `intent-cli claim acquire --scope release-prep:{repoArg}:<version> --actor <actor> --team {teamArg} --write --format json`.",
            "Proceed only on `status=acquired` with `push_succeeded=true`. On `held`, stop and name `holder` plus `holder_team`; on `retry-exhausted`, stop and surface the unrelated-advance failure. Never force-push, infer ownership from age, or take over automatically.",
        };

        var evidenceToCheck = new[]
        {
            $"Current intents — `intents/{domainArg}/` MVV / product goal / intent-tree: is the intent clear, or are there open questions to extract?",
            $"Open questions / clarifications — `intents/{domainArg}/clarifications/`: unresolved blocking questions push toward grill or clarification.",
            "Packet backlog — recent `.intent-cli/issues/<unit>/` packets: is there ready work to stack, or is the backlog already drafted?",
            $"Open PRs and review state — `intent-cli guide review` inputs / GitHub PR labels for {repoArg}: a PR awaiting review pushes toward review; a request-update pushes toward recovery/comment-fix.",
            "CLI / queue health — `intent-cli automation doctor`: a stale CLI or dirty queue pushes toward recovery before anything else.",
            "Drift / short-term-loop signals — repeated corrective packets on the same surface push toward improve.",
            $"Recorded supervision cycle — when `--domain {domainArg} --team {teamArg}` is supplied, read the team's append-only supervision state; no recorded cycle is a setup gap, while an existing cycle keeps the setup recommendation silent.",
            $"Bootstrap completion — when `--domain {domainArg} --team {teamArg}` is supplied, read the recorded topology and supervision cycle. Topology with no completed cycle/handoff is half-done and routes to `{GuideBootstrapCommand.CommandName}`; absent topology and a completed cycle are silent.",
            $"Improve-run recency — when `--domain {domainArg}` is supplied, read `.intent-cli/improve/{domainArg}/window.json` and `runs.jsonl`; compare only the latest run timestamp with the independently declared realignment window. A missing/aged run recommends realignment; a fresh record is immediately silent. Never infer quality from age.",
        };

        var decisionSet = new List<GuideNextAction>
        {
            new GuideNextAction
            {
                Action = ActionGrill,
                WhenToChoose = "The intent is still fuzzy — there are open product / technical / operational / verification questions to extract before any packet is cut. Persistent interview mode (G463).",
                SuggestedPrompt = $"intent-cli で <topic> を grill してください。（`intent-cli grill --domain {domainArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionStack,
                WhenToChoose = "The intent is clear and there is ready work to package — create an ordered packet backlog and publish the first issue. Forward planning (G464).",
                SuggestedPrompt = $"intent-cli で stack を実行してください。（`intent-cli stack --domain {domainArg} --target-repo {repoArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionImprove,
                WhenToChoose = "Recent work has drifted from MVV / ADR / intent tree, or a short-term-loop / repeated-patch pattern is showing. Retrospective realignment (G456).",
                SuggestedPrompt = $"intent-cli で improve プロセスを実行してください。（`intent-cli improve --domain {domainArg} --format markdown`）",
            },
            new GuideNextAction
            {
                Action = ActionInspect,
                WhenToChoose = "You need to observe what the product ACTUALLY does before deciding — evidence-backed observation of real app / CLI / UI / log / test behavior, separating observed evidence from inference and turning gaps into packet candidates (G466). This is NOT status / next-slice checking; for a quick read-only state summary use `intent-cli status brief` / `intent intent status` directly.",
                SuggestedPrompt = $"intent-cli で <target> を inspect してください。（`intent-cli inspect --domain {domainArg} --target-repo {repoArg} --format markdown`, alias `intent-cli guide inspect`）",
            },
            new GuideNextAction
            {
                Action = ActionIssuePublish,
                WhenToChoose = "A reviewed, contract-complete packet is ready to become a GitHub issue — publish through the normal boundary (host applies intent-target).",
                SuggestedPrompt = $"`intent-cli issue publish-flow <id> --repo {repoArg} --write --format json` then `intent-cli automation issue-publish --write`",
            },
            new GuideNextAction
            {
                Action = ActionReview,
                WhenToChoose = "An implementation PR is open and awaiting design/host review against its packet and intent.",
                SuggestedPrompt = $"`intent-cli guide review --pr <n> --repo {repoArg} --domain {domainArg} --format json`",
            },
            new GuideNextAction
            {
                Action = ActionRecovery,
                WhenToChoose = "The CLI is stale, the queue/labels are inconsistent, or a publish/closeout is stuck — repair operational state before design work.",
                SuggestedPrompt = $"`intent-cli automation doctor --format json` then `intent-cli automation reconcile` / `intent-cli automation publish-recovery` as indicated.",
            },
            new GuideNextAction
            {
                Action = ActionIdle,
                WhenToChoose = "Nothing is actionable on the design side right now — the backlog is drained, PRs are with the host, and no drift or open question is pending. Stop and wait.",
                SuggestedPrompt = "（no action — report idle and wait for the next design input or host hand-off）",
            },
        };

        if (supervision.SetupRecommended)
        {
            decisionSet.Insert(0, new GuideNextAction
            {
                Action = ActionSupervisionSetup,
                WhenToChoose = $"No completed supervision cycle is recorded for team `{team}`. Set up the team's standing supervision loop before relying on bounded recovery; the setup guidance states who owns it and where it runs. {SupervisionGuideText.DeploymentBasis}",
                SuggestedPrompt = SupervisionGuideText.NextAction(domainArg, teamArg, repoArg),
            });
        }

        if (bootstrap.ResumeRecommended)
        {
            decisionSet.Insert(0, new GuideNextAction
            {
                Action = ActionBootstrapResume,
                WhenToChoose = $"Team `{team}` has recorded topology but no completed supervision cycle and application-front-door handoff. Resume from named state `{bootstrap.StateName}`; preserve recorded facts and emit only missing steps.",
                SuggestedPrompt = $"`{GuideBootstrapCommand.CommandName} --domain {domainArg} --team {teamArg} --target-repo {repoArg} --routing-root {context?.RepoRoot ?? "<routing-root>"} --format markdown`",
            });
        }

        if (realignment.RecommendationIncluded)
        {
            decisionSet.Insert((bootstrap.ResumeRecommended ? 1 : 0) + (supervision.SetupRecommended ? 1 : 0), new GuideNextAction
            {
                Action = ActionRealignment,
                WhenToChoose = $"No recorded improve run is within the latest declared {realignment.WindowDays}-day realignment window. This is timestamp recency only; it is not a judgment that the previous review was poor or incomplete.",
                SuggestedPrompt = $"`intent-cli improve --domain {domainArg} --format markdown`; after the human/agent review, record it with `intent-cli improve record --domain {domainArg} --mode implementation-aware --artifact <touched-path> [--artifact <touched-path> ...] --write --format json`.",
            });
        }

        return new GuideNextResult
        {
            Process = "design-action-next-advisor",
            Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            Team = string.IsNullOrWhiteSpace(team) ? null : team,
            TargetRepo = string.IsNullOrWhiteSpace(targetRepo) ? null : targetRepo,
            Supervision = supervision,
            Realignment = realignment,
            Bootstrap = bootstrap,
            InvokingRole = normalizedRole,
            RoleContractFirst = roleContractFirst,
            DesignRoleGuide = GuideDesignThreadCommand.CommandName,
            ShortPrompt = ShortPrompt,
            ReadOnly = true,
            Summary =
                "next is the design-side action advisor: ask it what to do next and it lays out the catalog of design-side "
                + "processes (bootstrap-resume for recorded-topology half-done state, supervision-setup when no recorded cycle, realignment when a declared improve window lapses, grill, stack, improve, inspect, issue-publish, review, recovery, idle), the evidence to check first, "
                + "and the recommendation output shape. It recommends ONE process tied to the evidence; it is read-only by default "
                + "and never auto-executes the chosen action — the user decides whether to run the suggested prompt.",
            MeasuredIncident = GuideRoleContractGuidance.MeasuredIncident,
            ClaimBeforeStart = claimBeforeStart,
            NotThis = new[]
            {
                "next does NOT auto-execute the selected action — it recommends; the user runs the suggested prompt.",
                "next is READ-ONLY by default — it does not mutate packets, issues, labels, or queue state.",
                "next does NOT replace the host / review / worker loops — it advises the design thread, it does not drive operational automation.",
                "intent-cli does NOT launch Claude/Codex/Copilot or any AI provider; the AI agent owns the semantic decision and conversation.",
                "next does NOT start or manage the supervision process; it only detects a missing recorded cycle and recommends setup.",
                "next does NOT create or join a team; bootstrap discovery only reads recorded topology and supervision state and recommends the render-only bootstrap guide.",
                "next does NOT schedule, cron, or auto-run improve and does not create a stalled-work debt class; it only compares the latest recorded run timestamp with the independently declared window.",
                "next does NOT grade realignment quality. The review remains human/agent semantic work; recency is the only machine judgment.",
            },
            DoNotSubstitute = new[]
            {
                "When the user asks what to do next, run `intent-cli next` (or `intent-cli guide next`) and recommend ONE design-side process — do NOT silently start one.",
                "Do NOT turn next into a generic AI planner unrelated to intent-cli state — every recommendation must tie to the evidence checked.",
                "If a first-class next surface is not found in the installed CLI (e.g. `intent-cli next --help` fails), report `next advisor unavailable` and request a CLI update — do NOT silently substitute another workflow.",
            },
            EvidenceToCheck = evidenceToCheck,
            DecisionSet = decisionSet,
            RecommendationOutputShape = new[]
            {
                new GuideNextOutputField { Field = "recommended_action", Meaning = "Exactly one action id from the decision set (bootstrap-resume for a half-done bootstrap, supervision-setup when no cycle is recorded, realignment when a declared window lapses, or grill / stack / improve / inspect / issue-publish / review / recovery / idle)." },
                new GuideNextOutputField { Field = "reason", Meaning = "Why this action, tied to the specific evidence checked (cite the intent / packet / PR / health signal that drove it)." },
                new GuideNextOutputField { Field = "evidence_checked", Meaning = "The evidence actually inspected this run, so the recommendation is auditable." },
                new GuideNextOutputField { Field = "suggested_prompt", Meaning = "The paste-ready prompt / command for the recommended action that the user can run as-is." },
                new GuideNextOutputField { Field = "safety_boundary", Meaning = "The read-only / no-auto-execute boundary: next recommended, the user decides whether to run it." },
            },
            SafetyBoundary = new[]
            {
                "Read-only by default: next inspects evidence and recommends; it does not mutate packets, issues, labels, or queue state.",
                "No auto-execute: the recommended action runs only when the user chooses to run the suggested prompt.",
                "Never hand-edit workflow labels, queue-state, or publish metadata from next — those stay in the operational intent-cli surfaces.",
                "Supervision discovery is read-only: it reads the recorded cycle and never starts, stops, or manages a background process.",
                "Bootstrap discovery is read-only: it reads topology and cycle records, never invokes herdr, a scheduler, an OS command, an application integration, or an AI provider.",
                "Realignment discovery is read-only and recency-only: it reads the durable improve record, never grades review quality, and never schedules or auto-runs improve.",
            },
            Prompt = prompt,
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideNextResult result)
    {
        writer.WriteLine("# Guide next — design-side action advisor");
        writer.WriteLine();
        writer.WriteLine($"_Ask it:_ **{result.ShortPrompt}**");
        writer.WriteLine();
        if (result.RoleContractFirst is { } roleContract)
        {
            writer.WriteLine("## Read your role contract first (G672 — preview-through-1.x)");
            writer.WriteLine();
            writer.WriteLine($"- role: `{roleContract.Role}`");
            writer.WriteLine($"- operating guide: `{roleContract.Guide}`");
            writer.WriteLine($"- {roleContract.Instruction}");
            writer.WriteLine();
        }
        writer.WriteLine("## Measured incident record (G672 — preview-through-1.x)");
        writer.WriteLine();
        writer.WriteLine(result.MeasuredIncident);
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Domain))
        {
            writer.WriteLine($"- domain: {result.Domain}");
        }
        if (!string.IsNullOrWhiteSpace(result.TargetRepo))
        {
            writer.WriteLine($"- target repo: {result.TargetRepo}");
        }
        if (!string.IsNullOrWhiteSpace(result.Team))
        {
            writer.WriteLine($"- team: {result.Team}");
        }
        writer.WriteLine($"- read-only: {(result.ReadOnly ? "yes" : "no")}");
        writer.WriteLine($"- design-role operating guide: `{result.DesignRoleGuide}`");
        writer.WriteLine();

        if (result.Supervision is { Checked: true })
        {
            writer.WriteLine("## Supervision setup check");
            writer.WriteLine();
            writer.WriteLine(result.Supervision.CycleRecorded
                ? $"- recorded cycle: yes for `{result.Supervision.Team}`; supervision setup recommendation: silent"
                : result.Supervision.Error is null
                    ? $"- recorded cycle: no for `{result.Supervision.Team}`; supervision setup recommendation: **supervision-setup**"
                    : $"- recorded cycle: unavailable for `{result.Supervision.Team}`; repair the state read before deciding");
            if (result.Supervision.Error is not null)
            {
                writer.WriteLine($"- read error: {result.Supervision.Error}");
            }
            writer.WriteLine();
        }
        if (result.Bootstrap is { Checked: true })
        {
            writer.WriteLine("## Bootstrap completion check (G664 — preview-through-1.x)");
            writer.WriteLine();
            if (result.Bootstrap.Error is not null)
            {
                writer.WriteLine("- bootstrap state: unreadable; repair the recorded topology/state before deciding (fail closed)");
                writer.WriteLine($"- read error: {result.Bootstrap.Error}");
            }
            else if (!result.Bootstrap.TopologyRecorded)
            {
                writer.WriteLine("- topology recorded: no; bootstrap-resume recommendation: silent (bootstrap has not started)");
            }
            else if (result.Bootstrap.ResumeRecommended)
            {
                writer.WriteLine($"- topology recorded: yes; named state: `{result.Bootstrap.StateName}`; completed cycle/handoff: no; recommendation: **bootstrap-resume**");
            }
            else
            {
                writer.WriteLine("- topology recorded: yes; completed cycle/handoff: yes; bootstrap-resume recommendation: silent");
            }
            writer.WriteLine();
        }
        if (result.Realignment is { Checked: true })
        {
            writer.WriteLine("## Realignment recency check (G662 — preview-through-1.x)");
            writer.WriteLine();
            if (result.Realignment.Error is not null)
            {
                writer.WriteLine("- improve-run record: unreadable; repair the record read before deciding (fail closed)");
                writer.WriteLine($"- read error: {result.Realignment.Error}");
            }
            else if (!result.Realignment.Declared)
            {
                writer.WriteLine("- declared realignment window: none; recommendation: silent (do not invent a cadence)");
            }
            else if (!result.Realignment.RunRecorded && result.Realignment.RecommendationIncluded)
            {
                writer.WriteLine($"- latest run: none; declared window: {result.Realignment.WindowDays} days; recommendation: **realignment**");
            }
            else if (result.Realignment.RecommendationIncluded)
            {
                writer.WriteLine($"- latest run: {result.Realignment.LastRecordedAt:O}; declared window: {result.Realignment.WindowDays} days; age: {result.Realignment.AgeDays:F2} days; recommendation: **realignment**");
            }
            else
            {
                writer.WriteLine($"- latest run: {result.Realignment.LastRecordedAt:O}; declared window: {result.Realignment.WindowDays} days; age: {result.Realignment.AgeDays:F2} days; recommendation: silent (fresh record)");
            }
            writer.WriteLine("- judgment basis: timestamp recency only; intent-cli never grades the human/agent review's quality");
            writer.WriteLine();
        }
        writer.WriteLine(result.Summary);
        writer.WriteLine();

        writer.WriteLine("## Claim before starting named work (G679 — preview-through-1.x)");
        writer.WriteLine();
        foreach (var item in result.ClaimBeforeStart)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Procedure");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## What this is NOT");
        foreach (var item in result.NotThis)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Do not substitute another workflow");
        foreach (var item in result.DoNotSubstitute)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Evidence to check before recommending");
        foreach (var item in result.EvidenceToCheck)
        {
            writer.WriteLine($"- {item}");
        }
        writer.WriteLine();

        writer.WriteLine("## Decision set — when to choose each design-side process");
        foreach (var action in result.DecisionSet)
        {
            writer.WriteLine($"- **{action.Action}** — {action.WhenToChoose}");
            writer.WriteLine($"  - suggested prompt: {action.SuggestedPrompt}");
        }
        writer.WriteLine();

        writer.WriteLine("## Recommendation output shape");
        foreach (var field in result.RecommendationOutputShape)
        {
            writer.WriteLine($"- `{field.Field}`: {field.Meaning}");
        }
        writer.WriteLine();

        writer.WriteLine("## Safety boundary");
        foreach (var item in result.SafetyBoundary)
        {
            writer.WriteLine($"- {item}");
        }
    }

    private static bool TryParseArguments(string[] args, out string? domain, out string? team, out string? targetRepo, out string? role, out string format, out string error)
    {
        domain = null;
        team = null;
        targetRepo = null;
        role = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[index + 1].Trim();
                    index++;
                    break;

                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }
                    team = args[index + 1].Trim();
                    index++;
                    break;

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }
                    targetRepo = args[index + 1].Trim();
                    index++;
                    break;

                case "--role":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--role requires a value.";
                        return false;
                    }
                    role = args[index + 1].Trim();
                    index++;
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

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("next (alias: guide next)");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only: design-side action advisor. With --role, the invoking role's installed operating guide is the first read-before-acting instruction when that role has a contract; roles without a contract receive no invented pointer. Lays out the design-side process catalog, checks supervision setup with --domain plus --team, and compares the latest improve run with the domain's independently declared recency window. A missing/aged run yields a paste-ready realignment recommendation; a fresh record is immediately silent. Recency only: no quality grading, scheduler, cron, auto-run, or stalled-work debt class.");
        writer.WriteLine("Ask it: " + ShortPrompt);
    }

    private static GuideNextSupervisionStatus ReadSupervisionStatus(
        CliContext? context,
        string? domain,
        string? team)
    {
        if (context is null || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            return new GuideNextSupervisionStatus
            {
                Checked = false,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                Team = string.IsNullOrWhiteSpace(team) ? null : team,
            };
        }

        try
        {
            var state = NotifySupervisionStore.Read(
                context.ResolveSupervisionArtifactRootPath(),
                domain.Trim(),
                team.Trim());
            return new GuideNextSupervisionStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Team = team.Trim(),
                CycleRecorded = state.LastCycle is not null,
                SetupRecommended = state.Resolved && state.LastCycle is null,
                StateDirectory = state.Directory,
                Error = state.Resolved ? null : state.Error,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new GuideNextSupervisionStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Team = team.Trim(),
                Error = $"Supervision state could not be read: {exception.Message}",
            };
        }
    }

    private static GuideNextBootstrapStatus ReadBootstrapStatus(
        CliContext? context,
        string? domain,
        string? team)
    {
        if (context is null || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(team))
        {
            return new GuideNextBootstrapStatus
            {
                Checked = false,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
                Team = string.IsNullOrWhiteSpace(team) ? null : team,
            };
        }

        try
        {
            var state = GuideBootstrapCommand.InspectState(context, context.RepoRoot, domain, team);
            return new GuideNextBootstrapStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Team = team.Trim(),
                TopologyRecorded = state.TopologyRecorded,
                CycleRecorded = state.SupervisionCycleRecorded,
                Complete = state.Complete,
                ResumeRecommended = state.TopologyRecorded && !state.SupervisionCycleRecorded,
                StateName = state.Name,
                TopologyPath = state.TopologyPath,
                Error = state.ReadError,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new GuideNextBootstrapStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Team = team.Trim(),
                Error = $"Bootstrap state could not be read: {exception.Message}",
            };
        }
    }

    private static GuideNextRealignmentStatus ReadRealignmentStatus(CliContext? context, string? domain)
    {
        if (context is null || string.IsNullOrWhiteSpace(domain))
        {
            return new GuideNextRealignmentStatus
            {
                Checked = false,
                Domain = string.IsNullOrWhiteSpace(domain) ? null : domain,
            };
        }

        var artifactRoot = context.ResolveArtifactRootPath();
        var window = ImproveRealignmentWindowStore.Read(artifactRoot, domain.Trim());
        if (!window.Resolved)
        {
            return new GuideNextRealignmentStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                WindowPath = window.Path,
                Error = window.Error,
            };
        }

        if (window.Record is null)
        {
            return new GuideNextRealignmentStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                WindowPath = window.Path,
            };
        }

        var read = ImproveRunStore.ReadLatest(artifactRoot, domain.Trim());
        if (!read.Resolved)
        {
            return new GuideNextRealignmentStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Declared = true,
                WindowDays = window.Record.WindowDays,
                WindowPath = window.Path,
                RecordPath = read.Path,
                Error = read.Error,
            };
        }

        if (read.Latest is null)
        {
            return new GuideNextRealignmentStatus
            {
                Checked = true,
                Domain = domain.Trim(),
                Declared = true,
                WindowDays = window.Record.WindowDays,
                Lapsed = true,
                RecommendationIncluded = true,
                WindowPath = window.Path,
                RecordPath = read.Path,
            };
        }

        var age = UtcNowFactory().ToUniversalTime() - read.Latest.RecordedAt.ToUniversalTime();
        var ageDays = Math.Max(0d, age.TotalDays);
        var lapsed = ageDays > window.Record.WindowDays;
        return new GuideNextRealignmentStatus
        {
            Checked = true,
            Domain = domain.Trim(),
            Declared = true,
            RunRecorded = true,
            LastRecordedAt = read.Latest.RecordedAt,
            WindowDays = window.Record.WindowDays,
            AgeDays = ageDays,
            Lapsed = lapsed,
            RecommendationIncluded = lapsed,
            RecordPath = read.Path,
            WindowPath = window.Path,
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideNextResult
{
    [JsonPropertyName("process")]
    public required string Process { get; init; }

    [JsonPropertyName("invoking_role")]
    public string? InvokingRole { get; init; }

    [JsonPropertyName("role_contract_first")]
    public GuideRoleContractPointer? RoleContractFirst { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("target_repo")]
    public string? TargetRepo { get; init; }

    [JsonPropertyName("supervision")]
    public required GuideNextSupervisionStatus Supervision { get; init; }

    [JsonPropertyName("realignment")]
    public required GuideNextRealignmentStatus Realignment { get; init; }

    [JsonPropertyName("bootstrap")]
    public required GuideNextBootstrapStatus Bootstrap { get; init; }

    [JsonPropertyName("design_role_guide")]
    public required string DesignRoleGuide { get; init; }

    [JsonPropertyName("short_prompt")]
    public required string ShortPrompt { get; init; }

    [JsonPropertyName("read_only")]
    public required bool ReadOnly { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("measured_incident")]
    public required string MeasuredIncident { get; init; }

    [JsonPropertyName("claim_before_start")]
    public required IReadOnlyList<string> ClaimBeforeStart { get; init; }

    [JsonPropertyName("not_this")]
    public required IReadOnlyList<string> NotThis { get; init; }

    [JsonPropertyName("do_not_substitute")]
    public required IReadOnlyList<string> DoNotSubstitute { get; init; }

    [JsonPropertyName("evidence_to_check")]
    public required IReadOnlyList<string> EvidenceToCheck { get; init; }

    [JsonPropertyName("decision_set")]
    public required IReadOnlyList<GuideNextAction> DecisionSet { get; init; }

    [JsonPropertyName("recommendation_output_shape")]
    public required IReadOnlyList<GuideNextOutputField> RecommendationOutputShape { get; init; }

    [JsonPropertyName("safety_boundary")]
    public required IReadOnlyList<string> SafetyBoundary { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record GuideNextRealignmentStatus
{
    [JsonPropertyName("checked")]
    public required bool Checked { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("declared")]
    public bool Declared { get; init; }

    [JsonPropertyName("run_recorded")]
    public bool RunRecorded { get; init; }

    [JsonPropertyName("last_recorded_at")]
    public DateTimeOffset? LastRecordedAt { get; init; }

    [JsonPropertyName("window_days")]
    public int? WindowDays { get; init; }

    [JsonPropertyName("age_days")]
    public double? AgeDays { get; init; }

    [JsonPropertyName("lapsed")]
    public bool Lapsed { get; init; }

    [JsonPropertyName("recommendation_included")]
    public bool RecommendationIncluded { get; init; }

    [JsonPropertyName("record_path")]
    public string? RecordPath { get; init; }

    [JsonPropertyName("window_path")]
    public string? WindowPath { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed record GuideNextSupervisionStatus
{
    [JsonPropertyName("checked")]
    public required bool Checked { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("cycle_recorded")]
    public bool CycleRecorded { get; init; }

    [JsonPropertyName("setup_recommended")]
    public bool SetupRecommended { get; init; }

    [JsonPropertyName("state_directory")]
    public string? StateDirectory { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

internal sealed record GuideNextBootstrapStatus
{
    [JsonPropertyName("checked")] public required bool Checked { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("topology_recorded")] public bool TopologyRecorded { get; init; }
    [JsonPropertyName("cycle_recorded")] public bool CycleRecorded { get; init; }
    [JsonPropertyName("complete")] public bool Complete { get; init; }
    [JsonPropertyName("resume_recommended")] public bool ResumeRecommended { get; init; }
    [JsonPropertyName("state_name")] public string? StateName { get; init; }
    [JsonPropertyName("topology_path")] public string? TopologyPath { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
}

internal sealed record GuideNextAction
{
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("when_to_choose")]
    public required string WhenToChoose { get; init; }

    [JsonPropertyName("suggested_prompt")]
    public required string SuggestedPrompt { get; init; }
}

internal sealed record GuideNextOutputField
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("meaning")]
    public required string Meaning { get; init; }
}
