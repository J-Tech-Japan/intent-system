# リリースノート — intent-cli v0.29.0

> **PREPARED / NOT PUBLISHED。** これは独自に測定した G773–G777 corrective chain の
> prepare-only release-note set です。この unit は tag、GitHub Release、package
> publish、workflow trigger/configuration change、product source change を行いません。

v0.29.0 の GitHub Release はまだ存在せず、この notes は preparation evidence
だけです。matching install query は
`JTechJapan.IntentSystem.Cli --version 0.29.0` です。

この preparation 後の policy は次のとおりです:

```json
{
  "stableVersion": "0.29.0",
  "nextVersion": "0.29.1"
}
```

`0.29.1` は replaceable development placeholder だけです。次の real release
number を決定したものではなく、後続の release-prep packet が測定して決めます。
EN/JA の v0.29.1 file は DRAFT planning scaffold であり、changelog ではありません。

## 独自に測定した minor justification

version decision は `eng/version.json` から推測せず、Release build と tagged behavior
から測定しました。named base revision は
`65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf` です。

```bash
# normal clean Release build of the named base revision
dotnet build IntentSystem.sln --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.28.1-65e02d8-G772

# explicit release-prep identity on the same named base revision
dotnet build IntentSystem.sln --configuration Release --no-restore -p:Version=0.29.0
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.29.0-65e02d8-G772
```

最初の banner は current `nextVersion` placeholder identity であり、**v0.29.0
ではありません**。二つ目の banner は explicit `-p:Version=0.29.0` measurement
だけです。published v0.29.0 release の version は `release.yml` が導出します:
release event では `RAW` が `v0.29.0` tag、`VERSION="${RAW#v}"` が `0.29.0` です。
`eng/version.json` は local builds と dry runs を管理します。この preparation は
v0.29.0 tag を作成していません。

tagged v0.28.0 Release build は新しい G773 route を reject しました:

```text
$ dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll notify supervise repair-unreadable --format json
invalid-notification: Unknown argument 'repair-unreadable'.
```

named base はこの route を提供します:

```text
Usage: intent-cli notify supervise repair-unreadable --domain <d> --team <t> [--routing-root <host-root>] [--dry-run|--write] [--format markdown|json]
```

v0.28.0 release policy の auditable rule は **command-route addition は minor bump、
option-level addition は command route として数えない** です。G773 はこの一つの route
を追加したため、operator は v0.29.0 を選びました。G776 の `--wake-command` は
option-level declaration であり、二つ目の route として明示的に数えません。

## Release inventory: 正確に五 units

exact first-parent range は
`v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf` です。git は五 commit を
測定し、そのすべてを operator-observable outcome とともに次に記録します。

- G773 — PR #1686 / issue #1685; merge commit `370cfd3ad6b008503fc38d11822a31617949c372`。
  **Operator-observable outcome:** `notify supervise repair-unreadable` がまず
  preview し、read 不能な supervision evidence を reconstruction せず quarantine します。
- G774 — PR #1690 / issue #1687; merge commit `9f124d86b0cc76366d2bb8cfcdcffed17a9eca66`。
  **Operator-observable outcome:** rendered design guidance が implementation へ
  packet を渡す前に bounded な `packet_authoring_check` を表示します。
- G775 — PR #1691 / issue #1688; merge commit `75216283875b08ade3d100de7ddabe3fad0bd21c`。
  **Operator-observable outcome:** external frontend relabel guidance が residence、
  reader、routing root を update-residence move と区別します。
- G776 — PR #1692 / issue #1689; merge commit `b766f2d0961c665a2d6216c7ed24755556560626`。
  **Operator-observable outcome:** declared external wake command を durable canonical
  report の後の courtesy として表示し、intent-cli は実行・管理せず、undeclared output は変えません。
- G777 — PR #1694 / issue #1693; merge commit `65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf`。
  **Operator-observable outcome:** non-zero unreadable count が operator を
  repair-unreadable dry-run first、`--write` second へ導きます。

## First-parent accounting

```bash
git rev-list --first-parent --reverse v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf
git rev-list --first-parent --count v0.28.0..65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf
# 5
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `370cfd3ad6b008503fc38d11822a31617949c372` | G773 / PR #1686 / issue #1685 | included |
| `9f124d86b0cc76366d2bb8cfcdcffed17a9eca66` | G774 / PR #1690 / issue #1687 | included |
| `75216283875b08ade3d100de7ddabe3fad0bd21c` | G775 / PR #1691 / issue #1688 | included |
| `b766f2d0961c665a2d6216c7ed24755556560626` | G776 / PR #1692 / issue #1689 | included |
| `65e02d86d5e9e415d1fe934b0d5e8bad87af9ccf` | G777 / PR #1694 / issue #1693 | included |

## Truthfulness boundaries

- `repair-unreadable` は read 不能な line を evidence として**verbatim** に
  quarantine します。reconstruction を claim せず、automatic ではなく、read 時に
  実行されることもありません。
- declared wake channel がない team の delegate result と task envelope は
  **zero changed bytes** です。declared wake は courtesy-only であり、durable
  canonical record の代わりにはなりません。
- audit transaction `6279ad14` で real host の **9 records** を repair した事実は、
  観測した transaction で repair path が動いた evidence です。fleet-cleanliness
  claim ではありません。

## Prepare-only verification

PR には exact parent-absence、focused、adjacent、G613、full Release、build、
`git diff --check`、CI の evidence を記録します。この change は release notes、
version policy、readiness documentation、test guard に限定し、tag、GitHub Release、
package publish、workflow change、product-source change を含みません。
