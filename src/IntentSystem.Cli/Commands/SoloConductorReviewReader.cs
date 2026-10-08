using System.Text.Json;
using IntentSystem.Cli;

namespace IntentSystem.Cli.Commands;

/// <summary>One bounded, read-only GitHub review inventory request.</summary>
internal sealed record SoloConductorReviewReadRequest
{
    public required string Repo { get; init; }
    public required int PullRequest { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string Domain { get; init; }
    public required string Team { get; init; }
    public required CrossRuntimeReviewReadResult LocalReviews { get; init; }
}

internal sealed record SoloConductorReviewReadResult
{
    public bool Complete { get; init; }
    public string? Cause { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyList<IndependentReviewRowEvidence> Rows { get; init; } = [];

    public static SoloConductorReviewReadResult Failed(string cause, string detail) => new()
    {
        Complete = false,
        Cause = cause,
        Detail = detail,
        Rows = [],
    };
}

/// <summary>
/// Fixed-endpoint paginated GET for posted PR reviews. It owns no write or
/// process abstraction; tests may provide the command's one typed read seam.
/// </summary>
internal sealed class SoloConductorReviewReader
{
    internal const int PageSize = 100;
    internal const int MaximumPages = 100;
    internal const string CauseReadUnavailable = "review-read-unavailable";

    private readonly IGitHubCommandRunner commandRunner;

    internal SoloConductorReviewReader(IGitHubCommandRunner? commandRunner = null)
    {
        this.commandRunner = commandRunner ?? new GhCommandRunner();
    }

    internal SoloConductorReviewReadResult Read(SoloConductorReviewReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CrossRuntimeReviewPaths.IsRepositoryName(request.Repo)
            || request.PullRequest <= 0
            || !KnowledgeWriteBackRecord.TryValidateExecutionUnit(request.ExecutionUnit, out _)
            || string.IsNullOrWhiteSpace(request.Domain)
            || string.IsNullOrWhiteSpace(request.Team)
            || request.LocalReviews is null)
        {
            return SoloConductorReviewReadResult.Failed(
                "review-request-invalid",
                "The repository, positive PR number, resolved execution unit/domain/team, or local review snapshot is invalid; no GitHub review request was made.");
        }

        var rows = new List<IndependentReviewRowEvidence>();
        for (var page = 1; page <= MaximumPages; page++)
        {
            GitHubCommandResult response;
            try
            {
                response = commandRunner.Run(BuildArguments(request.Repo, request.PullRequest, page));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return SoloConductorReviewReadResult.Failed(
                    CauseReadUnavailable,
                    $"GitHub PR review page {page} for {request.Repo}#{request.PullRequest} could not be read: {exception.Message}");
            }

            if (response.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(response.StdErr) ? "gh returned a non-zero exit code." : response.StdErr.Trim();
                return SoloConductorReviewReadResult.Failed(
                    CauseReadUnavailable,
                    $"GitHub PR review page {page} for {request.Repo}#{request.PullRequest} could not be read: {error}");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(response.StdOut);
            }
            catch (JsonException exception)
            {
                return SoloConductorReviewReadResult.Failed(
                    CauseReadUnavailable,
                    $"GitHub PR review page {page} for {request.Repo}#{request.PullRequest} returned invalid JSON: {exception.Message}");
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return SoloConductorReviewReadResult.Failed(
                        CauseReadUnavailable,
                        $"GitHub PR review page {page} for {request.Repo}#{request.PullRequest} was not a JSON array; the review inventory is unavailable.");
                }

                var pageRows = document.RootElement.EnumerateArray().ToArray();
                foreach (var item in pageRows)
                {
                    rows.Add(IndependentReviewEvidence.ParseRow(
                        item,
                        request.ExecutionUnit,
                        request.Domain,
                        request.Team,
                        request.Repo,
                        request.PullRequest,
                        request.LocalReviews));
                }

                if (pageRows.Length < PageSize)
                {
                    return new SoloConductorReviewReadResult { Complete = true, Rows = rows };
                }
            }
        }

        return SoloConductorReviewReadResult.Failed(
            CauseReadUnavailable,
            $"GitHub PR review inventory for {request.Repo}#{request.PullRequest} reached the {MaximumPages}-page bound; a truncated inventory cannot satisfy approval.");
    }

    internal static IReadOnlyList<string> BuildArguments(string repo, int pullRequest, int page)
    {
        if (!CrossRuntimeReviewPaths.IsRepositoryName(repo))
        {
            throw new ArgumentException("repository must be '<owner>/<repo>'.", nameof(repo));
        }
        if (pullRequest <= 0) throw new ArgumentOutOfRangeException(nameof(pullRequest));
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));

        return [
            "api",
            "--method",
            "GET",
            $"repos/{repo}/pulls/{pullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture)}/reviews?per_page={PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}&page={page.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
        ];
    }
}
