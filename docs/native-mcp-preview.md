# Node.js不要の接続EXE：調査プレビュー

`PloMcp.Connect.exe`はC#/.NET Framework 4.8で作ったWindows用MCPサーバー。Node.jsのインストール・同梱・実行を必要としない。現在の版は`0.2.0-preview.1`。設定画面・接続診断に加え、CSVディレクトリの取込、ユーザー指定syntaxによる集計・EQ計算、結果とグラフの出力に対応する。

**AIから17ツールを呼び出せる。** 計算にはGUIと同じC#の`ResearchBridge`・`ResearchRun`・EQエンジンを使う。既存のNode.js版30ツールとは別のプレビューで、syntaxの自動生成など旧版の追加13ツールは対象外。利用者が設定したliteral/per_boardのsyntaxをそのまま使用する。

| AIが行う操作 | ツール |
| --- | --- |
| 状態・設定の確認 | `get_status` |
| 登録ディレクトリを調査し、CSVをまとめて登録 | `research_scan_directory`、`research_get_directory_scan`、`research_import_directory` |
| ユーザー条件・カタログを参照／保存 | `research_list_workspace`、`research_list_spots`、`research_get_condition_set`、`research_save_condition_set`、`research_save_catalog` |
| 入力検証・計算・進捗・中止 | `research_validate`、`research_start`、`research_get_job`、`research_cancel_job` |
| 保存結果・履歴・出力取得 | `research_get_result`、`research_list_history`、`research_export`、`get_artifact` |

出力形式はCSV・JSON・SVG・Markdown。SVGは全アクションを色分けし、点にカーソルを合わせると条件名・syntax・出典などを表示する。`research_export`が返すURIはMCPの`resources/read`でも取得できる。1 MiBを超える成果物は`get_artifact`が保存先を返し、本文は返さない。

## 調査の流れ

1. AIが設定済みディレクトリを`research_scan_directory`で走査する。Ahiは`highest_rank: "A"`、Khiは`"K"`。省略時は全ボード。KhiにはAを含むボードを入れない。
2. ノード・各ボードのCSV対応・問題一覧をページ末尾まで読む。ファイル名の規約、全アクションのweightが共通スケールであること、シナリオを利用者と確認する。
3. `research_import_directory`で1ノード・最大200ボードを新しいカタログへ取り込む。GUIへのCSV手動登録は不要。カタログはボードの最高ランク指定も保持する。
4. 利用者が選んだ条件セットとカタログの現在の版で`research_validate`を実行し、返されたジョブをポーリングして`plan_id`を取得する。
5. `research_start`を一意の`request_key`とともに呼び出す。`research_get_job`で完了を確認し、メタデータ・ボード別・カテゴリ別結果を取得する。
6. `research_export`で表やグラフを生成する。

自分のアクション前レンジは対象ノードの**全アクションCSVの合計**。相手は、直前の相手ノードの**全アクションCSVの合計**であり、実際に選ばれた1アクションだけへ絞らない。OOP ROOTに相手レンジがない場合は推測で補完しない。利用者が頻度のみの調査を選んだ場合に限ってEQなしで処理する。欠損EQは0に置換しない。

不明なCSVが同じノードに混在した場合、そのファイルを無視して頻度を計算しない。重複する物理ボード、位置や履歴の不一致、走査後の変更、設定の版競合を検出して処理を止める。検証した計画にはsyntax・カタログ・CSVハッシュのスナップショットを保持し、計算開始前に再確認する。

一度に実行できる調査ジョブは1件。計画と走査の有効期限は1時間、走査は最大8件、計画・ジョブ・成果物IDはそれぞれ1接続につき最大200件。`request_key`は同じ計画の再送で計算を重複開始しない。計画・ジョブ・走査・成果物IDは接続を閉じると失効し、保存した結果・条件・カタログは残る。

ジョブの制限時間は設定の`timeoutSeconds`（初期値300秒）。中止・制限時間超過・接続終了では協調的に計算を止め、確定済みボードを履歴へ保存する。明示的中止後はジョブの終了を確認してから部分結果を読む。

## 操作

利用者向けの最初からの手順は[README.txt](native-mcp-install.txt)。GitHub Actionsの成功したWindows buildから`PLO-Syntax-Lab-NoNode`を取得し、内部のZIPも展開する。`PloMcp.Connect.exe`・`FlopCommandApp.exe`・HTML版を同じフォルダーに配置する配布構成。

