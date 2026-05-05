namespace IntentSystem.Cli.Commands;

/// <summary>
/// G239: Read-only <c>intent-cli guide oneshot</c> command. Emits the
/// current host review/next-slice or child implement/update one-shot prompt
/// so operators do not need to manually open files under
/// <c>intents/rules/oneshot</c>. The command never launches an AI provider
/// and does not mutate queue state, runs, GitHub, packet files, or other
/// on-disk state.
/// </summary>
internal static class GuideOneshotCommand
{
    private const string KindHostReviewNextSlice = "host-review-next-slice";
    private const string KindChildImplementOrUpdate = "child-implement-or-update";

    private const string DomainIntentCli = "intent-cli";
    private const string DomainSekibanAsAService = "sekiban-as-a-service";

    private const string RepoIntentSystem = "J-Tech-Japan/intent-system";
    private const string RepoSekibanAsAService = "J-Tech-Japan/SekibanAsAService";

    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide oneshot --kind <host-review-next-slice|child-implement-or-update> "
        + "[--domain <intent-cli|sekiban-as-a-service>] [--repo <owner/repo>] [--format markdown]";

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

        if (!TryParseArguments(args, out var kind, out var domain, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal))
        {
            writer.WriteLine($"--format must be 'markdown' (got '{format}').");
            writer.WriteLine(UsageLine);
            return 1;
        }

        switch (kind)
        {
            case KindHostReviewNextSlice:
                return EmitHostPrompt(domain, writer);

            case KindChildImplementOrUpdate:
                return EmitChildPrompt(repo, writer);

            default:
                writer.WriteLine(
                    $"--kind must be '{KindHostReviewNextSlice}' or '{KindChildImplementOrUpdate}' (got '{kind}').");
                writer.WriteLine(UsageLine);
                return 1;
        }
    }

    private static int EmitHostPrompt(string? domain, TextWriter writer)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            writer.WriteLine($"--domain is required for --kind {KindHostReviewNextSlice}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var prompt = domain switch
        {
            DomainIntentCli => HostIntentCliPrompt,
            DomainSekibanAsAService => HostSekibanAsAServicePrompt,
            _ => null
        };

        if (prompt is null)
        {
            writer.WriteLine(
                $"Unsupported --domain '{domain}'. Supported: {DomainIntentCli}, {DomainSekibanAsAService}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        writer.WriteLine(prompt);
        return 0;
    }

    private static int EmitChildPrompt(string? repo, TextWriter writer)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            writer.WriteLine($"--repo is required for --kind {KindChildImplementOrUpdate}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!string.Equals(repo, RepoIntentSystem, StringComparison.Ordinal)
            && !string.Equals(repo, RepoSekibanAsAService, StringComparison.Ordinal))
        {
            writer.WriteLine(
                $"Unsupported --repo '{repo}'. Supported: {RepoIntentSystem}, {RepoSekibanAsAService}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        writer.WriteLine(ChildPromptHeader);
        writer.WriteLine();
        writer.WriteLine(ChildPromptBody);
        writer.WriteLine();
        writer.WriteLine($"## Notes for `{repo}`");
        writer.WriteLine();
        writer.WriteLine($"Run this prompt from a `{repo}` child worktree root.");
        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? kind,
        out string? domain,
        out string? repo,
        out string format,
        out string error)
    {
        kind = null;
        domain = null;
        repo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--kind":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--kind requires a value.";
                        return false;
                    }

                    kind = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domain = args[index + 1];
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

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown).";
                        return false;
                    }

                    format = args[index + 1];
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(kind))
        {
            error = "--kind is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide oneshot");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Emits the current checked-in one-shot prompt for the requested kind.");
        writer.WriteLine();
        writer.WriteLine("Supported kinds:");
        writer.WriteLine($"- {KindHostReviewNextSlice} (requires --domain)");
        writer.WriteLine($"- {KindChildImplementOrUpdate} (requires --repo)");
        writer.WriteLine();
        writer.WriteLine("Supported host domains:");
        writer.WriteLine($"- {DomainIntentCli}");
        writer.WriteLine($"- {DomainSekibanAsAService}");
        writer.WriteLine();
        writer.WriteLine("Supported child repos:");
        writer.WriteLine($"- {RepoIntentSystem}");
        writer.WriteLine($"- {RepoSekibanAsAService}");
    }

    internal const string HostIntentCliPrompt =
