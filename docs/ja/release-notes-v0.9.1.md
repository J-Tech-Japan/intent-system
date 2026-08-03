# リリースノート — intent-cli v0.9.1

> **リリースモデル:** この変更は **prepare-only** です。GitHub Release や tag の
> 作成、package の publish、リリース後の version roll、アナウンス、product behavior
> の変更は行いません。この準備が merge され、下記 readiness gate を満たした後も、
> Release 作成には operator の明示承認が必要です。GitHub Release の publish が
> `.github/workflows/release.yml`（`on: release: published`）を trigger し、NuGet
> package と platform artifact を build/publish します。

## v0.9.1 の内容

v0.9.1 が `v0.9.0` 以降から含む merged correctness slice は **G594** の正確に
1 件です。

- **G594** — [PR #1292](https://github.com/J-Tech-Japan/intent-system/pull/1292)、
  「G594: make session-layer readiness record-first」、
  merge [`022e7ec2acbc354aeb6a054f675165ba0e2a9238`](https://github.com/J-Tech-Japan/intent-system/commit/022e7ec2acbc354aeb6a054f675165ba0e2a9238)。

release preparation 中に、この merge commit が `main` の ancestor であることを検証
しました。この notes に含める shipped slice はほかにありません。

**MINOR ではなく PATCH である理由。** merge 済み実装のinspectionにより、G594が新しい
user-facing command surfaceを追加していないことを確認しました。共有record-first
preflightは既存の`automation doctor`、guide、`notify` surfaceから利用されます。
doctor checkはpairedな`--domain`と`--team`がoptionalなので、named scopeに対して
opt-inです。既存のunscoped doctor呼び出しとCI jobはstatus behaviorを維持します。

release preparationでは、`config.toml`と`host-binding.toml`だけがあり
session-layer mode recordがない同一host rootを使用しました。source buildしたmerged CLI
の実測値は次のとおりです。

```text
intent-cli automation doctor --domain intent-cli --team sekiban-workers --format json
→ exit 1; status: session-layer-not-ready; preflight verdict: configuration-incomplete

intent-cli automation doctor --format json
→ exit 0; status: ok; preflight verdict: unjudged
```

したがってscoped checkはfalse-green readiness gapを閉じますが、既存のunscoped callerを
反転させません。この実測互換性がPATCH根拠であり、Issue labelやplanning textからの
推測・転記ではありません。

### readinessはrecord-first、delivery identityは運用可能に（G594）

単一のmachine-readableなsession-layer preflightが、`automation doctor`、
orchestrator guideのREADY定義、`notify`で共有されるようになりました。mode recordがない
named teamはvacuous greenではなくconfiguration-incompleteになり、topologyをrecorded
modeと相関し、矛盾はinferenceやrepairをせずdiagnostic-onlyとして扱います。

herdr-only deliveryではlogical roleをrecorded workspaceとpaneの組で解決します。
logical roleはmachine全体でglobally uniqueなherdr agent nameと一致する必要がなくなり
ました。これにより1台のmachine上で複数teamが、それぞれ異なるglobal agent nameを
維持しながらcanonical logical roleを使用できます。この衝突をproduction useから報告し、
G594へ組み込む独立検証の契機を作った **sekiban design thread** にcreditします。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.9.1
```

operatorが承認・publishした後、self-contained binaryは
[v0.9.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.9.1)
から取得できます。使用前に`.sha256` sidecarを検証してください。

## v0.9.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.1
```

command、argument、flag、session-layer mode、topology schema、record writerの削除・改名は
ありません。PREVIEW herdr-only pathにある、既存callerに対して非破壊な変更2件を明示
します。

1. **`delivered: true`の条件が厳密になります。** settled状態のherdr recipientでは、
   boundedに観測したunattended working transitionと、それに続くfresh settled
   acknowledgementが必要です。composerに残った未submit promptをdeliveredとは報告
   しません。already-working recipientは引き続きdeliverableで、working transitionは
   `unobservable`と報告します。
2. **`automation doctor`にscoped preflightが加わります。** optionalな
   `--domain <d> --team <t>`の組を指定すると、そのnamed teamをrecord-firstで判定します。
   省略時はanonymousなrecord-less rootをunjudgedとし、上記の既存`ok` statusを維持
   します。

**herdr-only session transportは引き続きPREVIEW**です。agmsg transportとdelivery
behaviorは変更せず、**agmsgは引き続きPRIMARY**です。PREVIEWが修飾するのはtransport
だけで、four-thread modelではありません。

## リリース準備ゲート（G595）

以下は **`v0.9.1` GitHub Releaseを作成またはpublishする前**に満たす必要があります。
このgateはfail closedです。

- [ ] このnotesのshipped sliceはG594だけで、PR #1292の
      `022e7ec2acbc354aeb6a054f675165ba0e2a9238`としてmergeされ、そのcommitが`main`の
      ancestorである。
- [ ] EN/JA両方の`release-notes-v0.9.1.md`がparity-matchedなreal notesで、
      DRAFT-stub bannerがなく、非破壊な変更2件を開示している。
- [ ] `eng/version.json`が`stableVersion` `0.9.0`、`nextVersion` `0.9.1`のまま
      byte-unchangedである。
- [ ] 同一のrecord-less hostに対する実測が、scoped doctorで
      `session-layer-not-ready`、unscoped doctorで`ok`を返す。
- [ ] G475 next-version notes/package guard、release/version guard、full Release suite、
      build、pack、diff checkがexact G595 preparation headでgreenである。
- [ ] package metadataが正しい: `PackageId = JTechJapan.IntentSystem.Cli`、
      repository/project URLはこのrepository、licenseは`Apache-2.0`、README/docs linkは
      resolveする。
- [ ] exact-head GitHub CI（`Build and test (source contract)`）がgreenで、preparation PR
      にverification evidenceが記録されている。
- [ ] operatorが`v0.9.1` Release作成を明示承認している。

## v0.9.1 のpublish

このpreparationをmergeしても、GitHub Release/tagの作成、package publish、
アナウンス、`0.9.2`へのroll、新しいDRAFT stubの作成、product behavior changeは行い
ません。merge後にreadiness evidenceを確認し、operatorがRelease作成を明示承認する
必要があります。

その後に限りmaintainer/operator（または明示承認済みのexternal release automation）が
`v0.9.1` GitHub Releaseを作成・publishできます。publishが`release.yml`をtriggerし、
NuGet packageとplatform archiveをpublishします。

リリース後の検証項目:

- [ ] NuGetとGitHub assetのlinkがresolveし、downloadしたchecksumが一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.9.1`後の
      `intent-cli --version`が`0.9.1`を報告する。
- [ ] platform archiveのchecksum検証が成功し、そのbinaryが`0.9.1`を報告する。
- [ ] publish後にのみ、別途承認されたimmediate version rollを行い、次のEN/JA DRAFT
      stubを作成する。このpost-release workはG595に含まれない。
