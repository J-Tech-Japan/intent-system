# リリースノート — intent-cli 0.32.1

> **準備済み — 未公開。** これは安定版 0.32.1 の公開準備用リリースノートです。
> この準備 PR を統合しても、tag、GitHub Release、NuGet package、npm package、
> native archive は作成されません。

正確な [`v0.32.1` GitHub release page](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1)
は、別途公開した後の取得先です。実際に公開されるまでは、公開中の安定版は
`v0.32.0` のままです。既に公開されている
[`v0.32.1-preview.1` release](https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1)
とそのリリースノートは、別の記録として変更しません。このファイルは以前の
`release-notes-v0.32.1.md` 開発用草稿を置き換えますが、preview の記録は置き換えたり
安定版として扱ったりしません。

## 測定した製品範囲

この準備は product main の commit
`dba5724bc00f2207f601b5643cfb87595dc27795` を基準にします。下のリリース一覧には
G871 までに測定した製品単位が 24 件あります。これは機能単位の一覧であり、
v0.32.0 からこの基準 commit までの commit 数が 24 件だけだという意味ではありません。
G872 / PR #1906 は文書準備であり、25 件目の製品単位ではありません。G873 も
リリース準備であり、機能単位には数えません。

レビュー済み G872 source head
`8438aaaadab90215c1950869227ba007038cb8cd` の full11 では、成功 10,708 件、失敗 0 件、
`NotExecuted` の test-result row 6 件でした。raw TRX counter は別に、total 10,714、
executed 10,708、passed 10,708、failed 0、`notExecuted` 0 と記録しました。
これはレビュー済み source head のテスト証拠です。安定版 package や native artifact の証明ではありません。
公開済み `0.32.1-preview.1` の smoke も preview の証拠であり、
安定版 artifact の証明ではありません。安定版候補と公開 asset は別々に build し、確認する必要があります。

この範囲では solo-conductor の導入を分かりやすくし、証拠、approval、claim 解放の責務を
確認できるようにしました。durable ruling と pinned-source の来歴確認、ローカル TRX と
選択制の worker completion 確認、安全上重要な design-review 条件の明示的選択、role brief
とローカル lesson も追加しています。

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

## 互換性と運用上の影響

- 破壊的な schema 変更や data migration は確認されていません。新しい verification 動作は
  引き続き opt-in です。lesson の適用状況は助言にとどまり `declared-not-verified` と示されます。
  呼び出し元から渡された来歴情報は認証されません。
- solo-conductor の責務確認を厳格化し、文書に記載された作業を実際に確かめます。通常は
  対象 commit に対する review を投稿し、該当する receipt をそろえて、条件を満たす closeout 経路を使ってください。
  G856 は対象 commit に対して投稿された独立 review を要求します。G857 は対象 builder が claim を
  解放する際、canonical な closeout と該当する役割別 receipt を要求します。ナレッジの書き戻しが必須と宣言された場合は、
  architect と orchestrator それぞれに帰属する knowledge-writeback receipt が
  必要です。guide route が宣言された場合は、architect に帰属する guide reachability receipt が必要です。
- G860 と G861 の例外は範囲を限定しています。`claim release --override-loop-evidence` は
  明示的な空でない理由が必要で、closeout 後に指定された receipt が不足する場合だけに適用します。
  `automation pr-transition --override-loop-evidence --override-id <uuid>` は flag、理由、override id
  が必要で、local gate と他の approval check を通過した後、canonical posting だけが不足する場合に
  限ります。理由だけでは責務を免除しません。どちらも一般的な迂回手段や認証済み履歴の主張ではありません。
- G868 の verification は明示的な opt-in です。呼び出し元は
  `--require-verification` と verification unit、domain、team、head、packet digest、evidence reference
  を渡す必要があります。実行中の checkout や既定値から identity や evidence を推測しません。
- raw TRX evidence には test 名、output、machine path が含まれることがあります。lesson registry
  全体の digest は registry の識別情報を安定して示します。入力を commit または公開する前に、
  こうした開示内容を確認してください。
- brief の出力に失敗すると、不完全な出力や別プロセスが置いたファイルが残ることがあります。
  個別に確認してください。renderer は rollback や公開を保証しません。

未提供の広い作業は引き続き backlog です。共有状態の意味上の競合からの復旧 (#1864)、公開 issue
template の privacy renderer (#1865)、ruling に基づく hosted deferral (#1866)、従来仕様と ruling
の受け入れ条件の広い調整 (#1867)、公開 PR の構成と registry の作成 (#1868) が残ります。
これらは未完了の範囲を示すもので、この安定版公開の前提条件ではありません。

## インストールと公開

実際の公開後に利用する version 固定の .NET、npm、ランタイム同梱 asset の手順は
[インストールガイド](01-install.md)を参照してください。公開までは安定版 `0.32.0` を使うか、
公開中の preview を明示して選んでください。既存 install を通知なく置き換えません。

この準備が merge された後、conductor は既存の非公開 `workflow_dispatch` 経路で、正確な安定版候補を
build して確認します。対象 commit の到達性、3 種類の native binary、NuGet/npm version と checksum、
隔離した環境への install を確認します。候補の hash は公開 asset の同一性を証明しません。
公開時の GitHub Release event は asset を改めて build します。公開済み asset、source/version の同一性、
registry tag、新しい環境への install を別途確認してから、公開完了と判断します。
この準備 PR は、それらの作業が済んだとは主張しません。

安定版を実際に公開すると、npm の `latest` は `0.32.1` に移ります。既存の
`0.32.1-preview.1` と `preview` tag は別のままです。GitHub の stable latest release も、
安定版 release を実際に公開した後にのみ変わります。