"""
# One-shot: Host Review and Next Slice — intent-cli

Run MyIntentHost host-side review & next-slice exactly once. Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup.

Domain / repo:
- host repo root: cwd at start; confirm with `pwd` before executing
- domain: `intent-cli`
- child repo: `J-Tech-Japan/intent-system`
- child submodule: `submodules/intent-system`

Current baseline:
- Use the host-local installed `intent-cli` on PATH or at `$HOST_ROOT/.intent-cli/bin/intent-cli`.
- First run `intent-cli automation summary --format text` and use it as the label-contract source.
- Use installed `intent-cli` commands for command surfaces that exist and work.
- If an installed `intent-cli` command clearly fails for a transition, stop and report the failure. Do not invent raw `gh` fallback for intent-cli-owned transitions.

Workflow:
1. Confirm cwd is the host repo root.
2. Run `git pull --ff-only origin main`.
3. Run `git submodule update --init submodules/intent-system`.
4. Read `intents/intent-cli/automation/bindings.md` (if present).
5. Execute one wake only: Stage 1 review/closeout, then Stage 2 next-slice.
6. If an eligible `intent-target` PR exists, review it deterministically against parent intent state.
7. If review passes: merge the PR, close the linked issue, sync child main/submodule, update parent queue/runs, then classify continuation.
8. If review requires repair: leave an actionable PR comment and move the PR to the request-update state according to installed/runbook-supported transition behavior.
9. If no PR is available, still run Stage 2 next-slice once.
10. Do not cut a new child issue while any open child issue/PR with `intent-target` remains.
11. If next-slice is clear and WIP cap is empty, create/publish exactly one child issue and preload future packets only when they satisfy the Child Issue Contract.
12. If clarification is required, stop and report: background, question, options, pros/cons, and recommendation.
13. Commit and push parent host changes directly to `main` per `AGENTS.md`. Do not create a PR.

Final report must include:
- selected issue / PR or none
- label transitions applied
- review result
- merge / closeout result
- next issue created/published or not
- clarification status
- validation performed
- commits pushed
""";

    internal const string HostSekibanAsAServicePrompt =
"""
# One-shot: Host Review and Next Slice — sekiban-as-a-service

Run MyIntentHost host-side review & next-slice exactly once. Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup.

Domain / repo:
- host repo root: cwd at start; confirm with `pwd` before executing
- domain: `sekiban-as-a-service`
- child repo: `J-Tech-Japan/SekibanAsAService`
- child submodule: `submodules/SekibanAsAService`

Current baseline:
- Use the host-local installed `intent-cli` on PATH or at `$HOST_ROOT/.intent-cli/bin/intent-cli`.
- First run `intent-cli automation summary --format text` and use it as the label-contract source.
- Use installed `intent-cli` commands for command surfaces that exist and work.
- If an installed `intent-cli` command clearly fails for a transition, stop and report the failure. Do not invent raw `gh` fallback for intent-cli-owned transitions.

Workflow:
1. Confirm cwd is the host repo root.
2. Run `git pull --ff-only origin main`.
3. Run `git submodule update --init submodules/SekibanAsAService`.
4. Read `intents/sekiban-as-a-service/automation/bindings.md` (if present).
5. Execute one wake only: Stage 1 review/closeout, then Stage 2 next-slice.
6. If an eligible `intent-target` PR exists, review it deterministically against parent intent state.
7. If review passes: merge the PR, close the linked issue, sync child main/submodule, update parent queue/runs, then classify continuation.
8. If review requires repair: leave an actionable PR comment and move the PR to the request-update state according to installed/runbook-supported transition behavior.
9. If no PR is available, still run Stage 2 next-slice once.
10. Do not cut a new child issue while any open child issue/PR with `intent-target` remains.
11. If next-slice is clear and WIP cap is empty, create/publish exactly one child issue and preload future packets only when they satisfy the Child Issue Contract.
12. If clarification is required, stop and report: background, question, options, pros/cons, and recommendation.
13. Commit and push parent host changes directly to `main` per `AGENTS.md`. Do not create a PR.

Final report must include:
- selected issue / PR or none
- label transitions applied
- review result
- merge / closeout result
- next issue created/published or not
- clarification status
- validation performed
- commits pushed
""";

    internal const string ChildPromptHeader = "# One-shot: Child Implement or PR Comment Update";

    internal const string ChildPromptBody =
