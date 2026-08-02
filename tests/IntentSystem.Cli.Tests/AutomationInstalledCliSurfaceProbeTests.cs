using System.ComponentModel;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G443: the installed-CLI surface probe must not crash the calling command
/// when starting the probe process fails with the Linux <c>ETXTBSY</c>
/// ("Text file busy") exec race. The bounded retry in
/// <c>RunInstalledCli</c> clears the transient case; a persistent failure
/// degrades the surface to "missing" rather than throwing (which previously
/// surfaced as a hard exit 1 in release CI).
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class AutomationInstalledCliSurfaceProbeTests : IDisposable
{
    public AutomationInstalledCliSurfaceProbeTests()
    {
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    public void Dispose()
    {
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    [Fact]
    public void Check_PersistentTextFileBusy_DegradesToMissing_DoesNotThrow()
    {
        var tempRoot = Directory.CreateTempSubdirectory("g443-probe-");
        try
        {
            // A real file so the probe path resolves and File.Exists passes,
            // forcing the code into the ProbeRunner/exec branch.
            var fakeBinary = Path.Combine(tempRoot.FullName, "intent-cli");
            File.WriteAllText(fakeBinary, "#!/bin/sh\nexit 0\n");
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => fakeBinary;

            // Simulate a persistent ETXTBSY (errno 26) from Process.Start.
            AutomationInstalledCliSurfaceProbe.ProbeRunner =
                (_, _) => throw new Win32Exception(26, "An error occurred trying to start process — Text file busy");

            var context = new CliContext
            {
                RepoRoot = tempRoot.FullName,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "bootstrap",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees"
                    }
                }
            };

            // Must not throw: the Win32Exception is caught and each surface
            // degrades to unavailable rather than crashing the command.
            var report = AutomationInstalledCliSurfaceProbe.Check(context);

            Assert.False(report.Available);
            Assert.NotEmpty(report.Checks);
            Assert.All(report.Checks, check => Assert.False(check.Available));
            Assert.All(report.Checks, check => Assert.NotNull(check.Reason));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }
}
