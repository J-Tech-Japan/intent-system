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

    /// <summary>
    /// G277: Apply reconcile-mode label changes from the host-side safe
    /// reconcile lane. Allows specifically the misplacement-repair cases
    /// the strict transition path forbids — namely removing a misplaced
    /// <c>intent-pr-created</c> from a PR. Adding <c>intent-pr-created</c>
    /// to a PR is still rejected as a hard policy violation.
    /// </summary>
    void ApplyReconcileTransitions(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels);
}

/// <summary>
/// G535 review repair: testability seam for atomically replacing an
/// issue/PR's ENTIRE label set in a single GitHub API request, instead of
/// the sequential add/remove calls <see cref="IGitHubLabelMutator.ApplyLabelTransitions"/>
/// issues via <c>gh &lt;kind&gt; edit --add-label --remove-label</c> (which
/// is a `gh` CLI convenience wrapper, not proven to be one atomic HTTP
/// request). Kept as a separate, narrower interface — implemented only by
/// <see cref="GhCliGitHubLabelMutator"/> and the one command (<c>automation
/// pr-transition</c>'s <c>request-update</c> path) that needs the stronger
/// atomicity guarantee — so the many existing <see cref="IGitHubLabelMutator"/>
/// fakes across the test suite do not need to implement a capability they
/// never exercise.
/// </summary>
internal interface IGitHubLabelSetReplacer
{
    /// <summary>
    /// Replace the entire current label set on the issue/PR with
    /// <paramref name="desiredLabels"/> via one GitHub API request. The
    /// caller is responsible for computing the full desired set (current
    /// labels minus superseded ones, plus new ones) so unrelated labels
    /// are preserved. Must throw without any partial effect on failure —
    /// see <see cref="GhCliGitHubLabelMutator.ReplaceLabelSet"/> for the
    /// production adapter's documented atomicity/concurrency model.
    /// </summary>
    void ReplaceLabelSet(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> desiredLabels);
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
internal sealed class GhCliGitHubLabelMutator : IGitHubLabelMutator, IGitHubLabelSetReplacer
{
    /// <summary>
    /// G535 review repair: injectable seam for <see cref="ReplaceLabelSet"/>'s
    /// two `gh` invocations (the atomic PUT and the post-write verification
    /// re-read), so adapter-level tests can simulate a mutation failure, a
    /// re-read failure, or a re-read mismatch (concurrent change) without
    /// spawning a real `gh` process. Signature: (arguments, standard-input-
    /// or-null, description) -&gt; stdout. <see langword="null"/> (the
    /// default) uses the real <c>Process.Start</c>-based runner; only test
    /// code overrides it, and only for this method — <see cref="ReadLabels"/>
    /// and <see cref="ApplyLabelTransitions"/> keep their own unmodified
    /// <c>RunGh</c> path.
    /// </summary>
    internal static Func<IReadOnlyList<string>, string?, string, string>? GhInvokerOverride { get; set; }
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

