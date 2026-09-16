using System.Text.Json;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: every byte-identity fixture asserts the path its name describes before byte comparison.
/// </summary>
internal static class G839FixtureExpectations
{
    internal static void AssertMatchesFixtureName(string fixtureId, string output)
    {
        if (TryGetRecoveryExpectation(fixtureId, out var recoveryField, out var recoveryValue))
        {
            AssertRecoveryField(output, recoveryField, recoveryValue, fixtureId);
            return;
        }

        if (fixtureId.StartsWith("pr-transition-refusal-", StringComparison.Ordinal))
        {
            Assert.Contains(GetPrTransitionRefusalCause(fixtureId), output, StringComparison.Ordinal);
            return;
        }

        switch (fixtureId)
        {
            case "pr-transition-help":
                Assert.Contains("Usage: intent-cli automation pr-transition", output, StringComparison.Ordinal);
                return;
            case "pr-transition-parse-error":
                Assert.Contains("--pr is required", output, StringComparison.Ordinal);
                return;
            case "pr-transition-failure-may-have-applied-write-json":
                AssertPrTransitionMayHaveApplied(output, expected: true, fixtureId);
                return;
            case "pr-transition-failure-known-unapplied-write-json":
                AssertPrTransitionMayHaveApplied(output, expected: false, fixtureId);
                return;
            case "recovery-help":
                Assert.Contains("Usage: intent-cli automation pr-created-stale-recovery", output, StringComparison.Ordinal);
                return;
            case "recovery-parse-error":
                Assert.Contains("--issue is required", output, StringComparison.Ordinal);
                return;
            case "planned-labels-automation-summary":
                Assert.Contains("\"host_pr_transition_commands\"", output, StringComparison.Ordinal);
                return;
            case "planned-labels-automation-doctor":
                Assert.Contains("status: ok", output, StringComparison.Ordinal);
                return;
            case "planned-labels-worker-next-action":
                Assert.Contains("\"action\"", output, StringComparison.Ordinal);
                return;
            case "planned-labels-worker-pr-comment-preflight":
                Assert.Contains("\"actionable\"", output, StringComparison.Ordinal);
                return;
        }

        if (fixtureId.StartsWith("pr-transition-", StringComparison.Ordinal)
            && fixtureId.EndsWith("-json", StringComparison.Ordinal))
        {
            AssertPrTransitionApplied(output, fixtureId.Contains("-write-", StringComparison.Ordinal), fixtureId);
            return;
        }

        if (fixtureId.StartsWith("pr-transition-", StringComparison.Ordinal)
            && fixtureId.EndsWith("-text", StringComparison.Ordinal)
            && !fixtureId.StartsWith("pr-transition-refusal-", StringComparison.Ordinal))
        {
            var expectedApplied = fixtureId.Contains("-write-", StringComparison.Ordinal);
            Assert.Contains($"applied: {expectedApplied}", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool TryGetRecoveryExpectation(string fixtureId, out string fieldName, out string expectedValue)
    {
        fieldName = string.Empty;
        expectedValue = string.Empty;
        if (!fixtureId.StartsWith("recovery-", StringComparison.Ordinal))
        {
            return false;
        }

        if (fixtureId is "recovery-help" or "recovery-parse-error")
        {
            return false;
        }

        var middle = ExtractRecoveryMiddle(fixtureId);
        if (fixtureId.Contains("-refusal-", StringComparison.Ordinal)
            || fixtureId is "recovery-ruling-missing-markdown")
        {
            fieldName = "cause";
            expectedValue = middle;
            return true;
        }

        if (fixtureId.EndsWith("-write-json", StringComparison.Ordinal)
            && (middle.Contains("-failed", StringComparison.Ordinal)
                || middle.EndsWith("-unconfirmed", StringComparison.Ordinal)))
        {
            fieldName = "cause";
            expectedValue = middle;
            return true;
        }

        fieldName = "outcome";
        expectedValue = middle;
        return true;
    }

    private static string ExtractRecoveryMiddle(string fixtureId)
    {
        var remainder = fixtureId["recovery-".Length..];
        foreach (var suffix in new[]
                 {
                     "-dry-run-json",
                     "-dry-run-markdown",
                     "-write-json",
                     "-refusal-json",
                     "-refusal-markdown",
                     "-json",
                     "-markdown",
                 })
        {
            if (remainder.EndsWith(suffix, StringComparison.Ordinal))
            {
                return remainder[..^suffix.Length];
            }
        }

        return remainder;
    }

    private static void AssertRecoveryField(string output, string fieldName, string expectedValue, string fixtureId)
    {
        if (output.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            var actual = document.RootElement.GetProperty(fieldName).GetString();
            Assert.True(
                string.Equals(expectedValue, actual, StringComparison.Ordinal),
                $"Fixture '{fixtureId}' expected {fieldName} '{expectedValue}' but got '{actual}'.");
            return;
        }

        var marker = $"- {fieldName}: {expectedValue}";
        Assert.Contains(
            marker,
            output,
            StringComparison.Ordinal);
    }

    private static string GetPrTransitionRefusalCause(string fixtureId) => fixtureId switch
    {
        "pr-transition-refusal-head-required-text" => "cross-runtime-review-head-required",
        "pr-transition-refusal-head-stale-text" => "cross-runtime-review-head-stale",
        "pr-transition-refusal-team-unresolved-text" => "cross-runtime-review-team-unresolved",
        "pr-transition-refusal-missing-text" => "cross-runtime-review-missing",
        "pr-transition-refusal-blocked-text" => "cross-runtime-review-blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "unknown pr-transition refusal fixture"),
    };

    private static void AssertPrTransitionMayHaveApplied(string output, bool expected, string fixtureId)
    {
        using var document = JsonDocument.Parse(output);
        var actual = document.RootElement.GetProperty("may_have_applied").GetBoolean();
        Assert.True(
            actual == expected,
            $"Fixture '{fixtureId}' expected may_have_applied={expected} but got {actual}.");
    }

    private static void AssertPrTransitionApplied(string output, bool expectedApplied, string fixtureId)
    {
        using var document = JsonDocument.Parse(output);
        var actual = document.RootElement.GetProperty("applied").GetBoolean();
        Assert.True(
            actual == expectedApplied,
            $"Fixture '{fixtureId}' expected applied={expectedApplied} but got {actual}.");
    }
}
