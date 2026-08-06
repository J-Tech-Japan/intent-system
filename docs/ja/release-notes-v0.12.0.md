# リリースノート — intent-cli v0.12.0

> prepare-only です。この PR は GitHub Release、tag、package publish、workflow 実行、
> merge、post-release version roll を作成しません。readiness gate 後の Release 作成は
> operator が行います。

## 対象範囲

この minor release に含まれる検証済み `main` merge は次のとおりです。

- G610 — [PR #1324](https://github.com/J-Tech-Japan/intent-system/pull/1324)、merge `48204646`。
- G611 — [PR #1328](https://github.com/J-Tech-Japan/intent-system/pull/1328)、merge `4f4106f947e5`。
- G612 — [PR #1326](https://github.com/J-Tech-Japan/intent-system/pull/1326)、merge `1b1206a56e71`。
- G613 — [PR #1330](https://github.com/J-Tech-Japan/intent-system/pull/1330)、merge `f3d0838a1da0`。
- G614 — [PR #1334](https://github.com/J-Tech-Japan/intent-system/pull/1334)、merge `a260b63bd4a1`。
- G615 — [PR #1332](https://github.com/J-Tech-Japan/intent-system/pull/1332)、merge `940997c6b767`。
- G616 — [PR #1336](https://github.com/J-Tech-Japan/intent-system/pull/1336)、merge `21f6fb3c8a3b`。
- G617 — [PR #1338](https://github.com/J-Tech-Japan/intent-system/pull/1338)、merge `207a3d2e20e0`。
- G618 — [PR #1340](https://github.com/J-Tech-Japan/intent-system/pull/1340)、merge `7f2bb23bd4a5`。
- G619 — [PR #1342](https://github.com/J-Tech-Japan/intent-system/pull/1342)、merge `36b89ac9fbfc`。
- G620 — [PR #1344](https://github.com/J-Tech-Japan/intent-system/pull/1344)、merge `72878b63ff97`。
- G621 — [PR #1346](https://github.com/J-Tech-Japan/intent-system/pull/1346)、merge `a1886218f56c`。
- G623 — [PR #1350](https://github.com/J-Tech-Japan/intent-system/pull/1350)、merge `c04e137`。
- G624 — [PR #1352](https://github.com/J-Tech-Japan/intent-system/pull/1352)、merge `ccd4f29`。
- G625 — [PR #1354](https://github.com/J-Tech-Japan/intent-system/pull/1354)、merge `06f1a71`。
- G626 — [PR #1356](https://github.com/J-Tech-Japan/intent-system/pull/1356)、merge `2bb20d3`。
- G627 — [PR #1358](https://github.com/J-Tech-Japan/intent-system/pull/1358)、merge `5b86977`。
- G628 — [PR #1360](https://github.com/J-Tech-Japan/intent-system/pull/1360)、merge `f464a04`。

この十八件は手作業ではなく `git log v0.11.1..main` を実行して列挙しました。この範囲の
commit は、十八件の unit merge、prepare-only の notes authoring である G622（prepare-only
slice として除外）、または残った version roll と guard-fix のいずれかとして説明できます。直前の出荷範囲は
[v0.11.1](release-notes-v0.11.1.md) と [v0.11.0](release-notes-v0.11.0.md) を
参照してください。

## MINOR の根拠

これは検証可能な minor bump です。`v0.11.1` と比較して、
`session-layer topology update-kind`、`session-layer topology retire-legacy`、
`session-layer topology update-field`、`judgment-wait`、
`automation issue-publish --execution-unit` が新しい command surface であり、recipe は新たに
`delivery_method: file-backed` を宣言できます。これらの surface は `v0.11.1` には無く、
version policy は新しい command surface を minor bump に割り当てます。

## 挙動変更

1. **サポート対象の seat 操作。** agent kind は stated current kind が一致するときだけ
   `topology update-kind` で変更できます。以前は無かった field は registry-limited な
   `topology update-field` で宣言し、legacy fixed topology file は recorded evidence を伴う
   `topology retire-legacy` で退役できます。`record` は変わらず conflict を拒否します。
2. **file-backed delivery。** recipe が `delivery_method: file-backed` を宣言すると、
   intent-cli は durable で addressable な task envelope を書き、pane には 1 行の pointer だけが
   届きます。宣言がない場合、inline delivery は従来どおりです。
3. **unattended seat の readiness。** autopilot seat は allowlist 外の action を静かに自動拒否
   します。READY evidence は許可された action と denial の両方を示し、review evidence は
   liveness を成功とみなさず denial を確認します。
4. **documentation guard。** repository-wide の Markdown link/anchor guard と rolling Japanese
   terminology guard は regression があれば CI を失敗させます。

## G623–G628 の追加の挙動変更

5. **judgment の vocabulary（G623）。** `judgment-wait` が `operator-attention` に代わります。
   旧 command は 1.x を通じて `deprecate-with-alias` compatibility alias として残り、machine
   output は replacement の `judgment-wait` を示す `deprecation_warning` を持ちます。削除は
   次の MAJOR release でのみ可能です。
6. **transport の graduation（G624）。** `herdr-only` は preview ではなくなり、依存関係が少ない
   ため preferred です。`agmsg + herdr` は supported で、明示的に retired ではありません。
   どちらの transport も primary とは呼びません。
7. **dispatch outcome vocabulary（G625–G626）。** `issue-publish` は `--execution-unit` を受け付け、
   unresolved work は unresolved として報告します。observed working transition に到達した delivery は
   `working-did-not-settle` や `not-observed-within-bound` ではなく、successful な non-terminal state を
   報告します。旧名に match していた caller は **silent に match しなくなります**。machine の
   `working_transition` field に match するよう更新してください。

### Breaking change: legacy fixed-path topology read の削除（G627）

legacy `role-pane-mapping.json` だけがあり per-team record がない host は fail closed になります。
diagnostic は recovery command として `topology record` と `topology retire-legacy` を示し、自動
 migration は行いません。これは legacy-only host population に対する breaking change です。

8. **versioning policy（G628）。** 1.0 feature set は v0.12.0 で freeze します。freeze 後に追加された
   surface は `preview` として出荷し、ledger には `preview-through-1.x` として記録し、1.0
   compatibility promise の対象外とし、後続の MAJOR release で formalise します。

## 運用上の目的

v0.12.0 では、team が topology を手編集せずに seat の担当者と宣言済み delivery method を
変更でき、paste-sensitive な agent の wedge を避けられます。Japanese documentation は自然な
日本語として読め、1.0 compatibility promise とその ledger が公開された contract になります。

## Compatibility promise policy

v0.12.0 の freeze と freeze 後の preview lane は [1.0 compatibility promise](1.0-compatibility-promise.md)
で定義しています。1.x の surface が covered か preview かを判断するときは、この promise と
ledger を参照してください。

## インストールまたは更新

```bash
dotnet tool install -g JTechJapan.IntentSystem.Cli --version 0.12.0
```

## リリース準備ゲート

- [ ] `eng/version.json` は `stableVersion` `0.11.1` / `nextVersion` `0.12.0`。
- [ ] EN/JA notes は verified PR と merge commit を持つ十八件の unit、G610–G621 と G623–G628 を
      記載し、`git log v0.11.1..main` からの列挙であることを確認。
- [ ] minor 比較で、`judgment-wait`、`automation issue-publish --execution-unit`、三つの topology
      subcommand、宣言済み `delivery_method` が `v0.11.1` に無いことを確認。
- [ ] G627 の breaking change を理解していることを確認する。legacy-only host は fail closed となり、
      `topology record` または `topology retire-legacy` が必要。Release 作成前に v0.12.0 freeze と
      preview policy も確認する。
- [ ] G475、focused release-note check、full suite、diff check、exact-head CI が green。
- [ ] operator が v0.12.0 GitHub Release の作成・publish を明示承認。

## v0.12.0 の publish

この準備が merge され、すべての gate が green になった後に、operator は
[v0.12.0 GitHub Release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.12.0) を作成できます。
この PR 自体は package publish や Release 作成を行いません。
