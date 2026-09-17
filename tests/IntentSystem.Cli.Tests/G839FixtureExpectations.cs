using System.Text.Json;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G839: every byte-identity fixture asserts the path its name describes before byte comparison.
/// </summary>
internal static class G839FixtureExpectations
{
    internal static readonly string[][] FixtureGroups =
    [
        G839ByteIdentityHarness.RecoveryScenarioIds,
        G839ByteIdentityHarness.PrTransitionRefusalTextScenarioIds,
        G839ByteIdentityHarness.PrTransitionNonRefusalScenarioIds,
        [
            "planned-labels-automation-summary",
            "planned-labels-automation-doctor",
            "planned-labels-worker-next-action",
            "planned-labels-worker-pr-comment-preflight",
        ],
    ];

    internal static string GetNeighborFixtureId(string fixtureId)
    {
        var group = FixtureGroups.First(candidate => candidate.Contains(fixtureId));
        var index = Array.IndexOf(group, fixtureId);
        for (var offset = 1; offset < group.Length; offset++)
        {
            var neighbourId = group[(index + offset) % group.Length];
            if (!HasSameExpectation(fixtureId, neighbourId))
            {
                return neighbourId;
            }
        }

        throw new InvalidOperationException($"No distinct neighbour found for fixture '{fixtureId}'.");
    }

    private static bool HasSameExpectation(string leftFixtureId, string rightFixtureId)
    {
        if (TryGetRecoveryExpectation(leftFixtureId, out var leftField, out var leftValue)
            && TryGetRecoveryExpectation(rightFixtureId, out var rightField, out var rightValue))
        {
            return string.Equals(leftField, rightField, StringComparison.Ordinal)
                && string.Equals(leftValue, rightValue, StringComparison.Ordinal);
        }

        if (PrTransitionJsonExpectations.TryGetValue(leftFixtureId, out var leftJson)
            && PrTransitionJsonExpectations.TryGetValue(rightFixtureId, out var rightJson))
        {
            return leftJson == rightJson;
        }

        if (PrTransitionTextExpectations.TryGetValue(leftFixtureId, out var leftText)
            && PrTransitionTextExpectations.TryGetValue(rightFixtureId, out var rightText))
        {
            return leftText == rightText;
        }

        if (leftFixtureId is "pr-transition-help" or "recovery-help")
        {
            return string.Equals(leftFixtureId, rightFixtureId, StringComparison.Ordinal);
        }

        if (leftFixtureId is "pr-transition-parse-error" or "recovery-parse-error")
        {
            return string.Equals(leftFixtureId, rightFixtureId, StringComparison.Ordinal);
        }

        return string.Equals(leftFixtureId, rightFixtureId, StringComparison.Ordinal);
    }

    internal static void AssertMatchesFixtureName(string fixtureId, string output)
    {
        if (TryGetRecoveryExpectation(fixtureId, out var recoveryField, out var recoveryValue))
        {
            AssertRecoveryField(output, recoveryField, recoveryValue, fixtureId);
            return;
        }

        if (PrTransitionJsonExpectations.TryGetValue(fixtureId, out var jsonExpectation))
        {
            AssertPrTransitionJson(output, jsonExpectation, fixtureId);
            return;
        }

        if (PrTransitionTextExpectations.TryGetValue(fixtureId, out var textExpectation))
        {
            AssertPrTransitionText(output, textExpectation, fixtureId);
            return;
        }

        switch (fixtureId)
        {
            case "pr-transition-help":
                Assert.StartsWith("automation pr-transition", output, StringComparison.Ordinal);
                Assert.Contains("Supported transitions:", output, StringComparison.Ordinal);
                return;
            case "pr-transition-parse-error":
                Assert.StartsWith("--pr is required.", output, StringComparison.Ordinal);
                return;
            case "recovery-help":
                Assert.Equal(
                    "Usage: intent-cli automation pr-created-stale-recovery --repo <owner/repo> --issue <n> --execution-unit <unit> --team <team> --ruling <text> [--write] [--format json|markdown]",
                    output.TrimEnd());
                return;
            case "recovery-parse-error":
                Assert.StartsWith("--issue is required.", output, StringComparison.Ordinal);
                return;
            case "planned-labels-automation-summary":
                Assert.Contains(
                    "intent-cli automation pr-transition --transition review-start --write adds intent-target, intent-pr-reviewing and removes intent-pr-rereview-ready, rereview-ready when present",
                    output,
                    StringComparison.Ordinal);
                return;
            case "planned-labels-automation-doctor":
                Assert.Contains("status: ok", output, StringComparison.Ordinal);
                Assert.Contains("binary_source: cwd-local-shim", output, StringComparison.Ordinal);
                return;
            case "planned-labels-worker-next-action":
                AssertJsonField(output, "action", "wait", fixtureId);
                AssertJsonField(output, "recommended_workflow", "pr-comment-fix", fixtureId);
                return;
            case "planned-labels-worker-pr-comment-preflight":
                AssertJsonField(output, "classification", "repair-required", fixtureId);
                AssertJsonField(output, "actionable", true, fixtureId);
                AssertJsonField(output, "recommended_action", "repair-pr", fixtureId);
                return;
        }

        throw new ArgumentOutOfRangeException(nameof(fixtureId), fixtureId, "unknown fixture");
    }

