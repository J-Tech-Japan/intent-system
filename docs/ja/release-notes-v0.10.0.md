# リリースノート — intent-cli v0.10.0

> prepare-only です。この PR は Release、tag、package publish、announcement、merge、
> post-release version roll を作成しません。readiness gate 後の Release 作成は operator が行います。

## 対象範囲

first-parent の `main` merge から、この MINOR に含まれるのは次の四つだけです。

- G596 — [PR #1296](https://github.com/J-Tech-Japan/intent-system/pull/1296)、merge `4c5aec043cd03f488535cf10021a2afe81c5d328`。
- G598 — [PR #1298](https://github.com/J-Tech-Japan/intent-system/pull/1298)、merge `18db1ea2d4a09e175aa1e093598df8fe59c023fb`。
- G597 — [PR #1300](https://github.com/J-Tech-Japan/intent-system/pull/1300)、merge `7509cf6be504cdefacba0ae0a1f520f897609769`。
- G599 — [PR #1302](https://github.com/J-Tech-Japan/intent-system/pull/1302)、merge `be64197768459a219b233628cfd8ae6932f1068f`。

各 commit は `main` の first-parent ancestor として検証済みです。他の slice は含みません。

## MINOR の根拠

`v0.9.1` と merged `main` の command router 比較では追加のみです。`operator-attention`
と `query`、`resolve`、`supersede` は v0.9.1 には無く、削除された command はありません。
policy は新しい command surface を minor bump に割り当てます。

## 運用上の変更

G596/G599 は block を、判断すべき party に向けた durable/queryable record にします。design-owned
open record は `operator-required`、`route_to: design`、`ROUTE TO DESIGN` / `DESIGN REQUIRED`
として表示され、resolve 後は route が消え `actionable-stall` に戻ります。G599 前は同じ表示が
zero pending transitions でした。

G597 により design thread は heartbeat 一回で wait、role nudge、owner 問い合わせ、monitor repair
を判断できます。`automation heartbeat` は決定に `--team` が必要で、team-less は
`cannot-determine` を返すため runbook は recorded team を指定します。

G598 の herdr notify は unattended working transition を観測した時点で `delivered` を記録します。
settle evidence は別で、typed `resend_permitted` を持ち、delivery を否定しません。

[v0.9.1](release-notes-v0.9.1.md) と [v0.9.0](release-notes-v0.9.0.md) も参照してください。

## インストールまたは更新

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.10.0
```

## リリース準備ゲート

- [ ] `eng/version.json` は `stableVersion` `0.9.1` / `nextVersion` `0.10.0`。
- [ ] EN/JA notes は上記四 merge だけを記載。
- [ ] release/version guard、build/pack、Release suite、diff check、exact-head CI が green。
- [ ] operator が v0.10.0 GitHub Release の作成・publish を明示承認。

## v0.10.0 の publish

この準備が merge され、すべての gate が green になった後に、operator は
[v0.10.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.10.0) を作成できます。
この PR 自体は package publish や Release 作成を行いません。
