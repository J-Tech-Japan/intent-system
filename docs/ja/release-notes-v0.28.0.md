# リリースノート — intent-cli v0.28.0

> **PREPARED / NOT PUBLISHED。** これは独自に測定した v0.28.0 line の
> prepare-only release-note set です。この unit では tag、GitHub Release、package
> publish、post-release roll を行いません。

v0.28.0 の GitHub Release はまだ存在せず、この notes は preparation evidence
だけです。matching install query は
`JTechJapan.IntentSystem.Cli --version 0.28.0` です。

この release-prep 後の policy は次のとおりです:

```json
{
  "stableVersion": "0.28.0",
  "nextVersion": "0.28.1"
}
```

`0.28.1` は replaceable development placeholder だけです。次の real release
number を決定したものではなく、次の release-prep packet が測定して決めます。

## 独自に測定した command-surface difference

version decision は `eng/version.json` から推測せず、tagged v0.27.0 baseline と
この prepared head をそれぞれ Release build して測定しました:

```bash
# tagged baseline
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.27.0-f43fbd1-G753

# normal clean build of named revision 565530e5c965d55335790c9446ef0686988d14c8
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.27.1-565530e-G769
# release-prep build with explicit -p:Version=0.28.0
dotnet build IntentSystem.sln --configuration Release -p:Version=0.28.0
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
# intent-cli 0.28.0-565530e-G769
```

named revision の normal clean build は再現可能に
`intent-cli 0.27.1-565530e-G769` を出力します。上の `0.28.0` identity は
explicit version-policy input を使った release-prep build の結果です。published
release version は `release.yml` が `v0.28.0` tag の `RAW` tag から `VERSION` を
導出し、`eng/version.json` は local builds と dry runs を管理します。

child で別に観測した installed baseline は
`intent-cli 0.27.1-5d553b7-G756` でした。これは環境 evidence として記録し、
tagged v0.27.0 の比較対象とは置き換えません。32 個すべての command group と
各 direct subcommand の help を programmatic sweep で呼び出しました。tagged
v0.27.0 build は group descriptor 32 + direct-help usage 72 = **104 usages**、
prepared build は 32 + 74 = **106 usages** でした。
この比較で route の追加は正確に一つで、removal はありません。

| measured surface | tagged v0.27.0 Release build | prepared v0.28.0 Release build |
| --- | ---: | ---: |
| group descriptors | 32 | 32 |
| direct-help usage lines | 72 | 74 |
| total usages | 104 | 106 |
| `claim stranded` | absent/unimplemented | present |
| `notify supervise liveness` | absent (`invalid-notification: Unknown argument 'liveness'.`) | present |

追加された command route は次の二つです:

```text
Usage: intent-cli claim stranded [list] [--format json|markdown] (reports metadata-branch records absent from canonical branch)
Usage: intent-cli notify supervise liveness --domain <d> --team <t> [--routing-root <host-root>] [--format markdown|json]
```

既存の `notify supervise repair-cycle-history` は v0.27.0 baseline にすでに
含まれています。route が変わらない範囲で、その usage と `automation`、`worker`、
`state-doctor`、`closeout-drift-check` の surface は byte-identical でした。
option-level の追加は新しい command route として数えていません。測定した二つの
route 追加が、policy file だけではないこの minor bump の監査可能な reason です。

## Release inventory: 正確に 18 units

range は正確に `v0.27.0..565530e5c965d55335790c9446ef0686988d14c8` です。
この range の first-parent commit 18 個を git からすべて読み、以下に account しました。
各 entry は operator が観測できる outcome を示します。

- G754 — PR #1641 / issue #1640; merge commit `6ea81ac85e5fc104d5cd954766c916445f751183`。
  **Operator-observable outcome:** v0.27.0 後の version policy を測定した
  v0.28.0 preparation line に進め、後続の real number は選びません。
