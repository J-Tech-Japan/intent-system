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

**npm entry point (no .NET SDK required):** install the self-contained
platform package through npm, or run one command with npx:

```bash
npm install -g intent-system
intent-cli --version
npx intent-system guide onboarding
```

The npm shim never installs globally on your behalf. See the
[npm distribution guide](13-npm-distribution.md) for the one-line npx guidance,
checksums, release gating, and coexistence rules when the .NET tool is also on
`PATH`.

**No .NET SDK?** Download the self-contained binary for your platform from the
[latest GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/latest);
the runtime is bundled. Verify the `.sha256` sidecar before use. See the
[developer reference](09-developer-reference.md#install-without-a-net-sdk) for the full steps.

**Preview channel users** consuming the `preview-pack` artifact: see the
[developer reference preview section](09-developer-reference.md#preview-install).

## Stable 0.32.1 (prepared, not yet available)

This section documents the planned stable version; it does not publish it. Until
the exact [v0.32.1 GitHub release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1)
is actually published, the public stable version remains `0.32.0`, and the
pinned commands below are not yet installable. The .NET tool requires the
.NET 10 SDK. After publication, an isolated install keeps an existing global
stable tool untouched:

```bash
dotnet tool install --tool-path <isolated-directory> JTechJapan.IntentSystem.Cli --version 0.32.1
<isolated-directory>/intent-cli --version
```

After publication, the explicit npm choice is:

```bash
npm install -g intent-system@0.32.1
intent-cli --version
```

After the stable release is published, a preview user can deliberately move to
stable by selecting `0.32.1` with `dotnet tool update -g
JTechJapan.IntentSystem.Cli --version 0.32.1` or the npm command above. Under
SemVer, stable `0.32.1` follows the prerelease `0.32.1-preview.1`; select the
stable version explicitly rather than relying on an inferred default.

A global npm install can replace the package in that npm prefix. The stable
release will include self-contained macOS-arm64, Linux-x64, and Windows-x64
assets. Verify each downloaded asset against its `.sha256` sidecar before use.
Use `command -v intent-cli` (macOS/Linux) or `where intent-cli` (Windows), then
`intent-cli --version`, because the .NET tool, npm shim, and native binary share
the command name and the first `PATH` match runs.

Actual stable publication moves npm `latest` to `0.32.1`; the existing
`0.32.1-preview.1` version and `preview` tag remain separate. Until publication,
`latest` remains stable `0.32.0`. To roll an npm global install back deliberately,
run `npm install -g intent-system@0.32.0`; to roll back a .NET global tool,
run `dotnet tool update -g JTechJapan.IntentSystem.Cli --allow-downgrade --version 0.32.0`. For
an isolated .NET tool-path install, remove its directory and continue using the
existing stable command.

See the [stable 0.32.1 release notes](release-notes-v0.32.1.md) for the
measured scope and publication boundary. The [preview notes](release-notes-v0.32.1-preview.1.md)
remain a separate record of the published preview.

## Optional 0.32.1-preview.1 install

The version-specific commands below are opt-in instructions for after
`0.32.1-preview.1` is published. This documentation change does not make the
version available. The .NET tool requires the .NET 10 SDK. To keep the stable
global tool untouched, install the preview into a separate directory; replace
`<isolated-directory>` with a directory you own before running this command:

```bash
dotnet tool install --tool-path <isolated-directory> JTechJapan.IntentSystem.Cli --version 0.32.1-preview.1
```

Run the installed command by its full path and check which version is active:

```bash
<isolated-directory>/intent-cli --version
```

The npm entry point is another explicit user option. A global npm install can
replace the `intent-system` package already installed in that npm prefix:

```bash
npm install -g intent-system@0.32.1-preview.1
intent-cli --version
```

The explicit version selects this preview; `latest` remains the stable channel.
To return an npm global install to stable 0.32.0 deliberately, run
`npm install -g intent-system@0.32.0`. For the isolated .NET installation,
remove its directory and continue using the existing stable command.

After publication, the exact [GitHub release page for 0.32.1-preview.1](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)
is the destination for the self-contained macOS-arm64, Linux-x64, and
Windows-x64 assets. Verify each asset against its matching `.sha256` sidecar
before running it. Do not treat a preview-pack artifact or workflow-dispatch
candidate as the public release.

The .NET global tool, npm shim, and standalone binary all use the command name
`intent-cli`; the first matching `PATH` entry wins. For an isolated preview,
invoke the full tool-path shown above. For another installation, use
`command -v intent-cli` (macOS/Linux) or `where intent-cli` (Windows), then
`intent-cli --version`, before assuming which channel you are running.

The preview does not change stable 0.32.0 defaults. No-opt-in verification and
lesson behavior stays unchanged; the worker evidence gate requires its explicit
flags and does not infer policy from a team default. Local report provenance is
supplied by the caller, not authenticated. Lesson enforcement text is advisory
and declared-not-verified. The documentation preparation PR is not a product
publication or rollback guarantee; a brief output failure can leave a partial
or foreign residual that needs separate inspection. Raw TRX reports can embed
test names, output, or machine paths, and a whole-registry digest can reveal
that registry identity, so inspect those disclosures before committing or
publishing them.

See the [0.32.1-preview.1 release notes](release-notes-v0.32.1-preview.1.md)
for the measured scope, limits, and publication sequence.

## Next

Confirmed `intent-cli --version`? Continue to
[Start a project](02-project-start.md).
