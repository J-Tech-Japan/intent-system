namespace IntentSystem.Cli.Commands;

internal interface IDirectRunProcessRunner
{
    DirectRunProcessLaunchResult Start(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> arguments,
        bool inheritStandardInput,
        TimeSpan earlyExitWindow,
        Action<int> onStarted,
        Action<int> onExited,
        Action<string> onStdOutLine,
        Action<string> onStdErrLine);
}
