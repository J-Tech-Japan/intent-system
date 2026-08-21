# リリースノート — intent-cli v0.23.2

> **PREPARED / NOT PUBLISHED。** これは v0.23.1 後に開かれた line の
> prepare-only release-note set です。実質的な release documentation ですが、
> tag、GitHub Release、package が publish 済みであることを示すものではありません。

準備済み functional line の install verification では、下記の Release build から
`JTechJapan.IntentSystem.Cli --version` が
`intent-cli 0.23.2-2caa6d4-G728` を返しました。

## v0.23.2 の内容

公開済み v0.23.1 tag は `d49984dae761d589b2568f8eb1677ce3ff2facbc7` です。
下記の exact inventory は、その tag 以降に未出荷の六つの execution unit だけを含みます。
各項目は merge commit と operator が観測できる結果を記録します。

- G723 — PR #1571; merge commit `0252948e631194087a2cdacc7605f6023d8d0213`。
  **Operator-visible fix:** coordinating role が `orchestrator` と記録された topology は
  role-alias resolution を通って heartbeat に到達し、genuinely missing seat は actionable
  finding になります。
- G724 — PR #1572; merge commit `771d5e9d147997cf184e5c8db6be2407cee4b6cf`。
  **Operator-visible fix:** 二つの session-layer domain が coexist でき、domain-B の worker
  complete は domain A を silently rewrite せず、legacy recovery は host default を推測せず
  recorded domains を名前にします。
- G725 — PR #1576; merge commit `6820fef35dad12c07ef936278bf40e4a2071772e`。
  stalled-work check **detects and reports a skipped post-release version roll** します。
  release が無い場合や roll が正しい場合は silent で、roll を実行も repair もしません。
- G726 — PR #1577; merge commit `728989c6ef5bc7166718f0b7222a22c95d1c2e2e`。
  release path **gates and refuses an unreachable tag** by comparing the exact commit with the
  repository default branch before publication。history の rewrite や unreachable commit の
  repair はしません。
- G727 — PR #1578; merge commit `5d2d1ce51530c035944194e6cb762246fc589b13`。
  stalled-work **reports checkout freshness/provenance**。stale checkout は
  actionable、current checkout は silent、offline evidence は unknown で、report は fetch、
  pull、reset、sync を実行しません。
- G728 — PR #1580; merge commit `2caa6d42f1578d57c5667db1d475024d1afbc9f9`。
  post-release policy roll は stable `0.23.1` と next `0.23.2` を記録し、この line の
  release-note preparation を開始します。tag、publish、次の post-release roll は行いません。

`eb65cbc100e9a2bea9f3c7d912315233d0a6720c` は inventory item として意図的に含めません。
その content は公開済み v0.23.1 tag `d49984dae761d589b2568f8eb1677ce3ff2facbc7` に
ship 済みです。後から merge された位置だけでは、この line の新しい content とは判断できません。

## 六つの inventory の accounting

| merge commit | unit | PR |
| --- | --- | --- |
| `0252948e631194087a2cdacc7605f6023d8d0213` | G723 | #1571 |
| `771d5e9d147997cf184e5c8db6be2407cee4b6cf` | G724 | #1572 |
| `6820fef35dad12c07ef936278bf40e4a2071772e` | G725 | #1576 |
| `728989c6ef5bc7166718f0b7222a22c95d1c2e2e` | G726 | #1577 |
| `5d2d1ce51530c035944194e6cb762246fc589b13` | G727 | #1578 |
| `2caa6d42f1578d57c5667db1d475024d1afbc9f9` | G728 | #1580 |

## Prepared functional head と identity evidence

Functional content は、この G729 documentation unit が checkout を変更する前の
exact prepared functional head
`2caa6d42f1578d57c5667db1d475024d1afbc9f9` で independently build / measure しました。
実行した Release build と version query は次のとおりです。

```bash
dotnet build src/IntentSystem.Cli/IntentSystem.Cli.csproj --configuration Release
dotnet src/IntentSystem.Cli/bin/Release/net10.0/IntentSystem.Cli.dll --version
```

- **Release identity evidence source revision:**
  `2caa6d42f1578d57c5667db1d475024d1afbc9f9`
- **Display identity from that build:** `intent-cli 0.23.2-2caa6d4-G728`
- **Installed comparison:** `intent-cli 0.23.1-d49984d-G721`

Installed 0.23.1 と prepared functional head の各 command group を `--help` で直接比較した
count は次のとおりです。全 group unchanged であり、prepared line に新しい command surface
はありません。受け入れ済みの version roll は 0.23.2 であり、この比較はその settled line の
verification であって、新しい version decision ではありません。

| command group | installed 0.23.1 | prepared functional head | result |
| --- | ---: | ---: | --- |
| `automation` | 39 | 39 | unchanged |
| `notify` | 9 | 9 | unchanged |
| `session-layer` | 6 | 6 | unchanged |
| `guide` | 35 | 35 | unchanged |
| `worker` | 8 | 8 | unchanged |
| `issue` | 9 | 9 | unchanged |
| `review` | 3 | 3 | unchanged |
| `closeout` | 1 | 1 | unchanged |
| `claim` | 4 | 4 | unchanged |
| `metadata` | 2 | 2 | unchanged |

この G729 release-prep unit 自体は prepared functional head の外側です。この unit の
correction は、notes を載せる documentation merge commit にだけ存在します。eventual tag は
earlier functional head ではなく、その documentation merge commit に land します。

## Prepare-only boundary

この unit が変更するのは v0.23.2 の二つの release-note mirror と、それらの release-note
documentation tests だけです。`eng/version.json`、source、workflows、shipped v0.23.0/v0.23.1
notes、tag、GitHub Release、package、post-release roll は変更しません。No tag、no GitHub Release、
no publish action がこの evidence に含まれます。
