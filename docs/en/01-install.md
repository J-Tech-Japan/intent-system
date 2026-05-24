# Install

> **Ask intent-cli first.** After installing, run `intent-cli guide start` before
> doing any workflow work. ← [docs index](index.md)

The basic path is the .NET global tool from NuGet.org (requires a **.NET 10
SDK**; check with `dotnet --version`). The same commands work on macOS, Windows,
and Linux:

```bash
# Install
dotnet tool install -g intent-cli

# Upgrade in place
dotnet tool update -g intent-cli

# Verify
intent-cli --version
```

If `~/.dotnet/tools` (macOS/Linux) or `%USERPROFILE%\.dotnet\tools` (Windows) is
not on your `PATH`, the install output prints the line to add.

**No .NET SDK?** Download the self-contained binary for your platform from the
[latest GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest);
the runtime is bundled. Verify the `.sha256` sidecar before use. See the
[root README](../../README.md#install-without-a-net-sdk) for the full steps.

**Internal testers** consuming the `private-preview-pack` artifact: see the
[root README private-preview section](../../README.md#private-preview-install).

## Next

Confirmed `intent-cli --version`? Continue to
[Start a project](02-project-start.md) — but run `intent-cli guide start` first.
