# リリースノート — intent-cli v0.22.0

> **公開済み (RELEASED)。** v0.22.0 は
> https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.22.0 で公開済みです。
> release target は `c06dc49e89446bf3b723612dd72004d628914734`、workflow run
> `31903789754` は五つの job すべてで成功しました。以下の Release 本文と evidence は、
> この roll の merge 後に orchestration が適用できる source です。

clean-install で観測した version string は正確に `intent-cli 0.22.0-c06dc49-G708` です。
verification command は `JTechJapan.IntentSystem.Cli --version 0.22.0` です。
NuGet public package index:
https://www.nuget.org/packages/JTechJapan.IntentSystem.Cli/0.22.0。
直前の出荷範囲は [release-notes-v0.21.0.md](release-notes-v0.21.0.md) にリンクし、
ここでは重複記載しません。

## Preview lane — feature description より先に読む

G695-G708 は引き続き preview-through-1.x の surface です。
[1.0 compatibility promise](1.0-compatibility-promise.md) が明示的に更新されるまで
この範囲の外であり、minor version だけから stability guarantee を推測しないように
この境界を記録します。

## 十四件の merged feature unit

v0.22.0 には G695 から G708 まで正確に十四件の merged feature unit があります。
この inventory は `git log --first-parent v0.21.0..origin/main` で導出しました。
remote-tracking name が無い場合の同値な checked-out-main verification は
`git log --first-parent v0.21.0..main` です。その exact first-parent range は十五 commit、つまり post-v0.21.0 roll 一件と下記十四件の
merge commit です。記載した PR はすべて MERGED で、下記の full merge commit はすべて
`main` で確認できます。

- G695 — PR #1504; durable continuation-chain guidance が next transition または named blocker を出力し、merge commit `dfb6a539fe5c8c76bf29c54eafb643b63af3e48d` (main で確認)。
- G696 — PR #1506; per-kind seat command-style guidance が installed CLI から到達可能になり、merge commit `1a9cf3a9b733de4ffe600c5d528f0e9b30cf5339` (main で確認)。
- G697 — PR #1508; topology workspace move が first-class になり、team rebuild で hand-edit が不要になり、merge commit `2021f1d6196fab2b8bb23fb28176f26dddbeb59b` (main で確認)。
- G698 — PR #1510; role-scoped closeout record により二つの role の evidence が race せず共存し、merge commit `86f1ffdf9d9704d15d440b21d4db628bff607cf6` (main で確認)。
- G699 — PR #1512; supervision emission hygiene に same-key backoff、named park、status debounce が入り、merge commit `48ca83a0f1cf13080f7ddf04a699f42942d919c9` (main で確認)。
- G700 — PR #1514; host-state git write に bounded で observable な `index.lock` retry が入り、merge commit `c2e7d6002a912b2b712a04f0bc4976d6ba76e47b` (main で確認)。
- G701 — PR #1517; ADR-0006 が guide primacy を normative にし、registry-backed herdr layout と three-tier dialog rule を定義し、merge commit `b95a2d7634cdd72b2ef69fce983062aca6dcbab8` (main で確認)。
- G702 — PR #1520; npm distribution channel が global install の `intent-cli` と self-install しない npx guidance を提供し、merge commit `1746a6d0c2133f7724c57f7a26caed55c93a3e8f` (main で確認)。
- G703 — PR #1526; `intent-cli update` が executable path から channel を導出し、per-channel action を適用し、merge commit `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` (main で確認)。
- G704 — PR #1529; supervise install が setup を validate し、log を明示し、first cycle を prove し、merge commit `0c49569129635be6a35a07a3e9cfdf3621b44c4c` (main で確認)。
- G705 — PR #1531; feedback-channel guidance が render-only で GitHub issue を file せず、merge commit `6163d9b3589d331c6a82bb72923a91a15aef029b` (main で確認)。
- G706 — PR #1522; design-thread terminal rule が pane reading を liveness evidence に限定し、fallback observation route を戻し、merge commit `abf6dc640eb3131564d146df9783d453d0e5c70a` (main で確認)。
- G707 — PR #1524; supervision finding が escalation 前に own cycle 内で corroborate され、merge commit `be29c896b01df6a48502748e155e07b076c563c6` (main で確認)。
- G708 — PR #1533; closeout output が実際に write した内容を報告し、runs gap を明示的に repair 可能にし、merge commit `55d54951b677e8aa6f2d2f0bd49d278ed4e63531` (main で確認)。

### Full first-parent range accounting

完全な range を以下で説明します。post-v0.21.0 roll は context であり、release execution
unit ではありません。それ以外の十四行が G695-G708 の unit です。

