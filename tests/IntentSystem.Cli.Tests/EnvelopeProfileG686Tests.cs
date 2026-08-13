using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class EnvelopeProfileG686Tests : IDisposable
{
    private const string Domain = "g686-domain";
    private const string Team = "g686-team";
    private readonly string root = Directory.CreateTempSubdirectory("intent-g686-").FullName;

    [Fact]
    public void RecordProfile_UsesDedicatedConfirmedDigestCasAndRoleBinding()
    {
        var context = CreateContext();
        RecordRole(context, "orchestration", "wG686:p1", "/registry");

        var first = RecordProfile(context, "absent", "orchestration");
        Assert.True(first.GetProperty("applied").GetBoolean(), first.GetProperty("summary").GetString());
        Assert.True(first.GetProperty("changed").GetBoolean());
        var digest = first.GetProperty("digest").GetString();
        Assert.False(string.IsNullOrWhiteSpace(digest));

        using (var overrideWriter = new StringWriter())
        {
            Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecordProfile(
                context,
                ProfileArguments(digest!, "orchestration", roleOverride: true),
                overrideWriter));
        }

        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        using (var document = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var profile = document.RootElement.GetProperty("envelope_profiles").GetProperty("codex-operator");
            Assert.Equal("codex", profile.GetProperty("kind").GetString());
            Assert.Equal("/profile", profile.GetProperty("writable_roots")[0].GetString());
            Assert.Equal("codex-operator", document.RootElement.GetProperty("roles")
                .GetProperty("orchestration").GetProperty("envelope_profile_override").GetProperty("name").GetString());
        }

        using var staleWriter = new StringWriter();
        var staleExit = SessionLayerTopologyCommand.ExecuteRecordProfile(
            context,
            ProfileArguments("deadbeefdeadbeef", "orchestration", roleOverride: false),
            staleWriter);
        Assert.Equal(1, staleExit);
        using var stale = JsonDocument.Parse(staleWriter.ToString());
        Assert.True(stale.RootElement.GetProperty("conflict").GetBoolean());
        Assert.Contains("stale CAS", stale.RootElement.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);

        using var updateWriter = new StringWriter();
        var updateExit = SessionLayerTopologyCommand.ExecuteUpdateField(
            context,
            ["--domain", Domain, "--team", Team, "--role", "orchestration", "--field", "envelope_profile",
                "--current", "absent", "--new", "other", "--confirm-update-field", "--dry-run", "--format", "json"],
            updateWriter);
        Assert.Equal(1, updateExit);
        Assert.Contains("not in the topology update registry", updateWriter.ToString(), StringComparison.Ordinal);

        var resolved = NotifyRoleTopologyStore.Resolve(root, Domain, Team);
        Assert.True(resolved.Resolved, resolved.Summary);
        Assert.Equal(digest, resolved.Topology!.EnvelopeProfiles["codex-operator"].Digest);
        Assert.Null(resolved.Topology.Roles["orchestration"].EnvelopeProfileReference);
        Assert.Equal("codex-operator", resolved.Topology.Roles["orchestration"].EnvelopeProfileOverride!.Name);
    }

    [Fact]
    public void ProfileComparator_UsesRecordedEnvelopeAndLeavesWishFieldsOut()
    {
        var profile = Profile("codex-operator", "/profile");
        var process = Process(
            "/usr/local/bin/codex", "--model", "human-choice", "-c", "model_reasoning_effort=high",
            "--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/profile");

        var result = AgentLaunchShapeComparer.Compare("codex", profile, [process]);

        Assert.True(result.Resolved);
        Assert.True(result.Conforming, result.Summary);
        Assert.Equal(AgentLaunchEnvelopeDrift.None, result.Drift);
        Assert.Contains("model and reasoning effort are excluded", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Supervision_ProfilePrecedenceClearsRegistryDriftWhileUnprofiledSeatStillDrifts()
    {
        var context = CreateContext();
        WriteTopology(
            profile: Profile("codex-operator", "/profile"),
            profileReference: "codex-operator",
            includeBrokenRegistrySeat: true);

        var runner = new ProfileRunner();
        var pass = CreateSupervisor(context, runner, write: false).RunOnce();

        Assert.DoesNotContain(pass.Findings, item => item.Kind == "recipe-drift" && item.SubjectRole == "orchestration");
        var drift = Assert.Single(pass.Findings, item => item.Kind == "recipe-drift");
        Assert.Equal("review", drift.SubjectRole);
        Assert.Contains("recorded 'codex' recipe", drift.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-keys"));

        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        var withoutReference = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        withoutReference["roles"]!["orchestration"]!["envelope_profile"] = null;
        File.WriteAllText(path, withoutReference.ToJsonString());
        var registryPass = CreateSupervisor(context, new ProfileRunner(), write: false).RunOnce();
        Assert.Contains(registryPass.Findings, item => item.Kind == "recipe-drift" && item.SubjectRole == "orchestration");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    public void Supervision_InvalidProfileIsDistinctAndNeverFallsBack(string invalidShape)
    {
        var context = CreateContext();
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var role = new JsonObject
        {
            ["resident"] = "herdr",
            ["workspace_id"] = "wG686",
            ["pane_id"] = "wG686:p1",
            ["kind"] = "codex",
            ["cwd"] = "/registry",
            ["launch_args"] = new JsonArray("--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/registry"),
            ["envelope_profile"] = "missing-profile",
        };
        var profiles = new JsonObject();
        if (invalidShape == "mismatch")
        {
            var profile = AgentLaunchEnvelopeProfileCodec.WithDigest(Profile("missing-profile", "/profile") with { Kind = "copilot" });
            profiles[profile.Name] = AgentLaunchEnvelopeProfileCodec.ToJsonObject(profile);
        }
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = "wG686",
            ["envelope_profiles"] = profiles,
            ["roles"] = new JsonObject { ["orchestration"] = role },
        };
        File.WriteAllText(path, topology.ToJsonString());

        var resolution = NotifyRoleTopologyStore.Resolve(root, Domain, Team);
        Assert.False(resolution.Resolved);
        Assert.Equal("profile-invalid", resolution.Cause);

        var runner = new ProfileRunner();
        var pass = CreateSupervisor(context, runner, write: false).RunOnce();
        var finding = Assert.Single(pass.Findings, item => item.Kind == "profile-invalid");
        Assert.Equal("profile-invalid", finding.Cause);
        Assert.Contains("No registry fallback", finding.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["agent", "list"]));
    }

    [Theory]
    [InlineData("override-scalar")]
    [InlineData("override-array")]
    [InlineData("override-malformed-object")]
    [InlineData("profile-array-scalar")]
    [InlineData("profile-map-scalar")]
    public void Supervision_MalformedProfileShapesBecomeDistinctProfileInvalidFindings(string invalidShape)
    {
        var context = CreateContext();
        WriteMalformedTopology(invalidShape);

        var resolution = NotifyRoleTopologyStore.Resolve(root, Domain, Team);
        Assert.False(resolution.Resolved);
        Assert.Equal("profile-invalid", resolution.Cause);

        var runner = new ProfileRunner();
        var pass = CreateSupervisor(context, runner, write: false).RunOnce();
        var finding = Assert.Single(pass.Findings, item => item.Kind == "profile-invalid");
        Assert.Equal("profile-invalid", finding.Cause);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.SequenceEqual(["agent", "list"]));
    }

    [Fact]
    public void NoProfilePreservesRegistryComparatorAndTopLevelProfileIsNotParsedAsRole()
    {
        var profile = AgentLaunchEnvelopeProfileCodec.WithDigest(Profile("unreferenced", "/profile"));
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = "wG686",
            ["envelope_profiles"] = new JsonObject { [profile.Name] = AgentLaunchEnvelopeProfileCodec.ToJsonObject(profile) },
            ["roles"] = new JsonObject
            {
                ["orchestration"] = new JsonObject
                {
                    ["resident"] = "herdr",
                    ["workspace_id"] = "wG686",
                    ["pane_id"] = "wG686:p1",
                    ["kind"] = "codex",
                },
            },
        };
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, topology.ToJsonString());

        var resolved = NotifyRoleTopologyStore.Resolve(root, Domain, Team);
        Assert.True(resolved.Resolved, resolved.Summary);
        Assert.Single(resolved.Topology!.Roles);
        Assert.Single(resolved.Topology.EnvelopeProfiles);
        Assert.Null(resolved.Topology.Roles["orchestration"].EnvelopeProfileReference);

        var recipe = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        var expected = AgentLaunchShapeComparer.Compare("codex", recipe,
            [Process("/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/work")],
            ["--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/work"], "/work");
        Assert.True(expected.Conforming, expected.Summary);
    }

    [Fact]
    public void EnglishJapaneseDocsAndLedgerDeclareProfilePrecedenceAndInvalidFinding()
    {
        foreach (var path in new[] { "docs/en/08-command-reference.md", "docs/ja/08-command-reference.md" })
        {
            var text = ReadRepoFile(path);
            Assert.Contains("record-profile", text, StringComparison.Ordinal);
            Assert.Contains("current-digest", text, StringComparison.Ordinal);
            Assert.Contains("profile-invalid", text, StringComparison.Ordinal);
            Assert.Contains("byte", text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var path in new[] { "docs/en/12-agent-message-orchestration.md", "docs/ja/12-agent-message-orchestration.md" })
        {
            var text = ReadRepoFile(path);
            Assert.Contains("G686", text, StringComparison.Ordinal);
            Assert.Contains("profile-invalid", text, StringComparison.Ordinal);
            Assert.Contains("observed argv", text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var path in new[] { "docs/en/1.0-compatibility-ledger.md", "docs/ja/1.0-compatibility-ledger.md" })
        {
            var text = ReadRepoFile(path);
            Assert.Contains("G686", text, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", text, StringComparison.Ordinal);
            Assert.Contains("profile-invalid", text, StringComparison.Ordinal);
        }
    }

    private JsonElement RecordProfile(CliContext context, string currentDigest, string role)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecordProfile(
            context,
            ProfileArguments(currentDigest, role, roleOverride: false),
            writer));
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    private string[] ProfileArguments(string currentDigest, string? role, bool roleOverride) =>
    new[]
    {
        "--domain", Domain, "--team", Team, "--profile-name", "codex-operator", "--kind", "codex",
        "--sandbox-mode", "workspace-write", "--approval-mode", "never", "--roots-policy", "exact",
        "--writable-root", "/profile", "--network-access", "disabled", "--transport-mode", "herdr-only",
        "--evidence", "G686 measured operator choice", "--recorded-at", "2026-08-13T10:00:00Z",
        "--current-digest", currentDigest, "--confirm-record-profile", "--write", "--format", "json",
    }.Concat(role is null ? Array.Empty<string>() : new[] { "--role", role })
        .Concat(roleOverride ? new[] { "--role-override" } : Array.Empty<string>())
        .ToArray();

    private void RecordRole(CliContext context, string role, string pane, string cwd)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerTopologyCommand.ExecuteRecord(context,
            ["--domain", Domain, "--team", Team, "--role", role, "--resident", "herdr", "--workspace-id", "wG686",
                "--pane-id", pane, "--cwd", cwd, "--kind", "codex", "--write", "--format", "json"], writer));
    }

    private void WriteTopology(AgentLaunchEnvelopeProfile profile, string profileReference, bool includeBrokenRegistrySeat)
    {
        var profiles = new JsonObject { [profile.Name] = AgentLaunchEnvelopeProfileCodec.ToJsonObject(AgentLaunchEnvelopeProfileCodec.WithDigest(profile)) };
        var roles = new JsonObject
        {
            ["orchestration"] = new JsonObject
            {
                ["resident"] = "herdr", ["workspace_id"] = "wG686", ["pane_id"] = "wG686:p1", ["cwd"] = "/registry",
                ["kind"] = "codex", ["launch_args"] = new JsonArray("--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/registry"),
                ["envelope_profile"] = profileReference,
            },
        };
        if (includeBrokenRegistrySeat)
        {
            roles["review"] = new JsonObject
            {
                ["resident"] = "herdr", ["workspace_id"] = "wG686", ["pane_id"] = "wG686:p2", ["cwd"] = "/registry",
                ["kind"] = "codex", ["launch_args"] = new JsonArray("--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/registry"),
            };
        }
        var topology = new JsonObject
        {
            ["domain"] = Domain, ["team"] = Team, ["workspace_id"] = "wG686",
            ["envelope_profiles"] = profiles, ["roles"] = roles,
        };
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, topology.ToJsonString());
    }

    private void WriteMalformedTopology(string invalidShape)
    {
        var role = new JsonObject
        {
            ["resident"] = "herdr",
            ["workspace_id"] = "wG686",
            ["pane_id"] = "wG686:p1",
            ["kind"] = "codex",
        };
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = "wG686",
            ["roles"] = new JsonObject { ["orchestration"] = role },
        };

        switch (invalidShape)
        {
            case "override-scalar":
                role["envelope_profile_override"] = "bad";
                break;
            case "override-array":
                role["envelope_profile_override"] = new JsonArray("bad");
                break;
            case "override-malformed-object":
                role["envelope_profile_override"] = new JsonObject { ["kind"] = "codex" };
                break;
            case "profile-array-scalar":
                role["envelope_profile"] = "bad";
                topology["envelope_profiles"] = new JsonArray("bad");
                break;
            case "profile-map-scalar":
                role["envelope_profile"] = "bad";
                topology["envelope_profiles"] = new JsonObject { ["bad"] = "bad" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidShape), invalidShape, null);
        }

        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, topology.ToJsonString());
    }

    private NotifyMeasuredSupervisor CreateSupervisor(CliContext context, INotifyProcessRunner runner, bool write) =>
        new(context, root, Domain, Team, repo: null, ownerRole: "orchestration", intervalSeconds: 300,
            declaredBoundSeconds: null, staleMinutes: 45, claimedSilentMinutes: 720, backlogIdleMinutes: 45,
            repairSilentMinutes: 180, autoRedispatch: false, write, format: "json", runner,
            herdrExecutable: "fake-herdr", agmsgScriptsDirectory: "unused");

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
    };

    private static AgentLaunchEnvelopeProfile Profile(string name, string root) => new()
    {
        Name = name, Kind = "codex", SandboxMode = "workspace-write", ApprovalMode = "never", RootsPolicy = "exact",
        WritableRoots = [root], NetworkAccess = "disabled", TransportMode = "herdr-only",
        Evidence = "operator measured G686", RecordedAt = "2026-08-13T10:00:00Z",
    };

    private static NotifyPaneProcess Process(params string[] argv) =>
        new(23, "/profile", "codex", argv[0], argv, string.Join(' ', argv));

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }

    private sealed class ProfileRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, JsonSerializer.Serialize(new
                {
                    result = new
                    {
                        agents = new[]
                        {
                            new { name = "orchestration", workspace_id = "wG686", pane_id = "wG686:p1", agent = "codex", agent_session = new { id = "a1" }, agent_status = "working", interactive_ready = true },
                            new { name = "review", workspace_id = "wG686", pane_id = "wG686:p2", agent = "codex", agent_session = new { id = "a2" }, agent_status = "working", interactive_ready = true },
                        },
                    },
                }), string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG686:p1"]))
            {
                var argv = new[] { "/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/profile" };
                return ProcessInfo(argv);
            }
            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG686:p2"]))
            {
                var argv = new[] { "/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/other" };
                return ProcessInfo(argv);
            }
            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }

        private static NotifyProcessResult ProcessInfo(string[] argv) => new(
            0,
            JsonSerializer.Serialize(new
            {
                result = new
                {
                    process_info = new
                    {
                        foreground_processes = new[]
                        {
                            new { pid = 23, cwd = "/profile", name = "codex", argv0 = argv[0], argv, cmdline = string.Join(' ', argv) },
                        },
                    },
                },
            }),
            string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
