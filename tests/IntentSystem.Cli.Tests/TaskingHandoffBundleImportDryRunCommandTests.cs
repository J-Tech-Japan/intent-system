using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G198 — coverage for <c>intent-cli tasking handoff-bundle-import-dry-run</c>.
/// Class name contains <c>Tasking</c>, <c>Handoff</c>, <c>Bundle</c>,
/// <c>Import</c>, and <c>DryRun</c> so reviewers can match the issue's
/// recommended <c>~TaskingHandoffBundle</c> filter.
/// </summary>
public sealed class TaskingHandoffBundleImportDryRunCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffBundleImportDryRunCommandTests()
    {
        originalLauncher = TaskingHandoffBundleImportDryRunCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffBundleImportDryRunCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenValidBundle_ReturnsZeroAndEmitsRestorePlan()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("happy");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            output,
            StringComparison.Ordinal);
        Assert.Contains("status=ok", output, StringComparison.Ordinal);

        Assert.Contains($"role: {TaskingHandoffBundleImportDryRunConstants.Roles.TaskPacket}", output, StringComparison.Ordinal);
        Assert.Contains($"role: {TaskingHandoffBundleImportDryRunConstants.Roles.Preview}", output, StringComparison.Ordinal);
        Assert.Contains($"role: {TaskingHandoffBundleImportDryRunConstants.Roles.Checklist}", output, StringComparison.Ordinal);
        Assert.Contains($"role: {TaskingHandoffBundleImportDryRunConstants.Roles.Handoff}", output, StringComparison.Ordinal);

        Assert.Contains(
            $"current_status: {TaskingHandoffBundleImportDryRunConstants.CurrentStatuses.AlreadyPresentAndMatching}",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            $"dry_run_action: {TaskingHandoffBundleImportDryRunConstants.Actions.NoOpAlreadyMatches}",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBundle_RestorePlanContainsAllFourRolesInOrder()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("order");

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        var plan = result!.RestorePlan;
        Assert.Equal(4, plan.Count);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Roles.TaskPacket, plan[0].Role);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Roles.Preview, plan[1].Role);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Roles.Checklist, plan[2].Role);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Roles.Handoff, plan[3].Role);
    }

    [Fact]
    public void Execute_GivenValidBundle_DisclaimerIsPresentInTextAndJson()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("disclaimer");

        using (var textWriter = new StringWriter())
        {
            var textExit = TaskingHandoffBundleImportDryRunCommand.Execute(
                workspace.Context,
                new[] { "--from-bundle", bundlePath, "--format", "text" },
                textWriter);
            Assert.Equal(0, textExit);
            Assert.Contains(
                "DRY-RUN ONLY: this is a restore plan preview",
                textWriter.ToString(),
                StringComparison.Ordinal);
        }

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Contains(
            "DRY-RUN ONLY: this is a restore plan preview",
            result!.RestorePlanDisclaimer,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBundle_JsonResultRoundTripsStably()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("rt");

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);

        var first = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(first);
        var reserialized = JsonSerializer.Serialize(first);
        var second = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(reserialized);
        Assert.NotNull(second);

        Assert.Equal(first!.BundlePath, second!.BundlePath);
        Assert.Equal(first.BundleSha256, second.BundleSha256);
        Assert.Equal(first.Domain, second.Domain);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Errors, second.Errors);
        Assert.Equal(first.VerifySummary.Valid, second.VerifySummary.Valid);
        Assert.Equal(first.VerifySummary.FailedCheckIds, second.VerifySummary.FailedCheckIds);
        Assert.Equal(first.RestorePlan.Count, second.RestorePlan.Count);
        for (var i = 0; i < first.RestorePlan.Count; i++)
        {
            Assert.Equal(first.RestorePlan[i].Role, second.RestorePlan[i].Role);
            Assert.Equal(first.RestorePlan[i].RecordedPath, second.RestorePlan[i].RecordedPath);
            Assert.Equal(first.RestorePlan[i].RecordedSha256, second.RestorePlan[i].RecordedSha256);
            Assert.Equal(first.RestorePlan[i].CurrentStatus, second.RestorePlan[i].CurrentStatus);
            Assert.Equal(first.RestorePlan[i].DryRunAction, second.RestorePlan[i].DryRunAction);
        }

        Assert.Equal(first.RestorePlanDisclaimer, second.RestorePlanDisclaimer);
        Assert.Equal(first.SummaryLine, second.SummaryLine);
    }

    [Fact]
    public void Execute_GivenJsonFormat_IncludesStatusAndArtifactPathsForAutomation()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("automation");
        var bundle = ReadBundle(bundlePath);

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal(
            TaskingHandoffBundleImportDryRunConstants.Statuses.Ok,
            root.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
        Assert.Equal(bundlePath, root.GetProperty("bundle_path").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("bundle_sha256").GetString()));
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());

        var plan = root.GetProperty("restore_plan");
        Assert.Equal(4, plan.GetArrayLength());

        var paths = new List<string>();
        foreach (var entry in plan.EnumerateArray())
        {
            paths.Add(entry.GetProperty("recorded_path").GetString() ?? string.Empty);
        }

        Assert.Contains(bundle.SourceTaskPacketPath, paths);
        Assert.Contains(bundle.SourcePreviewPath, paths);
        Assert.Contains(bundle.SourceChecklistPath, paths);
        Assert.Contains(bundle.SourceHandoffPath, paths);
    }

    [Fact]
    public void Execute_GivenReferencedTaskPacketDeleted_FailsVerifyAndExitsNonZero()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("del-tp");
        var bundle = ReadBundle(bundlePath);
        File.Delete(bundle.SourceTaskPacketPath);

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(1, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Statuses.VerifyFailed, result!.Status);
        Assert.Empty(result.RestorePlan);
        Assert.False(result.VerifySummary.Valid);

        var failedIds = result.VerifySummary.FailedCheckIds;
        Assert.Contains(
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists,
            failedIds);
        Assert.Contains(
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches,
            failedIds);

        var anyHashErr = result.Errors.Any(e =>
            e.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, StringComparison.Ordinal)
            || e.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, StringComparison.Ordinal));
        Assert.True(anyHashErr, "Expected errors to mention task_packet hash/exists check id.");

        Assert.DoesNotContain("\"role\":", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReferencedPreviewEdited_FailsVerifyAndExitsNonZero()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("edit-pv");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourcePreviewPath);

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(1, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Equal(TaskingHandoffBundleImportDryRunConstants.Statuses.VerifyFailed, result!.Status);
        Assert.Empty(result.RestorePlan);
        Assert.False(result.VerifySummary.Valid);
        Assert.Contains(
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches,
            result.VerifySummary.FailedCheckIds);
    }

    [Fact]
    public void Execute_GivenMissingBundleFile_ReturnsNonZero_StatusMissingBundle()
    {
        using var workspace = new DryRunWorkspace();
        var missing = "/tmp/this-bundle-does-not-exist-g198.json";

        var (exit, output) = RunJson(workspace, missing);
        Assert.Equal(1, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Equal(
            TaskingHandoffBundleImportDryRunConstants.Statuses.MissingBundle,
            result!.Status);
        Assert.Empty(result.RestorePlan);
        Assert.Contains(result.Errors, e => e.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_GivenMalformedBundleJson_ReturnsNonZero_StatusMalformedBundle()
    {
        using var workspace = new DryRunWorkspace();
        var malformed = workspace.GetPath("garbage-bundle.json");
        File.WriteAllText(malformed, "{ this is not : valid json,");

        var (exit, output) = RunJson(workspace, malformed);
        Assert.Equal(1, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Equal(
            TaskingHandoffBundleImportDryRunConstants.Statuses.MalformedBundle,
            result!.Status);
        Assert.Empty(result.RestorePlan);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e =>
            e.Contains("parse", StringComparison.OrdinalIgnoreCase)
            || e.Contains("JSON", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_GivenBundleWithWrongStatus_FailsVerify_NoRestorePlan()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("wrong-status-base");
        var json = File.ReadAllText(bundlePath);
        var tampered = json.Replace(
            "\"bundle_status\": \"local_only\"",
            "\"bundle_status\": \"published\"",
            StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);
        var tamperedPath = workspace.GetPath("bundle-wrong-status.json");
        File.WriteAllText(tamperedPath, tampered);

        var (exit, output) = RunJson(workspace, tamperedPath);
        Assert.Equal(1, exit);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);
        Assert.Equal(
            TaskingHandoffBundleImportDryRunConstants.Statuses.VerifyFailed,
            result!.Status);
        Assert.Empty(result.RestorePlan);
        Assert.Contains(
            TaskingHandoffBundleVerifyConstants.CheckIds.BundleStatusLocalOnly,
            result.VerifySummary.FailedCheckIds);
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new DryRunWorkspace();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-bundle", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("unknown");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--bogus", "value" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new DryRunWorkspace();
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
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
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
    public void Execute_NeverWritesAnyArtifactFile_NeverOverwritesAnything()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("no-write");

        var snapshotBefore = workspace.SnapshotAllFiles();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);

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
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("no-provider");

        var launcherInvoked = false;
        TaskingHandoffBundleImportDryRunCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunAnalyzer.cs");
        var resultFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunResult.cs");
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
    public void Execute_NeverCreatesBranchOrWorktree_NoGitInvocationInNewSurface()
    {
        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunAnalyzer.cs");
        var resultFile = Path.Combine(commandsDir, "TaskingHandoffBundleImportDryRunResult.cs");

        Assert.True(File.Exists(commandFile), $"Missing command file at {commandFile}");
        Assert.True(File.Exists(analyzerFile), $"Missing analyzer file at {analyzerFile}");
        Assert.True(File.Exists(resultFile), $"Missing result file at {resultFile}");

        foreach (var path in new[] { commandFile, analyzerFile, resultFile })
        {
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("Process.Start(\"git", text, StringComparison.Ordinal);
            Assert.DoesNotContain("git checkout", text, StringComparison.Ordinal);
            Assert.DoesNotContain("git worktree", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_RestorePlanEntryFieldsAreStableConstants_RoleStatusActionLockedAgainstDrift()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("locked");

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);

        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);

        var allowedRoles = new HashSet<string>(StringComparer.Ordinal)
        {
            TaskingHandoffBundleImportDryRunConstants.Roles.TaskPacket,
            TaskingHandoffBundleImportDryRunConstants.Roles.Preview,
            TaskingHandoffBundleImportDryRunConstants.Roles.Checklist,
            TaskingHandoffBundleImportDryRunConstants.Roles.Handoff
        };
        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            TaskingHandoffBundleImportDryRunConstants.CurrentStatuses.AlreadyPresentAndMatching,
            TaskingHandoffBundleImportDryRunConstants.CurrentStatuses.PresentButHashDiffers,
            TaskingHandoffBundleImportDryRunConstants.CurrentStatuses.Missing
        };
        var allowedActions = new HashSet<string>(StringComparer.Ordinal)
        {
            TaskingHandoffBundleImportDryRunConstants.Actions.NoOpAlreadyMatches,
            TaskingHandoffBundleImportDryRunConstants.Actions.WouldOverwriteWithRecordedContent,
            TaskingHandoffBundleImportDryRunConstants.Actions.WouldRecreateFromRecordedContent
        };

        Assert.Equal(4, result!.RestorePlan.Count);
        foreach (var entry in result.RestorePlan)
        {
            Assert.Contains(entry.Role, allowedRoles);
            Assert.Contains(entry.CurrentStatus, allowedStatuses);
            Assert.Contains(entry.DryRunAction, allowedActions);
        }

        // Lock the canonical order in <c>Roles.InOrder</c> too.
        Assert.Equal(
            new[]
            {
                TaskingHandoffBundleImportDryRunConstants.Roles.TaskPacket,
                TaskingHandoffBundleImportDryRunConstants.Roles.Preview,
                TaskingHandoffBundleImportDryRunConstants.Roles.Checklist,
                TaskingHandoffBundleImportDryRunConstants.Roles.Handoff
            },
            TaskingHandoffBundleImportDryRunConstants.Roles.InOrder);
    }

    [Fact]
    public void Execute_GivenValidBundle_RecordedShaInPlanMatchesBundleSha()
    {
        using var workspace = new DryRunWorkspace();
        var bundlePath = workspace.GenerateBundle("sha");
        var bundle = ReadBundle(bundlePath);

        var (exit, output) = RunJson(workspace, bundlePath);
        Assert.Equal(0, exit);
        var result = JsonSerializer.Deserialize<TaskingHandoffBundleImportDryRunResult>(output);
        Assert.NotNull(result);

        var byRole = result!.RestorePlan.ToDictionary(e => e.Role, StringComparer.Ordinal);
        Assert.Equal(
            bundle.SourceTaskPacketSha256,
            byRole[TaskingHandoffBundleImportDryRunConstants.Roles.TaskPacket].RecordedSha256);
        Assert.Equal(
            bundle.SourcePreviewSha256,
            byRole[TaskingHandoffBundleImportDryRunConstants.Roles.Preview].RecordedSha256);
        Assert.Equal(
            bundle.SourceChecklistSha256,
            byRole[TaskingHandoffBundleImportDryRunConstants.Roles.Checklist].RecordedSha256);
        Assert.Equal(
            bundle.SourceHandoffSha256,
            byRole[TaskingHandoffBundleImportDryRunConstants.Roles.Handoff].RecordedSha256);
    }

    // --------- helpers ---------

    private static TaskingHandoffBundleArtifact ReadBundle(string bundlePath)
    {
        var bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(
            File.ReadAllText(bundlePath));
        Assert.NotNull(bundle);
        return bundle!;
    }

    private static void AppendByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.WriteByte((byte)' ');
    }

    private static (int ExitCode, string Output) RunJson(DryRunWorkspace workspace, string bundlePath)
    {
        using var writer = new StringWriter();
        var exit = TaskingHandoffBundleImportDryRunCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);
        return (exit, writer.ToString());
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

    private sealed class DryRunWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-bundle-import-dry-run-tests-")
            .FullName;

        public DryRunWorkspace()
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

        public string GenerateBundle(string tag)
        {
            var (previewPath, checklistPath) = GeneratePreviewAndChecklist(tag);
            return BuildBundle(tag, previewPath, checklistPath);
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
