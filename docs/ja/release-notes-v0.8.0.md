# リリースノート — intent-cli v0.8.0

> **リリースモデル:** この変更は **prepare-only** です。GitHub Release / tag の作成や
> package publish は行いません。この準備が merge され、下記 readiness gate を満たした後も、
> Release 作成には operator の明示承認が必要です。GitHub Release の publish によって
> `.github/workflows/release.yml` (`on: release: published`) が起動し、NuGet package と
> platform artifact を build・publish します。

## v0.8.0 の内容

v0.8.0 が `v0.7.1` 以降から含む shipped slice は、マージ済みの 12 件 **G570**、
**G571**、**G573**、**G574**、**G575**、**G577**、**G578**、**G579**、
**G580**、**G581**、**G582**、**G583** だけです。G576 は初版 notes の準備、G584 は
その refresh であり、どちらの maintenance slice も shipped product slice には数えません。

**minor バンプの理由。** G570 は `session-layer`、G578 は `notify` という新しい
top-level command group を追加します。persist された dual-mode session layer と
transport-neutral な role workflow を含む、限定的な repair ではなく継続利用する広範な
user capability であるため、文書化済み policy に従い `0.7.2` ではなく `0.8.0` とします。

### Persist され、双方向に戻せる session layer (G570)

domain/team ごとに利用する session transport を記録できるようになりました。default は
`agmsg`、同じ mode の再設定は idempotent、実際の変更は transition trail に append され、
どちらの方向にも切り替えられます。

```bash
intent-cli session-layer show --domain <domain> --team <team>
intent-cli session-layer set --domain <domain> --team <team> --mode agmsg --write
intent-cli session-layer set --domain <domain> --team <team> --mode herdr-only --write
```

記録済み mode が `intent-cli guide orchestrator-thread` を route するため、1 チームに 2 transport
の運用指示が同時に渡ることはありません。**agmsg と herdr-only はどちらも first-class な
session-layer mode として残り、選択可能かつ双方向に戻せます。agmsg は deprecated では
ありません。** agmsg は引き続き default / PRIMARY transport、herdr-only は PREVIEW です。
**PREVIEW の対象は herdr-only session transport だけであり、4 スレッド collaboration model
では決してありません。** design / orchestrator / implementation / review の role と権限境界は
どちらの mode でも同じです。

### herdr-only 運用 contract (G571, G575)

herdr-only preview には、次の完全な user-facing 運用手順があります。

- team workspace と role 固有 pane を provision し、durable な logical-role→pane mapping を
  記録します。期待 cwd/repository と agent kind、同一 pane の process detection、bounded
  ping/ack がすべて一致する verified liveness 後だけ READY とします。
- structured task を dispatch し、terminal marker は inspect 可能な artifact を指します。
  completion は **artifact-gated** です。state や marker text だけでは success にならず、named
  file / commit / PR / report が存在して task 固有 verification を通る必要があります。
- `agent wait` と `pane wait-output` を常に bounded にし、timeout 時は進捗を persist して、
  1 turn を開き続けず後続 wake で re-enter します。
- design-relevant な completion / blocked / question / escalation を mode-independent な
  `<host-repo>/.intent-cli/events/<team>.jsonl` boundary に記録します。これは inter-agent bus
  ではありません。3 frontend recipe は次のとおりです。
  1. Claude app watcher は完全な未読行を tail して offset を保持する。
  2. herdr 内の Codex CLI は pane 経由で prompt され、design-boundary reader の役割時だけ
     file を読む。
  3. **Codex Desktop は timer poll と durable byte-offset watermark を使う。** この Desktop
     recipe は新機能であり、agmsg では一度も support されていません。

G575 はこの contract の exercise で見つかった 3 件の手順 defect を harden しました。

- dispatch task text 内の marker literal が自分自身の pane echo から match できたため、現在は
  fresh nonce と prefix を分離し、marker match 単独では決して success にしません。
- approval / question が表示中でも `agent wait` は `idle` を返し得るため、every wait return 後に
  pane inspection と既存 supervision MAY/escalate 境界を適用してから re-enter します。
- 文書の `workspace_created` field と `agent wait` idle shape を installed herdr 0.7.5 に
  合わせました。

