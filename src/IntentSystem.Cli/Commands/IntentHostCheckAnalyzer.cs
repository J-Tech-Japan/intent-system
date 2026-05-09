namespace IntentSystem.Cli.Commands;

/// <summary>
/// G309: pure analyzer that classifies whether a parent host repo is a
/// canonical, fully-initialized intent host for a given domain and
/// target repo. Inputs are deterministic file-presence flags and the
/// parsed <c>.intent-cli/host-binding.toml</c> values; the analyzer
/// returns one of six classifications with structured remediation
/// steps so a fresh agent can decide whether to proceed with
/// packet/issue work or stop and surface the gap to the operator.
///
/// Classifications, in priority order (the analyzer short-circuits on
/// the first match):
/// <list type="bullet">
/// <item><c>uninitialized</c> — no <c>.intent-cli/config.toml</c>; the host has not run <c>intent init</c>.</item>
/// <item><c>wrong-host</c> — host-binding records a different host repo than the observed git remote.</item>
/// <item><c>missing-host-binding</c> — config exists but <c>host-binding.toml</c> is absent (G301 record missing).</item>
/// <item><c>domain-not-bootstrapped</c> — host binding present but no <c>intents/&lt;domain&gt;/</c> directory.</item>
/// <item><c>partially-initialized</c> — config + binding + intents/&lt;domain&gt;/ exist but one or more
///   onboarding artifacts is missing (<c>AGENTS.md</c>, <c>CLAUDE.md</c>, <c>queue-state.json</c>, <c>runs.jsonl</c>).</item>
/// <item><c>ok</c> — all required artifacts present, host binding matches the observed remote.</item>
/// </list>
///
/// Pure data in / pure data out: no file I/O, no `gh` calls, no state
/// mutation. The command layer captures the inputs.
/// </summary>
internal static class IntentHostCheckAnalyzer
{
    public const string ClassificationOk = "ok";
    public const string ClassificationUninitialized = "uninitialized";
    public const string ClassificationWrongHost = "wrong-host";
    public const string ClassificationMissingHostBinding = "missing-host-binding";
    public const string ClassificationDomainNotBootstrapped = "domain-not-bootstrapped";
    public const string ClassificationPartiallyInitialized = "partially-initialized";

    public static IntentHostCheckResult Analyze(IntentHostCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Domain);

        // 1. uninitialized — no `.intent-cli/config.toml`
        if (!input.ConfigTomlPresent)
        {
            return Result(
                ClassificationUninitialized,
                ok: false,
                summary: $"Host repo at '{input.RepoRoot}' has no `.intent-cli/config.toml`. Run `intent-cli intent init` to bootstrap a canonical intent host.",
                remediation: new[]
                {
                    $"Run `intent-cli intent init --domain {input.Domain} --target-repo {input.TargetRepo ?? "<owner/repo>"} --host-repo <owner/host> --write` from this repo to create the canonical baseline (G293/G301).",
                    "After init, re-run `intent host-check` to confirm the host is ready before any packet/issue work."
                },
                input);
        }

        // 2. wrong-host — host-binding records a different host repo
        var bindingResult = WrongHostGuard.Check(input.Domain, input.BoundHostRepo, input.ObservedRemoteUrl);
        if (string.Equals(bindingResult.Status, WrongHostGuard.StatusMismatch, StringComparison.Ordinal))
        {
            return Result(
                ClassificationWrongHost,
                ok: false,
                summary: $"Wrong host for domain `{input.Domain}`: bound host is `{bindingResult.BoundHostRepo}` but the observed remote is `{bindingResult.ObservedHostRepo}`. Operate this domain from the canonical host.",
                remediation: bindingResult.RemediationSteps,
                input);
        }

        // 3. missing-host-binding — config exists but binding is absent
        if (!input.HostBindingPresent)
        {
            return Result(
                ClassificationMissingHostBinding,
                ok: false,
                summary: $"Host repo at '{input.RepoRoot}' has `.intent-cli/config.toml` but no `host-binding.toml`. Re-run intent init with `--host-repo <owner/repo>` so wrong-host detection can fire (G301).",
                remediation: new[]
                {
                    $"Re-run `intent-cli intent init --domain {input.Domain} --target-repo {input.TargetRepo ?? "<owner/repo>"} --host-repo <owner/repo> --write` to record the canonical host binding (idempotent — existing files are preserved).",
                    "After init, the wrong-host guard can detect operation from a non-canonical host."
                },
                input);
        }

        // 4. domain-not-bootstrapped — binding present but no intents/<domain>/
        if (!input.IntentsDomainDirectoryPresent)
        {
            return Result(
                ClassificationDomainNotBootstrapped,
                ok: false,
                summary: $"Host binding is present but `intents/{input.Domain}/` is missing. The domain has not been bootstrapped on this host.",
                remediation: new[]
                {
                    $"Run `intent-cli intent init --domain {input.Domain} --target-repo {input.TargetRepo ?? "<owner/repo>"} --host-repo {input.BoundHostRepo ?? "<owner/host>"} --write` to bootstrap the domain (idempotent).",
                    $"After init, `intents/{input.Domain}/{{README.md,clarifications/open.md,intent-tree/00-map.md}}` will be present."
                },
                input);
        }

