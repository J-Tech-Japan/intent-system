# コーディングポリシー

速さより丁寧さ、実装の楽さよりコードの正確さを優先する。

## 原則

| 原則 | 基準 |
|------|------|
| Simple > Easy | 書きやすさより読みやすさを優先 |
| DRY | 本質的な重複は排除する |
| コメント | Why のみ。What/How は書かない |
| 関数サイズ | 1関数1責務。30行目安 |
| ファイルサイズ | 目安として300行。タスクに応じて柔軟に |
| ボーイスカウト | 触った箇所は少し改善して去る |
| Fail Fast | エラーは早期に検出。握りつぶさない |
| プロジェクトスクリプト優先 | ツール実行はプロジェクト定義のスクリプトを使う。直接実行は最後の手段 |

## フォールバック・デフォルト引数の禁止

値の流れを不明瞭にするコードは書かない。ロジックを追わないと値が分からないのは悪いコード。

### 禁止パターン

| パターン | 例 | 問題 |
|---------|-----|------|
| 必須データへのフォールバック | `user?.id ?? 'unknown'` | エラーになるべき状態で処理が進む |
| デフォルト引数の濫用 | `function f(x = 'default')` で全呼び出し元が省略 | 値がどこから来るか分からない |
| null合体で渡す口がない | `options?.cwd ?? process.cwd()` で上位から渡す経路なし | 常にフォールバックになる（意味がない） |
| try-catch で空値返却 | `catch { return ''; }` | エラーを握りつぶす |
| 不整合な値のサイレントスキップ | `if (a !== expected) return undefined` | 設定ミスが実行時に黙って無視される |

### 正しい実装

```typescript
// ❌ 禁止 - 必須データへのフォールバック
const userId = user?.id ?? 'unknown'
processUser(userId)  // 'unknown' で処理が進んでしまう

// ✅ 正しい - Fail Fast
if (!user?.id) {
  throw new Error('User ID is required')
}
processUser(user.id)

// ❌ 禁止 - デフォルト引数で全呼び出し元が省略
function loadConfig(path = './config.json') { ... }
// 全呼び出し元: loadConfig()  ← path を渡していない

// ✅ 正しい - 必須引数にして明示的に渡す
function loadConfig(path: string) { ... }
// 呼び出し元: loadConfig('./config.json')  ← 明示的

// ❌ 禁止 - null合体で渡す口がない
class Engine {
  constructor(config, options?) {
    this.cwd = options?.cwd ?? process.cwd()
    // 問題: options に cwd を渡す経路がない場合、常に process.cwd() になる
  }
}

// ✅ 正しい - 上位から渡せるようにする
function createEngine(config, cwd: string) {
  return new Engine(config, { cwd })
}
```

### 許容されるケース

- 外部入力（ユーザー入力、API応答）のバリデーション時のデフォルト値
- 設定ファイルのオプショナル値（明示的に省略可能と設計されている）
- 一部の呼び出し元のみがデフォルト引数を使用（全員が省略している場合は禁止）

### 判断基準

1. **必須データか？** → フォールバックせず、エラーにする
2. **全呼び出し元が省略しているか？** → デフォルト引数を削除し、必須にする
3. **上位から値を渡す経路があるか？** → なければ引数・フィールドを追加
4. **関連する値に不変条件があるか？** → ロード・セットアップ時にクロスバリデーションする

## 抽象化

### 条件分岐を追加する前に考える

- 同じ条件が他にもあるか → あればパターンで抽象化
- 今後も分岐が増えそうか → Strategy/Mapパターンを使う
- 型で分岐しているか → ポリモーフィズムで置換

```typescript
// ❌ 条件分岐を増やす
if (type === 'A') { ... }
else if (type === 'B') { ... }
else if (type === 'C') { ... }  // また増えた

// ✅ Mapで抽象化
const handlers = { A: handleA, B: handleB, C: handleC };
handlers[type]?.();
```

