# intent-system Worker Guide

このリポジトリで agent が作業するときは、Issue 本文を主入力としつつ、
このファイルを repo 全体の baseline guide として使う。

## Ask intent-cli first (guide-first)

intent / packet / issue / review / implementation-loop の作業を始める前に、まず
`intent-cli guide start` を実行し、そのフェーズ向けの `intent-cli guide …`
コマンドに従う。記憶やコピーした prompt から始めない。label / metadata の遷移は
intent-cli のコマンド経由で行い、手編集しない。詳細ルールは intent-cli の guidance
が source of truth（このファイルに長い spec を写経しない）。

- Implementation 側 agent は **GitHub-contract-only / metadata-free**：host の
  `.intent-cli` / queue-state / metadata branch / `intents/**` は読まない。Issue
  本文を standalone contract として扱う。
- Host / design 側 agent は metadata を扱ってよいが、手編集の前に intent-cli へ
  現在のコマンド・guidance を尋ねる。
- どの agent (Codex / Claude / Copilot / Cursor / OpenCode / Antigravity など) でも
  同じ `intent-cli guide start` を入口にする。

## Baseline

- 実装言語は `C# / .NET`
- `.NET SDK 10.0.100+` を baseline にする
- 実行導線は `dnx` または `dotnet tool exec` を優先する
- Node / TypeScript toolchain を勝手に導入しない

## Do Not Commit

- `node_modules/`
- package manager vendor directory
- `.takt/runs/`
- runtime trace
- temporary report
- generated cache

## Reading Order

1. GitHub Issue 本文
2. parent Intent repo の `Intent References`
3. parent Intent repo の `Rules And Specs`
4. この `AGENTS.md`

## Working Style

- Issue に書かれていない stack 変更はしない
- Out Of Scope を広げない
- generated artifact を solution と見なさない
- review で親 Intent と矛盾が見えたら、実装を押し切らず戻す
