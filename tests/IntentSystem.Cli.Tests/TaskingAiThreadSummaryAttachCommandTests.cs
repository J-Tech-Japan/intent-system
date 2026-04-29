using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G201 — coverage for <c>intent-cli tasking ai-thread-summary-attach</c>.
/// Class name contains <c>Tasking</c>, <c>AiThreadSummary</c>, and
/// <c>Attach</c> so reviewers can match the issue's recommended
/// <c>~TaskingAiThreadSummary</c> filter.
/// </summary>
public sealed class TaskingAiThreadSummaryAttachCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;
    private readonly Func<DateTimeOffset> originalTimestamp;

    public TaskingAiThreadSummaryAttachCommandTests()
    {
        originalLauncher = TaskingAiThreadSummaryAttachCommand.NestedProviderLauncher;
        originalTimestamp = TaskingAiThreadSummaryAttachCommand.TimestampFactory;
    }

    public void Dispose()
    {
        TaskingAiThreadSummaryAttachCommand.NestedProviderLauncher = originalLauncher;
        TaskingAiThreadSummaryAttachCommand.TimestampFactory = originalTimestamp;
    }

    [Fact]
    public void Execute_GivenValidBundleAndSummary_WritesAttachmentArtifact()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("v1");
        var summaryPath = workspace.WriteSummary("summary-v1.txt", "Operator-authored AI thread summary content.\n");
        var outPath = workspace.GetPath("attachment-v1.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.Equal(TaskingAiThreadSummaryAttachConstants.LocalOnlyStatus, attachment!.AttachmentStatus);
        Assert.False(attachment.IsPublished);
        Assert.False(attachment.IsAutomationVisible);
        Assert.Equal(
            TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.HandoffBundle,
            attachment.SourceArtifactKind);
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            attachment.SummaryLine,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidTaskPacketAndSummary_RecordsKindTaskPacket()
    {
        using var workspace = new AttachWorkspace();
        var taskPacketPath = workspace.GenerateTaskPacket("tp1");
        var summaryPath = workspace.WriteSummary("summary-tp1.txt", "task-packet summary.\n");
        var outPath = workspace.GetPath("attachment-tp1.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", taskPacketPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.Equal(
            TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacket,
            attachment!.SourceArtifactKind);
    }

    [Fact]
    public void Execute_GivenValidPreviewAndSummary_RecordsKindTaskPacketPreview()
    {
        using var workspace = new AttachWorkspace();
        var previewPath = workspace.GeneratePreview("pv1");
        var summaryPath = workspace.WriteSummary("summary-pv1.txt", "preview summary.\n");
        var outPath = workspace.GetPath("attachment-pv1.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", previewPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.Equal(
            TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacketPreview,
            attachment!.SourceArtifactKind);
    }

    [Fact]
    public void Execute_GivenValidChecklistAndSummary_RecordsKindTaskPacketChecklist()
    {
        using var workspace = new AttachWorkspace();
        var checklistPath = workspace.GenerateChecklist("ck1");
        var summaryPath = workspace.WriteSummary("summary-ck1.txt", "checklist summary.\n");
        var outPath = workspace.GetPath("attachment-ck1.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", checklistPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.Equal(
            TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.TaskPacketChecklist,
            attachment!.SourceArtifactKind);
    }

    [Fact]
    public void Execute_GivenValidHandoffPacketAndSummary_RecordsKindHandoffPacket()
    {
        using var workspace = new AttachWorkspace();
        var handoffPath = workspace.GenerateHandoff("hp1");
        var summaryPath = workspace.WriteSummary("summary-hp1.txt", "handoff summary.\n");
        var outPath = workspace.GetPath("attachment-hp1.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", handoffPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.Equal(
            TaskingAiThreadSummaryAttachConstants.SourceArtifactKinds.HandoffPacket,
            attachment!.SourceArtifactKind);
    }

    [Fact]
    public void Execute_GivenValidInputs_AttachmentJsonRoundTripsStably()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("rt");
        var summaryPath = workspace.WriteSummary("summary-rt.txt", "round-trip summary.\n");
        var outPath = workspace.GetPath("attachment-rt.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);
        Assert.Equal(0, exitCode);

        var first = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(first);
        var reserialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(reserialized);
        Assert.NotNull(second);

        Assert.Equal(first!.SourceArtifactPath, second!.SourceArtifactPath);
        Assert.Equal(first.SourceArtifactSha256, second.SourceArtifactSha256);
        Assert.Equal(first.SourceArtifactKind, second.SourceArtifactKind);
        Assert.Equal(first.SourceSummaryPath, second.SourceSummaryPath);
        Assert.Equal(first.SourceSummarySha256, second.SourceSummarySha256);
        Assert.Equal(first.SourceSummaryByteCount, second.SourceSummaryByteCount);
        Assert.Equal(first.Domain, second.Domain);
        Assert.Equal(first.IsPublished, second.IsPublished);
        Assert.Equal(first.IsAutomationVisible, second.IsAutomationVisible);
        Assert.Equal(first.AttachmentStatus, second.AttachmentStatus);
        Assert.Equal(first.GeneratedAtUtc, second.GeneratedAtUtc);
        Assert.Equal(first.ArtifactPath, second.ArtifactPath);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
    }

    [Fact]
    public void Execute_GivenValidInputs_RecordsBothSha256s()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("sha");
        var summaryContent = "deterministic summary content for sha test\n";
        var summaryPath = workspace.WriteSummary("summary-sha.txt", summaryContent);
        var outPath = workspace.GetPath("attachment-sha.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);
        Assert.Equal(0, exitCode);

        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);

        var artifactBytes = File.ReadAllBytes(bundlePath);
        var summaryBytes = File.ReadAllBytes(summaryPath);
        var expectedArtifactSha = IssuePrepareCommand.ComputeSha256Hex(artifactBytes);
        var expectedSummarySha = IssuePrepareCommand.ComputeSha256Hex(summaryBytes);

        Assert.Equal(expectedArtifactSha, attachment!.SourceArtifactSha256);
        Assert.Equal(expectedSummarySha, attachment.SourceSummarySha256);
        Assert.Equal(summaryBytes.Length, attachment.SourceSummaryByteCount);
        Assert.Matches("^[0-9a-f]+$", attachment.SourceArtifactSha256);
        Assert.Matches("^[0-9a-f]+$", attachment.SourceSummarySha256);
    }

    [Fact]
    public void Execute_GivenMissingArtifactFile_ReturnsNonZero_StatusMissingArtifact()
    {
        using var workspace = new AttachWorkspace();
        var missingPath = workspace.GetPath("never-existed.json");
        var summaryPath = workspace.WriteSummary("summary-missing.txt", "any summary\n");
        var outPath = workspace.GetPath("attachment-missing-artifact.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", missingPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"missing_artifact\"", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedArtifact_ReturnsNonZero_StatusMalformedSourceArtifact()
    {
        using var workspace = new AttachWorkspace();
        var malformedPath = workspace.GetPath("malformed.json");
        File.WriteAllText(malformedPath, "{ this is :: not valid json,,,");
        var summaryPath = workspace.WriteSummary("summary-malformed.txt", "summary text\n");
        var outPath = workspace.GetPath("attachment-malformed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", malformedPath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"status\": \"malformed_source_artifact\"", output, StringComparison.Ordinal);
        Assert.Contains("parse failure", output, StringComparison.Ordinal);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingSummaryFile_ReturnsNonZero_StatusMissingSummary()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("ms");
        var missingSummary = workspace.GetPath("no-summary-here.txt");
        var outPath = workspace.GetPath("attachment-missing-summary.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", missingSummary, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"missing_summary\"", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenBlankSummary_ReturnsNonZero_StatusBlankSummary()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("blank");
        var summaryPath = workspace.WriteSummary("summary-blank.txt", "");
        var outPath = workspace.GetPath("attachment-blank.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("\"status\": \"blank_summary\"", output, StringComparison.Ordinal);
        Assert.Contains("empty content", output, StringComparison.Ordinal);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenWhitespaceOnlySummary_ReturnsNonZero_StatusBlankSummary()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("ws");
        var summaryPath = workspace.WriteSummary("summary-ws.txt", "   \t \n\t  \r\n   ");
        var outPath = workspace.GetPath("attachment-ws.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("\"status\": \"blank_summary\"", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("mf");
        var summaryPath = workspace.WriteSummary("summary-mf.txt", "x\n");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("uk");
        var summaryPath = workspace.WriteSummary("summary-uk.txt", "x\n");
        var outPath = workspace.GetPath("attachment-uk.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-artifact", bundlePath,
                "--from-summary", summaryPath,
                "--out", outPath,
                "--bogus", "value"
            },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains("--bogus", output, StringComparison.Ordinal);
        Assert.Contains("Unknown argument", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("queue");
        var summaryPath = workspace.WriteSummary("summary-queue.txt", "queue test\n");
        var outPath = workspace.GetPath("attachment-queue.json");

        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(queueBefore, File.ReadAllBytes(queueStatePath));
        Assert.Equal(runsBefore, File.ReadAllBytes(runsLogPath));
    }

    [Fact]
    public void Execute_NeverOverwritesUnrelatedArtifacts_OnlyWritesToOutFlag()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("only-out");
        var summaryPath = workspace.WriteSummary("summary-only-out.txt", "only-out test\n");
        var outPath = workspace.GetPath("attachment-only-out.json");

        var sentinelPath = workspace.GetPath("sentinel.txt");
        var sentinelBytes = Encoding.UTF8.GetBytes("untouched\n");
        File.WriteAllBytes(sentinelPath, sentinelBytes);

        var bundleBefore = File.ReadAllBytes(bundlePath);
        var summaryBefore = File.ReadAllBytes(summaryPath);
        var sentinelBefore = File.ReadAllBytes(sentinelPath);

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));
        Assert.Equal(bundleBefore, File.ReadAllBytes(bundlePath));
        Assert.Equal(summaryBefore, File.ReadAllBytes(summaryPath));
        Assert.Equal(sentinelBefore, File.ReadAllBytes(sentinelPath));
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("np");
        var summaryPath = workspace.WriteSummary("summary-np.txt", "no provider\n");
        var outPath = workspace.GetPath("attachment-np.json");

        var launcherInvoked = false;
        TaskingAiThreadSummaryAttachCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "TaskingAiThreadSummaryAttachCommand.cs",
                     "TaskingAiThreadSummaryAttachAnalyzer.cs",
                     "TaskingAiThreadSummaryAttachArtifact.cs"
                 })
        {
            var path = Path.Combine(commandsDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_NeverGeneratesSummaryText_NoLlmOrAiCall()
    {
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "TaskingAiThreadSummaryAttachCommand.cs",
                     "TaskingAiThreadSummaryAttachAnalyzer.cs",
                     "TaskingAiThreadSummaryAttachArtifact.cs"
                 })
        {
            var path = Path.Combine(commandsDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
            Assert.DoesNotContain("OpenAI", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Anthropic", text, StringComparison.Ordinal);
            Assert.DoesNotContain("AzureAI", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_NeverCreatesBranchOrWorktree_NoGitInvocationInNewSurface()
    {
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "TaskingAiThreadSummaryAttachCommand.cs",
                     "TaskingAiThreadSummaryAttachAnalyzer.cs",
                     "TaskingAiThreadSummaryAttachArtifact.cs"
                 })
        {
            var path = Path.Combine(commandsDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("git checkout", text, StringComparison.Ordinal);
            Assert.DoesNotContain("git worktree", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_NeverPublishesGitHubIssue_NoGhInvocationInNewSurface()
    {
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "TaskingAiThreadSummaryAttachCommand.cs",
                     "TaskingAiThreadSummaryAttachAnalyzer.cs",
                     "TaskingAiThreadSummaryAttachArtifact.cs"
                 })
        {
            var path = Path.Combine(commandsDir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("gh issue create", text, StringComparison.Ordinal);
            Assert.DoesNotContain("gh issue edit", text, StringComparison.Ordinal);
            Assert.DoesNotContain("gh pr", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("contract");
        var summaryPath = workspace.WriteSummary("summary-contract.txt", "contract test\n");
        var outPath = workspace.GetPath("attachment-contract.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var attachment = JsonSerializer.Deserialize<TaskingAiThreadSummaryAttachArtifact>(
            File.ReadAllBytes(outPath));
        Assert.NotNull(attachment);
        Assert.False(attachment!.IsPublished);
        Assert.False(attachment.IsAutomationVisible);
        Assert.Equal("local_only", attachment.AttachmentStatus);
        Assert.Equal(TaskingAiThreadSummaryAttachConstants.LocalOnlyStatus, attachment.AttachmentStatus);
    }

    [Fact]
    public void Execute_GivenValidInputs_TextOutputContainsLiteralStatusPhrase()
    {
        using var workspace = new AttachWorkspace();
        var bundlePath = workspace.GenerateBundle("text");
        var summaryPath = workspace.WriteSummary("summary-text.txt", "text output test\n");
        var outPath = workspace.GetPath("attachment-text.json");

        using var writer = new StringWriter();
        var exitCode = TaskingAiThreadSummaryAttachCommand.Execute(
            workspace.Context,
            new[] { "--from-artifact", bundlePath, "--from-summary", summaryPath, "--out", outPath, "--format", "text" },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("UNPUBLISHED, local-only, not automation-visible", output, StringComparison.Ordinal);
    }

    private static bool TryResolveCommandsSourceDirectory(out string commandsDirectory)
    {
        commandsDirectory = string.Empty;
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "IntentSystem.Cli", "Commands");
            if (Directory.Exists(candidate))
            {
                commandsDirectory = candidate;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private sealed class AttachWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-ai-thread-summary-attach-tests-")
            .FullName;

        public AttachWorkspace()
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

        public string GetPath(string relative) => Path.Combine(rootPath, relative);

        public string WriteSummary(string fileName, string content)
        {
            var path = GetPath(fileName);
            File.WriteAllText(path, content);
            return path;
        }

        public string GenerateHandoff(string tag)
        {
            var handoffPath = GetPath($"handoff-{tag}.json");
            using var sink = new StringWriter();
            var exit = TaskingHandoffCommand.Execute(
                Context,
                new[] { "--out", handoffPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G190 handoff for tests: exit={exit}, stderr={sink}");
            }

            return handoffPath;
        }

        public string GenerateTaskPacket(string tag)
        {
            var handoffPath = GenerateHandoff($"for-tp-{tag}");
            var taskPacketPath = GetPath($"task-packet-{tag}.json");
            using var sink = new StringWriter();
            var exit = TaskingTaskPacketCommand.Execute(
                Context,
                new[] { "--from-handoff", handoffPath, "--out", taskPacketPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G191 task packet for tests: exit={exit}, stderr={sink}");
            }

            return taskPacketPath;
        }

        public string GeneratePreview(string tag)
        {
            var taskPacketPath = GenerateTaskPacket($"for-pv-{tag}");
            var previewPath = GetPath($"preview-{tag}.json");
            using var sink = new StringWriter();
            var exit = TaskingTaskPacketPreviewCommand.Execute(
                Context,
                new[] { "--from-task-packet", taskPacketPath, "--out", previewPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G192 preview for tests: exit={exit}, stderr={sink}");
            }

            return previewPath;
        }

        public string GenerateChecklist(string tag)
        {
            var previewPath = GeneratePreview($"for-ck-{tag}");
            var checklistPath = GetPath($"checklist-{tag}.json");
            using var sink = new StringWriter();
            var exit = TaskingTaskPacketChecklistCommand.Execute(
                Context,
                new[] { "--from-preview", previewPath, "--out", checklistPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G193 checklist for tests: exit={exit}, stderr={sink}");
            }

            return checklistPath;
        }

        public string GenerateBundle(string tag)
        {
            var previewPath = GeneratePreview($"for-bundle-{tag}");
            var checklistPath = GetPath($"checklist-bundle-{tag}.json");
            using (var sink = new StringWriter())
            {
                var exit = TaskingTaskPacketChecklistCommand.Execute(
                    Context,
                    new[] { "--from-preview", previewPath, "--out", checklistPath },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G193 checklist for bundle tests: exit={exit}, stderr={sink}");
                }
            }

            var bundlePath = GetPath($"bundle-{tag}.json");
            using (var sink = new StringWriter())
            {
                var exit = TaskingHandoffBundleCommand.Execute(
                    Context,
                    new[]
                    {
                        "--from-preview", previewPath,
                        "--from-checklist", checklistPath,
                        "--out", bundlePath
                    },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G194 bundle for tests: exit={exit}, stderr={sink}");
                }
            }

            return bundlePath;
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
