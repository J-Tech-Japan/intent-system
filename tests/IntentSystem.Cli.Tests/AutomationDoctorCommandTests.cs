using System.Security.Cryptography;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationDoctorCommandTests
{
    [Fact]
    public void Execute_TextOutputSurfacesRequiredHostPrTransitions()
    {
        using var workspace = new AutomationDoctorWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--format", "text"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Automation doctor", output, StringComparison.Ordinal);
        Assert.Contains("status: ok", output, StringComparison.Ordinal);
        Assert.Contains("read_only: true", output, StringComparison.Ordinal);
        Assert.Contains("installed_cli_path:", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation pr-transition", output, StringComparison.Ordinal);
        Assert.Contains("--transition review-start", output, StringComparison.Ordinal);
        Assert.Contains("--transition request-update", output, StringComparison.Ordinal);
        Assert.Contains("--transition approved", output, StringComparison.Ordinal);
        Assert.Contains("intent-target", output, StringComparison.Ordinal);
        Assert.Contains("intent-pr-reviewing", output, StringComparison.Ordinal);
        Assert.Contains("intent-pr-request-update", output, StringComparison.Ordinal);
        Assert.Contains("intent-pr-rereview-ready", output, StringComparison.Ordinal);
        Assert.Contains("legacy rereview-ready", output, StringComparison.Ordinal);
        Assert.Contains("intent-pr-approved", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonOutputExposesStableRequiredCommandList()
    {
        using var workspace = new AutomationDoctorWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("ok", result.Status);
        Assert.True(result.ReadOnly);
        Assert.EndsWith(Path.Combine(".intent-cli", "bin", "intent-cli"), result.InstalledCliPath, StringComparison.Ordinal);
        Assert.Equal(6, result.RequiredCommands.Count);
        Assert.All(result.RequiredCommands, command => Assert.True(command.Available));
        Assert.Equal("1", result.AutomationCapabilitySchemaVersion);
        Assert.Equal("automation-command-surface/v1", result.AutomationCommandSurfaceVersion);
        Assert.Equal(4, result.AutomationCommandCapabilities.Count);
        Assert.Contains(result.AutomationCommandCapabilities, capability =>
            string.Equals(capability.Capability, "issue-publish", StringComparison.Ordinal)
            && capability.AddLabels.Contains("intent-target", StringComparer.Ordinal));
        Assert.Contains(result.AutomationCommandCapabilities, capability =>
            string.Equals(capability.Capability, "pr-transition.review-start", StringComparison.Ordinal)
            && string.Equals(capability.Transition, "review-start", StringComparison.Ordinal));
        Assert.Contains(result.AutomationCommandCapabilities, capability =>
            string.Equals(capability.Capability, "pr-transition.request-update", StringComparison.Ordinal)
            && string.Equals(capability.Transition, "request-update", StringComparison.Ordinal));
        Assert.Contains(result.AutomationCommandCapabilities, capability =>
            string.Equals(capability.Capability, "pr-transition.approved", StringComparison.Ordinal)
            && string.Equals(capability.Transition, "approved", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Command, "intent-cli automation summary", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Command, "intent-cli automation host-review-preflight", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Command, "intent-cli automation issue-publish", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Transition, "review-start", StringComparison.Ordinal)
            && command.Usage.Contains("--transition review-start", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Transition, "request-update", StringComparison.Ordinal)
            && command.Usage.Contains("--transition request-update", StringComparison.Ordinal));
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Transition, "approved", StringComparison.Ordinal)
            && command.Usage.Contains("--transition approved", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.True(root.TryGetProperty("required_commands", out var requiredCommands));
        Assert.Equal(6, requiredCommands.GetArrayLength());
        Assert.True(root.TryGetProperty("installed_cli_path", out _));
        Assert.True(root.TryGetProperty("automation_capability_schema_version", out _));
        Assert.True(root.TryGetProperty("automation_command_surface_version", out _));
        Assert.True(root.TryGetProperty("automation_command_capabilities", out var capabilities));
        Assert.Equal(4, capabilities.GetArrayLength());
    }

    [Fact]
    public void Execute_StaleInstalledCliReportsMissingSurfaceAndFails()
    {
        using var workspace = new AutomationDoctorWorkspace();
        workspace.WriteInstalledCliScript(stalePrTransition: true);

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("stale-host-cli", result.Status);
        Assert.Contains(".intent-cli", result.InstalledCliPath, StringComparison.Ordinal);
        Assert.Contains(result.RequiredCommands, command =>
            string.Equals(command.Command, "intent-cli automation pr-transition", StringComparison.Ordinal)
            && string.Equals(command.Transition, "review-start", StringComparison.Ordinal)
            && !command.Available
            && command.Reason!.Contains("not yet implemented", StringComparison.Ordinal));
        Assert.Contains("Abort before label transitions", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeverWritesAnyFile()
    {
        using var workspace = new AutomationDoctorWorkspace();
        workspace.WriteSentinel();
        var beforeSnapshot = workspace.SnapshotWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        var afterSnapshot = workspace.SnapshotWorkspace();
        Assert.Equal(beforeSnapshot.Count, afterSnapshot.Count);
        foreach (var (path, hash) in beforeSnapshot)
        {
            Assert.True(afterSnapshot.TryGetValue(path, out var afterHash),
                $"file disappeared during command execution: {path}");
            Assert.Equal(hash, afterHash);
        }
    }

    [Fact]
    public void CommandRouter_RegistersAutomationDoctor()
    {
        using var workspace = new AutomationDoctorWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["automation", "doctor", "--format", "json"],
            workspace.Context,
            writer);

        Assert.Equal(0, exitCode);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
        Assert.Equal("ok", result.Status);
    }

    [Fact]
    public void ProgramMain_AutomationDoctorDoesNotRequireIntentCliDirectory()
    {
        using var workspace = new AutomationDoctorWorkspace(createIntentCliDirectory: false);
        var originalDirectory = Directory.GetCurrentDirectory();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        // G282: cwd-local shim is intentionally absent. Force the PATH fallback
        // to also miss so we exercise the original "neither shim nor global
        // tool present" branch and the doctor still emits stale-host-cli.
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;

        try
        {
            Directory.SetCurrentDirectory(workspace.RootPath);
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var exitCode = Program.Main(["automation", "doctor", "--format", "json"]);

        Assert.Equal(1, exitCode);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.DoesNotContain("Command 'automation doctor' is not yet implemented.", stdout.ToString(), StringComparison.Ordinal);
        var result = JsonSerializer.Deserialize<AutomationDoctorResult>(stdout.ToString())!;
        Assert.Equal("stale-host-cli", result.Status);
        Assert.True(result.ReadOnly);
        Assert.Contains(".intent-cli", result.InstalledCliPath, StringComparison.Ordinal);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void Execute_GlobalDotnetToolOnPathSatisfiesDoctorWhenCwdShimIsAbsent()
    {
        using var workspace = new AutomationDoctorWorkspace(createIntentCliDirectory: false);
        // Simulate a global dotnet tool install at $HOME/.dotnet/tools/intent-cli.
        var pretendGlobalDir = Directory.CreateTempSubdirectory("g282-global-tool-").FullName;
        var pretendGlobalBinary = Path.Combine(pretendGlobalDir, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        WriteHealthyShim(pretendGlobalBinary);
        AutomationInstalledCliSurfaceProbe.PathResolver = name =>
            string.Equals(name, Path.GetFileName(pretendGlobalBinary), StringComparison.Ordinal)
                ? pretendGlobalBinary
                : null;

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(
                workspace.Context,
                ["--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
            Assert.Equal("ok", result.Status);
            Assert.Equal(pretendGlobalBinary, result.InstalledCliPath);
            Assert.Equal("path-global-tool", result.BinarySource);
            Assert.Equal(Path.Combine(workspace.RootPath, ".intent-cli"), result.HostDataRoot);
            Assert.Contains("binary_source=path-global-tool", result.Summary, StringComparison.Ordinal);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
            if (Directory.Exists(pretendGlobalDir))
            {
                Directory.Delete(pretendGlobalDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_NoCwdShimAndNoPathBinary_ReportsStaleHostCliWithBinarySourceMissing()
    {
        using var workspace = new AutomationDoctorWorkspace(createIntentCliDirectory: false);
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(
                workspace.Context,
                ["--format", "json"],
                writer);

            Assert.Equal(1, exitCode);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
            Assert.Equal("stale-host-cli", result.Status);
            Assert.Equal("missing", result.BinarySource);
            Assert.Equal(Path.Combine(workspace.RootPath, ".intent-cli"), result.HostDataRoot);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
        }
    }

    [Fact]
    public void Execute_PathGlobalToolWithStaleSurface_ReportsStaleHostCliAndKeepsPathGlobalToolSource()
    {
        using var workspace = new AutomationDoctorWorkspace(createIntentCliDirectory: false);
        var pretendGlobalDir = Directory.CreateTempSubdirectory("g282-global-tool-stale-").FullName;
        var pretendGlobalBinary = Path.Combine(pretendGlobalDir, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        WriteShimWithStalePrTransition(pretendGlobalBinary);
        AutomationInstalledCliSurfaceProbe.PathResolver = name =>
            string.Equals(name, Path.GetFileName(pretendGlobalBinary), StringComparison.Ordinal)
                ? pretendGlobalBinary
                : null;

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(
                workspace.Context,
                ["--format", "json"],
                writer);

            Assert.Equal(1, exitCode);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
            Assert.Equal("stale-host-cli", result.Status);
            Assert.Equal("path-global-tool", result.BinarySource);
            Assert.Equal(pretendGlobalBinary, result.InstalledCliPath);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
            if (Directory.Exists(pretendGlobalDir))
            {
                Directory.Delete(pretendGlobalDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_ExplicitInstalledPathOverrideWinsOverCwdShimAndPath()
    {
        using var workspace = new AutomationDoctorWorkspace();
        // workspace.WriteInstalledCliScript(false) was already called by ctor, so cwd shim is a healthy binary.
        var explicitBinaryDir = Directory.CreateTempSubdirectory("g282-explicit-").FullName;
        var explicitBinary = Path.Combine(explicitBinaryDir, OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
        WriteHealthyShim(explicitBinary);
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => explicitBinary;

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(
                workspace.Context,
                ["--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
            Assert.Equal("ok", result.Status);
            Assert.Equal(explicitBinary, result.InstalledCliPath);
            Assert.Equal("explicit-override", result.BinarySource);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
            if (Directory.Exists(explicitBinaryDir))
            {
                Directory.Delete(explicitBinaryDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_CwdLocalShimPresent_BinarySourceIsCwdLocalShim()
    {
        using var workspace = new AutomationDoctorWorkspace(); // ctor writes a healthy cwd shim
        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null; // ensure PATH lookup is not the source

        try
        {
            using var writer = new StringWriter();
            var exitCode = AutomationDoctorCommand.Execute(
                workspace.Context,
                ["--format", "json"],
                writer);

            Assert.Equal(0, exitCode);
            var result = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;
            Assert.Equal("ok", result.Status);
            Assert.Equal("cwd-local-shim", result.BinarySource);
            Assert.EndsWith(Path.Combine(".intent-cli", "bin", OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli"),
                result.InstalledCliPath, StringComparison.Ordinal);
        }
        finally
        {
            AutomationInstalledCliSurfaceProbe.PathResolver = null;
        }
    }

    private static void WriteHealthyShim(string scriptPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(
            scriptPath,
            "#!/bin/sh\n"
            + "case \"$*\" in\n"
            + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
            + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
            + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
            + "  'automation pr-transition') echo '--transition is required (review-start, request-update, or approved).'; exit 1 ;;\n"
            + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
            + "esac\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void WriteShimWithStalePrTransition(string scriptPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        File.WriteAllText(
            scriptPath,
            "#!/bin/sh\n"
            + "case \"$*\" in\n"
            + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
            + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
            + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
            + "  'automation pr-transition') echo \"Command 'automation pr-transition' is not yet implemented.\"; exit 1 ;;\n"
            + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
            + "esac\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    [Fact]
    public void CommandRouter_HelpSurfacesAutomationDoctor()
    {
        using var workspace = new AutomationDoctorWorkspace();

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute([], workspace.Context, writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("automation doctor", output, StringComparison.Ordinal);
        Assert.Contains("automation pr-transition", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenUnknownFormat_ReturnsNonZero()
    {
        using var workspace = new AutomationDoctorWorkspace();

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format", writer.ToString(), StringComparison.Ordinal);
    }

    private sealed class AutomationDoctorWorkspace : IDisposable
    {
        private readonly string rootPath = Directory
            .CreateTempSubdirectory("automation-doctor-tests-")
            .FullName;

        public string RootPath => rootPath;

        public AutomationDoctorWorkspace(bool createIntentCliDirectory = true)
        {
            if (createIntentCliDirectory)
            {
                WriteInstalledCliScript(stalePrTransition: false);
            }

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

        public void WriteSentinel()
        {
            File.WriteAllText(Path.Combine(rootPath, "sentinel.txt"), "unchanged");
        }

        public void WriteInstalledCliScript(bool stalePrTransition)
        {
            var binPath = Path.Combine(rootPath, ".intent-cli", "bin");
            Directory.CreateDirectory(binPath);
            var scriptPath = Path.Combine(binPath, "intent-cli");
            var prTransitionBlock = stalePrTransition
                ? "  echo \"Command 'automation pr-transition' is not yet implemented.\"\n  exit 1\n"
                : "  echo '--transition is required (review-start, request-update, or approved).'\n  exit 1\n";
            File.WriteAllText(
                scriptPath,
                "#!/bin/sh\n"
                + "case \"$*\" in\n"
                + "  'automation summary') echo '--domain is required.'; exit 1 ;;\n"
                + "  'automation host-review-preflight') echo '--repo is required.'; exit 1 ;;\n"
                + "  'automation issue-publish') echo '--issue is required.'; exit 1 ;;\n"
                + "  'automation pr-transition')\n"
                + prTransitionBlock
                + "    ;;\n"
                + "  *) echo \"unexpected probe: $*\"; exit 1 ;;\n"
                + "esac\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    scriptPath,
                    UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute);
            }
        }

        public IReadOnlyDictionary<string, string> SnapshotWorkspace()
        {
            var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                var hash = Convert.ToHexString(SHA256.HashData(bytes));
                snapshot[path] = hash;
            }
            return snapshot;
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
