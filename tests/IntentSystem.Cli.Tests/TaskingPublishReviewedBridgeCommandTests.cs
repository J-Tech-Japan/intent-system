using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G199 — coverage for <c>intent-cli tasking publish-reviewed-bridge</c>.
/// Class name contains <c>Tasking</c>, <c>PublishReviewed</c>, and
/// <c>Bridge</c> so reviewers can match the issue's recommended
/// <c>~Tasking</c> filter.
/// </summary>
public sealed class TaskingPublishReviewedBridgeCommandTests : IDisposable
{
    private readonly Func<bool>? originalLauncher;
    private readonly Func<DateTimeOffset> originalTimestampFactory;

    public TaskingPublishReviewedBridgeCommandTests()
    {
        originalLauncher = TaskingPublishReviewedBridgeCommand.NestedProviderLauncher;
        originalTimestampFactory = TaskingPublishReviewedBridgeCommand.TimestampFactory;
        TaskingPublishReviewedBridgeCommand.TimestampFactory =
            () => new DateTimeOffset(2026, 4, 29, 12, 34, 56, TimeSpan.Zero);
    }

    public void Dispose()
    {
        TaskingPublishReviewedBridgeCommand.NestedProviderLauncher = originalLauncher;
        TaskingPublishReviewedBridgeCommand.TimestampFactory = originalTimestampFactory;
    }

    [Fact]
    public void Execute_GivenValidBundleBodyAndApproval_WritesReviewedReadyArtifact()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("happy");
        var bodyPath = workspace.WriteValidBody("body-happy.md");
        var outPath = workspace.GetPath("reviewed-happy.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            output,
            StringComparison.Ordinal);
        Assert.Contains("Status: ok", output, StringComparison.Ordinal);

