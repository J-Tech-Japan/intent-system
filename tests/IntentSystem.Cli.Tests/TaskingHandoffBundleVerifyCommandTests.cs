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
