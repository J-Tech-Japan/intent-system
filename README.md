# intent-system

## Packaged invocation

The CLI is packaged as a .NET tool with:

- package id: `intent-cli`
- command name: `intent-cli`

Local package smoke path:

```bash
dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj -o .artifacts/packages
dotnet tool exec --yes --source .artifacts/packages --version 0.1.0 intent-cli project status
```

Equivalent `dnx` path:

```bash
dnx --yes --source .artifacts/packages --version 0.1.0 intent-cli project status
```
