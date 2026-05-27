# Install

← [docs index](README.md) | → [Start a project](02-project-start.md)

Install `intent-cli` and verify it works. Once confirmed, continue to [Start a project](02-project-start.md).

## If .NET is not installed

Installing intent-cli as a NuGet global tool requires the .NET SDK.
If the `dotnet` command is not yet available, install the .NET 10 SDK from the official Microsoft download page:

- https://dotnet.microsoft.com/en-us/download

After installing, confirm the SDK is available:

```bash
dotnet --version
```

Once a version number appears, you are ready for the next step.

## Install

The basic path is the .NET global tool from NuGet.org (requires a **.NET 10
SDK**). The same commands work on macOS, Windows, and Linux:

```bash
# Install
dotnet tool install -g JTechJapan.IntentSystem.Cli

# Upgrade in place
dotnet tool update -g JTechJapan.IntentSystem.Cli

# Verify
intent-cli --version
```

If `~/.dotnet/tools` (macOS/Linux) or `%USERPROFILE%\.dotnet\tools` (Windows) is
not on your `PATH`, the install output prints the line to add.

**No .NET SDK?** Download the self-contained binary for your platform from the
[latest GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest);
the runtime is bundled. Verify the `.sha256` sidecar before use. See the
[root README](../../README.md#install-without-a-net-sdk) for the full steps.

**Preview channel users** consuming the `preview-pack` artifact: see the
[root README preview section](../../README.md#preview-install).

## Next

Confirmed `intent-cli --version`? Continue to
[Start a project](02-project-start.md).
