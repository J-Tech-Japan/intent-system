using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G194 — coverage for <c>intent-cli tasking handoff-bundle</c>. Class name
/// contains <c>Tasking</c>, <c>Handoff</c>, and <c>Bundle</c> so reviewers can
/// match the issue's recommended <c>~Bundle|~Handoff|~Tasking</c> filter.
/// </summary>
public sealed class TaskingHandoffBundleCommandTests : IDisposable
{
    private readonly Func<DateTimeOffset> originalTimestampFactory;
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffBundleCommandTests()
    {
        originalTimestampFactory = TaskingHandoffBundleCommand.TimestampFactory;
        originalLauncher = TaskingHandoffBundleCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffBundleCommand.TimestampFactory = originalTimestampFactory;
        TaskingHandoffBundleCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenMatchedPreviewAndChecklist_WritesUnpublishedBundleArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("matched");
        var outPath = workspace.GetPath("bundle-matched.json");

        TaskingHandoffBundleCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, 789, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(outPath));

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        var root = doc.RootElement;
        Assert.False(root.GetProperty("is_published").GetBoolean());
        Assert.False(root.GetProperty("is_automation_visible").GetBoolean());
        Assert.Equal("local_only", root.GetProperty("bundle_status").GetString());
        Assert.Equal("2026-04-29T12:34:56.789Z", root.GetProperty("generated_at_utc").GetString());

