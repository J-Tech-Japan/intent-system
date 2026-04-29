using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G196 — coverage for <c>intent-cli tasking handoff-bundle-verify</c>. Class
/// name contains <c>Tasking</c>, <c>Handoff</c>, <c>Bundle</c>, and
/// <c>Verify</c> so reviewers can match the issue's recommended
/// <c>~Tasking|~Bundle|~Verify</c> filter.
/// </summary>
public sealed class TaskingHandoffBundleVerifyCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;

    public TaskingHandoffBundleVerifyCommandTests()
    {
        originalLauncher = TaskingHandoffBundleVerifyCommand.NestedProviderLauncher;
    }

    public void Dispose()
    {
        TaskingHandoffBundleVerifyCommand.NestedProviderLauncher = originalLauncher;
    }

    [Fact]
    public void Execute_GivenValidBundle_ReturnsZeroAndAllChecksPass()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("happy");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Valid: True", output, StringComparison.Ordinal);
        foreach (var id in TaskingHandoffBundleVerifyConstants.CheckIds.All)
        {
            Assert.Contains($"- {id}: passed=True", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_GivenValidBundle_TextOutputContainsLiteralStatusPhrase()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("phrase");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            writer.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidBundle_AllRequiredCheckIdsAppearExactlyOnce()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("once");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        foreach (var id in TaskingHandoffBundleVerifyConstants.CheckIds.All)
        {
            var marker = $"- {id}:";
            var first = output.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(first >= 0, $"Required check id missing in output: {id}");
            var second = output.IndexOf(marker, first + marker.Length, StringComparison.Ordinal);
            Assert.True(second < 0, $"Required check id appeared more than once: {id}");
        }
    }

    [Fact]
    public void Execute_GivenJsonFormat_ResultRoundTripsStably()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("rt");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);

        var result =
            JsonSerializer.Deserialize<TaskingHandoffBundleVerifyResult>(writer.ToString());
        Assert.NotNull(result);
        Assert.True(result!.Valid);
        Assert.Empty(result.Errors);
        Assert.Equal("intent-cli", result.Domain);
        Assert.False(string.IsNullOrWhiteSpace(result.BundleSha256));
        Assert.Equal(bundlePath, result.BundlePath);
    }

    [Fact]
    public void Execute_GivenJsonFormat_IncludesValidErrorsAndBundleIdentity()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("identity");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.True, root.GetProperty("valid").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("errors").ValueKind);
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());

        var bundlePathProp = root.GetProperty("bundle_path").GetString();
        Assert.Equal(bundlePath, bundlePathProp);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("bundle_sha256").GetString()));
        Assert.Equal("intent-cli", root.GetProperty("domain").GetString());
    }

    [Fact]
    public void Execute_GivenMissingBundleFile_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new VerifyWorkspace();
        var missing = "/tmp/this-bundle-does-not-exist-g196.json";

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", missing },
            writer);

        Assert.Equal(1, exitCode);
        var output = writer.ToString();
        Assert.Contains(missing, output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("does not exist", StringComparison.Ordinal)
                || output.Contains("not found", StringComparison.OrdinalIgnoreCase),
            $"Expected actionable 'does not exist' or 'not found' message; got: {output}");
    }

    [Fact]
    public void Execute_GivenMalformedBundleJson_ReturnsNonZero_ActionableErrorMessage()
    {
        using var workspace = new VerifyWorkspace();
        var malformed = workspace.GetPath("malformed-bundle.json");
        File.WriteAllText(malformed, "{ this is not : valid json,");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", malformed, "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("valid").ValueKind);
        var checks = doc.RootElement.GetProperty("checks");
        var anyParseFailFailed = false;
        foreach (var check in checks.EnumerateArray())
        {
            var id = check.GetProperty("id").GetString();
            var passed = check.GetProperty("passed").GetBoolean();
            if (id == TaskingHandoffBundleVerifyConstants.CheckIds.BundleJsonParses && !passed)
            {
                anyParseFailFailed = true;
            }
        }

        Assert.True(anyParseFailFailed, "Expected bundle_json_parses to be reported as failed.");
    }

    [Fact]
    public void Execute_GivenBundleMissingTaskPacketPath_ReturnsNonZero()
    {
        using var workspace = new VerifyWorkspace();
        var tampered = workspace.GenerateTamperedBundle(
            "missing-task-packet",
            json => json.Replace(
                "\"source_task_packet_path\":",
                "\"source_task_packet_path\": \"\",\n  \"__orig_source_task_packet_path\":",
                StringComparison.Ordinal));

        var checkResult = RunVerify(workspace, tampered);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketPathPresent);
    }

    [Fact]
    public void Execute_GivenBundleMissingPreviewPath_ReturnsNonZero()
    {
        using var workspace = new VerifyWorkspace();
        var tampered = workspace.GenerateTamperedBundle(
            "missing-preview",
            json => json.Replace(
                "\"source_preview_path\":",
                "\"source_preview_path\": \"\",\n  \"__orig_source_preview_path\":",
                StringComparison.Ordinal));

        var checkResult = RunVerify(workspace, tampered);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewPathPresent);
    }

    [Fact]
    public void Execute_GivenBundleMissingChecklistPath_ReturnsNonZero()
    {
        using var workspace = new VerifyWorkspace();
        var tampered = workspace.GenerateTamperedBundle(
            "missing-checklist",
            json => json.Replace(
                "\"source_checklist_path\":",
                "\"source_checklist_path\": \"\",\n  \"__orig_source_checklist_path\":",
                StringComparison.Ordinal));

        var checkResult = RunVerify(workspace, tampered);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistPathPresent);
    }

    [Fact]
    public void Execute_GivenBundleWithEmptyChecklist_ReturnsNonZero_ChecklistEmptyCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("empty-cl");
        var bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(
            File.ReadAllText(bundlePath));
        Assert.NotNull(bundle);

        var emptied = bundle! with
        {
            ChecklistPassedCheckIds = Array.Empty<string>(),
            ChecklistFailedCheckIds = Array.Empty<string>()
        };
        var emptiedPath = workspace.GetPath("bundle-empty-checklist.json");
        File.WriteAllText(emptiedPath, JsonSerializer.Serialize(emptied));

        var checkResult = RunVerify(workspace, emptiedPath);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistPassedOrFailedCheckIdsPresent);
    }

    [Fact]
    public void Execute_GivenBundleWithWrongStatus_ReturnsNonZero_BundleStatusCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var tampered = workspace.GenerateTamperedBundle(
            "wrong-status",
            json => json.Replace(
                "\"bundle_status\": \"local_only\"",
                "\"bundle_status\": \"published\"",
                StringComparison.Ordinal));

        var checkResult = RunVerify(workspace, tampered);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.BundleStatusLocalOnly);
    }

    [Fact]
    public void Execute_GivenBundleIsPublishedTrue_ReturnsNonZero_IsPublishedCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var tampered = workspace.GenerateTamperedBundle(
            "published-true",
            json => json.Replace(
                "\"is_published\": false",
                "\"is_published\": true",
                StringComparison.Ordinal));

        var checkResult = RunVerify(workspace, tampered);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.BundleIsPublishedFalse);
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new VerifyWorkspace();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--from-bundle", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("unknown");

        var args = new[] { "--from-bundle", bundlePath, "--bogus", "value" };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(workspace.Context, args, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new VerifyWorkspace();
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
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
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
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("no-artifact");

        var bundleBytesBefore = File.ReadAllBytes(bundlePath);
        var snapshotBefore = workspace.SnapshotAllFiles();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
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
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("no-provider");

        var launcherInvoked = false;
        TaskingHandoffBundleVerifyCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath },
            writer);

        Assert.Equal(0, exitCode);
        Assert.False(launcherInvoked);

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        var commandFile = Path.Combine(commandsDir, "TaskingHandoffBundleVerifyCommand.cs");
        var analyzerFile = Path.Combine(commandsDir, "TaskingHandoffBundleVerifyAnalyzer.cs");
        var resultFile = Path.Combine(commandsDir, "TaskingHandoffBundleVerifyResult.cs");
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
    public void Execute_AlwaysReportsBundleStatusFromSourceWithoutInverting()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("contract");

        // text format
        using (var writer = new StringWriter())
        {
            var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
                workspace.Context,
                new[] { "--from-bundle", bundlePath, "--format", "text" },
                writer);
            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains(
                $"- {TaskingHandoffBundleVerifyConstants.CheckIds.BundleStatusLocalOnly}: passed=True",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                $"- {TaskingHandoffBundleVerifyConstants.CheckIds.BundleIsPublishedFalse}: passed=True",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                $"- {TaskingHandoffBundleVerifyConstants.CheckIds.BundleIsAutomationVisibleFalse}: passed=True",
                output,
                StringComparison.Ordinal);
        }

        // json format
        using (var writer = new StringWriter())
        {
            var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
                workspace.Context,
                new[] { "--from-bundle", bundlePath, "--format", "json" },
                writer);
            Assert.Equal(0, exitCode);

            using var doc = JsonDocument.Parse(writer.ToString());
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("valid").ValueKind);
        }

        // The SOURCE bundle on disk really has these literal contract fields —
        // verify propagates without inversion.
        var sourceBundle =
            JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(File.ReadAllText(bundlePath));
        Assert.NotNull(sourceBundle);
        Assert.Equal("local_only", sourceBundle!.BundleStatus);
        Assert.False(sourceBundle.IsPublished);
        Assert.False(sourceBundle.IsAutomationVisible);
    }

    // -------------------------------------------------------------------
    // G197 — content-hash pinning for referenced source artifacts.
    // -------------------------------------------------------------------

    [Fact]
    public void Execute_GivenValidBundle_AllReferencedArtifactsExistAndHashesMatch()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-happy");

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(0, checkResult.ExitCode);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches, passedIds);
    }

    [Fact]
    public void Execute_GivenReferencedTaskPacketDeleted_TaskPacketFileExistsCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-del-tp");
        var bundle = ReadBundle(bundlePath);
        File.Delete(bundle.SourceTaskPacketPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches, passedIds);
    }

    [Fact]
    public void Execute_GivenReferencedPreviewDeleted_PreviewFileExistsCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-del-pv");
        var bundle = ReadBundle(bundlePath);
        File.Delete(bundle.SourcePreviewPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches, passedIds);
    }

    [Fact]
    public void Execute_GivenReferencedChecklistDeleted_ChecklistFileExistsCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-del-cl");
        var bundle = ReadBundle(bundlePath);
        File.Delete(bundle.SourceChecklistPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches, passedIds);
    }

    [Fact]
    public void Execute_GivenReferencedHandoffDeleted_HandoffFileExistsCheckFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-del-ho");
        var bundle = ReadBundle(bundlePath);
        File.Delete(bundle.SourceHandoffPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists);
        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists, passedIds);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches, passedIds);
    }

    [Fact]
    public void Execute_GivenReferencedTaskPacketEdited_TaskPacketHashMismatchFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-edit-tp");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourceTaskPacketPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists, passedIds);

        var failedDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches);
        Assert.Contains("recorded:", failedDetail, StringComparison.Ordinal);
        Assert.Contains("actual:", failedDetail, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceTaskPacketSha256, failedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReferencedPreviewEdited_PreviewHashMismatchFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-edit-pv");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourcePreviewPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists, passedIds);

        var failedDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches);
        Assert.Contains("recorded:", failedDetail, StringComparison.Ordinal);
        Assert.Contains("actual:", failedDetail, StringComparison.Ordinal);
        Assert.Contains(bundle.SourcePreviewSha256, failedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReferencedChecklistEdited_ChecklistHashMismatchFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-edit-cl");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourceChecklistPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists, passedIds);

        var failedDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches);
        Assert.Contains("recorded:", failedDetail, StringComparison.Ordinal);
        Assert.Contains("actual:", failedDetail, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceChecklistSha256, failedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReferencedHandoffEdited_HandoffHashMismatchFails()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-edit-ho");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourceHandoffPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);

        var passedIds = ExtractPassedCheckIds(checkResult);
        Assert.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists, passedIds);

        var failedDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches);
        Assert.Contains("recorded:", failedDetail, StringComparison.Ordinal);
        Assert.Contains("actual:", failedDetail, StringComparison.Ordinal);
        Assert.Contains(bundle.SourceHandoffSha256, failedDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenJsonFormat_HashMismatchAppearsInErrorsArrayWithBothHashes()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-json-mm");
        var bundle = ReadBundle(bundlePath);
        AppendByte(bundle.SourceTaskPacketPath);

        var checkResult = RunVerify(workspace, bundlePath);
        Assert.Equal(1, checkResult.ExitCode);

        using var doc = JsonDocument.Parse(checkResult.Output);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("valid").ValueKind);

        var actualBytes = File.ReadAllBytes(bundle.SourceTaskPacketPath);
        var actualSha = ComputeSha256HexLocal(actualBytes);

        var errors = doc.RootElement.GetProperty("errors");
        var foundMismatchEntry = false;
        foreach (var err in errors.EnumerateArray())
        {
            var entry = err.GetString() ?? string.Empty;
            if (entry.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, StringComparison.Ordinal)
                && entry.Contains(bundle.SourceTaskPacketSha256, StringComparison.Ordinal)
                && entry.Contains(actualSha, StringComparison.Ordinal))
            {
                foundMismatchEntry = true;
                break;
            }
        }

        Assert.True(
            foundMismatchEntry,
            $"Expected errors[] to include task_packet_hash_matches with both recorded and actual sha256 hex. Output: {checkResult.Output}");
    }

    [Fact]
    public void Execute_GivenAllChecksPass_NewCheckIdsAppearInStableOrderAfterPathSha256Checks()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-order");

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);
        Assert.Equal(0, exitCode);

        using var doc = JsonDocument.Parse(writer.ToString());
        var idsInOrder = new List<string>();
        foreach (var check in doc.RootElement.GetProperty("checks").EnumerateArray())
        {
            idsInOrder.Add(check.GetProperty("id").GetString()!);
        }

        // The eight new G197 ids must appear in the documented stable order
        // and they must come AFTER the field-presence (path/sha256) tier and
        // AFTER the artifact-only invariant check, i.e. at the end.
        var expectedTail = new[]
        {
            TaskingHandoffBundleVerifyConstants.CheckIds.BundleArtifactOnlyNoProviderLaunchDirective,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches
        };

        Assert.True(idsInOrder.Count >= expectedTail.Length);
        var tail = idsInOrder.GetRange(idsInOrder.Count - expectedTail.Length, expectedTail.Length);
        Assert.Equal(expectedTail, tail);

        // Each new file_exists must come AFTER its path-presence sibling, and
        // each hash_matches must come AFTER its sha256-presence sibling.
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketPathPresent,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketSha256Present,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewPathPresent,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewFileExists);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewSha256Present,
            TaskingHandoffBundleVerifyConstants.CheckIds.PreviewHashMatches);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistPathPresent,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistFileExists);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistSha256Present,
            TaskingHandoffBundleVerifyConstants.CheckIds.ChecklistHashMatches);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffPathPresent,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffFileExists);
        AssertOrder(idsInOrder,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffSha256Present,
            TaskingHandoffBundleVerifyConstants.CheckIds.HandoffHashMatches);
    }

    [Fact]
    public void Execute_GivenSourcePathFieldEmpty_FileExistsCheckSkippedDeterministically()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-empty-path");

        // Mutate the bundle JSON to set source_task_packet_path to "" without
        // any artifact removal, simulating an upstream G194 path-missing case.
        var bundle = ReadBundle(bundlePath);
        var emptied = bundle with { SourceTaskPacketPath = string.Empty };
        var emptiedPath = workspace.GetPath("bundle-g197-empty-path-tampered.json");
        File.WriteAllText(emptiedPath, JsonSerializer.Serialize(emptied));

        var checkResult = RunVerify(workspace, emptiedPath);
        Assert.Equal(1, checkResult.ExitCode);

        AssertCheckFailed(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketPathPresent);

        var fileExistsDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketFileExists);
        Assert.True(
            fileExistsDetail.Contains("skipped", StringComparison.OrdinalIgnoreCase)
                || fileExistsDetail.Contains("missing or empty", StringComparison.OrdinalIgnoreCase),
            $"Expected file_exists detail to mention 'skipped' or 'missing or empty path'; got: {fileExistsDetail}");

        var hashDetail = GetFailedCheckDetail(
            checkResult,
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches);
        Assert.Contains("skipped", hashDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_PreservesAllExistingG196Checks_NoIdRemovedOrRenamed()
    {
        // The 16 ids accepted at the close of G196. If any of them ever
        // disappears or is renamed, this test is the canary.
        var g196Ids = new[]
        {
            "bundle_path_present_and_readable",
            "bundle_json_parses",
            "bundle_status_local_only",
            "bundle_is_published_false",
            "bundle_is_automation_visible_false",
            "task_packet_path_present",
            "task_packet_sha256_present",
            "preview_path_present",
            "preview_sha256_present",
            "handoff_path_present",
            "handoff_sha256_present",
            "checklist_path_present",
            "checklist_sha256_present",
            "checklist_passed_check_ids_present_or_failed_check_ids_present",
            "recommended_worker_action_non_empty",
            "bundle_artifact_only_no_provider_launch_directive"
        };

        foreach (var id in g196Ids)
        {
            Assert.Contains(id, TaskingHandoffBundleVerifyConstants.CheckIds.All);
        }
    }

    [Fact]
    public void Execute_NeverWritesAnyArtifactFile_AfterReadingReferencedArtifacts()
    {
        using var workspace = new VerifyWorkspace();
        var bundlePath = workspace.GenerateBundle("g197-no-write");

        var snapshotBefore = workspace.SnapshotAllFiles();

        using var writer = new StringWriter();
        var exitCode = TaskingHandoffBundleVerifyCommand.Execute(
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

    private static TaskingHandoffBundleArtifact ReadBundle(string bundlePath)
    {
        var bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(
            File.ReadAllText(bundlePath));
        Assert.NotNull(bundle);
        return bundle!;
    }

    private static void AppendByte(string path)
    {
        // Adding a single byte changes the file's sha256. ASCII space is
        // typically harmless to JSON-decoding tools but the analyzer never
        // re-parses these source artifacts; only the byte hash is compared.
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write);
        stream.WriteByte((byte)' ');
    }

    private static IReadOnlyList<string> ExtractPassedCheckIds((int ExitCode, string Output) result)
    {
        var ids = new List<string>();
        using var doc = JsonDocument.Parse(result.Output);
        foreach (var check in doc.RootElement.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("passed").GetBoolean())
            {
                ids.Add(check.GetProperty("id").GetString()!);
            }
        }

        return ids;
    }

    private static string GetFailedCheckDetail((int ExitCode, string Output) result, string checkId)
    {
        using var doc = JsonDocument.Parse(result.Output);
        foreach (var check in doc.RootElement.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("id").GetString() == checkId)
            {
                Assert.False(
                    check.GetProperty("passed").GetBoolean(),
                    $"Expected {checkId} to be passed=false.");
                return check.GetProperty("detail").GetString() ?? string.Empty;
            }
        }

        Assert.Fail($"Check id {checkId} not found in output.");
        return string.Empty;
    }

    private static void AssertOrder(IReadOnlyList<string> ids, string before, string after)
    {
        var beforeIndex = ids.ToList().IndexOf(before);
        var afterIndex = ids.ToList().IndexOf(after);
        Assert.True(beforeIndex >= 0, $"{before} not found");
        Assert.True(afterIndex >= 0, $"{after} not found");
        Assert.True(
            beforeIndex < afterIndex,
            $"Expected {before} (index {beforeIndex}) to come before {after} (index {afterIndex}).");
    }

    private static string ComputeSha256HexLocal(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static (int ExitCode, string Output) RunVerify(VerifyWorkspace workspace, string bundlePath)
    {
        using var writer = new StringWriter();
        var exit = TaskingHandoffBundleVerifyCommand.Execute(
            workspace.Context,
            new[] { "--from-bundle", bundlePath, "--format", "json" },
            writer);
        return (exit, writer.ToString());
    }

    private static void AssertCheckFailed((int ExitCode, string Output) result, string checkId)
    {
        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal(JsonValueKind.False, doc.RootElement.GetProperty("valid").ValueKind);

        var found = false;
        foreach (var check in doc.RootElement.GetProperty("checks").EnumerateArray())
        {
            if (check.GetProperty("id").GetString() == checkId)
            {
                Assert.False(
                    check.GetProperty("passed").GetBoolean(),
                    $"Expected check {checkId} to be passed=false but it was passed=true.");
                found = true;
                break;
            }
        }

        Assert.True(found, $"Expected check id {checkId} to appear in output.");
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

    private sealed class VerifyWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-handoff-bundle-verify-tests-")
            .FullName;

        public VerifyWorkspace()
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

        public string GenerateTamperedBundle(string tag, Func<string, string> tamper)
        {
            var bundlePath = GenerateBundle($"{tag}-base");
            var json = File.ReadAllText(bundlePath);
            var tampered = tamper(json);
            Assert.NotEqual(json, tampered);
            var tamperedPath = GetPath($"bundle-{tag}-tampered.json");
            File.WriteAllText(tamperedPath, tampered);
            return tamperedPath;
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
