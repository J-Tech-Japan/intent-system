# G282 Support global dotnet tool intent-cli in automation doctor and guide preflight

## Why this slice

Local testing is moving to a single global `intent-cli` installed from latest
`main` (e.g. `$HOME/.dotnet/tools/intent-cli`). After removing the cwd-local
`.intent-cli/bin/intent-cli` shim to avoid mixed versions across review
folders, `intent-cli automation doctor --format json` still reported
`stale-host-cli` because it required the cwd-local shim — even though
`command -v intent-cli` resolved to the global tool and the command surface
was available.

## What changed

`AutomationInstalledCliSurfaceProbe` now resolves the binary in priority
order:

1. **Explicit override** — `INTENT_CLI_INSTALLED_PATH` env var (when set
   and the file exists). Lets operators pin a specific binary for
   version-specific tests. Reported as `binary_source: explicit-override`.
2. **Cwd-local shim** — `.intent-cli/bin/intent-cli` under the host data
   root, when present. Legacy default; pins the exact binary used by
   automation in this checkout. Reported as `binary_source: cwd-local-shim`.
3. **PATH global tool** — first `intent-cli` (or `intent-cli.exe`) found on
   `PATH`. The default local-testing route once the cwd-local shim is
   removed. Reported as `binary_source: path-global-tool`.

If none of the three is found, the probe returns the canonical cwd-local
path and `binary_source: missing`; the doctor reports `stale-host-cli`.

`automation doctor` output gains two new fields:

- `binary_source` — one of `explicit-override` / `cwd-local-shim` /
  `path-global-tool` / `missing`. Lets operators tell apart "I'm running
  the global tool" from "I'm running this checkout's pinned shim".
- `host_data_root` — absolute path to the cwd-relative `.intent-cli`
  directory (the data root for queue/runs/packets). Surfaced separately
  so the binary path and the data root are no longer conflated in
  operator output.

The host-loop and child-loop guide preflight wording now states that the
installed CLI may come from a global dotnet tool install on `PATH`
(default local-testing route), naming the three `binary_source` values
explicitly.

## Boundaries

- `dotnet run` fallback remains forbidden in automation loops.
- The `.intent-cli` data root semantics are unchanged; only the binary
  resolution changed.
- Stale detection still fires when the resolved binary is missing
  required command surfaces (regardless of whether it came from PATH or
  the cwd-local shim).

## Verification

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --filter "FullyQualifiedName~AutomationDoctor"

git diff --check
```

Focused tests cover: global dotnet tool on PATH satisfies the doctor when
the cwd-local shim is absent; neither cwd shim nor PATH binary returns
`stale-host-cli` with `binary_source: missing`; PATH-global-tool with a
stale surface still reports `stale-host-cli` and keeps
`binary_source: path-global-tool`; explicit `INTENT_CLI_INSTALLED_PATH`
wins over both the cwd shim and PATH (`binary_source: explicit-override`);
cwd-local shim presence is reported as `binary_source: cwd-local-shim`.
