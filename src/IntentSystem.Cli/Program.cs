using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // G360: process-level `--version` / `-v` MUST exit before
            // any host-state resolution. Operators rely on
            // `intent-cli --version` to verify the installed binary
            // from any cwd (home directory, child implementation
            // repo, fresh CI machine). Routing it through the
            // bootstrap path would still emit the G299 missing-host-
            // state guidance because the command router doesn't
            // recognize it; handle it inline here instead.
            if (VersionCommand.IsVersionRequest(args))
            {
                return VersionCommand.Execute(Console.Out);
            }

            // G368: CI-packed private-preview artifacts expire 14 days
            // after their build timestamp (G367 metadata). Once the
            // expiry passes, fail closed BEFORE any workflow command or
            // host-state lookup so operators see a single, clear
            // "download a newer artifact" message regardless of the
            // current working directory. `--version` is intentionally
            // exempt (handled above) so the operator can still inspect
            // the embedded build/expiry trailer. Source builds
            // (no PrivatePreview AssemblyMetadata) pass through.
            if (PrivatePreviewExpiryGate.Check(Console.Out) == PrivatePreviewExpiryDecision.Expired)
            {
                return PrivatePreviewExpiryGate.ExpiredExitCode;
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            if (IsIntentInitCommand(args)
                || IsAutomationWorktreeCommand(args)
                || IsGuideOneshotCommand(args)
                || IsImproveCommand(args)
                || IsWorkerCommand(args)
                || IsHelpCommand(args))
            {
                return CommandRouter.Execute(args, CreateBootstrapContext(currentDirectory, args), Console.Out);
            }

            var repoRoot = RepoRootResolver.Resolve(currentDirectory);
            if (repoRoot is null)
            {
                // G299: fail-closed structured guidance instead of the bare
                // "Could not find .intent-cli directory" error. The bare
                // error encouraged agents to fall back to ordinary GitHub
                // review or raw PR comments when invoked from a child
                // implementation checkout that has no `.intent-cli/`.
                return MissingHostStateGuidance.Emit(Console.Out, args, currentDirectory);
            }

            var context = new CliContext
            {
                RepoRoot = repoRoot,
                Config = CliConfigLoader.LoadFromFile(CliRuntimeContracts.GetConfigPath(repoRoot))
            };

            return CommandRouter.Execute(args, context, Console.Out);
        }
        catch (Exception exception) when (
            exception is DirectoryNotFoundException
            or FileNotFoundException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static bool IsIntentInitCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "intent", StringComparison.Ordinal)
            && (string.Equals(args[1], "init", StringComparison.Ordinal)
                || string.Equals(args[1], "host-check", StringComparison.Ordinal));
    }

    private static bool IsAutomationWorktreeCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "automation", StringComparison.Ordinal)
            && (string.Equals(args[1], "base-branch-check", StringComparison.Ordinal)
                || string.Equals(args[1], "check", StringComparison.Ordinal)
                || string.Equals(args[1], "clarification-stop", StringComparison.Ordinal)
                || string.Equals(args[1], "closeout-drift-check", StringComparison.Ordinal)
                || string.Equals(args[1], "complete", StringComparison.Ordinal)
                || string.Equals(args[1], "doctor", StringComparison.Ordinal)
                || string.Equals(args[1], "durable-state-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "host-loop-next-action", StringComparison.Ordinal)
                || string.Equals(args[1], "host-review-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "host-review-diagnostics", StringComparison.Ordinal)
                || string.Equals(args[1], "host-sync-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "issue-publish", StringComparison.Ordinal)
                || string.Equals(args[1], "label-palette-audit", StringComparison.Ordinal)
                || string.Equals(args[1], "label-palette-sync", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-transition", StringComparison.Ordinal)
                || string.Equals(args[1], "publish-lifecycle-repair", StringComparison.Ordinal)
                || string.Equals(args[1], "publish-recovery", StringComparison.Ordinal)
                || string.Equals(args[1], "queue-seed-from-packet", StringComparison.Ordinal)
                || string.Equals(args[1], "reconcile", StringComparison.Ordinal)
                || string.Equals(args[1], "same-repo-metadata-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "summary", StringComparison.Ordinal)
                || string.Equals(args[1], "workspace-guard", StringComparison.Ordinal));
    }

    private static bool IsGuideOneshotCommand(string[] args)
    {
        // G333: child implementation loops must be able to fetch their
        // own guidance from a standalone child repo cwd that has no
        // `.intent-cli/` directory. The `guide` family is read-only —
        // it emits Markdown / JSON without touching parent durable
        // state — so every guide subcommand is safe to bootstrap from
        // an unprovisioned cwd. The previous narrow allow-list left
        // `prompt-matrix`, `host-ownership`, `intent-work`, `worker`,
        // and `closeout` fail-closed under G299, which contradicted
        // the GitHub-contract-only child worker model. The list is
        // now inclusive of every guide surface the child loop uses;
        // any future read-only guide command should keep this
        // behavior by appearing under the `guide` group with no
        // queue-state read.
        return args.Length >= 2
            && string.Equals(args[0], "guide", StringComparison.Ordinal)
            && (string.Equals(args[1], "oneshot", StringComparison.Ordinal)
                || string.Equals(args[1], "automation", StringComparison.Ordinal)
                || string.Equals(args[1], "collaborate", StringComparison.Ordinal)
                || string.Equals(args[1], "rules", StringComparison.Ordinal)
                || string.Equals(args[1], "workflow", StringComparison.Ordinal)
                || string.Equals(args[1], "model", StringComparison.Ordinal)
                // G456: design-thread improve / realignment guidance — read-only, no host state required.
                || string.Equals(args[1], "improve", StringComparison.Ordinal)
                || string.Equals(args[1], "commands", StringComparison.Ordinal)
                || string.Equals(args[1], "onboarding", StringComparison.Ordinal)
                || string.Equals(args[1], "prompt-matrix", StringComparison.Ordinal)
                || string.Equals(args[1], "host-ownership", StringComparison.Ordinal)
                || string.Equals(args[1], "intent-work", StringComparison.Ordinal)
                || string.Equals(args[1], "worker", StringComparison.Ordinal)
                || string.Equals(args[1], "closeout", StringComparison.Ordinal)
                || string.Equals(args[1], "prompt-template", StringComparison.Ordinal)
                || string.Equals(args[1], "question-style", StringComparison.Ordinal)
                || string.Equals(args[1], "interview-mode", StringComparison.Ordinal)
                || string.Equals(args[1], "interview-readiness", StringComparison.Ordinal)
                || string.Equals(args[1], "review-verification-policy", StringComparison.Ordinal)
                || string.Equals(args[1], "review", StringComparison.Ordinal)
                // G334: external-user self-discovery surface. Read-only,
                // no host state required, so the bootstrap allow-list
                // must include `guide help` (and the synonymous
                // `guide --help` form is handled by the per-group help
                // routing inside CommandRouter, which also requires
                // bootstrap-friendly entry from a child cwd).
                || string.Equals(args[1], "help", StringComparison.Ordinal)
                || string.Equals(args[1], "--help", StringComparison.Ordinal)
                // G438: external artifact intake guidance — read-only, no host state required.
                || string.Equals(args[1], "artifact-intake", StringComparison.Ordinal));
    }

    /// <summary>
    /// G457: the top-level <c>intent-cli improve</c> alias is a read-only
    /// design-thread reflection surface (delegates to
    /// <c>GuideImproveCommand</c>). Like the <c>guide improve</c> form it
    /// emits Markdown / JSON without touching parent durable state, so it
    /// must bootstrap from any cwd — including a child implementation repo
    /// with no <c>.intent-cli/</c> directory — rather than failing the
    /// host-state gate and pushing the agent toward an operational recovery
    /// command instead.
    /// </summary>
    private static bool IsImproveCommand(string[] args)
    {
        return args.Length >= 1 && string.Equals(args[0], "improve", StringComparison.Ordinal);
    }

    /// <summary>
    /// G300: child worker commands (next-action / claim / complete /
    /// result-summary / *-preflight) are GitHub-contract-only and must be
    /// runnable from a child implementation repo cwd that has no
    /// `.intent-cli/` directory. The worker family takes <c>--repo
    /// &lt;owner/repo&gt;</c> on the command line and uses installed
    /// `intent-cli` only for GitHub label transitions; parent host queue
    /// state, runs.jsonl, and intent metadata stay host-only. Bootstrapping
    /// the context with cwd as RepoRoot lets these commands run; commands
    /// that incidentally read queue-state already degrade to a warning
    /// when the file is missing (G205 / G300).
    /// </summary>
    private static bool IsWorkerCommand(string[] args)
    {
        return args.Length >= 2
            && string.Equals(args[0], "worker", StringComparison.Ordinal)
            && (string.Equals(args[1], "next-action", StringComparison.Ordinal)
                || string.Equals(args[1], "claim", StringComparison.Ordinal)
                || string.Equals(args[1], "complete", StringComparison.Ordinal)
                || string.Equals(args[1], "result-summary", StringComparison.Ordinal)
                || string.Equals(args[1], "issue-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-review-preflight", StringComparison.Ordinal)
                || string.Equals(args[1], "pr-comment-preflight", StringComparison.Ordinal));
    }

    /// <summary>
    /// G334: help-style invocations must reach CommandRouter from any
    /// cwd so external users can self-discover the surface without
    /// first cd-ing to a host repo. Every recognized help shape is
    /// read-only — the router prints group descriptors / workflow
    /// pointers / examples, never touches queue-state, never calls
    /// gh, and never launches an AI provider. Recognized shapes:
    /// <list type="bullet">
    ///   <item><c>intent-cli --help</c> (top-level help).</item>
    ///   <item><c>intent-cli &lt;group&gt;</c> (per-group help).</item>
    ///   <item><c>intent-cli &lt;group&gt; --help</c> /
    ///         <c>intent-cli &lt;group&gt; help</c> (same surface).</item>
    /// </list>
    /// The bootstrap context skips host-state resolution; the help
    /// surface does not consult host state and the per-subcommand
    /// handlers that DO consult state continue to fail-closed under
    /// G299 when reached without bootstrap.
    /// </summary>
    private static bool IsHelpCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }
        if (args.Length == 1)
        {
            // `intent-cli --help` reaches the top-level help; bare
            // `intent-cli <group>` reaches per-group help. G379:
            // `intent-cli --help-all` is the read-only full-catalog help.
            return string.Equals(args[0], "--help", StringComparison.Ordinal)
                || string.Equals(args[0], "--help-all", StringComparison.Ordinal)
                || !args[0].StartsWith('-');
        }
        if (args.Length == 2)
        {
            // G379: `intent-cli --help --all` is a read-only help shape that
            // must reach CommandRouter from any cwd (no host state needed).
            if (string.Equals(args[0], "--help", StringComparison.Ordinal)
                && string.Equals(args[1], "--all", StringComparison.Ordinal))
            {
                return true;
            }
            return !args[0].StartsWith('-')
                && (string.Equals(args[1], "--help", StringComparison.Ordinal)
                    || string.Equals(args[1], "help", StringComparison.Ordinal));
        }
        return false;
    }

    private static CliContext CreateBootstrapContext(string currentDirectory, string[] args)
    {
        var domain = args.Length >= 3 && !string.IsNullOrWhiteSpace(args[2])
            ? args[2].Trim()
            : "bootstrap";

        return new CliContext
        {
            RepoRoot = currentDirectory,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = domain,
                    ArtifactRoot = CliRuntimeContracts.IntentCliDirectoryName,
                    WorktreeRoot = CliRuntimeContracts.DefaultWorktreeRoot
                }
            }
        };
    }
}
