# フラットからツリーへの再構成 (G405)

> **まず intent-cli に聞く:** `intent-cli guide intent-work setup --kind restructure --domain <name> --target-repo <owner/repo>`
> ← [intent の整理](03-intents.md)

このページでは、既存のフラット intent ドメインを tree-v1 レイアウト (G403/G404) に再構成するための、デザイン AI 支援ワークフローを説明します。

## いつ使うか

次のような場合に再構成ワークフローを使用してください：
- 単一のファイルが大きすぎて検索・レビュー・相互リンクが困難になった場合
- 既存のコンテンツを破棄せずに tree-v1 構成を採用したい場合

既存のフラットドメインは引き続き有効です。再構成は段階的かつ任意です。

## 役割と責任

| アクター | 責任 |
|---|---|
| **intent-cli** | 決定論的な分析、移動提案、参照マップ、安全チェック、lint、検証。セマンティックなグループ化判断は行わない。 |
| **ホスト/デザイン AI + オペレーター** | セマンティックなグループ化判断、コンテンツの移動・書き換え、リンクの更新、レビュー用コミット。 |

## ステップバイステップワークフロー

### 1. フラットドメインの分析（読み取り専用）

```bash
intent-cli intent analyze-tree --domain <name> --format markdown
```

出力内容：
- フラットファイルとサイズ一覧
- 各 H2/H3 見出しに対するカテゴリ先の提案（キーワードヒューリスティック — 確認して調整してください）
- 検出された参照：Markdown リンク、見出しアンカー、実行ユニット ID（例: G405）、パケットパス、GitHub issue/PR URL
- 移行参照マップ：旧パス + 見出し → 提案新パス

### 2. 再構成前の lint

```bash
intent-cli intent lint-layout --domain <name> --format markdown
```

`LARGE-FLAT-FILE`、`MISSING-MANIFEST`、`BROKEN-RELATIVE-LINK` の警告を確認してください。

### 3. ツリーの初期化（未初期化の場合）

```bash
intent-cli intent init-tree --domain <name> --target-repo <owner/repo> --write
```

必要に応じて機能フォルダを追加：

```bash
intent-cli intent add-feature --domain <name> --name <feature> --write
```

### 4. ホスト/デザイン AI がオペレーターと協働して再構成

分析計画を入力として、デザイン AI（オペレーターの監督のもと）：
1. 最終カテゴリグループを決定（intent-cli の提案は出発点）
2. フラットファイルからコンテンツブロックを提案先に移動またはコピー
3. Markdown リンクと見出しアンカーを更新
4. 元の参照（パケットパス、GitHub issue/PR URL、実行ユニット ID）を維持

### 5. オプション：バックアップ + スタブファイルの生成

```bash
intent-cli intent analyze-tree --domain <name> --write
```

作成されるもの：
- フラットファイルの `.restructure-backup/` コピー（非破壊的）
- プレースホルダー付きのスタブファイル

### 6. 再構成後の lint

```bash
intent-cli intent lint-layout --domain <name> --format markdown
```

確認事項：
- `BROKEN-RELATIVE-LINK` の数が増えていない
- `MISSING-FEATURES-INDEX` と `MISSING-FEATURE-OVERVIEW` が解消されている
- 移動済みファイルに `LARGE-FLAT-FILE` 警告が残っていない

### 7. レビュー用コミット

再構成を通常のレビュー可能な変更としてコミットします：
- 再構成したフラットファイルを記載
- 作成したカテゴリと機能を列挙
- 移行参照マップを含める
- 元の参照（GitHub URL、実行ユニット ID）が保持されていることを記載

**レビューチェックリスト：**
- [ ] 全ての元の見出しが宛先ファイルに追跡可能
- [ ] Markdown リンクが更新され正しく解決される
- [ ] 実行ユニット ID と GitHub URL が存在する
- [ ] `manifest.yaml` と `features/index.md` が更新されている
- [ ] 元のフラットファイルがバックアップされているか、明示的な確認の上で削除されている

## Lint コードリファレンス

| コード | 説明 | 修正方法 |
|---|---|---|
| `MISSING-DOMAIN` | ドメインディレクトリが存在しない | `intent init-tree --write` を実行 |
| `MISSING-MANIFEST` | `manifest.yaml` がない（フラットドメイン） | `intent init-tree --write` を実行 |
| `MISSING-CATEGORY-FOLDER` | マニフェストに記載のカテゴリフォルダが存在しない | フォルダを作成または `init-tree --write` を再実行 |
| `LARGE-FLAT-FILE` | フラットファイルがサイズしきい値を超えている | `analyze-tree` で再構成計画を立てる |
| `BROKEN-RELATIVE-LINK` | 相対リンクが解決しない | リンクを更新するか欠落ファイルを作成 |
| `MISSING-FEATURES-INDEX` | `features/` があるが `features/index.md` がない | `add-feature --write` を実行 |
| `MISSING-FEATURE-OVERVIEW` | 機能フォルダに `overview.md` がない | ゴールと受け入れ基準を含む `overview.md` を追加 |

## 関連ドキュメント

- [intent の整理・保守](03-intents.md)
- [Intent ナレッジツリーレイアウト (tree-v1)](03a-intent-tree-layout.md)
- [packet 作成と issue 公開](04-packets-issues.md)
