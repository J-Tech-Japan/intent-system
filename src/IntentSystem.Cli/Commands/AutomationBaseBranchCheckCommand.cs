using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G294: read-only diagnostic that compares an observed PR base branch
/// against the configured <c>base_branch_policy</c> for a host domain. The
/// caller passes <c>--actual-base</c> (typically captured from
/// <c>gh pr view &lt;n&gt; --json baseRefName --jq .baseRefName</c>) so this
/// command stays deterministic and easy to test without invoking
/// GitHub. When <c>--policy</c> is omitted, the command reads
/// <c>base_branch_policy</c> from the loaded host config (defaulting to
/// <c>direct-main</c>). Never mutates state.
/// </summary>
internal static class AutomationBaseBranchCheckCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string StatusOk = "ok";
    private const string StatusMismatch = "mismatch";

    private const string UsageLine =
        "Usage: intent-cli automation base-branch-check --repo <owner/repo> --pr <n> --actual-base <branch> [--policy direct-main|main-ai] [--implementation-base <branch>] [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var request, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var configuredPolicy = string.IsNullOrWhiteSpace(request.PolicyOverride)
            ? context.Config.Project.BaseBranchPolicy
            : request.PolicyOverride!;

        if (string.IsNullOrWhiteSpace(configuredPolicy))
        {
            configuredPolicy = CliRuntimeContracts.DefaultBaseBranchPolicy;
        }

        if (!BaseBranchPolicyContract.IsKnownPolicy(configuredPolicy))
        {
            writer.WriteLine(
                $"Unknown base branch policy '{configuredPolicy}'. Expected '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}'.");
            return 1;
        }

        // G362: when the host config sets an explicit
        // ImplementationBaseBranch (same-repo topology, e.g.
        // `main-ai`), that field takes precedence over the
        // policy-derived default. Otherwise fall through to the
        // policy lookup so non-same-repo hosts keep pre-G362
        // behavior byte-identically.
        //
        // PR #829 review repair: the `--policy` CLI flag is the
        // caller's explicit override and MUST win over the
        // ImplementationBaseBranch config fallback. Only consult the
        // config field when `--policy` was NOT supplied, so that
        // `intent-cli automation base-branch-check --policy direct-main ...`
        // never silently compares against a same-repo
        // implementation branch and hides a real mismatch.
        var configuredImplementationBase = context.Config.Project.ImplementationBaseBranch;
        var policyDerivedBase = BaseBranchPolicyContract.ResolveExpectedBaseBranch(configuredPolicy);

        // G471: an explicit `--implementation-base <branch>` names the resolved
        // effective implementation / PR base branch and takes the HIGHEST
        // precedence — above both `--policy` and the config fallback. The
        // generated host-loop guidance passes this for non-default branches so
        // a `develop-v2` host validates against `develop-v2` unambiguously,
        // instead of forcing `--policy direct-main` (which would compare against
        // `main` and falsely report a mismatch) and instead of depending on the
        // host having `implementation_base_branch` in its own config.
        var expectedBase = !string.IsNullOrWhiteSpace(request.ImplementationBaseOverride)
            ? request.ImplementationBaseOverride!.Trim()
            : !string.IsNullOrWhiteSpace(request.PolicyOverride)
                ? policyDerivedBase
                : string.IsNullOrWhiteSpace(configuredImplementationBase)
                    ? policyDerivedBase
                    : configuredImplementationBase.Trim();
        var actualBase = request.ActualBase.Trim();
        var matches = string.Equals(actualBase, expectedBase, StringComparison.Ordinal);

        var result = new BaseBranchCheckResult
        {
            Repo = request.Repo,
            Pr = request.PrNumber,
            Policy = configuredPolicy,
            ExpectedBase = expectedBase,
            ActualBase = actualBase,
            Status = matches ? StatusOk : StatusMismatch,
            Summary = matches
                ? $"PR #{request.PrNumber} targets `{actualBase}`, matching policy `{configuredPolicy}`."
                : $"PR #{request.PrNumber} targets `{actualBase}` but policy `{configuredPolicy}` requires `{expectedBase}`. Re-target the PR or update `base_branch_policy` to match the project workflow.",
            RecommendedActions = matches
                ? Array.Empty<string>()
                : new[]
                {
                    $"Re-target PR #{request.PrNumber} so its base branch is `{expectedBase}`, OR",
                    $"Update `[project] base_branch_policy` in `.intent-cli/config.toml` if `{actualBase}` is the intended base."
                }
        };

        if (string.Equals(request.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return matches ? 0 : 1;
    }

    private static void WriteMarkdown(TextWriter writer, BaseBranchCheckResult result)
    {
        writer.WriteLine($"# Base branch check — {result.Repo} PR #{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- Policy: `{result.Policy}`");
        writer.WriteLine($"- Expected base: `{result.ExpectedBase}`");
        writer.WriteLine($"- Actual base: `{result.ActualBase}`");
        writer.WriteLine($"- Status: **{result.Status}**");
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.RecommendedActions.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended actions");
            foreach (var action in result.RecommendedActions)
            {
                writer.WriteLine($"- {action}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out BaseBranchCheckRequest request, out string error)
    {
        request = default!;
        error = string.Empty;

        string? repo = null;
        int? prNumber = null;
        string? actualBase = null;
        string? policyOverride = null;
        string? implementationBaseOverride = null;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--pr":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var parsedPr) || parsedPr <= 0)
                    {
                        error = "--pr requires a positive integer.";
                        return false;
                    }
                    prNumber = parsedPr;
                    index++;
                    break;
                case "--actual-base":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--actual-base requires a branch name (e.g. 'main' or 'main-ai').";
                        return false;
                    }
                    actualBase = args[++index].Trim();
                    break;
                case "--policy":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = $"--policy requires a value ('{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}').";
                        return false;
                    }
                    policyOverride = args[++index].Trim();
                    if (!BaseBranchPolicyContract.IsKnownPolicy(policyOverride))
                    {
                        error = $"--policy must be '{CliRuntimeContracts.DirectMainBaseBranchPolicy}' or '{CliRuntimeContracts.MainAiBaseBranchPolicy}' (got '{policyOverride}').";
                        return false;
                    }
                    break;
                case "--implementation-base":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--implementation-base requires a branch name (e.g. 'develop-v2').";
                        return false;
                    }
                    implementationBaseOverride = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requestedFormat = args[++index].Trim();
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "Base branch check requires '--repo <owner/repo>'.";
            return false;
        }

        if (prNumber is null)
        {
            error = "Base branch check requires '--pr <n>'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(actualBase))
        {
            error = "Base branch check requires '--actual-base <branch>' (e.g. captured via 'gh pr view <n> --json baseRefName --jq .baseRefName').";
            return false;
        }

        request = new BaseBranchCheckRequest
        {
            Repo = repo!,
            PrNumber = prNumber.Value,
            ActualBase = actualBase!,
            PolicyOverride = policyOverride,
            ImplementationBaseOverride = implementationBaseOverride,
            Format = format
        };
        return true;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private sealed record BaseBranchCheckRequest
    {
        public required string Repo { get; init; }

        public required int PrNumber { get; init; }

        public required string ActualBase { get; init; }

        public required string Format { get; init; }

        public string? PolicyOverride { get; init; }

        public string? ImplementationBaseOverride { get; init; }
    }

    internal sealed record BaseBranchCheckResult
    {
        [JsonPropertyName("repo")]
        public required string Repo { get; init; }

        [JsonPropertyName("pr")]
        public required int Pr { get; init; }

        [JsonPropertyName("policy")]
        public required string Policy { get; init; }

        [JsonPropertyName("expected_base")]
        public required string ExpectedBase { get; init; }

        [JsonPropertyName("actual_base")]
        public required string ActualBase { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("summary")]
        public required string Summary { get; init; }

        [JsonPropertyName("recommended_actions")]
        public required IReadOnlyList<string> RecommendedActions { get; init; }
    }
}
