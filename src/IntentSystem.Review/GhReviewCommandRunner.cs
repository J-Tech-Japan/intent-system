using System.Diagnostics;
using System.Text;

namespace IntentSystem.Review;

public sealed class GhReviewCommandRunner : IReviewCommandRunner
{
    // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient console
    // code page (Windows cp932) so Japanese payloads stay valid JSON/text.
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public ReviewCommandResult Run(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gh process.");
        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ReviewCommandResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdOut,
            StdErr = stdErr
        };
    }
}