"""
Run one child implementation/update wake exactly once. Do not create or update any automation, loop, cron, monitor, reminder, or recurring wakeup.

Repository:
- cwd is the child repository worktree root.
- derive `<OWNER>/<REPO>` from `gh repo view --json owner,name` unless an explicit repo is provided.

Current baseline:
- Use installed `intent-cli`; if it is missing, stop. Do not use `dotnet run` fallback.
- Do not use `intent-cli run`.
- Do not ask `intent-cli` to launch Claude/Codex or any AI provider.
- Do not manually reimplement label selection logic in the prompt.
- Let `intent-cli worker next-action --repo <OWNER>/<REPO> --format json` select the single target.
- Let `intent-cli worker result-summary` and `intent-cli worker complete --write` define completion/label actions where available.
- The child worker must not apply or remove `intent-target`; that is host-owned.
- `intent-pr-created` belongs to the source issue only; never apply it to a PR.

Preflight:
1. Run `git fetch --all --prune`.
2. Check `git status --short`.
3. If this is a dedicated disposable automation worktree, clean local residue before starting. If this is a personal/shared checkout, stop instead of discarding user work.
4. Ensure the worktree is clean before claim/implementation.

Selection:
1. Run:
   `intent-cli worker next-action --repo <OWNER>/<REPO> --format json`
2. If `action` is `none`, stop and report idle.
3. If `action` is `pr-comment-fix`, process only the returned PR URL/number.
4. If `action` is `issue-to-pr`, process only the returned issue URL/number.
5. If warnings are returned, include them in the final report.

For `pr-comment-fix`:
1. Claim the selected work with the installed worker command if available.
2. Inspect the selected PR and its actionable review/comment feedback.
3. Checkout the PR branch.
4. Make only the minimal repair required by the latest actionable feedback.
5. Run relevant focused validation.
6. Commit and push to the existing PR branch.
7. Add a short PR comment summarizing the fix and validation.
8. Classify the outcome as exactly one of: `repair-pushed`, `no-actionable-comments`, `already-resolved`, `clarification-required`, `failed`, or `label-cleanup-required`.
9. Run `intent-cli worker result-summary --kind pr-comment-fix --repo <OWNER>/<REPO> --pr <PR_NUMBER> --outcome <OUTCOME> --format json`.
10. Run `intent-cli worker complete --write` with the result-summary guidance when available.

For `issue-to-pr`:
1. Claim the selected work with the installed worker command if available.
2. Treat the GitHub issue body as the standalone contract.
3. Do not read parent host `.intent-cli/issues/<execution-unit>/` packets to fill missing contract details.
4. If the issue body is not standalone, decline with `declined-contract-incomplete` or `clarification-required`; do not guess.
5. Start from `origin/main`.
6. Create a new implementation branch using the agent's normal branch prefix.
7. Implement the smallest change that satisfies the Acceptance Criteria.
8. Run relevant focused validation.
9. Push the branch and create a draft PR.
10. Put `Closes #<issue>` in the PR body.
11. Do not add `intent-target` to the PR from the child loop.
12. Do not add `intent-pr-created` to the PR.
13. Classify the outcome as exactly one of: `pr-created`, `declined-contract-incomplete`, `clarification-required`, `already-resolved`, `failed`, or `label-cleanup-required`.
14. Run `intent-cli worker result-summary --kind issue-to-pr --repo <OWNER>/<REPO> --issue <ISSUE_NUMBER> --pr <PR_NUMBER> --outcome <OUTCOME> --format json`.
15. Run `intent-cli worker complete --write` with the result-summary guidance when available.

Clarification / failure:
- If clarification is needed, stop and report: background, question, options, pros/cons, and recommendation.
- Do not create a second issue or PR in the same wake.
- Do not continue to another target after success or failure.

Final report must include:
- selected action
- selected issue/PR URL
- branch / PR updated or created
- outcome classification
- validation performed
- intent-cli result-summary / completion result
- label actions applied by intent-cli, if any
- warnings or clarification needed
""";
}
