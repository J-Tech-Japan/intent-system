using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class ReviewCommentCommandTests
{
    [Fact]
    public void Execute_GivenRequestAndBody_PostsCommentWritesArtifactAndTransitionsToFixing()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var bodyPath = tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            CreateReviewRequestJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"G10","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/46"}""" + Environment.NewLine);
        using var writer = new StringWriter();
        var publisher = new FakePublisher();

        var originalFactory = ReviewCommentCommand.PublisherFactory;
        var originalTimestampFactory = ReviewCommentCommand.TimestampFactory;

        try
        {
            ReviewCommentCommand.PublisherFactory = () => publisher;
            ReviewCommentCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-04T04:40:00Z");

            var exitCode = ReviewCommentCommand.Execute(
                CreateContext(repoRoot),
                ["G10", "--from-file", "prepared-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, publisher.CallCount);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46", publisher.LinkedPr);
            Assert.Equal("repair in place", publisher.Body);
            Assert.Contains("Review comment posted for G10", writer.ToString(), StringComparison.Ordinal);

            var artifact = ReviewCommentArtifactSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "reviews", "G10.comment.json")));
            Assert.Equal("G10", artifact.ExecutionUnit);
            Assert.Equal(".intent-cli/reviews/G10.request.json", artifact.ReviewRequestRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46", artifact.LinkedPr);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1", artifact.CommentRef);
            Assert.Equal(Path.GetFullPath(bodyPath), artifact.BodyPath);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Fixing, queueState.Items.Single(item => item.ExecutionUnit == "G10").State);
            Assert.Equal(QueueItemState.Blocked, queueState.Items.Single(item => item.ExecutionUnit == "B1").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(2, runEvents.Count);
            Assert.Equal("fix-requested", runEvents[^1].Event);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46", runEvents[^1].LinkedPr);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1", runEvents[^1].CommentRef);
        }
        finally
        {
            ReviewCommentCommand.PublisherFactory = originalFactory;
            ReviewCommentCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenCurrentReviewSessionAlreadyPublishedComment_ReusesExistingCommentRefWithoutPostingDuplicate()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var bodyPath = tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            CreateReviewRequestJson());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"G10","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/46"}""" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G10.request.json"),
            """
            {
              "schema_version": "1",
              "execution_unit": "G10",
              "entry_kind": "review",
              "upstream_request_ref": ".intent-cli/reviews/G10.request.json",
              "provider": "Codex",
              "model": "gpt-5.4-mini",
              "transport": "responses",
              "launched_at": "2026-04-04T04:35:00Z",
              "provider_session_id": "pid:777",
              "transport_summary": "launched"
            }
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G10.result.json"),
            """
            {
              "schema_version": "1",
              "execution_unit": "G10",
              "entry_kind": "review",
              "upstream_request_ref": ".intent-cli/reviews/G10.request.json",
              "provider": "Codex",
              "model": "gpt-5.4-mini",
              "session_id": "pid:777",
              "run_status": "succeeded",
              "review_outcome": "fix-requested",
              "review_comment_body_path": ".intent-cli/reviews/G10.comment.md",
              "raw_log_ref": ".intent-cli/runs/G10.provider.jsonl",
              "packet_ref": ".intent-cli/issues/G10/packet.yaml",
              "review_context_ref": ".intent-cli/issues/G10/review-context.md",
              "linked_pr": {
                "repo": "J-Tech-Japan/intent-system",
                "number": 46,
                "url": "https://github.com/J-Tech-Japan/intent-system/pull/46"
              },
              "worktree": {
                "path": "/repo/.intent-cli/worktrees/G10"
              }
            }
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs", "G10.provider.jsonl"),
            string.Join(
                Environment.NewLine,
                [
                    DirectRunProviderEventJsonl.SerializeLine(new DirectRunProviderEvent
                    {
                        Timestamp = "2026-04-04T04:35:00Z",
                        ExecutionUnit = "G10",
                        Provider = "Codex",
                        EntryKind = "review",
                        SessionId = "pid:777",
                        Kind = "provider-event",
                        Payload = System.Text.Json.JsonSerializer.SerializeToElement(
                            "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-9")
                    })
                ]) + Environment.NewLine);
        using var writer = new StringWriter();
        var publisher = new FakePublisher();

        var originalFactory = ReviewCommentCommand.PublisherFactory;
        var originalTimestampFactory = ReviewCommentCommand.TimestampFactory;

        try
        {
            ReviewCommentCommand.PublisherFactory = () => publisher;
            ReviewCommentCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-04T04:40:00Z");

            var exitCode = ReviewCommentCommand.Execute(
                CreateContext(repoRoot),
                ["G10", "--from-file", "prepared-comment.md"],
                writer);

            Assert.Equal(0, exitCode);
            Assert.Equal(0, publisher.CallCount);
            Assert.Contains("Review comment posted for G10", writer.ToString(), StringComparison.Ordinal);

            var artifact = ReviewCommentArtifactSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "reviews", "G10.comment.json")));
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-9", artifact.CommentRef);
            Assert.Equal(Path.GetFullPath(bodyPath), artifact.BodyPath);

            var resultArtifact = DirectRunResultArtifactJson.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs", "G10.result.json")));
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-9", resultArtifact.ReviewCommentRef);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-9", runEvents[^1].CommentRef);
        }
        finally
        {
            ReviewCommentCommand.PublisherFactory = originalFactory;
            ReviewCommentCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingRequestArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        using var writer = new StringWriter();

        var exitCode = ReviewCommentCommand.Execute(
            CreateContext(repoRoot),
            ["G10", "--from-file", "prepared-comment.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review request artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenEmptyBodyFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            CreateReviewRequestJson());
        tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "   ");
        using var writer = new StringWriter();

        var exitCode = ReviewCommentCommand.Execute(
            CreateContext(repoRoot),
            ["G10", "--from-file", "prepared-comment.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must not be empty", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingLinkedPrInRequest_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = Path.Combine(repoRoot, ".intent-cli", "queue-state.json");
        var queueStateContents = QueueStateSerializer.Serialize(CreateQueueState());
        tempDirectory.CreateFile(queueStatePath, queueStateContents);
        var runLogPath = Path.Combine(repoRoot, ".intent-cli", "runs.jsonl");
        tempDirectory.CreateFile(runLogPath, string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "reviews", "G10.request.json"),
            """
            {
              "execution_unit": "G10",
              "review_context_ref": ".intent-cli/issues/G10/review-context.md",
              "deterministic_review_checks": [],
              "acceptance_criteria": [],
              "expected_evidence": []
            }
            """);
        tempDirectory.CreateFile(Path.Combine("repo", "prepared-comment.md"), "repair in place");
        using var writer = new StringWriter();

        var exitCode = ReviewCommentCommand.Execute(
            CreateContext(repoRoot),
            ["G10", "--from-file", "prepared-comment.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("linked_pr", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(queueStateContents, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateItem("G10", QueueItemState.Review),
                CreateItem("B1", QueueItemState.Blocked) with
                {
                    Dependencies = ["G10"],
                    BlockedBy = ["G10"]
                }
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Review Item",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreateReviewRequestJson()
    {
        return """
        {
          "execution_unit": "G10",
          "review_context_ref": ".intent-cli/issues/G10/review-context.md",
          "linked_pr": "https://github.com/J-Tech-Japan/intent-system/pull/46",
          "deterministic_review_checks": [
            "review comment command が deterministic diff review の実行, merge, closeout の責務へ広がっていない"
          ],
          "acceptance_criteria": [],
          "expected_evidence": [
            "dotnet test IntentSystem.sln"
          ]
        }
        """;
    }

    private sealed class FakePublisher : IReviewCommentPublisher
    {
        public string LinkedPr { get; private set; } = string.Empty;

        public string Body { get; private set; } = string.Empty;

        public int CallCount { get; private set; }

        public string PostComment(string linkedPr, string body)
        {
            CallCount++;
            LinkedPr = linkedPr;
            Body = body;
            return "https://github.com/J-Tech-Japan/intent-system/pull/46#issuecomment-1";
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-review-comment-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