    /// <summary>
    /// G535 review repair: build the <c>gh api --method PUT
    /// repos/&lt;repo&gt;/issues/&lt;number&gt;/labels --input -</c> argument
    /// list for atomically replacing a label set. GitHub's REST API models
    /// PR labels through the issues endpoint for both issues and PRs, so
    /// <paramref name="kind"/> is validated but not part of the path.
    /// </summary>
    internal static IReadOnlyList<string> BuildReplaceLabelsArguments(string repo, string kind, int number)
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
            "api",
            "--method", "PUT",
            $"repos/{repo}/issues/{number}/labels",
            "--input", "-",
        };
    }

    /// <summary>
    /// G535 review repair: build the JSON request body for the labels-PUT
    /// call above — <c>{"labels":[...]}</c>, deterministically ordered so
    /// the payload (and any test assertion against it) is stable.
    /// </summary>
    internal static string BuildReplaceLabelsRequestBody(IReadOnlyCollection<string> desiredLabels)
    {
        ArgumentNullException.ThrowIfNull(desiredLabels);
        var normalized = NormalizeLabelSet(desiredLabels);
        return JsonSerializer.Serialize(new ReplaceLabelsRequestBody { Labels = normalized });
    }

    /// <summary>
    /// G535 review repair: deterministic, deduplicated, whitespace-free
    /// label set — shared by the request-body builder and the post-write
    /// verification comparison so both sides normalize identically.
    /// </summary>
    private static IReadOnlyList<string> NormalizeLabelSet(IEnumerable<string> labels) =>
        labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();

    private sealed record ReplaceLabelsRequestBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("labels")]
        public required IReadOnlyList<string> Labels { get; init; }
    }

    public IReadOnlyList<GitHubAutomationLabel> ReadLabels(string repo, string kind, int number)
    {
        var args = BuildViewArguments(repo, kind, number);
        var stdout = RunGh(args, $"read labels on {kind} #{number} in {repo}");
        return DeserializeLabels(stdout, $"`gh {kind} view #{number}` for {repo}");
    }

    /// <summary>
    /// G535 review repair: replaces the PR/issue's entire label set with
    /// <paramref name="desiredLabels"/> via ONE GitHub REST call (<c>PUT
    /// /repos/{repo}/issues/{number}/labels</c>), instead of the sequential
    /// add/remove calls <see cref="ApplyLabelTransitions"/> issues through
    /// <c>gh &lt;kind&gt; edit</c>. A single HTTP call means a failure can
    /// never leave a half-applied state (e.g. remove succeeded but add
    /// failed, or vice versa) — the request either fully lands or the call
    /// throws and nothing on GitHub has changed.
    ///
    /// Bounded concurrency model: GitHub's "Set labels" endpoint has no
    /// conditional/If-Match support for optimistic concurrency, so a label
    /// change racing between the caller's label read (before it computed
    /// <paramref name="desiredLabels"/>) and this write cannot be prevented
    /// outright. Instead, after the PUT this method re-reads the labels and
    /// fails loudly — throws — if the result does not match
    /// <paramref name="desiredLabels"/> exactly, rather than silently
    /// claiming success on a lost update.
    /// </summary>
    public void ReplaceLabelSet(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> desiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(desiredLabels);

        // Policy guard: intent-pr-created is issue-only, mirroring
        // ApplyLabelTransitions' equivalent check.
        if (string.Equals(kind, Kinds.Pr, StringComparison.Ordinal)
            && desiredLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"label policy violation: '{WorkerNextActionConstants.Labels.IntentPrCreated}' is issue-only and must not be applied to a PR.");
        }

        var normalizedDesired = NormalizeLabelSet(desiredLabels);
        var putArguments = BuildReplaceLabelsArguments(repo, kind, number);
        var requestBody = BuildReplaceLabelsRequestBody(normalizedDesired);

        InvokeGh(putArguments, requestBody, $"replace label set on {kind} #{number} in {repo}");

        var verifyArguments = BuildViewArguments(repo, kind, number);
        var verifyStdout = InvokeGh(verifyArguments, null, $"verify replaced label set on {kind} #{number} in {repo}");
        var actualLabels = NormalizeLabelSet(
            DeserializeLabels(verifyStdout, $"`gh {kind} view #{number}` for {repo}").Select(label => label.Name));

        if (!actualLabels.SequenceEqual(normalizedDesired, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"label replacement on {kind} #{number} in {repo} could not be verified: expected "
                + $"[{string.Join(", ", normalizedDesired)}] but read back [{string.Join(", ", actualLabels)}] "
                + "after the write (possible concurrent label change); refusing to claim success.");
        }
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

    public void ApplyReconcileTransitions(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        ArgumentNullException.ThrowIfNull(addLabels);
        ArgumentNullException.ThrowIfNull(removeLabels);

        // G277 reconcile policy: the strict transition path bars any touch
        // of intent-pr-created on a PR. Reconcile is the lane that exists
        // precisely to clean up misplacement, so removing a misplaced
        // intent-pr-created from a PR is allowed. Adding intent-pr-created
        // to a PR is still a hard policy violation.
        if (string.Equals(kind, Kinds.Pr, StringComparison.Ordinal)
            && addLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"label policy violation: '{WorkerNextActionConstants.Labels.IntentPrCreated}' is issue-only and must not be added to a PR even from the reconcile lane.");
        }

        if (addLabels.Count == 0 && removeLabels.Count == 0)
        {
            return; // nothing to do
        }

        var args = BuildEditArguments(repo, kind, number, addLabels, removeLabels);
        RunGh(args,
            $"apply reconcile label transitions on {kind} #{number} in {repo}");
    }

    /// <summary>
    /// G535 review repair: dispatches through <see cref="GhInvokerOverride"/>
    /// when a test has set one, otherwise runs the real `gh` process
    /// (piping <paramref name="standardInput"/> to stdin when non-null).
    /// Used exclusively by <see cref="ReplaceLabelSet"/> — <see cref="ReadLabels"/>
    /// and <see cref="ApplyLabelTransitions"/> keep calling <see cref="RunGh"/>
    /// directly, unaffected by this seam.
    /// </summary>
    private static string InvokeGh(IReadOnlyList<string> arguments, string? standardInput, string description)
    {
        if (GhInvokerOverride is { } overrideInvoker)
        {
            return overrideInvoker(arguments, standardInput, description);
        }

        return standardInput is null
            ? RunGh(arguments, description)
            : RunGhWithInput(arguments, standardInput, description);
    }

    private static string RunGhWithInput(IReadOnlyList<string> arguments, string standardInput, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
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
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
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

    private static string RunGh(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            // G484: decode gh stdout/stderr as UTF-8 regardless of the ambient
            // console code page (Windows cp932) so Japanese payloads stay valid.
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom,
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
