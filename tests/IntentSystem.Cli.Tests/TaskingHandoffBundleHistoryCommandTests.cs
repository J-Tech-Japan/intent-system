using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G200 — coverage for <c>intent-cli tasking handoff-bundle-history</c>. Class
/// name contains <c>Tasking</c>, <c>Handoff</c>, <c>Bundle</c>, and
/// <c>History</c> so reviewers can match the issue's recommended
/// <c>~TaskingHandoffBundle</c> filter.
/// </summary>
public sealed class TaskingHandoffBundleHistoryCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffBundleHistoryCommandTests()
    {
        originalLauncher = TaskingHandoffBundleHistoryCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffBundleHistoryCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenDirectoryWithThreeValidBundles_ListsAllThreeAsValid()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("three-valid");
        workspace.GenerateBundleInto(scanDir, "alpha-bundle");
        workspace.GenerateBundleInto(scanDir, "beta-bundle");
        workspace.GenerateBundleInto(scanDir, "gamma-bundle");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(3, result!.EntryCount);
        Assert.Equal(3, result.ValidCount);
        Assert.Equal(0, result.InvalidCount);
        Assert.Equal(0, result.MalformedCount);
        Assert.Equal(0, result.UnreadableCount);
        Assert.Equal(3, result.Entries.Count);

        // Sorted ordinal: alpha-bundle.json, beta-bundle.json, gamma-bundle.json
        Assert.Equal("alpha-bundle.json", result.Entries[0].FileName);
        Assert.Equal("beta-bundle.json", result.Entries[1].FileName);
        Assert.Equal("gamma-bundle.json", result.Entries[2].FileName);

        foreach (var entry in result.Entries)
        {
            Assert.Equal(
                TaskingHandoffBundleHistoryConstants.Classifications.ValidBundle,
                entry.Classification);
            Assert.Equal("intent-cli", entry.Domain);
            Assert.Equal("local_only", entry.BundleStatus);
            Assert.False(entry.IsPublished);
            Assert.False(entry.IsAutomationVisible);
            Assert.NotNull(entry.SourceTaskPacketPath);
            Assert.NotNull(entry.SourcePreviewPath);
            Assert.NotNull(entry.SourceChecklistPath);
            Assert.NotNull(entry.SourceHandoffPath);
            Assert.NotNull(entry.ChecklistReadyForHandoff);
            Assert.Empty(entry.Errors);
        }
    }

    [Fact]
    public void Execute_GivenEmptyDirectory_ReturnsZeroAndEmptyEntries()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("empty");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("no bundle entries found", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenEmptyDirectory_JsonHasZeroCounts()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("empty-json");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(0, result!.EntryCount);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Execute_GivenMissingDirectoryPath_ReturnsNonZero_DeterministicError()
    {
        using var workspace = new HistoryWorkspace();
        var missing = workspace.GetPath("does-not-exist-dir");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", missing },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains(missing, output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("not found", StringComparison.Ordinal)
            || output.Contains("does not exist", StringComparison.Ordinal),
            $"Expected 'not found' or 'does not exist' in error output but got: {output}");
    }

    [Fact]
    public void Execute_GivenPathIsFileNotDirectory_ReturnsNonZero()
    {
        using var workspace = new HistoryWorkspace();
        var filePath = workspace.GetPath("a-regular-file.json");
        File.WriteAllText(filePath, "{}");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", filePath },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("not a directory", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenDirectoryWithMalformedJson_ReportsAsMalformed()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("malformed-mix");
        workspace.GenerateBundleInto(scanDir, "real-bundle");
        File.WriteAllText(Path.Combine(scanDir, "garbage.json"), "{ not : valid json,,,");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(2, result!.EntryCount);
        Assert.Equal(1, result.ValidCount);
        Assert.Equal(1, result.MalformedCount);

        var malformed = result.Entries.Single(e =>
            e.Classification == TaskingHandoffBundleHistoryConstants.Classifications.Malformed);
        Assert.Equal("garbage.json", malformed.FileName);
        Assert.NotEmpty(malformed.Errors);
        Assert.Contains(
            malformed.Errors,
            err => err.Contains("parse failure", StringComparison.Ordinal));
        Assert.Null(malformed.Domain);
        Assert.Null(malformed.BundleStatus);
    }

    [Fact]
    public void Execute_GivenDirectoryWithInvalidBundle_ReportsAsInvalidBundle()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("invalid-bundle");

        // Build a valid bundle, then tamper status to violate the local_only contract.
        var bundlePath = workspace.GenerateBundleInto(scanDir, "tampered");
        var json = File.ReadAllText(bundlePath);
        var tampered = json.Replace(
            "\"bundle_status\": \"local_only\"",
            "\"bundle_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        File.WriteAllText(bundlePath, tampered);

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(1, result!.EntryCount);
        Assert.Equal(0, result.ValidCount);
        Assert.Equal(1, result.InvalidCount);

        var invalid = result.Entries.Single();
        Assert.Equal(
            TaskingHandoffBundleHistoryConstants.Classifications.InvalidBundle,
            invalid.Classification);
        Assert.NotEmpty(invalid.Errors);
        Assert.Contains(
            invalid.Errors,
            err => err.Contains("bundle_status", StringComparison.Ordinal)
                && err.Contains("local_only", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenDirectoryWithMixedClassifications_AllCountsCorrect()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("mixed");

        // 1 valid bundle (a-good.json — sorted first)
        workspace.GenerateBundleInto(scanDir, "a-good");

        // 1 invalid bundle (b-invalid.json — flipped is_published)
        var invalidPath = workspace.GenerateBundleInto(scanDir, "b-invalid");
        var invalidJson = File.ReadAllText(invalidPath);
        var invalidTampered = invalidJson.Replace(
            "\"is_published\": false",
            "\"is_published\": true",
            StringComparison.Ordinal);
        Assert.NotEqual(invalidJson, invalidTampered);
        File.WriteAllText(invalidPath, invalidTampered);

        // 1 malformed (c-malformed.json — invalid JSON)
        File.WriteAllText(Path.Combine(scanDir, "c-malformed.json"), "garbage{");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(3, result!.EntryCount);
        Assert.Equal(1, result.ValidCount);
        Assert.Equal(1, result.InvalidCount);
        Assert.Equal(1, result.MalformedCount);
        Assert.Equal(0, result.UnreadableCount);

        Assert.Equal(
            result.ValidCount + result.InvalidCount + result.MalformedCount + result.UnreadableCount,
            result.EntryCount);

        Assert.Equal("a-good.json", result.Entries[0].FileName);
        Assert.Equal("b-invalid.json", result.Entries[1].FileName);
        Assert.Equal("c-malformed.json", result.Entries[2].FileName);
    }

    [Fact]
    public void Execute_GivenJsonFormat_ResultRoundTripsStably()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("round-trip");
        workspace.GenerateBundleInto(scanDir, "rt-bundle");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);
        Assert.Equal(0, exitCode);

        var first = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(first);

        var reserialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(reserialized);
        Assert.NotNull(second);

        Assert.Equal(first!.FromDirectory, second!.FromDirectory);
        Assert.Equal(first.EntryCount, second.EntryCount);
        Assert.Equal(first.ValidCount, second.ValidCount);
        Assert.Equal(first.InvalidCount, second.InvalidCount);
        Assert.Equal(first.MalformedCount, second.MalformedCount);
        Assert.Equal(first.UnreadableCount, second.UnreadableCount);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
        Assert.Equal(first.Entries.Count, second.Entries.Count);

        for (var i = 0; i < first.Entries.Count; i++)
        {
            var a = first.Entries[i];
            var b = second.Entries[i];
            Assert.Equal(a.FileName, b.FileName);
            Assert.Equal(a.AbsolutePath, b.AbsolutePath);
            Assert.Equal(a.Classification, b.Classification);
            Assert.Equal(a.Domain, b.Domain);
            Assert.Equal(a.BundleStatus, b.BundleStatus);
            Assert.Equal(a.IsPublished, b.IsPublished);
            Assert.Equal(a.IsAutomationVisible, b.IsAutomationVisible);
            Assert.Equal(a.SourceTaskPacketPath, b.SourceTaskPacketPath);
            Assert.Equal(a.SourcePreviewPath, b.SourcePreviewPath);
            Assert.Equal(a.SourceChecklistPath, b.SourceChecklistPath);
            Assert.Equal(a.SourceHandoffPath, b.SourceHandoffPath);
            Assert.Equal(a.ChecklistReadyForHandoff, b.ChecklistReadyForHandoff);
            Assert.Equal(a.Errors, b.Errors);
        }
    }

    [Fact]
    public void Execute_GivenJsonFormat_PerEntryStatusErrorsArePresent()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("per-entry-errors");
        workspace.GenerateBundleInto(scanDir, "valid-one");
        File.WriteAllText(Path.Combine(scanDir, "broken.json"), "{ : ");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);
        Assert.Equal(0, exitCode);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);

        var valid = result!.Entries.Single(e =>
            e.Classification == TaskingHandoffBundleHistoryConstants.Classifications.ValidBundle);
        Assert.Empty(valid.Errors);

        var malformed = result.Entries.Single(e =>
            e.Classification == TaskingHandoffBundleHistoryConstants.Classifications.Malformed);
        Assert.NotEmpty(malformed.Errors);
    }

    [Fact]
    public void Execute_GivenTextFormat_StableOutputContainsKeyHeadingsAndCounts()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("text-fmt");
        workspace.GenerateBundleInto(scanDir, "valid-text");

        // invalid (flipped is_automation_visible)
        var invalidPath = workspace.GenerateBundleInto(scanDir, "invalid-text");
        var invalidJson = File.ReadAllText(invalidPath);
        var invalidTampered = invalidJson.Replace(
            "\"is_automation_visible\": false",
            "\"is_automation_visible\": true",
            StringComparison.Ordinal);
        File.WriteAllText(invalidPath, invalidTampered);

        // malformed
        File.WriteAllText(Path.Combine(scanDir, "zzz-malformed.json"), "garbage");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();

        Assert.Contains(TaskingHandoffBundleHistoryAnalyzer.HeaderPhrase, output, StringComparison.Ordinal);
        Assert.Contains("3 entries", output, StringComparison.Ordinal);
        Assert.Contains("[valid_bundle]", output, StringComparison.Ordinal);
        Assert.Contains("[invalid_bundle]", output, StringComparison.Ordinal);
        Assert.Contains("[malformed]", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverMutatesAnyFileInScannedDirectory()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("no-mutate");
        var bundlePath = workspace.GenerateBundleInto(scanDir, "stable-bundle");
        var sentinelPath = Path.Combine(scanDir, "sentinel.txt");
        var sentinelContent = Encoding.UTF8.GetBytes("do-not-touch-me");
        File.WriteAllBytes(sentinelPath, sentinelContent);

        var bundleBytesBefore = File.ReadAllBytes(bundlePath);
        var sentinelBytesBefore = File.ReadAllBytes(sentinelPath);
        var filesBefore = Directory.GetFiles(scanDir).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir },
            writer);

        Assert.Equal(0, exitCode);

        var bundleBytesAfter = File.ReadAllBytes(bundlePath);
        var sentinelBytesAfter = File.ReadAllBytes(sentinelPath);
        var filesAfter = Directory.GetFiles(scanDir).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        Assert.Equal(bundleBytesBefore, bundleBytesAfter);
        Assert.Equal(sentinelBytesBefore, sentinelBytesAfter);
        Assert.Equal(filesBefore, filesAfter);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("no-queue");
        workspace.GenerateBundleInto(scanDir, "queue-test");

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
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir },
            writer);

        Assert.Equal(0, exitCode);

        var queueAfter = File.ReadAllBytes(queueStatePath);
        var runsAfter = File.ReadAllBytes(runsLogPath);
        Assert.Equal(queueBefore, queueAfter);
        Assert.Equal(runsBefore, runsAfter);
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("no-provider");
        workspace.GenerateBundleInto(scanDir, "no-provider-bundle");

        var launcherInvoked = false;
        TaskingHandoffBundleHistoryCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryAnalyzer.cs");
        var resultFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryResult.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile) || !File.Exists(resultFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile, resultFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_NeverCreatesBundle_NoBundleArtifactWriteAttempt()
    {
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryAnalyzer.cs");
        var resultFile = Path.Combine(commandsDir, "TaskingHandoffBundleHistoryResult.cs");
        if (!File.Exists(commandFile) || !File.Exists(analyzerFile) || !File.Exists(resultFile))
        {
            return;
        }

        foreach (var path in new[] { commandFile, analyzerFile, resultFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("File.WriteAllText", text, StringComparison.Ordinal);
            Assert.DoesNotContain("File.WriteAllBytes", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new HistoryWorkspace();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-directory", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("unknown-arg");

        var args = new[] { "--from-directory", scanDir, "--bogus", "value" };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNonRecursiveScan_DoesNotPickUpJsonInSubdirectories()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("non-recursive");
        workspace.GenerateBundleInto(scanDir, "top");

        var subDir = Path.Combine(scanDir, "sub");
        Directory.CreateDirectory(subDir);
        workspace.GenerateBundleInto(subDir, "inner");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(1, result!.EntryCount);
        Assert.Equal(1, result.ValidCount);
        Assert.Equal("top.json", result.Entries.Single().FileName);
    }

    [Fact]
    public void Execute_GivenSortedEntriesByFileName_ListReturnsOrdinalSortedOrder()
    {
        using var workspace = new HistoryWorkspace();
        var scanDir = workspace.CreateScanDirectory("sort-order");
        workspace.GenerateBundleInto(scanDir, "c");
        workspace.GenerateBundleInto(scanDir, "a");
        workspace.GenerateBundleInto(scanDir, "b");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", scanDir, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleHistoryResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.Equal(3, result!.EntryCount);
        Assert.Equal("a.json", result.Entries[0].FileName);
        Assert.Equal("b.json", result.Entries[1].FileName);
        Assert.Equal("c.json", result.Entries[2].FileName);
    }

    [Fact]
    public void Execute_GivenBlankFromDirectoryValue_ReturnsNonZero()
    {
        using var workspace = new HistoryWorkspace();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleHistoryCommand.Execute(
            workspace.Context,
            new[] { "--from-directory", "   " },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-directory", writer.ToString(), StringComparison.Ordinal);
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

    private sealed class HistoryWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-bundle-history-tests-")
            .FullName;

        public HistoryWorkspace()
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

        public string CreateScanDirectory(string tag)
        {
            var path = Path.Combine(rootPath, $"scan-{tag}");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>
        /// Generate a fresh G190 → G191 → G192 → G193 → G194 chain and place
        /// the resulting bundle JSON in the supplied scan directory under
        /// <c>{tag}.json</c>.
        /// </summary>
        public string GenerateBundleInto(string scanDir, string tag)
        {
            var (previewPath, checklistPath) = GeneratePreviewAndChecklist(tag);
            var bundlePath = Path.Combine(scanDir, $"{tag}.json");

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
