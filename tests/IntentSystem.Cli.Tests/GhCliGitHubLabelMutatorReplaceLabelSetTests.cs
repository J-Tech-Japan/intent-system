using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G535 review repair: adapter-level tests for
/// <see cref="GhCliGitHubLabelMutator.ReplaceLabelSet"/> — the atomic,
/// single-GitHub-request label-set replacement used by <c>automation
/// pr-transition</c>'s <c>request-update</c> path. These exercise the real
/// adapter class (not a fake <see cref="IGitHubLabelMutator"/>), injecting
/// <see cref="GhCliGitHubLabelMutator.GhInvokerOverride"/> to simulate the
/// underlying `gh` process without spawning one, so the atomicity and
/// bounded-concurrency (re-read-and-verify) contract is actually proven
/// rather than merely asserted at the command layer.
/// </summary>
public sealed class GhCliGitHubLabelMutatorReplaceLabelSetTests : IDisposable
{
    public void Dispose()
    {
        GhCliGitHubLabelMutator.GhInvokerOverride = null;
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

        mutator.ReplaceLabelSet(
            "org/repo",
            GhCliGitHubLabelMutator.Kinds.Pr,
            1760,
            new[] { "intent-pr-request-update", "intent-target" });

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
    public void ReplaceLabelSet_MutationFailure_ThrowsAndNeverAttemptsVerifyRead()
    {
        // "Mutation HTTP failure" — the PUT call itself throws. No
        // half-transition: since it's the first and only call attempted,
        // nothing on GitHub could have changed, and the adapter must not
        // even attempt the verification re-read.
        var invocations = 0;
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, description) =>
        {
            invocations++;
            throw new InvalidOperationException($"simulated `gh` failure: {description}");
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => mutator.ReplaceLabelSet(
                "org/repo", GhCliGitHubLabelMutator.Kinds.Pr, 1760, new[] { "intent-target" }));

        Assert.Contains("simulated `gh` failure", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public void ReplaceLabelSet_VerifyReadFailure_Throws()
    {
        // "Current-label fetch failure" on the post-write verification
        // re-read — the PUT succeeded but we can no longer confirm it, so
        // the adapter must fail loudly rather than silently claim success.
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

        var exception = Assert.Throws<InvalidOperationException>(
            () => mutator.ReplaceLabelSet(
                "org/repo", GhCliGitHubLabelMutator.Kinds.Pr, 1760, new[] { "intent-target" }));

        Assert.Contains("simulated re-read failure", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public void ReplaceLabelSet_VerifyReadMismatch_ThrowsConcurrentChangeError()
    {
        // "Response mismatch/concurrent-change failure" — the PUT call
        // reports success but the re-read shows a DIFFERENT set than what
        // was requested (e.g. another process changed labels in between).
        // The adapter must fail loudly, never claim atomic success on a
        // lost update.
        var mutator = new GhCliGitHubLabelMutator();
        GhCliGitHubLabelMutator.GhInvokerOverride = (_, _, _) =>
        {
            return """{"labels":[{"name":"intent-target"},{"name":"some-other-label"}]}""";
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                new[] { "intent-target", "intent-pr-request-update" }));

        Assert.Contains("could not be verified", exception.Message, StringComparison.Ordinal);
        Assert.Contains("concurrent", exception.Message, StringComparison.OrdinalIgnoreCase);
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

        Assert.Throws<InvalidOperationException>(
            () => mutator.ReplaceLabelSet(
                "org/repo",
                GhCliGitHubLabelMutator.Kinds.Pr,
                1760,
                new[] { "intent-target", "intent-pr-created" }));

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
