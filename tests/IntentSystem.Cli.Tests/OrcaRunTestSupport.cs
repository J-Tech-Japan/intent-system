using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G837 shared workspace, fixtures, and helpers for Orca Run binding tests.
/// </summary>
[Collection("WorkerNextActionSharedState")]
internal static class OrcaRunTestSupport
{
    public const string Domain = "intent-cli";
    public const string RunId = "run_585adfc0e774";

    public const string RecordedOnlySuffix =
        "Recorded only; intent-cli did not run orca or verify the Run.";

    public const string DiscoverySummary =
        "Recorded bindings only; no Run was contacted.";

    public const string ReceiveInstructionOrcaPush =
        "Receive by Orca push: this seat runs in an Orca terminal; intent-cli sends and wakes nothing.";

    public const string ReceiveInstructionInboxPull =
        "Receive by scheduled pull: this app seat reads its canonical inbox and status once per scheduled wake; intent-cli schedules nothing.";

    public const string GuardUnreadableSummaryPrefix =
        "Orca Run binding guard:";

    public const string HostStateEnvelope = "non-sandboxed-host-repository-write";

    private static readonly DateTimeOffset FixtureTransitionAt = new(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);

    public static void ResetSeams()
    {
        TeamModeOrcaRunGuard.AfterLockHook = null;
        TeamModeOrcaRunGuard.BeforeWriteHook = null;
        OrcaRunRecordCommand.AfterTopologyLockHook = null;
        GuardedFileRead.ReadAllTextFactory = null;
        GuardedFileWrite.AppendLineFactory = null;
        TeamModeCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.UtcNowFactory = null;
        AutomationHostLoopNextActionCommand.CandidateListerFactory = null;
        AutomationHostLoopNextActionCommand.IdentityCaptureFactory = null;
    }

    public static FakeBinFixture CreateFakeBinFixture(string workspaceRoot)
    {
        var fakeBinDir = Directory.CreateTempSubdirectory("orca-fakebin-").FullName;
        var orcaPath = Path.Combine(fakeBinDir, "orca");
        var ghPath = Path.Combine(fakeBinDir, "gh");
        var orcaLogPath = Path.Combine(workspaceRoot, "fake-orca.log");
        var ghLogPath = Path.Combine(workspaceRoot, "fake-gh.log");
        File.WriteAllText(orcaPath, "#!/bin/sh\n"
            + "echo \"$(date -u +%FT%TZ) orca $*\" >> \"${FAKE_ORCA_LOG}\"\n"
            + "echo \"fake orca: refusing to run (intent-cli must never invoke orca)\" >&2\n"
            + "exit 97\n");
        File.WriteAllText(ghPath, "#!/bin/sh\n"
            + "echo \"$PPID $*\" >> \"${FAKE_GH_LOG}\"\n"
            + "echo \"fake gh refused\" >&2\n"
            + "exit 1\n");
        if (!OperatingSystem.IsWindows())
        {
            var execute = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            File.SetUnixFileMode(orcaPath, execute);
            File.SetUnixFileMode(ghPath, execute);
        }

        return new FakeBinFixture(fakeBinDir, orcaLogPath, ghLogPath);
    }

    public static void ClearFakeLogs(FakeBinFixture fixture)
    {
        File.WriteAllText(fixture.OrcaLogPath, string.Empty);
        File.WriteAllText(fixture.GhLogPath, string.Empty);
    }

    public static void AssertFakeOrcaLive(FakeBinFixture fixture)
    {
        var probeLog = fixture.OrcaLogPath + ".probe";
        File.WriteAllText(probeLog, string.Empty);
        var previousOrcaLog = Environment.GetEnvironmentVariable("FAKE_ORCA_LOG");
        Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", probeLog);
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(fixture.BinDirectory, "orca"),
                Arguments = "probe",
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();
            Assert.True(File.Exists(probeLog), "fake orca probe log must exist");
            Assert.Contains("orca probe", File.ReadAllText(probeLog), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", previousOrcaLog);
            if (File.Exists(probeLog))
            {
                File.Delete(probeLog);
            }
        }
    }

    public static void AssertOrcaLogEmpty(FakeBinFixture fixture)
    {
        Assert.True(File.Exists(fixture.OrcaLogPath), "fake orca log must exist (fake must be on PATH)");
        Assert.True(string.IsNullOrWhiteSpace(File.ReadAllText(fixture.OrcaLogPath)),
            $"fake orca log must be empty but contained: {File.ReadAllText(fixture.OrcaLogPath)}");
    }

    internal sealed record FakeBinFixture(string BinDirectory, string OrcaLogPath, string GhLogPath);

