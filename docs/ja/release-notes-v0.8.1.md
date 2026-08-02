# リリースノート — intent-cli v0.8.1

> **リリースモデル:** この変更は **prepare-only** です。GitHub Release / tag の作成、
> package publish、version policy の roll、product behavior の変更は行いません。この準備が
> merge され、下記 readiness gate を満たした後も、Release 作成には operator の明示承認が
> 必要です。GitHub Release の publish により `.github/workflows/release.yml`
> (`on: release: published`) が起動し、NuGet package と platform artifact を build / publish
> します。

## v0.8.1 の内容

v0.8.1 が `v0.8.0` 以降から含む correctness slice は、マージ済みの 5 件 **G585**、
**G586**、**G587**、**G588**、**G589** だけです。各 PR と merge commit は次のとおりです。

- **G585** — [PR #1274](https://github.com/J-Tech-Japan/intent-system/pull/1274)、
  “G585: disclose omitted session-layer team”、
  merge [`9e87d0973f53bd56e03af0622c54dd4d505eedbd`](https://github.com/J-Tech-Japan/intent-system/commit/9e87d0973f53bd56e03af0622c54dd4d505eedbd)。
- **G586** — [PR #1276](https://github.com/J-Tech-Japan/intent-system/pull/1276)、
  “G586: restore herdr pane-first topology”、
  merge [`0e93973c95cd992dbceda1c8416818a20ab1c319`](https://github.com/J-Tech-Japan/intent-system/commit/0e93973c95cd992dbceda1c8416818a20ab1c319)。
- **G587** — [PR #1278](https://github.com/J-Tech-Japan/intent-system/pull/1278)、
  “G587: make packet readiness answer in one shot”、
  merge [`bdadd711f6e6ed9960a2542f82369c886e3bb163`](https://github.com/J-Tech-Japan/intent-system/commit/bdadd711f6e6ed9960a2542f82369c886e3bb163)。
- **G588** — [PR #1280](https://github.com/J-Tech-Japan/intent-system/pull/1280)、
  “Fix notify routing for recorded external roles”、
  merge [`e4f18f50c31ddb327f598d6fe5017a7674e1685b`](https://github.com/J-Tech-Japan/intent-system/commit/e4f18f50c31ddb327f598d6fe5017a7674e1685b)。
- **G589** — [PR #1282](https://github.com/J-Tech-Japan/intent-system/pull/1282)、
  “G589: make CI waits observable without timers”、
  merge [`d6a5110a6d9d2c6fd4575b6b8c28b46ca6820a23`](https://github.com/J-Tech-Japan/intent-system/commit/d6a5110a6d9d2c6fd4575b6b8c28b46ca6820a23)。

release preparation で、5 件すべての merge commit が `main` 上にあることを検証済みです。
この 5 fix は、herdr-only での wake reliability という 1 つのテーマを構成します: 正しい
mode / topology guidance、fail-closed packet readiness、recorded role 全員への delivery、CI wait
終了の観測可能性です。

**minor ではなく patch とする理由。** 文書化済み policy は、新しい command surface または
広範な behavior change を MINOR に予約しています。この 5 slice はどれも command surface を
追加せず、すべて既存 surface の correctness fix です。そのため本リリースは `0.8.0` から
`0.8.1` への PATCH です。

### 正しい session-mode 解決と可視な herdr topology (G585, G586)

- **G585** は team 省略時の routing defect を修正します。caller が `--team` を省略しても
  session-layer resolver は誤った scope の答えを黙って返さず、guidance が team-scoped record と
  corrective command を開示します。
- **G586** は herdr-only topology を literal に復元します: 1 team につき 1 workspace、team 名の
  1 tab、role-cwd の pane を role ごとに 1 つ。pane split が default で、tab creation は明示的に
  justify された例外です。explicit-id / fail-closed mutation rule は維持されます。

### packet readiness が 1 回の actionable answer で fail closed (G587)

`packet.yaml` しか存在しない packet を readiness が green と報告しなくなりました。packet-draft と
queue-seed surface は、欠けている canonical file、欠けている contract section、その他すべての
refusal reason を一貫してまとめて報告するため、author は 1-error cycle を繰り返さず packet 全体を
修復できます。

### canonical notify が recorded role の全員へ到達 (G588)

`intent-cli notify delegate` と `notify report` は、team に記録された external resident に、その
recorded reader 経由で route します。sender と report-to role は team topology 上に存在する必要が
ありますが、deliverable でなければならないのは recipient だけです。herdr resolution は caller
team の workspace に scoped され、dry-run / write は同じ resolution verdict を使い、解決不能 route
は foreign pane を prompt せず引き続き fail closed します。

### timer がなくても CI wait completion を観測可能 (G589)

`automation stalled-work` は、正当な pending exact-head CI wait と、terminal な all-green / failed
outcome を区別します。CI-aware finding は PR head SHA、pass/fail/skip/pending breakdown、安定した
dedupe key を運びます。pending 単独は non-escalating のままで、すべての path は read-only です。
orchestrator guide は re-check producer を明記します: timer-loop では configured timer、
herdr-only では明示的に arm した exact-head CI-completion watch です。wait の終了は正当な wake
signal ですが、success proof ではありません。

## インストール

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.8.1
```

operator が承認・publish した後は、self-contained binary を
[v0.8.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.8.1)
から取得できます。使用前に `.sha256` sidecar を検証してください。

## v0.8.0 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.1
```

command / argument / flag / 文書化済み session-layer mode の削除・改名はありません。5 slice は
すべて既存 behavior の correctness fix です。次の 2 件は **additive かつ non-breaking な
output-shape change** なので、明示的に注意してください。

1. `external` recipient に対する `notify delegate` / `notify report` は team event を append し、
   `event_appended=true` を返せるようになりました。以前この route は常に `false` を返していました。
   既存の event / command-result schema は変わりません。
2. `automation stalled-work` は新しい finding kind `ci-pending`、
   `ci-all-green-not-transitioned`、`ci-failed-not-transitioned` を返す場合があります。`kind` を exhaustive
   に switch する consumer はこれらの additive value を受理する必要があります。finding schema は
   変わりません。

## リリース準備ゲート (G590)

以下は **`v0.8.1` GitHub Release を作成または publish する前**に満たす必要があります。この gate
は fail closed です。

- [ ] 上記 shipped 5 slices が、記載した PR / merge commit どおり `main` に merge 済みであり、
      notes に追加の shipped slice が含まれていない。
- [ ] EN/JA 両方の `release-notes-v0.8.1.md` が parity のある real notes で、DRAFT-stub banner が
      残らず、2 件の additive output-shape change を両方開示している。
- [ ] `eng/version.json` が変更されず、`stableVersion` `0.8.0` / `nextVersion` `0.8.1` のまま。
- [ ] G475 next-version notes/package guard、release-note/version-policy guard、full Release suite が
      current-state guard flip なしで exact G590 preparation head 上 green。
- [ ] package metadata が正しい: `PackageId = JTechJapan.IntentSystem.Cli`、repository/project URL が
      この repository を指す、license が `Apache-2.0`、README/docs link が解決する。
- [ ] Main CI (`Build and test (source contract)`) と preview-pack が green で、preparation merge commit
      上の post-merge build + pack evidence が記録されている。
- [ ] operator が `v0.8.1` Release 作成を明示承認している。

## v0.8.1 の publish

この preparation の merge は GitHub Release / tag の作成、package publish、`0.8.2` への roll、
future version の stub removal、release announcement のいずれも行いません。merge 後に readiness
evidence を確認し、operator が Release 作成を明示承認する必要があります。

その後に限り、maintainer/operator (または明示 authorize された外部 release automation)が
`v0.8.1` GitHub Release を作成・publish できます。publish が `release.yml` を trigger し、
NuGet package と platform archive を publish します。

post-release verification:

- [ ] NuGet / GitHub asset link が解決し、download checksum が一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.1` 後の
      `intent-cli --version` が `0.8.1` を報告する。
- [ ] platform archive の checksum が通り、binary が `0.8.1` を報告する。
- [ ] publish 後、別途 authorize された immediate roll を `stableVersion → 0.8.1` /
      `nextVersion → 0.8.2` へ行い、新しい EN/JA DRAFT stubs、refresh 済み readiness section、
      green な post-roll child-main CI を揃える。この roll は G590 の一部ではありません。
