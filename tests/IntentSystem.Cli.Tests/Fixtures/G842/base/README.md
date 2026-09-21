# G842 back-compat goldens

These files were generated from merge base `cbe5475977d151f953d1235733e6d9688e0296f1`.

From the detached base checkout, run the env-gated capture facts with:

```text
G842_CAPTURE=1 dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj -c Release --filter 'FullyQualifiedName~G842BackCompatGoldenTests.CaptureMergeBaseGoldens|FullyQualifiedName~G842Ac13GoldenTests.CaptureMergeBaseAc13Goldens'
```

The fixture host pins the domain, repository, PR, head, execution unit, packet bytes,
queue bytes, runtime version, and verdict bytes. It uses no GitHub or provider process.
`{{G842_WORKSPACE_ROOT}}` replaces the temporary host root in JSON, Markdown, and all
rendered files. Runtime-generated ISO and compact record timestamps are replaced by
`{{G842_TIMESTAMP}}` and `{{G842_COMPACT_TIMESTAMP}}`; the older base has no clock seam.
The no-declaration comment golden is intentionally empty because that command refuses
before producing a comment. The AC13 files use a fixed `IssuePublishFlowCommand` clock.
All other bytes are compared exactly.
