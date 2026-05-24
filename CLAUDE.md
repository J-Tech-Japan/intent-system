# intent-system Claude Guide

このリポジトリで Claude Code が作業するときは、Issue 本文を主入力としつつ、
repo 全体の baseline は [AGENTS.md](./AGENTS.md) に従う。

## Ask intent-cli first (guide-first)

このリポジトリの workflow は `intent-cli` が権威。intent / packet / issue / review /
implementation-loop を始める前に `intent-cli guide start` を実行し、フェーズ向けの
`intent-cli guide …` コマンドに従う。metadata / label の挙動を推測したり、長いルールを
ここに写経しない（intent-cli の guidance が source of truth）。implementation 作業は
**GitHub-contract-only**（issue/PR + repo code のみ、host の `.intent-cli` metadata は
読まない）。詳しい役割分担は `intent-cli guide start` を参照。

## Reading Order

1. GitHub Issue 本文
2. `AGENTS.md`
3. parent Intent repo の `Intent References`
4. parent Intent repo の `Rules And Specs`

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

## Working Style

- Issue に書かれていない stack 変更はしない
- Out Of Scope を広げない
- generated artifact を solution と見なさない
- review で親 Intent と矛盾が見えたら、実装を押し切らず戻す
