# リリースノート — intent-cli v0.23.0

> **⚠️ DRAFT / 未リリース。** これは v0.22.0 後のラインに対する
> release-prep evidence です。release-prep パケットが author します。
> GitHub Release が公開されるまで、このファイルを changelog として扱ってはいけません。

このラインの install verification: `JTechJapan.IntentSystem.Cli --version 0.23.0`。
将来の Release は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.23.0 で公開されます。

## なぜこのラインは v0.23.0 なのか

v0.22.0 後の post-release roll は、このラインが存在する前に `nextVersion` を
`0.22.1` としました。release-prep は、その placeholder を `0.23.0` に retarget します。
このラインには二つの新しい command surface があり、G715 は実レコードの広い集団の
validation verdict を変更するためです:

- `intent-cli notify supervise reconcile|uninstall` は、managed supervision artifact に対する
  current-GUI-session の明示的な lifecycle surface です。
- `intent-cli guide workflow task supervision-setup` は、session-scoped supervision setup
  contract を render する新しい guide workflow task です。
- 測定された host では、`linkage-recovered` event を持つ 191 unit のうち 190 unit が、
  merged G715 head の下で invalid から valid になります。この population-wide compatibility
  change は patch-shaped ではありません。

この bump は既存 policy に従います: 新しい command surface または broad behaviour change
を含むラインは minor version を使います。これは release preparation のみであり、tag や
GitHub Release を作成せず、package を publish せず、credential を扱わず、post-release roll
も実行しません。

## Preview lane — feature description より先に読む

G710 から G720 は引き続き preview-through-1.x surface です。
[1.0 compatibility promise](1.0-compatibility-promise.md) が明示的に更新されるまでこの範囲の外であり、
minor version だけから stability guarantee を推測しないでください。

## 正確に十一件の merged feature unit

v0.22.0 tag `c06dc49e89446bf3b723612dd72004d628914734` から prepared head
`be13f7c0b9b306dad99d692903cee8837b31f0e8` までには正確に twenty-six commits があります。
post-release roll の `c48a5635` はその一つですが、**release execution unit ではありません**。
下記の inventory は正確に十一件、G710 から G720 を対象にします。first-parent accounting
には次を使いました:

```bash
git rev-list --count v0.22.0..HEAD
git log --first-parent v0.22.0..origin/main
git log --first-parent v0.22.0..main
```

count command の結果は、指定した prepared head に対して `26` でした。

- G710 — PR #1537; v0.22.0 の released verification evidence、policy-derived version check、
  bilingual release documentation を修復しました。product feature の追加ではありません。merge commit `335bb686ba966368abbdadac149bc27d9aea7c6b`。
- G711 — PR #1539; GitHub Actions OIDC による npm trusted publishing、pinned Node/npm、
  package ごとの trusted-publisher guidance、provenance を追加しました。merge commit `a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6`。
- G712 — PR #1541; session-scoped supervision reconciliation と `notify supervise reconcile|uninstall` lifecycle boundary を追加しました。merge commit `037c4acc8a02401d4bdb58b0011e05d2026dafdb`。PR #1542; reachable な `guide workflow task supervision-setup` route と documentation を復元しました。merge commit `130c99f828a6574b822203072d03554cda6a1182`。
- G713 — PR #1545; 記録された session-layer workspace の herdr pane-label topology guidance と
  validation を追加しました。merge commit `553f963439b2e3a700c2acc5800679b78d86b325`。
- G714 — PR #1548; **feature ではなく correction** です。以前の herdr-only host-state routing
  guidance を修復し、host-state boundary を truthful にしました。merge commit `c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3`。
- G715 — PR #1554; legacy publish、queue、runs shape の backward-compatible reading と、
  shipped `linkage-recovered` closeout event の読み取りを追加しました。merge commit `e25d770caacbcdafa2aa9bebea72e895dc22fcbb`。
- G716 — PR #1553; **feature ではなく correction** です。live E-versus-F probe の後、以前の
  `.git` claim は retracted/corrected されました。corrected rule は exact `<repo>/.git` root が
  measured metadata write を可能にすることを示しつつ、この recipe では least privilege を保つ
  non-sandboxed host-state routing を選びます。`git worktree add` は未測定です。merge commit `4b0a1a31b075746927d0d73c6f9b370c531e9845`。
- G717 — PR #1559; claims-enabled host では、古い `intent-issue-in-progress` label だけで worker が
  issue を所有済みとは扱いません。execution-unit claim が未保持なら preflight は進み、保持中なら
  worker を止めて owner を示します。merge commit `68c11039f4335df7799c65c814c39873132f4c68`。
