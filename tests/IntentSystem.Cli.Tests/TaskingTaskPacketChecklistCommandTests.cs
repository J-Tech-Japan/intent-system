using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G193 — coverage for <c>intent-cli tasking task-packet-checklist</c>. Class
/// name contains <c>Tasking</c>, <c>TaskPacket</c>, and <c>Checklist</c> so
/// reviewers can match the issue's recommended
/// <c>~Tasking|~TaskPacket|~Checklist</c> filter.
/// </summary>
public sealed class TaskingTaskPacketChecklistCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;

    public TaskingTaskPacketChecklistCommandTests()
    {
        // Snapshot only the seams G193 actually mutates. Avoid clobbering
        // upstream G190 / G191 / G192 timestamp seams (the workspace helper
        // invokes those commands but the assertions live on the G193 output).
        originalTimestampFactory = TaskingTaskPacketChecklistCommand.TimestampFactory;
        originalLauncher = TaskingTaskPacketChecklistCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingTaskPacketChecklistCommand.TimestampFactory = originalTimestampFactory;
        TaskingTaskPacketChecklistCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenValidPreview_WritesUnpublishedChecklistArtifact()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview.json");
        var outPath = workspace.GetPath("checklist.json");

        TaskingTaskPacketChecklistCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal("local_only", root.GetProperty("checklist_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
        Assert.Contains("ready_for_handoff=true", summary, StringComparison.Ordinal);

        Assert.True(root.GetProperty("ready_for_handoff").GetBoolean());
    }

    [Fact]
    public void Execute_GivenValidPreview_ChecklistJsonRoundTripsStably()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-rt.json");
        var outPath = workspace.GetPath("checklist-rt.json");

        TaskingTaskPacketChecklistCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(TaskingTaskPacketChecklistConstants.LocalOnlyStatus, roundTripped.ChecklistStatus);
        Assert.Equal("local_only", roundTripped.ChecklistStatus);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Equal(previewPath, roundTripped.SourcePreviewPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
        Assert.True(roundTripped.ReadyForHandoff);
        Assert.Equal(
            TaskingTaskPacketChecklistConstants.CheckIds.All.Count,
            roundTripped.Checks.Count);
    }

    [Fact]
    public void Execute_GivenValidPreview_RecordsAllSourceReferencesAndSha256s()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-sha.json");
        var outPath = workspace.GetPath("checklist-sha.json");

        var expectedPreviewBytes = File.ReadAllBytes(previewPath);
        var expectedPreviewSha = ComputeSha256HexLowercase(expectedPreviewBytes);

        var sourcePreview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(expectedPreviewBytes);
        Assert.NotNull(sourcePreview);

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);
        Assert.Equal(previewPath, checklist!.SourcePreviewPath);
        Assert.Equal(expectedPreviewSha, checklist.SourcePreviewSha256);
        Assert.Equal(expectedPreviewSha, checklist.SourcePreviewSha256.ToLowerInvariant());
        Assert.Equal(sourcePreview!.SourceTaskPacketPath, checklist.SourceTaskPacketPath);
        Assert.Equal(sourcePreview.SourceTaskPacketSha256, checklist.SourceTaskPacketSha256);
        Assert.Equal(sourcePreview.SourceHandoffPath, checklist.SourceHandoffPath);
        Assert.Equal(sourcePreview.SourceHandoffSha256, checklist.SourceHandoffSha256);

        Assert.False(string.IsNullOrWhiteSpace(checklist.SourceTaskPacketPath));
        Assert.False(string.IsNullOrWhiteSpace(checklist.SourceTaskPacketSha256));
        Assert.False(string.IsNullOrWhiteSpace(checklist.SourceHandoffPath));
        Assert.False(string.IsNullOrWhiteSpace(checklist.SourceHandoffSha256));
    }

    [Fact]
    public void Execute_GivenValidPreview_AllTenChecksPresentByStableId()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-checks.json");
        var outPath = workspace.GetPath("checklist-checks.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);

        var expectedIds = TaskingTaskPacketChecklistConstants.CheckIds.All;
        Assert.Equal(10, expectedIds.Count);
        Assert.Equal(expectedIds.Count, checklist!.Checks.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in checklist.Checks)
        {
            Assert.True(seen.Add(check.Id), $"Duplicate check id: {check.Id}");
        }

        foreach (var expected in expectedIds)
        {
            Assert.Contains(expected, seen);
        }
    }

    [Fact]
    public void Execute_GivenValidPreview_ReadyForHandoffTrueIffAllChecksPass()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-ready.json");
        var outPath = workspace.GetPath("checklist-ready.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);
        Assert.True(checklist!.ReadyForHandoff);
        foreach (var check in checklist.Checks)
        {
            Assert.True(check.Passed, $"Expected check '{check.Id}' to pass: {check.Detail}");
        }
    }

    [Fact]
    public void Execute_GivenPreviewWithEmptyRecommendedAction_FailsRecommendedActionCheck()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-empty-action-src.json");
        var modifiedPreviewPath = workspace.GetPath("preview-empty-action.json");
        var preview = JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(
            File.ReadAllText(previewPath));
        Assert.NotNull(preview);
        var mutated = preview! with { RecommendedWorkerAction = "   " };
        File.WriteAllText(modifiedPreviewPath, JsonSerializer.Serialize(mutated));

        var outPath = workspace.GetPath("checklist-empty-action.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", modifiedPreviewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);
        Assert.False(checklist!.ReadyForHandoff);

        var actionCheck = checklist.Checks.Single(
            c => c.Id == TaskingTaskPacketChecklistConstants.CheckIds.RecommendedWorkerActionNonEmpty);
        Assert.False(actionCheck.Passed);
    }

    [Fact]
    public void Execute_GivenPreviewWithEmptyMarkdown_FailsMarkdownChecks()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-empty-md-src.json");
        var modifiedPreviewPath = workspace.GetPath("preview-empty-md.json");
        var preview = JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(
            File.ReadAllText(previewPath));
        Assert.NotNull(preview);
        var mutated = preview! with { RenderedPreviewMarkdown = string.Empty };
        File.WriteAllText(modifiedPreviewPath, JsonSerializer.Serialize(mutated));

        var outPath = workspace.GetPath("checklist-empty-md.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", modifiedPreviewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);
        Assert.False(checklist!.ReadyForHandoff);

        var nonEmpty = checklist.Checks.Single(
            c => c.Id == TaskingTaskPacketChecklistConstants.CheckIds.RenderedPreviewMarkdownNonEmpty);
        var marksUnpublished = checklist.Checks.Single(
            c => c.Id == TaskingTaskPacketChecklistConstants.CheckIds.RenderedPreviewMarksUnpublished);
        Assert.False(nonEmpty.Passed);
        Assert.False(marksUnpublished.Passed);

        Assert.Null(checklist.RenderedPreviewExcerpt);
    }

    [Fact]
    public void Execute_GivenMissingPreviewFile_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new ChecklistWorkspace();
        var missing = workspace.GetPath("does-not-exist.json");
        var outPath = workspace.GetPath("checklist-missing.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", missing, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedPreviewJson_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GetPath("malformed-preview.json");
        File.WriteAllText(previewPath, "{ this is not : valid json,");
        var outPath = workspace.GetPath("checklist-malformed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenPreviewWithWrongStatus_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new ChecklistWorkspace();
        var validPreview = workspace.GeneratePreview("preview-source.json");
        var json = File.ReadAllText(validPreview);
        var tampered = json.Replace(
            "\"preview_status\": \"local_only\"",
            "\"preview_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("preview-tampered.json");
        File.WriteAllText(tamperedPath, tampered);
        var outPath = workspace.GetPath("checklist-tampered.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", tamperedPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-missing-flag.json");

        // Drop --out entirely.
        var args = new[] { "--from-preview", previewPath };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-unknown.json");
        var outPath = workspace.GetPath("checklist-unknown.json");

        var args = new[]
        {
            "--from-preview", previewPath,
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-no-mut.json");

        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("checklist-no-mut.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-no-provider.json");

        var launcherInvoked = false;
        TaskingTaskPacketChecklistCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        var outPath = workspace.GetPath("checklist-no-provider.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingTaskPacketChecklistCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingTaskPacketChecklistAnalyzer.cs");
        var artifactFile = Path.Combine(commandsDir, "TaskingTaskPacketChecklistArtifact.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile) || !File.Exists(artifactFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile, artifactFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        using var workspace = new ChecklistWorkspace();
        workspace.WriteQueueState("{\"schema_version\":\"1\",\"items\":[]}\n");
        workspace.WriteClarificationOpen(
            "## Current Open Blockers\n- 現時点で child issue cut を要する root blocker はない\n");
        workspace.WriteAutomationBindings("# bindings\n");

        var previewPath = workspace.GeneratePreview("preview-contract.json");
        var outPath = workspace.GetPath("checklist-contract.json");

        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-preview", previewPath,
                "--out", outPath,
                "--format", "text"
            },
            writer);

        Assert.Equal(0, exitCode);

        var checklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(checklist);
        Assert.False(checklist!.IsPublished);
        Assert.False(checklist.IsAutomationVisible);
        Assert.Equal("local_only", checklist.ChecklistStatus);
        Assert.Equal(TaskingTaskPacketChecklistConstants.LocalOnlyStatus, checklist.ChecklistStatus);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_automation_visible").ValueKind);
        Assert.Equal("local_only", doc.RootElement.GetProperty("checklist_status").GetString());
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new ChecklistWorkspace();
        var previewPath = workspace.GeneratePreview("preview-json-fmt.json");
        TaskingTaskPacketChecklistCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("checklist-json-fmt.json");
        using var writer = new StringWriter();
        var exitCode = TaskingTaskPacketChecklistCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-preview", previewPath,
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);

        var stdoutChecklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(writer.ToString());
        var fileChecklist =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutChecklist);
        Assert.NotNull(fileChecklist);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(fileChecklist, canonical),
            JsonSerializer.Serialize(stdoutChecklist, canonical));
    }

    private static string ComputeSha256HexLowercase(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            builder.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
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

    private sealed class ChecklistWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-task-packet-checklist-tests-")
            .FullName;

        public ChecklistWorkspace()
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

        public void WriteAutomationBindings(string content)
        {
            var path = Path.Combine(rootPath, "intents", "intent-cli", "automation");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "bindings.md"), content);
        }

        /// <summary>
        /// Generate a real G190 handoff, a real G191 task packet, and a real
        /// G192 preview derived from them in this workspace by invoking the
        /// actual upstream commands. Returns the absolute path of the resulting
        /// preview artifact.
        /// </summary>
        public string GeneratePreview(string filename)
        {
            var handoffPath = GetPath("__handoff_" + filename);
            using (var sink = new StringWriter())
            {
                var handoffExit = TaskingHandoffCommand.Execute(
                    Context,
                    new[] { "--out", handoffPath },
                    sink);
                if (handoffExit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G190 handoff for tests: exit={handoffExit}, stderr={sink}");
                }
            }

            var taskPacketPath = GetPath("__task_packet_" + filename);
            using (var sink = new StringWriter())
            {
                var taskPacketExit = TaskingTaskPacketCommand.Execute(
                    Context,
                    new[] { "--from-handoff", handoffPath, "--out", taskPacketPath },
                    sink);
                if (taskPacketExit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G191 task packet for tests: exit={taskPacketExit}, stderr={sink}");
                }
            }

            var previewPath = GetPath(filename);
            using (var sink = new StringWriter())
            {
                var previewExit = TaskingTaskPacketPreviewCommand.Execute(
                    Context,
                    new[] { "--from-task-packet", taskPacketPath, "--out", previewPath },
                    sink);
                if (previewExit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G192 preview for tests: exit={previewExit}, stderr={sink}");
                }
            }

            return previewPath;
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
