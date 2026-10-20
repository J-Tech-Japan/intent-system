# リリースノート — intent-cli 0.32.1-preview.1

> **準備中 — 未公開。** これは予定している `0.32.1-preview.1` のリリース候補に関する文書です。この準備 PR を統合しても、タグ、GitHub Release、NuGet package、npm package、実行ファイルは作成されません。

この preview.1 のリリースノートは、差し替え可能な開発中の草稿 `release-notes-v0.32.1.md` とは別の文書です。

正確な [`v0.32.1-preview.1` の GitHub リリースページ](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)を、公開後の取得先として記載しています。このリンクは将来の公開先であり、リリースが存在することや、レジストリ上のファイルが利用できることを示す証拠ではありません。現在の安定版は `v0.32.0` です。

## 対象としたソース範囲

基準となる commit は `6054eb9f46d8fe4e87e370a53bd527c116e3a05b` です。`v0.32.0` からこの commit までの `first-parent` の範囲には 24 件のコミットがあり、下記の 24 件の実装単位を含みます。G872 の文書準備は 25 件目の製品機能ではありません。この基準点におけるソース全体のテスト結果は、成功 10,701 件、失敗 0 件、`NotExecuted` の結果行 6 件でした。一方、TRX の raw `Counters.notExecuted` は 0 です。結果行と counter は区別し、この数値をインストール済み package やリリース候補の成果物の動作証明として扱いません。

この範囲では、solo-conductor の導入手順を分かりやすくし、証拠と claim/review の責務を確認できるようにし、ruling の記録と pinned-source の来歴確認を追加しました。ローカル TRX と選択制の worker completion 確認、安全上重要な design review 条件の明示選択、役割別 brief と明示的なローカル lesson も含まれます。

