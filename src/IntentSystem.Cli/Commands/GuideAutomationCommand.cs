namespace IntentSystem.Cli.Commands;

/// <summary>
/// G240: Read-only <c>intent-cli guide automation</c> command. Emits the
/// current recurring host review/next-slice or child implement/update
/// automation setup prompt so operators do not need to manually open files
/// under <c>intents/rules/automations</c>. The command never launches an AI
/// provider and does not mutate queue state, runs, GitHub, packet files, or
/// other on-disk state.
/// </summary>
internal static class GuideAutomationCommand
{
    private const string KindHostReview = "host-review";
    private const string KindChildImplementUpdate = "child-implement-update";

    private const string DomainIntentCli = "intent-cli";
    private const string DomainSekibanAsAService = "sekiban-as-a-service";

    private const string RepoIntentSystem = "J-Tech-Japan/intent-system";
    private const string RepoSekibanAsAService = "J-Tech-Japan/SekibanAsAService";

    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide automation --kind <host-review|child-implement-update> "
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

        // G260: nested `guide automation setup ...` paste-ready setup-prompt subcommand.
        if (args.Length >= 1 && string.Equals(args[0], "setup", StringComparison.Ordinal))
        {
            return GuideAutomationSetupCommand.Execute(context, args[1..], writer);
        }

        // G274: nested `guide automation local-loop ...` agent-specific local recurring loop guide.
        if (args.Length >= 1 && string.Equals(args[0], "local-loop", StringComparison.Ordinal))
        {
            return GuideAutomationLocalLoopCommand.Execute(context, args[1..], writer);
        }

        // G322: nested `guide automation lint ...` validates a generated
        // contract against the required safety clauses.
        if (args.Length >= 1 && string.Equals(args[0], "lint", StringComparison.Ordinal))
        {
            return GuideAutomationLintCommand.Execute(context, args[1..], writer);
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
            case KindHostReview:
                return EmitHostReviewPrompt(domain, writer);

            case KindChildImplementUpdate:
                return EmitChildImplementUpdatePrompt(repo, writer);

            default:
                writer.WriteLine(
                    $"--kind must be '{KindHostReview}' or '{KindChildImplementUpdate}' (got '{kind}').");
                writer.WriteLine(UsageLine);
                return 1;
        }
    }

    private static int EmitHostReviewPrompt(string? domain, TextWriter writer)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            writer.WriteLine($"--domain is required for --kind {KindHostReview}.");
            writer.WriteLine(UsageLine);
            return 1;
        }

        var prompt = domain switch
        {
            DomainIntentCli => HostReviewIntentCliPrompt,
            DomainSekibanAsAService => HostReviewSekibanAsAServicePrompt,
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

    private static int EmitChildImplementUpdatePrompt(string? repo, TextWriter writer)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            writer.WriteLine($"--repo is required for --kind {KindChildImplementUpdate}.");
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

        writer.WriteLine(ChildImplementUpdateHeader);
        writer.WriteLine();
        writer.WriteLine(ChildImplementUpdateBody);
        writer.WriteLine();
        writer.WriteLine($"## Notes for `{repo}`");
        writer.WriteLine();
        writer.WriteLine($"Run this loop from a `{repo}` child worktree root.");
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
        writer.WriteLine("guide automation");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Emits the current checked-in recurring automation setup prompt for the requested kind.");
        writer.WriteLine();
        writer.WriteLine("Supported kinds:");
        writer.WriteLine($"- {KindHostReview} (requires --domain)");
        writer.WriteLine($"- {KindChildImplementUpdate} (requires --repo)");
        writer.WriteLine();
        writer.WriteLine("Supported host domains:");
        writer.WriteLine($"- {DomainIntentCli}");
        writer.WriteLine($"- {DomainSekibanAsAService}");
        writer.WriteLine();
        writer.WriteLine("Supported child repos:");
        writer.WriteLine($"- {RepoIntentSystem}");
        writer.WriteLine($"- {RepoSekibanAsAService}");
    }

    internal const string HostReviewIntentCliPrompt =
