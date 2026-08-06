using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G206: Testability seam for <c>intent-cli worker next-action</c>. The
/// production implementation shells out to <c>gh pr list</c> and
/// <c>gh issue list</c> with label filters; tests inject a fake to avoid
/// any GitHub network access.
/// </summary>
internal interface IGitHubAutomationCandidateLister
{
    IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels);

    IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels);

    /// <summary>
    /// G448: list MERGED pull requests (with closing-issue references) for the
    /// unified state doctor's merged-PR-not-completed lane. Default returns an
    /// empty list so existing fakes/implementations keep compiling; the
    /// gh-backed lister overrides it with a <c>--state merged</c> query.
    /// </summary>
    IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
        => Array.Empty<GitHubAutomationPrCandidate>();
}

/// <summary>
/// G206: Single PR candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListPullRequests"/>.
/// </summary>
internal sealed record GitHubAutomationPrCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();

    [JsonPropertyName("closingIssuesReferences")]
    public IReadOnlyList<GitHubPrClosingIssueReference> ClosingIssuesReferences { get; init; }
        = Array.Empty<GitHubPrClosingIssueReference>();

    /// <summary>
    /// G289: GitHub PR state ("OPEN" / "CLOSED" / "MERGED"). Empty for
    /// callers that pre-date the field; treated as open for backward compat.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// G319: GitHub PR draft flag from <c>gh pr list --json isDraft</c>.
    /// Defaults to <c>false</c> so existing test fakes that don't seed it
    /// keep the prior non-draft behavior. Consumed by the host-loop-next-action
    /// approved-PR continuation lane (G297 forbids merging a draft PR
    /// even after an approval transition).
    /// </summary>
    [JsonPropertyName("isDraft")]
    public bool IsDraft { get; init; }

    /// <summary>
    /// G589: exact PR head the check rollup belongs to. Empty for callers and
    /// fixtures that pre-date CI-aware stalled-work; those callers retain the
    /// existing non-CI classification.
    /// </summary>
    [JsonPropertyName("headRefOid")]
    public string HeadRefOid { get; init; } = string.Empty;

    /// <summary>
    /// G589: authoritative GitHub check/status rollup for <see cref="HeadRefOid"/>.
    /// The stalled-work analyzer normalizes CheckRun and StatusContext entries
    /// without performing any workflow transition.
    /// </summary>
    [JsonPropertyName("statusCheckRollup")]
    public IReadOnlyList<GitHubAutomationStatusCheckCandidate> StatusCheckRollup { get; init; }
        = Array.Empty<GitHubAutomationStatusCheckCandidate>();
}

/// <summary>G589: union-shaped row from GitHub's PR statusCheckRollup.</summary>
internal sealed record GitHubAutomationStatusCheckCandidate
{
    [JsonPropertyName("__typename")]
    public string TypeName { get; init; } = string.Empty;

    /// <summary>CheckRun lifecycle status, normally QUEUED / IN_PROGRESS / COMPLETED.</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    /// <summary>CheckRun terminal conclusion, e.g. SUCCESS / FAILURE / SKIPPED.</summary>
    [JsonPropertyName("conclusion")]
    public string Conclusion { get; init; } = string.Empty;

    /// <summary>StatusContext state, e.g. PENDING / EXPECTED / SUCCESS / FAILURE / ERROR.</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// G206: Single issue candidate row returned by
/// <see cref="IGitHubAutomationCandidateLister.ListIssues"/>.
/// </summary>
internal sealed record GitHubAutomationIssueCandidate
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// G533: GitHub's own "last modified" timestamp — bumped by any
    /// label change, comment, or other timeline event on the issue, so it
    /// is the closest available proxy for "last observable activity"
    /// without a dedicated per-issue timeline-events fetch. Empty for
    /// callers that pre-date the field; treated as unknown (falls back to
    /// <see cref="CreatedAt"/>) by consumers.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public string UpdatedAt { get; init; } = string.Empty;

    [JsonPropertyName("labels")]
    public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
        = Array.Empty<GitHubAutomationLabel>();

    /// <summary>
    /// G289: GitHub issue state ("OPEN" / "CLOSED"). Empty for callers that
    /// pre-date the field; treated as open for backward compat.
    /// </summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;
}