### 抽象度を揃える

1つの関数内では同じ粒度の処理を並べる。詳細な処理は別関数に切り出す。「何をするか」と「どうやるか」を混ぜない。

```typescript
// ❌ 抽象度が混在
function processOrder(order) {
  validateOrder(order);           // 高レベル
  const conn = pool.getConnection(); // 低レベル詳細
  conn.query('INSERT...');        // 低レベル詳細
}

// ✅ 抽象度を揃える
function processOrder(order) {
  validateOrder(order);
  saveOrder(order);  // 詳細は隠蔽
}
```

オーケストレーション関数（Step 1 → Step 2 → Step 3 と処理を並べる関数）では特に注意する。あるStepの内部に条件分岐が膨らんでいたら、そのStepを関数に抽出する。判定基準は分岐の数ではなく、**その分岐がその関数の抽象レベルに合っているか**。

```typescript
// ❌ オーケストレーション関数に詳細な分岐が露出
async function executePipeline(options) {
  const task = resolveTask(options);      // Step 1: 高レベル ✅

  // Step 2: 低レベル詳細が露出 ❌
  let execCwd = cwd;
  if (options.createWorktree) {
    const result = await confirmAndCreateWorktree(cwd, task, true);
    execCwd = result.execCwd;
    branch = result.branch;
  } else if (!options.skipGit) {
    baseBranch = getCurrentBranch(cwd);
    branch = generateBranchName(config, options.issueNumber);
    createBranch(cwd, branch);
  }

  await executeTask({ cwd: execCwd, ... }); // Step 3: 高レベル ✅
}

// ✅ 詳細を関数に抽出し、抽象度を揃える
async function executePipeline(options) {
  const task = resolveTask(options);
  const ctx = await resolveExecutionContext(options);
  await executeTask({ cwd: ctx.execCwd, ... });
}
```

### 言語・フレームワークの作法に従う

- Pythonなら Pythonic に、KotlinならKotlinらしく
- フレームワークの推奨パターンを使う
- 独自の書き方より標準的な書き方を選ぶ
- 不明なときはリサーチする。推測で実装しない

### インターフェース設計

インターフェースは利用側の都合で設計する。実装側の内部構造を露出しない。

| 原則 | 基準 |
|------|------|
| 利用者視点 | 呼び出し側が必要としないものを押し付けない |
| 構成と実行の分離 | 「何を使うか」はセットアップ時に決定し、実行APIはシンプルに保つ |
| メソッド増殖の禁止 | 同じことをする複数メソッドは構成の違いで吸収する |

```typescript
// ❌ メソッド増殖 — 構成の違いを呼び出し側に押し付けている
interface NotificationService {
  sendEmail(to, subject, body)
  sendSMS(to, message)
  sendPush(to, title, body)
  sendSlack(channel, message)
}

// ✅ 構成と実行の分離
interface NotificationService {
  setup(config: ChannelConfig): Channel
}
interface Channel {
  send(message: Message): Promise<Result>
}
```

### 抽象化の漏れ

特定実装が汎用層に現れたら抽象化が漏れている。汎用層はインターフェースだけを知り、分岐は実装側で吸収する。

```typescript
// ❌ 汎用層に特定実装のインポートと分岐
import { uploadToS3 } from '../aws/s3.js'
if (config.storage === 's3') {
  return uploadToS3(config.bucket, file, options)
}

// ✅ 汎用層はインターフェースのみ。非対応は生成時にエラー
const storage = createStorage(config)
return storage.upload(file, options)
```

## 構造

### 分割の基準

- 独自のstateを持つ → 分離
- 50行超のUI/ロジック → 分離
- 複数の責務がある → 分離

### 機能追加時の到達経路

新しい機能や画面を追加したら、実装と同じ変更セットで利用者が到達する経路も更新する。フレームワーク固有の配線方法は各ドメイン知識に従う。

