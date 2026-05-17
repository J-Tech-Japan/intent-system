using System.Diagnostics;
using System.Reflection;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class VersionCommandTests : IDisposable
{
    public VersionCommandTests()
    {
        VersionCommand.OverrideVersionString = null;
    }

    public void Dispose()
    {
        VersionCommand.OverrideVersionString = null;
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("version")]
    public void IsVersionRequest_RecognizesCanonicalShapes(string token)
    {
        Assert.True(VersionCommand.IsVersionRequest(new[] { token }));
    }

    [Theory]
    [InlineData("worker", "next-action")]
    [InlineData("--help")]
    [InlineData("intent", "status")]
    public void IsVersionRequest_RejectsNonVersionTokens(params string[] args)
    {
        Assert.False(VersionCommand.IsVersionRequest(args));
    }

    [Fact]
    public void IsVersionRequest_EmptyArgs_ReturnsFalse()
    {
        Assert.False(VersionCommand.IsVersionRequest(Array.Empty<string>()));
    }

    [Fact]
    public void Execute_WithOverride_WritesExactVersionString_AndReturnsZero()
    {
        VersionCommand.OverrideVersionString = "intent-cli 0.2.0-f6cbf65-G357";
        using var writer = new StringWriter();

        var exitCode = VersionCommand.Execute(writer);

        Assert.Equal(0, exitCode);
        Assert.Equal("intent-cli 0.2.0-f6cbf65-G357" + Environment.NewLine, writer.ToString());
    }

    [Fact]
    public void BuildVersionString_FromAssemblyWithInformationalVersion_IncludesPackageVersionShaAndExecutionUnit()
    {
        // G360 acceptance: the cli assembly's
        // AssemblyInformationalVersionAttribute is baked at pack/build
        // time with the canonical format. This test pins the shape
        // produced by BuildVersionString against the executing
        // IntentSystem.Cli assembly so the build-time wiring keeps
        // producing the expected string.
        var assembly = typeof(VersionCommand).Assembly;
        var result = VersionCommand.BuildVersionString(assembly);

        Assert.StartsWith("intent-cli ", result, StringComparison.Ordinal);
        // Package version segment must be present.
        Assert.Matches(@"intent-cli \d+\.\d+\.\d+", result);
        // Latest implemented execution unit segment must be present;
        // the csproj sets G360 by default, but allow any G-number to
        // future-proof against bumps.
        Assert.Matches(@"-G\d+$", result);
        // The `+commit` SourceLink suffix must be stripped — the
        // canonical form uses dash-separated segments only.
        Assert.DoesNotContain('+', result);
    }

    [Fact]
    public void Execute_VersionFromNonHostCwd_DoesNotResolveHostState()
    {
        // G360 acceptance: running `intent-cli --version` from a
        // directory that has no `.intent-cli/` must NOT emit the
        // G299 missing-host-state guidance. Exercises the real
        // Program.Main entry by launching the built binary against
        // a temp cwd with no `.intent-cli/`.
        var binaryPath = LocateBuiltCliBinary();
        Assert.True(File.Exists(binaryPath), $"built CLI binary not found at {binaryPath}");

        var nonHostDir = Directory.CreateTempSubdirectory("g360-non-host-").FullName;
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = binaryPath;
            process.StartInfo.Arguments = "--version";
            process.StartInfo.WorkingDirectory = nonHostDir;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0,
                $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.StartsWith("intent-cli ", stdout, StringComparison.Ordinal);
            // Must NOT contain the G299 fail-closed missing-host-state guidance.
            Assert.DoesNotContain("missing host state", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("G299", stdout, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(nonHostDir))
            {
                Directory.Delete(nonHostDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_DashVFromNonHostCwd_PrintsVersionAndExitsZero()
    {
        var binaryPath = LocateBuiltCliBinary();
        var nonHostDir = Directory.CreateTempSubdirectory("g360-non-host-").FullName;
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = binaryPath;
            process.StartInfo.Arguments = "-v";
            process.StartInfo.WorkingDirectory = nonHostDir;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.Equal(0, process.ExitCode);
            Assert.StartsWith("intent-cli ", stdout, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(nonHostDir))
            {
                Directory.Delete(nonHostDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Execute_HostDependentCommandFromNonHostCwd_StillReturnsMissingHostStateGuidance()
    {
        // G360 negative test: confirm the existing G299 fail-closed
        // posture is preserved for commands that genuinely require
        // host state. Running e.g. `intent-cli intent status` from a
        // non-host cwd must NOT silently succeed.
        var binaryPath = LocateBuiltCliBinary();
        var nonHostDir = Directory.CreateTempSubdirectory("g360-non-host-").FullName;
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = binaryPath;
            process.StartInfo.Arguments = "intent status";
            process.StartInfo.WorkingDirectory = nonHostDir;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            Assert.NotEqual(0, process.ExitCode);
            // The G299 guidance text mentions "host state" so this is a
            // reliable smoke check that the fail-closed lane still
            // fires for host-dependent commands.
            Assert.Contains("host", stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(nonHostDir))
            {
                Directory.Delete(nonHostDir, recursive: true);
            }
        }
    }

    /// <summary>
    /// Resolve the locally-built IntentSystem.Cli executable path.
    /// Uses the test assembly's adjacency to the project tree to
    /// find the dll the build just produced.
    /// </summary>
    private static string LocateBuiltCliBinary()
    {
        // tests/IntentSystem.Cli.Tests/bin/Debug/net10.0/IntentSystem.Cli.Tests.dll
        // → src/IntentSystem.Cli/bin/Debug/net10.0/IntentSystem.Cli(.dll)
        var testDir = Path.GetDirectoryName(typeof(VersionCommandTests).Assembly.Location)!;
        var repoRoot = testDir;
        while (repoRoot is not null
            && !File.Exists(Path.Combine(repoRoot, "IntentSystem.sln")))
        {
            var parent = Directory.GetParent(repoRoot);
            if (parent is null) break;
            repoRoot = parent.FullName;
        }
        if (repoRoot is null)
        {
            return string.Empty;
        }
        var cliDir = Path.Combine(repoRoot, "src", "IntentSystem.Cli", "bin", "Debug", "net10.0");
        // dotnet SDK produces a native host launcher `IntentSystem.Cli`
        // (the project's AssemblyName, not the `ToolCommandName`).
        // Prefer that native exec when present.
        var execPath = Path.Combine(cliDir, "IntentSystem.Cli");
        if (File.Exists(execPath))
        {
            return execPath;
        }
        // Fall back to `dotnet IntentSystem.Cli.dll` via the runtime.
        return Path.Combine(cliDir, "IntentSystem.Cli.dll");
    }
}
