# リリースノート — intent-cli v0.11.0

> prepare-only です。この PR は GitHub Release、tag、package publish、announcement、
> merge、post-release version roll を作成しません。readiness gate 後の Release 作成は
> operator が行います。

## 対象範囲

この minor release に含まれる、検証済み `main` merge は次の五つだけです。

- G601 — [PR #1306](https://github.com/J-Tech-Japan/intent-system/pull/1306)、merge `237ff790ecf9`。
- G602 — [PR #1308](https://github.com/J-Tech-Japan/intent-system/pull/1308)、merge `de80aabf7fb7`。
- G603 — [PR #1310](https://github.com/J-Tech-Japan/intent-system/pull/1310)、merge `2912127275eb`。
- G604 — [PR #1312](https://github.com/J-Tech-Japan/intent-system/pull/1312)、merge `72ccaba3a859`。
- G605 — [PR #1314](https://github.com/J-Tech-Japan/intent-system/pull/1314)、merge `d9afcaa915fa`。

各 commit は `main` 上で解決することを検証済みです。他の slice は含みません。
既出の範囲は [v0.10.0](release-notes-v0.10.0.md) を参照してください。

## MINOR の根拠

これは仮定ではなく検証可能な minor bump です。`v0.10.0` と比較して、
`session-layer marker generate` は同 tag には存在しなかった新しい command です。
per-team topology surface も本 release で初出です。`topology record`、`show`、
`validate` は明示的な `--domain`（および `--team`）を要求し、team ごとの topology を
保存します。version policy は新しい command surface を minor bump に割り当てます。

## 運用上の目的

v0.11.0 は、recorded truth が自身の identity を持つようにします。workspace は記録済みの
truth に束縛された generated marker から自分が扱う mode と team を表示でき、mode switch は
具体的な migration plan を返して residue を表面化します。同じ番号の別 domain issue は foreign
state を変更できず、任意数の team が一つの host の topology surface を共有できます。operating
guide は測定済み herdr 0.8.0 baseline と live-handoff recovery の注意点を説明します。

## 挙動変更と migration

1. **topology identity の明示。** `session-layer topology record`、`show`、`validate`
   は `--domain` と `--team` を必須にします。config の `default_domain` に依存した invocation
   は usage guidance とともに停止するため、runbook snippet は recorded domain/team を渡すよう
   更新してください。
2. **machine-local な per-team topology。** topology は CLI-owned directory-local gitignore を
   伴う `.intent-cli/topology/<domain>/<team>.json` に移りました。legacy fixed
   `role-pane-mapping.json` は新ファイルが無い場合だけ読み、re-record を警告します。machine
   value は自動 copy されません。new/legacy の不一致は validate、doctor/preflight、show、notify
   のすべてで fail-closed です。team ごとにその machine で re-record して移行してください。
3. **repo-qualified な worker completion。** `worker complete` は queue item を
   repository-qualified issue identity で照合します。別 repository の同番号は match せず、
   cross-domain write は両 identity を示して fail-closed になります。破損した completed
   linkage は recorded かつ merged の evidence からだけ repair されます。
4. **明確な preflight cause。** documented empty marker placeholder は malformed/not-ready では
   なく informational な `marker-not-generated` です。advisory other-mode residue と structural
   notify failure が共存するときは、structural failure が報告 cause のままです。

## インストールまたは更新

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.11.0
```

## リリース準備ゲート

- [ ] `eng/version.json` は `stableVersion` `0.10.0` / `nextVersion` `0.11.0`。
- [ ] EN/JA notes は上記五つの検証済み merge だけを記載。
- [ ] minor-command 比較で、新しい marker generate と explicit-domain topology surface が
      `v0.10.0` に無いことを確認。
- [ ] release/version guard、full suite、diff check、exact-head CI が green。
- [ ] operator が v0.11.0 GitHub Release の作成・publish を明示承認。

## v0.11.0 の publish

この準備が merge され、すべての gate が green になった後に、operator は
[v0.11.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.11.0) を作成できます。
この PR 自体は package publish や Release 作成を行いません。
