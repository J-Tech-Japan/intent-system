# Contributing to intent-cli

Thank you for your interest in contributing. This guide covers how to report
bugs, suggest features, improve documentation, and submit pull requests.

## Ask intent-cli first

**This is the core operating rule.** Before you change workflow labels, packet
metadata, automation state, or host artifacts — ask `intent-cli` for the current
guidance rather than editing things by hand. The CLI exists so you never have to
guess at the label/metadata contract.

```bash
intent-cli guide help
intent-cli guide commands list --format json
```

If you are contributing as an AI agent: read the GitHub issue body as your
standalone contract. Do not read or mutate the parent host's queue-state, packet
directories, or intent tree. Use `intent-cli worker` commands for all label
transitions; never use raw `gh ... edit --add-label`.

## Ways to contribute

- **Bug reports** — open a GitHub issue using the Bug Report template. Include
  your `intent-cli --version`, operating system, install method, and the exact
  command that failed.
- **Feature suggestions** — open a GitHub issue using the Feature Request
  template. Describe the problem you are trying to solve, not just the solution.
- **Documentation improvements** — edit files under `docs/` and open a PR. Run
  `git diff --check` before submitting.
- **Code contributions** — see the sections below.

## Setting up the development environment

Requirements:
- .NET 10 SDK (`dotnet --version` should report `10.x`)
- Git

```bash
git clone https://github.com/J-Tech-Japan/intent-system.git
cd intent-system
dotnet restore IntentSystem.sln
dotnet build IntentSystem.sln --configuration Release
dotnet test IntentSystem.sln --configuration Release
```

## Pull request expectations

- Base your PR on `main`.
- Keep each PR focused on one change. Smaller PRs are reviewed faster.
- Include tests for new behavior. The test suite lives under `tests/`.
- Run `git diff --check` to catch whitespace errors before submitting.
- Write a clear PR description explaining **why** the change is needed, not just
  what it does.
- Add a closing reference to the related issue (`Closes #N`) in the PR body.

## Coding conventions

- Language: C# / .NET 10
- No comments unless the reason is non-obvious (a hidden constraint, a subtle
  invariant, a workaround for a specific bug).
- Do not introduce Node/TypeScript toolchain; this project is .NET-only.
- Do not add features beyond the issue scope.

## Tests

Run the full test suite from the repo root:

```bash
dotnet test IntentSystem.sln --configuration Release
```

To run a focused set:

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~YourTestClass"
```

Note: the test project references sibling projects and requires
`dotnet restore IntentSystem.sln` before running in isolation.

## Code of conduct

Please read [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md). We expect all
participants to adhere to it.

## License

By contributing, you agree your contributions will be licensed under the
[Apache-2.0 license](LICENSE).