| 基準 | 判定 |
|------|------|
| 新機能の実装だけ追加し、呼び出し側・導線・到達経路の更新を忘れる | REJECT |
| 利用者がどこから到達するか未定義のまま公開機能を追加する | REJECT |
| 実装追加と同じ変更セットで導線と到達経路を更新する | OK |
| 一時導線を追加した場合、その用途と除去条件を記録する | OK |

### 依存の方向

- 上位層 → 下位層（逆方向禁止）
- データ取得はルート（View/Controller）で行い、子に渡す
- 子は親のことを知らない

### 実行条件と依存条件の一致

依存やトリガーは、実際にその処理を再実行したい条件と一致させる。静的ルールや実装都合のためだけに依存を増やし、意図しない再実行を起こさない。

| 基準 | 判定 |
|------|------|
| lint や実装都合だけで依存やトリガーを増やし、再実行ループを生む | REJECT |
| 無関係な state 変化や callback 再生成で初期処理が再実行される | REJECT |
| 再実行条件が URL・フィルタ・明示的更新操作などの仕様に対応している | OK |
| 初期化と再取得のトリガーを分けて設計している | OK |

## 状態管理

- 状態は使う場所に閉じ込める
- 子は状態を直接変更しない（イベントを親に通知）
- 状態の流れは単方向

## エラーハンドリング

エラーは一元管理する。各所でtry-catchしない。

```typescript
// ❌ 各所でtry-catch
async function createUser(data) {
  try {
    const user = await userService.create(data)
    return user
  } catch (e) {
    console.error(e)
    throw new Error('ユーザー作成に失敗しました')
  }
}

// ✅ 上位層で一元処理
// Controller/Handler層でまとめてキャッチ
// または @ControllerAdvice / ErrorBoundary で処理
async function createUser(data) {
  return await userService.create(data)  // 例外はそのまま上に投げる
}
```

### エラー処理の配置

| 層 | 責務 |
|----|------|
| ドメイン/サービス層 | ビジネスルール違反時に例外をスロー |
| Controller/Handler層 | 例外をキャッチしてレスポンスに変換 |
| グローバルハンドラ | 共通例外（NotFound, 認証エラー等）を処理 |

## 変換処理の配置

変換メソッドはDTO側に持たせる。

```typescript
// ✅ Request/Response DTOに変換メソッド
interface CreateUserRequest {
  name: string
  email: string
}

function toUseCaseInput(req: CreateUserRequest): CreateUserInput {
  return { name: req.name, email: req.email }
}

// Controller
const input = toUseCaseInput(request)
const output = await useCase.execute(input)
return UserResponse.from(output)
```

変換の方向:
```
Request → toInput() → UseCase/Service → Output → Response.from()
```

## 共通化の判断

基本的に重複は排除する。本質的に同じロジックであり、まとめるべきと判断したら DRY にする。回数で機械的に判断しない。

### 共通化すべきもの

- 本質的に同じロジックの重複
- 同じスタイル/UIパターン
- 同じバリデーションロジック
- 同じフォーマット処理

### 共通化すべきでないもの

- ドメインが異なる重複（例: 顧客用バリデーションと管理者用バリデーションは別物）
- 表面的に似ているが変更理由が異なるコード
- 「将来使うかも」という予測に基づくもの

```typescript
// ❌ 過度な汎用化
function formatValue(value, type, options) {
  if (type === 'currency') { ... }
  else if (type === 'date') { ... }
  else if (type === 'percentage') { ... }
}

// ✅ 用途別に関数を分ける
function formatCurrency(amount: number): string { ... }
function formatDate(date: Date): string { ... }
function formatPercentage(value: number): string { ... }
```

## 同一実装の別名関数（DRY 違反）

AIは同じ処理を異なる関数名で複数定義しがちである。