    private sealed record PrTransitionJsonExpectation(
        string Transition,
        bool Applied,
        string[] AddLabels,
        string[] RemoveLabels,
        string Repo = G839ByteIdentityHarness.Repo,
        string? GateDecision = null,
        string? GateCause = null,
        bool? MayHaveApplied = null,
        string? ErrorContains = null);

    private sealed record PrTransitionTextExpectation(
        string SummaryLine,
        string Mode,
        bool Applied,
        string? CrossRuntimeReviewLine = null);

    private static readonly Dictionary<string, PrTransitionJsonExpectation> PrTransitionJsonExpectations =
        new(StringComparer.Ordinal)
        {
            ["pr-transition-review-start-dry-run-json"] = new(
                "review-start", false,
                ["intent-target", "intent-pr-reviewing"],
                ["intent-pr-rereview-ready", "rereview-ready"]),
            ["pr-transition-review-start-write-json"] = new(
                "review-start", true,
                ["intent-target", "intent-pr-reviewing"],
                []),
            ["pr-transition-request-update-dry-run-json"] = new(
                "request-update", false,
                ["intent-pr-request-update"],
                ["intent-pr-reviewing"]),
            ["pr-transition-request-update-write-json"] = new(
                "request-update", true,
                ["intent-pr-request-update"],
                ["intent-pr-reviewing"]),
            ["pr-transition-review-release-write-json"] = new(
                "review-release", true,
                [],
                ["intent-pr-reviewing"]),
            ["pr-transition-approved-ungated-dry-run-json"] = new(
                "approved", false,
                ["intent-pr-approved"],
                ["intent-pr-reviewing", "intent-pr-rereview-ready", "rereview-ready", "intent-pr-request-update", "intent-pr-update-in-progress"],
                Repo: G839ByteIdentityHarness.UngatedRepo),
            ["pr-transition-approved-ungated-write-json"] = new(
                "approved", true,
                ["intent-pr-approved"],
                ["intent-pr-reviewing"],
                Repo: G839ByteIdentityHarness.UngatedRepo),
            ["pr-transition-approved-undeclared-write-json"] = new(
                "approved", true,
                ["intent-pr-approved"],
                ["intent-pr-reviewing"]),
            ["pr-transition-review-release-dry-run-json"] = new(
                "review-release", false,
                [],
                ["intent-pr-reviewing"]),
            ["pr-transition-approved-undeclared-dry-run-json"] = new(
                "approved", false,
                ["intent-pr-approved"],
                ["intent-pr-reviewing", "intent-pr-rereview-ready", "rereview-ready", "intent-pr-request-update", "intent-pr-update-in-progress"]),
            ["pr-transition-approved-undeclared-write-json"] = new(
                "approved", true,
                ["intent-pr-approved"],
                ["intent-pr-reviewing"]),
            ["pr-transition-approved-satisfied-dry-run-json"] = new(
                "approved", false,
                ["intent-pr-approved"],
                ["intent-pr-reviewing", "intent-pr-rereview-ready", "rereview-ready", "intent-pr-request-update", "intent-pr-update-in-progress"],
                GateDecision: "satisfied"),
            ["pr-transition-approved-satisfied-write-json"] = new(
                "approved", true,
                ["intent-pr-approved"],
                ["intent-pr-reviewing"],
                GateDecision: "satisfied"),
            ["pr-transition-failure-may-have-applied-write-json"] = new(
                "request-update", false,
                ["intent-pr-request-update"],
                ["intent-pr-rereview-ready"],
                MayHaveApplied: true,
                ErrorContains: "simulated ambiguous gh API failure"),
            ["pr-transition-failure-known-unapplied-write-json"] = new(
                "approved", false,
                ["intent-pr-approved"],
                ["intent-pr-rereview-ready"],
                MayHaveApplied: false,
                ErrorContains: "failed to apply PR transition"),
        };

