# リリースノート — intent-cli v0.32.0

> **PREPARED / NOT PUBLISHED。** これは `v0.32.0-preview.4` prerelease のための、測定済み
> G795–G838 の units（この範囲のすべての番号ではありません）の prepare-only notes です。tag / GitHub Release / package publish、workflow または
> publish configuration、consumer follow-up、product source の変更は行いません。

v0.32.0 の stable GitHub Release はまだ存在せず、この notes は preparation evidence だけです。
v0.32.0 を cut した後の stable install query は `JTechJapan.IntentSystem.Cli --version 0.32.0` です。
この preparation 後の policy は変わりません:

```json
{
  "stableVersion": "0.32.0",
  "nextVersion": "0.32.1"
}
```

`0.32.1` は replaceable development placeholder であり、次の real release number の決定では
ありません。EN/JA の v0.32.1 file は planning scaffold であり、changelog ではありません。
normal identity は placeholder であり **not** v0.32.0（v0.32.0 ではありません）。この
prepare-only slice は no tag、no GitHub Release、no workflow change、no product source change です。

## Preview.3: preview.2 からの変更

preview.2（`v0.32.0-preview.2`、2026-09-05）には deadlock がありました。claims-enabled host で claim
verifier は `--team` を要求する一方、`worker claim` はそれを受け付けず、team 所有の unit を worker claim
できませんでした。preview.3 はその修正（G815）と、preview.2 以降に merge したそのほかのすべてを含みます。preview.2 の
利用者が対応すべき変更:

- **supervision は opt-in（G828）。** `guide next` の `supervision-setup` 推奨と、`guide bootstrap` の
  supervision cycle 要求は、`.intent-cli/config.toml` に `[supervision] opt_in_teams = ["<domain>/<team>"]`
  と宣言した team だけに適用されます。既存の `bound.json`・install record・cycle は opt-in の根拠に
  なりません。standing supervisor を使う team はこの宣言を追加します。`guide next` の
  `bootstrap.resume_recommended` は bootstrap の完了状態に従うようになり、opt-in 済みで roster が欠け
  cycle が記録済みの team にも resume を勧めます。
- **`stalls.jsonl` は runtime-local（G827）。** CLI 管理の `.intent-cli/supervision/.gitignore` に
  `**/stalls.jsonl` が入りました。追跡している host は
  `intent-cli notify supervise repair-cycle-history --domain <d> --team <t> --write` を実行すると、
  file を残したまま index から外れます。`notify supervise shrink` と `archive` は約 1 GB を超える file
  でも落ちなくなりました。
- **agmsg + herdr は deprecated（G829）。** 引き続き動作し、記録なしの既定値のままです。herdr-only は
  `intent-cli session-layer set --domain <d> [--team <t>] --mode herdr-only --write` で記録します。
- **review の差し戻しで詰まらなくなった（G824）。** `request-update` は `intent-pr-approved` を外します。
- **公開済み issue 本文を正規に更新できる（G825）。** `intent-cli issue sync-body <unit> --repo <owner/repo>`
  を dry-run で確認してから `--write --expected-remote-sha256 <digest>` で実行します。保証は
  read-compare-write-verified で、atomic ではありません。

## Preview.4: preview.3 からの変更

preview.3（`v0.32.0-preview.3`、2026-09-14）は preview.2 の修正と八つの post-preview.2 route を
出荷しました。preview.4 は preview.3 以降に merge したすべてを含みます。preview.3 の
利用者が対応すべき変更:

- **`solo-conductor` は三つ目の `team-mode` 値（G833）。** `intent-cli team-mode set --mode solo-conductor --write`
  で記録し、`intent-cli guide solo-conductor` で operating contract を読みます。solo conductor の
  ten-step per-unit loop と blocking reviewer-independence rules を出力しますが、agent を起動・管理しません。
  **前方互換の hazard:** G833 のない intent-cli は `solo-conductor` を含む `.intent-cli/team-mode.json` を
  拒否し、file 全体の load 失敗により *every team on that host* に影響します。mode を記録する前に host を
  読むすべての intent-cli を更新してください。別 seat が古い binary の preview.3 利用者は、このコマンドを
  実行する前にこれを読む必要があります。
