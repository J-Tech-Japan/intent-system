# npm distribution

← [docs index](README.md) | → [developer reference](09-developer-reference.md)

G702 makes the npm distribution an installation interface for `intent-cli`.
The unscoped `intent-system` package is a thin entry point; its optional
platform dependency supplies a self-contained release binary for macOS Apple
Silicon, Linux x64, or Windows x64.

## Persistent and one-shot use

For a persistent command, install globally and then invoke the command:

```bash
npm install -g intent-system
intent-cli --version
```

For a one-shot command, use npx:

```bash
npx intent-system guide onboarding
```

The shim detects npx from the npm user agent and checks whether `intent-cli` is
already on `PATH`. If both conditions say this is a one-shot invocation, it
prints exactly one concise line after the command suggesting
`npm install -g intent-system`, followed by the resulting `intent-cli` command.
It never performs that installation itself. There is no `postinstall` hook and
no package-install network download.

## Channel-aware update (G703)

`intent-cli update` derives the channel from the fully resolved real path of the
running executable on every invocation. It does not persist a channel marker.
The detection line names both the channel and the path evidence before an
update action starts:

```bash
intent-cli update
```

The supported actions are:

- .NET global tool: `dotnet tool update -g JTechJapan.IntentSystem.Cli`.
- npm global: `npm install -g intent-system@latest`.
- npx cache: guidance only — rerun the command with `npx intent-system@latest`;
  the CLI does not mutate the cache or install globally.
- standalone binary: download the matching release archive, verify its
  `.sha256` sidecar, then replace the binary through a same-volume temp+rename
  swap. A checksum mismatch leaves the original binary byte-identical.

Use the no-effect check from any metadata-free directory:

```bash
intent-cli update --check --format json
intent-cli update --check --format markdown
```

The check reports the current version, latest release, and would-be action;
`process_spawned=false` and `writes_performed=false` are part of the result.
Unknown or ambiguous executable paths fail closed and print manual guidance for
all four channels rather than guessing.

## Release integrity

The release workflow derives one version from the published Git tag and uses
it for the NuGet package, every npm package, every self-contained binary, and
the binary `--version` output. Each platform npm package records a SHA-256
digest and includes a matching `.sha256` sidecar. Platform packages are
published only as part of the same guarded operator release transaction as
NuGet. Pull-request CI runs package preparation, `npm pack`, checksum/version
verification, and a packed-install smoke test; it never publishes to npm and
does not require npm organization credentials.

## Trusted publishing for releases (G711)

The npm publish job runs only for a published GitHub Release, after the same
tag-derived packaging, checksum verification, and release-asset steps used by
the other distribution jobs. It authenticates with GitHub Actions OIDC
trusted publishing. The job is explicitly pinned to Node `22.14.0` and npm
`11.5.1`; it has job-scoped `id-token: write` permission and stores no npm
token or registry credential in this repository.

Before the first release, an operator must perform the one-time npmjs.com
trusted-publisher registration separately for each package:

- `intent-system`
- `@j-tech-japan/intent-cli-darwin-arm64`
- `@j-tech-japan/intent-cli-linux-x64`
- `@j-tech-japan/intent-cli-win32-x64`

For each package, select GitHub Actions as the trusted publisher and register
the `J-Tech-Japan/intent-system` repository with the workflow file
`.github/workflows/release.yml`. These are operator account actions; this
repository neither creates the npm organization nor stores a publish secret.
Successful GitHub Actions trusted publishes receive npm provenance
attestations automatically, which consumers can verify from the published
package metadata.

If OIDC authentication or the per-package registration is missing, the
release job fails with the package name and the missing trusted-publisher or
`id-token: write` configuration. It never reports a successful skip. A
workflow-dispatch dry run still prepares, packs, checksums, and verifies the
packages without attempting publication.

## Coexisting with the .NET tool

The npm route and the .NET global tool can coexist:

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli
# or: npm install -g intent-system
command -v intent-cli
intent-cli --version
```

Both routes install a command named `intent-cli`. The first matching directory
on `PATH` wins, so choose the intended channel by ordering
`$HOME/.dotnet/tools` and the npm global bin directory, or use the package
manager's update command for that channel. Do not mix a stale binary with a
new package when diagnosing version differences.
