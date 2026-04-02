# intent-system Worker Guide

このリポジトリで agent が作業するときは、Issue 本文を主入力としつつ、
このファイルを repo 全体の baseline guide として使う。

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