- **cross-runtime review は team ごとに宣言（G834/G835）。** `.intent-cli/config.toml` に
  `[[cross_runtime_review.teams]]` を追加し、`team`・`conductor_runtime`・`repos`（gated-repo 一覧）を
  指定します。宣言のない team、または declaring team の一覧にない repo は ungated で、宣言のない host と
  byte-identical です。**gated repo 上の declared team** では、`intent-cli automation pr-transition --transition approved`
  に PR の現在 head と一致する `--head-sha <sha>` と満たされた gate（G834）が追加で必要になり、`issue publish-flow`
  は `packet_digest` でキーされた design verdict を要求します（G835）。`intent-cli review cross-runtime request`
  は prompt を出力し、`intent-cli review cross-runtime record` は verdict を保存します。process は実行せず、
  seat 自身が reviewer を走らせます。`--kind design` と `--model` は宣言のあるなしに関わらず任意の caller で使えます。
- **`intent-cli automation pr-created-stale-recovery`（G836）** は unmerged PR close 後の stale
  `intent-pr-created` label を回復し、その label だけを外します。既定は dry-run で、claims store のない host では
  `claim-unavailable` で拒否します。
- **`intent-cli session-layer topology record-orca-run`（G837）** は team-shape seat または solo sidecar に
  adopted Orca Run を結び付け、read-only の `intent-cli session-layer topology orca-runs` で読み戻します。binding がある間は
  `intent-cli team-mode set --write` が effective-mode 変更を拒否し、これが preview.3 利用者を mid-task で
  止める唯一の経路です。intent-cli は `orca` を実行せず Run も作成しません。

## 独自に測定した minor justification

named product base は `cd276e20754db09337a94a8b973ddebdd1564ba3` です。minor の判断は
v0.28.0 の auditable rule、**a command-route addition is a minor bump; option-level additions
do not count as command routes.** に従います。G796 は新しい role への event-kind routing、G800 は
research-delegation route を追加し、この二つの command-surface route additions が minor の測定済み
理由です。alias table と config repair、guide rendering、G801 npm dist-tag behavior は列挙しますが
routes としては **not counted** です。G803 の structured guide-field canonicalization も列挙しますが、
additional route としては **not counted** です。

preview.2 以降、compatibility ledger には八つの command route が加わりました:
`automation progress-supervision`, `guide progress-supervision`, `guide steward-thread`, `issue sync-body`, `notify ack`, `notify acknowledge`, `notify progress-supervision`, `session-layer seat`。operator の決定（2026-09-14）により、これらは 0.33.0 を開かず、未 release の
0.32.0 minor に含めて `v0.32.0-preview.3` として出します。v0.32.0 には stable release がまだ無いため、
これらが上げるはずの minor はいま準備中のものと同じだからです。0.32.0 の中で数える route additions です。

preview.3 以降、compatibility ledger には六つの command route が加わりました:
`automation pr-created-stale-recovery`, `guide solo-conductor`, `review cross-runtime`, `review cross-runtime request`, `review cross-runtime record`, `review cross-runtime status`。Registered command table の行だけを数えます（`| Registered command |` で開き `## Durable schema と legacy inventory` で閉じる table、七行の alias table は除外）。測定した行数は EN/JA 両 mirror で 204 から 210 になりました。operator の決定（2026-09-15）により、これらは 0.33.0 を開かず、未 release の 0.32.0 minor に含めて `v0.32.0-preview.4` として出します。G837 の `session-layer topology record-orca-run` と `session-layer topology orca-runs` は既に登録済みの `session-layer topology` route の subcommand であり、route additions としては **not counted** です。

merged history から route の判断を再現できます: G796 は six-kind event routing addition、G800 は
first-class research delegation route で、後から加わった八つの route は上に列挙しました。