| パターン | 例 | 判定 |
|---------|-----|------|
| 同一実装の別名関数 | `copyFacets()` と `placeFacetFiles()` が同じ処理 | REJECT |
| 引数シグネチャが同一で本体も同一 | 2つの関数が同じパラメータを受け取り同じ処理を行う | REJECT |

```typescript
// REJECT - 同じ実装が別名で存在
function copyFiles(src: string, dest: string): void {
  for (const f of readdirSync(src)) {
    copyFileSync(join(src, f), join(dest, f));
  }
}
function placeFiles(src: string, dest: string): void {
  for (const f of readdirSync(src)) {
    copyFileSync(join(src, f), join(dest, f));
  }
}

// OK - 1つの関数にまとめる
function copyFiles(src: string, dest: string): void {
  for (const f of readdirSync(src)) {
    copyFileSync(join(src, f), join(dest, f));
  }
}
```

検証アプローチ:
1. 新規追加された関数の本体が、既存関数と同一または酷似していないか確認
2. 同じファイル内の関数同士、および同じモジュール内の関数同士を比較
3. 重複があれば1つにまとめ、呼び出し元を統一

## Stateful Regex の危険なパターン

`/g` フラグ付き正規表現はステートフル（`lastIndex` を保持する）。モジュールスコープに定義して `test()` と `replace()` を混用すると予期しない結果になる。

| パターン | 例 | 判定 |
|---------|-----|------|
| モジュールスコープの `/g` 正規表現を `test()` で使用 | `const RE = /x/g; if (RE.test(s)) ...` | REJECT |
| `/g` 正規表現を `test()` と `replace()` で使い回し | `RE.test(s)` の後に `s.replace(RE, ...)` | REJECT |

```typescript
// REJECT - モジュールスコープの /g 正規表現を test() で使用
const PATTERN = /\{\{facet:(\w+)\}\}/g;
function hasFacetRef(text: string): boolean {
  return PATTERN.test(text);  // lastIndex が進み、次回の呼び出しで結果が変わる
}

// OK - test() には /g を付けない、または関数内で new RegExp
const PATTERN_CHECK = /\{\{facet:(\w+)\}\}/;  // /g なし
const PATTERN_REPLACE = /\{\{facet:(\w+)\}\}/g;  // replace 用は /g
function hasFacetRef(text: string): boolean {
  return PATTERN_CHECK.test(text);
}
function replaceFacetRefs(text: string): string {
  return text.replace(PATTERN_REPLACE, ...);
}
```

検証アプローチ:
1. モジュールスコープの正規表現に `/g` フラグがあるか確認
2. `/g` 付き正規表現が `test()` で使われていないか確認
3. 同一の正規表現が `test()` と `replace()` の両方で使われていないか確認

## 禁止事項

- **フォールバックは原則禁止** - `?? 'unknown'`、`|| 'default'`、`try-catch` で握りつぶすフォールバックを書かない。エラーは上位に伝播させる。どうしても必要な場合はコメントで理由を明記する
- **説明コメント** - コードで意図を表現する。What/How のコメントは書かない
- **未使用コード** - 「念のため」のコードは書かない
- **any型** - 型安全を破壊しない
- **オブジェクト/配列の直接変更** - スプレッド演算子で新規作成
- **console.log** - 本番コードに残さない
- **機密情報のハードコーディング**
- **契約文字列のハードコード散在** - ファイル名・設定キー名は定数で1箇所管理。リテラルの散在は禁止
- **各所でのtry-catch** - エラーは上位層で一元処理
- **後方互換・Legacy対応の自発的追加** - 明示的な指示がない限り不要
- **内部実装のパブリック API エクスポート** - 公開するのはドメイン操作の関数・型のみ。インフラ層の関数や内部クラスをエクスポートしない
- **リファクタリング後の旧コード残存** - 置き換えたコード・エクスポートは削除する。明示的に残すよう指示されない限り残さない
- **安全機構を迂回するワークアラウンド** - 根本修正が正しいなら追加の迂回は不要
- **プロジェクトスクリプトを迂回するツール直接実行** - `npx tool` 等の直接実行は lockfile を迂回しバージョン不一致を起こす。プロジェクトが定義したスクリプト（npm scripts, Makefile 等）を探して使う。見つからない場合のみ直接実行を検討する
- **配線忘れ** - 新しいパラメータやフィールドを追加したら、grep で呼び出しチェーン全体を確認する。呼び出し元が値を渡していないと `options.xxx ?? fallback` で常にフォールバックが使われる
- **冗長な条件分岐** - if/else で同一関数を呼び出し引数の差異のみの場合、三項演算子やスプレッド構文で統一する
- **コピペパターン** - 新しいコードを書く前に同種の既存実装を grep で確認し、既存パターンに合わせる。独自の書き方を持ち込まない