"""
# Automation: Host Review & Next Slice — intent-cli

Run MyIntentHost host-side review and next-slice on a recurring schedule. Each wake performs Stage 1 (review/closeout) then Stage 2 (next-slice). At most one PR review and at most one new child issue per wake.

Cwd / repo:
- host repo root: cwd at start; confirm with `pwd` and capture `HOST_ROOT="$PWD"` before entering any subdirectory
- domain: `intent-cli`
- child repo: `J-Tech-Japan/intent-system`
- child submodule: `submodules/intent-system`

Current baseline:
- Use the host-local installed `intent-cli` on PATH or at `$HOST_ROOT/.intent-cli/bin/intent-cli`.
- First run `intent-cli automation summary --format text` and use it as the label-contract source.
- Use installed `intent-cli` commands for command surfaces that exist and work; do not invent raw `gh` fallback for intent-cli-owned transitions.
- `intent-cli` must not launch Claude, Codex, or any AI provider.

Wake contract:
1. Confirm cwd is the host repo root; capture `HOST_ROOT="$PWD"`.
2. Run `git pull --ff-only origin main`.
3. Run `git submodule update --init submodules/intent-system`.
4. Read `intents/intent-cli/automation/bindings.md` (if present).
5. Stage 1: review at most one open `intent-target` PR. Use `intent-cli automation host-review-preflight` and `intent-cli automation pr-transition` for supported transitions.
6. If review passes: merge the PR, close the linked issue, sync the child submodule, update parent queue/runs.
7. If review requires repair: leave an actionable PR comment and move the PR to `intent-pr-request-update` via the installed transition.
8. Stage 2: run `intent-cli intent next-slice --dry-run --domain intent-cli --target-repo J-Tech-Japan/intent-system --format json`. Honor the WIP cap — do not cut a new child issue while any open child issue/PR carries `intent-target`.
9. If next-slice is clear and the WIP cap is empty, publish exactly one new child issue and apply `intent-target` only after parent state is durable.
10. If clarification is required, stop and report: background, question, options, pros/cons, and recommendation.
11. Commit and push parent host changes directly to `main` per `AGENTS.md`. Do not create a PR.

Hard rules:
- At most one PR review per wake; at most one issue cut per wake.
- `intent-pr-created` is issue-only; never apply it to a PR.
- Raw/manual target-repository label mutation is forbidden, and host-state publication/linkage remains host-owned. The sole child exception is the installed canonical `worker complete --kind issue --outcome pr-created --github-only --write`, which may apply target-repository `intent-target` to the PR; `intent-pr-created` remains on the source issue and is never applied to the PR. For PR `repair-pushed`, canonical child completion changes only PR repair labels.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.

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

    internal const string HostReviewSekibanAsAServicePrompt =
"""
# Automation: Host Review & Next Slice — sekiban-as-a-service

Run MyIntentHost host-side review and next-slice on a recurring schedule. Each wake performs Stage 1 (review/closeout) then Stage 2 (next-slice). At most one PR review and at most one new child issue per wake.

Cwd / repo:
- host repo root: cwd at start; confirm with `pwd` and capture `HOST_ROOT="$PWD"` before entering any subdirectory
- domain: `sekiban-as-a-service`
- child repo: `J-Tech-Japan/SekibanAsAService`
- child submodule: `submodules/SekibanAsAService`

Current baseline:
- Use the host-local installed `intent-cli` on PATH or at `$HOST_ROOT/.intent-cli/bin/intent-cli`.
- First run `intent-cli automation summary --format text` and use it as the label-contract source.
- Use installed `intent-cli` commands for command surfaces that exist and work; do not invent raw `gh` fallback for intent-cli-owned transitions.
- `intent-cli` must not launch Claude, Codex, or any AI provider.

Wake contract:
1. Confirm cwd is the host repo root; capture `HOST_ROOT="$PWD"`.
2. Run `git pull --ff-only origin main`.
3. Run `git submodule update --init submodules/SekibanAsAService`.
4. Read `intents/sekiban-as-a-service/automation/bindings.md` (if present).
5. Stage 1: review at most one open `intent-target` PR. Use `intent-cli automation host-review-preflight` and `intent-cli automation pr-transition` for supported transitions.
6. If review passes: merge the PR, close the linked issue, sync the child submodule, update parent queue/runs.
7. If review requires repair: leave an actionable PR comment and move the PR to `intent-pr-request-update` via the installed transition.
8. Stage 2: run `intent-cli intent next-slice --dry-run --domain sekiban-as-a-service --target-repo J-Tech-Japan/SekibanAsAService --format json`. Honor the WIP cap — do not cut a new child issue while any open child issue/PR carries `intent-target`.
9. If next-slice is clear and the WIP cap is empty, publish exactly one new child issue and apply `intent-target` only after parent state is durable.
10. If clarification is required, stop and report: background, question, options, pros/cons, and recommendation.
11. Commit and push parent host changes directly to `main` per `AGENTS.md`. Do not create a PR.

Hard rules:
- At most one PR review per wake; at most one issue cut per wake.
- `intent-pr-created` is issue-only; never apply it to a PR.
- Raw/manual target-repository label mutation is forbidden, and host-state publication/linkage remains host-owned. The sole child exception is the installed canonical `worker complete --kind issue --outcome pr-created --github-only --write`, which may apply target-repository `intent-target` to the PR; `intent-pr-created` remains on the source issue and is never applied to the PR. For PR `repair-pushed`, canonical child completion changes only PR repair labels.
- Stop on Hard Clarification rather than guessing when source-of-truth is ambiguous.

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

    internal const string ChildImplementUpdateHeader = "# Automation: Child Implement & Update Loop";

    internal const string ChildImplementUpdateBody =
