# G866 VSTest capture

`small-vstest.trx` is an unedited VSTest TRX captured once by running the existing
G865 command test filter. It contains two genuine result/entry/definition GUID
chains and is embedded as a regression fixture; G866 does not launch a test
process.

Capture invocation:

```sh
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj \
  --configuration Release --no-restore \
  --filter 'FullyQualifiedName~PacketVerificationContractTests.ActualCommandReportsCompleteTwoProofContractWithoutClaimingExecution' \
  --logger 'trx;LogFileName=g866-small-capture.trx' \
  --results-directory /private/tmp/g866-capture-results
```

The capture used .NET SDK `11.0.100-preview.3.26207.106` and produced two
passing results. The committed bytes have SHA-256
`05806d94d6d671b5aa58200faeb2092a09cfef07cd6b3b7a0e82cc5aef9a95ef`.

`full-cli-vstest.trx.gz` is a byte-preserving gzip copy of the original 14.9 MiB
`IntentSystem.Cli.Tests.trx` produced by the G865 final 11-project Release run
at source HEAD `6faecf8d7c6805bf7239b134d470336ca993d1a6`. That real report has
9,543 CLI rows (9,537 Passed, 6 NotExecuted), a declared `notExecuted=0`, and
four `UnitTest`/`UnitTestResult` display-name differences. The uncompressed
original SHA-256 is
`5a62eb1560ad835ac646bc2a54a03018c4e5c919253dcb0367faeb245dca7b46`; the
committed gzip SHA-256 is
`a000d14fb45734abb7018e3d4e6bdbcf800572648115a8745e711ca77c885ba3`.
