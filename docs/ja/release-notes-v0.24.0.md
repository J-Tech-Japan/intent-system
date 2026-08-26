# リリースノート — intent-cli v0.24.0

> **PREPARED / NOT PUBLISHED。** これは v0.23.2 後の line に対する
> prepare-only release-note set です。`v0.24.0` の tag、GitHub Release、package
> publish、post-release roll はまだ実行していません。no GitHub Release exists yet for v0.24.0。

準備中の line の install verification は、prepared line が land した後に
`JTechJapan.IntentSystem.Cli --version 0.24.0` で行います。

## この line が v0.24.0 である理由

v0.23.2 後の post-release roll は、feature content が入る前に次の patch の
placeholder `0.23.3` を記録しました。Release-prep は、installed 0.23.2 には無い
二つの command surface がこの line の Release build で確認されたため、
`eng/version.json` の `nextVersion` を `0.24.0` に retarget します。

- `notify supervise shrink` — installed 0.23.2 は `Unknown argument 'shrink'` を返します。
- `session-layer topology record-host-state` — installed 0.23.2 は
  `Unknown session-layer topology subcommand` を返します。

Bump policy は新しい command surface に minor version を予約します。これは再導出した
version guess ではなく、測定済みで確定した decision です。

## v0.24.0 の内容

下記の exact inventory は、v0.23.2 後の first-parent range にある六つの release unit だけを
含みます。各項目は merged commit から記述し、operator が観測できる outcome を示します。

- G731 — PR #1589; merge commit `d168fac3cbef482879aa9521f6478e7d3a8dc6d1`。
  **Operator-visible outcome:** sender-local report は observed delivery result で recovery
  されます。report root と routing root が異なっても external append 成功なら受理され、実際の
  write failure なら `undelivered` と named `notify collect` delivery-level recovery path を示します。
  implementation seat は host write を retry せず、root も widen しません。
- G732 — PR #1591; merge commit `37068fa076ccf9eed5f1f87f92075756f4b5abf7`。
  **Operator-visible outcome:** v0.23.0 notes は shipped artifacts と npm publication gap を明記します。
  GitHub Release、NuGet、self-contained binaries は利用できますが、npm leg は registry に到達しませんでした。
- G733 — PR #1595; merge commit `0bb78b85df6467a1ebadb5c9d35e4a5ffb4c9072`。
  **Operator-visible outcome:** implementation seat は host round trip なしで GitHub issue を pushed PR まで進められます。
  child repository の Git/PR は seat が持ち、正確な host-state duty だけを canonical message channel で依頼します。
- G734 — PR #1598; merge commit `4aea6b5ef24cf86d8ef6cc2aba88b5ecf02d4e65`。
  **Operator-visible outcome:** running supervisor 中でも既存の supervision state を安全に shrink でき、readable evidence、
  audit accounting、次 cycle の append を保てます。`cycles.jsonl` も同じ safety boundary に含まれます。
- G735 — PR #1599; merge commit `2d77c557e7e7871fac70d17906c18b0c4416f185`。
  **Operator-visible outcome:** 同じ old workspace/pane を共有する role は、その pane が一つの new pane に対応すれば一緒に移動します。
  異なる old pane の一つの new pane への convergence は ambiguity として拒否され、topology record は sanctioned whole-team move path を示します。
- G736 — PR #1600; merge commit `a7d10026a9a4dd2693f464a5c5e34ce134b2c661`。
  **Operator-visible outcome:** first publish attempt の前に topology validation が legacy または all-sandboxed team の missing
  host-state capacity を示します。host-state role と envelope の declaration は route を discoverable にしますが、
  declaration は capable participant を作りません。実際に capable な host-state seat が必要です。

## First-parent range の accounting

測定に使った command は次のとおりです。

```bash
git log --first-parent v0.23.2..main
git rev-list --first-parent --count v0.23.2..main  # seven commits
```

七つの first-parent commit を下表で account します。G730 は feature の release unit ではなく、
line の内容が存在する前に patch placeholder を作った post-release version roll なので除外します。

| first-parent commit | classification | PR |
| --- | --- | --- |
| `3debf8ee2f571612f969e18ac46898de1057457f` | G730 post-release version roll; not a release unit | #1584 |
| `d168fac3cbef482879aa9521f6478e7d3a8dc6d1` | G731 release unit | #1589 |
| `37068fa076ccf9eed5f1f87f92075756f4b5abf7` | G732 release unit | #1591 |
| `0bb78b85df6467a1ebadb5c9d35e4a5ffb4c9072` | G733 release unit | #1595 |
| `4aea6b5ef24cf86d8ef6cc2aba88b5ecf02d4e65` | G734 release unit | #1598 |
| `2d77c557e7e7871fac70d17906c18b0c4416f185` | G735 release unit | #1599 |
| `a7d10026a9a4dd2693f464a5c5e34ce134b2c661` | G736 release unit | #1600 |

## Prepared functional head と identity evidence

G737 は自分自身の prepared functional head の外側にあります。六つの functional unit は、
この release-prep の documentation/version unit が policy を変更する前の exact prepared functional head
`a7d10026a9a4dd2693f464a5c5e34ce134b2c661` で測定しました。その revision の Release build は次を返しました。

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.23.3-a7d1002-G734
```

- **Release identity evidence source revision:**
  `a7d10026a9a4dd2693f464a5c5e34ce134b2c661`
- **Display identity from that Release build:** `intent-cli 0.23.3-a7d1002-G734`
- **0.23.3 と表示される理由:** pre-G737 functional head には roll 済み placeholder が残っていました。この release-prep unit が
  policy を 0.24.0 に retarget し、eventual v0.24.0 tag はこの documentation commit が land した後のものになります。

## Release-prep verification

最終 verification command と測定した count はここに記録します。

```text
Targeted release-prep guards: 164 passed, 0 failed, 0 skipped, total 164.
Full Release suite: 5232 passed, 0 failed, 1 skipped, total 5233.
```

Targeted guard は v0.24.0 inventory、version-source policy、package metadata、EN/JA developer-reference mirror を検証します。
Full Release suite は CLI test project の Release configuration で実行します。`git diff --check` も必須です。

## Prepare-only boundary

この PR が変更するのは `eng/version.json`、EN/JA v0.24.0 release notes、EN/JA developer-reference readiness section、
release-note/version tests だけです。同じ PR で不要になった v0.23.3 draft stub を削除します。source runtime behavior、
v0.23.x shipped notes、tag、GitHub Release、package、credentials、workflow、post-release version roll は変更しません。
No tag、no GitHub Release、no publish、no post-release roll がこの evidence の範囲です。
