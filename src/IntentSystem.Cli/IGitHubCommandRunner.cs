namespace IntentSystem.Cli;

internal interface IGitHubCommandRunner
{
    GitHubCommandResult Run(IReadOnlyList<string> arguments);
}