## 測定した version identities

named base を clean Release build で確認しました:

```text
$ git rev-parse HEAD
cd276e20754db09337a94a8b973ddebdd1564ba3
$ dotnet build IntentSystem.sln --configuration Release --no-restore; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.1-cd276e2-G837
```

この normal identity は `nextVersion` placeholder であり、**v0.32.0 ではありません**。
同じ base を explicit release property で測定しました:

```text
$ dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.32.0; echo BUILD_RC:$?
BUILD_RC:0
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
intent-cli 0.32.0-cd276e2-G837
```

published version の third identity は local policy file ではなく `release.yml` が tag から導出します:

```text
$ raw=v0.32.0; version="${raw#v}"; printf 'RAW=%s\nVERSION=%s\n' "$raw" "$version"
RAW=v0.32.0
VERSION=0.32.0
```

release workflow は `RAW` から `-p:Version=<tag>` を供給し、`eng/version.json` は local builds と
dry runs だけを管理します。`v0.32.0-preview.4` のような preview release tag も同じ方法で
`VERSION=0.32.0-preview.4` になります。この prepare-only slice は no tag（tag を作成していません）です。

## Release inventory: 正確に 33 の shipped first-parent unit

shipped inventory は exact first-parent range から導出しました。Git は四十一の commit を測定し、
以下の 33 の shipped unit（G813 は部分実装）には一つずつ operator-observable outcome を記録します。
G802・G804・G830 の release-prep commit と五つの claim state commit は accounting table で分類しますが、
shipped unit には数えません:

- G795 — PR #1740 / issue #1737; merge commit `1b3c7229cfe8c8f8565034a7e2220a94ac14785b`。
  **Operator-observable outcome:** canonical Architect, Orchestrator, Builder, Reviewer, Steward の role values は四つの legacy aliases を受け入れ、unknown role は拒否します。
- G798 — PR #1742 / issue #1741; merge commit `09b1f4edca51f3acbbe3e901356866996f4be29f`。
  **Operator-observable outcome:** recorded role configuration は canonical normalizer 経由で load され、queue-state role fields は runtime semantics なしに read/display されます。
- G796 — PR #1743 / issue #1738; merge commit `67c8578090f1a53e8894aeff88abd6cd8b83ff15`。
  **Operator-observable outcome:** six event kinds は Steward または Architect に route され、opaque ruling payload は digest と origin の境界を保ったまま byte-identically relay されます。
- G800 — PR #1747 / issue #1745; merge commit `6e0bff220e2bf51308596c19ee258835ce509dd8`。
  **Operator-observable outcome:** Architect または Reviewer は sourced research を Orchestrator または Steward へ delegate でき、ruling-bearing research は judgement seat で拒否されます。direct research は ungated です。
- G797 — PR #1746 / issue #1739; merge commit `11457187ad0f9c2c269b80de84b0fd9ea278dfe5`。
  **Operator-observable outcome:** guides は canonical roles と Steward を説明し、retired-name glossary を保持し、vendor/runtime role coupling なしに installed route names を保存します。
- G801 — PR #1749 / issue #1748; merge commit `2a833a976688b3139678e4954162a9c00d32d0f4`。
  **Operator-observable outcome:** npm publish calls は stable version では `latest`、preview/rc/beta/alpha SemVer では non-default prerelease dist-tag を導出します。
- G803 — PR #1753 / issue #1752; merge commit `16267f9d58af31669252186a16ce09ab0dd47ba4`。
  **Operator-observable outcome:** structured guide fields の role identifier は canonical name を出力し、seven-entry inventory と one shared four-surface full-payload regression を持ちます。
- G799 — PR #1756 / issue #1744; merge commit `d4645e2f02aea6a7969804a156df176cc2122a4a`。
  **Operator-observable outcome:** `notify adjudicate live-pair` は `notify adjudicate` に渡す現在の live CAS pair を返し、CAS refusal は sequence・dialog-change・wrong-projection の原因を区別して示します。
