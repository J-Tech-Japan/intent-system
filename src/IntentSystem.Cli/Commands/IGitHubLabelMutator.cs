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
    /// caller passes <paramref name="currentLabels"/> (already fetched)
    /// alongside the full desired set (current labels minus superseded
    /// ones, plus new ones) so unrelated labels are preserved AND so the
    /// implementation can detect an already-converged request and perform
    /// a genuine no-op — zero GitHub calls — without an extra read.
    ///
    /// Returns <see cref="LabelSetReplacementCertainty"/> on a KNOWN good
    /// outcome (converged no-op, or applied-and-verified). On any failure
    /// whose outcome on GitHub is not certain, throws
    /// <see cref="LabelSetReplacementException"/> carrying a
    /// <see cref="LabelSetReplacementFailureCertainty"/> — implementations
    /// must never claim "nothing changed" once a mutation request may
    /// have reached GitHub. See
    /// <see cref="GhCliGitHubLabelMutator.ReplaceLabelSet"/> for the
    /// production adapter's documented atomicity/concurrency model.
    /// </summary>
    LabelSetReplacementCertainty ReplaceLabelSet(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> currentLabels,
        IReadOnlyCollection<string> desiredLabels);
}

/// <summary>
/// G535 review repair: the KNOWN-good outcomes of
/// <see cref="IGitHubLabelSetReplacer.ReplaceLabelSet"/>. Ambiguous/failed
/// outcomes are never represented here — they throw
/// <see cref="LabelSetReplacementException"/> instead, so a caller can
/// never mistake "I don't know what happened" for a success value.
/// </summary>
internal enum LabelSetReplacementCertainty
{
    /// <summary>The desired set already equaled the current set (order-insensitive) — no GitHub call was made.</summary>
    NoOpAlreadyConverged,

    /// <summary>The PUT was transmitted and the post-write verification read confirmed the desired set landed exactly.</summary>
    AppliedAndVerified,
}

/// <summary>
/// G535 review repair: classifies a <see cref="LabelSetReplacementException"/>
/// by what is actually knowable about whether the mutation reached GitHub —
/// never conflating "we don't know" with "it definitely didn't happen".
/// </summary>
internal enum LabelSetReplacementFailureCertainty
{
    /// <summary>
    /// The failure occurred before any request was transmitted (e.g. the
    /// `gh` process itself never started). GitHub was never contacted —
    /// safe to report that nothing changed.
    /// </summary>
    KnownUnapplied,

    /// <summary>
    /// The `gh` process for the PUT call started, but something failed
    /// before its outcome could be confirmed (non-zero exit, a write/read
    /// error mid-transmission, a timeout). The request may already have
    /// been transmitted and applied by GitHub — the caller must re-read
    /// current labels rather than assume either outcome.
    /// </summary>
    MayHaveApplied,

    /// <summary>
    /// The PUT call itself reported success, but the post-write
    /// verification read failed, so the resulting label state could not be
    /// confirmed. The mutation most likely applied.
    /// </summary>
    AppliedButVerificationReadFailed,

    /// <summary>
    /// The PUT call reported success and the verification read succeeded,
    /// but the labels read back did not match the desired set. This
    /// generally means a concurrent change raced the write — but note the
    /// bounded limitation documented on
    /// <see cref="GhCliGitHubLabelMutator.ReplaceLabelSet"/>: a concurrent
    /// label added BEFORE this method's PUT (and therefore invisible to
    /// its <c>desiredLabels</c> computation) may have been silently
    /// overwritten by the PUT and is UNDETECTABLE by this check, since the
    /// verification read would then equal <c>desiredLabels</c> exactly.
    /// </summary>
    AppliedButMismatchedOrConcurrentlyChanged,
}

/// <summary>
/// G535 review repair: thrown by <see cref="IGitHubLabelSetReplacer.ReplaceLabelSet"/>
/// for any outcome that is not a KNOWN success. <see cref="Certainty"/>
/// tells the caller exactly how much (or how little) is knowable about
/// whether the mutation reached GitHub.
/// </summary>
internal sealed class LabelSetReplacementException : InvalidOperationException
{
    public LabelSetReplacementException(
        string message,
        LabelSetReplacementFailureCertainty certainty,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Certainty = certainty;
    }

    public LabelSetReplacementFailureCertainty Certainty { get; }
}