- G755 — PR #1643 / issue #1642; merge commit `9a30e95accc9d92d56ba0bdb62b1974ec7ab8302`。
  **Operator-observable outcome:** canonical claim reader が remote default branch
  を使い、current checkout branch を authority と誤認しません。
- G756 — PR #1646 / issue #1644; merge commit `ec261ec4c16454d122a3baec0d48393a4245f513`。
  **Operator-observable outcome:** external-resident role の effective reader を
  show し、recorded/effective path の divergence を advisory に示します。
- G757 — PR #1648 / issue #1645; merge commit `071ccf2c988e6244633c0971c8098fbd31b17093`。
  **Operator-observable outcome:** external role が caller-held cursor と bounded
  wait で自分の event を collect できます。
- G758 — PR #1652 / issue #1647; merge commit `145c5a43c031353a5e5ad4d7ea9eb3fb7365304c`。
  **Operator-observable outcome:** checkout freshness を local containment として
  read-only に判定し、ancestor の default tip を current と扱います。
- G759 — PR #1650 / issue #1649; merge commit `c6e6922e8ca89520465adfa8f69375eefd5d4fa6`。
  **Operator-observable outcome:** file-backed delegation が absolute task envelope
  を read and execute する指示を出し、inline delivery は変えません。
- G760 — PR #1653 / issue #1651; merge commit `5d553b7a0aeecf8d9939080eada9772963fe35c8`。
  **Operator-observable outcome:** task-envelope report を recipient topology cwd
  に保存し、cwd が無い場合は安全な placeholder を使います。
- G761 — PR #1660 / issue #1655; merge commit `5a6e850412beb5cd515991b3486022e457726f6a`。
  **Operator-observable outcome:** operator が CAS safety 付きで external と herdr の
  residence transition を明示的に確認できます。
- G762 — PR #1659 / issue #1657; merge commit `ff11a355377fe2b1698cce1e14f39d8c79c20bd5`。
  **Operator-observable outcome:** rendered design guidance が external-resident
  seat の role-scoped collect receive contract を示します。
- G763 — PR #1667 / issue #1663; merge commit `6cc2b05127f7dc8c9080e425eb5af8e0e099ace7`。
  **Operator-observable outcome:** stranded metadata-branch claim を report し、
  remote receipt を確認しながら migrate できます。
- G764 — PR #1666 / issue #1664; merge commit `642a86626f95fe271be663fca9d79240a58e6fd7`。
  **Operator-observable outcome:** loopless receiver への request-update を同じ wake
  で受ける guidance を追加し、G524 cap と timer loop は保持します。
- G765 — PR #1670 / issue #1665; merge commit `db5394d75e267e17606f9a5fb96b3607ec58b435`。
  **Operator-observable outcome:** persistence metadata、keep/legacy reconciliation、
  read-only liveness を lifecycle execution なしに観測できます。
- G766 — PR #1671 / issue #1669; merge commit `7adb2b5cac8090865d19c864842dbed48ffab7d2`。
  **Operator-observable outcome:** metadata-branch claim によって empty canonical
  branch が configured ownership store に見えることを防ぎます。
- G767 — PR #1673 / issue #1672; merge commit `4dcf1916a94dfb871a1249fd60a3a4569b0a032c`。
  **Operator-observable outcome:** malformed な supervision JSONL record を報告し、
  valid record を clean absence にせず読めます。
- G768 — PR #1676 / issue #1674; merge commit `af8b82c37c27ff319c7468084b8ac59590f887fb`。
  **Operator-observable outcome:** real concurrent writer が cycle、stall、prompt-audit
  の record を落とさず atomic に append できます。
- G769 — PR #1677 / issue #1675; merge commit `a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324`。
  **Operator-observable outcome:** explicit routing root を尊重し、missing store と
  empty history の実在 store を区別します。
- G770 — PR #1680 / issue #1678; merge commit `b111fc644dfca24b911c26eef6bad9c784ad6cd4`。
  **Operator-observable outcome:** no-flag の not-found と empty-history を含め、
  すべての successful liveness response に supervision state を出します。
