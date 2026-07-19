using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G535 review repair: adapter-level tests for
/// <see cref="GhCliGitHubLabelMutator.ReplaceLabelSet"/> — the atomic,
/// single-GitHub-request label-set replacement used by <c>automation
/// pr-transition</c>'s <c>request-update</c> path. These exercise the real
/// adapter class (not a fake <see cref="IGitHubLabelMutator"/>), injecting
/// <see cref="GhCliGitHubLabelMutator.GhInvokerOverride"/> to simulate the
/// underlying `gh` process without spawning one, so the atomicity,
/// idempotent-no-op, and phase-aware failure-certainty contract is
/// actually proven rather than merely asserted at the command layer.
/// </summary>
public sealed class GhCliGitHubLabelMutatorReplaceLabelSetTests : IDisposable
{
    public void Dispose()
    {
        GhCliGitHubLabelMutator.GhInvokerOverride = null;
    }

    [Fact]
    public void ReplaceLabelSet_AlreadyConverged_ReturnsNoOpAndMakesZeroGhCalls()
    {
        var invocations = 0;
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, _) =>
        {
            invocations++;
            return string.Empty;
        };

        var certainty = mutator.ReplaceLabelSet(
            "org/repo",
            GhCliGitHubLabelMutator.Kinds.Pr,
            1760,
            currentLabels: new[] { "intent-target", "intent-pr-request-update" },
            desiredLabels: new[] { "intent-pr-request-update", "intent-target" }); // same set, different order

        Assert.Equal(LabelSetReplacementCertainty.NoOpAlreadyConverged, certainty);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public void ReplaceLabelSet_IssuesExactlyOnePutCall_ThenOneVerifyReadNoOtherCalls()
    {
        var invocations = new List<(IReadOnlyList<string> Arguments, string? StandardInput)>();
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (arguments, standardInput, _) =>
        {
            invocations.Add((arguments, standardInput));
            if (invocations.Count == 1)
            {
                return string.Empty; // PUT response body is unused.
            }

            return """{"labels":[{"name":"intent-target"},{"name":"intent-pr-request-update"}]}""";
        };

        var certainty = mutator.ReplaceLabelSet(
            "org/repo",
            GhCliGitHubLabelMutator.Kinds.Pr,
            1760,
            currentLabels: new[] { "intent-target", "intent-pr-rereview-ready" },
            desiredLabels: new[] { "intent-pr-request-update", "intent-target" });

        Assert.Equal(LabelSetReplacementCertainty.AppliedAndVerified, certainty);
        Assert.Equal(2, invocations.Count);

        var put = invocations[0];
        Assert.Equal("api", put.Arguments[0]);
        Assert.Contains("--method", put.Arguments);
        Assert.Contains("PUT", put.Arguments);
        Assert.Contains("repos/org/repo/issues/1760/labels", put.Arguments);
        Assert.NotNull(put.StandardInput);
        Assert.Contains("intent-target", put.StandardInput);
        Assert.Contains("intent-pr-request-update", put.StandardInput);
        // No sequential add/remove `gh pr edit` call anywhere in the sequence.
        Assert.DoesNotContain(invocations, call => call.Arguments.Contains("edit"));

        var verify = invocations[1];
        Assert.Equal("pr", verify.Arguments[0]);
        Assert.Equal("view", verify.Arguments[1]);
        Assert.Null(verify.StandardInput);
    }

    [Fact]
    public void ReplaceLabelSet_GhProcessNeverStarts_ThrowsKnownUnapplied()
    {
        // "Pre-send failure" — the gh process itself never started, so
        // nothing was transmitted to GitHub. Safe to report known-unapplied.
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, description) =>
            throw new GhProcessNotStartedException($"simulated launch failure: {description}");