1. Windowsで`dist\PloMcp.Connect.exe`を開く。
2. 「追加」でCSVの親フォルダーを選ぶ。複数指定でき、フルパスを表示する。
3. 結果と履歴の保存先を確認する。履歴の初期値はGUIと同じ`%LOCALAPPDATA%\FlopCommandApp\History`。
4. 「設定を保存」を押す。初期保存先は`%LOCALAPPDATA%\FlopCommandApp\mcp-native.json`。
5. 「接続を診断」を押す。自分自身を子プロセスとして起動し、MCP初期化・ping・ツール発見・状態取得・終了を確認する。

既存の`mcp.local.json`は「既存設定を開く」から再利用できる。相対パスは設定ファイルの位置を基準に解決する。結果・履歴フォルダーがまだ存在しない既存設定は読み込めないため、先にそのフォルダーを用意する。読込だけでは元の設定を変更しない。

保存時は既存の上限値・履歴削除設定を保持し、旧ファイルを`.bak`へ退避する。この版には削除ツールはない。編集中に元ファイルが更新されていれば上書きを拒否する。リンク・ジャンクションは許可しない。起動後に設定が変わった場合も、再起動まで新しいツール操作を拒否する。

「AI用の設定をコピー」でstdio対応クライアント用のJSONを取得できる。ChatGPTへ自動登録する機能ではない。

```text
PloMcp.Connect.exe --stdio --config "C:\PLO\mcp-native.json"
PloMcp.Connect.exe --check --config "C:\PLO\mcp-native.json"
```

引数なし、または`--setup`で設定画面を開く。`--check`は診断JSONを標準出力に返す。`--stdio`はUTF-8・改行区切りJSON-RPCのみを標準出力に書き、起動エラーは標準エラーへ返す。

## ビルドと検証

Windows PowerShell 5.1から次を実行する。Node.jsを使う既存の`build.ps1`とは独立している。

```powershell
.\build-native-mcp.ps1 -WithTests
```

通常アプリ・HTMLもビルドしたクリーンなチェックアウトでは、`.\package-native-mcp.ps1 -SourceCommit <40桁のコミットSHA>`で配布ZIPを作成する。配布スクリプト自体もNode.js不要。固定のファイル一覧だけをコピーし、利用者のCSV・設定・キーを含めない。`build-info.json`で元コミットと同梱ファイルのSHA-256を確認できる。CIはZIPを別フォルダーへ展開し、そこから実際のEXEを起動してMCP調査フローを再検証してから配布する。

Windows CIにはNode.jsがPATHにない状態での専用ビルド・テストを追加。別の検証では、開発用の公式MCP SDKクライアントからEXEを起動する。クライアントの開発テストだけがNode.jsを使用し、製品EXEの依存にはならない。

ツールのJSON SchemaとAI用指示は`mcp/generate-native-schemas.mjs`で既存Node版から生成し、`mcp/native/ToolDefinitions.json`をソースとして保存する。EXEに埋め込むため、通常のビルド時にも生成スクリプトを実行する必要はない。開発CIで`--check`を実行し、定義のずれを検出する。C#側でも引数の型・必須値・上限・未定義キーを検証する。

通信はMCP `2025-11-25`を実装し、initializeでその版を返す。未実装の版や機能を対応済みと宣言しない。参照：[初期化](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle)、[stdio](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)、[tools](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)。

## 今回確認した範囲

- Windows .NET Framework 4.8向けEXEのクロスコンパイル。
- 同じC#ソースをLinux/.NET 8で実行し、設定保存・競合・JSON・MCP・CSV取込・集計・EQ・出力・実子プロセスを検証。
- 公式SDKによる接続と、Node.jsが見つからないPATHでの調査フロー全体の実行。
- 既存Node版との10ノードのCSV対応一致、A/K/Q/all指定、相手不在、syntax保存と変更検出、全アクション頻度、出力4形式、協調的中止・接続終了時の部分保存・再起動後の履歴読込。

Windows CIにはNode.jsなしのビルド・C#検証、設定画面の起動／パス表示／保存／終了、公式SDKと配布ZIP内のEXEを使ったMCP調査の確認を組み込んでいる。実行結果は対象コミットのWindows buildを参照する。利用者のWindows PC、ブラウザ版ChatGPT、トンネルを通した実接続は別途確認が必要。

ブラウザ版ChatGPTへの接続には別途トンネルが必要。公式トンネルクライアントと本EXEを利用者環境で組み合わせる検証は未実施で、EXE化だけで社内の実行・通信制限が解消されるものではない。
