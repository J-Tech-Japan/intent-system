# intent-system Claude Guide

このリポジトリで Claude Code が作業するときは、Issue 本文を主入力としつつ、
repo 全体の baseline は [AGENTS.md](./AGENTS.md) に従う。

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