        Assert.True(File.Exists(outPath), $"Artifact not written at {outPath}");
        var artifact = ReadArtifact(outPath);
        Assert.False(artifact.IsPublished);
        Assert.False(artifact.IsAutomationVisible);
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.LocalOnlyStatus,
            artifact.ReviewedBridgeStatus);
        Assert.Contains(
            "UNPUBLISHED, local-only, not automation-visible",
            artifact.SummaryLine,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenValidInputs_ReviewedArtifactJsonRoundTripsStably()
    {
        using var workspace = new BridgeWorkspace();
        var (artifact, _) = RunSuccessful(workspace, "rt", "approved");

        var serialized = JsonSerializer.Serialize(artifact);
        var reparsed = JsonSerializer.Deserialize<TaskingPublishReviewedBridgeArtifact>(serialized);
        Assert.NotNull(reparsed);
        Assert.Equal(artifact.SourceBundlePath, reparsed!.SourceBundlePath);
        Assert.Equal(artifact.SourceBundleSha256, reparsed.SourceBundleSha256);
        Assert.Equal(artifact.SourceBodyPath, reparsed.SourceBodyPath);
        Assert.Equal(artifact.SourceBodySha256, reparsed.SourceBodySha256);
        Assert.Equal(artifact.Domain, reparsed.Domain);
        Assert.Equal(artifact.IsPublished, reparsed.IsPublished);
        Assert.Equal(artifact.IsAutomationVisible, reparsed.IsAutomationVisible);
        Assert.Equal(artifact.ReviewedBridgeStatus, reparsed.ReviewedBridgeStatus);
        Assert.Equal(artifact.ApprovalMarker, reparsed.ApprovalMarker);
        Assert.Equal(artifact.ApprovalMarkerKind, reparsed.ApprovalMarkerKind);
        Assert.Equal(artifact.GeneratedAtUtc, reparsed.GeneratedAtUtc);
        Assert.Equal(artifact.SummaryLine, reparsed.SummaryLine);
        Assert.Equal(artifact.VerifySummary.Valid, reparsed.VerifySummary.Valid);
        Assert.Equal(artifact.BodyContractValidation.IsValid, reparsed.BodyContractValidation.IsValid);
    }

    [Fact]
    public void Execute_GivenValidInputs_RecordsBundleSha256AndBodySha256()
    {
        using var workspace = new BridgeWorkspace();
        var (artifact, paths) = RunSuccessful(workspace, "shas", "approved");

        var bundleBytes = File.ReadAllBytes(paths.BundlePath);
        var bodyBytes = File.ReadAllBytes(paths.BodyPath);
        var expectedBundleSha = IssuePrepareCommand.ComputeSha256Hex(bundleBytes);
        var expectedBodySha = IssuePrepareCommand.ComputeSha256Hex(bodyBytes);

        Assert.Equal(expectedBundleSha, artifact.SourceBundleSha256);
        Assert.Equal(expectedBodySha, artifact.SourceBodySha256);
        Assert.Matches("^[0-9a-f]+$", artifact.SourceBundleSha256);
        Assert.Matches("^[0-9a-f]+$", artifact.SourceBodySha256);
    }

    [Fact]
    public void Execute_GivenApprovalLiteralApproved_RecordsApprovalKindApproved()
    {
        using var workspace = new BridgeWorkspace();
        var (artifact, _) = RunSuccessful(workspace, "appr", "approved");
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.ApprovalMarkerKinds.Approved,
            artifact.ApprovalMarkerKind);
        Assert.Equal("approved", artifact.ApprovalMarker);
    }

    [Fact]
    public void Execute_GivenApprovalLiteralReviewedByOperator_RecordsApprovalKindReviewedByOperator()
    {
        using var workspace = new BridgeWorkspace();
        var (artifact, _) = RunSuccessful(workspace, "rbo", "reviewed-by-operator");
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.ApprovalMarkerKinds.ReviewedByOperator,
            artifact.ApprovalMarkerKind);
        Assert.Equal("reviewed-by-operator", artifact.ApprovalMarker);
    }

    [Fact]
    public void Execute_GivenApprovalWithTagPrefix_RecordsApprovalKindApprovedWithTag()
    {
        using var workspace = new BridgeWorkspace();
        var marker = "approved:tomohisa-2026-04-29";
        var (artifact, _) = RunSuccessful(workspace, "tag", marker);
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.ApprovalMarkerKinds.ApprovedWithTag,
            artifact.ApprovalMarkerKind);
        Assert.Equal(marker, artifact.ApprovalMarker);
    }

    [Fact]
    public void Execute_GivenInvalidApprovalMarker_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("inv");
        var bodyPath = workspace.WriteValidBody("body-inv.md");
        var outPath = workspace.GetPath("reviewed-inv.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "yes",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written on approval failure");

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.ApprovalMarkerInvalid,
            doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Execute_GivenBlankApprovalMarker_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("blank");
        var bodyPath = workspace.WriteValidBody("body-blank.md");
        var outPath = workspace.GetPath("reviewed-blank.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written on blank approval");

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.ApprovalMarkerInvalid,
            doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Execute_GivenBundleWithFailedVerify_ReturnsNonZero_StatusVerifyFailed()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("vf");
        var bundle = JsonSerializer.Deserialize<TaskingHandoffBundleArtifact>(
            File.ReadAllText(bundlePath))!;
        // Tamper the task packet so its sha no longer matches the bundle's
        // recorded source_task_packet_sha256.
        using (var stream = new FileStream(bundle.SourceTaskPacketPath, FileMode.Append, FileAccess.Write))
        {
            stream.WriteByte((byte)' ');
        }

        var bodyPath = workspace.WriteValidBody("body-vf.md");
        var outPath = workspace.GetPath("reviewed-vf.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written on verify_failed");

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.VerifyFailed,
            doc.RootElement.GetProperty("status").GetString());

        var failedCheckIds = doc.RootElement
            .GetProperty("verify_summary")
            .GetProperty("failed_check_ids");
        var ids = new List<string>();
        foreach (var element in failedCheckIds.EnumerateArray())
        {
            ids.Add(element.GetString() ?? string.Empty);
        }

        Assert.Contains(
            TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches,
            ids);

        var errors = doc.RootElement.GetProperty("errors");
        var anyMatch = false;
        foreach (var element in errors.EnumerateArray())
        {
            var s = element.GetString() ?? string.Empty;
            if (s.Contains(TaskingHandoffBundleVerifyConstants.CheckIds.TaskPacketHashMatches, StringComparison.Ordinal))
            {
                anyMatch = true;
                break;
            }
        }

        Assert.True(anyMatch, "Expected errors to mention task_packet hash check id");
    }

    [Fact]
    public void Execute_GivenMissingBundleFile_ReturnsNonZero()
    {
        using var workspace = new BridgeWorkspace();
        var bodyPath = workspace.WriteValidBody("body-missing-bundle.md");
        var outPath = workspace.GetPath("reviewed-missing-bundle.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", "/tmp/does-not-exist-g199.json",
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written when bundle missing");
    }

    [Fact]
    public void Execute_GivenMissingBodyFile_ReturnsNonZero_StatusMissingBody()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("mb");
        var outPath = workspace.GetPath("reviewed-missing-body.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", "/tmp/does-not-exist-g199-body.md",
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written on missing_body");
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.MissingBody,
            doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Execute_GivenContractInvalidBody_ReturnsNonZero_StatusBodyContractInvalid()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("ci");
        var bodyPath = workspace.GetPath("body-ci.md");
        // Drop "## Acceptance Criteria" section.
        var content = CompleteValidBody.Replace(
            "## Acceptance Criteria\n\n- Deterministic behaviour described here.\n\n",
            string.Empty,
            StringComparison.Ordinal);
        File.WriteAllText(bodyPath, content);
        var outPath = workspace.GetPath("reviewed-ci.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath));

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.BodyContractInvalid,
            doc.RootElement.GetProperty("status").GetString());

        var errors = doc.RootElement.GetProperty("errors");
        var anyMissing = false;
        foreach (var element in errors.EnumerateArray())
        {
            var s = element.GetString() ?? string.Empty;
            if (s.Contains("Acceptance Criteria", StringComparison.Ordinal))
            {
                anyMissing = true;
                break;
            }
        }

        Assert.True(anyMissing, "Expected errors to mention 'Acceptance Criteria' missing heading");
    }

    [Fact]
    public void Execute_GivenBodyWithPlaceholderRelatedLinks_ReturnsNonZero_StatusBodyContractInvalid()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("rl");
        var bodyPath = workspace.GetPath("body-rl.md");
        var content = CompleteValidBody.Replace(
            "- Host runbook: intents/rules/automations/runbook.md",
            "- TODO",
            StringComparison.Ordinal);
        File.WriteAllText(bodyPath, content);
        var outPath = workspace.GetPath("reviewed-rl.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath));
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            TaskingPublishReviewedBridgeConstants.Statuses.BodyContractInvalid,
            doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Execute_GivenMalformedBundleJson_ReturnsNonZero_NoArtifactWritten()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GetPath("bundle-garbage.json");
        File.WriteAllText(bundlePath, "{ this is not : valid json,");
        var bodyPath = workspace.WriteValidBody("body-garbage.md");
        var outPath = workspace.GetPath("reviewed-garbage.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--format", "json"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.False(File.Exists(outPath), "Artifact must not be written on malformed bundle");
    }

    [Fact]
    public void Execute_GivenMissingFlag_ReturnsNonZero()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("mf");
        var bodyPath = workspace.WriteValidBody("body-mf.md");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved"
                // no --out
            },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--out", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownArgument_ReturnsNonZero()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("ua");
        var bodyPath = workspace.WriteValidBody("body-ua.md");
        var outPath = workspace.GetPath("reviewed-ua.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath,
                "--bogus", "value"
            },
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("--bogus", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesToQueueStateOrRunsLog()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("noq");
        var bodyPath = workspace.WriteValidBody("body-noq.md");
        var outPath = workspace.GetPath("reviewed-noq.json");

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
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exit);

        Assert.Equal(queueBefore, File.ReadAllBytes(queueStatePath));
        Assert.Equal(runsBefore, File.ReadAllBytes(runsLogPath));
    }

    [Fact]
    public void Execute_NeverOverwritesUnrelatedArtifacts_OnlyWritesToOutFlag()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("only");
        var bodyPath = workspace.WriteValidBody("body-only.md");
        var outPath = workspace.GetPath("reviewed-only.json");

        var sentinelPath = workspace.GetPath("sentinel.txt");
        var sentinelContent = Encoding.UTF8.GetBytes("DO NOT TOUCH\n");
        File.WriteAllBytes(sentinelPath, sentinelContent);

        var snapshotBefore = workspace.SnapshotAllFiles();

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exit);

        // Sentinel must be byte-identical.
        Assert.Equal(sentinelContent, File.ReadAllBytes(sentinelPath));

        var snapshotAfter = workspace.SnapshotAllFiles();
        // Only the new file must be the --out path.
        var newPaths = snapshotAfter.Keys.Where(p => !snapshotBefore.ContainsKey(p)).ToList();
        Assert.Single(newPaths);
        Assert.Equal(Path.GetFullPath(outPath), Path.GetFullPath(newPaths[0]));

        // Any pre-existing file (excluding the sentinel and any unrelated)
        // must not have been mutated.
        foreach (var (path, contentBefore) in snapshotBefore)
        {
            Assert.True(snapshotAfter.ContainsKey(path), $"Pre-existing file disappeared: {path}");
            Assert.Equal(contentBefore, snapshotAfter[path]);
        }
    }

    [Fact]
    public void Execute_NeverInvokesProvider_NoProcessStartInNewSurface()
    {
        using var workspace = new BridgeWorkspace();
        var bundlePath = workspace.GenerateBundle("npr");
        var bodyPath = workspace.WriteValidBody("body-npr.md");
        var outPath = workspace.GetPath("reviewed-npr.json");

        var launcherInvoked = false;
        TaskingPublishReviewedBridgeCommand.NestedProviderLauncher = () =>
        {
            launcherInvoked = true;
            return true;
        };

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", "approved",
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exit);
        Assert.False(launcherInvoked, "NestedProviderLauncher must never be invoked by G199");

        if (!TryResolveCommandsSourceDirectory(out var commandsDir))
        {
            return;
        }

        foreach (var name in NewSurfaceFileNames)
        {
            var p = Path.Combine(commandsDir, name);
            if (!File.Exists(p))
            {
                continue;
            }

            var text = File.ReadAllText(p);
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

        foreach (var name in NewSurfaceFileNames)
        {
            var p = Path.Combine(commandsDir, name);
            Assert.True(File.Exists(p), $"Missing new-surface file at {p}");
            var text = File.ReadAllText(p);
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

        foreach (var name in NewSurfaceFileNames)
        {
            var p = Path.Combine(commandsDir, name);
            Assert.True(File.Exists(p), $"Missing new-surface file at {p}");
            var text = File.ReadAllText(p);
            Assert.DoesNotContain("gh issue create", text, StringComparison.Ordinal);
            Assert.DoesNotContain("gh issue edit", text, StringComparison.Ordinal);
            Assert.DoesNotContain("gh pr", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Execute_AlwaysMarksContractFieldsLiteralFalseAndLocalOnly()
    {
        using var workspace = new BridgeWorkspace();
        var (artifact, _) = RunSuccessful(workspace, "lock", "approved:tomohisa-2026-04-29");
        Assert.False(artifact.IsPublished);
        Assert.False(artifact.IsAutomationVisible);
        Assert.Equal("local_only", artifact.ReviewedBridgeStatus);

        // Verify accepted shapes constant is locked too.
        Assert.Contains("approved", TaskingPublishReviewedBridgeConstants.AcceptedApprovalShapes);
        Assert.Contains("reviewed-by-operator", TaskingPublishReviewedBridgeConstants.AcceptedApprovalShapes);
        Assert.Contains(
            TaskingPublishReviewedBridgeConstants.AcceptedApprovalShapes,
            s => s.StartsWith("approved:", StringComparison.Ordinal));
    }

    // ----- helpers -----

    private static readonly string[] NewSurfaceFileNames = new[]
    {
        "TaskingPublishReviewedBridgeCommand.cs",
        "TaskingPublishReviewedBridgeAnalyzer.cs",
        "TaskingPublishReviewedBridgeArtifact.cs"
    };

    private static TaskingPublishReviewedBridgeArtifact ReadArtifact(string path)
    {
        var json = File.ReadAllText(path);
        var artifact = JsonSerializer.Deserialize<TaskingPublishReviewedBridgeArtifact>(json);
        Assert.NotNull(artifact);
        return artifact!;
    }

    private static (TaskingPublishReviewedBridgeArtifact artifact, BridgePaths paths) RunSuccessful(
        BridgeWorkspace workspace,
        string tag,
        string approval)
    {
        var bundlePath = workspace.GenerateBundle(tag);
        var bodyPath = workspace.WriteValidBody($"body-{tag}.md");
        var outPath = workspace.GetPath($"reviewed-{tag}.json");

        using var writer = new StringWriter();
        var exit = TaskingPublishReviewedBridgeCommand.Execute(
            workspace.Context,
            new[]
            {
                "--from-bundle", bundlePath,
                "--from-body", bodyPath,
                "--approval", approval,
                "--out", outPath
            },
            writer);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(outPath), $"Artifact not written at {outPath}");

        return (ReadArtifact(outPath), new BridgePaths(bundlePath, bodyPath, outPath));
    }

    private sealed record BridgePaths(string BundlePath, string BodyPath, string OutPath);

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

    private const string CompleteValidBody =
        "# G999 Example child issue body for validator tests\n\n"
        + "## Goal\n\nStand-alone description of what this slice changes.\n\n"
        + "## Why This Slice Exists Now\n\nSurrounding rationale for cutting the slice now.\n\n"
        + "## Current Observed State\n\nObserved state described in actionable terms.\n\n"
        + "## Accepted Baseline You May Assume\n\nBaseline assumptions stated explicitly.\n\n"
        + "## Target Repo / Path / Part\n\n- Repository: J-Tech-Japan/intent-system\n- Likely areas: src/, tests/\n\n"
        + "## In Scope\n\n- The slice change itself.\n\n"
        + "## Out Of Scope\n\n- Adjacent refactors.\n\n"
        + "## Acceptance Criteria\n\n- Deterministic behaviour described here.\n\n"
        + "## Verification\n\nRun the focused suite.\n\n"
        + "## Related Links\n\n- Host runbook: intents/rules/automations/runbook.md\n\n"
        + "## Base Branch Policy\n\nPolicy: `direct-main`\nExpected PR base branch: `main`\nOpen all child PRs against `main` directly.\n";

    private sealed class BridgeWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("tasking-publish-reviewed-bridge-tests-")
            .FullName;

        public BridgeWorkspace()
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

        public string WriteValidBody(string relative)
        {
            var path = GetPath(relative);
            File.WriteAllText(path, CompleteValidBody);
            return path;
        }

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
