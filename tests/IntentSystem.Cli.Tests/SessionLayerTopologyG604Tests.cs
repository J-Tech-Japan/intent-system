using System.Diagnostics;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG604Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-topology-g604-").FullName;

    [Fact]
    public void Record_TwoTeamsUseIndependentMachineLocalIgnoredFiles_G604()
    {
        RunGit("init", "-q");

        Assert.True(Record("intent-cli-dev", "orchestration", "w1:p1").Applied);
        Assert.True(Record("intent-cli-review", "review", "w2:p1").Applied);

        var first = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev");
        var second = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-review");
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.NotEqual(File.ReadAllText(first), File.ReadAllText(second));
        Assert.True(NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev").Resolved);
        Assert.True(NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-review").Resolved);
        Assert.True(File.Exists(NotifyRoleTopologyStore.ResolveLocalIgnorePath(root)));
        Assert.False(File.Exists(Path.Combine(root, ".gitignore")));
        Assert.Equal(string.Empty, RunGit("status", "--porcelain").Trim());
    }

    [Fact]
    public void Resolve_CopiedNewRecordAndDualLocationConflictFailClosed_G604()
    {
        Assert.True(Record("intent-cli-dev", "orchestration", "w1:p1").Applied);
        var source = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev");
        var copied = NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-review");
        Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
        File.Copy(source, copied);

        var copiedResolution = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-review");
        Assert.False(copiedResolution.Resolved);
        Assert.Equal("topology-identity-mismatch", copiedResolution.Cause);
        Assert.Contains("intent-cli-dev", copiedResolution.Summary, StringComparison.Ordinal);
        Assert.Contains("intent-cli-review", copiedResolution.Summary, StringComparison.Ordinal);

        File.Delete(copied);
        File.WriteAllText(NotifyRoleTopologyStore.ResolvePath(root),
            """
            { "team": "intent-cli-dev", "workspace_id": "other", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "other", "pane_id": "other:p1" }
            }}
            """);
        var conflict = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev");
        Assert.False(conflict.Resolved);
        Assert.Equal("topology-location-conflict", conflict.Cause);
        Assert.Contains(NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev"), conflict.Summary, StringComparison.Ordinal);
        Assert.Contains(NotifyRoleTopologyStore.ResolvePath(root), conflict.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_LegacyOnlyWarnsAndModeOnlyPreflightIsConfigurationIncomplete_G604()
    {
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath,
            """
            { "team": "intent-cli-dev", "workspace_id": "w1", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "w1", "pane_id": "w1:p1" }
            }}
            """);
        var compatibility = NotifyRoleTopologyStore.Resolve(root, Domain, "intent-cli-dev");
        Assert.True(compatibility.Resolved);
        Assert.Contains(compatibility.Warnings, warning => warning.Contains("topology record", StringComparison.Ordinal));

        File.Delete(legacyPath);
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
        };
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--mode", "herdr-only", "--write", "--format", "json"], writer));
        var preflight = SessionLayerPreflight.Analyze(root, Domain, "intent-cli-dev");
        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, preflight.Verdict);
        var missing = Assert.Single(preflight.Scopes.Single().Findings, finding => finding.Cause == "topology-missing");
        Assert.Contains("topology record", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TopologyCommands_RequireExplicitNonDefaultDomainForRecordAndValidate_G604()
    {
        var context = CreateContext("sekiban-as-a-service");
        using var recordWriter = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecord(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--role", "orchestration", "--resident", "herdr",
                "--workspace-id", "w1", "--pane-id", "w1:p1", "--cwd", "/machine-local", "--write", "--format", "json"],
            recordWriter));

        Assert.True(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, Domain, "intent-cli-dev")));
        Assert.False(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, "sekiban-as-a-service", "intent-cli-dev")));

        using var validateWriter = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteValidate(context,
            ["--domain", Domain, "--team", "intent-cli-dev", "--format", "json"], validateWriter));
        using var validation = JsonDocument.Parse(validateWriter.ToString());
        Assert.True(validation.RootElement.GetProperty("valid").GetBoolean());
        Assert.Contains($"/{Domain}/intent-cli-dev.json", validation.RootElement.GetProperty("record_path").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Record_HerdrRecipeMayDeclareFileBackedEnvelopeDelivery_G619()
    {
        const string team = "intent-cli-dev";
        var context = CreateContext(Domain);
        using var writer = new StringWriter();

        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecord(context,
            ["--domain", Domain, "--team", team, "--role", "implementation", "--resident", "herdr",
                "--workspace-id", "w1", "--pane-id", "w1:p2", "--cwd", "/machine-local", "--kind", "copilot",
                "--delivery-method", "file-backed", "--write", "--format", "json"], writer));

        var resolution = NotifyRoleTopologyStore.Resolve(root, Domain, team);
        Assert.True(resolution.Resolved);
        Assert.Equal("file-backed", resolution.Topology!.Roles["implementation"].DeliveryMethod);
        using var record = JsonDocument.Parse(File.ReadAllText(NotifyRoleTopologyStore.ResolvePath(root, Domain, team)));
        Assert.Equal("file-backed", record.RootElement.GetProperty("roles").GetProperty("implementation")
            .GetProperty("delivery_method").GetString());
    }

    [Fact]
    public void DualLocationConflict_BlocksValidatePreflightAndAutomationDoctor_G604()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "orchestration", "w1:p1").Applied);
        var legacyPath = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
        File.WriteAllText(legacyPath,
            """
            { "team": "intent-cli-dev", "workspace_id": "other", "roles": {
                "orchestration": { "resident": "herdr", "workspace_id": "other", "pane_id": "other:p1" }
            }}
            """);

        var context = CreateContext(Domain);
        using var modeWriter = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(context,
            ["--domain", Domain, "--team", team, "--mode", "herdr-only", "--write", "--format", "json"], modeWriter));
        using var validateWriter = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteValidate(context,
            ["--domain", Domain, "--team", team, "--format", "json"], validateWriter));
        using (var validate = JsonDocument.Parse(validateWriter.ToString()))
        {
            Assert.Contains(validate.RootElement.GetProperty("findings").EnumerateArray(), finding =>
                finding.GetProperty("cause").GetString() == "topology-location-conflict");
        }

        var preflight = SessionLayerPreflight.Analyze(root, Domain, team);
        Assert.False(preflight.Ready);
        Assert.Contains(preflight.Scopes.Single().Findings, finding => finding.Cause == "topology-location-conflict");

        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;
        try
        {
            using var doctorWriter = new StringWriter();
            Assert.Equal(1, AutomationDoctorCommand.Execute(context, ["--format", "json"], doctorWriter));
            using var doctor = JsonDocument.Parse(doctorWriter.ToString());
            var findings = doctor.RootElement.GetProperty("topology_health").GetProperty("teams")[0]
                .GetProperty("findings").EnumerateArray();
            Assert.Contains(findings, finding => finding.GetProperty("cause").GetString() == "topology-location-conflict");
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
        }
    }

    [Fact]
    public void UpdateKind_RequiresMatchConfirmationAndOnlyChangesKind_G614()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var missingConfirmation = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--write"], missingConfirmation));
        using var mismatch = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "claude", "--new-kind", "copilot", "--confirm-update-kind", "--write"], mismatch));
        Assert.Equal(before, File.ReadAllText(path));

        using var updated = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--confirm-update-kind", "--write"], updated));
        using var beforeJson = JsonDocument.Parse(before);
        using var afterJson = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("copilot", afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("kind").GetString());
        Assert.Equal(beforeJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("pane_id").GetString(), afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("pane_id").GetString());
        Assert.Equal(beforeJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("cwd").GetString(), afterJson.RootElement.GetProperty("roles").GetProperty("review").GetProperty("cwd").GetString());
    }

    [Fact]
    public void UpdateField_DeclaresOnlyAllowedAbsentDeliveryMethodAndPreservesSiblingBytes_G620()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var missingConfirmation = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "file-backed", "--write"], missingConfirmation));
        Assert.Equal(before, File.ReadAllText(path));

        using var output = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "file-backed", "--confirm-update-field", "--write"], output));

        using var beforeJson = JsonDocument.Parse(before);
        using var afterJson = JsonDocument.Parse(File.ReadAllText(path));
        var beforeRole = beforeJson.RootElement.GetProperty("roles").GetProperty("review");
        var afterRole = afterJson.RootElement.GetProperty("roles").GetProperty("review");
        Assert.Equal("file-backed", afterRole.GetProperty("delivery_method").GetString());
        foreach (var sibling in beforeRole.EnumerateObject())
            Assert.Equal(sibling.Value.GetRawText(), afterRole.GetProperty(sibling.Name).GetRawText());

        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("delivery_method", result.RootElement.GetProperty("field").GetString());
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.False(result.RootElement.GetProperty("conflict").GetBoolean());

        var strictRecord = SessionLayerTopologyWriter.Record(root, new SessionLayerTopologyRecordRequest
        {
            Domain = Domain, Team = team, Role = "review", Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "w1", PaneId = "w1:p1", Cwd = "/machine-local", Kind = "codex",
            DeliveryMethod = "inline", Write = true, Format = "json",
        });
        Assert.True(strictRecord.Conflict);
        Assert.Contains("conflict", strictRecord.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateField_RefusesBothStaleCurrentDirectionsAndUnregisteredNames_G620()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var valueWhenAbsent = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "inline", "--new", "file-backed", "--confirm-update-field", "--write"], valueWhenAbsent));
        Assert.Contains("absent", valueWhenAbsent.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));

        using var declare = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "inline", "--confirm-update-field", "--write"], declare));
        var withDeclaredValue = File.ReadAllText(path);

        using var absentWhenValue = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "file-backed", "--confirm-update-field", "--write"], absentWhenValue));
        Assert.Contains("inline", absentWhenValue.ToString(), StringComparison.Ordinal);
        Assert.Equal(withDeclaredValue, File.ReadAllText(path));

        foreach (var field in new[] { "kind", "roles.review.delivery_method" })
        {
            using var unregistered = new StringWriter();
            Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateField(context,
                ["--domain", Domain, "--team", team, "--role", "review", "--field", field, "--current", "absent", "--new", "file-backed", "--confirm-update-field", "--write"], unregistered));
            using var refusal = JsonDocument.Parse(unregistered.ToString());
            Assert.Equal(field, refusal.RootElement.GetProperty("field").GetString());
            Assert.Contains("registry", refusal.RootElement.GetProperty("summary").GetString(), StringComparison.Ordinal);
            Assert.Equal(withDeclaredValue, File.ReadAllText(path));
        }
    }

    [Theory]
    [InlineData("--dry-run", "--write")]
    [InlineData("--write", "--dry-run")]
    public void UpdateField_DryRunAlwaysWinsAndUsesTheSameAbsentComparison_G620(string firstModeFlag, string secondModeFlag)
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var output = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "file-backed", "--confirm-update-field", firstModeFlag, secondModeFlag], output));

        Assert.Equal(before, File.ReadAllText(path));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("dry-run", result.RootElement.GetProperty("mode").GetString());
        Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.True(result.RootElement.GetProperty("changed").GetBoolean());
    }

    [Theory]
    [InlineData("--dry-run", "--write")]
    [InlineData("--write", "--dry-run")]
    public void UpdateKind_DryRunAlwaysWinsOverWriteAndPreservesRecord_G614(string firstModeFlag, string secondModeFlag)
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var output = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--confirm-update-kind", firstModeFlag, secondModeFlag], output));

        Assert.Equal(before, File.ReadAllText(path));
        using var result = JsonDocument.Parse(output.ToString());
        Assert.Equal("dry-run", result.RootElement.GetProperty("mode").GetString());
        Assert.False(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.True(result.RootElement.GetProperty("changed").GetBoolean());
    }

    [Fact]
    public void NewTopologyMutations_OnlyAdvertiseAndAcceptJsonFormat_G614()
    {
        const string team = "intent-cli-dev";
        Assert.True(Record(team, "review", "w1:p1").Applied);
        var context = CreateContext(Domain);
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, team);
        var before = File.ReadAllText(path);

        using var updateMarkdown = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateKind(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--current-kind", "codex", "--new-kind", "copilot", "--confirm-update-kind", "--dry-run", "--format", "markdown"], updateMarkdown));
        Assert.Contains("only '--format json'", updateMarkdown.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));

        using var updateFieldMarkdown = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteUpdateField(context,
            ["--domain", Domain, "--team", team, "--role", "review", "--field", "delivery_method", "--current", "absent", "--new", "file-backed", "--confirm-update-field", "--dry-run", "--format", "markdown"], updateFieldMarkdown));
        Assert.Contains("only '--format json'", updateFieldMarkdown.ToString(), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));

        using var retireMarkdown = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--confirm-retire-legacy", "--write", "--format", "markdown"], retireMarkdown));
        Assert.Contains("only '--format json'", retireMarkdown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void G614_DocumentationAndRenderedGuideKeepAgentKindsNeutral_G614()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repo, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repo, "docs", "ja", "12-agent-message-orchestration.md"));
        var guide = HerdrOnlyOperatingGuide.RenderMarkdown([]);

        foreach (var content in new[] { english, japanese })
        {
            Assert.Contains("pane move", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("legacy-topology-retirements.jsonl", content, StringComparison.Ordinal);
            Assert.Contains("implementation", content, StringComparison.Ordinal);
            Assert.DoesNotContain("orchestrator = Claude", content, StringComparison.Ordinal);
            Assert.DoesNotContain("reviewer = Codex", content, StringComparison.Ordinal);
        }

        Assert.Contains("Same-tab `herdr pane move` is unsupported", guide, StringComparison.Ordinal);
        Assert.Contains("legacy-topology-retirements.jsonl", guide, StringComparison.Ordinal);
        Assert.Contains("Logical role defaults are `implementation`", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void RetireLegacy_RequiresCurrentRecordConfirmationAndEvidence_G614()
    {
        const string team = "intent-cli-dev";
        var context = CreateContext(Domain);
        using var missing = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--confirm-retire-legacy", "--write"], missing));

        Assert.True(Record(team, "review", "w1:p1").Applied);
        var legacy = NotifyRoleTopologyStore.ResolvePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "{ \"team\": \"intent-cli-dev\", \"workspace_id\": \"w1\", \"roles\": {} }");
        using var noConfirmation = new StringWriter();
        Assert.Equal(1, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--write"], noConfirmation));
        Assert.True(File.Exists(legacy));
        using var retired = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRetireLegacy(context,
            ["--domain", Domain, "--team", team, "--evidence", "fleet:zero4racer", "--confirm-retire-legacy", "--write"], retired));
        Assert.False(File.Exists(legacy));
        var evidencePath = SessionLayerTopologyRetirementEvidence.ResolvePath(root);
        Assert.Equal(Path.Combine(root, ".intent-cli", "legacy-topology-retirements.jsonl"), evidencePath);
        Assert.DoesNotContain(Path.DirectorySeparatorChar + "topology" + Path.DirectorySeparatorChar, evidencePath, StringComparison.Ordinal);
        using var evidence = JsonDocument.Parse(File.ReadAllText(evidencePath));
        Assert.Equal("fleet:zero4racer", evidence.RootElement.GetProperty("evidence").GetString());
        Assert.Equal(Environment.MachineName, evidence.RootElement.GetProperty("host").GetString());
        Assert.True(evidence.RootElement.GetProperty("timestamp_utc").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
    }

    private CliContext CreateContext(string defaultDomain) => new()
    {
        RepoRoot = root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = defaultDomain, ArtifactRoot = ".intent-cli" } },
    };

    private SessionLayerTopologyRecordResult Record(string team, string role, string paneId) =>
        SessionLayerTopologyWriter.Record(root, new SessionLayerTopologyRecordRequest
        {
            Domain = Domain,
            Team = team,
            Role = role,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = paneId[..paneId.IndexOf(':', StringComparison.Ordinal)],
            PaneId = paneId,
            Cwd = "/machine-local",
            Kind = "codex",
            Write = true,
            Format = "json",
        });

    private string RunGit(params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Arguments = string.Join(' ', arguments),
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