/// <summary>
/// G535 review repair: thrown specifically when the `gh` process for a
/// label-set-replacing PUT call never actually started (e.g. the
/// executable could not be launched) — the one case where a subsequent
/// failure can be confidently classified as
/// <see cref="LabelSetReplacementFailureCertainty.KnownUnapplied"/> rather
/// than <see cref="LabelSetReplacementFailureCertainty.MayHaveApplied"/>.
/// Once a process has started, ANY failure is ambiguous — see
/// <see cref="GhCliGitHubLabelMutator.RunGhWithInput"/>.
/// </summary>
internal sealed class GhProcessNotStartedException : IOException
{
    public GhProcessNotStartedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
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
    /// <c>gh &lt;kind&gt; edit</c>. A single HTTP call means there is no
    /// window where one label lands and another doesn't from THIS call's
    /// own actions — but it does NOT mean every failure is knowably
    /// harmless; see the phase-aware failure semantics below.
    ///
    /// <para><b>Already-converged no-op:</b> if <paramref name="currentLabels"/>
    /// already equals <paramref name="desiredLabels"/> (order-insensitive),
    /// this returns <see cref="LabelSetReplacementCertainty.NoOpAlreadyConverged"/>
    /// immediately — zero GitHub calls, genuinely idempotent, not merely
    /// non-erroring.</para>
    ///
    /// <para><b>Phase-aware failure semantics — this is the part that must
    /// never overclaim safety:</b></para>
    /// <list type="bullet">
    /// <item>If the `gh` process for the PUT never starts (e.g. the
    /// executable can't be launched), NOTHING was transmitted — throws
    /// with <see cref="LabelSetReplacementFailureCertainty.KnownUnapplied"/>.</item>
    /// <item>Once that process HAS started, ANY failure (non-zero exit, a
    /// write/read error, a timeout) is ambiguous: `gh` may have already
    /// transmitted the request and GitHub may have already applied it
    /// before the failure surfaced. Throws with
    /// <see cref="LabelSetReplacementFailureCertainty.MayHaveApplied"/> —
    /// never claims the mutation did not happen.</item>
    /// <item>If the PUT itself reports success but the post-write
    /// verification read fails, the mutation MOST LIKELY applied but its
    /// resulting state is unconfirmed — throws with
    /// <see cref="LabelSetReplacementFailureCertainty.AppliedButVerificationReadFailed"/>.</item>
    /// <item>If the PUT reports success and the verification read
    /// succeeds but the labels don't match, throws with
    /// <see cref="LabelSetReplacementFailureCertainty.AppliedButMismatchedOrConcurrentlyChanged"/>
    /// — this is NOT a rollback signal, the PUT itself still very likely
    /// applied.</item>
    /// </list>
    ///
    /// <para><b>Bounded concurrency model — stated honestly:</b> GitHub's
    /// "Set labels" endpoint has no conditional/If-Match support for
    /// optimistic concurrency, so a label change racing between the
    /// caller's initial label read (used to compute <paramref
    /// name="desiredLabels"/>) and this method's PUT cannot be prevented.
    /// The post-write verification read only detects a mismatch that is
    /// STILL PRESENT at the moment of that read. A label added by another
    /// process AFTER the caller's initial read but BEFORE this PUT — and
    /// therefore never reflected in <paramref name="desiredLabels"/> — can
    /// be silently overwritten by the PUT; if nothing ELSE changes labels
    /// between the PUT and the verification read, that read will equal
    /// <paramref name="desiredLabels"/> exactly and this method will
    /// report <see cref="LabelSetReplacementCertainty.AppliedAndVerified"/>
    /// even though a concurrent addition was just lost. This race is
    /// fundamentally undetectable by a read-after-write check alone —
    /// verification proves "the state now matches what we intended," not
    /// "no concurrent change occurred at any point."</para>
    /// </summary>
    public LabelSetReplacementCertainty ReplaceLabelSet(
        string repo,
        string kind,
        int number,
        IReadOnlyCollection<string> currentLabels,
        IReadOnlyCollection<string> desiredLabels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(currentLabels);
        ArgumentNullException.ThrowIfNull(desiredLabels);

        // Policy guard: intent-pr-created is issue-only, mirroring
        // ApplyLabelTransitions' equivalent check. This is a pure
        // validation refusal before any GitHub interaction — genuinely
        // known-unapplied, so it stays a plain InvalidOperationException
        // rather than a LabelSetReplacementException; the command layer
        // treats any non-LabelSetReplacementException failure as
        // known-unapplied by construction.
        if (string.Equals(kind, Kinds.Pr, StringComparison.Ordinal)
            && desiredLabels.Contains(WorkerNextActionConstants.Labels.IntentPrCreated, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"label policy violation: '{WorkerNextActionConstants.Labels.IntentPrCreated}' is issue-only and must not be applied to a PR.");
        }

        var normalizedCurrent = NormalizeLabelSet(currentLabels);
        var normalizedDesired = NormalizeLabelSet(desiredLabels);

        if (normalizedCurrent.SequenceEqual(normalizedDesired, StringComparer.Ordinal))
        {
            return LabelSetReplacementCertainty.NoOpAlreadyConverged;
        }

        var putArguments = BuildReplaceLabelsArguments(repo, kind, number);
        var requestBody = BuildReplaceLabelsRequestBody(normalizedDesired);

        try
        {
            InvokeGh(putArguments, requestBody, $"replace label set on {kind} #{number} in {repo}");
        }
        catch (GhProcessNotStartedException exception)
        {
            throw new LabelSetReplacementException(
                $"the label-set replacement request for {kind} #{number} in {repo} was never transmitted: "
                + $"{exception.Message}",
                LabelSetReplacementFailureCertainty.KnownUnapplied,
                exception);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            throw new LabelSetReplacementException(
                $"the label-set replacement request for {kind} #{number} in {repo} may already have reached "
                + $"GitHub before this failure occurred: {exception.Message}. The mutation's outcome is UNKNOWN — "
                + $"re-read current labels (`gh {kind} view {number} --repo {repo} --json labels`) before "
                + "retrying or assuming any state.",
                LabelSetReplacementFailureCertainty.MayHaveApplied,
                exception);
        }

        IReadOnlyList<GitHubAutomationLabel> actual;
        try
        {
            var verifyArguments = BuildViewArguments(repo, kind, number);
            var verifyStdout = InvokeGh(verifyArguments, null, $"verify replaced label set on {kind} #{number} in {repo}");
            actual = DeserializeLabels(verifyStdout, $"`gh {kind} view #{number}` for {repo}");
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            throw new LabelSetReplacementException(
                $"the label-set replacement request for {kind} #{number} in {repo} was transmitted, but the "
                + $"post-write verification read failed: {exception.Message}. The mutation LIKELY applied but its "
                + "exact resulting state is unconfirmed — re-read current labels before assuming any state.",
                LabelSetReplacementFailureCertainty.AppliedButVerificationReadFailed,
                exception);
        }

        var actualLabels = NormalizeLabelSet(actual.Select(label => label.Name));
        if (!actualLabels.SequenceEqual(normalizedDesired, StringComparer.Ordinal))
        {
            throw new LabelSetReplacementException(
                $"the label-set replacement request for {kind} #{number} in {repo} was transmitted, but the "
                + $"post-write read-back does not match the intended set: expected [{string.Join(", ", normalizedDesired)}] "
                + $"but read [{string.Join(", ", actualLabels)}]. The write itself very likely applied — this most "
                + "often means a concurrent label change followed it — re-read current labels before assuming any state.",
                LabelSetReplacementFailureCertainty.AppliedButMismatchedOrConcurrentlyChanged);
        }

        return LabelSetReplacementCertainty.AppliedAndVerified;
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

    /// <summary>
    /// G535 review repair: used exclusively by <see cref="ReplaceLabelSet"/>'s
    /// PUT call, where the distinction between "never started" (safe to
    /// call known-unapplied) and "started but failed" (ambiguous — may
    /// have transmitted/applied) is load-bearing. <see cref="Process.Start(ProcessStartInfo)"/>
    /// failing or returning null throws <see cref="GhProcessNotStartedException"/>;
    /// any failure after that point (writing stdin, reading stdout, a
    /// non-zero exit) throws a plain <see cref="InvalidOperationException"/>
    /// so the caller cannot mistake it for the known-unapplied case.
    /// </summary>
    private static string RunGhWithInput(IReadOnlyList<string> arguments, string standardInput, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
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

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GhProcessNotStartedException(
                    $"failed to start `gh` process to {description}");
        }
        catch (GhProcessNotStartedException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or IOException)
        {
            // Process.Start itself threw before any process exists — still
            // definitively "never started."
            throw new GhProcessNotStartedException(
                $"could not start `gh` to {description}: {exception.Message}",
                exception);
        }

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            using (process)
            {
                process.StandardInput.Write(standardInput);
                process.StandardInput.Close();
                stdout = process.StandardOutput.ReadToEnd();
                stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                exitCode = process.ExitCode;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            // The process DID start — from here on, any failure is
            // ambiguous: `gh` may already have transmitted (and GitHub
            // already applied) the request before this exception surfaced.
            throw new InvalidOperationException(
                $"`gh` process for '{description}' started but failed before completion: {exception.Message}",
                exception);
        }

        if (exitCode != 0)
        {
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"`gh` exited {exitCode} to {description}: {errorBody.Trim()} (the process started, so the "
                + "underlying request may already have reached GitHub)");
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
            StandardOutputEncoding = ProcessOutputEncoding.Utf8NoBom,
            StandardErrorEncoding = ProcessOutputEncoding.Utf8NoBom,
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
