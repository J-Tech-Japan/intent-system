using System.Diagnostics;
using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G211: Testability seam for the <c>intent-cli worker claim</c> and
/// <c>intent-cli worker complete</c> commands. The production
/// implementation shells out to <c>gh issue/pr view --json labels</c>
/// (read) and <c>gh issue/pr edit --add-label / --remove-label</c>
/// (write); tests inject a fake to avoid GitHub network access and to
/// verify the dry-run / write split.
/// </summary>
internal interface IGitHubLabelMutator
{
    /// <summary>
    /// Read the current labels on the issue/PR identified by
    /// <paramref name="repo"/>, <paramref name="kind"/>, and
    /// <paramref name="number"/>. Read-only — never mutates GitHub.
    /// </summary>
    IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number);

    /// <summary>
    /// Apply the requested add/remove transitions in a single round
    /// trip per direction. Implementations MUST reject inputs that
    /// imply <c>intent-pr-created</c> on a PR target — that label is
    /// issue-only by policy.
    /// </summary>
    void ApplyLabelTransitions(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels);
}

/// <summary>
/// G211: Default mutator that shells out to <c>gh</c>. The only file
/// in the worker claim/complete surface permitted to call
/// <c>Process.Start</c> — analyzer and command layers stay pure. The
/// add/remove arguments are issued in a single <c>gh edit</c> call so
/// the operation is roughly atomic from GitHub's perspective.
///
/// All gh edit calls go through the policy-checking
/// <see cref="ApplyLabelTransitions"/> entry point; direct shell-out is
/// not exposed.
/// </summary>
internal sealed class GhCliGitHubLabelMutator : IGitHubLabelMutator
{
    /// <summary>
    /// G211: stable kind tokens accepted by <see cref="ReadLabels"/> and
    /// <see cref="ApplyLabelTransitions"/>. Exposed internally so the
    /// command layer can validate before calling the mutator.
    /// </summary>
    public static class Kinds
    {
        public const string Issue = "issue";
        public const string Pr = "pr";
    }

    /// <summary>
    /// G211: <c>gh view --json labels</c> field name. Exposed so
    /// adapter-shape tests can lock the supported subset.
    /// </summary>
    internal const string ViewJsonFields = "labels";

    /// <summary>
    /// G211: build the <c>gh issue/pr view</c> argument list shared by
    /// the live adapter and adapter-shape tests.
    /// </summary>
    internal static IReadOnlyList<string> BuildViewArguments(string repo, string kind, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number),
                "issue/PR number must be positive.");
        }
        if (!string.Equals(kind, Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, Kinds.Pr, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"unrecognized kind '{kind}'. Supported: '{Kinds.Issue}', '{Kinds.Pr}'.",
                nameof(kind));
        }

        return new List<string>
        {
            kind,
            "view",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", ViewJsonFields,
        };
    }

    /// <summary>
    /// G211: build the <c>gh issue/pr edit</c> argument list. Empty
    /// add/remove sets short-circuit — the caller skips the call.
    /// </summary>
    internal static IReadOnlyList<string> BuildEditArguments(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(addLabels);
        ArgumentNullException.ThrowIfNull(removeLabels);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number),
                "issue/PR number must be positive.");
        }
        if (!string.Equals(kind, Kinds.Issue, StringComparison.Ordinal)
            && !string.Equals(kind, Kinds.Pr, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"unrecognized kind '{kind}'. Supported: '{Kinds.Issue}', '{Kinds.Pr}'.",
                nameof(kind));
        }

        var args = new List<string>
        {
            kind,
            "edit",
            number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repo", repo,
        };
        foreach (var label in addLabels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            args.Add("--add-label");
            args.Add(label);
        }
        foreach (var label in removeLabels)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }
            args.Add("--remove-label");
            args.Add(label);
        }
        return args;
    }

    public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
    {
        var args = BuildViewArguments(repo, kind, number);
        var stdout = RunGh(args, $"read labels on {kind} #{number} in {repo}");
        return DeserializeLabels(stdout, $"`gh {kind} view #{number}` for {repo}");
    }

    public void ApplyLabelTransitions(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        ArgumentNullException.ThrowIfNull(addLabels);
        ArgumentNullException.ThrowIfNull(removeLabels);

        // Policy guard: intent-pr-created is issue-only. The analyzer
        // already refuses this combination, but we also reject it at
        // the mutator boundary so direct callers can't bypass policy.
        if (string.Equals(kind, Kinds.Pr, StringComparison.Ordinal)
            && (addLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal)
                || removeLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"label policy violation: '{WorkerNextActionConstants.Labels.IntentPrCreated}' is issue-only and must not be applied to a PR.");
        }

        if (addLabels.Count == 0 && removeLabels.Count == 0)
        {
            return; // nothing to do
        }

        var args = BuildEditArguments(repo, kind, number, addLabels, removeLabels);
        RunGh(args,
            $"apply label transitions on {kind} #{number} in {repo}");
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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

        return stdout;
    }

    private static IReadOnlyList<GitHubAutomationLabel> DeserializeLabels(
        string stdout,
        string callDescription)
    {
        try
        {
            var view = JsonSerializer.Deserialize<LabelView>(stdout);
            return view?.Labels ?? (IReadOnlyList<GitHubAutomationLabel>)Array.Empty<GitHubAutomationLabel>();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"could not parse {callDescription} JSON: {exception.Message}",
                exception);
        }
    }

    private sealed record LabelView
    {
        [System.Text.Json.Serialization.JsonPropertyName("labels")]
        public IReadOnlyList<GitHubAutomationLabel> Labels { get; init; }
            = Array.Empty<GitHubAutomationLabel>();
    }
}
