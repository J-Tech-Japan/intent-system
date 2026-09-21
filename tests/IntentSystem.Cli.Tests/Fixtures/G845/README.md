# G845 sync-body dry-run goldens

Captured from merge base `103f4b869d4b5b39170e65e3230e3ac35c7c2aaf` (`103f4b86`) by checking out that SHA in an isolated scratchpad checkout and running the same four published-fixture cases with `G845BaseGoldenCaptureTestsTemp` under `dotnet test --configuration Release -p:NuGetAudit=false`. Only temporary workspace paths and timestamp fields are eligible for normalization; these dry-run outputs contained neither, so no captured bytes were normalized.