---

# テストポリシー

全ての振る舞いの変更には対応するテストが必要であり、全てのバグ修正にはリグレッションテストが必要。

## 原則

| 原則 | 基準 |
|------|------|
| Given-When-Then | テストは3段階で構造化する |
| 1テスト1概念 | 複数の関心事を1テストに混ぜない |
| 振る舞いを検証 | 実装の詳細ではなく振る舞いをテストする |
| 独立性 | 他のテストや実行順序に依存しない |
| 再現性 | 時間やランダム性に依存せず、毎回同じ結果 |

## カバレッジ基準

| 対象 | 基準 |
|------|------|
| 新しい振る舞い | テスト必須。テストがなければ REJECT |
| バグ修正 | リグレッションテスト必須。テストがなければ REJECT |
| 振る舞いの変更 | テストの更新必須。更新がなければ REJECT |
| ビルド（型チェック） | ビルド成功必須。失敗は REJECT |
| エッジケース・境界値 | テスト推奨（Warning） |

## テスト優先度

| 優先度 | 対象 |
|--------|------|
| 高 | ビジネスロジック、状態遷移 |
| 中 | エッジケース、エラーハンドリング |
| 低 | 単純なCRUD |

**注意:** デザイン参照が指定されている場合、UIの見た目の検証は中優先度に格上げする。デザイン忠実再現ポリシーを参照。

## テスト構造: Given-When-Then

```typescript
test('ユーザーが存在しない場合、NotFoundエラーを返す', async () => {
  // Given: 存在しないユーザーID
  const nonExistentId = 'non-existent-id'

  // When: ユーザー取得を試みる
  const result = await getUser(nonExistentId)

  // Then: NotFoundエラーが返る
  expect(result.error).toBe('NOT_FOUND')
})
```

## テスト品質

| 観点 | 良い | 悪い |
|------|------|------|
| 独立性 | 他のテストに依存しない | 実行順序に依存 |
| 型安全 | コードはビルド（型チェック）が通ること |
| 再現性 | 毎回同じ結果 | 時間やランダム性に依存 |
| 明確性 | 失敗時に原因が分かる | 失敗しても原因不明 |
| 焦点 | 1テスト1概念 | 複数の関心事が混在 |

### 命名

テスト名は期待される振る舞いを記述する。`should {期待する振る舞い} when {条件}` パターンを使う。

### 構造

- Arrange-Act-Assert パターン（Given-When-Then と同義）
- マジックナンバー・マジックストリングを避ける

## 再取得ループのリグレッション

画面の初期取得がある場合、無関係な再レンダ、loading 切替、Context callback の参照変化で API が再実行されないことをテストで担保する。

| 基準 | 判定 |
|------|------|
| 初期取得バグ修正に対し、重複 API 呼び出しの回帰テストがない | REJECT |
| 1回呼ばれたことだけを確認し、再レンダ後の安定性を見ていない | 警告 |
| rerender や state 更新後も呼び出し回数が増えないことを検証している | OK |

## 到達経路のリグレッション

利用者向け機能や画面を追加・変更した場合、利用者がその機能へ到達できることをテストまたは同等の検証で担保する。