internal sealed record GitHubAutomationLabel
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// G206: Default lister that shells out to <c>gh pr list</c> and
/// <c>gh issue list</c>. The only file in the worker next-action surface
/// permitted to call <c>Process.Start</c> — the analyzer and command layers
/// must remain pure. Both calls request stable supported field subsets.
/// PR listing also includes body and closing issue metadata so host selectors
/// can model issue-linked PR fallback without extra mutation-capable calls.
/// </summary>
internal sealed class GhCliGitHubAutomationCandidateLister : IGitHubAutomationCandidateLister
{
    /// <summary>
    /// G206: comma-separated <c>gh pr list --json</c> field list. Exposed
    /// internally so adapter-shape regression tests can lock the supported
    /// subset.
    /// </summary>
    // G289: also request `state` so the analyzer can defensively filter
    // closed issues / PRs from WIP detection even when callers (e.g. test
    // fakes) don't pre-apply `--state open`.
    // G533: also request `updatedAt` — the closest available proxy for
    // "last observable issue activity" (label change, comment, etc.)
    // without a dedicated per-issue timeline-events fetch, used by
    // `automation stalled-work`'s claimed-but-silent detection.
    internal const string ListJsonFields = "number,title,url,createdAt,updatedAt,labels,state";

    // G319: also request `isDraft` so the host-loop-next-action analyzer
    // can map an approved-but-draft PR to `approved-pr-draft-blocked`
    // (G297) instead of attempting a merge.
    internal const string PrListJsonFields =
        "number,title,url,body,createdAt,updatedAt,labels,closingIssuesReferences,state,isDraft,headRefOid,statusCheckRollup";

    /// <summary>
    /// G206: builds the <c>gh pr list</c> argument list shared by the live
    /// adapter and adapter-shape tests.
    /// </summary>
    internal static IReadOnlyList<string> BuildPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    /// <summary>
    /// G206: builds the <c>gh issue list</c> argument list.
    /// </summary>
    /// <summary>
    /// G448: builds the <c>gh pr list --state merged</c> argument list used by
    /// the unified state doctor's merged-PR lane.
    /// </summary>
    internal static IReadOnlyList<string> BuildMergedPrListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "pr",
            "list",
            "--repo", repo,
            "--state", "merged",
            "--json", PrListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    internal static IReadOnlyList<string> BuildIssueListArguments(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentNullException.ThrowIfNull(requiredLabels);

        var args = new List<string>
        {
            "issue",
            "list",
            "--repo", repo,
            "--state", "open",
            "--json", ListJsonFields,
            "--limit", "200"
        };
        foreach (var label in requiredLabels)
        {
            args.Add("--label");
            args.Add(label);
        }
        return args;
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildIssueListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list issues in {repo}");
        return DeserializeList<GitHubAutomationIssueCandidate>(stdout, $"`gh issue list` for {repo}");
    }

    public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(
        string repo,
        IReadOnlyCollection<string> requiredLabels)
    {
        var args = BuildMergedPrListArguments(repo, requiredLabels);
        var stdout = RunGh(args, $"list merged PRs in {repo}");
        return DeserializeList<GitHubAutomationPrCandidate>(stdout, $"`gh pr list --state merged` for {repo}");
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    $"failed to start `gh` process to {description}");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new InvalidOperationException(
                $"could not invoke `gh` to {description}: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            // G385: classify the failure (auth vs generic) and surface a
            // sanitized preview instead of the raw, possibly secret-bearing
            // gh output.
            var classification = GitHubCliJsonBoundary.ClassifyProcessFailure(stderr, stdout);
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"[{classification}] `gh` failed to {description} with exit {exitCode}: "
                + GitHubCliJsonBoundary.SanitizePreview(errorBody));
        }

        return stdout;
    }

    /// <summary>
    /// G385: deserialize the candidate list through the hardened JSON boundary.
    /// Internal so adapter tests can lock the contamination contract without a
    /// real <c>gh</c> subprocess.
    /// </summary>
    internal static IReadOnlyList<T> DeserializeList<T>(string stdout, string callDescription)
    {
        // G385: harden the parser boundary against contaminated stdout (BOM,
        // trailing update notices, warning lines printed alongside the array —
        // observed under Windows PowerShell native-command capture). Parse only
        // the validated JSON array; on genuine failure raise a structured,
        // sanitized diagnostic rather than a raw JsonException.
        var extraction = GitHubCliJsonBoundary.ExtractJsonArray(stdout, callDescription);
        if (!extraction.Succeeded)
        {
            throw new InvalidOperationException(
                $"[{extraction.Classification}] {extraction.DiagnosticMessage}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<T>>(extraction.Json);
            return (IReadOnlyList<T>?)result ?? Array.Empty<T>();
        }
        catch (JsonException exception)
        {
            // The array shape was already validated, so a typed-deserialize
            // failure here is a schema mismatch, not contamination — still
            // surface a sanitized, classified diagnostic.
            throw new InvalidOperationException(
                $"[{GitHubCliJsonBoundary.Classifications.GithubJsonInvalid}] could not map {callDescription} "
                + $"JSON to the expected shape: {exception.Message}; sanitized preview: "
                + $"\"{GitHubCliJsonBoundary.SanitizePreview(extraction.Json)}\"",
                exception);
        }
    }
}