- G771 — PR #1682 / issue #1681; merge commit `565530e5c965d55335790c9446ef0686988d14c8`。
  **Operator-observable outcome:** claim outcome は bounded cleanup failure を tolerate
  し、real cause と leftover warning を保持し、stale root だけを sweep します。

## First-parent accounting

正確な accounting は次で測定しました:

```bash
git rev-list --first-parent --reverse v0.27.0..565530e5c965d55335790c9446ef0686988d14c8
git rev-list --first-parent --count v0.27.0..565530e5c965d55335790c9446ef0686988d14c8
# 18
```

| first-parent commit | classification | release inventory |
| --- | --- | --- |
| `6ea81ac85e5fc104d5cd954766c916445f751183` | G754 / PR #1641 | included |
| `9a30e95accc9d92d56ba0bdb62b1974ec7ab8302` | G755 / PR #1643 | included |
| `ec261ec4c16454d122a3baec0d48393a4245f513` | G756 / PR #1646 | included |
| `071ccf2c988e6244633c0971c8098fbd31b17093` | G757 / PR #1648 | included |
| `145c5a43c031353a5e5ad4d7ea9eb3fb7365304c` | G758 / PR #1652 | included |
| `c6e6922e8ca89520465adfa8f69375eefd5d4fa6` | G759 / PR #1650 | included |
| `5d553b7a0aeecf8d9939080eada9772963fe35c8` | G760 / PR #1653 | included |
| `5a6e850412beb5cd515991b3486022e457726f6a` | G761 / PR #1660 | included |
| `ff11a355377fe2b1698cce1e14f39d8c79c20bd5` | G762 / PR #1659 | included |
| `6cc2b05127f7dc8c9080e425eb5af8e0e099ace7` | G763 / PR #1667 | included |
| `642a86626f95fe271be663fca9d79240a58e6fd7` | G764 / PR #1666 | included |
| `db5394d75e267e17606f9a5fb96b3607ec58b435` | G765 / PR #1670 | included |
| `7adb2b5cac8090865d19c864842dbed48ffab7d2` | G766 / PR #1671 | included |
| `4dcf1916a94dfb871a1249fd60a3a4569b0a032c` | G767 / PR #1673 | included |
| `af8b82c37c27ff319c7468084b8ac59590f887fb` | G768 / PR #1676 | included |
| `a92a53fda2f8901e49b0e60d5d7c00d5c2a6c324` | G769 / PR #1677 | included |
| `b111fc644dfca24b911c26eef6bad9c784ad6cd4` | G770 / PR #1680 | included |
| `565530e5c965d55335790c9446ef0686988d14c8` | G771 / PR #1682 | included |

inventory は G754 から G771 まで正確に 18 units であり、別の roll commit を
黙って落としたり release unit として数えたりしていません。

## supervision-history の honest な chain

G768 は concurrent partial write による新しい corruption を止めますが、既存の
damage は repair しません。real host には今も **9 unreadable records** があります。
G771 は post-commit deletion failure を harmless にし、primary cause を見えるまま
にしますが、**250 ms × 3** の cleanup bound は変えず、deletion を reliable に
もしません。real deletion は約 **1.8 s** のままです。これは測定した limitation
であり、incident が消えたという claim ではありません。G768/G771 entry と
この test guard が両方の statement を pin します。

Issue **#1679** と **#1662** は、それぞれ G771 と G765/G770 の work で closed
です。**#1661** はこの range より前にすでに fixed でした。これらを note に
残すのは、operator が report と release evidence を結び付けるためです。

## Prepare-only verification

Final child Release verification は `Failed: 0, Passed: 5445, Skipped: 1,
Total: 5446` でした。one skipped test は既存の environment-gated test です。
`git diff --check` は clean でした。focused と adjacent の guard count は
PR evidence とともに報告します。product source change、tag、GitHub Release、
publish、post-release action はこの preparation に含まれません。
