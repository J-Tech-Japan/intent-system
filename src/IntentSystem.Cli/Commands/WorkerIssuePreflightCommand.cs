using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G202: Read-only <c>intent-cli worker issue-preflight</c> command. Answers
/// "is this GitHub issue actionable for the current implementation repository
/// right now?" with a single deterministic classification. Never mutates queue
/// state, runs, GitHub labels/PRs, source files, or any on-disk state. Exposes
/// <see cref="IssueLookupFactory"/> and <see cref="NestedProviderLauncher"/> so
/// tests can inject fakes and assert that no nested provider is launched.
/// </summary>
internal static class WorkerIssuePreflightCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    private static readonly Regex LeadingExecutionUnitPattern = new(
        @"^(?:[A-Z][A-Z0-9]*-G?[0-9]+|G[0-9]+)(?![A-Za-z0-9])",
        RegexOptions.Compiled);

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubIssueLookup"/> here so
    /// no real GitHub network call is made. Production callers leave this null
    /// and the default <see cref="GhCliGitHubIssueLookup"/> is used.
    /// </summary>
    public static Func<IGitHubIssueLookup>? IssueLookupFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked by this command. Tests assert that
    /// it remains uninvoked across all preflight code paths to lock in the
    /// "no nested provider launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var issueNumber, out var workdir, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = string.IsNullOrWhiteSpace(workdir)
            ? Directory.GetCurrentDirectory()
            : workdir!;

        IGitHubIssueLookup lookup;
        try
        {
            lookup = IssueLookupFactory?.Invoke() ?? new GhCliGitHubIssueLookup();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub issue lookup: {exception.Message}");
            return 1;
        }

        GitHubIssueLookupResult issuePayload;
        try
        {
            issuePayload = lookup.Lookup(repo!, issueNumber);
        }
        catch (Exception exception)
        {
            writer.WriteLine(
                $"failed to look up GitHub issue {repo}#{issueNumber}: {exception.Message}");
            return 1;
        }

        if (issuePayload is null)
        {
            writer.WriteLine(
                $"GitHub issue lookup returned no payload for {repo}#{issueNumber}.");
            return 1;
        }

        WorkerIssuePreflightResult result;
        try
        {
            var claimVerification = ResolveClaimVerification(
                context.RepoRoot,
                issuePayload.Title);
            result = WorkerIssuePreflightAnalyzer.Analyze(
                issuePayload,
                repo!,
                issueNumber,
                resolvedWorkdir,
                claimVerification);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or FormatException)
        {
            writer.WriteLine(
                $"failed to classify GitHub issue {repo}#{issueNumber}: {exception.Message}");
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    /// <summary>
    /// G717: consult the canonical execution-unit claim before applying the
    /// lifecycle-label precedence. The preflight workdir remains a target
    /// mismatch context; claim evidence comes from the command's repository
    /// root so a child worktree cannot substitute a stale local label or
    /// record. Hosts without a claims store return the legacy no-store result.
    /// </summary>
    private static ClaimOwnershipVerification? ResolveClaimVerification(
        string repoRoot,
        string? issueTitle)
    {
        var match = LeadingExecutionUnitPattern.Match(issueTitle ?? string.Empty);
        if (!match.Success)
        {
            return null;
        }

        try
        {
            return ClaimOwnershipVerifier.Verify(
                repoRoot,
                $"execution-unit:{match.Value}",
                invokingTeam: null);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or IOException
            or FormatException)
        {
            // The analyzer remains usable for legacy/test callers when the
            // optional claim consultation cannot be initialized. A claims
            // enabled Git checkout reports canonical-unavailable through the
            // verifier itself; this catch is only for an unavailable seam.
            return null;
        }
    }

    private static void WriteText(TextWriter writer, WorkerIssuePreflightResult result)
    {
        writer.WriteLine($"# Worker issue-preflight for {result.Repo}#{result.Issue}");
        writer.WriteLine();
        writer.WriteLine($"- title: {result.Title}");
        writer.WriteLine($"- issue_state: {result.IssueState}");
        writer.WriteLine($"- classification: {result.Classification}");
        writer.WriteLine($"- actionable: {result.Actionable.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- recommended_action: {result.RecommendedAction}");
        writer.WriteLine();

        writer.WriteLine("## Labels");
        if (result.Labels.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var label in result.Labels)
            {
                writer.WriteLine($"- {label}");
            }
        }
        writer.WriteLine();

        if (result.ClaimStatus is not null)
        {
            writer.WriteLine("## Claim registry");
            writer.WriteLine($"- scope: {result.ClaimScope}");
            writer.WriteLine($"- status: {result.ClaimStatus}");
            writer.WriteLine($"- holder: {result.ClaimHolder ?? "(none)"}");
            writer.WriteLine($"- holder_team: {result.ClaimHolderTeam ?? "(none)"}");
            writer.WriteLine($"- detail: {result.ClaimDetail}");
            writer.WriteLine();
        }

        writer.WriteLine("## Reasons");
        if (result.Reasons.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var reason in result.Reasons)
            {
                writer.WriteLine($"- {reason}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Advisories");
        if (result.Advisories.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var advisory in result.Advisories)
            {
                writer.WriteLine($"- {advisory}");
            }
        }
        writer.WriteLine();

        writer.WriteLine(result.SummaryLine);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int issueNumber,
        out string? workdir,
        out string format,
        out string error)
    {
        repo = null;
        issueNumber = 0;
        workdir = null;
        format = FormatText;
        error = string.Empty;
        var sawIssue = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1];
                    index++;
                    break;

                case "--issue":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--issue requires a value (issue number).";
                        return false;
                    }
                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out issueNumber)
                        || issueNumber <= 0)
                    {
                        error = $"--issue must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }
                    sawIssue = true;
                    index++;
                    break;

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> --issue <number> [--workdir <path>] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        if (!sawIssue)
        {
            error = "--issue is required (e.g. --issue 123).";
            return false;
        }

        return true;
    }
}