| 基準 | 判定 |
|------|------|
| 新規の画面・機能を追加したのに、到達経路や起動条件の検証がない | REJECT |
| 画面ファイル単体の描画だけを見て、入口からの到達確認をしていない | 警告 |
| route、メニュー、ボタン、リンク、外部呼び出しなど実際の入口から対象機能へ到達できることを確認している | OK |

## UIライブラリ統合のリグレッション

DataGrid、日付ピッカー、仮想リスト、チャートなど、外部 UI ライブラリの主要コンポーネントを導入・変更した場合は、実コンポーネントをマウントするテストでクラッシュしないことを担保する。

| 基準 | 判定 |
|------|------|
| 外部 UI ライブラリの主要コンポーネントを追加・変更したのに、実マウントの回帰テストがない | REJECT |
| ライブラリの props 整合性を、浅いモックや存在確認だけで済ませている | 警告 |
| route から対象画面を描画し、主要 UI が例外なくマウントされることを確認している | OK |
| 主要 UI コンポーネント単体でも、代表的な props で実 render している | OK |

## テスト戦略

- ロジックにはユニットテスト、境界にはインテグレーションテストを優先
- ユニットテストでカバーできるものにE2Eテストを使いすぎない
- 新しいロジックにE2Eテストしかない場合、ユニットテストの追加を提案する

### インテグレーションテストが必須な場面

ユニットテストだけでは検証できないデータフローの結合を検証する。

| 条件 | 判定 |
|------|------|
| 3つ以上のモジュールを横断するデータフロー | インテグレーションテスト必須 |
| 新しいステータス／状態が既存のワークフローに合流する | 遷移フロー全体のインテグレーションテスト必須 |
| 新しいオプションが呼び出しチェーンを通じて末端まで伝搬する | チェーン全体の結合テスト必須 |
| 各モジュールのユニットテストが全てパスしている | ユニットテストのみで十分（上記に該当しない場合） |

## ユニットテスト基準

| 基準 | 判定 |
|------|------|
| テスト対象の内部実装をモックする（振る舞いではなく実装を検証） | REJECT |
| テスト間でフィクスチャを共有して変更する | REJECT。テスト独立性の喪失 |
| モックの戻り値が実際の型と乖離している | 警告。型安全なモックを使う |
| 正常系のみテストして境界値がない | 警告 |

## E2Eテスト基準

| 基準 | 判定 |
|------|------|
| 外部API呼び出しをモックせず本番APIを叩く | REJECT。テストの再現性が失われる |
| テスト対象のコア処理をモックする | REJECT。E2Eの意味がなくなる |
| 固定 sleep でタイミングを合わせる | REJECT。状態ベースの待機を使う |
| テスト間で共有状態を持つ | 警告。テストの独立性が損なわれる |
| 正常フローだけテストして異常フローがない | 警告 |
| ユニットテストでカバーできるロジック検証をE2Eで書く | 警告 |

## テスト環境の分離

テストインフラの設定はテストシナリオのパラメータに連動させる。ハードコードされた前提は別シナリオで壊れる。

| 原則 | 基準 |
|------|------|
| パラメータ連動 | テストの入力パラメータに応じてフィクスチャ・設定を生成する |
| 暗黙の前提排除 | 特定の環境（ユーザーの個人設定等）に依存しない |
| 整合性 | テスト設定内の関連する値は互いに矛盾しない |
| プロセス終了保証 | テストランナーにタイムアウトと強制終了を設定し、プロセスリークを防ぐ |

```typescript
// ❌ ハードコードされた前提 — 別のバックエンドでテストすると不整合になる
writeConfig({ backend: 'postgres', connectionPool: 10 })

// ✅ パラメータに連動
const backend = process.env.TEST_BACKEND ?? 'postgres'
writeConfig({ backend, connectionPool: backend === 'sqlite' ? 1 : 10 })
```