| 単位 | 対応 PR | 利用者向けの変更 |
| --- | --- | --- |
| G851 | [PR #1848](https://github.com/J-Tech-Japan/intent-system/pull/1848) | 複数席を設定する前に、単独席の運用担当者を `solo-conductor` に案内します。 |
| G852 | [PR #1850](https://github.com/J-Tech-Japan/intent-system/pull/1850) | 四つのスレッド構成と solo-conductor の選択肢を整理し、特定の通信手段を第一候補とはしません。 |
| G844 | [PR #1851](https://github.com/J-Tech-Japan/intent-system/pull/1851) | issue の公開拒否で実際に発生した原因と詳細を報告します。 |
| G843 | [PR #1852](https://github.com/J-Tech-Japan/intent-system/pull/1852) | 作成者が記述した受け入れ条件のラベルを証拠 gate で認識します。 |
| G814 | [PR #1854](https://github.com/J-Tech-Japan/intent-system/pull/1854) | 範囲を限定した安定した証拠をもとに model を解決します。 |
| G853 | [PR #1856](https://github.com/J-Tech-Japan/intent-system/pull/1856) | agent が実行する Orca mailbox の一連の操作手順を整えます。 |
| G854 | [PR #1860](https://github.com/J-Tech-Japan/intent-system/pull/1860) | guide reachability declaration の位置が不正なら拒否します。 |
| G855 | [PR #1863](https://github.com/J-Tech-Japan/intent-system/pull/1863) | 読み取り専用の unit status を追加します。 |
| G856 | [PR #1871](https://github.com/J-Tech-Japan/intent-system/pull/1871) | 投稿済みの exact-head solo-conductor approval を要求します。 |
| G857 | [PR #1874](https://github.com/J-Tech-Japan/intent-system/pull/1874) | claim の解放前に、公開済みの solo completion evidence を要求します。 |
| G858 | [PR #1877](https://github.com/J-Tech-Japan/intent-system/pull/1877) | solo-debt の引き継ぎ期間を追加します。 |
| G859 | [PR #1880](https://github.com/J-Tech-Japan/intent-system/pull/1880) | unit-status inventory で無関係な source report を分離します。 |
| G860 | [PR #1882](https://github.com/J-Tech-Japan/intent-system/pull/1882) | claim 解放時の missing-receipt に対する明示的な override を監査します。 |
| G861 | [PR #1884](https://github.com/J-Tech-Japan/intent-system/pull/1884) | declared-team の approval-posting 例外を監査します。 |
| G862 | [PR #1886](https://github.com/J-Tech-Japan/intent-system/pull/1886) | 変更できない scope 限定 ruling の記録と読み取りを追加します。 |
| G863 | [PR #1888](https://github.com/J-Tech-Japan/intent-system/pull/1888) | packet の公開前に、指定された ruling source を検証します。 |
| G864 | [PR #1890](https://github.com/J-Tech-Japan/intent-system/pull/1890) | unit status で、指定された ruling source を確認します。 |
| G865 | [PR #1892](https://github.com/J-Tech-Japan/intent-system/pull/1892) | packet の verification contract を検証します。 |
| G866 | [PR #1894](https://github.com/J-Tech-Japan/intent-system/pull/1894) | packet に結び付いたローカル TRX の証拠を追加します。 |
| G867 | [PR #1896](https://github.com/J-Tech-Japan/intent-system/pull/1896) | 読み取り専用の検証証拠一覧を追加します。 |
| G868 | [PR #1898](https://github.com/J-Tech-Japan/intent-system/pull/1898) | 選択制の worker completion に、明示した最新のローカル証拠を要求します。 |
| G869 | [PR #1900](https://github.com/J-Tech-Japan/intent-system/pull/1900) | 安全上重要な design review 条件を明示的に選択します。 |
| G870 | [PR #1902](https://github.com/J-Tech-Japan/intent-system/pull/1902) | 役割ごとに内容が一定する brief を出力します。 |
| G871 | [PR #1904](https://github.com/J-Tech-Japan/intent-system/pull/1904) | role brief に明示的なローカル lesson を追加します。 |

## 互換性と制約

- 安定版 `0.32.0` の既定動作は維持されます。検証、lesson、worker の証拠に関する動作は、文書に示す選択肢を利用者が有効にしない限り従来どおりです。worker proof gate には明示的な flag が必要で、team の設定から既定の方針を推測しません。
- ローカル report の来歴は呼び出し元から渡されるもので、認証されません。ソースのテストは、command が実行されたこと、reviewer に権限があったこと、受け入れ条件が意味的に真であることを証明しません。
- lesson の適用状況は助言にとどまり、`declared-not-verified` として示されます。有効な completion gate ではありません。
- raw TRX の証拠には test 名、output、machine path が含まれることがあります。lesson registry 全体の digest は、その registry の安定した識別情報を外部に示します。入力を commit または公開する前に、開示される情報を確認してください。
- brief の出力に失敗すると、不完全な出力や別プロセスが置いたファイルが残ることがあります。別途確認してください。renderer は元に戻す処理や公開を保証しません。
- G872 は文書のみを変更します。release workflow の実行、タグの作成、package の公開は行わず、候補 artifact が後の公開 asset と一致することも証明しません。`workflow_dispatch` の候補と公開時の GitHub Release event は別々に build されます。以前の `0.32.1-preview.483.1` artifact の確認は別の証拠であり、この `preview.1` 候補ではありません。

今後も残る広い作業には、共有状態における意味上の競合からの復旧（#1864）、公開 issue template のプライバシー表示（#1865）、ruling に基づく hosted deferral（#1866）、従来仕様と ruling に関する受け入れ条件の広い調整（#1867）、公開 PR の構成と registry の作成（#1868）があります。G856–G861 で追加された solo-conductor の承認、完了、debt、claim に関する手順があっても、#1857 が認証済みの履歴照合手段になるわけではありません。これらは今後の作業範囲であり、関連 issue がすべて完了したという主張でも、この preview の前提条件でもありません。

## インストールと公開

公開後に利用する version 固定の .NET、npm、ランタイム同梱 asset の選択手順は[インストールガイド](01-install.md)を参照してください。PATH 上での共存と、安定版 `0.32.0` に戻す手順を説明します。通常の安定版のインストール手順は変更しません。

この準備が merge された後、conductor は既存の非公開 `workflow_dispatch` 経路で merge 済みの候補を build し、対象 commit が既定ブランチの履歴に含まれること、3 種類の native binary、NuGet/npm の version と checksum、隔離した環境への install を確認します。さらに候補に有効期限が設定されていないことを確認します。候補 artifact の hash は公開 artifact の同一性を証明しません。GitHub Release の公開 event では asset を改めて build します。conductor は公開 asset、ソースと version の同一性、npm dist-tag、新しい環境への install を別途確認してから公開完了と判断します。この準備 PR は、それらの作業が行われたとは主張しません。