$"""
Run one wake of the child coding automation loop on a recurring schedule. Each wake handles at most one action.

Assumptions:
- Current working directory is the target repository worktree root.
- `intent-cli` must be available on PATH.
- Use `gh` CLI for GitHub operations.
- Do not use `gh-issue-to-pr`, `gh-fix-pr-comment`, or any local skill file that restates workflow; use `intent-cli guide ...` instead.
- {DispatcherSkillCarveOut.Sentence}
- Do not use GitHub MCP.
- Do not call `intent-cli run`.
- Do not run `dotnet run` as a fallback for `intent-cli`.
- Do not ask `intent-cli` to launch Claude, Codex, or any AI provider.
- Handle at most one action per wake.
- Do not manually select issues or PRs by reading labels.
- Label state is owned by `intent-cli`; implementation skills must not add/remove/propagate workflow labels unless `intent-cli` explicitly recommends it.
- The current worktree is a dedicated automation worktree. Unsaved local changes in this child worktree and its submodules may be discarded before selection.

Wake steps:
1. Confirm the current directory is a git worktree root. If not, stop with status `wrong-worktree`.
2. Save the child worktree path: `CHILD_WORKDIR="$PWD"`.
3. Confirm `intent-cli` is available with `command -v intent-cli`. If not found, stop with status `intent-cli-not-found`. Do not fall back to `dotnet run`.
4. Resolve the target GitHub repo from the child worktree using `gh repo view --json nameWithOwner --jq .nameWithOwner` (with a `git remote get-url origin` fallback).
5. In the child worktree, run `git fetch --all --prune` and `git status --short`. If dirty, treat the diff as previous wake residue and clean before selector / claim / complete / push / label mutation:

   ```bash
   DIRTY_BEFORE="$(git status --short)"
   if [ -n "$DIRTY_BEFORE" ]; then
     git reset --hard
     git clean -fd
     git submodule foreach --recursive 'git reset --hard && git clean -fd'
     git submodule update --init --recursive
   fi
   ```

   Never use `git clean -fdx`. Never run this cleanup in a non-dedicated personal checkout.
6. From the parent host root, run `intent-cli worker next-action --repo "$REPO" --workdir "$CHILD_WORKDIR" --format json`.
7. Dispatch on `action`.
8. If `action` is `none`: stop with status `idle`. Do not push. Do not mutate labels.
9. If `action` is `issue-to-pr`:
   - Claim with `intent-cli worker claim --kind issue --number <n> --write --format json`.
   - Follow the issue-to-PR workflow for the returned issue URL. Do not use the `gh-issue-to-pr` local skill.
   - Use the issue body as the standalone contract; do not read parent host packet files to fill gaps.
   - Create at most one draft PR. Do not manually add workflow labels: issue-to-PR canonical completion may apply target-repository `intent-target` to the PR, while `intent-pr-created` remains on the source issue.
   - Classify the outcome as one of: `pr-created`, `declined-contract-incomplete`, `clarification-required`, `already-resolved`, `failed`, `label-cleanup-required`.
   - From the parent host root run `intent-cli worker result-summary --kind issue-to-pr ...` then `intent-cli worker complete --kind issue --number <n> --outcome <outcome> --write --format json`.
10. If `action` is `pr-comment-fix`:
    - Claim with `intent-cli worker claim --kind pr --number <n> --write --format json`.
    - Follow the PR-comment-fix workflow for the returned PR URL. Do not use the `gh-fix-pr-comment` local skill.
    - Repair only the narrow requested change. Push only the PR branch.
    - Classify the outcome as one of: `repair-pushed`, `no-actionable-comments`, `already-resolved`, `clarification-required`, `failed`, `label-cleanup-required`.
    - From the parent host root run `intent-cli worker result-summary --kind pr-comment-fix ...` then `intent-cli worker complete --kind pr --number <n> --outcome <outcome> --write --format json`.

Hard label rules:
- Never put `intent-pr-created` on a PR.
- Never manually add or remove `intent-target` from this implementation loop. Only canonical `worker complete --kind issue --outcome pr-created --github-only --write` may apply target-repository `intent-target`; host-state publication/linkage remains host-owned.
- Do not manually invent label transitions. Use `intent-cli` claim/complete recommendations only.

Final report must include:
- selected action
- selected URL
- outcome
- branch / PR updated or created
- validation run
- `intent-cli` warnings
- claim/complete result
- label actions applied, if any
""";
}
