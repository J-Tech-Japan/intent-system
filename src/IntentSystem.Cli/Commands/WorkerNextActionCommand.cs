using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: <c>intent-cli worker next-action</c> command. Selects at most one
/// coding-automation target (PR repair / issue-to-PR / none) deterministically
/// per the priority order in #517.
///
/// No-mutation invariants (verified by tests):
/// - never invokes <c>NestedProviderLauncher</c>;
/// - command + analyzer files contain no <c>Process.Start</c>, no
///   <c>gh issue edit</c>/<c>gh pr edit</c>/<c>gh pr merge</c>/
///   <c>gh pr close</c>/<c>gh pr reopen</c>/<c>gh pr comment</c>/
///   <c>gh pr review</c>, no <c>resolveReviewThread</c> mutation;
/// - whole-workspace byte-snapshot before/after asserts no file is created
///   or modified;
/// - the only file in the worker next-action surface that calls
///   <c>Process.Start</c> is the lister adapter
///   <see cref="GhCliGitHubAutomationCandidateLister"/>, by design.
/// </summary>
internal static class WorkerNextActionCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test seam: tests inject a fake <see cref="IGitHubAutomationCandidateLister"/>
    /// here so no real GitHub network call is made. Production callers leave
    /// this null and the default <see cref="GhCliGitHubAutomationCandidateLister"/>
    /// is used.
    /// </summary>
    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all command paths to lock the "no nested provider
    /// launch" guarantee.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var repo, out var workdir, out var githubOnly, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> prs;
        IReadOnlyList<GitHubAutomationIssueCandidate> issues;
        try
        {
            prs = lister.ListPullRequests(
                repo!,
                new[] { WorkerNextActionConstants.Labels.IntentTarget });
            issues = lister.ListIssues(
                repo!,
                new[] { WorkerNextActionConstants.Labels.IntentTarget });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine(
                $"failed to list automation candidates for {repo}: {exception.Message}");
            return 1;
        }

        WorkerNextActionResult result;
        try
        {
            result = WorkerNextActionAnalyzer.Analyze(repo!, prs, issues);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException)
        {
            writer.WriteLine($"failed to classify next-action for {repo}: {exception.Message}");
            return 1;
        }

        // G281: --workdir is child worktree CONTEXT only — selection runs against
        // GitHub state for --repo, never against the workdir's local filesystem.
        // We surface diagnostic warnings when workdir is supplied but does not
        // look like a git worktree (or does not exist), without ever flipping
        // the selection to no-actionable. The parent host's `.intent-cli`
        // remains the durable state root from the command's cwd.
        if (!string.IsNullOrWhiteSpace(workdir))
        {
            var workdirWarnings = new List<string>(result.Warnings);
            foreach (var warning in BuildWorkdirWarnings(workdir!))
            {
                workdirWarnings.Add(warning);
            }
            if (workdirWarnings.Count != result.Warnings.Count)
            {
                result = result with { Warnings = workdirWarnings };
            }
        }

        // G333: surface the strict child-loop assertion on the result.
        if (githubOnly)
        {
            result = result with { GithubOnly = true };
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
    /// G281: emit advisory warnings when <c>--workdir</c> is supplied but does
    /// not look like a usable child git worktree. Selection itself does NOT
    /// depend on the child workdir state — the warnings are operator-facing
    /// hints that the worktree may be missing or stale before they try to
    /// run the chosen workflow against it.
    /// </summary>
    internal static IEnumerable<string> BuildWorkdirWarnings(string workdir)
    {
        if (!Directory.Exists(workdir))
        {
            yield return $"workdir '{workdir}' does not exist; selection used GitHub state from --repo only";
            yield break;
        }

        var gitPath = Path.Combine(workdir, ".git");
        if (!Directory.Exists(gitPath) && !File.Exists(gitPath))
        {
            yield return $"workdir '{workdir}' is not a git worktree (no .git entry); selection used GitHub state from --repo only";
        }
    }

    private static void WriteText(TextWriter writer, WorkerNextActionResult result)
    {
        writer.WriteLine($"# Worker next-action for {result.Repo}");
        writer.WriteLine();
        writer.WriteLine($"- action: {result.Action}");
        if (result.Number is { } number)
        {
            writer.WriteLine($"- number: {number}");
        }
        if (!string.IsNullOrEmpty(result.Url))
        {
            writer.WriteLine($"- url: {result.Url}");
        }
        writer.WriteLine($"- reason: {result.Reason}");
        if (!string.IsNullOrEmpty(result.RecommendedWorkflow))
        {
            writer.WriteLine($"- recommended_workflow: {result.RecommendedWorkflow}");
        }
        if (!string.IsNullOrEmpty(result.SourceClassification))
        {
            writer.WriteLine($"- source_classification: {result.SourceClassification}");
        }
        writer.WriteLine();

        writer.WriteLine("## Warnings");
        if (result.Warnings.Count == 0)
        {
            writer.WriteLine("- (none)");
        }
        else
        {
            foreach (var warning in result.Warnings)
            {
                writer.WriteLine($"- {warning}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out bool githubOnly,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        githubOnly = false;
        format = FormatText;
        error = string.Empty;

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

                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;

                case "--github-only":
                    // G333: strict child-loop assertion. `worker next-action`
                    // is already GitHub-label-based by data flow — no
                    // queue-state read; selection comes from `gh issue
                    // list` / `gh pr list` via the candidate lister.
                    // The flag explicitly records the binding on the
                    // result so the host loop / operator can audit.
                    githubOnly = true;
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
                    error = $"Unknown argument '{argument}'. Supported: --repo <owner/repo> [--workdir <path>] [--github-only] [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required (e.g. --repo owner/repo).";
            return false;
        }

        return true;
    }
}
