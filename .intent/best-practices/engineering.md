# Engineering

## Scope

`intent-system` is a `.NET 10` CLI-first repository. Engineering guidance should favor deterministic command behavior, bounded artifacts, and repo-local validation over adding new platform surface.

## Repo-Specific Guidance

- prefer `dnx` or `dotnet tool exec` as the packaged invocation path for `intent-cli`
- keep new commands thin; compose existing command cores before adding fresh lifecycle logic
- treat `.intent-cli/` as the canonical runtime artifact root for intake, issues, runs, reviews, and queue state
- serialized artifact shape matters as much as human-readable output because downstream commands re-read these files
- command tests should use hermetic temp repos and should keep `dotnet test IntentSystem.sln` reliable
- do not introduce Node or TypeScript tooling unless an issue explicitly requires it

## Review Prompts

- does the change preserve deterministic artifact contracts?
- does it stay within the existing CLI/packet boundary instead of widening scope?
- does it keep the full .NET test baseline stable?
