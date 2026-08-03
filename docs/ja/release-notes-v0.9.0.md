# リリースノート — intent-cli v0.9.0

> **リリースモデル:** この変更は **prepare-only** です。GitHub Release / tag の作成、
> package publish、post-release version roll、product behavior の変更は行いません。この準備が
> merge され、下記 readiness gate を満たした後も、Release 作成には operator の明示承認が
> 必要です。GitHub Release の publish により `.github/workflows/release.yml`
> (`on: release: published`) が起動し、NuGet package と platform artifact を build / publish
> します。

## v0.9.0 の内容

v0.9.0 が `v0.8.1` 以降から含む merged slice は **G591** と **G592** の正確に 2 件です。
各 PR と merge commit は次のとおりです。

- **G591** — [PR #1286](https://github.com/J-Tech-Japan/intent-system/pull/1286)、
  “G591: make host-local CLI refresh fail closed”、
  merge [`d73072668c7bda24b124ca46d65c805ed561b740`](https://github.com/J-Tech-Japan/intent-system/commit/d73072668c7bda24b124ca46d65c805ed561b740)。
- **G592** — [PR #1288](https://github.com/J-Tech-Japan/intent-system/pull/1288)、
  “G592: Add canonical session-layer topology commands”、
  merge [`a18ecefc9dd6aae9c551f424edfdb00752cc9d91`](https://github.com/J-Tech-Japan/intent-system/commit/a18ecefc9dd6aae9c551f424edfdb00752cc9d91)。

release preparation で、両 merge commit が `main` 上にあることを検証済みです。この 2 件により
**herdr-only は hand-authored state を必要としなくなり**、同じ operational risk を閉じます。
intent-cli が依存する durable state は intent-cli の writer が所有するか、atomic かつ
fail-closed な置換 path によって保護されます。

**patch ではなく minor とする理由。** 文書化済み version policy は、新しい command surface
または広範な behavior change に **MINOR** bump を予約しています。G592 が新しい
`session-layer topology` command group を追加するため、開発中だった `0.8.2` patch line を
`0.9.0` へ retarget します。`v0.8.2` Release は作成されず、superseded となった EN/JA の
draft stub はこの preparation と同時に削除します。

### host-local CLI refresh が working install を保護 (G591)

host-local refresh script は生成 package を CLI project の package id で解決し、local candidate
version を `eng/version.json` から導出します。package と temporary wrapper を隔離した candidate
location で build / verify し、wrapper version と required automation-summary capability まで
確認してから、最後に atomic promotion を行います。

candidate check が 1 つでも失敗した場合、refresh は failed check と remedy を報告し、以前から
install 済みの wrapper / package を byte-for-byte で維持し、candidate / temporary artifact を
削除します。refresh failure が working host-local CLI install を破壊することはなくなりました。

### delivery topology に canonical writer / validator を追加 (G592)

team delivery mapping
`<host-repo>/.intent-cli/role-pane-mapping.json` に canonical command surface が追加されました。

- `intent-cli session-layer topology validate --team <team> --format json` は recorded topology を
  読み、machine-readable な `valid` answer と全 finding を 1 invocation で返します。missing /
  unsupported residence、missing pane id、unsafe external reader、workspace mismatch、unreadable /
  absent topology の各 finding は role と field を明記します。
- `intent-cli session-layer topology record --team <team> ... --write` は operator-supplied な
  herdr role（`workspace_id`、`pane_id`、`cwd`、optional kind）または external role
  （routing-root-relative reader、optional frontend）を記録します。完全一致は idempotent no-op、
  異なる既存 role は silent repair せず refuse します。
- `intent-cli session-layer topology show --team <team> --format json` は送信せず、各 recorded
  residence と解決済み pane / reader を報告します。`notify` と同じ delivery-target resolution
  function を使うため、両 surface が delivery 先について食い違うことはありません。

これらの command は herdr query、id の guess、resource provision、conflict の auto-repair、
delivery fallback の追加を行いません。mapping が存在するか herdr-only scope が必要とする場合、
`automation doctor` も invalid topology health を報告し、notify の topology refusal は remedy として
`topology validate` / `record` を示します。fail-closed delivery semantics は変更されません。

## v0.8.1 は silent release として出荷済み

`v0.8.1` は operator decision により **silent release** として publish され、v0.9.0 と合わせて
announcement されます。独立した v0.8.1 announcement を見ていない reader は、5 件の
wake-reliability slice について
[v0.8.1 リリースノート](release-notes-v0.8.1.md)を参照してください。その 5 件はここで
restatement せず link のみにし、上記 v0.9.0 の 2-slice scope には含めません。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.9.0
```

operator が承認・publish した後は、self-contained binary を
[v0.9.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.9.0)
から取得できます。使用前に `.sha256` sidecar を検証してください。

## v0.8.1 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.0
```

既存 command / argument / flag / topology schema / topology location / session-layer mode の
削除・改名はありません。新しい `session-layer topology` group は additive です。既存 notify
routing は fail-closed のままで fallback は増えません。host-local refresh の safety change は
candidate CLI install の verification / promotion のみに作用し、成功時の refresh behavior は同じです。

## リリース準備ゲート (G593)

以下は **`v0.9.0` GitHub Release を作成または publish する前**に満たす必要があります。この gate
は fail closed です。

- [ ] 上記 shipped 2 slices が、正確に PR #1286 /
      `d73072668c7bda24b124ca46d65c805ed561b740` と PR #1288 /
      `a18ecefc9dd6aae9c551f424edfdb00752cc9d91` で `main` に merge 済みであり、
      notes に追加の shipped slice が含まれていない。
- [ ] EN/JA 両方の `release-notes-v0.9.0.md` が parity のある real notes で DRAFT-stub banner が
      残らず、superseded な両 `release-notes-v0.8.2.md` が存在しない。
- [ ] `eng/version.json` が同じ exact preparation head 上で `stableVersion` `0.8.1` /
      `nextVersion` `0.9.0` を記録している。
- [ ] MINOR rationale が新しい `session-layer topology` command group を明記し、silent v0.8.1
      release は restatement せず link している。
- [ ] G475 next-version notes/package guard、release-note/version-policy guard、full Release suite が
      test flip なしで exact G593 preparation head 上 green。
- [ ] package metadata が正しい: `PackageId = JTechJapan.IntentSystem.Cli`、repository/project URL が
      この repository を指す、license が `Apache-2.0`、README/docs link が解決する。
- [ ] exact-head main CI (`Build and test (source contract)`) が green で、preparation PR に
      verification evidence が記録されている。
- [ ] operator が `v0.9.0` Release 作成を明示承認している。

## v0.9.0 の publish

この preparation の merge は GitHub Release / tag の作成、package publish、post-release roll の
いずれも行いません。merge 後に readiness evidence を確認し、operator が Release 作成を明示承認
する必要があります。

その後に限り、maintainer/operator（または明示 authorize された外部 release automation）が
`v0.9.0` GitHub Release を作成・publish できます。publish が `release.yml` を trigger し、
NuGet package と platform archive を publish します。

post-release verification:

- [ ] NuGet / GitHub asset link が解決し、download checksum が一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.0` 後の
      `intent-cli --version` が `0.9.0` を報告する。
- [ ] platform archive の checksum が通り、binary が `0.9.0` を報告する。
- [ ] publish 後、別途 authorize された immediate roll を `stableVersion → 0.9.0` /
      `nextVersion → 0.9.1` へ行い、新しい EN/JA DRAFT stub、refresh 済み readiness section、
      green な post-roll child-main CI を揃える。この roll は G593 の一部ではありません。
