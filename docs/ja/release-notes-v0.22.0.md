# リリースノート — intent-cli v0.22.0

> **prepare-only / 未リリース。** この文書は v0.22.0 の Release 本文と
> readiness evidence を準備します。この準備 PR は GitHub Release または tag を作成せず、
> package を publish せず、credentials を扱いません。`v0.22.0` はこの準備 PR では
> release されません。

Release を別途承認した後の install verification:
`JTechJapan.IntentSystem.Cli --version 0.22.0`。
将来の Release の場所は
https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.22.0 です。
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
- G703 — PR #1522; `intent-cli update` が executable path から channel を導出し、per-channel action を適用し、merge commit `abf6dc640eb3131564d146df9783d453d0e5c70a` (main で確認)。
- G704 — PR #1526; supervise install が setup を validate し、log を明示し、first cycle を prove し、merge commit `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` (main で確認)。
- G705 — PR #1529; feedback-channel guidance が render-only で GitHub issue を file せず、merge commit `0c49569129635be6a35a07a3e9cfdf3621b44c4c` (main で確認)。
- G706 — PR #1524; design-thread terminal rule が pane reading を liveness evidence に限定し、fallback observation route を戻し、merge commit `be29c896b01df6a48502748e155e07b076c563c6` (main で確認)。
- G707 — PR #1531; supervision finding が escalation 前に own cycle 内で corroborate され、merge commit `6163d9b3589d331c6a82bb72923a91a15aef029b` (main で確認)。
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
| `abf6dc640eb3131564d146df9783d453d0e5c70a` | G703、PR #1522 |
| `2160c1ddef9c2bf0a8268b8ef3258ba4f965f3fd` | G704、PR #1526 |
| `0c49569129635be6a35a07a3e9cfdf3621b44c4c` | G705、PR #1529 |
| `be29c896b01df6a48502748e155e07b076c563c6` | G706、PR #1524 |
| `6163d9b3589d331c6a82bb72923a91a15aef029b` | G707、PR #1531 |
| `55d54951b677e8aa6f2d2f0bd49d278ed4e63531` | G708、PR #1533 |

### Origins and minor rationale

external defect は隣接する measurement と operator work から区別できます。G695 は外部 defect
#1491、G706 は #1516、G707 は #1518、G708 は #1527 に続きます。measured な supervise stall
が G704 になりました。G696-G700 は own backlog の improvement です。guide primacy、npm
distribution、update channel、feedback guidance は operator decision を記録します。
minor version はこの十四件の user-visible な orchestration / distribution surface により妥当で、
preview boundary と 1.0 compatibility promise は明示したままです。

## Distribution boundary — v0.22.0 の npm skip

v0.22.0 では npm publication を skip します。npm organisation (organization) access と package-name reservation
は未完了の operator account action です。そのため G702 npm publish step は v0.22.0 では実行しません。
これは defect ではなく distribution gap です。この prepare-only PR は credentials を要求・処理せず、
package や registry state を変更しません。既存の npm entry-point guidance は記録したままにし、
account と reservation の前提が完了した後の operator action として publication を扱います。

## Release-readiness gate (G709) — リリース準備ゲート

- [ ] operator が別途 Release を承認するまで `eng/version.json` を stable `0.21.0`、next `0.22.0` に保つ。
- [ ] 上記 command で十四 unit と十五 first-parent commit を確認し、focused guard と full Release suite を実行する。
- [ ] CLI を build し、metadata-free な `intent-cli guide orchestrator-thread` の既存 route を実行してから、この readiness section に進む。installed guide が operator/agent の入口です。
- [ ] EN/JA notes と readiness が同じ unit、merge、npm-skip contract を持つことを確認する。
- [ ] この prepare-only slice では tag、GitHub Release、NuGet または npm publish を行わず、credentials を扱わない。

v0.22.0 の publish は operator が明示的に承認する将来の action です。v0.22.0 の publish では G702 npm publish step を実行せず、未完了の npm organization と package-name reservation は defect ではなく distribution gap として残ります。
