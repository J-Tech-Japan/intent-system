using System.Diagnostics;
using System.Reflection;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

public sealed class VersionCommandTests : IDisposable
{
    public VersionCommandTests()
    {
        VersionCommand.OverrideVersionString = null;
        VersionCommand.OverridePrivatePreviewMetadata = null;
        // G367: the binary embedded into this test process might carry
        // AssemblyMetadata("PrivatePreviewChannel") when the CI pack
        // workflow built it. Tests that pin the single-line `--version`
        // contract must opt out so a CI run does not accidentally trip
        // them by appending the metadata trailer.
        VersionCommand.SuppressPrivatePreviewTrailer = true;
    }

    public void Dispose()
    {
        VersionCommand.OverrideVersionString = null;
        VersionCommand.OverridePrivatePreviewMetadata = null;
        VersionCommand.SuppressPrivatePreviewTrailer = false;
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

    [Fact]
    public void Execute_WithPrivatePreviewMetadataOverride_AppendsTrailerLineAfterVersion()
    {
        // G367: a CI-built private-preview package surfaces an extra
        // single-line trailer below the version banner so operators
        // can grep `channel=private-preview` and `expires=` without
        // unpacking the .nupkg or touching host state.
        VersionCommand.SuppressPrivatePreviewTrailer = false;
        VersionCommand.OverridePrivatePreviewMetadata = new PrivatePreviewMetadata
        {
            Channel = PrivatePreviewMetadata.ChannelPrivatePreview,
            BuildTimestamp = new DateTimeOffset(2026, 5, 19, 12, 34, 56, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 6, 2, 12, 34, 56, TimeSpan.Zero),
            SourceCommit = "f6cbf65",
        };
        using var writer = new StringWriter();

        var exitCode = VersionCommand.Execute(writer);

        Assert.Equal(0, exitCode);
        var lines = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("intent-cli ", lines[0], StringComparison.Ordinal);
        Assert.Equal(
            "channel=private-preview built=2026-05-19T12:34:56Z expires=2026-06-02T12:34:56Z commit=f6cbf65",
            lines[1]);
    }

    [Fact]
    public void Execute_WithoutPrivatePreviewMetadata_OmitsTrailer()
    {
        // G367: source builds (no AssemblyMetadata("PrivatePreviewChannel"))
        // must NOT emit the trailer so the `--version` contract stays
        // single-line for ordinary contributors.
        VersionCommand.SuppressPrivatePreviewTrailer = false;
        VersionCommand.OverridePrivatePreviewMetadata = null;
        using var writer = new StringWriter();

        var exitCode = VersionCommand.Execute(writer);

        Assert.Equal(0, exitCode);
        var lines = writer.ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        // The running test assembly has no private-preview metadata
        // baked in unless the CI workflow set the MSBuild properties,
        // which the suppression seam above accounts for. Without the
        // override the trailer must be absent.
        Assert.Single(lines);
        Assert.StartsWith("intent-cli ", lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolve the locally-built IntentSystem.Cli executable path.
    /// Uses the test assembly's adjacency to the project tree to
    /// find the dll the build just produced.
    /// </summary>
    private static string LocateBuiltCliBinary()
    {
        // G370: the CI workflow builds Release while the local dev
        // loop builds Debug. Detect the configuration from the test
        // assembly's own bin path so the same lookup works in both
        // (and any future configuration name that lands here).
        // Layout under `tests/IntentSystem.Cli.Tests/bin/<Config>/<TFM>/`:
        //   tests/.../IntentSystem.Cli.Tests.dll
        //   → src/IntentSystem.Cli/bin/<Config>/<TFM>/IntentSystem.Cli(.dll)
        var testDir = Path.GetDirectoryName(typeof(VersionCommandTests).Assembly.Location)!;
        var configurationDir = Directory.GetParent(testDir);
        var configurationName = configurationDir?.Parent?.Name == "bin"
            ? configurationDir!.Name
            : "Debug";
        var targetFramework = Path.GetFileName(testDir);

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
        var cliDir = Path.Combine(repoRoot, "src", "IntentSystem.Cli", "bin", configurationName, targetFramework);
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
