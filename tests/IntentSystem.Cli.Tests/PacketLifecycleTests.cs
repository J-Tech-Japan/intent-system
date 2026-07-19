using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

public sealed class PacketLifecycleTests
{
    [Fact]
    public void ReadState_GivenNoSidecar_ReturnsAbsent()
    {
        using var workspace = new PacketDirectoryWorkspace();

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Absent, outcome.State);
        Assert.Null(outcome.Metadata);
        Assert.Null(outcome.Detail);
    }

    [Fact]
    public void ReadState_GivenReadyLifecycle_ReturnsValidActive()
    {
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar("lifecycle: ready\n");

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.ValidActive, outcome.State);
        Assert.Equal("ready", outcome.Metadata?.Lifecycle);
    }

    [Theory]
    [InlineData("absorbed")]
    [InlineData("retired")]
    [InlineData("superseded")]
    public void ReadState_GivenNonPublishableLifecycle_ReturnsValidRetired(string lifecycle)
    {
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar($"lifecycle: {lifecycle}\n");

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.ValidRetired, outcome.State);
        Assert.Equal(lifecycle, outcome.Metadata?.Lifecycle);
    }

    [Fact]
    public void ReadState_GivenMissingLifecycleKey_ReturnsInvalid()
    {
        // G534 review repair: a sidecar that exists but never declares the
        // required `lifecycle` key must fail closed (Invalid), not be
        // treated the same as no sidecar at all (Absent).
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar("retired_reason: \"some note\"\n");

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Invalid, outcome.State);
        Assert.Contains("lifecycle", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadState_GivenBlankLifecycleValue_ReturnsInvalid()
    {
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar("lifecycle: \n");

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Invalid, outcome.State);
        Assert.Contains("blank", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadState_GivenUnknownLifecycleValue_ReturnsInvalid()
    {
        // G534 review repair: the literal SKS-G812 field-finding shape — a
        // typo'd or unrecognized lifecycle value must never silently be
        // treated as "not retired" (i.e. publishable).
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar("lifecycle: retird\n");

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Invalid, outcome.State);
        Assert.Contains("unknown lifecycle value", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadState_GivenEmptyFile_ReturnsInvalid()
    {
        using var workspace = new PacketDirectoryWorkspace();
        workspace.WriteSidecar(string.Empty);

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Invalid, outcome.State);
    }

    [Fact]
    public void ReadState_GivenSidecarPathIsADirectory_ReturnsInvalidUnreadable()
    {
        // G534 review repair: an unreadable sidecar (here: a directory
        // occupying the expected file path) must fail closed as Invalid,
        // never silently fall back to Absent.
        using var workspace = new PacketDirectoryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.PacketDirectory, PacketLifecycle.SidecarFileName));

        var outcome = PacketLifecycle.ReadState(workspace.PacketDirectory);

        Assert.Equal(PacketLifecycleState.Invalid, outcome.State);
        Assert.Contains("unreadable", outcome.Detail, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PacketDirectoryWorkspace : IDisposable
    {
        public string PacketDirectory { get; } =
            Directory.CreateTempSubdirectory("intent-cli-packet-lifecycle-tests-").FullName;

        public void WriteSidecar(string content)
        {
            File.WriteAllText(Path.Combine(PacketDirectory, PacketLifecycle.SidecarFileName), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(PacketDirectory))
            {
                Directory.Delete(PacketDirectory, recursive: true);
            }
        }
    }
}
