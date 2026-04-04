namespace IntentSystem.Review;

public interface IGitCommandRunner
{
    GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments);
}
