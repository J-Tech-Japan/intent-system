namespace IntentSystem.Cli;

internal sealed class GhRunSubmitPublisher : IRunSubmitPublisher
{
    private readonly IGitHubCommandRunner commandRunner;

    public GhRunSubmitPublisher()
        : this(new GhCommandRunner())
    {
    }

    public GhRunSubmitPublisher(IGitHubCommandRunner commandRunner)
    {
        this.commandRunner = commandRunner ?? throw new ArgumentNullException(nameof(commandRunner));
    }

    public string CreateDraftPullRequest(string targetRepo, string headBranch, string title, string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetRepo);
        ArgumentException.ThrowIfNullOrWhiteSpace(headBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);

        var bodyPath = Path.GetTempFileName();

        try
        {
            File.WriteAllText(bodyPath, body);

            var result = commandRunner.Run(
                [
                    "pr",
                    "create",
                    "--draft",
                    "--base",
                    "main",
                    "--head",
                    headBranch,
                    "--title",
                    title,
                    "--body-file",
                    bodyPath,
                    "--repo",
                    targetRepo
                ]);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr)
                    ? "gh pr create failed."
                    : result.StdErr.Trim();
                throw new InvalidOperationException(error);
            }

            var pullRequestUrl = result.StdOut.Trim();
            if (!Uri.TryCreate(pullRequestUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("GitHub PR create response must contain an absolute URL.");
            }

            return pullRequestUrl;
        }
        finally
        {
            if (File.Exists(bodyPath))
            {
                File.Delete(bodyPath);
            }
        }
    }
}
