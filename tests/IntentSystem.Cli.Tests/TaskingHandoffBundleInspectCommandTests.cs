using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G195 — coverage for <c>intent-cli tasking handoff-bundle-inspect</c>. Class
/// name contains <c>Tasking</c>, <c>Handoff</c>, <c>Bundle</c>, and
/// <c>Inspect</c> so reviewers can match the issue's recommended
/// <c>~Inspect|~Bundle</c> filter.
/// </summary>
public sealed class TaskingHandoffBundleInspectCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffBundleInspectCommandTests()
    {
        originalLauncher = TaskingHandoffBundleInspectCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffBundleInspectCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenValidBundle_RendersConciseTextSummary()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("text");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("UNPUBLISHED, local-only, not automation-visible", output, StringComparison.Ordinal);
        Assert.Contains("Domain: intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("Ready for handoff: True", output, StringComparison.Ordinal);
        Assert.Contains("Source references:", output, StringComparison.Ordinal);
        Assert.Contains("Checklist:", output, StringComparison.Ordinal);
        Assert.Contains("Recommended worker action:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBundle_TextSummaryListsAllSourceReferences()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("srcs");
        var bundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(bundlePath));
        Assert.NotNull(bundle);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains(bundle!.SourcePreviewPath, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceChecklistPath, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceTaskPacketPath, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceHandoffPath, output, StringComparison.Ordinal);

        Assert.Contains(bundle.SourcePreviewSha256, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceChecklistSha256, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceTaskPacketSha256, output, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceHandoffSha256, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBundle_TextSummaryListsPassedAndFailedCheckCounts()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("counts");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains("Passed (10):", output, StringComparison.Ordinal);
        foreach (var id in TaskingTaskPacketChecklistConstants.CheckIds.All)
        {
            Assert.Contains(id, output, StringComparison.Ordinal);
        }

        Assert.Contains("Failed (0): (none)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_EmitsRoundTrippableSummary()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("json-rt");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);

        var summary =
            JsonSerializer.Deserialize<TaskingHandoffBundleInspectSummary>(writer.ToString());
        Assert.NotNull(summary);
        Assert.Equal("intent-cli", summary!.Domain);
        Assert.Equal("local_only", summary.BundleStatus);
        Assert.False(summary.IsPublished);
        Assert.False(summary.IsAutomationVisible);
        Assert.True(summary.ChecklistReadyForHandoff);
        Assert.Equal(10, summary.PassedCheckCount);
        Assert.Equal(0, summary.FailedCheckCount);
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            summary.SummaryLine,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_FieldsMatchSourceBundle()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("json-match");
        var sourceBundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(bundlePath));
        Assert.NotNull(sourceBundle);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var summary =
            JsonSerializer.Deserialize<TaskingHandoffBundleInspectSummary>(writer.ToString());
        Assert.NotNull(summary);

        Assert.Equal("local_only", summary!.BundleStatus);
        Assert.False(summary.IsPublished);
        Assert.False(summary.IsAutomationVisible);
        Assert.Equal(10, summary.PassedCheckCount);
        Assert.Equal(0, summary.FailedCheckCount);

        Assert.Equal(sourceBundle!.SourcePreviewPath, summary.SourcePreviewPath);
        Assert.Equal(sourceBundle.SourcePreviewSha256, summary.SourcePreviewSha256);
        Assert.Equal(sourceBundle.SourceChecklistPath, summary.SourceChecklistPath);
        Assert.Equal(sourceBundle.SourceChecklistSha256, summary.SourceChecklistSha256);
        Assert.Equal(sourceBundle.SourceTaskPacketPath, summary.SourceTaskPacketPath);
        Assert.Equal(sourceBundle.SourceTaskPacketSha256, summary.SourceTaskPacketSha256);
        Assert.Equal(sourceBundle.SourceHandoffPath, summary.SourceHandoffPath);
        Assert.Equal(sourceBundle.SourceHandoffSha256, summary.SourceHandoffSha256);
        Assert.Equal(sourceBundle.RecommendedWorkerAction, summary.RecommendedWorkerAction);
    }

    [Fact]
    public void Execute_GivenBundleWithFailedChecks_PropagatesCounts()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundleWithFailedCheck("failed");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains("Ready for handoff: False", output, StringComparison.Ordinal);
        Assert.Contains("Failed (1):", output, StringComparison.Ordinal);
        Assert.Contains(
            TaskingTaskPacketChecklistConstants.CheckIds.RecommendedWorkerActionNonEmpty,
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingBundleFile_ReturnsNonZero()
    {
        using var workspace = new InspectWorkspace();
        var missing = workspace.GetPath("does-not-exist-bundle.json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", missing },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-bundle", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMalformedBundleJson_ReturnsNonZero()
    {
        using var workspace = new InspectWorkspace();
        var malformed = workspace.GetPath("malformed-bundle.json");
        File.WriteAllText(malformed, "{ this is not : valid json,");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", malformed },
            writer);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Execute_GivenBundleWithWrongStatus_ReturnsNonZero()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("wrong-status");
        var json = File.ReadAllText(bundlePath);
        var tampered = json.Replace(
            "\"bundle_status\": \"local_only\"",
            "\"bundle_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("bundle-tampered-status.json");
        File.WriteAllText(tamperedPath, tampered);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", tamperedPath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("local_only", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new InspectWorkspace();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-bundle", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("unknown");

        var args = new[] { "--from-bundle", bundlePath, "--bogus", "value" };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("no-mut");

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
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);

        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverWritesAnyArtifactFile()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("no-artifact");

        var bundleBytesBefore = File.ReadAllBytes(bundlePath);
        var snapshotBefore = workspace.SnapshotAllFiles();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);

        var bundleBytesAfter = File.ReadAllBytes(bundlePath);
        Assert.Equal(bundleBytesBefore, bundleBytesAfter);

        var snapshotAfter = workspace.SnapshotAllFiles();
        Assert.Equal(snapshotBefore.Count, snapshotAfter.Count);
        foreach (var (path, contentBefore) in snapshotBefore)
        {
            Assert.True(snapshotAfter.ContainsKey(path), $"File disappeared: {path}");
            Assert.Equal(contentBefore, snapshotAfter[path]);
        }
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("no-provider");

        var launcherInvoked = false;
        TaskingHandoffBundleInspectCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleInspectCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleInspectCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleInspectAnalyzer.cs");
        var summaryFile = Path.Combine(commandsDir, "TaskingHandoffBundleInspectSummary.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile) || !File.Exists(summaryFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile, summaryFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_AlwaysReportsLiteralFalseAndLocalOnlyFromSourceBundle()
    {
        using var workspace = new InspectWorkspace();
        var bundlePath = workspace.GenerateBundle("contract");

        // text format
        using (var writer = new StringWriter())
        {
            var exitCode = TaskingHandoffBundleInspectCommand.Execute(
                workspace.Context,
                new[] { "--from-bundle", bundlePath, "--format", "text" },
                writer);
            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Bundle status: local_only", output, StringComparison.Ordinal);
            Assert.Contains("Is published: False", output, StringComparison.Ordinal);
            Assert.Contains("Is automation visible: False", output, StringComparison.Ordinal);
        }

        // json format
        using (var writer = new StringWriter())
        {
            var exitCode = TaskingHandoffBundleInspectCommand.Execute(
                workspace.Context,
                new[] { "--from-bundle", bundlePath, "--format", "json" },
                writer);
            Assert.Equal(0, exitCode);

            var summary =
                JsonSerializer.Deserialize<TaskingHandoffBundleInspectSummary>(writer.ToString());
            Assert.NotNull(summary);
            Assert.Equal("local_only", summary!.BundleStatus);
            Assert.False(summary.IsPublished);
            Assert.False(summary.IsAutomationVisible);

            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("is_published").ValueKind);
            Assert.Equal(
                JsonValueKind.False,
                doc.RootElement.GetProperty("is_automation_visible").ValueKind);
            Assert.Equal("local_only", doc.RootElement.GetProperty("bundle_status").GetString());
        }

        // Confirm the SOURCE bundle on disk really had these literal contract
        // fields — the inspect command propagates without inversion.
        var sourceBundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(bundlePath));
        Assert.NotNull(sourceBundle);
        Assert.Equal("local_only", sourceBundle!.BundleStatus);
        Assert.False(sourceBundle.IsPublished);
        Assert.False(sourceBundle.IsAutomationVisible);
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

    private sealed class InspectWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-bundle-inspect-tests-")
            .FullName;

        public InspectWorkspace()
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

        public IReadOnlyDictionary<string, byte[]> SnapshotAllFiles()
        {
            var map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                map[path] = File.ReadAllBytes(path);
            }

            return map;
        }

        /// <summary>
        /// Generate the full G190 → G191 → G192 → G193 → G194 chain and return
        /// the bundle path. The bundle's checks all pass.
        /// </summary>
        public string GenerateBundle(string tag)
        {
            var (previewPath, checklistPath) = GeneratePreviewAndChecklist(tag);
            return BuildBundle(tag, previewPath, checklistPath);
        }

        /// <summary>
        /// Generate a chain whose checklist contains a failed check (the
        /// preview's recommended_worker_action is whitespace), then build a
        /// bundle on top.
        /// </summary>
        public string GenerateBundleWithFailedCheck(string tag)
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
            using (var sink = new StringWriter())
            {
                var exit = TaskingTaskPacketChecklistCommand.Execute(
                    Context,
                    new[] { "--from-preview", mutatedPreviewPath, "--out", checklistPath },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G193 checklist (empty action): exit={exit}, stderr={sink}");
                }
            }

            return BuildBundle(tag, mutatedPreviewPath, checklistPath);
        }

        private string BuildBundle(string tag, string previewPath, string checklistPath)
        {
            var bundlePath = GetPath($"bundle-{tag}.json");
            using var sink = new StringWriter();
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

            return bundlePath;
        }

        private (string previewPath, string checklistPath) GeneratePreviewAndChecklist(string tag)
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
                    $"Failed to generate G193 checklist: exit={exit}, stderr={sink}");
            }

            return (previewPath, checklistPath);
        }

        private string GeneratePreview(string tag)
        {
            var handoffPath = GetPath($"__handoff_{tag}.json");
            using (var sink = new StringWriter())
            {
                var exit = TaskingHandoffCommand.Execute(
                    Context,
                    new[] { "--out", handoffPath },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G190 handoff: exit={exit}, stderr={sink}");
                }
            }

            var taskPacketPath = GetPath($"__task_packet_{tag}.json");
            using (var sink = new StringWriter())
            {
                var exit = TaskingTaskPacketCommand.Execute(
                    Context,
                    new[] { "--from-handoff", handoffPath, "--out", taskPacketPath },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G191 task packet: exit={exit}, stderr={sink}");
                }
            }

            var previewPath = GetPath($"preview-{tag}.json");
            using (var sink = new StringWriter())
            {
                var exit = TaskingTaskPacketPreviewCommand.Execute(
                    Context,
                    new[] { "--from-task-packet", taskPacketPath, "--out", previewPath },
                    sink);
                if (exit != 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to generate G192 preview: exit={exit}, stderr={sink}");
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