### 安全な shipped-skill update (G573)

embedded `intent-cli` dispatcher skill は shipped-version lineage を持つようになりました。
以前 shipped された version と完全一致する copy は `stale-shipped` と分類され、`--force` なしで
current embedded version へ update できます。local edit 済み copy は `locally-modified` のまま
保護され、install は上書きせず fail closed します。known installed copy が stale のとき、guide
surface は bounded な "Skill update available" nudge を 1 回表示します。

customize 境界も明確です: **installed official skill の編集は unsupported** です。local workflow
knowledge は managed artifact の外に置いてください。`--force` は operator の明示選択であり、
edit 検出への自動回答ではありません。

### Park 済み work を publishable と報告しない (G574)

stalled-work detection は converged blocked unit と本当の idle candidate を区別します。queue
entry の `state` が `blocked` かつ `blocked_by` list が non-empty の場合、unit は informational
`blocked-parked`、transport は heartbeat、publish recommendation はありません。この分類に
`linked_issue` は関与せず null でも構いません。G574 は half-converged representation に
`state-drift` classification を導入しました。unit が unblock された場合の age は unblock
transition から再開します。

### 同じ release 内で修正した production-trial findings (G577–G583)

herdr-only の evidence は実際の production trial になりました。intent-cli team 自身が
herdr-only で G577 から G583 の publish / implementation / review / merge / closeout という
実 work を進め、**team の agmsg process はゼロ**でした。trial が露呈した
具体的な defect と quality gap はすべて同じ release line で修正されています。

- **G577 — 1 つの `--workdir` rule。** 10 automation command にあった private で不統一な
  relative-path handling を shared resolver に統一しました。省略または blank は repository root、
  relative value は caller cwd ではなくその root から resolve し、absolute path は維持します。
- **G578 — transport-neutral role workflow。** `intent-cli notify delegate`、`report`、`escalate` が
  logical role を validate し、記録済み session mode を内部で resolve、delegation に canonical
  report command を埋め込み、design-boundary escalation は既存 events channel に保ちます。
- **G579 — atomic な canonical state write。** canonical queue-state persistence は unique な
  sibling temporary file へ write/flush し、1 回の overwrite move で publish し、success / interruption
  のどちらでも temporary file を除去します。reader は truncate 済み target を観測しません。
- **G580 — shared-static race guard。** discovery-based test が settable CLI static seam 全体を
  reflection し、IL から assignment する test class を発見し、class が provably serialized でなければ
  seam/class 名付きで fail します。許容する 5 つの split-collection case は explicit で、support 済み
  xUnit v2 runner semantics に fail-closed で bind されています。
- **G581 — worker cooperation 不要の observation。** herdr の
  `pane.agent_status_changed` を normative な SECOND wake source にしました。recorded logical-role
  mapping、working-to-settled transition、settle delay、per-role dedupe を使い、event は inspect の
  理由にすぎず success proof ではありません。
- **G582 — 実測した 5 findings。** 両 mode が session switch checklist を render します。
  agmsg→herdr teardown は次回 launch を hook-trust screen で block し得る outgoing project hook と
  delivery mode を除去します。すべての herdr mutation は non-empty な explicit pane/workspace id を
  resolve し、別 team の focused pane を mutate せず fail closed します。すべての `events.jsonl`
  reader は restart-durable な identity/offset/line watermark を持ち、rotation / truncation /
  backwards progress / replacement で fail closed します。さらに threshold を超えた approved open
  PR を stalled-work の `approved-not-merged` として報告します。
- **G583 — warning-free build floor。** authored 56-test-warning inventory と当時の `main` にあった
  nullable warning 5 件を source で修正しました。solution-wide の .NET 10 analyzer と
  warnings-as-errors により次の warning は build を fail し、scratch edit 削除前の deliberate
  CS8603 negative proof で floor を実証しました。

### 3 wake sources と相補的な failure mode (G578, G581, G582)

herdr-only は 3 つの wake source を使い、どれも単独では outcome とみなしません。

1. **herdr state-change subscription** — `pane.agent_status_changed` は worker cooperation が不要ですが、
   task outcome は運びません。settle/dedupe gate 後も orchestration は pane state、fresh completion
   marker、artifact、canonical intent-cli/GitHub facts、approval/question pause を確認します。
