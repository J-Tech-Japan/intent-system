namespace IntentSystem.Cli;

internal interface IGitRemoteCommandRunner
{
    GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments);
}