    private static readonly Dictionary<string, PrTransitionTextExpectation> PrTransitionTextExpectations =
        new(StringComparer.Ordinal)
        {
            ["pr-transition-review-start-dry-run-text"] = new(
                "Would apply host PR transition 'review-start': add intent-target, intent-pr-reviewing; remove intent-pr-rereview-ready, rereview-ready.",
                "dry-run", false),
            ["pr-transition-review-start-write-text"] = new(
                "Would apply host PR transition 'review-start': add intent-target, intent-pr-reviewing; remove (none).",
                "write", true),
            ["pr-transition-request-update-dry-run-text"] = new(
                "Would apply host PR transition 'request-update': add intent-pr-request-update; remove intent-pr-reviewing.",
                "dry-run", false),
            ["pr-transition-review-release-dry-run-text"] = new(
                "Would apply host PR transition 'review-release': add (none); remove intent-pr-reviewing.",
                "dry-run", false),
            ["pr-transition-review-release-write-text"] = new(
                "Would apply host PR transition 'review-release': add (none); remove intent-pr-reviewing.",
                "write", true),
            ["pr-transition-request-update-write-text"] = new(
                "Would apply host PR transition 'request-update': add intent-pr-request-update; remove intent-pr-reviewing.",
                "write", true),
            ["pr-transition-approved-ungated-dry-run-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing, intent-pr-rereview-ready, rereview-ready, intent-pr-request-update, intent-pr-update-in-progress.",
                "dry-run", false),
            ["pr-transition-approved-ungated-write-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing.",
                "write", true),
            ["pr-transition-approved-undeclared-dry-run-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing, intent-pr-rereview-ready, rereview-ready, intent-pr-request-update, intent-pr-update-in-progress.",
                "dry-run", false),
            ["pr-transition-approved-undeclared-write-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing.",
                "write", true),
            ["pr-transition-approved-satisfied-dry-run-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing, intent-pr-rereview-ready, rereview-ready, intent-pr-request-update, intent-pr-update-in-progress.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: satisfied"),
            ["pr-transition-approved-satisfied-write-text"] = new(
                "Would apply host PR transition 'approved': add intent-pr-approved; remove intent-pr-reviewing.",
                "write", true,
                CrossRuntimeReviewLine: "cross_runtime_review: satisfied"),
            ["pr-transition-refusal-head-required-text"] = new(
                "cross-runtime-review-head-required: team 'intent-cli/intent-cli-dev' declares cross-runtime review; pass `--head-sha <head-sha>` with the exact head the reviewers approved.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: refused (cross-runtime-review-head-required)"),
            ["pr-transition-refusal-head-stale-text"] = new(
                "cross-runtime-review-head-stale: --head-sha 2222222222222222222222222222222222222222 is not the current head of PR #1823 in J-Tech-Japan/intent-system (3333333333333333333333333333333333333333); review and CI must bind to the current head.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: refused (cross-runtime-review-head-stale)"),
            ["pr-transition-refusal-team-unresolved-text"] = new(
                "cross-runtime-review-team-unresolved: execution unit 'G839' has no held claim with a team (claim status 'unheld': unheld). Fix: acquire the execution-unit claim with a team: `intent-cli claim acquire --scope execution-unit:G839 --actor <actor> --team <team> --reason <text> --write`.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: refused (cross-runtime-review-team-unresolved)"),
            ["pr-transition-refusal-missing-text"] = new(
                "cross-runtime-review-missing: [cross-runtime-review-missing] head 2222222222222222222222222222222222222222 has no approve whose latest record comes from a runtime other than the conductor runtime 'claude' (cross-runtime review).",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: missing (cross-runtime-review-missing)"),
            ["pr-transition-refusal-blocked-text"] = new(
                "cross-runtime-review-blocked: [cross-runtime-review-blocked] the latest record on head 2222222222222222222222222222222222222222 from codex is request-changes. [cross-runtime-review-missing] head 2222222222222222222222222222222222222222 has no approve whose latest record comes from a runtime other than the conductor runtime 'claude' (cross-runtime review).",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: blocked (cross-runtime-review-blocked)"),
            ["pr-transition-refusal-head-unreadable-text"] = new(
                "cross-runtime-review-head-stale: the current head of PR #1823 in J-Tech-Japan/intent-system could not be read, so --head-sha 2222222222222222222222222222222222222222 cannot be confirmed: simulated head read failure",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: refused (cross-runtime-review-head-stale)"),
            ["pr-transition-refusal-rereview-missing-text"] = new(
                "cross-runtime-review-rereview-missing: [cross-runtime-review-rereview-missing] cursor requested changes on head 1111111111111111111111111111111111111111 and has no record on head 2222222222222222222222222222222222222222; that runtime must re-review the delta.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: missing (cross-runtime-review-rereview-missing)"),
            ["pr-transition-refusal-record-unreadable-text"] = new(
                "cross-runtime-review-record-unreadable: [cross-runtime-review-record-unreadable] record '.intent-cli/cross-runtime-reviews/j-tech-japan__intent-system/pr-1823/zz.json' failed validation and fails the gate closed: record is not a valid cross-runtime-review-record: JSON deserialization for type 'IntentSystem.Cli.Commands.CrossRuntimeReviewRecord' was missing required properties including: 'artifact_kind', 'repo', 'pr', 'head_sha', 'execution_unit', 'domain'.",
                "dry-run", false,
                CrossRuntimeReviewLine: "cross_runtime_review: blocked (cross-runtime-review-record-unreadable)"),
            ["pr-transition-failure-may-have-applied-write-text"] = new(
                "failed to confirm PR transition on PR #1823 in J-Tech-Japan/intent-system (the mutation may already have applied — do not assume it did not): simulated ambiguous gh API failure",
                "write", false),
            ["pr-transition-failure-known-unapplied-write-text"] = new(
                "failed to apply PR transition on PR #1823 in J-Tech-Japan/intent-system: failed to apply PR transition",
                "write", false),
        };

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

        if ((fixtureId.EndsWith("-write-json", StringComparison.Ordinal)
                || fixtureId.EndsWith("-write-markdown", StringComparison.Ordinal))
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
                     "-refusal-write-json",
                     "-refusal-write-markdown",
                     "-write-json",
                     "-write-markdown",
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

    private static void AssertPrTransitionJson(string output, PrTransitionJsonExpectation expected, string fixtureId)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        Assert.Equal(expected.Repo, root.GetProperty("repo").GetString());
        Assert.Equal(expected.Transition, root.GetProperty("transition").GetString());
        Assert.Equal(expected.Applied, root.GetProperty("applied").GetBoolean());
        AssertLabelSet(root, "add_labels", expected.AddLabels, fixtureId);
        AssertLabelSet(root, "remove_labels", expected.RemoveLabels, fixtureId);

        if (expected.GateDecision is not null)
        {
            var gate = root.GetProperty("cross_runtime_review");
            Assert.Equal(expected.GateDecision, gate.GetProperty("decision").GetString());
            if (expected.GateCause is not null)
            {
                Assert.Equal(expected.GateCause, gate.GetProperty("cause").GetString());
            }
        }
        else if (root.TryGetProperty("cross_runtime_review", out _))
        {
            Assert.Fail($"Fixture '{fixtureId}' expected no cross_runtime_review gate but one was present.");
        }

        if (expected.MayHaveApplied is not null)
        {
            Assert.Equal(expected.MayHaveApplied.Value, root.GetProperty("may_have_applied").GetBoolean());
        }

        if (expected.ErrorContains is not null)
        {
            var error = root.GetProperty("error").GetString();
            Assert.NotNull(error);
            Assert.Contains(expected.ErrorContains, error, StringComparison.Ordinal);
        }
    }

    private static void AssertPrTransitionText(string output, PrTransitionTextExpectation expected, string fixtureId)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(expected.SummaryLine, lines[0]);
        Assert.Contains($"mode: {expected.Mode}", output, StringComparison.Ordinal);
        Assert.Contains($"applied: {expected.Applied.ToString().ToLowerInvariant()}", output, StringComparison.OrdinalIgnoreCase);

        if (expected.CrossRuntimeReviewLine is not null)
        {
            Assert.Contains(expected.CrossRuntimeReviewLine, output, StringComparison.Ordinal);
        }
    }

    private static void AssertLabelSet(JsonElement root, string propertyName, string[] expected, string fixtureId)
    {
        var actual = root.GetProperty(propertyName)
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.True(
            expected.SequenceEqual(actual!, StringComparer.Ordinal),
            $"Fixture '{fixtureId}' expected {propertyName} [{string.Join(", ", expected)}] but got [{string.Join(", ", actual!)}].");
    }

    private static void AssertJsonField(string output, string propertyName, string expected, string fixtureId)
    {
        using var document = JsonDocument.Parse(output);
        var actual = document.RootElement.GetProperty(propertyName).GetString();
        Assert.True(
            string.Equals(expected, actual, StringComparison.Ordinal),
            $"Fixture '{fixtureId}' expected {propertyName} '{expected}' but got '{actual}'.");
    }

    private static void AssertJsonField(string output, string propertyName, bool expected, string fixtureId)
    {
        using var document = JsonDocument.Parse(output);
        var actual = document.RootElement.GetProperty(propertyName).GetBoolean();
        Assert.True(
            actual == expected,
            $"Fixture '{fixtureId}' expected {propertyName}={expected} but got {actual}.");
    }
}