2. **canonical notify report** — `intent-cli notify report` は task id / status / summary / artifact を
   運ぶ最も informative な source ですが、worker が report step に到達して実行することに依存します。
   claim は artifact と canonical state に照らして再検証します。
3. **periodic stalled-work check** — real-time completion signal ではなく最後の net です。canonical
   state から overdue work と recovery recommendation を導き、`approved-not-merged` には merge と
   closeout を推奨します。

coverage standard は、**すべての wake source が fail しても stall を検出可能なままにする**
ことです。どの source も唯一の detector にはできません。G582 が `approved-not-merged` を追加した
理由は、report や state-change wake を逃した approved open PR を不可視にしないためです。

## Install

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.8.0
```

operator が承認・publish した後は、
[v0.8.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.8.0)
から self-contained binary も取得できます。`.sha256` sidecar を検証してください。

## v0.7.1 からのアップグレード

```bash
dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.0
```

default は引き続き agmsg です。operator が `session-layer set` で mode を記録しない限り、既存 team
が herdr-only へ移ることはありません。mode change は reversible で、guide の
drain/provision/READY checklist に従う必要があります。

## リリース準備ゲート (G576、G584 で refresh)

以下は **`v0.8.0` GitHub Release を作成または publish する前**に満たす必要があります。
この gate は fail closed です。

- [ ] shipped 12 slices が `main` に merge 済み: G570 (PR #1246)、G571 (PR #1248)、
      G573 (PR #1250)、G574 (PR #1252)、G575 (PR #1254)、G577 (PR #1258)、
      G578 (PR #1260)、G579 (PR #1262)、G580 (PR #1264)、G581 (PR #1266)、
      G582 (PR #1268)、G583 (PR #1270)。G576 (PR #1256) は初回 release preparation、
      本 G584 refresh は notes maintenance であり、どちらも shipped slice には数えない。
- [ ] EN/JA notes がその 12-slice lineage だけを cover し、superseded
      `release-notes-v0.7.2.md` がどちらにも残っていない。
- [ ] `eng/version.json` が `stableVersion` `0.7.1`、`nextVersion` `0.8.0` を示す。
- [ ] G475 next-version release-note/package guard が **current-state guard flip ゼロ**で通り、
      exact G584 notes-maintenance head で full test suite が green。
- [ ] pre-release evidence は intent-cli team が G577–G583 を publish から closeout まで進めた
      real herdr-only production trial で、team の agmsg process はゼロであり、surfaced finding は
      fixing slice と上記で対応している。
- [ ] 3 wake source と各 failure mode が文書化されたままで、report / state-change delivery を
      逃しても `approved-not-merged` が coverage standard を維持する。
- [ ] package metadata が正しい: `PackageId = JTechJapan.IntentSystem.Cli`、repository/project URL、
      license `Apache-2.0`、README/docs link を確認。
- [ ] Main CI (`Build and test (source contract)`) と preview-pack が green で、merge commit 上の
      post-merge build + pack evidence が記録されている。
- [ ] operator が `v0.8.0` Release 作成を明示承認している。

## v0.8.0 の publish

この notes maintenance の merge は Release / tag / package publish / version roll /
announcement のいずれも行いません。merge 後に readiness evidence を確認し、operator が
Release 作成を明示承認する必要があります。

その後に限り、maintainer/operator (または明示 authorize された外部 release automation)が
`v0.8.0` GitHub Release を作成・publish できます。publish が `release.yml` を trigger し、
NuGet package と platform archive を publish します。

post-release verification:

- [ ] NuGet / GitHub asset link が解決し、download checksum が一致する。
- [ ] `dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.8.0` 後の
      `intent-cli --version` が `0.8.0` を報告する。
- [ ] platform archive の checksum が通り、binary が `0.8.0` を報告する。
- [ ] publish 後、別途 authorize された immediate roll を `stableVersion → 0.8.0` /
      `nextVersion → 0.8.1` へ行い、新しい EN/JA DRAFT stubs、refresh 済み readiness section、
      roll 後の child-main CI green を揃える。この roll は G576 / G584 の一部ではありません。
