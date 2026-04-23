using System.Text.Json;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli;

internal sealed class GhQueueDispatchPublisher : IQueueDispatchPublisher
{
    private readonly IGitHubCommandRunner commandRunner;

    public GhQueueDispatchPublisher()
        : this(new GhCommandRunner())
    {
    }

    public GhQueueDispatchPublisher(IGitHubCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public LinkedIssue CreateIssue(string targetRepo, string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        var repoRef = GitHubRepositoryRef.Parse(targetRepo);
        var payloadPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(payloadPath, JsonSerializer.Serialize(new { title, body }));

            var result = commandRunner.Run(
                [
                    "api",
                    $"repos/{repoRef.Owner}/{repoRef.Repo}/issues",
                    "--method",
                    "POST",
                    "--input",
                    payloadPath
                ]);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? "gh api issue create failed."
                    : result.StdErr.Trim();
                throw new InvalidOperationException(error);
            }

            using var document = JsonDocument.Parse(result.StdOut);
            if (!document.RootElement.TryGetProperty("number", out var numberElement)
                || !numberElement.TryGetInt32(out var issueNumber))
            {
                throw new InvalidOperationException("GitHub issue create response must contain number.");
            }

            if (!document.RootElement.TryGetProperty("html_url", out var htmlUrlElement)
                || string.IsNullOrWhiteSpace(htmlUrlElement.GetString()))
            {
                throw new InvalidOperationException("GitHub issue create response must contain html_url.");
            }

            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = issueNumber,
                Url = htmlUrlElement.GetString()
                    ?? throw new InvalidOperationException("GitHub issue create response html_url was null.")
            };
        }
        finally
        {
            if (File.Exists(payloadPath))
            {
                File.Delete(payloadPath);
            }
        }
    }

    public void AddLabel(string targetRepo, int issueNumber, string labelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(issueNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(labelName);

        var repoRef = GitHubRepositoryRef.Parse(targetRepo);
        var payloadPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(payloadPath, JsonSerializer.Serialize(new { labels = new[] { labelName } }));

            var result = commandRunner.Run(
                [
                    "api",
                    $"repos/{repoRef.Owner}/{repoRef.Repo}/issues/{issueNumber}/labels",
                    "--method",
                    "POST",
                    "--input",
                    payloadPath
                ]);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? "gh api issue label add failed."
                    : result.StdErr.Trim();
                throw new InvalidOperationException(error);
            }
        }
        finally
        {
            if (File.Exists(payloadPath))
            {
                File.Delete(payloadPath);
            }
        }
    }

    private sealed record GitHubRepositoryRef
    {
        public required string Owner { get; init; }

        public required string Repo { get; init; }

        public static GitHubRepositoryRef Parse(string targetRepo)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);

            var segments = targetRepo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length != 2)
            {
                throw new InvalidOperationException(
                    $"Target repo '{targetRepo}' must use the GitHub owner/repo shape.");
            }

            return new GitHubRepositoryRef
            {
                Owner = segments[0],
                Repo = segments[1]
            };
        }
    }
}