| first-parent commit | meaning |
| --- | --- |
| `8ee71bc81697b91b9e155a52a25b64225ecc7427` | PR #1502、post-v0.21.0 version roll、release execution unit ではありません |
| `dfb6a539fe5c8c76bf29c54eafb643b63af3e48d` | G695、PR #1504 |
| `1a9cf3a9b733de4ffe600c5d528f0e9b30cf5339` | G696、PR #1506 |
| `2021f1d6196fab2b8bb23fb28176f26dddbeb59b` | G697、PR #1508 |
| `86f1ffdf9d9704d15d440b21d4db628bff607cf6` | G698、PR #1510 |
| `48ca83a0f1cf13080f7ddf04a699f42942d919c9` | G699、PR #1512 |
| `c2e7d6002a912b2b712a04f0bc4976d6ba76e47b` | G700、PR #1514 |
| `b95a2d7634cdd72b2ef69fce983062aca6dcbab8` | G701、PR #1517 |
| `1746a6d0c2133f7724c57f7a26caed55c93a3e8f` | G702、PR #1520 |
| `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` | G703、PR #1526 |
| `0c49569129635be6a35a07a3e9cfdf3621b44c4c` | G704、PR #1529 |
| `6163d9b3589d331c6a82bb72923a91a15aef029b` | G705、PR #1531 |
| `abf6dc640eb3131564d146df9783d453d0e5c70a` | G706、PR #1522 |
| `be29c896b01df6a48502748e155e07b076c563c6` | G707、PR #1524 |
| `55d54951b677e8aa6f2d2f0bd49d278ed4e63531` | G708、PR #1533 |

### Origins and minor rationale

external defect は隣接する measurement と operator work から区別できます。G695 は外部 defect
#1491、G706 は #1516、G707 は #1518、G708 は #1527 に続きます。measured な supervise stall
が G704 になりました。G696-G700 は own backlog の improvement です。guide primacy、npm
distribution、update channel、feedback guidance は operator decision を記録します。
minor version はこの十四件の user-visible な orchestration / distribution surface により妥当で、
preview boundary と 1.0 compatibility promise は明示したままです。

## 公開済み Release evidence

- 既存の Release には十六個の asset と、それぞれの checksum companion が添付されています。
  `intent-cli-0.22.0-linux-x64.tar.gz`、`intent-cli-0.22.0-linux-x64.tar.gz.sha256`、
  `intent-cli-0.22.0-osx-arm64.tar.gz`、`intent-cli-0.22.0-osx-arm64.tar.gz.sha256`、
  `intent-cli-0.22.0-win-x64.zip`、`intent-cli-0.22.0-win-x64.zip.sha256`、
  `intent-system-0.22.0.tgz`、`intent-system-0.22.0.tgz.sha256`、
  `j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz`、
  `j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz.sha256`、
  `j-tech-japan-intent-cli-linux-x64-0.22.0.tgz`、
  `j-tech-japan-intent-cli-linux-x64-0.22.0.tgz.sha256`、
  `j-tech-japan-intent-cli-win32-x64-0.22.0.tgz`、
  `j-tech-japan-intent-cli-win32-x64-0.22.0.tgz.sha256`、
  `JTechJapan.IntentSystem.Cli.0.22.0.nupkg`、
  `JTechJapan.IntentSystem.Cli.0.22.0.nupkg.sha256` です。
- NuGet は version `0.22.0` で public です。package page と public index は
  https://www.nuget.org/packages/JTechJapan.IntentSystem.Cli/0.22.0 と
  https://api.nuget.org/v3/registration5-gz-semver2/jtechjapan.intentsystem.cli/index.json です。

## Distribution boundary — v0.22.0 の npm skip

v0.22.0 の npm registry publication は、npm organisation (organization) access、
package-name reservation、`NPM_TOKEN` が absent または未完了の operator account prerequisite のため
skip しました。registry に npm package は publish しておらず、G702 npm publish step は v0.22.0 では実行しません。
これは defect ではなく distribution gap です。既存の Release には四つの npm tarball と checksum companion が添付されています:
`intent-system-0.22.0.tgz`、`intent-system-0.22.0.tgz.sha256`、
`j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz`、`j-tech-japan-intent-cli-darwin-arm64-0.22.0.tgz.sha256`、
`j-tech-japan-intent-cli-linux-x64-0.22.0.tgz`、`j-tech-japan-intent-cli-linux-x64-0.22.0.tgz.sha256`、
`j-tech-japan-intent-cli-win32-x64-0.22.0.tgz`、`j-tech-japan-intent-cli-win32-x64-0.22.0.tgz.sha256`。この roll は credentials や operator account action を行いません。

## Release body source for orchestration — リリース本文 source

この PR の merge 後、orchestration は既存 Release に次の source を適用できます:

`gh release edit v0.22.0 --repo J-Tech-Japan/intent-system --notes-file docs/ja/release-notes-v0.22.0.md`

この implementation roll は command を実行せず、GitHub Release state を変更しません。