        var exception = Assert.Throws<LabelSetReplacementException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                currentLabels: new[] { "intent-target" },
                desiredLabels: new[] { "intent-target", "intent-pr-request-update" }));

        Assert.Equal(LabelSetReplacementFailureCertainty.KnownUnapplied, exception.Certainty);
        Assert.Contains("never transmitted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceLabelSet_MutationTransmissionAmbiguous_ThrowsMayHaveApplied()
    {
        // "Post-send nonzero/timeout" — the gh process for the PUT started
        // but failed afterward (e.g. non-zero exit, mid-transmission
        // error). We cannot know whether GitHub already applied it.
        var invocations = 0;
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, description) =>
        {
            invocations++;
            throw new InvalidOperationException($"simulated post-send failure: {description}");
        };

        var exception = Assert.Throws<LabelSetReplacementException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                currentLabels: new[] { "intent-target" },
                desiredLabels: new[] { "intent-target", "intent-pr-request-update" }));

        Assert.Equal(LabelSetReplacementFailureCertainty.MayHaveApplied, exception.Certainty);
        Assert.Contains("may already have reached", exception.Message, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", exception.Message, StringComparison.Ordinal);
        // Never even attempts the verify re-read once the PUT's own outcome is ambiguous.
        Assert.Equal(1, invocations);
    }

    [Fact]
    public void ReplaceLabelSet_VerifyReadFailure_ThrowsAppliedButVerificationReadFailed()
    {
        // "Verify failure" — the PUT succeeded, but the post-write
        // verification read itself failed (e.g. current-label fetch
        // failure). The mutation LIKELY applied; must not report rollback.
        var invocations = 0;
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, description) =>
        {
            invocations++;
            if (invocations == 1)
            {
                return string.Empty; // PUT succeeds.
            }

            throw new InvalidOperationException($"simulated re-read failure: {description}");
        };

        var exception = Assert.Throws<LabelSetReplacementException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                currentLabels: new[] { "intent-target" },
                desiredLabels: new[] { "intent-target", "intent-pr-request-update" }));

        Assert.Equal(LabelSetReplacementFailureCertainty.AppliedButVerificationReadFailed, exception.Certainty);
        Assert.Contains("LIKELY applied", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public void ReplaceLabelSet_VerifyReadMismatch_ThrowsAppliedButMismatchedOrConcurrentlyChanged()
    {
        // "Mismatch" — the PUT call reports success and the re-read
        // succeeds, but shows a DIFFERENT set than requested. Must not be
        // reported as a rollback/no-mutation — the write itself very
        // likely applied.
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, _) =>
            """{"labels":[{"name":"intent-target"},{"name":"some-other-label"}]}""";

        var exception = Assert.Throws<LabelSetReplacementException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                currentLabels: new[] { "intent-target" },
                desiredLabels: new[] { "intent-target", "intent-pr-request-update" }));

        Assert.Equal(
            LabelSetReplacementFailureCertainty.AppliedButMismatchedOrConcurrentlyChanged,
            exception.Certainty);
        Assert.Contains("very likely applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceLabelSet_DesiredSetContainingIntentPrCreatedOnPr_RefusesBeforeAnyGhCall()
    {
        var invocations = 0;
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, _) =>
        {
            invocations++;
            return string.Empty;
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                currentLabels: new[] { "intent-target" },
                desiredLabels: new[] { "intent-target", "intent-pr-created" }));

        // A pure validation refusal, not a phase-aware ambiguity — never
        // reached GitHub at all.
        Assert.IsNotType<LabelSetReplacementException>(exception);
        Assert.Equal(0, invocations);
    }

    [Fact]
    public void BuildReplaceLabelsArguments_ReturnsSinglePutCallShape()
    {
        var arguments = GhCliGitHubLabelMutator.BuildReplaceLabelsArguments(
            "org/repo", GhCliGitHubLabelMutator.Kinds.Pr, 1760);

        Assert.Equal(
            new[] { "api", "--method", "PUT", "repos/org/repo/issues/1760/labels", "--input", "-" },
            arguments);
    }

    [Fact]
    public void BuildReplaceLabelsRequestBody_SerializesDeduplicatedSortedLabels()
    {
        var body = GhCliGitHubLabelMutator.BuildReplaceLabelsRequestBody(
            new[] { "intent-pr-request-update", "intent-target", "intent-target", "", "  " });

        Assert.Equal("""{"labels":["intent-pr-request-update","intent-target"]}""", body);
    }
}