- G718 — PR #1558; この line の bilingual v0.23.0 release notes と release-readiness evidence を
  prepare しました。tag、GitHub Release、package publish、credential の処理、post-release roll は
  作成・実行しません。merge commit `e7cbba0ce2d143edd19e1c60804073e41ac9401d`。
- G719 — PR #1562; host routing root に write できない implementation seat でも report を sender-local
  （seat-local）に保存して orchestration に handoff でき、external reader には delegation-level routing fault を
  表示します。merge commit `bc13c9436b98cc48aa02c4eb85cfbb99e9fab598`。
- G720 — PR #1563; Target section に authored `- Target paths: <path>` line がなければ、
  `issue validate-body` が issue creation 前に distinct diagnostic で拒否し、historical published
  body は legacy consumer で引き続き受理されます。merge commit `be13f7c0b9b306dad99d692903cee8837b31f0e8`。

prepared line の first-parent merge accounting は次の通りです:

| first-parent commit | inventory |
| --- | --- |
| `c48a5635` | v0.22.0 後の post-release roll; release execution unit ではありません |
| `335bb686ba966368abbdadac149bc27d9aea7c6b` | G710 / PR #1537 |
| `a4b4ecb7ac904b077f3cd75f2c738aa5c163ebc6` | G711 / PR #1539 |
| `037c4acc8a02401d4bdb58b0011e05d2026dafdb` | G712 / PR #1541 |
| `130c99f828a6574b822203072d03554cda6a1182` | G712 repair / PR #1542 |
| `553f963439b2e3a700c2acc5800679b78d86b325` | G713 / PR #1545 |
| `c21c2c7e2e976914eed5231148cc1f1f6cf3c5e3` | G714 / PR #1548 |
| `4b0a1a31b075746927d0d73c6f9b370c531e9845` | G716 / PR #1553 |
| `e25d770caacbcdafa2aa9bebea72e895dc22fcbb` | G715 / PR #1554 |
| `e7cbba0ce2d143edd19e1c60804073e41ac9401d` | G718 / PR #1558 |
| `ea17d02d9489c60ee96e0d088693814b1daad945` | G717 claim handoff; release execution unit ではありません |
| `68c11039f4335df7799c65c814c39873132f4c68` | G717 / PR #1559 |
| `7f0233080366c86dd449aa7873b339037a7f8f39` | G719 claim handoff; release execution unit ではありません |
| `bc13c9436b98cc48aa02c4eb85cfbb99e9fab598` | G719 / PR #1562 |
| `be13f7c0b9b306dad99d692903cee8837b31f0e8` | G720 / PR #1563; prepared head |

## Release-readiness evidence

- `eng/version.json` が single policy source です: `stableVersion` は `0.22.0`、
  `nextVersion` は `0.23.0` です。
- **Release identity evidence source revision:**
  `be13f7c0b9b306dad99d692903cee8837b31f0e8`（上で指定した exact prepared
  head。この documentation-only PR で checkout を変更する前に、この revision
  から Release build を実行しました。）
- Release build の command は次の通りです:

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj -c Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
```

表示された identity は正確に `intent-cli 0.23.0-be13f7c-G718` でした。
- その build から二つの新 command surface を独立に probe しました:

```bash
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll notify supervise reconcile --help
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll guide workflow task supervision-setup --format json
```

前者は reconcile/uninstall usage を表示して exit 0、後者は
`metadata_free: true` と `read_only: true` を含む contract を表示して exit 0 でした。
- focused release-note documentation と version-source policy guard は次の command で実行しました:

```bash
dotnet test tests/IntentSystem.Cli.Tests/IntentSystem.Cli.Tests.csproj --configuration Release --no-build --no-restore -p:IsTestProject=true --filter 'FullyQualifiedName~ReleaseNotesV0230DocsTests|FullyQualifiedName~VersionSourcePolicyGuardTests'
```

**12 passed、0 skipped、0 failed、total 12** でした。
- eleven test project を対象にした full Release sweep は **5,474 passed、1 skipped、2 failed、total 5,477** でした。
  failure は既存の `PackagedInvocationSmokeTests` だけで、child `dotnet pack` が shared NuGet vulnerability cache を
  更新できず (`NU1900`、permission denied) に失敗しました。この documentation/test edit の failure ではありません。
- `docs/en/release-notes-v0.22.1.md` と `docs/ja/release-notes-v0.22.1.md` は superseded のため削除し、
  この 0.23.0 notes を bilingual replacement にします。

## Prepare-only boundary

この change set は prepared bilingual release documentation とその documentation test だけを変更します。tag や GitHub Release を
作成せず、package を publish せず、credential を扱わず、post-release roll を実行しません。
公開後の post-release roll は別の action です: `stableVersion` を公開済み version にし、
`nextVersion` を次の patch にし、次の DRAFT stub を追加し、両言語の readiness section を更新して、
main CI が green であることを確認します。
