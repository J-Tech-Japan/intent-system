using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class NextSliceClassifyCommandTests
{
    [Fact]
    public void Execute_GivenWipPresent_ClassifiesAsSkipDueToWip()
    {
        using var workspace = new NextSliceWorkspace();
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G180",
                  "title": "context collect command",
                  "state": "active",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G180/implementation.md",
                    "review_context": ".intent-cli/issues/G180/review-context.md",
                    "yaml": ".intent-cli/issues/G180/packet.yaml"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.SkipDueToWip, result!.Classification);
        Assert.NotEmpty(result.WipRefs);
        Assert.Contains("G180", result.WipRefs);
    }

    [Fact]
    public void Execute_GivenOpenClarificationBlocker_ClassifiesAsClarificationRequired()
    {
        using var workspace = new NextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            - Should we use plain text format by default for `next-slice classify`?
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.ClarificationRequired, result!.Classification);
        Assert.False(string.IsNullOrWhiteSpace(result.ClarificationSummary));
    }

    [Fact]
    public void Execute_GivenIssuePacketCandidate_ClassifiesAsIssueCutReady()
    {
        using var workspace = new NextSliceWorkspace();
        workspace.WriteIssuePacket("G999", "# G999 sample issue body\n");

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.IssueCutReady, result!.Classification);
        Assert.Equal("G999", result.CandidateExecutionUnit);
        Assert.False(string.IsNullOrWhiteSpace(result.CandidatePacketPath));
        Assert.Contains("G999", result.CandidatePacketPath!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIncompletePacketMissingReviewContext_ClassifiesAsInspectManually()
    {
        // Regression for the G185 review note: a directory containing only
        // github-body.md (no review-context.md) is a partial packet that
        // intent-cli's existing issue prepare/publish-reviewed boundary
        // cannot deterministically consume. The classifier must NOT report
        // such a state as `issue-cut-ready`; it must degrade to
        // `inspect-manually` with a reason naming the incomplete packet.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteIncompleteIssuePacket("G998", "# G998 partial body only\n");

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.InspectManually, result!.Classification);
        Assert.Null(result.CandidateExecutionUnit);
        Assert.Null(result.CandidatePacketPath);
        Assert.NotNull(result.Reason);
        Assert.Contains("incomplete", result.Reason!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review-context.md", result.Reason!, StringComparison.Ordinal);
        Assert.Contains("G998", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenEmptyState_ClassifiesAsNoActionableItem()
    {
        using var workspace = new NextSliceWorkspace();

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.NoActionableItem, result!.Classification);
        Assert.Empty(result.WipRefs);
        Assert.Null(result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_GivenMalformedQueueState_ClassifiesAsInspectManually()
    {
        using var workspace = new NextSliceWorkspace();
        workspace.WriteQueueState("{ this is not valid json at all");

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.InspectManually, result!.Classification);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Contains("queue-state", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_RoundTripsStably()
    {
        using var workspace = new NextSliceWorkspace();
        workspace.WriteIssuePacket("G555", "# packet body\n");

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);

        // Stable snake_case fields are present and round-trip.
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
        Assert.Equal(NextSliceClassification.IssueCutReady, root.GetProperty("classification").GetString());
        Assert.True(root.TryGetProperty("rationale", out _));
        Assert.True(root.TryGetProperty("wip_refs", out _));
        Assert.True(root.TryGetProperty("clarification_summary", out _));
        Assert.True(root.TryGetProperty("candidate_execution_unit", out _));
        Assert.True(root.TryGetProperty("candidate_packet_path", out _));
        Assert.True(root.TryGetProperty("recommended_next_action", out _));
        Assert.True(root.TryGetProperty("reason", out _));

        var roundTripped = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.Equal(NextSliceClassification.IssueCutReady, roundTripped.Classification);
        Assert.Equal("G555", roundTripped.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_GivenSentinelOnlyClarifications_DoesNotClassifyAsClarificationRequired()
    {
        // Same-shape regression that bit G179: the durable Japanese no-blocker
        // sentinel under "## Current Open Blockers" must NOT trigger
        // clarification-required. This verifies the shared
        // ClarificationOpenDetector is being used.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            このファイルは durable な markdown artifact として残し続ける。

            ## Current Open Blockers

            - 現時点で child issue cut を要する root blocker はない。
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.NotEqual(NextSliceClassification.ClarificationRequired, result!.Classification);
        // With nothing else actionable, this should fall through to no-actionable-item.
        Assert.Equal(NextSliceClassification.NoActionableItem, result.Classification);
    }

    [Fact]
    public void Execute_GivenUnknownFormat_ReturnsNonZero()
    {
        using var workspace = new NextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenLinkedIssuePacket_ClassifiesAsNoActionableItem()
    {
        // A packet for a unit that is already linked in queue-state should NOT
        // be reported as issue-cut-ready.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteIssuePacket("G900", "# already linked\n");
        workspace.WriteQueueState(
            """
            {
              "schema_version": "1",
              "updated_at": "2026-04-28T23:00:00Z",
              "items": [
                {
                  "execution_unit": "G900",
                  "title": "already linked",
                  "state": "queued",
                  "dependencies": [],
                  "blocked_by": [],
                  "clarification_return_path": "intents/intent-cli/clarifications/open.md",
                  "packet_paths": {
                    "implementation": ".intent-cli/issues/G900/implementation.md",
                    "review_context": ".intent-cli/issues/G900/review-context.md",
                    "yaml": ".intent-cli/issues/G900/packet.yaml"
                  },
                  "linked_issue": {
                    "repo": "owner/repo",
                    "number": 123,
                    "url": "https://github.com/owner/repo/issues/123"
                  },
                  "worker_role": "coder",
                  "review_role": "reviewer",
                  "priority": "high"
                }
              ]
            }
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(NextSliceClassification.NoActionableItem, result!.Classification);
    }

    // ─── G275 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Execute_G275_CrossDomainPacketExcluded_WhenDomainAndTargetRepoSpecified()
    {
        // SKS-G183 has clarification_return_path pointing to sks domain.
        // When --domain intent-cli is passed, it must be excluded.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteIssuePacketWithQueueEntry(
            "SKS-G183",
            "# SKS packet body\n",
            "intents/sks/clarifications/open.md");

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json", "--domain", "intent-cli"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        // SKS packet excluded → no actionable item.
        Assert.Equal(NextSliceClassification.NoActionableItem, result!.Classification);
        Assert.Null(result.CandidateExecutionUnit);
    }

    [Fact]
    public void Execute_G275_ClarifiedIntentState_DoesNotBlock()
    {
        // A clarification file with intent_state: clarified must not classify as
        // ClarificationRequired, even when there are bullet items in the section.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            ---
            intent_state: clarified
            ---
            ## Current Open Blockers

            - This note is present but the file is clarified
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.NotEqual(NextSliceClassification.ClarificationRequired, result!.Classification);
    }

    [Fact]
    public void Execute_G275_ClarificationSectionNone_DoesNotBlock()
    {
        // A clarification file whose "Current Open Blockers" section contains only
        // "None" (bare text) must not classify as ClarificationRequired.
        using var workspace = new NextSliceWorkspace();
        workspace.WriteClarificationOpen(
            """
            # intent-cli clarifications

            ## Current Open Blockers

            None
            """);

        using var writer = new StringWriter();
        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.NotEqual(NextSliceClassification.ClarificationRequired, result!.Classification);
        Assert.Equal(NextSliceClassification.NoActionableItem, result.Classification);
    }

    [Fact]
    public void Execute_G275_TargetRepoFlag_IsAccepted()
    {
        // --target-repo flag must be parseable without error.
        using var workspace = new NextSliceWorkspace();
        using var writer = new StringWriter();

        var exitCode = NextSliceClassifyCommand.Execute(
            workspace.Context,
            ["--format", "json", "--target-repo", "J-Tech-Japan/intent-system"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<NextSliceClassifyResult>(writer.ToString());
        Assert.NotNull(result);
    }

    private sealed class NextSliceWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("next-slice-classify-tests-")
            .FullName;

        public NextSliceWorkspace()
        {
            Directory.CreateDirectory(Path.Combine(rootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = rootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli"
                    }
                }
            };
        }

        public CliContext Context { get; }

        public void WriteQueueState(string content)
        {
            File.WriteAllText(Context.GetQueueStatePath(), content);
        }

        public void WriteClarificationOpen(string content)
        {
            var path = Path.Combine(rootPath, "intents", "intent-cli", "clarifications");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "open.md"), content);
        }

        public void WriteIssuePacket(string executionUnit, string body)
        {
            // A complete packet has BOTH github-body.md AND review-context.md
            // (the canonical packet shape produced by the projection pipeline).
            // Tests that want an INCOMPLETE packet should use
            // WriteIncompleteIssuePacket instead.
            var path = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "github-body.md"), body);
            File.WriteAllText(
                Path.Combine(path, "review-context.md"),
                $"# Review context for {executionUnit}\n\nGenerated for next-slice classify tests.\n");
        }

        public void WriteIncompleteIssuePacket(string executionUnit, string body)
        {
            // Writes only github-body.md (no review-context.md) to simulate a
            // partial packet that the issue prepare/publish-reviewed boundary
            // cannot deterministically consume.
            var path = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "github-body.md"), body);
        }

        /// <summary>
        /// G275: Writes a complete packet AND a queue-state entry with the given
        /// clarification_return_path so domain filtering can be tested.
        /// </summary>
        public void WriteIssuePacketWithQueueEntry(
            string executionUnit,
            string body,
            string clarificationReturnPath)
        {
            WriteIssuePacket(executionUnit, body);
            WriteQueueState(
                $$"""
                {
                  "schema_version": "1",
                  "updated_at": "2026-05-01T00:00:00Z",
                  "items": [
                    {
                      "execution_unit": "{{executionUnit}}",
                      "title": "test packet",
                      "state": "queued",
                      "dependencies": [],
                      "blocked_by": [],
                      "clarification_return_path": "{{clarificationReturnPath}}",
                      "packet_paths": {"implementation": "a", "review_context": "b", "yaml": "c"},
                      "worker_role": "coder",
                      "review_role": "reviewer",
                      "priority": "normal"
                    }
                  ]
                }
                """);
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