        var summary = root.GetProperty("summary_line").GetString();
        Assert.NotNull(summary);
        Assert.Contains("UNPUBLISHED", summary, StringComparison.Ordinal);
        Assert.Contains("not automation-visible", summary, StringComparison.Ordinal);
        Assert.Contains("ready_for_handoff=true", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMatchedInputs_BundleJsonRoundTripsStably()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("rt");
        var outPath = workspace.GetPath("bundle-rt.json");

        TaskingHandoffBundleCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 1, 2, 3, TimeSpan.Zero);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var roundTripped =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(roundTripped);
        Assert.Equal("intent-cli", roundTripped!.Domain);
        Assert.False(roundTripped.IsPublished);
        Assert.False(roundTripped.IsAutomationVisible);
        Assert.Equal(TaskingHandoffBundleConstants.LocalOnlyStatus, roundTripped.BundleStatus);
        Assert.Equal("local_only", roundTripped.BundleStatus);
        Assert.Equal("2026-04-29T01:02:03.000Z", roundTripped.GeneratedAtUtc);
        Assert.Equal(Path.GetFullPath(outPath), roundTripped.ArtifactPath);
        Assert.Equal(previewPath, roundTripped.SourcePreviewPath);
        Assert.Equal(checklistPath, roundTripped.SourceChecklistPath);
        Assert.Contains("UNPUBLISHED", roundTripped.SummaryLine, StringComparison.Ordinal);
        Assert.True(roundTripped.ChecklistReadyForHandoff);
    }

    [Fact]
    public void Execute_GivenMatchedInputs_RecordsAllSourceReferencesAndSha256s()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("sha");
        var outPath = workspace.GetPath("bundle-sha.json");

        var expectedPreviewBytes = File.ReadAllBytes(previewPath);
        var expectedChecklistBytes = File.ReadAllBytes(checklistPath);
        var expectedPreviewSha = ComputeSha256HexLowercase(expectedPreviewBytes);
        var expectedChecklistSha = ComputeSha256HexLowercase(expectedChecklistBytes);

        var sourcePreview =
            JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(expectedPreviewBytes);
        Assert.NotNull(sourcePreview);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(bundle);
        Assert.Equal(previewPath, bundle!.SourcePreviewPath);
        Assert.Equal(expectedPreviewSha, bundle.SourcePreviewSha256);
        Assert.Equal(checklistPath, bundle.SourceChecklistPath);
        Assert.Equal(expectedChecklistSha, bundle.SourceChecklistSha256);

        Assert.Equal(sourcePreview!.SourceTaskPacketPath, bundle.SourceTaskPacketPath);
        Assert.Equal(sourcePreview.SourceTaskPacketSha256, bundle.SourceTaskPacketSha256);
        Assert.Equal(sourcePreview.SourceHandoffPath, bundle.SourceHandoffPath);
        Assert.Equal(sourcePreview.SourceHandoffSha256, bundle.SourceHandoffSha256);

        Assert.False(string.IsNullOrWhiteSpace(bundle.SourcePreviewSha256));
        Assert.False(string.IsNullOrWhiteSpace(bundle.SourceChecklistSha256));
        Assert.False(string.IsNullOrWhiteSpace(bundle.SourceTaskPacketSha256));
        Assert.False(string.IsNullOrWhiteSpace(bundle.SourceHandoffSha256));
    }

    [Fact]
    public void Execute_GivenMatchedInputs_RecordsPassedAndFailedCheckIds()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("ids");
        var outPath = workspace.GetPath("bundle-ids.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(bundle);

        var expectedIds = TaskingTaskPacketChecklistConstants.CheckIds.All;
        Assert.Equal(10, expectedIds.Count);
        Assert.Equal(expectedIds.Count, bundle!.ChecklistPassedCheckIds.Count);
        Assert.Empty(bundle.ChecklistFailedCheckIds);

        foreach (var expected in expectedIds)
        {
            Assert.Contains(expected, bundle.ChecklistPassedCheckIds);
        }
    }

    [Fact]
    public void Execute_GivenMatchedInputs_ChecklistReadyForHandoffPropagated()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("ready");
        var outPath = workspace.GetPath("bundle-ready.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(bundle);
        Assert.True(bundle!.ChecklistReadyForHandoff);
    }

    [Fact]
    public void Execute_GivenChecklistWithFailedChecks_PropagatesFailedCheckIdsAndReadyFalse()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) =
            workspace.GeneratePreviewAndChecklistWithEmptyRecommendedAction("failed");
        var outPath = workspace.GetPath("bundle-failed.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);

        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(bundle);
        Assert.False(bundle!.ChecklistReadyForHandoff);
        Assert.Contains(
            TaskingTaskPacketChecklistConstants.CheckIds.RecommendedWorkerActionNonEmpty,
            bundle.ChecklistFailedCheckIds);
        Assert.Contains("ready_for_handoff=false", bundle.SummaryLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPreviewChecklistMismatch_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewA, _) = workspace.GeneratePreviewAndChecklist("pairA");
        var (_, checklistB) = workspace.GeneratePreviewAndChecklist("pairB");
        var outPath = workspace.GetPath("bundle-mismatch.json");

        var previewABytes = File.ReadAllBytes(previewA);
        var previewASha = ComputeSha256HexLowercase(previewABytes);

        var checklistBJson = File.ReadAllText(checklistB);
        var checklistBObj =
            JsonSerializer.Deserialize<TaskingTaskPacketChecklistArtifact>(checklistBJson);
        Assert.NotNull(checklistBObj);
        Assert.NotEqual(previewASha, checklistBObj!.SourcePreviewSha256);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewA, "--from-checklist", checklistB, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));

        var output = writer.ToString();
        Assert.Contains(checklistBObj.SourcePreviewSha256, output, StringComparison.Ordinal);
        Assert.Contains(previewASha, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPreviewFile_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (_, checklistPath) = workspace.GeneratePreviewAndChecklist("missing-preview");
        var missing = workspace.GetPath("does-not-exist-preview.json");
        var outPath = workspace.GetPath("bundle-missing-preview.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", missing, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingChecklistFile_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, _) = workspace.GeneratePreviewAndChecklist("missing-checklist");
        var missing = workspace.GetPath("does-not-exist-checklist.json");
        var outPath = workspace.GetPath("bundle-missing-checklist.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", missing, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedPreviewJson_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (_, checklistPath) = workspace.GeneratePreviewAndChecklist("malformed-prev");
        var malformed = workspace.GetPath("malformed-preview.json");
        File.WriteAllText(malformed, "{ this is not : valid json,");
        var outPath = workspace.GetPath("bundle-malformed-prev.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", malformed, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMalformedChecklistJson_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, _) = workspace.GeneratePreviewAndChecklist("malformed-chk");
        var malformed = workspace.GetPath("malformed-checklist.json");
        File.WriteAllText(malformed, "{ this is not : valid json,");
        var outPath = workspace.GetPath("bundle-malformed-chk.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", malformed, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenPreviewWithWrongStatus_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("wrong-prev");
        var json = File.ReadAllText(previewPath);
        var tampered = json.Replace(
            "\"preview_status\": \"local_only\"",
            "\"preview_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("preview-tampered-status.json");
        File.WriteAllText(tamperedPath, tampered);
        var outPath = workspace.GetPath("bundle-wrong-prev-status.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", tamperedPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenChecklistWithWrongStatus_ReturnsNonZero_NoOutputArtifact()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("wrong-chk");
        var json = File.ReadAllText(checklistPath);
        var tampered = json.Replace(
            "\"checklist_status\": \"local_only\"",
            "\"checklist_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("checklist-tampered-status.json");
        File.WriteAllText(tamperedPath, tampered);
        var outPath = workspace.GetPath("bundle-wrong-chk-status.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", tamperedPath, "--out", outPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(outPath));
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("missing-flag");

        // Drop --out entirely.
        var args = new[] { "--from-preview", previewPath, "--from-checklist", checklistPath };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("unknown");
        var outPath = workspace.GetPath("bundle-unknown.json");

        var args = new[]
        {
            "--from-preview", previewPath,
            "--from-checklist", checklistPath,
            "--out", outPath,
            "--bogus", "value"
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("no-mut");

        var queueStatePath = workspace.Context.GetQueueStatePath();
        var runsLogPath = workspace.Context.GetRunLogPath();

        var queueBytes = Encoding.UTF8.GetBytes("{\"schema_version\":\"1\",\"items\":[]}\n");
        var runsBytes = Encoding.UTF8.GetBytes("{\"event\":\"sentinel\",\"ts\":\"2026-04-29T00:00:00Z\"}\n");
        Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
        File.WriteAllBytes(queueStatePath, queueBytes);
        File.WriteAllBytes(runsLogPath, runsBytes);

        var queueBefore = File.ReadAllBytes(queueStatePath);
        var runsBefore = File.ReadAllBytes(runsLogPath);

        var outPath = workspace.GetPath("bundle-no-mut.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
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
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("no-provider");

        var launcherInvoked = false;
        TaskingHandoffBundleCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        var outPath = workspace.GetPath("bundle-no-provider.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[] { "--from-preview", previewPath, "--from-checklist", checklistPath, "--out", outPath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleAnalyzer.cs");
        var artifactFile = Path.Combine(commandsDir, "TaskingHandoffBundleArtifact.cs");
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
        using var workspace = new BundleWorkspace();
        workspace.WriteQueueState("{\"schema_version\":\"1\",\"items\":[]}\n");
        workspace.WriteClarificationOpen(
            "## Current Open Blockers\n- 現時点で child issue cut を要する root blocker はない\n");
        workspace.WriteAutomationBindings("# bindings\n");

        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("contract");
        var outPath = workspace.GetPath("bundle-contract.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-preview", previewPath,
                "--from-checklist", checklistPath,
                "--out", outPath,
                "--format", "text"
            },
            writer);

        Assert.Equal(0, exitCode);

        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(bundle);
        Assert.False(bundle!.IsPublished);
        Assert.False(bundle.IsAutomationVisible);
        Assert.Equal("local_only", bundle.BundleStatus);
        Assert.Equal(TaskingHandoffBundleConstants.LocalOnlyStatus, bundle.BundleStatus);

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_automation_visible").ValueKind);
        Assert.Equal("local_only", doc.RootElement.GetProperty("bundle_status").GetString());
    }

    [Fact]
    public void Execute_GivenJsonFormat_StdoutContainsArtifactJson()
    {
        using var workspace = new BundleWorkspace();
        var (previewPath, checklistPath) = workspace.GeneratePreviewAndChecklist("json-fmt");
        TaskingHandoffBundleCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 5, 0, 0, TimeSpan.Zero);

        var outPath = workspace.GetPath("bundle-json-fmt.json");
        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-preview", previewPath,
                "--from-checklist", checklistPath,
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(0, exitCode);

        var stdoutBundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(writer.ToString());
        var fileBundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(outPath));
        Assert.NotNull(stdoutBundle);
        Assert.NotNull(fileBundle);

        var canonical = new JsonSerializerOptions { WriteIndented = false };
        Assert.Equal(
            JsonSerializer.Serialize(fileBundle, canonical),
            JsonSerializer.Serialize(stdoutBundle, canonical));
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

    private sealed class BundleWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-bundle-tests-")
            .FullName;

        public BundleWorkspace()
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
        /// Generate a real G190 handoff, G191 task packet, G192 preview, and
        /// a real G193 checklist derived from that preview. Returns the
        /// (preview path, checklist path) pair.
        /// </summary>
        public (string previewPath, string checklistPath) GeneratePreviewAndChecklist(string tag)
        {
            var previewPath = GeneratePreview(tag);
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

            return (previewPath, checklistPath);
        }

        /// <summary>
        /// Like <see cref="GeneratePreviewAndChecklist"/>, but mutates the
        /// preview to set <c>recommended_worker_action</c> to whitespace before
        /// running the checklist. The resulting checklist will fail
        /// <c>recommended_worker_action_non_empty</c>.
        /// </summary>
        public (string previewPath, string checklistPath)
            GeneratePreviewAndChecklistWithEmptyRecommendedAction(string tag)
        {
            var basePreviewPath = GeneratePreview($"{tag}-base");
            var preview = JsonSerializer.Deserialize<TaskingTaskPacketPreviewArtifact>(
                File.ReadAllText(basePreviewPath));
            if (preview is null)
            {
                throw new InvalidOperationException("Generated preview deserialized to null.");
            }

            var mutated = preview with { RecommendedWorkerAction = "   " };
            var mutatedPreviewPath = GetPath($"preview-{tag}-empty-action.json");
            File.WriteAllText(mutatedPreviewPath, JsonSerializer.Serialize(mutated));

            var checklistPath = GetPath($"checklist-{tag}-empty-action.json");
            using var sink = new StringWriter();
            var exit = TaskingTaskPacketChecklistCommand.Execute(
                Context,
                new[] { "--from-preview", mutatedPreviewPath, "--out", checklistPath },
                sink);
            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to generate G193 checklist (empty action) for tests: exit={exit}, stderr={sink}");
            }

            return (mutatedPreviewPath, checklistPath);
        }

        private string GeneratePreview(string tag)
        {
            var handoffPath = GetPath($"__handoff_{tag}.json");
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

            var taskPacketPath = GetPath($"__task_packet_{tag}.json");
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

            var previewPath = GetPath($"preview-{tag}.json");
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