- G805 — PR #1765 / issue #1757; merge commit `5ffcea48b0060569112efee06ad12baa5d8c9b59`。
  **Operator-observable outcome:** read-only の `automation stalled-work` は、最新の APPROVED / CHANGES_REQUESTED review が最新の `intent-pr-*` label transition より先行しているとき `review-verdict-ahead-of-label` を報告します。
- G806 — PR #1766 / issue #1758; merge commit `8e1ded058aeb6738c9419ce584f8da0d9fa59888`。
  **Operator-observable outcome:** read-only の `automation stalled-work` は、intake の無い published issue と、runtime queue transition に達していない delivered intake を owner / action evidence 付きで区別します。
- G807 — PR #1767 / issue #1759; merge commit `ea5959f83f528675072b8f034f5c3fc30cbb07b8`。
  **Operator-observable outcome:** 新しい read-only route `guide steward-thread` は Steward の metadata-free operating contract を Markdown / JSON で出力します。
- G808 — PR #1768 / issue #1760; merge commit `80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36`。
  **Operator-observable outcome:** 新しい read-only route `session-layer seat preflight` は席の起動前に `.git` の書き込み可否・remote 到達性・installed identity・host claim-path 解決・runtime family を調べ、remedy line を示します。
- G809 — PR #1769 / issue #1763; merge commit `3d604981865a04c2875fde06d76cf5b60d41fda0`。
  **Operator-observable outcome:** `notify delegate --to` は event kind による再 routing をせず、指定した recipient にだけ届けます。
- G811 — PR #1770 / issue #1762; merge commit `828e19815e0bd8356298e5c49ae3c90bd8a1751d`。
  **Operator-observable outcome:** Herdr から外部 Steward への completion channel は、外部の consumption receipt と identity-bound な return acknowledgement を新しい route `notify acknowledge`（alias `notify ack`）で記録し、pane や model を起こしません。
- G812 — PR #1772 / issue #1764; merge commit `cf40ac8f3211925339134f5fa8bb91bc722e549b`。
  **Operator-observable outcome:** 新しい route `notify progress-supervision`・`automation progress-supervision`・`guide progress-supervision` は typed progress phase と deadline を bounded recovery evidence 付きで評価します。`--write` は command 所有の evidence を追記するだけで、provider や pane を起動しません。
- G810 — PR #1773 / issue #1761; merge commit `0f7cb50b0f7d42da120fe04bc4421807dcf5da16`。
  **Operator-observable outcome:** `notify supervise` は計測した timing・source category・identity-bound recovery field を含む `g810-cost-aware-supervision/v1` contract を出力します。
- G815 — PR #1776 / issue #1775; merge commit `7003e08b0152e91f89069a67b5ca299bef7cd6de`。
  **Operator-observable outcome:** `worker claim --team` は invoking team を claim ownership verification まで運ぶため、claims-enabled host で team 所有の execution unit を worker claim できます（preview.2 は `--team` を要求しながら受け付けませんでした）。
- G813 — PR #1781 / issue #1774; merge commit `3d91a8004c97ab800f365d89189c12def8a39980`。
  **Operator-observable outcome:** worker と host-loop の command は明示した team identity を保持します。**部分実装:** Orca Run mailbox binding（#1771）は未提供で、linkage issue #1774 は open のままです。
- G822 — PR #1786 / issue #1785; merge commit `9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5`。
  **Operator-observable outcome:** `automation host-loop-next-action` の team-scoped identity refusal は、欠けている fact の分類と、それを供給する command を示します。
- G823 — PR #1788 / issue #1778, #1787; merge commit `01944a5ed47139b47276ef2fcbc951183f8e442b`。
  **Operator-observable outcome:** `automation publish-lifecycle-repair` は evidence を読む前に各 unit を `created_issue_url` の repository に結び付け、別 repository の issue で判定せず、foreign / unproven な unit を除外します。