        // 5. partially-initialized — one or more onboarding artifacts missing
        var missing = CollectMissingArtifacts(input);
        if (missing.Count > 0)
        {
            return Result(
                ClassificationPartiallyInitialized,
                ok: false,
                summary: $"Host repo at '{input.RepoRoot}' is partially initialized; {missing.Count} required artifact(s) missing: {string.Join(", ", missing.Select(m => "`" + m + "`"))}.",
                remediation: BuildPartialRemediation(missing, input),
                input);
        }

        // 6. ok
        return Result(
            ClassificationOk,
            ok: true,
            summary: BuildOkSummary(input),
            remediation: Array.Empty<string>(),
            input);
    }

    private static IReadOnlyList<string> CollectMissingArtifacts(IntentHostCheckInput input)
    {
        var missing = new List<string>();
        if (!input.AgentsMarkdownPresent) missing.Add("AGENTS.md");
        if (!input.ClaudeMarkdownPresent) missing.Add("CLAUDE.md");
        if (!input.QueueStateJsonPresent) missing.Add(".intent-cli/queue-state.json");
        if (!input.RunsJsonlPresent) missing.Add(".intent-cli/runs.jsonl");
        return missing;
    }

    private static IReadOnlyList<string> BuildPartialRemediation(
        IReadOnlyList<string> missing,
        IntentHostCheckInput input)
    {
        var steps = new List<string>();
        if (missing.Contains("AGENTS.md") || missing.Contains("CLAUDE.md"))
        {
            steps.Add($"Re-run `intent-cli intent init --domain {input.Domain} --target-repo {input.TargetRepo ?? "<owner/repo>"} --host-repo {input.BoundHostRepo ?? "<owner/host>"} --write` to regenerate the missing AGENTS.md / CLAUDE.md (idempotent — existing files are preserved).");
        }
        if (missing.Contains(".intent-cli/queue-state.json"))
        {
            steps.Add("Run a host loop wake (review/closeout/next-slice) to populate `.intent-cli/queue-state.json`. The first publish will create the file.");
        }
        if (missing.Contains(".intent-cli/runs.jsonl"))
        {
            steps.Add("Run a host loop wake; `.intent-cli/runs.jsonl` is appended on each closeout / publish.");
        }
        return steps;
    }

    private static string BuildOkSummary(IntentHostCheckInput input)
    {
        var parts = new List<string>
        {
            $"Host at '{input.RepoRoot}' is canonical for domain `{input.Domain}`."
        };
        if (!string.IsNullOrEmpty(input.BoundHostRepo))
        {
            parts.Add($"Host binding: `{input.BoundHostRepo}`.");
        }
        if (!string.IsNullOrEmpty(input.TargetRepo))
        {
            parts.Add($"Target repo: `{input.TargetRepo}`.");
        }
        if (!string.IsNullOrEmpty(input.ChildSubmodulePath))
        {
            parts.Add($"Child submodule path: `{input.ChildSubmodulePath}`.");
        }
        return string.Join(" ", parts);
    }

    private static IntentHostCheckResult Result(
        string classification,
        bool ok,
        string summary,
        IReadOnlyList<string> remediation,
        IntentHostCheckInput input) =>
        new()
        {
            Classification = classification,
            Ok = ok,
            Summary = summary,
            RemediationSteps = remediation,
            Domain = input.Domain,
            TargetRepo = input.TargetRepo,
            HostRepoRoot = input.RepoRoot,
            BoundHostRepo = input.BoundHostRepo,
            ObservedRemoteUrl = input.ObservedRemoteUrl,
            ChildSubmodulePath = input.ChildSubmodulePath
        };
}

internal sealed record IntentHostCheckInput
{
    public required string RepoRoot { get; init; }
    public required string Domain { get; init; }
    public string? TargetRepo { get; init; }
    public string? ObservedRemoteUrl { get; init; }
    public string? ChildSubmodulePath { get; init; }

    public required bool ConfigTomlPresent { get; init; }
    public required bool HostBindingPresent { get; init; }
    public string? BoundHostRepo { get; init; }
    public required bool IntentsDomainDirectoryPresent { get; init; }
    public required bool AgentsMarkdownPresent { get; init; }
    public required bool ClaudeMarkdownPresent { get; init; }
    public required bool QueueStateJsonPresent { get; init; }
    public required bool RunsJsonlPresent { get; init; }
}

internal sealed record IntentHostCheckResult
{
    public required string Classification { get; init; }
    public required bool Ok { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> RemediationSteps { get; init; }
    public required string Domain { get; init; }
    public required string? TargetRepo { get; init; }
    public required string HostRepoRoot { get; init; }
    public required string? BoundHostRepo { get; init; }
    public required string? ObservedRemoteUrl { get; init; }
    public required string? ChildSubmodulePath { get; init; }
}
