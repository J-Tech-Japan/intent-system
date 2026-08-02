# リリースノート — intent-cli v0.8.0

> **リリースモデル:** この変更は **prepare-only** です。GitHub Release / tag の作成や
> package publish は行いません。この準備が merge され、下記 readiness gate を満たした後も、
> Release 作成には operator の明示承認が必要です。GitHub Release の publish によって
> `.github/workflows/release.yml` (`on: release: published`) が起動し、NuGet package と
> platform artifact を build・publish します。

## v0.8.0 の内容

v0.8.0 が `v0.7.1` 以降から含むのは、マージ済みの 5 スライス **G570**、**G571**、
**G573**、**G574**、**G575** だけです。

**minor バンプの理由。** G570 は新しい top-level command group `session-layer` を追加し、
persist された dual-mode session layer は orchestrator guidance 全体に及ぶ広範な挙動追加です。
限定的な repair ではなく継続利用する user capability であるため、文書化済み policy に従い
`0.7.2` ではなく `0.8.0` とします。

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
の運用指示が同時に渡ることはありません。位置付けは明確です: **agmsg は PRIMARY、
herdr-only は PREVIEW です。PREVIEW の対象は session transport だけであり、4 スレッドモデル
では決してありません。** design / orchestrator / implementation / review の role と権限境界は
どちらの mode でも同じです。

### Release 前に実証した herdr-only 運用モデル (G571, G575)

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

release 前の 2026-08-02、design team は merged main に対して concept spike を全実行し、
**11/11 checks passed、spike team の agmsg process はゼロ**でした。spike で見つかった 3 件の
手順 defect もすべて G575 で修正しました。

- dispatch task text 内の marker literal が自分自身の pane echo から match できたため、現在は
  fresh nonce と prefix を分離し、marker match 単独では決して success にしません。
- approval / question が表示中でも `agent wait` は `idle` を返し得るため、every wait return 後に
  pane inspection と既存 supervision MAY/escalate 境界を適用してから re-enter します。
- 文書の `workspace_created` field と `agent wait` idle shape を installed herdr 0.7.5 に
  合わせました。

この release-preparation では追加 dogfood を実施したとは主張しません。

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

stalled-work detection は converged blocked unit と本当の idle candidate を区別します。queue item と
linked issue の両方が blocked の場合、unit は informational `blocked-parked`、transport は
heartbeat、publish recommendation はありません。half-converged blocked state は引き続き
`state-drift` で、unit が unblock された場合の age は unblock transition から再開します。

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

## リリース準備ゲート (G576)

以下は **`v0.8.0` GitHub Release を作成または publish する前**に満たす必要があります。
この gate は fail closed です。

- [ ] release-bound 5 slices が `main` に merge 済み: G570 (PR #1246)、G571 (PR #1248)、
      G573 (PR #1250)、G574 (PR #1252)、G575 (PR #1254)、および本 G576 release-preparation PR。
- [ ] EN/JA notes がその 5-slice lineage だけを cover し、superseded
      `release-notes-v0.7.2.md` がどちらにも残っていない。
- [ ] `eng/version.json` が `stableVersion` `0.7.1`、`nextVersion` `0.8.0` を示す。
- [ ] G475 next-version release-note/package guard が **current-state guard flip ゼロ**で通り、
      exact release-preparation head で full test suite が green。
- [ ] pre-release evidence は検証済み 11/11 spike と team の agmsg process ゼロであること。
      未検証の marker-only / state-only claim へ置き換えない。
- [ ] package metadata が正しい: `PackageId = JTechJapan.IntentSystem.Cli`、repository/project URL、
      license `Apache-2.0`、README/docs link を確認。
- [ ] Main CI (`Build and test (source contract)`) と preview-pack が green で、merge commit 上の
      post-merge build + pack evidence が記録されている。
- [ ] operator が `v0.8.0` Release 作成を明示承認している。

## v0.8.0 の publish

この準備の merge は Release / tag / package publish / version roll / announcement のいずれも
行いません。merge 後に readiness evidence を確認し、operator が Release 作成を明示承認する
必要があります。

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
      roll 後の child-main CI green を揃える。この roll は G576 の一部ではありません。