- G824 — PR #1791 / issue #1782, #1789; merge commit `20ef0a6caa184b91e4c3cd2378bd37d941e0de7a`。
  **Operator-observable outcome:** `automation pr-transition --transition request-update` は `intent-pr-approved` を外し、reconcile はどちらの review decision が後かを推測せず `conflicting-review-decision` で停止します。
- G825 — PR #1793 / issue #1777, #1790; merge commit `ca7272f4b88e00577e7c423d41a888f0f0defaf6`。
  **Operator-observable outcome:** 新しい route `issue sync-body` は validation と repository binding の後に公開済み child issue の本文を更新し、すべての結果に `read-compare-write-verified; not atomic` という保証を明記します。
- G826 — PR #1795 / issue #1792; merge commit `71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44`。
  **Operator-observable outcome:** `issue publish-flow` は packet.yaml の `issue_title`（YAML / JSON 形式）から題名を解決し、scaffold 由来の packet を `<id> (untitled)` で公開しなくなりました。
- G827 — PR #1796 / issue #1794; merge commit `0762312ddccf14009d3252c5101d0b893ca7be6b`。
  **Operator-observable outcome:** `notify supervise shrink` と `archive` は最長 1 record 分の memory で supervision file を stream 処理し（1.25 GB の `cycles.jsonl` でピーク約 55 MB）、`stalls.jsonl` は runtime-local になりました。
- G828 — PR #1799 / issue #1798; merge commit `227a981d42cee28924ba34cbde3f45d12717a202`。
  **Operator-observable outcome:** supervision は opt-in になりました。`[supervision] opt_in_teams` に宣言した team だけが `supervision-setup` の推奨を受け、bootstrap の完了条件に supervision cycle が含まれます。
- G829 — PR #1802 / issue #1801; merge commit `e78b27d1e99247380fa7518d67470304fb1d7e7b`。
  **Operator-observable outcome:** agmsg + herdr は deprecated と表示され、`session-layer show` は `mode_deprecated` と `deprecation_notice` を追加します。記録なしの既定値は `agmsg` のままです。
- G831 — PR #1806 / issue #1805; merge commit `2f5452a00866c02e9e80949c9e80b5d2b347fd35`。
  **Operator-observable outcome:** `guide model` は recorded session layer 上の four-thread と five-thread role models を説明します。
- G832 — PR #1808 / issue #1807; merge commit `159952b410b6429acc03414993ac43a650df7fc7`。
  **Operator-observable outcome:** `guide orchestrator-thread` の model summary は canonical roles と両方の thread models を名前にします。
- G833 — PR #1810 / issue #1809; merge commit `9e461d8fda129cfb9447d99b158abb31d9c5c70b`。
  **Operator-observable outcome:** `team-mode` は三つ目の `solo-conductor` team shape を受け入れ、新しい read-only route `guide solo-conductor` は solo conductor の ten-step per-unit loop と blocking reviewer-independence rules を出力しますが、agent を起動・管理しません。
- G834 — PR #1812 / issue #1811; merge commit `2e6e6b62defcdcfed95f7f8036eb4a134ccc5adf`。
  **Operator-observable outcome:** 新しい `review cross-runtime` グループ（`request`・`record`・`status`）は read-only review prompt を出力し、`repo`/`pr`/`head_sha` に結び付いた implementation verdict を記録し、`[[cross_runtime_review.teams]]` に宣言された team の approval を gate します。process は実行しません。
- G835 — PR #1817 / issue #1813; merge commit `1ff9e75d1ee80739a9ec8aeea8d905b60a757a86`。
  **Operator-observable outcome:** `review cross-runtime request` は `--kind design` と `--model` を得て、design artifact と reviewer model を任意の caller（gated か否かに関わらず）が選べます。別途、gated repo 上で `[[cross_runtime_review.teams]]` に宣言された team では、`packet_digest` でキーされた design verdict が `issue publish-flow` の packet 公開を gate します。宣言のない team または ungated repo は宣言のない host と byte-identical です。
