using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class DurableStatePreflightAnalyzerTests
{
    [Fact]
    public void Analyze_QueueStateForwardOnly_ReturnsVerifiedCommitReady()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly,
                        Summary = "added linked_pr=`https://github.com/o/r/pull/551` on `SKS-G215`",
                        Changes = new[]
                        {
                            new QueueStateForwardChange
                            {
                                ExecutionUnit = "SKS-G215",
                                Kind = QueueStateForwardChangeKind.AddedLinkedPr,
                                LinkedPrUrl = "https://github.com/o/r/pull/551",
                            },
                        },
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, result.Classification);
        Assert.Single(result.VerifiedPaths);
        Assert.Empty(result.ReviewPaths);
        Assert.Empty(result.UnsafePaths);
        Assert.NotNull(result.RecommendedCommitMessage);
        Assert.Contains(".intent-cli/queue-state.json", result.RecommendedCommitMessage!, StringComparison.Ordinal);
        Assert.Contains("G312", result.RecommendedCommitMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_RunsJsonlAppendOnly_ReturnsVerifiedCommitReady()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/runs.jsonl",
                    IsDeleted = false,
                    RunsJsonlDelta = new RunsJsonlAppendOnlyResult
                    {
                        Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly,
                        Summary = "runs.jsonl is append-only with 2 new event(s).",
                        AppendedEventCount = 2,
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, result.Classification);
        Assert.Single(result.VerifiedPaths);
        Assert.NotNull(result.RecommendedCommitMessage);
    }

    [Fact]
    public void Analyze_BothQueueStateAndRunsForwardOnly_ReturnsVerifiedCommitReady()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                BuildVerifiedQueueStatePath(),
                BuildVerifiedRunsJsonlPath(),
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, result.Classification);
        Assert.Equal(2, result.VerifiedPaths.Count);
    }

    [Fact]
    public void Analyze_QueueStateNeedsReview_ReturnsNeedsOperatorReview()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview,
                        Summary = "title changed",
                        Changes = Array.Empty<QueueStateForwardChange>(),
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
        Assert.Single(result.ReviewPaths);
        Assert.Empty(result.VerifiedPaths);
        Assert.Null(result.RecommendedCommitMessage);
    }

    [Fact]
    public void Analyze_DirtyIntentsPath_ReturnsUnsafe()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = "intents/intent-cli/intent-tree/00-map.md",
                    IsDeleted = false,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Single(result.UnsafePaths);
        Assert.Contains("intents/", result.UnsafePaths[0].Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DirtyPublishYaml_WithoutDelta_ReturnsUnsafe()
    {
        // G343: when the caller supplies no canonical-content delta we
        // cannot prove the publish.yaml write is a deterministic
        // host-loop write, so the path stays unsafe. The unsafe-stop
        // message names the recovery command so host-loop guidance can
        // route the operator to a structured repair (G343 AC5).
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/issues/SKS-G215/publish.yaml",
                    IsDeleted = false,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Single(result.UnsafePaths);
        Assert.Contains("automation publish-lifecycle-repair", result.UnsafePaths[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DirtyPublishYaml_CanonicalContent_ReturnsVerified()
    {
        // G343 AC1: canonical publish.yaml — the durable host-loop
        // write produced by the publish-flow command — is accepted as
        // a forward-only durable-state path and routed into the
        // verified-commit-ready lane. Without this, the issue-publish
        // boundary stalls before applying `intent-target`.
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/issues/SKS-G343/publish.yaml",
                    IsDeleted = false,
                    PublishYamlDelta = new PublishYamlCanonicalResult
                    {
                        Classification = PublishYamlCanonicalAnalyzer.ClassificationCanonical,
                        Summary = "publish.yaml for `SKS-G343` parses as canonical.",
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady, result.Classification);
        Assert.Single(result.VerifiedPaths);
        Assert.Equal(".intent-cli/issues/SKS-G343/publish.yaml", result.VerifiedPaths[0].Path);
        Assert.Empty(result.UnsafePaths);
    }

    [Fact]
    public void Analyze_DirtyPublishYaml_NonCanonicalContent_ReturnsUnsafe()
    {
        // G343 AC4: non-canonical publish.yaml content (operator-edited
        // execution-unit drift) stays a hard structured stop with the
        // recovery command surfaced.
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/issues/SKS-G343/publish.yaml",
                    IsDeleted = false,
                    PublishYamlDelta = new PublishYamlCanonicalResult
                    {
                        Classification = PublishYamlCanonicalAnalyzer.ClassificationNonCanonical,
                        Summary = "execution_unit drift detected. Run "
                            + "intent-cli automation publish-lifecycle-repair --write to regenerate.",
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Single(result.UnsafePaths);
        Assert.Contains("execution_unit drift", result.UnsafePaths[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DirtyPublishYaml_InvalidContent_ReturnsUnsafe()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/issues/SKS-G343/publish.yaml",
                    IsDeleted = false,
                    PublishYamlDelta = new PublishYamlCanonicalResult
                    {
                        Classification = PublishYamlCanonicalAnalyzer.ClassificationInvalid,
                        Summary = "unparseable YAML; regenerate via repair lane.",
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
    }

    [Fact]
    public void Analyze_DirtyNonPublishYamlUnderIssuesDir_ReturnsUnsafe()
    {
        // G343: operator-owned files alongside publish.yaml (issue
        // body markdown, clarifications, ad-hoc notes) MUST stay
        // unsafe even though publish.yaml itself is now liftable.
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/issues/SKS-G343/issue-body.md",
                    IsDeleted = false,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Single(result.UnsafePaths);
        Assert.Contains("operator-owned", result.UnsafePaths[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_DeletedDurableStatePath_ReturnsUnsafe()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = true,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Contains("deleted", result.UnsafePaths[0].Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Analyze_InvalidQueueStateJson_ReturnsUnsafe()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationInvalid,
                        Summary = "did not parse",
                        Changes = Array.Empty<QueueStateForwardChange>(),
                    },
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
    }

    [Fact]
    public void Analyze_MixedVerifiedAndUnsafe_ReturnsUnsafe()
    {
        // If ANY path is unsafe, the whole bundle is unsafe — never let a
        // verified path slip through alongside an unsafe one.
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                BuildVerifiedQueueStatePath(),
                new DurableStateDirtyPath
                {
                    Path = "intents/intent-cli/intent-tree/00-map.md",
                    IsDeleted = false,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
        Assert.Null(result.RecommendedCommitMessage);
    }

    [Fact]
    public void Analyze_AgentsMarkdown_ReturnsUnsafe()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = "AGENTS.md",
                    IsDeleted = false,
                },
            },
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationUnsafe, result.Classification);
    }

    [Fact]
    public void Analyze_EmptyDirtyPaths_ReturnsNeedsOperatorReview()
    {
        var input = new DurableStatePreflightInput
        {
            DirtyPaths = Array.Empty<DurableStateDirtyPath>(),
        };

        var result = DurableStatePreflightAnalyzer.Analyze(input);

        // No verified paths and no unsafe paths — classification is
        // needs-operator-review (the host loop should never call this
        // with an empty bundle, but the analyzer must be deterministic).
        Assert.Equal(DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview, result.Classification);
    }

    private static DurableStateDirtyPath BuildVerifiedQueueStatePath() => new()
    {
        Path = ".intent-cli/queue-state.json",
        IsDeleted = false,
        QueueStateDelta = new QueueStateForwardDeltaResult
        {
            Classification = QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly,
            Summary = "added linked_pr=`https://github.com/o/r/pull/551` on `SKS-G215`",
            Changes = new[]
            {
                new QueueStateForwardChange
                {
                    ExecutionUnit = "SKS-G215",
                    Kind = QueueStateForwardChangeKind.AddedLinkedPr,
                    LinkedPrUrl = "https://github.com/o/r/pull/551",
                },
            },
        },
    };

    private static DurableStateDirtyPath BuildVerifiedRunsJsonlPath() => new()
    {
        Path = ".intent-cli/runs.jsonl",
        IsDeleted = false,
        RunsJsonlDelta = new RunsJsonlAppendOnlyResult
        {
            Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly,
            Summary = "runs.jsonl is append-only with 1 new event(s).",
            AppendedEventCount = 1,
        },
    };
}
