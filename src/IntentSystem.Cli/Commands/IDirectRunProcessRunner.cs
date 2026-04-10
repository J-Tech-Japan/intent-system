namespace IntentSystem.Cli.Commands;

internal interface IDirectRunProcessRunner
{
    DirectRunProcessLaunchResult Start(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan earlyExitWindow,
        Action<int> onStarted,
        Action<string> onStdOutLine,
        Action<string> onStdErrLine);
}