- G836 — PR #1819 / issue #1784, #1818; merge commit `75d68523739318eb97932c6f55a35e547e00b769`。
  **Operator-observable outcome:** 新しい route `automation pr-created-stale-recovery` は unmerged PR close 後の stale `intent-pr-created` label だけを外し、既定は dry-run で、identity-bound linkage・PR state・open-closing-PR・unheld-claim の検査に失敗した場合は拒否し、claims store のない host では `claim-unavailable` で拒否します。
- G837 — PR #1821 / issue #1820; merge commit `cd276e20754db09337a94a8b973ddebdd1564ba3`。
  **Operator-observable outcome:** `session-layer topology record-orca-run` と read-only の `session-layer topology orca-runs` は team-shape seat または solo sidecar 上で adopted Orca Run id を記録・表示・検証・bootstrap し、binding がある間は `team-mode set --write` が effective-mode 変更を拒否します。intent-cli は `orca` を実行せず Run も作成しません。

## First-parent accounting

```text
$ git rev-list --first-parent --reverse v0.31.0..cd276e20754db09337a94a8b973ddebdd1564ba3
1b3c7229cfe8c8f8565034a7e2220a94ac14785b
09b1f4edca51f3acbbe3e901356866996f4be29f
67c8578090f1a53e8894aeff88abd6cd8b83ff15
6e0bff220e2bf51308596c19ee258835ce509dd8
11457187ad0f9c2c269b80de84b0fd9ea278dfe5
2a833a976688b3139678e4954162a9c00d32d0f4
b0f5354ba9a922e1676a2e654d866c2a08f60104
16267f9d58af31669252186a16ce09ab0dd47ba4
1f4f94155914c9f6097ec8f6bbad916f13e7817b
d4645e2f02aea6a7969804a156df176cc2122a4a
5ffcea48b0060569112efee06ad12baa5d8c9b59
8e1ded058aeb6738c9419ce584f8da0d9fa59888
ea5959f83f528675072b8f034f5c3fc30cbb07b8
80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36
3d604981865a04c2875fde06d76cf5b60d41fda0
828e19815e0bd8356298e5c49ae3c90bd8a1751d
cf40ac8f3211925339134f5fa8bb91bc722e549b
0f7cb50b0f7d42da120fe04bc4421807dcf5da16
ebb7f21745a24d985c3ffdc7b370195506c1338c
3262d7c543f3663a8ce83fb8a2e86018162d6962
540c6efdf63300d05fe79e8020574c1bc3f96ad7
f9a91a61edb3ed6291750a19e0247212fccad270
baef0f3534c0a7a0de7dac0e0f1b4a7946c7c0a9
7003e08b0152e91f89069a67b5ca299bef7cd6de
3d91a8004c97ab800f365d89189c12def8a39980
9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5
01944a5ed47139b47276ef2fcbc951183f8e442b
20ef0a6caa184b91e4c3cd2378bd37d941e0de7a
ca7272f4b88e00577e7c423d41a888f0f0defaf6
71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44
0762312ddccf14009d3252c5101d0b893ca7be6b
227a981d42cee28924ba34cbde3f45d12717a202
e78b27d1e99247380fa7518d67470304fb1d7e7b
d5f72c10a26e4844fac38dbc362ab1d6052bc237
2f5452a00866c02e9e80949c9e80b5d2b347fd35
159952b410b6429acc03414993ac43a650df7fc7
9e461d8fda129cfb9447d99b158abb31d9c5c70b
2e6e6b62defcdcfed95f7f8036eb4a134ccc5adf
1ff9e75d1ee80739a9ec8aeea8d905b60a757a86
75d68523739318eb97932c6f55a35e547e00b769
cd276e20754db09337a94a8b973ddebdd1564ba3
$ git rev-list --first-parent --count v0.31.0..cd276e20754db09337a94a8b973ddebdd1564ba3
41
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `1b3c7229cfe8c8f8565034a7e2220a94ac14785b` | G795 / PR #1740 / issue #1737 | included |
| `09b1f4edca51f3acbbe3e901356866996f4be29f` | G798 / PR #1742 / issue #1741 | included |
| `67c8578090f1a53e8894aeff88abd6cd8b83ff15` | G796 / PR #1743 / issue #1738 | included |
| `6e0bff220e2bf51308596c19ee258835ce509dd8` | G800 / PR #1747 / issue #1745 | included |
| `11457187ad0f9c2c269b80de84b0fd9ea278dfe5` | G797 / PR #1746 / issue #1739 | included |
| `2a833a976688b3139678e4954162a9c00d32d0f4` | G801 / PR #1749 / issue #1748 | included |
| `b0f5354ba9a922e1676a2e654d866c2a08f60104` | G802 / PR #1751 / issue #1750 | prior release prep |
| `16267f9d58af31669252186a16ce09ab0dd47ba4` | G803 / PR #1753 / issue #1752 | included |
| `1f4f94155914c9f6097ec8f6bbad916f13e7817b` | G804 / PR #1755 | prior release prep |
| `d4645e2f02aea6a7969804a156df176cc2122a4a` | G799 / PR #1756 / issue #1744 | included |
| `5ffcea48b0060569112efee06ad12baa5d8c9b59` | G805 / PR #1765 / issue #1757 | included |
| `8e1ded058aeb6738c9419ce584f8da0d9fa59888` | G806 / PR #1766 / issue #1758 | included |
| `ea5959f83f528675072b8f034f5c3fc30cbb07b8` | G807 / PR #1767 / issue #1759 | included |
| `80270af5c54bf4e89dfbb8eb5f4a93e7142e8e36` | G808 / PR #1768 / issue #1760 | included |
| `3d604981865a04c2875fde06d76cf5b60d41fda0` | G809 / PR #1769 / issue #1763 | included |
| `828e19815e0bd8356298e5c49ae3c90bd8a1751d` | G811 / PR #1770 / issue #1762 | included |
| `cf40ac8f3211925339134f5fa8bb91bc722e549b` | G812 / PR #1772 / issue #1764 | included |
| `0f7cb50b0f7d42da120fe04bc4421807dcf5da16` | G810 / PR #1773 / issue #1761 | included |
| `ebb7f21745a24d985c3ffdc7b370195506c1338c` | `claim: acquire execution-unit:G813` | claim state commit, not a unit |
| `3262d7c543f3663a8ce83fb8a2e86018162d6962` | `claim: acquire execution-unit:G815` | claim state commit, not a unit |
| `540c6efdf63300d05fe79e8020574c1bc3f96ad7` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `f9a91a61edb3ed6291750a19e0247212fccad270` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `baef0f3534c0a7a0de7dac0e0f1b4a7946c7c0a9` | `claim: take over execution-unit:G815` | claim state commit, not a unit |
| `7003e08b0152e91f89069a67b5ca299bef7cd6de` | G815 / PR #1776 / issue #1775 | included |
| `3d91a8004c97ab800f365d89189c12def8a39980` | G813 / PR #1781 / linkage issue #1774 (not closed) | partial unit, included |
| `9d521ebc4ef45c7aea52527e77bb21aa34f1c9a5` | G822 / PR #1786 / issue #1785 | included |
| `01944a5ed47139b47276ef2fcbc951183f8e442b` | G823 / PR #1788 / issue #1778, #1787 | included |
| `20ef0a6caa184b91e4c3cd2378bd37d941e0de7a` | G824 / PR #1791 / issue #1782, #1789 | included |
| `ca7272f4b88e00577e7c423d41a888f0f0defaf6` | G825 / PR #1793 / issue #1777, #1790 | included |
| `71ca5da979f549b9f4f1f4c4ebb45ddd0696fb44` | G826 / PR #1795 / issue #1792 | included |
| `0762312ddccf14009d3252c5101d0b893ca7be6b` | G827 / PR #1796 / issue #1794 | included |
| `227a981d42cee28924ba34cbde3f45d12717a202` | G828 / PR #1799 / issue #1798 | included |
| `e78b27d1e99247380fa7518d67470304fb1d7e7b` | G829 / PR #1802 / issue #1801 | included |
| `d5f72c10a26e4844fac38dbc362ab1d6052bc237` | G830 / PR #1804 / issue #1803 | prior release prep |
| `2f5452a00866c02e9e80949c9e80b5d2b347fd35` | G831 / PR #1806 / issue #1805 | included |
| `159952b410b6429acc03414993ac43a650df7fc7` | G832 / PR #1808 / issue #1807 | included |
| `9e461d8fda129cfb9447d99b158abb31d9c5c70b` | G833 / PR #1810 / issue #1809 | included |
| `2e6e6b62defcdcfed95f7f8036eb4a134ccc5adf` | G834 / PR #1812 / issue #1811 | included |
| `1ff9e75d1ee80739a9ec8aeea8d905b60a757a86` | G835 / PR #1817 / issue #1813 | included |
| `75d68523739318eb97932c6f55a35e547e00b769` | G836 / PR #1819 / issue #1784, #1818 | included |
| `cd276e20754db09337a94a8b973ddebdd1564ba3` | G837 / PR #1821 / issue #1820 | included |

この first-parent range はこの四十一の commit だけで、second-parent commit の changelog ではありません。
G802・G804・G830 は preview.1・preview.2・preview.3 のための過去の release preparation です。五つの claim state commit
は claim transaction が child の default branch に書いたもので、product change を含まず unit では
ありません。G813 は部分実装の unit として含め、その linkage issue #1774 は open のままです。

## Alias promise and compatibility boundary

role-renaming release は existing host と互換です。legacy names `design`、`orchestration`、
`implementation`、`review` の四つは Architect、Orchestrator、Builder、Reviewer の aliases として
still work します。Existing roles configuration keeps loading、existing queue-state keeps reading and
displaying、no installed guide route changed name です。これは compatibility promise であり、new route
claim ではありません。

## Truthfulness と prepare-only boundaries

- G795 の five canonical roles と four aliases は一つの normalizer で処理し、unknown role は黙って
  persist せず refusal します。
- G798 の roles configuration は loadable のまま、queue-state `worker_role` / `review_role` は
  runtime behavior ではなく read/display fields のままです。
- G796 の ruling payload は opaque で、bytes、digest、origin を保持し、指定された relay envelope
  だけを追加できます。
- G800 の research delegation は source-bearing finding を要求し、ruling-bearing report は finding
  を rule すべき judgement seat を名前にして拒否します。Direct Architect/Reviewer research は成功し、
  gate ではありません。Visibility counts は grading なしの measurement で、size threshold、model name、
  runtime condition は使いません。
- G825 の `issue sync-body` は `read-compare-write-verified; not atomic` と明記し、GitHub の issue 本文に
  存在しない compare-and-swap を主張しません。
- G828 と G829 は supervision と transport の command を制限・変更しません。supervision の command は
  どの team でも動き、`agmsg` は引き続き動作して記録なしの既定値のままです。ただし G828 は `opt_in_teams` の
  config validation を追加し、上記のとおり `bootstrap.resume_recommended` を変えます。
- この prepare-only slice には tag、GitHub Release、package publish、workflow/publish configuration、
  consumer follow-up、product source の変更はありません。

## Prepare-only verification

`ReleaseNotesV0320G802Tests` は EN/JA の unit/PR/issue/merge tuples を比較し、両 mirror の四つの
alias statements、三つの measured identities、exact forty-one-commit accounting を guard し、一フィールドの
mirror mutation と preview.3 base `e78b27d1e99247380fa7518d67470304fb1d7e7b` の stale measurement で意図的に fail します。
`ReleasePackageMetadataTests` は policy shape と demanded next-version placeholder を引き続き guard します。
diff は EN/JA v0.32.0 notes と tests に限定され、`eng/version.json` と v0.32.1 placeholders は untouched
です。tag / GitHub Release / package publish / workflow または publish-config / product source change は
含みません。