    public static string[] RecordOrcaRunArgs(
        OrcaRunWorkspace workspace,
        string role,
        string current,
        string newRunId,
        string? receivePolicy = null,
        string? frontend = null,
        bool write = false)
    {
        var args = new List<string>
        {
            "session-layer", "topology", "record-orca-run",
            "--domain", Domain,
            "--team", workspace.Team,
            "--role", role,
            "--current", current,
            "--new", newRunId,
        };
        if (receivePolicy is not null)
        {
            args.AddRange(["--receive-policy", receivePolicy]);
        }

        if (frontend is not null)
        {
            args.AddRange(["--frontend", frontend]);
        }

        args.Add("--confirm-record-orca-run");
        args.Add(write ? "--write" : "--dry-run");
        args.AddRange(["--format", "json"]);
        return args.ToArray();
    }

    public static JsonElement Parse(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    internal sealed class OrcaRunWorkspace : IDisposable
    {
        private readonly string? previousPath;
        private readonly string? previousFakeLog;
        private readonly string? previousFakeGhLog;
        private readonly string fakeBinDir;

        public OrcaRunWorkspace(string suffix, string? team = null)
        {
            previousPath = Environment.GetEnvironmentVariable("PATH");
            previousFakeLog = Environment.GetEnvironmentVariable("FAKE_ORCA_LOG");
            previousFakeGhLog = Environment.GetEnvironmentVariable("FAKE_GH_LOG");
            ResetSeams();

            Root = Directory.CreateTempSubdirectory($"orca-run-g837-{suffix}-").FullName;
            FakeBin = CreateFakeBinFixture(Root);
            fakeBinDir = FakeBin.BinDirectory;
            var existing = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", $"{FakeBin.BinDirectory}:{existing}");
            Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", FakeBin.OrcaLogPath);
            Environment.SetEnvironmentVariable("FAKE_GH_LOG", FakeBin.GhLogPath);
            ClearFakeLogs(FakeBin);

            Team = team ?? $"g837-{suffix}";
            Context = new CliContext
            {
                RepoRoot = Root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string Root { get; }
        public string Team { get; }
        public FakeBinFixture FakeBin { get; }
        public CliContext Context { get; }
        public string TopologyPath => NotifyRoleTopologyStore.ResolvePath(Root, Domain, Team);
        public string TeamModePath => TeamModeStore.ResolvePath(Root);
        public string SoloBindingPath => OrcaRunSoloStore.ResolvePath(Root, Domain, Team);

        public void InstallFiveSeatDeliveryFixture() =>
            WriteDeliveryTopology(new Dictionary<string, object>
            {
                ["orchestration"] = HerdrRole("w1:p1"),
                ["implementation"] = HerdrRole("w1:p2"),
                ["review"] = HerdrRole("w1:p3"),
                ["design"] = ExternalRole("claude-app"),
                ["steward"] = ExternalRole("orca"),
            });

        public void InstallFourSeatDeliveryFixture() =>
            WriteDeliveryTopology(new Dictionary<string, object>
            {
                ["orchestration"] = HerdrRole("w1:p1"),
                ["implementation"] = HerdrRole("w1:p2"),
                ["review"] = HerdrRole("w1:p3"),
                ["design"] = ExternalRole("claude-app"),
            });

        public void InstallAliasFiveSeatFixture() =>
            WriteDeliveryTopology(new Dictionary<string, object>
            {
                ["orchestration"] = HerdrRole("w1:p1"),
                ["implementation"] = HerdrRole("w1:p2"),
                ["review"] = HerdrRole("w1:p3"),
                ["design"] = ExternalRole("claude-app"),
                ["architect"] = ExternalRole("claude-app"),
                ["steward"] = ExternalRole("orca"),
            });

        public void InstallAliasFourSeatFixture() =>
            WriteDeliveryTopology(new Dictionary<string, object>
            {
                ["orchestration"] = HerdrRole("w1:p1"),
                ["implementation"] = HerdrRole("w1:p2"),
                ["review"] = HerdrRole("w1:p3"),
                ["design"] = ExternalRole("claude-app"),
                ["architect"] = ExternalRole("claude-app"),
            });

        public void WriteDeliveryTopology(IReadOnlyDictionary<string, object> roles)
        {
            WriteTeamModeFile(TeamMode.Delivery, Team);
            WriteRawTopology(new
            {
                schema_version = "1",
                domain = Domain,
                team = Team,
                workspace_id = "w1",
                host_state = new { role = "design", envelope = HostStateEnvelope },
                roles,
            });
        }

        public void InstallSoloFixture()
        {
            WriteTeamModeFile(TeamMode.SoloConductor, Team);
        }

        public void SetTeamMode(string mode, string? team = null, bool write = true, bool domainWide = false)
        {
            if (!write)
            {
                var args = new List<string>
                {
                    "--domain", Domain,
                    "--mode", mode,
                    "--format", "json",
                };
                if (!domainWide)
                {
                    args.AddRange(["--team", team ?? Team]);
                }

                using var writer = new StringWriter();
                Assert.Equal(0, TeamModeCommand.ExecuteSet(Context, args.ToArray(), writer));
                return;
            }

            WriteTeamModeFile(mode, domainWide ? null : team ?? Team);
        }

        public void WriteTeamModeFile(string mode, string? team = null)
        {
            var path = TeamModePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entry = new Dictionary<string, object?>
            {
                ["domain"] = Domain,
                ["mode"] = mode,
                ["updated_at"] = FixtureTransitionAt.UtcDateTime.ToString("O"),
                ["transitions"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["from"] = TeamMode.Default,
                        ["to"] = mode,
                        ["at"] = FixtureTransitionAt.UtcDateTime.ToString("O"),
                        ["reason"] = "g837-fixture",
                    },
                },
            };
            if (team is not null)
            {
                entry["team"] = team;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schema_version = "1",
                entries = new[] { entry },
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void WriteDomainWideTeamModeFile(string mode) => WriteTeamModeFile(mode, team: null);

        public void EnsureDomainWideTeamModeEntry(string mode)
        {
            var root = JsonNode.Parse(File.Exists(TeamModePath) ? File.ReadAllText(TeamModePath) : """{"schema_version":"1","entries":[]}""")!.AsObject();
            var entries = root["entries"] as JsonArray ?? new JsonArray();
            if (entries.Any(entry => entry is JsonObject obj && !obj.ContainsKey("team")))
            {
                File.WriteAllText(TeamModePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return;
            }

            var domainWideEntry = JsonNode.Parse(JsonSerializer.Serialize(new
            {
                domain = Domain,
                mode,
                updated_at = FixtureTransitionAt.UtcDateTime.ToString("O"),
                transitions = new[]
                {
                    new
                    {
                        from = TeamMode.Default,
                        to = mode,
                        at = FixtureTransitionAt.UtcDateTime.ToString("O"),
                        reason = "g837-fixture",
                    },
                },
            }))!;
            entries.Insert(0, domainWideEntry);
            root["entries"] = entries;
            Directory.CreateDirectory(Path.GetDirectoryName(TeamModePath)!);
            File.WriteAllText(TeamModePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void WriteTopologyOrcaRun(string role, string runId, string receivePolicy, string? frontend = null)
        {
            var root = JsonNode.Parse(File.ReadAllText(TopologyPath))!.AsObject();
            var roleObject = root["roles"]![role]!.AsObject();
            var orcaRun = new JsonObject
            {
                ["run_id"] = runId,
                ["receive_policy"] = receivePolicy,
            };
            if (frontend is not null)
            {
                orcaRun["frontend"] = frontend;
            }

            roleObject["orca_run"] = orcaRun;
            File.WriteAllText(TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void RemoveWorkspaceIdsFromTopology() => MakeTopologyWorkspaceIdAmbiguous();

        public void MakeTopologyWorkspaceIdAmbiguous()
        {
            var root = JsonNode.Parse(File.ReadAllText(TopologyPath))!.AsObject();
            root.Remove("workspace_id");
            if (root["roles"] is JsonObject roles)
            {
                foreach (var role in roles)
                {
                    if (role.Value is not JsonObject roleObject)
                    {
                        continue;
                    }

                    roleObject.Remove("workspace_id");
                    if (roleObject.TryGetPropertyValue("pane_id", out var paneNode)
                        && paneNode is JsonValue paneValue
                        && paneValue.TryGetValue<string>(out var paneId)
                        && paneId.Contains(':', StringComparison.Ordinal))
                    {
                        roleObject["pane_id"] = paneId[(paneId.IndexOf(':', StringComparison.Ordinal) + 1)..];
                    }
                }
            }

            File.WriteAllText(TopologyPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        public void RecordExternal(string role, string frontend)
        {
            var result = RunRaw(ExternalRecordArgs(role, frontend));
            Assert.Equal(0, result.ExitCode);
        }

        public void RecordHerdr(string role, string paneId)
        {
            var result = RunRaw(HerdrRecordArgs(role, paneId));
            Assert.Equal(0, result.ExitCode);
        }

        public string[] ExternalRecordArgs(string role, string frontend) =>
        [
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", role,
            "--resident", "external",
            "--reader", $".intent-cli/events/{Domain}/{Team}-{role}.jsonl",
            "--frontend", frontend,
            "--write", "--format", "json",
        ];

        public string[] HerdrRecordArgs(string role, string paneId) =>
        [
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", role,
            "--resident", "herdr",
            "--workspace-id", "w1",
            "--pane-id", paneId,
            "--cwd", "/host",
            "--delivery-method", "inline",
            "--write", "--format", "json",
        ];

        public void WriteRawTopology(object topology)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TopologyPath)!);
            File.WriteAllText(TopologyPath, JsonSerializer.Serialize(topology, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void WriteSoloBinding(object payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SoloBindingPath)!);
            File.WriteAllText(SoloBindingPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        public byte[] TopologyBytes() => File.Exists(TopologyPath) ? File.ReadAllBytes(TopologyPath) : [];

        public byte[] TeamModeBytes() => File.Exists(TeamModePath) ? File.ReadAllBytes(TeamModePath) : [];

        public (int ExitCode, string Output) RunRaw(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public (int ExitCode, JsonElement Result) RunJson(params string[] args)
        {
            var (exitCode, output) = RunRaw(args);
            return (exitCode, Parse(output));
        }

        public JsonElement RunRecordOrcaRun(
            string role,
            string current,
            string newRunId,
            string? receivePolicy = null,
            string? frontend = null,
            bool write = false)
        {
            var (exitCode, result) = RunJson(RecordOrcaRunArgs(this, role, current, newRunId, receivePolicy, frontend, write));
            Assert.Equal(0, exitCode);
            return result;
        }

        public string CaptureTopologyValidateOutput()
        {
            var (_, result) = RunJson(
                "session-layer", "topology", "validate",
                "--domain", Domain, "--team", Team, "--format", "json");
            return result.ToString();
        }

        public JsonElement RunRecordOrcaRunExpectFailure(
            string role,
            string current,
            string newRunId,
            string? receivePolicy = null,
            string? frontend = null,
            bool write = false)
        {
            var (exitCode, result) = RunJson(RecordOrcaRunArgs(this, role, current, newRunId, receivePolicy, frontend, write));
            Assert.Equal(1, exitCode);
            return result;
        }

        public static object HerdrRole(string paneId) => new
        {
            resident = "herdr",
            workspace_id = "w1",
            pane_id = paneId,
            cwd = "/host",
            delivery_method = "inline",
        };

        public object ExternalRole(string frontend) => ExternalRoleFor(Team, frontend);

        public static object ExternalRoleFor(string team, string frontend) => new
        {
            resident = "external",
            reader = $".intent-cli/events/{Domain}/{team}-reader.jsonl",
            frontend,
        };

        public IReadOnlyList<string> RelativePathsUnderIntentCli()
        {
            var paths = new List<string>();
            var intentCli = Path.Combine(Root, ".intent-cli");
            if (!Directory.Exists(intentCli))
            {
                return paths;
            }

            foreach (var file in Directory.EnumerateFiles(intentCli, "*", SearchOption.AllDirectories))
            {
                paths.Add(Path.GetRelativePath(Root, file).Replace('\\', '/'));
            }

            return paths.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        public void AssertOnlyNewPaths(IReadOnlyList<string> expected, IReadOnlySet<string> before)
        {
            var after = RelativePathsUnderIntentCli().ToHashSet(StringComparer.Ordinal);
            var created = after.Except(before, StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected.OrderBy(path => path, StringComparer.Ordinal).ToArray(), created);
        }

        public void AssertTopologyDeepEqualsExceptOrcaRun(string beforeText, string afterText)
        {
            var before = JsonNode.Parse(beforeText) as JsonObject ?? new JsonObject();
            var after = JsonNode.Parse(afterText) as JsonObject ?? new JsonObject();
            StripOrcaRun(before);
            StripOrcaRun(after);
            Assert.True(JsonNode.DeepEquals(before, after));
        }

        private static void StripOrcaRun(JsonObject root)
        {
            if (root["roles"] is JsonObject roles)
            {
                foreach (var role in roles)
                {
                    if (role.Value is JsonObject roleObject)
                    {
                        roleObject.Remove("orca_run");
                    }
                }
            }
        }

        public void Dispose()
        {
            ResetSeams();
            Environment.SetEnvironmentVariable("PATH", previousPath);
            Environment.SetEnvironmentVariable("FAKE_ORCA_LOG", previousFakeLog);
            Environment.SetEnvironmentVariable("FAKE_GH_LOG", previousFakeGhLog);
            if (Directory.Exists(fakeBinDir))
            {
                try
                {
                    Directory.Delete(fakeBinDir, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            if (Directory.Exists(Root))
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
