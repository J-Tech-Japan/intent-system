# タスク計画

## 元の要求

Issue #1: [A1] Projection Schema — `execution` の sub-slice を、`implementation_issue_packet` と `review_context_packet` に一意に写せる projection schema を実装可能な形で固定する。

## 分析結果

### 目的

`intent-system` リポジトリ（現在 README.md のみのグリーンフィールド）に、execution の sub-slice データから2種類の projected packet を生成するための型定義・バリデーションスキーマ・deterministic 変換ロジックを実装する。

### 参照資料の調査結果

5つの参照資料を GitHub API 経由で取得・確認済み:

- **`01-projection-schema.md` (spec)**: 入力フィールド12項目、出力パケット2種類（implementation: 17フィールド、review: 7フィールド）、変換ルール5項目、scope補完ルールを定義
- **`issue-projection-format.md` (rule)**: projection の目的（実装者・reviewer・agent が同じ形で扱える）、標準 Markdown/YAML 形式、execution → projected field のマッピング表を定義
- **`issue-template-and-review-context.md` (rule)**: 固定見出し構造、各セクションの意味、review 時に parent Intent tree へ戻れる参照束の要件を定義
- **`04-mvp-sub-slices.md` (execution)**: A1 の定義「execution row を packet field へ写す projection schema を固定する」、target_repo: `submodules/intent-system`
- **`03-bootstrap-manual-operation.md` (rule)**: bootstrap phase では手動代行可、`.intent-cli/` artifact をこの repo に先に作る運用

現在の実装との差異: リポジトリにコードが存在しないため、全て新規実装。

### スコープ

| 要件 | 変更要/不要 | 根拠 |
|------|-----------|------|
| projection input field 定義 | 変更要 | 新規実装（リポジトリにコードなし） |
| implementation_issue_packet schema | 変更要 | 新規実装 |
| review_context_packet schema | 変更要 | 新規実装 |
| deterministic 変換ルール | 変更要 | 新規実装 |
| issue_kind enum | 変更要 | 新規実装 |
| プロジェクト設定（package.json 等） | 変更要 | 新規実装 |

### 検討したアプローチ

| アプローチ | 採否 | 理由 |
|-----------|------|------|
| TypeScript + Zod | **採用** | スキーマ定義タスクに最適。ランタイムバリデーション + 型推論を両立。下流の A2 (Packet Generator) と自然に統合可能 |
| JSON Schema のみ | 不採用 | バリデーションはできるが変換ロジックを書けない。A2 で結局 TS コードが必要になる |
| YAML/Markdown 仕様書のみ | 不採用 | 受け入れ基準「field mapping が一意に決まる」を満たすにはコードでの固定が必要 |

### 実装アプローチ

**技術スタック**: TypeScript (ESM) + Zod ^3 + Vitest ^3

**ファイル構成** (10ソースファイル + 3設定ファイル):

```
intent-system/
├── src/
│   ├── schema/
│   │   ├── issue-kind.ts                    (~15行) IssueKind 値定義
│   │   ├── projection-input.ts              (~65行) 入力 Zod スキーマ + 型
│   │   ├── implementation-issue-packet.ts   (~50行) 出力スキーマ + 型
│   │   ├── review-context-packet.ts         (~40行) 出力スキーマ + 型
│   │   └── index.ts                         (~10行) re-exports
│   ├── mapping/
│   │   ├── field-resolvers.ts               (~55行) 共通フィールド解決ロジック
│   │   ├── project-to-implementation-packet.ts (~60行) 入力→issue packet 変換
│   │   ├── project-to-review-context-packet.ts (~45行) 入力→review context 変換
│   │   └── index.ts                         (~10行) re-exports
│   └── index.ts                             (~10行) public API
├── package.json
├── tsconfig.json
└── vitest.config.ts
```

**依存の方向**: `index.ts` → `mapping/` → `schema/`（一方向のみ、循環依存なし）

**変換ルール（spec より。field-resolvers.ts で実装）:**

| 出力フィールド | 解決ロジック |
|---------------|------------|
| `source_execution_unit` | 入力をそのまま透過 |
| `dependencies` | `depends_on_subslices` を優先、なければ `depends_on`、どちらもなければ `[]` |
| `intent_references` | `related_intents` を優先、`source_concepts` を補助追加 |
| `rules_and_specs` | 明示指定があればそれを使用。なければ `source_concepts` から rule/spec/design パスを抽出 |
| `acceptance_criteria` | `success_signal` を配列の初期値として展開 |
| `in_scope` | 明示指定があればそれを使用。なければ `target_part` を含む最小境界を生成 |
| `out_of_scope` | 明示指定があればそれを使用。なければ空配列 |
| `issue_title` | 明示指定があればそれを使用。なければ `source_execution_unit` + `goal` から生成 |
| `issue_kind` | 明示指定があればそれを使用。デフォルト `"feature"` |

**パブリック API（index.ts からエクスポートするもの）:**
- 型: `ProjectionInput`, `ImplementationIssuePacket`, `ReviewContextPacket`, `IssueKind`
- Zod スキーマ: `ProjectionInputSchema`, `ImplementationIssuePacketSchema`, `ReviewContextPacketSchema`
- 関数: `projectToImplementationPacket()`, `projectToReviewContextPacket()`

## 実装ガイドライン

- **純粋関数**: mapping 関数は副作用なし。同一入力に対して常に同一出力（deterministic）
- **Zod パース**: 入力は `ProjectionInputSchema.parse()` でバリデーション。出力も対応スキーマでバリデーション。不正データは Zod エラーとして伝搬
- **issue_kind の値**: `feature | bugfix | boundary-fix | verification | refactor | clarification-followup`（spec の "issue_kind の推奨値" セクション準拠）
- **review packet の parent_intent_root**: 入力で `parent_intent_root` が未指定の場合は空文字列を設定する（受け入れ基準 #3「review packet から parent Intent root に戻れる」を満たすために必須フィールドとしてスキーマに含める）
- **参照すべきパターン**: グリーンフィールドのため既存パターンなし。spec 内の "標準 YAML projection 例"（A2 Engine Tick Boundary）をテストデータの参考にすること
- **テスト**: 各 field-resolver の単体テスト + 2つの変換関数の統合テスト。テストデータは spec の YAML 例と A1 sub-slice 相当のデータを使用

## スコープ外

| 項目 | 除外理由 |
|------|---------|
| Markdown / YAML の actual rendering 実装 | タスク指示書の Out of Scope に明記。A2 Packet Generator の担当 |
| queue-state 更新ロジック | タスク指示書の Out of Scope に明記。B1/B2 の担当 |
| workflow engine や takt adapter | タスク指示書の Out of Scope に明記。C1/C2 の担当 |

## 確認事項

なし。全ての仕様は参照資料から確定済み。