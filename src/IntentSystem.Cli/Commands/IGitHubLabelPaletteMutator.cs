using System.Diagnostics;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: testability seam for label metadata create / edit
/// operations. <see cref="AutomationLabelPaletteSyncCommand"/> uses
/// this to apply the canonical workflow label palette to a repository;
/// tests inject a fake that records calls so assertions can verify
/// exactly which create / edit operations the sync planner emitted.
/// The fake also lets tests verify idempotency: a second sync against
/// the same repo state must record zero mutations.
///
/// Distinct from <see cref="IGitHubLabelMutator"/> (G211), which
/// transitions individual issue/PR label assignments via
/// <c>gh issue/pr edit --add-label / --remove-label</c>. The palette
/// mutator never touches issue or PR assignments — it only manages
/// the label definitions themselves via <c>gh label create</c> /
/// <c>gh label edit</c>.
/// </summary>
internal interface IGitHubLabelPaletteMutator
{
    void CreateLabel(string repo, string name, string color, string description);
    void EditLabel(string repo, string name, string color, string description);
}

/// <summary>
/// G366: default palette mutator that shells out to
/// <c>gh label create</c> and <c>gh label edit</c>.
/// <see cref="AutomationLabelPaletteSyncCommand"/> only calls this
/// when invoked with <c>--write</c>; the read-only path emits the
/// planned mutations as a JSON / text report instead.
/// </summary>
internal sealed class GhCliGitHubLabelPaletteMutator : IGitHubLabelPaletteMutator
{
    public void CreateLabel(string repo, string name, string color, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        ArgumentNullException.ThrowIfNull(description);

        Run(
            new[]
            {
                "label", "create", name,
                "--repo", repo,
                "--color", color,
                "--description", description,
            },
            $"create label `{name}` in {repo}");
    }

    public void EditLabel(string repo, string name, string color, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        ArgumentNullException.ThrowIfNull(description);

        Run(
            new[]
            {
                "label", "edit", name,
                "--repo", repo,
                "--color", color,
                "--description", description,
            },
            $"edit label `{name}` in {repo}");
    }

    private static void Run(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
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
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh` failed to {description} with exit {exitCode}: {errorBody.Trim()}");
        }
    }
}
