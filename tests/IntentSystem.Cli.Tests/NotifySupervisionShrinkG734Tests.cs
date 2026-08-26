using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G734: the sanctioned shrink path rewrites existing legacy records, keeps
/// both JSONL files in the same atomic boundary, resolves readable evidence
/// through a committed definition manifest, and leaves an explicit audit.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionShrinkG734Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g734-").FullName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly DateTimeOffset FixtureEpoch =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        NotifySupervisionStore.ShrinkFaultInjector = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShrinkWrite_RewritesExistingRecordsKeepsCyclesReadableAndAuditsEveryOutcome()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);

        const int stallCount = 240;
        File.WriteAllText(
            stallsPath,
            string.Join(
                    Environment.NewLine,
                    Enumerable.Range(0, stallCount).Select(index => SerializeLegacyStall(index)))
                + Environment.NewLine);
        for (var index = 0; index < 4; index++)
        {
            Assert.True(NotifySupervisionStore.RecordCycle(
                cyclesPath,
                new NotifySupervisionCycle
                {
                    CycleId = $"cycle-{index}",
                    StartedAt = DateTimeOffset.UtcNow.AddMinutes(-index),
                    CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-index),
                    Writer = NotifySupervisionWriterIdentity.Current(),
                    IntervalSeconds = 300,
                },
                write: true).Applied);
        }

        var beforeStalls = new FileInfo(stallsPath).Length;
        var beforeCycles = new FileInfo(cyclesPath).Length;
        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement;
        Assert.Equal("supervise-shrink", result.GetProperty("operation").GetString());
        Assert.Equal("write", result.GetProperty("command_mode").GetString());
        Assert.Equal(stallCount + 4, result.GetProperty("before_record_count").GetInt32());
        Assert.Equal(stallCount + 4, result.GetProperty("after_record_count").GetInt32());
        Assert.True(result.GetProperty("after_bytes").GetInt64() < result.GetProperty("before_bytes").GetInt64());
        Assert.True(result.GetProperty("invariant_text").GetProperty("literal_bytes_removed_from_records").GetInt64() > 0);
        Assert.True(result.GetProperty("invariant_text").GetProperty("net_record_bytes_saved").GetInt64() > 0);
        Assert.Equal(
            4,
            result.GetProperty("files").GetProperty("cycles").GetProperty("before_record_count").GetInt32());
        Assert.Equal(
            4,
            result.GetProperty("files").GetProperty("cycles").GetProperty("after_record_count").GetInt32());
        Assert.Equal("running", result.GetProperty("supervisor_state").GetString());

        var rawStalls = File.ReadAllText(stallsPath);
        Assert.DoesNotContain(NotifySupervisionStore.HerdrRegistrationDefinition, rawStalls, StringComparison.Ordinal);
        Assert.Contains("\"evidence_ref\":\"recorded-herdr-seat-registration\"", rawStalls, StringComparison.Ordinal);
        Assert.Equal(beforeCycles, new FileInfo(cyclesPath).Length);
        Assert.True(File.Exists(NotifySupervisionStore.ResolveEvidenceDefinitionsPath(artifactRoot, Domain, Team)));
        Assert.True(File.Exists(NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team)));

        var read = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(read.Resolved, read.Error);
        Assert.Equal(stallCount, read.StallHistory.Count(record => record.Kind == "g734-legacy-stall"));
        Assert.Contains(read.StallHistory, record =>
            record.RegistrationDefinition == NotifySupervisionStore.HerdrRegistrationDefinition
            && record.Evidence!.Contains(
                $"registration_definition:{NotifySupervisionStore.HerdrRegistrationDefinition}",
                StringComparer.Ordinal));
        Assert.Equal("cycle-0", read.LastCycle?.CycleId);

        var audit = File.ReadAllText(NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team));
        Assert.Contains("\"records_archived\":0", audit, StringComparison.Ordinal);
        Assert.Contains("\"records_discarded\":0", audit, StringComparison.Ordinal);
        Assert.Contains("\"records_rotated\":0", audit, StringComparison.Ordinal);
        Assert.Contains("cycles.jsonl", audit, StringComparison.Ordinal);
    }

    [Fact]
    public void NewStallAppend_UsesReadableReferenceAndReadRestoresTheDefinition()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var path = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var record = CreateStall("new-reference", includeDefinitionEvidence: true);

        Assert.True(NotifySupervisionStore.OpenStall(path, record, write: true).Applied);

        var raw = File.ReadAllText(path);
        Assert.DoesNotContain(NotifySupervisionStore.HerdrRegistrationDefinition, raw, StringComparison.Ordinal);
        Assert.Contains("evidence_ref", raw, StringComparison.Ordinal);
        var read = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        var restored = Assert.Single(read.StallHistory);
        Assert.Equal(NotifySupervisionStore.HerdrRegistrationDefinition, restored.RegistrationDefinition);
        Assert.Contains(
            $"registration_definition:{NotifySupervisionStore.HerdrRegistrationDefinition}",
            restored.Evidence!);
    }

    [Fact]
    public void ShrinkDryRun_ReportsBothFilesWithoutWritingManifestOrAudit()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);
        File.WriteAllText(stallsPath, SerializeLegacyStall(1) + Environment.NewLine);
        File.WriteAllText(cyclesPath, "{\"kind\":\"cycle\",\"cycle\":{\"cycle_id\":\"dry\"}}" + Environment.NewLine);
        var beforeStalls = File.ReadAllText(stallsPath);
        var beforeCycles = File.ReadAllText(cyclesPath);

        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--dry-run", "--format", "json"],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("dry-run", document.RootElement.GetProperty("command_mode").GetString());
        Assert.True(document.RootElement.GetProperty("would_change").GetBoolean());
        Assert.Equal(beforeStalls, File.ReadAllText(stallsPath));
        Assert.Equal(beforeCycles, File.ReadAllText(cyclesPath));
        Assert.False(File.Exists(NotifySupervisionStore.ResolveEvidenceDefinitionsPath(artifactRoot, Domain, Team)));
        Assert.False(File.Exists(NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team)));
    }

    [Fact]
    public void ShrinkWrite_RejectsUnknownEvidenceReferenceWithoutChangingAnyStateFile()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var definitionsPath = NotifySupervisionStore.ResolveEvidenceDefinitionsPath(artifactRoot, Domain, Team);
        var auditPath = NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);

        var unresolved = CreateStall("unknown-reference", includeDefinitionEvidence: true) with
        {
            RegistrationDefinition = null,
            EvidenceReference = "constructed-unknown-reference",
            EvidenceReferenceIncludesEvidence = false,
        };
        File.WriteAllText(stallsPath, SerializeOpenStall(unresolved) + Environment.NewLine);
        File.WriteAllText(cyclesPath, SerializeCycleEvent("unreadable-reference-cycle") + Environment.NewLine);
        var beforeStalls = File.ReadAllText(stallsPath);
        var beforeCycles = File.ReadAllText(cyclesPath);

        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("shrink-validation-failed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("unknown supervision evidence", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(beforeStalls, File.ReadAllText(stallsPath));
        Assert.Equal(beforeCycles, File.ReadAllText(cyclesPath));
        Assert.False(File.Exists(definitionsPath));
        Assert.False(File.Exists(auditPath));
        Assert.False(File.Exists(NotifySupervisionStore.ResolveShrinkTransactionPath(artifactRoot, Domain, Team)));
    }

    [Fact]
    public void ShrinkWrite_RejectsMissingEvidenceDefinitionWithoutChangingAnyStateFile()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var definitionsPath = NotifySupervisionStore.ResolveEvidenceDefinitionsPath(artifactRoot, Domain, Team);
        var auditPath = NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);

        var missingDefinition = CreateStall("missing-definition", includeDefinitionEvidence: false) with
        {
            RegistrationDefinition = null,
            Evidence = ["registration_lookup:test-owned counterexample"],
            EvidenceReference = NotifySupervisionStore.HerdrRegistrationEvidenceKey,
            EvidenceReferenceIncludesEvidence = false,
        };
        File.WriteAllText(stallsPath, SerializeOpenStall(missingDefinition) + Environment.NewLine);
        File.WriteAllText(cyclesPath, SerializeCycleEvent("missing-definition-cycle") + Environment.NewLine);
        var beforeStalls = File.ReadAllText(stallsPath);
        var beforeCycles = File.ReadAllText(cyclesPath);

        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("shrink-validation-failed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("unknown supervision evidence", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(beforeStalls, File.ReadAllText(stallsPath));
        Assert.Equal(beforeCycles, File.ReadAllText(cyclesPath));
        Assert.False(File.Exists(definitionsPath));
        Assert.False(File.Exists(auditPath));
    }

    [Fact]
    public void ShrinkWrite_RejectsUnreadableEvidenceManifestWithoutChangingAnyStateFile()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var definitionsPath = NotifySupervisionStore.ResolveEvidenceDefinitionsPath(artifactRoot, Domain, Team);
        var auditPath = NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);

        var storedReference = CreateStall("unreadable-manifest", includeDefinitionEvidence: false) with
        {
            RegistrationDefinition = null,
            Evidence = ["registration_lookup:test-owned counterexample"],
            EvidenceReference = NotifySupervisionStore.HerdrRegistrationEvidenceKey,
            EvidenceReferenceIncludesEvidence = false,
        };
        File.WriteAllText(stallsPath, SerializeOpenStall(storedReference) + Environment.NewLine);
        File.WriteAllText(cyclesPath, SerializeCycleEvent("unreadable-manifest-cycle") + Environment.NewLine);
        File.WriteAllText(
            definitionsPath,
            "{\"schema\":\"intent-cli.supervision-evidence/unknown\",\"definitions\":{}}\n");
        var beforeStalls = File.ReadAllText(stallsPath);
        var beforeCycles = File.ReadAllText(cyclesPath);
        var beforeDefinitions = File.ReadAllText(definitionsPath);

        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            output);

        Assert.Equal(1, exitCode);
        Assert.Contains("shrink-validation-failed", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(beforeStalls, File.ReadAllText(stallsPath));
        Assert.Equal(beforeCycles, File.ReadAllText(cyclesPath));
        Assert.Equal(beforeDefinitions, File.ReadAllText(definitionsPath));
        Assert.False(File.Exists(auditPath));
    }

    [Theory]
    [InlineData("AfterManifestReplacement")]
    [InlineData("AfterStallsReplacement")]
    [InlineData("AfterCyclesReplacement")]
    [InlineData("BeforeAuditAppend")]
    public void ShrinkFaultBoundaries_RecoverReadableStateAndDurableAccounting(string faultName)
    {
        var faultPoint = Enum.Parse<NotifySupervisionShrinkFaultPoint>(faultName, ignoreCase: false);
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var auditPath = NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team);
        var transactionPath = NotifySupervisionStore.ResolveShrinkTransactionPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);
        File.WriteAllText(
            stallsPath,
            string.Join(
                    Environment.NewLine,
                    Enumerable.Range(0, 3).Select(index => SerializeLegacyStall(index)))
                + Environment.NewLine);
        File.WriteAllText(cyclesPath, SerializeCycleEvent("transaction-cycle") + Environment.NewLine);

        NotifySupervisionStore.ShrinkFaultInjector = point =>
        {
            if (point == faultPoint)
            {
                throw new IOException($"fault-injected:{point}");
            }
        };

        using var firstOutput = new StringWriter();
        var firstExitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            firstOutput);
        Assert.Equal(1, firstExitCode);
        Assert.Contains("fault-injected", firstOutput.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(transactionPath), firstOutput.ToString());

        NotifySupervisionStore.ShrinkFaultInjector = null;
        using var recoveryOutput = new StringWriter();
        var recoveryExitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            recoveryOutput);
        Assert.Equal(0, recoveryExitCode);
        Assert.False(File.Exists(transactionPath));

        var read = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(read.Resolved, read.Error);
        Assert.Equal(3, read.StallHistory.Count(record => record.Kind == "g734-legacy-stall"));
        Assert.Empty(read.PromptAudits);
        Assert.NotNull(read.LastCycle);
        var auditLines = File.ReadLines(auditPath).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        Assert.Contains(
            auditLines,
            line => line.Contains("\"outcome\":\"recovered-completed\"", StringComparison.Ordinal));
        Assert.All(
            auditLines,
            line => Assert.Contains("transaction_id", line, StringComparison.Ordinal));
        Console.WriteLine(
            $"G734 transaction recovery: fault={faultPoint}; first_exit={firstExitCode}; recovery_exit={recoveryExitCode}; "
            + $"stalls={read.StallHistory.Count}; cycles=1; audit_outcomes=recovered-completed,completed; "
            + $"journal_removed={!File.Exists(transactionPath)}");
    }

    [Fact]
    public void ShrinkRecovery_AbortsUnexpectedTargetAndAuditsTheAbortedOutcome()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
        var auditPath = NotifySupervisionStore.ResolveShrinkAuditPath(artifactRoot, Domain, Team);
        var transactionPath = NotifySupervisionStore.ResolveShrinkTransactionPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);
        File.WriteAllText(stallsPath, SerializeLegacyStall(1) + Environment.NewLine);
        File.WriteAllText(cyclesPath, SerializeCycleEvent("abort-cycle") + Environment.NewLine);

        NotifySupervisionStore.ShrinkFaultInjector = point =>
        {
            if (point == NotifySupervisionShrinkFaultPoint.AfterManifestReplacement)
            {
                throw new IOException("fault-injected:abort-fixture");
            }
        };
        using var firstOutput = new StringWriter();
        Assert.Equal(
            1,
            NotifySuperviseShrinkCommand.Execute(
                context,
                ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
                firstOutput));
        NotifySupervisionStore.ShrinkFaultInjector = null;

        // A valid external append after the simulated crash is an unexpected
        // target hash. Recovery must not overwrite it or silently claim the
        // original transaction completed.
        File.AppendAllText(cyclesPath, SerializeCycleEvent("external-cycle") + Environment.NewLine);
        using var recoveryOutput = new StringWriter();
        var recoveryExitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            recoveryOutput);

        Assert.Equal(1, recoveryExitCode);
        Assert.Contains("shrink-recovery-aborted", recoveryOutput.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(transactionPath));
        var read = NotifySupervisionStore.Read(artifactRoot, Domain, Team);
        Assert.True(read.Resolved, read.Error);
        Assert.Single(read.StallHistory);
        Assert.Equal(2, File.ReadLines(cyclesPath).Count());
        Assert.Contains(
            File.ReadLines(auditPath),
            line => line.Contains("\"outcome\":\"aborted\"", StringComparison.Ordinal));
        Console.WriteLine(
            $"G734 transaction recovery: fault=external-target-conflict; exit={recoveryExitCode}; "
            + "audit_outcome=aborted; retained_stalls=1; retained_cycles=2");
    }

    [Fact]
    public void ClockFallbackWriterIdentityUsesExplicitSameHostPidLiveness()
    {
        var current = NotifySupervisionWriterIdentity.Current();
        var fallback = current with
        {
            ProcessStartTime = DateTimeOffset.UtcNow,
            ProcessStartTimeSource = "clock-fallback",
        };

        Assert.True(fallback.IsLiveOn(current));
    }

    [Fact]
    public void ProcessStartTimeResolutionToleranceDoesNotAcceptAgedPidReuse()
    {
        var current = NotifySupervisionWriterIdentity.Current();
        var near = current with { ProcessStartTime = current.ProcessStartTime.AddMilliseconds(50) };
        var far = current with { ProcessStartTime = current.ProcessStartTime.AddSeconds(1) };

        Assert.True(near.IsLiveOn(current));
        Assert.False(far.IsLiveOn(current));
    }

    [Fact]
    public void DensityReport_MeasuresTenThousandRecordsAgainstTheIssueBaseline()
    {
        var context = CreateContext();
        var artifactRoot = context.ResolveSupervisionArtifactRootPath();
        var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);
        const int recordCount = 10_063;
        File.WriteAllText(
            stallsPath,
            string.Join(
                    Environment.NewLine,
                    Enumerable.Range(0, recordCount).Select(index => SerializeLegacyStall(index, payloadLength: 4_100)))
                + Environment.NewLine);

        using var output = new StringWriter();
        var exitCode = NotifySuperviseShrinkCommand.Execute(
            context,
            ["--domain", Domain, "--team", Team, "--write", "--format", "json"],
            output);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement;
        var beforeAverage = result.GetProperty("before_average_bytes_per_record").GetDouble();
        var afterAverage = result.GetProperty("after_average_bytes_per_record").GetDouble();
        Assert.Equal(recordCount, result.GetProperty("before_record_count").GetInt32());
        Assert.Equal(recordCount, result.GetProperty("after_record_count").GetInt32());
        Assert.True(beforeAverage > 4_970, $"fixture should exercise the issue baseline; measured {beforeAverage}");
        Assert.True(afterAverage < beforeAverage);
        Assert.True(result.GetProperty("invariant_text").GetProperty("other_record_bytes_saved").GetInt64() == 0);
        Console.WriteLine($"G734 density: before_bytes={result.GetProperty("before_bytes").GetInt64()}; after_bytes={result.GetProperty("after_bytes").GetInt64()}; records={recordCount}; before_average={beforeAverage:F2}; after_average={afterAverage:F2}; baseline=4970; invariant={result.GetProperty("invariant_text")}");
    }

    [Fact]
    public async Task ShrinkWrite_ReportsRunningExternalSupervisorAndNextCycleAppends()
    {
        var liveRoot = Directory.CreateTempSubdirectory("notify-g734-live-").FullName;
        Process? supervisor = null;
        Task<string>? supervisorOutput = null;
        Task<string>? supervisorError = null;
        try
        {
            var artifactRoot = Path.Combine(liveRoot, ".intent-cli", "supervision");
            var stallsPath = NotifySupervisionStore.ResolveStallPath(artifactRoot, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(stallsPath)!);
            const int recordCount = 10_063;
            File.WriteAllText(
                stallsPath,
                string.Join(
                        Environment.NewLine,
                        Enumerable.Range(0, recordCount).Select(index => SerializeLegacyStall(index)))
                    + Environment.NewLine);

            var agmsg = Path.Combine(liveRoot, "agmsg");
            Directory.CreateDirectory(agmsg);
            var teamScript = Path.Combine(agmsg, "team.sh");
            File.WriteAllText(teamScript, "#!/bin/sh\nexit 0\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(teamScript, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var cliDll = Path.Combine(
                RepoVersionPolicySource.RepoRoot(),
                "src",
                "IntentSystem.Cli",
                "bin",
                "Release",
                "net10.0",
                "IntentSystem.Cli.dll");
            Assert.True(File.Exists(cliDll), $"missing built CLI: {cliDll}");
            supervisor = StartCli(
                liveRoot,
                cliDll,
                agmsg,
                "notify", "supervise", "--domain", Domain, "--team", Team,
                "--interval", "1", "--write", "--format", "json");
            supervisorOutput = supervisor.StandardOutput.ReadToEndAsync();
            supervisorError = supervisor.StandardError.ReadToEndAsync();

            var cyclesPath = NotifySupervisionStore.ResolveCyclePath(artifactRoot, Domain, Team);
            await WaitUntilAsync(
                () => File.Exists(cyclesPath) && File.ReadLines(cyclesPath).Any(),
                TimeSpan.FromSeconds(10));
            var cycleCountBeforeShrink = File.ReadLines(cyclesPath).Count();

            using var shrink = StartCli(
                liveRoot,
                cliDll,
                agmsg,
                "notify", "supervise", "shrink", "--domain", Domain, "--team", Team,
                "--write", "--format", "json");
            var shrinkOutput = await shrink.StandardOutput.ReadToEndAsync();
            var shrinkError = await shrink.StandardError.ReadToEndAsync();
            await shrink.WaitForExitAsync();
            Assert.True(shrink.ExitCode == 0, shrinkOutput + shrinkError);
            using var shrinkJson = JsonDocument.Parse(shrinkOutput);
            var rootElement = shrinkJson.RootElement;
            var supervisorProcessState = supervisor.HasExited
                ? $"exited:{supervisor.ExitCode}"
                : "running";
            var supervisorStdout = supervisorOutput?.IsCompleted == true
                ? await supervisorOutput
                : "<not-complete>";
            var supervisorStderr = supervisorError?.IsCompleted == true
                ? await supervisorError
                : "<not-complete>";
            Assert.True(
                string.Equals(rootElement.GetProperty("supervisor_state").GetString(), "running", StringComparison.Ordinal),
                $"supervisor_process={supervisorProcessState}; stdout={supervisorStdout}; stderr={supervisorStderr}; shrink={shrinkOutput}");
            Assert.True(rootElement.GetProperty("before_record_count").GetInt32() >= recordCount);
            Assert.Equal(rootElement.GetProperty("before_record_count").GetInt32(), rootElement.GetProperty("after_record_count").GetInt32());
            Assert.True(rootElement.GetProperty("after_bytes").GetInt64() < rootElement.GetProperty("before_bytes").GetInt64());

            await WaitUntilAsync(
                () => File.ReadLines(cyclesPath).Count() > cycleCountBeforeShrink,
                TimeSpan.FromSeconds(10));
            var cycleCountAfterShrink = File.ReadLines(cyclesPath).Count();
            var rawStalls = File.ReadAllText(stallsPath);
            Assert.DoesNotContain(NotifySupervisionStore.HerdrRegistrationDefinition, rawStalls, StringComparison.Ordinal);
            Assert.Contains("evidence_ref", rawStalls, StringComparison.Ordinal);
            Console.WriteLine($"G734 live supervisor shrink: state=running; cycles={cycleCountBeforeShrink}->{cycleCountAfterShrink}; shrink={shrinkOutput.Trim()}");
            supervisor.Kill(entireProcessTree: true);
            await supervisor.WaitForExitAsync();
            _ = await supervisorOutput!;
            _ = await supervisorError!;
        }
        finally
        {
            if (supervisor is { HasExited: false })
            {
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync();
            }

            if (Directory.Exists(liveRoot))
            {
                Directory.Delete(liveRoot, recursive: true);
            }
        }
    }

    private string SerializeLegacyStall(int index, int payloadLength = 0)
    {
        var record = CreateStall($"legacy-{index}", includeDefinitionEvidence: true) with
        {
            Key = $"legacy:{index}",
            Kind = "g734-legacy-stall",
            SurfacedAt = FixtureEpoch.AddSeconds(index),
            Summary = payloadLength == 0
                ? "A retained legacy stall with a dynamic registration observation."
                : new string('x', payloadLength),
            RegistrationLookup = $"legacy lookup {index}",
        };
        return JsonSerializer.Serialize(
            new NotifySupervisionEvent { Kind = "open", Stall = record },
            JsonOptions);
    }

    private static string SerializeOpenStall(NotifySupervisionStallRecord record) =>
        JsonSerializer.Serialize(
            new NotifySupervisionEvent { Kind = "open", Stall = record },
            JsonOptions);

    private static string SerializeCycleEvent(string cycleId) =>
        JsonSerializer.Serialize(
            new NotifySupervisionEvent
            {
                Kind = "cycle",
                Cycle = new NotifySupervisionCycle
                {
                    CycleId = cycleId,
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Writer = NotifySupervisionWriterIdentity.Current(),
                    IntervalSeconds = 1,
                },
            },
            JsonOptions);

    private static NotifySupervisionStallRecord CreateStall(string key, bool includeDefinitionEvidence) => new()
    {
        Key = key,
        Kind = "missing-cycle-probe",
        OwnerRole = "orchestration",
        SubjectRole = "implementation",
        Source = "g734-test",
        Summary = "A retained stall with readable evidence.",
        SurfacedAt = DateTimeOffset.UtcNow,
        RegistrationDefinition = NotifySupervisionStore.HerdrRegistrationDefinition,
        RegistrationLookup = "herdr agent list matched a test-owned workspace and pane",
        RegistrationResult = "registration-missing; foreground-processes-absent",
        Evidence = includeDefinitionEvidence
            ?
            [
                $"registration_definition:{NotifySupervisionStore.HerdrRegistrationDefinition}",
                "registration_lookup:test-owned workspace/pane",
                "registration_result:registration-missing; foreground-processes-absent",
            ]
            : null,
        ConsultedObservations = ["test-owned observation: foreground_processes=0"],
    };

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
        },
    };

    private static Process StartCli(
        string workingDirectory,
        string cliDll,
        string agmsg,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(cliDll);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment[NotifyTransportPaths.AgmsgScriptsEnvironmentVariable] = agmsg;
        var process = Process.Start(startInfo);
        Assert.NotNull(process);
        return process!;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(predicate(), "Timed out waiting for the live supervision fixture.");
    }
}
