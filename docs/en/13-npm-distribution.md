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

## Release integrity

The release workflow derives one version from the published Git tag and uses
it for the NuGet package, every npm package, every self-contained binary, and
the binary `--version` output. Each platform npm package records a SHA-256
digest and includes a matching `.sha256` sidecar. Platform packages are
published only as part of the same guarded operator release transaction as
NuGet. Pull-request CI runs package preparation, `npm pack`, checksum/version
verification, and a packed-install smoke test; it never publishes to npm and
does not require npm organization credentials.

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
