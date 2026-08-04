# リリースノート — intent-cli v0.11.1

> prepare-only です。この PR は GitHub Release、tag、package publish、workflow 実行、
> merge、version roll を作成しません。readiness gate 後の Release 作成は operator が行います。

## 対象範囲

この patch release に含まれる検証済み `main` merge は次の二つだけです。

- G607 — [PR #1318](https://github.com/J-Tech-Japan/intent-system/pull/1318)、merge `764905194ee1`。
- G608 — [PR #1320](https://github.com/J-Tech-Japan/intent-system/pull/1320)、merge `a138e32b82a7`。

両 commit は `main` 上で解決することを検証済みです。他の slice は含みません。直前の
release scope は [v0.11.0](release-notes-v0.11.0.md) を参照してください。

## PATCH の根拠

これは検証済みの patch release です。新しい command surface は追加せず、挙動も変更しません。
v0.11.0 以降の `src` delta は `GuideModelCommand`、`GuideOnboardingCommand`、
`GuideCommandsListCommand` の presentation string に限定されます。G608 review は transport
operating contract が byte-unchanged のままであることを検証しました。したがって v0.11.1 の
install は、installed guide の transport presentation を published docs chooser と整合させます。

## 運用上の目的

G607 は orchestration-first の 02a onboarding page と docs index の並び替えを追加しました。
G608 はこの front door を完成させます。default reading trail は orchestration に到達し、最初の
decision は四つの self-contained page と dual initial prompt を持つ 2×2 pattern chooser です。
`herdr-only` と `agmsg` + herdr は条件に応じた supported choice です。primary なのは
4 スレッドモデルであり、PREVIEW は transport の maturity note に限られます。

## インストールまたは更新

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.11.1
```

## リリース準備ゲート

- [ ] `eng/version.json` は `stableVersion` `0.11.0` / `nextVersion` `0.11.1` のまま。
- [ ] EN/JA notes は、上記の verified PR と merge commit を持つ G607/G608 だけを記載。
- [ ] patch 比較で、新しい command surface が無く、`src` は上記三つの guide presentation-string
      file だけであることを確認。
- [ ] G475、focused release-note check、full suite、diff check、exact-head CI が green。
- [ ] operator が v0.11.1 GitHub Release の作成・publish を明示承認。

## v0.11.1 の publish

この準備が merge され、すべての gate が green になった後に、operator は
[v0.11.1 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.11.1) を作成できます。
この PR 自体は package publish や Release 作成を行いません。
